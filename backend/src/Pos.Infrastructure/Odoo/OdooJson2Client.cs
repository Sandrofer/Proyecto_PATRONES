using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pos.Application;

namespace Pos.Infrastructure.Odoo;

public sealed class OdooOptions
{
    public const string Section = "Odoo";

    public string BaseUrl { get; set; } = "http://localhost:8069";
    public string Database { get; set; } = "pos";
    // Secret: provided through user-secrets or environment variables, never in Git.
    public string ApiKey { get; set; } = "";
    public int PosConfigId { get; set; }
    public int TimeoutSeconds { get; set; } = 15;
}

// Thin client for Odoo's external JSON-2 API:
//   POST {BaseUrl}/json/2/{model}/{method}  with the named arguments as JSON.
// It translates transport problems into Application exceptions so the use
// cases never see HttpClient types.
public sealed class OdooJson2Client(HttpClient http, IOptions<OdooOptions> options, ILogger<OdooJson2Client> logger)
{
    readonly OdooOptions _options = options.Value;

    public async Task<JsonNode?> CallAsync(string model, string method, object arguments, CancellationToken ct)
    {
        // Serialized up front (not JsonContent): Odoo's HTTP server ignores
        // chunked request bodies, so the request must carry Content-Length.
        string json = JsonSerializer.Serialize(arguments, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"json/2/{model}/{method}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", _options.ApiKey);
        request.Headers.Add("X-Odoo-Database", _options.Database);
        logger.LogDebug("Odoo {Model}.{Method} <- {Body}", model, method, json);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Odoo unreachable calling {Model}.{Method}", model, method);
            throw new ExternalServiceUnavailableException("No se pudo comunicar con Odoo.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Odoo timeout calling {Model}.{Method}", model, method);
            throw new ExternalServiceUnavailableException("Odoo no respondió a tiempo.", ex);
        }

        using (response)
        {
            string body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
                return string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body);

            logger.LogWarning("Odoo {Status} calling {Model}.{Method}: {Body}",
                (int)response.StatusCode, model, method, Truncate(body));

            // Gateway errors mean the request may not have been processed:
            // the outcome is unknown, not a rejection.
            if (response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout)
                throw new ExternalServiceUnavailableException("Odoo no está disponible en este momento.");

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new ExternalServiceRejectedException(
                    "Odoo rechazó las credenciales de integración. Revise la API key y los permisos del usuario.");

            throw new ExternalServiceRejectedException($"Odoo rechazó la operación: {ExtractMessage(body)}");
        }
    }

    // Checks a user's login and password with Odoo (JSON-RPC session
    // authentication; JSON-2 only accepts API keys). Returns the Odoo user id,
    // or null when the credentials are wrong. No session is kept: the
    // HttpClient is configured without cookies.
    public async Task<int?> AuthenticateAsync(string login, string password, CancellationToken ct)
    {
        string json = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            @params = new { db = _options.Database, login, password }
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "web/session/authenticate")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ExternalServiceUnavailableException("No se pudo comunicar con Odoo.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ExternalServiceUnavailableException("Odoo no respondió a tiempo.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new ExternalServiceUnavailableException("Odoo no está disponible en este momento.");

            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
            var error = body?["error"];
            if (error is not null)
            {
                if (error["data"]?["name"]?.GetValue<string>() == "odoo.exceptions.AccessDenied")
                    return null;
                logger.LogWarning("Odoo authentication error: {Error}", Truncate(error.ToJsonString()));
                throw new ExternalServiceRejectedException("Odoo no pudo validar el usuario.");
            }

            return body?["result"]?["uid"] is JsonValue uid && uid.TryGetValue<int>(out int id) ? id : null;
        }
    }

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null
    };

    static string ExtractMessage(string body)
    {
        try
        {
            return JsonNode.Parse(body)?["message"]?.GetValue<string>() ?? Truncate(body);
        }
        catch (JsonException)
        {
            return Truncate(body);
        }
    }

    static string Truncate(string text) => text.Length <= 400 ? text : text[..400] + "…";
}
