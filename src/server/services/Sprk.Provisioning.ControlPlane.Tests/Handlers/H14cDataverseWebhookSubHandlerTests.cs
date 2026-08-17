// -----------------------------------------------------------------------------
// H14cDataverseWebhookSubHandlerTests.cs
//
// Unit tests over H14cDataverseWebhookSubHandler (task 073 — wave C4 Batch 3F).
//
// ADR-038 CATEGORY: Path #1 — pure C# unit test. NO live Dataverse Web API
// calls. Fakes replace IKvSecretReader + IServiceEndpointWebhookRegistrar.
//
// COVERAGE:
//   AC-1  Happy path (Created) — Success with deterministic key.
//   AC-2  Happy path (Updated — pre-existing serviceendpoint) — Success.
//   AC-3  Signing key NotFound — Resumable, MissingSigningKey, registrar never called.
//   AC-4  Registrar Failure — RetryableWithCleanup, RegistrationFailed.
//   AC-5  Handler-id mismatch — throws.
//   AC-6  Idempotency key format determinism.
//   AC-7  Signing key is the SAME canonical secret name H14b consumes (shared secret).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H14cDataverseWebhookSubHandlerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h14c-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string DataverseEnvUrl = "https://spaarke-acme.crm.dynamics.com";
    private const string KeyVaultName = "sprk-acme-prod-kv";
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";
    private const string WebhookUrl = "https://sprk-acme-prod.azurewebsites.net/api/webhooks/dataverse/communication";
    private const string SigningKey = "super-secret-hmac-key";

    // ---------- AC-1 happy path created ----------

    [Fact]
    public async Task AC1_HappyPath_Created_Succeeds()
    {
        var reader = FakeReader.Success(SigningKey);
        var registrar = FakeRegistrar.Created("se-1");
        var handler = BuildHandler(reader, registrar);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(H14cDataverseWebhookSubHandler.BuildIdempotencyKey(CustomerId, DataverseEnvUrl, WebhookUrl));
        registrar.CallCount.Should().Be(1);
        registrar.LastRequest!.SigningKey.Should().Be(SigningKey);
        registrar.LastRequest.Name.Should().Be(H14cDataverseWebhookSubHandler.ServiceEndpointName);
    }

    // ---------- AC-2 happy path updated ----------

    [Fact]
    public async Task AC2_HappyPath_Updated_Succeeds()
    {
        var reader = FakeReader.Success(SigningKey);
        var registrar = FakeRegistrar.Updated("se-1");
        var handler = BuildHandler(reader, registrar);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
    }

    // ---------- AC-3 signing key not found ----------

    [Fact]
    public async Task AC3_SigningKeyNotFound_FailsResumable_RegistrarNeverCalled()
    {
        var reader = FakeReader.NotFound();
        var registrar = FakeRegistrar.Created("se-1");
        var handler = BuildHandler(reader, registrar);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H14cRejections.MissingSigningKey);
        registrar.CallCount.Should().Be(0);
    }

    // ---------- AC-4 registrar failure ----------

    [Fact]
    public async Task AC4_RegistrarFailure_FailsRetryableWithCleanup()
    {
        var reader = FakeReader.Success(SigningKey);
        var registrar = FakeRegistrar.Failure("Dataverse 500");
        var handler = BuildHandler(reader, registrar);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.RetryableWithCleanup);
        failure.RejectionCode.Should().Be(H14cRejections.RegistrationFailed);
        failure.Diagnostic.Should().Contain("Dataverse 500");
    }

    // ---------- AC-5 handler-id mismatch ----------

    [Fact]
    public async Task AC5_HandlerIdMismatch_Throws()
    {
        var handler = BuildHandler(FakeReader.Success(SigningKey), FakeRegistrar.Created("se-1"));
        var wrongEnvelope = new HandlerEnvelope
        {
            HandlerId = "H0", RunId = RunId, CustomerId = CustomerId, ParametersJson = "{}", EnqueuedAt = DateTimeOffset.UtcNow,
        };

        var act = async () => await handler.HandleAsync(wrongEnvelope, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*mismatched HandlerId*");
    }

    // ---------- AC-6 idempotency key determinism ----------

    [Fact]
    public void AC6_IdempotencyKey_IsDeterministic()
    {
        var k1 = H14cDataverseWebhookSubHandler.BuildIdempotencyKey(CustomerId, DataverseEnvUrl, WebhookUrl);
        var k2 = H14cDataverseWebhookSubHandler.BuildIdempotencyKey(CustomerId, DataverseEnvUrl, WebhookUrl);
        k1.Should().Be(k2);
        k1.Should().StartWith($"h14-{CustomerId}-dataverse-");

        var k3 = H14cDataverseWebhookSubHandler.BuildIdempotencyKey(CustomerId, DataverseEnvUrl, "https://different-url/");
        k3.Should().NotBe(k1);
    }

    // ---------- AC-7 shared signing-key secret name with H14b ----------

    [Fact]
    public void AC7_SigningKeySecretName_MatchesH14b()
    {
        H14cDataverseWebhookSubHandler.SigningKeySecretName.Should().Be(H14bGraphWebhookSubHandler.SigningKeySecretName);
        H14cDataverseWebhookSubHandler.SigningKeySecretName.Should().Be("Communication-Webhook-SigningKey");
    }

    // ---------- helpers ----------

    private static H14cDataverseWebhookSubHandler BuildHandler(FakeReader reader, FakeRegistrar registrar)
        => new(reader, registrar, Options.Create(new IntegrationWiringOptions()), NullLogger<H14cDataverseWebhookSubHandler>.Instance);

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H14cDataverseWebhookSubHandler.HandlerIdentifier,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = H14cDataverseWebhookSubHandler.BuildParametersJson(
            TenantId, DataverseEnvUrl, KeyVaultName, SubscriptionId, WebhookUrl),
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private sealed class FakeReader : IKvSecretReader
    {
        private readonly KvSecretReadResult _result;
        private FakeReader(KvSecretReadResult result) => _result = result;
        public static FakeReader Success(string value) => new(new KvSecretReadResult.Success(value));
        public static FakeReader NotFound() => new(new KvSecretReadResult.NotFound());

        public Task<KvSecretReadResult> ReadSecretAsync(string vaultName, string subscriptionId, string secretName, CancellationToken ct)
            => Task.FromResult(_result);
    }

    private sealed class FakeRegistrar : IServiceEndpointWebhookRegistrar
    {
        private readonly ServiceEndpointWebhookOutcome _outcome;
        public int CallCount { get; private set; }
        public ServiceEndpointWebhookRequest? LastRequest { get; private set; }

        private FakeRegistrar(ServiceEndpointWebhookOutcome outcome) => _outcome = outcome;

        public static FakeRegistrar Created(string id) => new(new ServiceEndpointWebhookOutcome.Created(id));
        public static FakeRegistrar Updated(string id) => new(new ServiceEndpointWebhookOutcome.Updated(id));
        public static FakeRegistrar Failure(string diagnostic) => new(new ServiceEndpointWebhookOutcome.Failure(diagnostic));

        public Task<ServiceEndpointWebhookOutcome> RegisterAsync(ServiceEndpointWebhookRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_outcome);
        }
    }
}
