using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

class SimpleProxyServer
{

    class Program
    {
        private static readonly string BlacklistFilePath = @".\blacklist.txt";
        private static HashSet<string> Blacklist = new HashSet<string>();

        static async Task Main(string[] args)
        {
            const int port = 8888;
            const int apiPort = 8889;
            var listener = new TcpListener(IPAddress.Any, port);

            Console.WriteLine($"Starting proxy server on port {port}...");
            EnsureBlacklistFileExists();
            LoadBlacklist();
            WatchBlacklistFile();

            listener.Start();
            _ = StartApiServer(apiPort);

            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                _ = HandleClient(client);
            }
        }

        // ---------- Blacklist ----------

        private static void EnsureBlacklistFileExists()
        {
            if (!File.Exists(BlacklistFilePath))
            {
                Console.WriteLine($"Blacklist file not found. Creating default file: {BlacklistFilePath}");
                File.WriteAllText(BlacklistFilePath, "# Add one URL fragment per line to block.\n# Example:\nyoutube\nads.google.com\nxxx\n");
                Console.WriteLine("Default blacklist file created.");
            }
        }

        private static void LoadBlacklist()
        {
            try
            {
                if (File.Exists(BlacklistFilePath))
                {
                    var lines = File.ReadAllLines(BlacklistFilePath);
                    Blacklist = new HashSet<string>(lines, StringComparer.OrdinalIgnoreCase);
                    Console.WriteLine("Blacklist loaded:");
                    foreach (var item in Blacklist)
                    {
                        if (!item.StartsWith("#") && !string.IsNullOrWhiteSpace(item))
                        {
                            Console.WriteLine($"  - {item}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading blacklist: {ex.Message}");
            }
        }

        private static void WatchBlacklistFile()
        {
            var directory = Path.GetDirectoryName(BlacklistFilePath);
            var fileName = Path.GetFileName(BlacklistFilePath);

            if (directory == null) return;

            var watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
            };

            watcher.Changed += (sender, e) => { LoadBlacklist(); };
            watcher.Created += (sender, e) => { LoadBlacklist(); };
            watcher.Renamed += (sender, e) => { LoadBlacklist(); };

            watcher.EnableRaisingEvents = true;
            Console.WriteLine($"Watching blacklist file: {BlacklistFilePath}");
        }

        // ---------- Proxyserver ----------

        private static async Task HandleClient(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                var reader = new StreamReader(stream, Encoding.ASCII);
                var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };

                try
                {
                    var requestLine = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(requestLine)) return;

                    Console.WriteLine($"Request: {requestLine}");
                    var parts = requestLine.Split(' ');

                    if (parts.Length < 3) return;

                    var method = parts[0];
                    var uri = parts[1];
                    var version = parts[2];

                    if (IsBlacklisted(uri))
                    {
                        Console.WriteLine($"Blocked URL: {uri}");
                        await SendBlockedResponse(writer);
                        return;
                    }

                    if (method == "CONNECT")
                    {
                        await HandleHttpsTunneling(client, writer, uri);
                    }
                    else
                    {
                        await HandleHttpRequest(client, reader, writer, method, uri, version);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        private static bool IsBlacklisted(string url)
        {
            foreach (var fragment in Blacklist)
            {
                if (!fragment.StartsWith("#") && !string.IsNullOrWhiteSpace(fragment) && url.Contains(fragment)) // , StringComparison.OrdinalIgnoreCase
                {
                    return true;
                }
            }
            return false;
        }

        private static async Task HandleHttpsTunneling(TcpClient client, StreamWriter writer, string uri)
        {
            var parts = uri.Split(':');
            var host = parts[0];
            var port = parts.Length > 1 ? int.Parse(parts[1]) : 443;

            try
            {
                using (var targetClient = new TcpClient())
                {
                    await targetClient.ConnectAsync(host, port);

                    await writer.WriteLineAsync("HTTP/1.1 200 Connection Established\r\n\r\n");

                    var clientStream = client.GetStream();
                    var targetStream = targetClient.GetStream();

                    var clientToTarget = RelayStream(clientStream, targetStream);
                    var targetToClient = RelayStream(targetStream, clientStream);

                    await Task.WhenAny(clientToTarget, targetToClient);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HTTPS Tunnel error: {ex.Message}");
            }
        }

        private static async Task HandleHttpRequest(TcpClient client, StreamReader reader, StreamWriter writer, string method, string uri, string version)
        {
            var uriObj = new Uri(uri);
            var host = uriObj.Host;
            var port = uriObj.Port;

            try
            {
                using (var targetClient = new TcpClient())
                {
                    await targetClient.ConnectAsync(host, port);

                    var targetStream = targetClient.GetStream();
                    var targetWriter = new StreamWriter(targetStream, Encoding.ASCII) { AutoFlush = true };
                    var targetReader = new StreamReader(targetStream, Encoding.ASCII);

                    // Forward the request to the target server
                    await targetWriter.WriteLineAsync($"{method} {uriObj.PathAndQuery} {version}");
                    string line;
                    while (!string.IsNullOrWhiteSpace(line = await reader.ReadLineAsync()))
                    {
                        await targetWriter.WriteLineAsync(line);
                    }
                    await targetWriter.WriteLineAsync();

                    // Relay the response back to the client
                    var clientStream = client.GetStream();
                    await targetReader.BaseStream.CopyToAsync(clientStream);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HTTP Request error: {ex.Message}");
            }
        }

        private static async Task RelayStream(NetworkStream fromStream, NetworkStream toStream)
        {
            try
            {
                await fromStream.CopyToAsync(toStream);
            }
            catch
            {
                // Ignored
            }
        }
        
        // ---------- Blocked Response (Needs to be testet) ----------
        
        private static async Task SendBlockedResponse(StreamWriter writer)
        {
            var html = @"
                <html>
                <head><title>Blocked</title></head>
                <body>
                    <h1>Access Denied</h1>
                    <p>The requested URL is blocked by the proxy server.</p>
                </body>
                </html>";

            await writer.WriteLineAsync("HTTP/1.1 403 Forbidden");
            await writer.WriteLineAsync("Content-Type: text/html");
            await writer.WriteLineAsync($"Content-Length: {html.Length}");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync(html);
        }

        // ---------- API Server ----------

        private static readonly string ApiKey = "my-secure-api-key";
        private static readonly Dictionary<string, string> ValidUsers = new Dictionary<string, string>()
        {
            { "admin", "password123" }, // Beispielnutzer
            { "user1", "mypassword" }
        };

        private static bool IsAuthorized(HttpListenerRequest request)
        {
            // Prüft den API-Key
            var apiKey = request.Headers["Authorization"];
            if (apiKey != ApiKey)
            {
                return false;
            }

            // Prüft Benutzername und Passwort
            var username = request.Headers["X-Username"];
            var password = request.Headers["X-Password"];
            return !string.IsNullOrWhiteSpace(username) &&
                   !string.IsNullOrWhiteSpace(password) &&
                   ValidUsers.TryGetValue(username, out var validPassword) &&
                   validPassword == password;
        }

        private static async Task StartApiServer(int port)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://*:{port}/");
            listener.Start();

            Console.WriteLine($"API server running on port {port}...");

            while (true)
            {
                var context = await listener.GetContextAsync();
                _ = HandleApiRequest(context);
            }
        }

        private static async Task HandleApiRequest(HttpListenerContext context)
        {
            var response = context.Response;
            var request = context.Request;

            if (!IsAuthorized(request))
            {
                await WriteResponse(response, "Unauthorized", 401);
                return;
            }

            try
            {
                if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/blacklist")
                {
                    var blacklistItems = string.Join("\n", Blacklist);
                    var responseContent = $"Current Blacklist:\n{blacklistItems}";
                    await WriteResponse(response, responseContent, 200);
                }
                else if (request.HttpMethod == "POST" && request.Url.AbsolutePath == "/blacklist/add")
                {
                    var fragment = await ReadRequestBody(request);
                    if (!string.IsNullOrWhiteSpace(fragment) && Blacklist.Add(fragment))
                    {
                        SaveBlacklist();
                        await WriteResponse(response, $"Added '{fragment}' to blacklist.", 200);
                    }
                    else
                    {
                        await WriteResponse(response, $"'{fragment}' is already in the blacklist or invalid.", 400);
                    }
                }
                else if (request.HttpMethod == "POST" && request.Url.AbsolutePath == "/blacklist/remove")
                {
                    var fragment = await ReadRequestBody(request);
                    if (!string.IsNullOrWhiteSpace(fragment) && Blacklist.Remove(fragment))
                    {
                        SaveBlacklist();
                        await WriteResponse(response, $"Removed '{fragment}' from blacklist.", 200);
                    }
                    else
                    {
                        await WriteResponse(response, $"'{fragment}' was not found in the blacklist or invalid.", 400);
                    }
                }
                else
                {
                    await WriteResponse(response, "Invalid endpoint.", 404);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"API Error: {ex.Message}");
                await WriteResponse(response, "Internal server error.", 500);
            }
        }

        private static async Task<string> ReadRequestBody(HttpListenerRequest request)
        {
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                return await reader.ReadToEndAsync();
            }
        }

        private static async Task WriteResponse(HttpListenerResponse response, string content, int statusCode)
        {
            response.StatusCode = statusCode;
            response.ContentType = "text/plain";
            var buffer = Encoding.UTF8.GetBytes(content);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        private static void SaveBlacklist()
        {
            try
            {
                File.WriteAllLines(BlacklistFilePath, Blacklist);
                Console.WriteLine("Blacklist saved to file.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving blacklist: {ex.Message}");
            }
        }
    }
}