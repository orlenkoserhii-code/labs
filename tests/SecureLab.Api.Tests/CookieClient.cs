using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SecureLab.Api.Tests;

public static class CookieClient
{
    public static async Task<HttpClient> LoginAsync(this WebApplicationFactory<Program> factory, string userName, string password, bool withAntiforgery = true)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { userName, password });
        if (!response.IsSuccessStatusCode)
        {
            client.Dispose();
            throw new InvalidOperationException("Study login failed (credentials omitted).");
        }
        if (withAntiforgery)
        {
            try
            {
                await client.UseAntiforgeryTokenAsync();
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
        return client;
    }

    public static async Task UseAntiforgeryTokenAsync(this HttpClient client)
    {
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var response = await client.GetAsync("/api/auth/csrf-token");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body.GetProperty("token").GetString());
    }
}
