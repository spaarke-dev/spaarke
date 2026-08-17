// -----------------------------------------------------------------------------
// H05ConsentCaptureHandlerTests.cs
//
// L2 CONTROL-PLANE H0.5 consent-capture handler unit tests (task 042).
//
// SCOPE:
//   Pure unit tests with an in-memory IDataverseEnvironmentRegistryClient
//   stub + in-memory IHandlerEnqueuer stub. No live SB, no live Cosmos, no
//   process shell-outs. ADR-038 path #1 (pure C# unit test — no external
//   processes).
//
// COVERAGE (POML acceptance criteria mapping):
//   4. tid propagates to downstream H0 payload; no default-tenant fallback
//   5. Re-consent no-op: {Ready, Running, WaitingOnGate} → no H0 enqueue
//   6. Re-consent restart: {Failed, Cancelled} → H0 enqueue
//   7. Idempotency: deterministic idempotency key across identical inputs
//
// PLUS defensive negative branches (missing payload, malformed JSON,
// missing customerId / tenantId, envelope/payload customerId mismatch,
// registry throws).
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.ConsentCapture;
using Sprk.Provisioning.ControlPlane.Registry;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H05ConsentCaptureHandlerTests
{
    private const string CustomerId = "acme-corp";
    private const string TenantId = "11111111-1111-1111-1111-111111111111";
    private const string RunId = "01HZZ-run-0001";

    [Fact]
    public void HandlerId_MatchesDesignDocConstant()
    {
        var handler = NewHandler(new StubRegistry(), new StubEnqueuer());
        handler.HandlerId.Should().Be("H0.5", "the value MUST match design.md §4.1 handler-catalog verbatim.");
    }

    [Fact]
    public async Task HandleAsync_MissingParameters_ReturnsFailure_ResumableMissingParameters()
    {
        var handler = NewHandler(new StubRegistry(), new StubEnqueuer());
        var envelope = NewEnvelope(parametersJson: "");

        var result = await handler.HandleAsync(envelope, CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(ConsentCaptureRejectionCodes.MissingParameters);
    }

    [Fact]
    public async Task HandleAsync_MalformedJson_ReturnsFailure_MalformedPayload()
    {
        var handler = NewHandler(new StubRegistry(), new StubEnqueuer());
        var envelope = NewEnvelope(parametersJson: "{ not valid json");

        var result = await handler.HandleAsync(envelope, CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(ConsentCaptureRejectionCodes.MalformedPayload);
    }

    [Fact]
    public async Task HandleAsync_MissingCustomerId_ReturnsFailure_MissingCustomerId()
    {
        // Payload with empty customerId; envelope-side customerId is present so the
        // envelope validation passes and we hit the payload validator.
        var payload = new ConsentCapturePayload { CustomerId = "", TenantId = TenantId };
        var envelope = NewEnvelope(parametersJson: SerializePayload(payload));
        var handler = NewHandler(new StubRegistry(), new StubEnqueuer());

        var result = await handler.HandleAsync(envelope, CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(ConsentCaptureRejectionCodes.MissingCustomerId);
    }

    [Fact]
    public async Task HandleAsync_MissingTenantId_ReturnsFailure_MissingTenantId_NoDefaultTenantFallback()
    {
        // §4D I1: no hardcoded default tenant — handler MUST fail when tid is missing.
        var payload = new ConsentCapturePayload { CustomerId = CustomerId, TenantId = "" };
        var envelope = NewEnvelope(parametersJson: SerializePayload(payload));
        var enqueuer = new StubEnqueuer();
        var handler = NewHandler(new StubRegistry(), enqueuer);

        var result = await handler.HandleAsync(envelope, CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(ConsentCaptureRejectionCodes.MissingTenantId);
        enqueuer.Sent.Should().BeEmpty("no downstream enqueue when tenantId is missing (§4D I1).");
    }

    [Fact]
    public async Task HandleAsync_CustomerIdMismatch_ReturnsFailure_CustomerIdMismatch()
    {
        // Envelope carries one customerId; payload carries a different one.
        // This would cross a Cosmos partition boundary (§4D I3) — reject up front.
        var payload = new ConsentCapturePayload { CustomerId = "different-customer", TenantId = TenantId };
        var envelope = NewEnvelope(customerId: CustomerId, parametersJson: SerializePayload(payload));
        var handler = NewHandler(new StubRegistry(), new StubEnqueuer());

        var result = await handler.HandleAsync(envelope, CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(ConsentCaptureRejectionCodes.CustomerIdMismatch);
    }

    [Fact]
    public async Task HandleAsync_FreshConsent_NoExistingEnvironment_EnqueuesH0()
    {
        var payload = ValidPayload();
        var envelope = NewEnvelope(parametersJson: SerializePayload(payload));
        var enqueuer = new StubEnqueuer();
        var handler = NewHandler(new StubRegistry(snapshot: null), enqueuer);

        var result = await handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>()
            .Which.IdempotencyKey.Should().Be($"consent-{CustomerId}-{TenantId}");
        enqueuer.Sent.Should().ContainSingle();

        var next = enqueuer.Sent[0];
        next.HandlerId.Should().Be("H0");
        next.RunId.Should().Be(RunId, "the H0 enqueue MUST carry the same RunId so downstream tracing correlates.");
        next.CustomerId.Should().Be(CustomerId, "§4D I3 — CustomerId partition-key discipline preserved across enqueue.");

        // §4D I1 — the tid MUST propagate to H0's payload.
        using var doc = JsonDocument.Parse(next.ParametersJson);
        doc.RootElement.GetProperty("tenantId").GetString().Should().Be(TenantId);
        doc.RootElement.GetProperty("customerId").GetString().Should().Be(CustomerId);
    }

    [Theory]
    [InlineData("Ready")]
    [InlineData("Running")]
    [InlineData("WaitingOnGate")]
    [InlineData("READY")]         // case-insensitive
    [InlineData("running")]        // case-insensitive
    public async Task HandleAsync_ReConsentNoOp_LiveExistingRun_DoesNotEnqueueH0(string existingStatus)
    {
        var payload = ValidPayload();
        var envelope = NewEnvelope(parametersJson: SerializePayload(payload));
        var enqueuer = new StubEnqueuer();
        var registry = new StubRegistry(snapshot: new DataverseEnvironmentRegistrySnapshot(
            EnvironmentId: "env-1",
            CustomerId: CustomerId,
            TenantId: TenantId,
            SetupStatus: existingStatus,
            CurrentRunId: "existing-run-id-abc"));
        var handler = NewHandler(registry, enqueuer);

        var result = await handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>()
            .Which.IdempotencyKey.Should().Be($"consent-{CustomerId}-{TenantId}");
        enqueuer.Sent.Should().BeEmpty("re-consent no-op branch MUST NOT restart the pipeline.");
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    [InlineData("FAILED")]         // case-insensitive
    [InlineData("cancelled")]      // case-insensitive
    public async Task HandleAsync_ReConsentRestart_TerminalStatus_EnqueuesH0(string existingStatus)
    {
        var payload = ValidPayload();
        var envelope = NewEnvelope(parametersJson: SerializePayload(payload));
        var enqueuer = new StubEnqueuer();
        var registry = new StubRegistry(snapshot: new DataverseEnvironmentRegistrySnapshot(
            EnvironmentId: "env-1",
            CustomerId: CustomerId,
            TenantId: TenantId,
            SetupStatus: existingStatus,
            CurrentRunId: null));
        var handler = NewHandler(registry, enqueuer);

        var result = await handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        enqueuer.Sent.Should().ContainSingle("restart-from-H0 MUST enqueue exactly one H0 dispatch.");
        enqueuer.Sent[0].HandlerId.Should().Be("H0");
    }

    [Fact]
    public async Task HandleAsync_UnknownStatus_DefaultsToEnqueueH0()
    {
        // Defensive branch — a future SetupStatus value should NOT block the
        // pipeline (safer to enqueue H0; level-1 dedup catches the duplicate).
        var payload = ValidPayload();
        var envelope = NewEnvelope(parametersJson: SerializePayload(payload));
        var enqueuer = new StubEnqueuer();
        var registry = new StubRegistry(snapshot: new DataverseEnvironmentRegistrySnapshot(
            EnvironmentId: "env-1",
            CustomerId: CustomerId,
            TenantId: TenantId,
            SetupStatus: "SomeFutureStatus",
            CurrentRunId: null));
        var handler = NewHandler(registry, enqueuer);

        var result = await handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        enqueuer.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task HandleAsync_RegistryThrows_ReturnsFailure_ResumableRegistryUnavailable()
    {
        var payload = ValidPayload();
        var envelope = NewEnvelope(parametersJson: SerializePayload(payload));
        var enqueuer = new StubEnqueuer();
        var registry = new ThrowingRegistry(new InvalidOperationException("Dataverse unreachable"));
        var handler = NewHandler(registry, enqueuer);

        var result = await handler.HandleAsync(envelope, CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(ConsentCaptureRejectionCodes.RegistryUnavailable);
        enqueuer.Sent.Should().BeEmpty("no H0 enqueue when the registry lookup fails.");
    }

    [Fact]
    public async Task HandleAsync_IsIdempotent_SameInputsYieldSameIdempotencyKey()
    {
        // Level-3 idempotency at the return-value level: two invocations
        // with identical (customerId, tenantId) MUST return the SAME
        // idempotency key — this is what allows the future reconciler to
        // detect the duplicate in ProvisioningRun.CompletedPhases.
        var payload = ValidPayload();
        var envelope = NewEnvelope(parametersJson: SerializePayload(payload));
        var handler1 = NewHandler(new StubRegistry(), new StubEnqueuer());
        var handler2 = NewHandler(new StubRegistry(), new StubEnqueuer());

        var r1 = await handler1.HandleAsync(envelope, CancellationToken.None);
        var r2 = await handler2.HandleAsync(envelope, CancellationToken.None);

        r1.Should().BeOfType<HandlerResult.Success>();
        r2.Should().BeOfType<HandlerResult.Success>();
        ((HandlerResult.Success)r1).IdempotencyKey
            .Should().Be(((HandlerResult.Success)r2).IdempotencyKey);
    }

    [Theory]
    [InlineData("acme", "tid-1", "acme", "tid-1", true)]
    [InlineData("acme", "tid-1", "acme", "tid-2", false)]  // different tenant
    [InlineData("acme", "tid-1", "beta", "tid-1", false)]  // different customer
    public void BuildIdempotencyKey_ChangesWhenAnyDimensionDiffers(
        string c1, string t1, string c2, string t2, bool shouldBeEqual)
    {
        var k1 = H05ConsentCaptureHandler.BuildIdempotencyKey(c1, t1);
        var k2 = H05ConsentCaptureHandler.BuildIdempotencyKey(c2, t2);

        if (shouldBeEqual) k1.Should().Be(k2);
        else k1.Should().NotBe(k2);
    }

    // ---------------------------------------------------------------------
    // Helpers + stubs
    // ---------------------------------------------------------------------

    private static H05ConsentCaptureHandler NewHandler(
        IDataverseEnvironmentRegistryClient registry,
        IHandlerEnqueuer enqueuer)
    {
        return new H05ConsentCaptureHandler(
            registry,
            enqueuer,
            NullLogger<H05ConsentCaptureHandler>.Instance);
    }

    private static HandlerEnvelope NewEnvelope(
        string customerId = CustomerId,
        string parametersJson = "{}")
    {
        return new HandlerEnvelope
        {
            HandlerId = H05ConsentCaptureHandler.HandlerIdConstant,
            RunId = RunId,
            CustomerId = customerId,
            ParametersJson = parametersJson,
            EnqueuedAt = DateTimeOffset.UtcNow,
        };
    }

    private static ConsentCapturePayload ValidPayload() =>
        new() { CustomerId = CustomerId, TenantId = TenantId };

    private static string SerializePayload(ConsentCapturePayload payload) =>
        JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

    private sealed class StubRegistry : IDataverseEnvironmentRegistryClient
    {
        private readonly DataverseEnvironmentRegistrySnapshot? _snapshot;

        public StubRegistry(DataverseEnvironmentRegistrySnapshot? snapshot = null)
        {
            _snapshot = snapshot;
        }

        public Task<DataverseEnvironmentRegistrySnapshot?> LookupByTenantIdAsync(
            string tenantId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_snapshot);
        }
    }

    private sealed class ThrowingRegistry : IDataverseEnvironmentRegistryClient
    {
        private readonly Exception _toThrow;
        public ThrowingRegistry(Exception toThrow) => _toThrow = toThrow;

        public Task<DataverseEnvironmentRegistrySnapshot?> LookupByTenantIdAsync(
            string tenantId, CancellationToken cancellationToken)
        {
            throw _toThrow;
        }
    }

    private sealed class StubEnqueuer : IHandlerEnqueuer
    {
        public List<HandlerEnvelope> Sent { get; } = new();

        public Task EnqueueAsync(HandlerEnvelope envelope, CancellationToken cancellationToken)
        {
            Sent.Add(envelope);
            return Task.CompletedTask;
        }
    }
}
