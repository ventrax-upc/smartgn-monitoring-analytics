using System.Net.Http.Headers;
using System.Net.Http.Json;
using Com.Ventrax.Monitoring.Application.Exceptions;
using Com.Ventrax.Monitoring.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace Com.Ventrax.Monitoring.Infrastructure.Clients;

// Meter Authorization Client: pregunta a IAM si la cuenta puede usar esa función sobre ese medidor.
// IAM verifica titularidad (Installation) o delegación vigente del subusuario.
// Contrato: POST /internal/v1/authorizations/check -> { "allowed": true|false }
public class IamMeterAuthorizationClient : IMeterAuthorizationClient
{
    private const string Dependency = "IAM";

    private readonly HttpClient _http;
    private readonly IntegrationOptions _integration;

    public IamMeterAuthorizationClient(HttpClient http, IOptions<IntegrationOptions> integration)
    {
        _http = http;
        _integration = integration.Value;
    }

    public async Task<bool> IsAllowedAsync(Guid accountId, Guid meterId, string function, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/authorizations/check")
        {
            // requiresPro=false: el beneficio Pro lo comprueba Monitoring con Billing por gRPC
            Content = JsonContent.Create(new CheckRequest(accountId, meterId, function, RequiresPro: false)),
        };
        if (!string.IsNullOrEmpty(_integration.InternalServiceToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _integration.InternalServiceToken);
        }

        try
        {
            using var response = await _http.SendAsync(request, ct);

            // Cuenta o medidor inexistente / sin permiso -> denegado (403 en Monitoring)
            if (response.StatusCode is System.Net.HttpStatusCode.BadRequest
                or System.Net.HttpStatusCode.Forbidden
                or System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }

            // 401 (token interno rechazado), 5xx, etc. -> IAM no confirmó: 503, nunca "permitido"
            if (!response.IsSuccessStatusCode)
            {
                throw new DependencyUnavailableException(Dependency);
            }

            var body = await response.Content.ReadFromJsonAsync<CheckResponse>(ct);
            return body?.Allowed == true;
        }
        catch (HttpRequestException ex)
        {
            throw new DependencyUnavailableException(Dependency, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Timeout del HttpClient
            throw new DependencyUnavailableException(Dependency, ex);
        }
    }

    private sealed record CheckRequest(Guid AccountId, Guid MeterId, string Function, bool RequiresPro);

    private sealed record CheckResponse(bool Allowed);
}
