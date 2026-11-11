using Com.Ventrax.Monitoring.Application.Exceptions;
using Com.Ventrax.Monitoring.Application.Interfaces;
using Com.Ventrax.Monitoring.Infrastructure.Clients.Billing;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Com.Ventrax.Monitoring.Infrastructure.Clients;

// Billing Entitlement gRPC Client: ¿la cuenta tiene Pro vigente?
// Si Billing no responde se lanza DependencyUnavailableException (503): nunca se asume Free ni Pro.
public class BillingGrpcClient : IBillingGrpcClient
{
    private const string Dependency = "Payments & Billing";

    private readonly SubscriptionEntitlementService.SubscriptionEntitlementServiceClient _client;
    private readonly IntegrationOptions _integration;

    public BillingGrpcClient(
        SubscriptionEntitlementService.SubscriptionEntitlementServiceClient client,
        IOptions<IntegrationOptions> integration)
    {
        _client = client;
        _integration = integration.Value;
    }

    public async Task<bool> HasProEntitlementAsync(Guid accountId, CancellationToken ct = default)
    {
        var request = new ProEntitlementRequest { AccountId = accountId.ToString() };

        var headers = new Metadata();
        if (!string.IsNullOrEmpty(_integration.InternalServiceToken))
        {
            headers.Add("authorization", $"Bearer {_integration.InternalServiceToken}");
        }

        try
        {
            var response = await _client.GetProEntitlementAsync(
                request,
                new CallOptions(headers, DateTime.UtcNow.AddSeconds(_integration.RequestTimeoutSeconds), ct));
            return response.ProActive;
        }
        catch (RpcException ex) when (!ct.IsCancellationRequested)
        {
            throw new DependencyUnavailableException(Dependency, ex);
        }
    }
}
