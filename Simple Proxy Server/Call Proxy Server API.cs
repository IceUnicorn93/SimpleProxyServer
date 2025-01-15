using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Simple_Proxy_Server
{
    internal class Call_Proxy_Server_API
    {
        private static readonly string ApiBaseUrl = "http://localhost:8889";
        private const string ApiKey = "my-secure-api-key";
        private const string Username = "admin";
        private const string Password = "password123";

        static async Task Main(string[] args)
        {
            // Anzeigen der aktuellen Blacklist
            Console.WriteLine("1. Aktuelle Blacklist anzeigen:");
            await GetBlacklist();

            // Eintrag zur Blacklist hinzufügen
            Console.WriteLine("\n2. 'example.com' zur Blacklist hinzufügen:");
            await AddToBlacklist("example.com");

            // Eintrag aus der Blacklist entfernen
            Console.WriteLine("\n3. 'example.com' aus der Blacklist entfernen:");
            await RemoveFromBlacklist("example.com");

            // Aktuelle Blacklist anzeigen
            Console.WriteLine("\n4. Aktuelle Blacklist erneut anzeigen:");
            await GetBlacklist();
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", ApiKey);
            client.DefaultRequestHeaders.Add("X-Username", Username);
            client.DefaultRequestHeaders.Add("X-Password", Password);
            return client;
        }

        // GET /blacklist
        private static async Task GetBlacklist()
        {
            using (var client = CreateHttpClient())
            {
                try
                {
                    var response = await client.GetAsync($"{ApiBaseUrl}/blacklist");
                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsStringAsync();
                    Console.WriteLine("Aktuelle Blacklist:");
                    Console.WriteLine(content);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Fehler beim Abrufen der Blacklist: {ex.Message}");
                } 
            }
        }

        // POST /blacklist/add
        private static async Task AddToBlacklist(string fragment)
        {
            using (var client = CreateHttpClient())
            {
                try
                {
                    var content = new StringContent(fragment, Encoding.UTF8, "text/plain");
                    var response = await client.PostAsync($"{ApiBaseUrl}/blacklist/add", content);
                    response.EnsureSuccessStatusCode();

                    var responseContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Antwort vom Server: {responseContent}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Fehler beim Hinzufügen zur Blacklist: {ex.Message}");
                } 
            }
        }

        // POST /blacklist/remove
        private static async Task RemoveFromBlacklist(string fragment)
        {
            using (var client = CreateHttpClient())
            {
                try
                {
                    var content = new StringContent(fragment, Encoding.UTF8, "text/plain");
                    var response = await client.PostAsync($"{ApiBaseUrl}/blacklist/remove", content);
                    response.EnsureSuccessStatusCode();

                    var responseContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Antwort vom Server: {responseContent}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Fehler beim Entfernen aus der Blacklist: {ex.Message}");
                } 
            }
        }
    }
}
