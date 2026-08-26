// -----------------------------------------------------------------------------
// H14bGraphWebhookSubHandlerTests.cs
//
// Unit tests over H14bGraphWebhookSubHandler (task 073 — wave C4 Batch 3F).
//
// ADR-038 CATEGORY: Path #1 — pure C# unit test. NO live Graph REST calls.
// Fakes replace IKvSecretReader + IGraphSubscriptionCreator.
//
// COVERAGE:
//   AC-1  Happy path (both Communication + Email targets, both Created) — Success.
//   AC-2  Signing key NotFound — Resumable, MissingSigningKey code, subscription creator never called.
//   AC-3  Signing key read Failure (infra) — Resumable, SigningKeyReadFailed code.
//   AC-4  No webhook targets configured (both resources absent) — Resumable, NoWebhookTargetsConfigured.
//   AC-5  One of two targets fails create — RetryableWithCleanup, diagnostic names the failing module.
//   AC-6  Only Communication target configured (Email absent) — Success, only 1 subscription call.
//   AC-7  Handler-id mismatch — throws.
//   AC-8  Idempotency key format determinism (resource-set + notification base URL).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H14bGraphWebhookSubHandlerTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-h14b-run";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string KeyVaultName = "sprk-acme-prod-kv";
    private const string SubscriptionId = "22222222-3333-4444-5555-666666666666";
    private const string NotificationBaseUrl = "https://sprk-acme-prod.azurewebsites.net";
    private const string CommunicationResource = "communications/callRecords";
    private const string EmailResource = "users/mailbox-guid/messages";
    private const string SigningKey = "super-secret-hmac-key";

    // ---------- AC-1 happy path ----------

    [Fact]
    public async Task AC1_HappyPath_BothTargets_Succeeds()
    {
        var reader = FakeReader.Success(SigningKey);
        var creator = FakeCreator.Success();
        var handler = BuildHandler(reader, creator);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var success = result.Should().BeOfType<HandlerResult.Success>().Subject;
        success.IdempotencyKey.Should().Be(H14bGraphWebhookSubHandler.BuildIdempotencyKey(
            CustomerId, new[] { CommunicationResource, EmailResource }, NotificationBaseUrl));
        creator.CallCount.Should().Be(2);
        creator.Requests.Select(r => r.ClientState).Should().OnlyContain(cs => cs == SigningKey);
    }

    // ---------- AC-2 signing key not found ----------

    [Fact]
    public async Task AC2_SigningKeyNotFound_FailsResumable_CreatorNeverCalled()
    {
        var reader = FakeReader.NotFound();
        var creator = FakeCreator.Success();
        var handler = BuildHandler(reader, creator);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable);
        failure.RejectionCode.Should().Be(H14bRejections.MissingSigningKey);
        creator.CallCount.Should().Be(0);
    }

    // ---------- AC-3 signing key read infra failure ----------

    [Fact]
    public async Task AC3_SigningKeyReadFailure_FailsResumable()
    {
        var reader = FakeReader.Failure("az CLI timeout");
        var handler = BuildHandler(reader, FakeCreator.Success());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(H14bRejections.SigningKeyReadFailed);
        failure.Diagnostic.Should().Contain("az CLI timeout");
    }

    // ---------- AC-4 no targets configured ----------

    [Fact]
    public async Task AC4_NoWebhookTargetsConfigured_FailsResumable()
    {
        var reader = FakeReader.Success(SigningKey);
        var creator = FakeCreator.Success();
        var handler = BuildHandler(reader, creator);
        var envelope = BuildEnvelope(H14bGraphWebhookSubHandler.BuildParametersJson(
            TenantId, KeyVaultName, SubscriptionId, NotificationBaseUrl, null, null, 4230));

        var result = await handler.HandleAsync(envelope, CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.RejectionCode.Should().Be(H14bRejections.NoWebhookTargetsConfigured);
        creator.CallCount.Should().Be(0);
    }

    // ---------- AC-5 partial failure ----------

    [Fact]
    public async Task AC5_OneTargetFails_FailsRetryableWithCleanup_NamesFailingModule()
    {
        var reader = FakeReader.Success(SigningKey);
        var creator = FakeCreator.FailForResource(EmailResource, "403 Forbidden");
        var handler = BuildHandler(reader, creator);

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.RetryableWithCleanup);
        failure.RejectionCode.Should().Be(H14bRejections.SubscriptionCreateFailed);
        failure.Diagnostic.Should().Contain("Email").And.Contain("403 Forbidden");
    }

    // ---------- AC-6 only one target configured ----------

    [Fact]
    public async Task AC6_OnlyCommunicationTarget_Succeeds_OneCreatorCall()
    {
        var reader = FakeReader.Success(SigningKey);
        var creator = FakeCreator.Success();
        var handler = BuildHandler(reader, creator);
        var envelope = BuildEnvelope(H14bGraphWebhookSubHandler.BuildParametersJson(
            TenantId, KeyVaultName, SubscriptionId, NotificationBaseUrl, CommunicationResource, null, 4230));

        var result = await handler.HandleAsync(envelope, CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        creator.CallCount.Should().Be(1);
        creator.Requests.Single().ModuleName.Should().Be("Communication");
    }

    // ---------- AC-7 handler-id mismatch ----------

    [Fact]
    public async Task AC7_HandlerIdMismatch_Throws()
    {
        var handler = BuildHandler(FakeReader.Success(SigningKey), FakeCreator.Success());
        var wrongEnvelope = new HandlerEnvelope
        {
            HandlerId = "H0",
            RunId = RunId,
            CustomerId = CustomerId,
            ParametersJson = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        var act = async () => await handler.HandleAsync(wrongEnvelope, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*mismatched HandlerId*");
    }

    // ---------- AC-8 idempotency key determinism ----------

    [Fact]
    public void AC8_IdempotencyKey_IsDeterministic()
    {
        var k1 = H14bGraphWebhookSubHandler.BuildIdempotencyKey(
            CustomerId, new[] { CommunicationResource, EmailResource }, NotificationBaseUrl);
        var k2 = H14bGraphWebhookSubHandler.BuildIdempotencyKey(
            CustomerId, new[] { EmailResource, CommunicationResource }, NotificationBaseUrl);
        k1.Should().Be(k2);
        k1.Should().StartWith($"h14-{CustomerId}-graph-");
    }

    // ---------- helpers ----------

    private static H14bGraphWebhookSubHandler BuildHandler(FakeReader reader, FakeCreator creator)
        => new(reader, creator, NullLogger<H14bGraphWebhookSubHandler>.Instance);

    private static HandlerEnvelope BuildEnvelope(string? parametersJson = null) => new()
    {
        HandlerId = H14bGraphWebhookSubHandler.HandlerIdentifier,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = parametersJson ?? H14bGraphWebhookSubHandler.BuildParametersJson(
            TenantId, KeyVaultName, SubscriptionId, NotificationBaseUrl, CommunicationResource, EmailResource, 4230),
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private sealed class FakeReader : IKvSecretReader
    {
        private readonly KvSecretReadResult _result;
        private FakeReader(KvSecretReadResult result) => _result = result;
        public static FakeReader Success(string value) => new(new KvSecretReadResult.Success(value));
        public static FakeReader NotFound() => new(new KvSecretReadResult.NotFound());
        public static FakeReader Failure(string diagnostic) => new(new KvSecretReadResult.Failure(diagnostic));

        public Task<KvSecretReadResult> ReadSecretAsync(string vaultName, string subscriptionId, string secretName, CancellationToken ct)
            => Task.FromResult(_result);
    }

    private sealed class FakeCreator : IGraphSubscriptionCreator
    {
        private readonly Func<GraphSubscriptionRequest, GraphSubscriptionOutcome> _behavior;
        public int CallCount { get; private set; }
        public List<GraphSubscriptionRequest> Requests { get; } = new();

        private FakeCreator(Func<GraphSubscriptionRequest, GraphSubscriptionOutcome> behavior) => _behavior = behavior;

        public static FakeCreator Success() => new(req => new GraphSubscriptionOutcome.Created($"sub-{req.ModuleName}"));

        public static FakeCreator FailForResource(string resource, string diagnostic) => new(req =>
            req.Resource == resource
                ? new GraphSubscriptionOutcome.Failure(diagnostic)
                : new GraphSubscriptionOutcome.Created($"sub-{req.ModuleName}"));

        public Task<GraphSubscriptionOutcome> CreateOrUpdateAsync(GraphSubscriptionRequest request, CancellationToken ct)
        {
            CallCount++;
            Requests.Add(request);
            return Task.FromResult(_behavior(request));
        }
    }
}
