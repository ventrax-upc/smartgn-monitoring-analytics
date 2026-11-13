using System.Net;
using System.Security.Claims;
using System.Text;
using Com.Ventrax.Monitoring.Application.Exceptions;
using Com.Ventrax.Monitoring.Infrastructure.Clients;
using Com.Ventrax.Monitoring.Infrastructure.Clients.Telemetry;
using Com.Ventrax.Monitoring.Infrastructure.Security;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Com.Ventrax.Monitoring.Tests;

public class TelemetryGrpcClientTests
{
    private static readonly Guid Meter = Guid.NewGuid();
    private static readonly DateTimeOffset From = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = From.AddDays(2);

    // Servidor falso: devuelve las lecturas del rango, más nuevas primero, "limit" por dispositivo (como Telemetry)
    private sealed class FakeTelemetryServer(List<Reading> stored, bool fail = false)
        : TelemetryQueryService.TelemetryQueryServiceClient
    {
        public List<ReadingsBatchRequest> BatchRequests { get; } = [];
        public Metadata? LastHeaders { get; private set; }

        public override AsyncUnaryCall<ReadingsBatchResponse> GetReadingsBatchAsync(ReadingsBatchRequest request, CallOptions options)
        {
            BatchRequests.Add(request);
            LastHeaders = options.Headers;
            if (fail)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, "down"));
            }

            var response = new ReadingsBatchResponse();
            response.Readings.AddRange(stored
                .Where(r => request.SupplyPointIds.Contains(r.SupplyPointId)
                            && r.MeasuredAtUnixMs >= request.FromUnixMs
                            && r.MeasuredAtUnixMs < request.ToUnixMs)
                .GroupBy(r => r.DeviceId)
                .SelectMany(g => g.OrderByDescending(r => r.MeasuredAtUnixMs).Take(request.Limit)));
            return Call(response);
        }

        public override AsyncUnaryCall<LatestReadingsResponse> GetLatestReadingsAsync(LatestReadingsRequest request, CallOptions options)
        {
            var response = new LatestReadingsResponse();
            response.Readings.AddRange(stored
                .Where(r => request.SupplyPointIds.Contains(r.SupplyPointId))
                .GroupBy(r => r.DeviceId)
                .Select(g => g.OrderByDescending(r => r.MeasuredAtUnixMs).First()));
            return Call(response);
        }

        private static AsyncUnaryCall<T> Call<T>(T response) => new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
    }

    private static Reading Reading(Guid meter, DateTimeOffset at, double total, string freshness = "current")
    {
        var reading = new Reading
        {
            DeviceId = meter.ToString(),
            SupplyPointId = meter.ToString(),
            EventId = Guid.NewGuid().ToString(),
            MeasuredAtUnixMs = at.ToUnixTimeMilliseconds(),
            Freshness = freshness,
        };
        reading.Metrics["totalVolumeM3"] = total;
        reading.Metrics["pressureKpa"] = 2.3;
        reading.Metrics["gasDetected"] = 1;
        reading.Metrics["valveOpen"] = 0;
        return reading;
    }

    private static TelemetryGrpcClient Client(FakeTelemetryServer server, int pageSize = 1000, string token = "") => new(
        server,
        Options.Create(new IntegrationOptions { InternalServiceToken = token }),
        Options.Create(new TelemetryMetricsOptions { PageSize = pageSize, MaxPages = 20 }),
        NullLogger<TelemetryGrpcClient>.Instance);

    [Fact]
    public async Task Batch_MapsMetricsAndUsesUnixMilliseconds()
    {
        var server = new FakeTelemetryServer([Reading(Meter, From.AddHours(1), 10.5, "delayed")]);

        var readings = await Client(server, token: "service-token").GetReadingsBatchAsync(Guid.NewGuid(), [Meter], From, To);

        var reading = Assert.Single(readings);
        Assert.Equal(10.5m, reading.TotalVolumeM3);
        Assert.Equal(2.3, reading.PressureKpa);
        Assert.True(reading.GasDetected);
        Assert.False(reading.ValveOpen);
        Assert.True(reading.Delayed);
        Assert.Equal(From.ToUnixTimeMilliseconds(), server.BatchRequests[0].FromUnixMs);
        Assert.Equal("Bearer service-token", server.LastHeaders!.GetValue("authorization"));
    }

    [Fact]
    public async Task Batch_PagesBackwards_WhenAMeterFillsThePage()
    {
        var stored = Enumerable.Range(0, 7).Select(i => Reading(Meter, From.AddHours(i), 100 + i)).ToList();
        var server = new FakeTelemetryServer(stored);

        var readings = await Client(server, pageSize: 3).GetReadingsBatchAsync(Guid.NewGuid(), [Meter], From, To);

        Assert.Equal(7, readings.Count);
        Assert.Equal(3, server.BatchRequests.Count); // 3 + 3 + 1
    }

    [Fact]
    public async Task Latest_ReturnsOneReadingPerMeter()
    {
        var server = new FakeTelemetryServer([Reading(Meter, From, 1), Reading(Meter, From.AddHours(1), 2)]);

        var latest = await Client(server).GetLatestReadingsAsync(Guid.NewGuid(), [Meter]);

        Assert.Equal(2m, Assert.Single(latest).TotalVolumeM3);
    }

    [Fact]
    public async Task GrpcFailure_BecomesDependencyUnavailable()
    {
        var server = new FakeTelemetryServer([], fail: true);

        await Assert.ThrowsAsync<DependencyUnavailableException>(() =>
            Client(server).GetReadingsBatchAsync(Guid.NewGuid(), [Meter], From, To));
    }
}

public class IamMeterAuthorizationClientTests
{
    private sealed class StubHandler(HttpStatusCode status, string body = "") : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static IamMeterAuthorizationClient Client(StubHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("http://iam") },
        Options.Create(new IntegrationOptions { InternalServiceToken = "internal" }));

    [Fact]
    public async Task Allowed_IsReadFromIamResponse()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"allowed":true}""");

        Assert.True(await Client(handler).IsAllowedAsync(Guid.NewGuid(), Guid.NewGuid(), "VIEW_CONSUMPTION"));
        Assert.Equal("/internal/v1/authorizations/check", handler.Request!.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer internal", handler.Request.Headers.Authorization!.ToString());
        Assert.Contains("\"function\":\"VIEW_CONSUMPTION\"", handler.RequestBody);
        Assert.Contains("\"requiresPro\":false", handler.RequestBody);
    }

    [Fact]
    public async Task NotAllowed_ForbiddenOrNotFound_MeanDenied()
    {
        Assert.False(await Client(new StubHandler(HttpStatusCode.OK, """{"allowed":false}"""))
            .IsAllowedAsync(Guid.NewGuid(), Guid.NewGuid(), "VIEW_HISTORY"));
        Assert.False(await Client(new StubHandler(HttpStatusCode.NotFound))
            .IsAllowedAsync(Guid.NewGuid(), Guid.NewGuid(), "VIEW_HISTORY"));
    }

    [Fact]
    public async Task ServerErrorOrRejectedServiceToken_IsUnavailable_NotAllowed()
    {
        await Assert.ThrowsAsync<DependencyUnavailableException>(() =>
            Client(new StubHandler(HttpStatusCode.ServiceUnavailable)).IsAllowedAsync(Guid.NewGuid(), Guid.NewGuid(), "USE_REPORTS"));
        await Assert.ThrowsAsync<DependencyUnavailableException>(() =>
            Client(new StubHandler(HttpStatusCode.Unauthorized)).IsAllowedAsync(Guid.NewGuid(), Guid.NewGuid(), "USE_REPORTS"));
    }
}

public class JwtAuthenticationTests
{
    private static ClaimsPrincipal Principal(string? sub, string? accountId)
    {
        var claims = new List<Claim>();
        if (sub is not null) claims.Add(new Claim("sub", sub));
        if (accountId is not null) claims.Add(new Claim("accountId", accountId));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public void AccountId_ComesFromToken_WhenSubjectMatches()
    {
        var id = Guid.NewGuid().ToString();

        Assert.Equal(Guid.Parse(id), JwtAuthentication.TryGetAccountId(Principal(id, id)));
    }

    [Fact]
    public void MismatchedOrMissingClaims_AreRejected()
    {
        Assert.Null(JwtAuthentication.TryGetAccountId(Principal(Guid.NewGuid().ToString(), Guid.NewGuid().ToString())));
        Assert.Null(JwtAuthentication.TryGetAccountId(Principal(null, Guid.NewGuid().ToString())));
        Assert.Null(JwtAuthentication.TryGetAccountId(Principal("not-a-guid", "not-a-guid")));
    }
}
