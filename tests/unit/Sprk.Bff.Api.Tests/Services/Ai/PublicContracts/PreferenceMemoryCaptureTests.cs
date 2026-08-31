// spaarkeai-assistant-enhancements-r4 (FR-08, task 031) — unit tests for the feedback→memory CAPTURE facade.
//
// PreferenceMemoryCapture resolves the caller's AAD oid → canonical Dataverse systemuserid, maps a neutral
// PreferenceCaptureInput onto a governed User-scope MemoryItem (fact type Preference, task 030) and persists
// it via the SHARED IMemoryItemStore (no forked store — ADR-013 / §11 reuse). These are BEHAVIOR tests of
// that facade→store boundary and its governance guardrails:
//   • Explicit directive → User scope, keyed by the resolved systemuserid, Origin=user, ConfirmedByUser=true.
//   • Inferred (ai-derived) → Origin=ai-derived, ConfirmedByUser=false.
//   • trustLevel is carried INERT (null) — ADR-042 #616 deferred; Tier-3 deletion/retention set.
//   • Per-user ONLY: never Record/global scope; the store is called with a User-scope item and nothing else.
//   • Unresolvable oid → Skipped, NO store write (never guess a partition).
//   • Best-effort: an unexpected store failure degrades to Failed and never throws.
//
// Mocking boundary (ADR-038 §4 module-boundary mocks): IMemoryItemStore + ISystemUserIdentityResolver are
// genuine persistence / identity seams.

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Identity;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.PublicContracts;

public sealed class PreferenceMemoryCaptureTests
{
    private const string Tenant = "tenant-r4-fr08";
    private const string Oid = "11111111-1111-1111-1111-111111111111";
    private static readonly Guid SystemUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Mock<IMemoryItemStore> _store = new(MockBehavior.Strict);
    private readonly Mock<ISystemUserIdentityResolver> _resolver = new(MockBehavior.Strict);

    private PreferenceMemoryCapture CreateSut() =>
        new(_store.Object, _resolver.Object, NullLogger<PreferenceMemoryCapture>.Instance);

    private void SetupResolver(Guid? systemUserId) =>
        _resolver
            .Setup(r => r.ResolveSystemUserIdAsync(Oid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemUserId);

    private static PreferenceCaptureInput Input(string origin, bool confirmed, string directive = "always summarize my tasks first") =>
        new(
            TenantId: Tenant,
            UserAadObjectId: Oid,
            Key: directive,
            Value: directive,
            Origin: origin,
            ConfirmedByUser: confirmed,
            SessionId: "session-1");

    // ── Explicit directive: User scope, canonical key, user origin, confirmed, full Tier-3 envelope ──
    [Fact]
    public async Task CapturePreferenceAsync_ExplicitDirective_PersistsUserScopePreference_KeyedBySystemUserId()
    {
        SetupResolver(SystemUserId);
        MemoryItem? captured = null;
        _store
            .Setup(s => s.UpsertAsync(It.IsAny<MemoryItem>(), Tenant, It.IsAny<CancellationToken>()))
            .Callback<MemoryItem, string, CancellationToken>((item, _, _) => captured = item)
            .ReturnsAsync((MemoryItem item, string _, CancellationToken _) => item);

        var outcome = await CreateSut().CapturePreferenceAsync(
            Input(MemoryOrigin.User, confirmed: true), CancellationToken.None);

        outcome.Status.Should().Be(PreferenceMemoryCaptureStatus.Captured);

        _store.Verify(s => s.UpsertAsync(It.IsAny<MemoryItem>(), Tenant, It.IsAny<CancellationToken>()), Times.Once);
        captured.Should().NotBeNull();

        // Per-user ONLY: User scope, keyed by the RESOLVED systemuserid (not the raw oid), no Record keying.
        captured!.Scope.Should().Be(MemoryScope.User);
        captured.UserId.Should().Be(SystemUserId.ToString("D"));
        captured.UserId.Should().NotBe(Oid);
        captured.SubjectType.Should().BeNull();
        captured.SubjectId.Should().BeNull();

        // Fact: the governed Preference type (task 030), user origin, confirmed.
        captured.Fact.Type.Should().Be(MemoryFactType.Preference);
        captured.Fact.Key.Should().Be("always summarize my tasks first");
        captured.Fact.Source.Should().Be(MemoryOrigin.User);
        captured.Fact.ConfirmedByUser.Should().BeTrue();
        // A confirmed directive recalls immediately (full confidence; the ConfirmedByUser gate also admits it).
        captured.Fact.Confidence.Should().Be(1.0);

        // Provenance envelope + Tier-3; trustLevel carried INERT (ADR-042 #616).
        captured.Version.Should().Be(MemoryItemContract.SchemaVersion);
        captured.Source.Should().Be(MemoryOrigin.User);
        captured.SessionId.Should().Be("session-1");
        captured.TrustLevel.Should().BeNull();
        captured.DeletionPolicy.Should().Be("user-erasable");
        captured.RetentionClass.Should().Be("tier-3-user-owned");
    }

    // ── Inferred preference: ai-derived origin, unconfirmed ──
    [Fact]
    public async Task CapturePreferenceAsync_Inferred_PersistsAiDerivedUnconfirmedPreference()
    {
        SetupResolver(SystemUserId);
        MemoryItem? captured = null;
        _store
            .Setup(s => s.UpsertAsync(It.IsAny<MemoryItem>(), Tenant, It.IsAny<CancellationToken>()))
            .Callback<MemoryItem, string, CancellationToken>((item, _, _) => captured = item)
            .ReturnsAsync((MemoryItem item, string _, CancellationToken _) => item);

        var outcome = await CreateSut().CapturePreferenceAsync(
            Input(MemoryOrigin.AiDerived, confirmed: false, directive: "the answer was too long"),
            CancellationToken.None);

        outcome.Status.Should().Be(PreferenceMemoryCaptureStatus.Captured);
        captured!.Scope.Should().Be(MemoryScope.User);
        captured.Fact.Type.Should().Be(MemoryFactType.Preference);
        captured.Fact.Source.Should().Be(MemoryOrigin.AiDerived);
        captured.Source.Should().Be(MemoryOrigin.AiDerived);
        captured.Fact.ConfirmedByUser.Should().BeFalse();
        captured.TrustLevel.Should().BeNull();
        // GOVERNANCE (ADR-042): an unconfirmed ai-derived inference MUST sit BELOW the MemoryItemStore
        // recall gate (MinUnconfirmedConfidence = 0.7) so it is a DORMANT candidate — persisted + visible to
        // the FR-B-03 review surface / task-032 producer, but NOT folded into the user prompt fragment (i.e.
        // it does not bias behavior) until the user confirms it. A single thumbs-down+comment must not
        // silently start steering the assistant.
        captured.Fact.Confidence.Should().BeLessThan(0.7,
            "an unconfirmed inference stays dormant below the recall gate until the user acknowledges it");
        captured.Fact.Confidence.Should().Be(PreferenceMemoryCapture.UnconfirmedCandidateConfidence);
    }

    // ── Unresolvable user → Skipped, NO store write (never guess a partition) ──
    [Fact]
    public async Task CapturePreferenceAsync_UnresolvableUser_SkipsWithoutStoreWrite()
    {
        SetupResolver(null); // no canonical systemuserid for this oid

        var outcome = await CreateSut().CapturePreferenceAsync(
            Input(MemoryOrigin.User, confirmed: true), CancellationToken.None);

        outcome.Status.Should().Be(PreferenceMemoryCaptureStatus.Skipped);
        outcome.Reason.Should().Contain("user");
        _store.Verify(s => s.UpsertAsync(It.IsAny<MemoryItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Empty directive → Skipped, resolver + store never touched ──
    [Fact]
    public async Task CapturePreferenceAsync_EmptyDirective_Skips()
    {
        var outcome = await CreateSut().CapturePreferenceAsync(
            new PreferenceCaptureInput(Tenant, Oid, Key: "   ", Value: "   ", MemoryOrigin.User, true, null),
            CancellationToken.None);

        outcome.Status.Should().Be(PreferenceMemoryCaptureStatus.Skipped);
        _resolver.Verify(r => r.ResolveSystemUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _store.Verify(s => s.UpsertAsync(It.IsAny<MemoryItem>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Unknown origin normalises to ai-derived (safer unconfirmed posture) ──
    [Fact]
    public async Task CapturePreferenceAsync_UnknownOrigin_NormalisesToAiDerived()
    {
        SetupResolver(SystemUserId);
        MemoryItem? captured = null;
        _store
            .Setup(s => s.UpsertAsync(It.IsAny<MemoryItem>(), Tenant, It.IsAny<CancellationToken>()))
            .Callback<MemoryItem, string, CancellationToken>((item, _, _) => captured = item)
            .ReturnsAsync((MemoryItem item, string _, CancellationToken _) => item);

        await CreateSut().CapturePreferenceAsync(
            Input(origin: "insights-engine", confirmed: false), CancellationToken.None);

        captured!.Source.Should().Be(MemoryOrigin.AiDerived);
        captured.Fact.Source.Should().Be(MemoryOrigin.AiDerived);
    }

    // ── Best-effort: an unexpected store failure degrades to Failed and never throws ──
    [Fact]
    public async Task CapturePreferenceAsync_StoreThrows_DegradesToFailed()
    {
        SetupResolver(SystemUserId);
        _store
            .Setup(s => s.UpsertAsync(It.IsAny<MemoryItem>(), Tenant, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cosmos unavailable"));

        var outcome = await CreateSut().CapturePreferenceAsync(
            Input(MemoryOrigin.User, confirmed: true), CancellationToken.None);

        outcome.Status.Should().Be(PreferenceMemoryCaptureStatus.Failed);
    }

    // ── A store ArgumentException (keying rejection) degrades to Skipped, never throws ──
    [Fact]
    public async Task CapturePreferenceAsync_StoreRejectsKeying_DegradesToSkipped()
    {
        SetupResolver(SystemUserId);
        _store
            .Setup(s => s.UpsertAsync(It.IsAny<MemoryItem>(), Tenant, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("invalid subject keying"));

        var outcome = await CreateSut().CapturePreferenceAsync(
            Input(MemoryOrigin.User, confirmed: true), CancellationToken.None);

        outcome.Status.Should().Be(PreferenceMemoryCaptureStatus.Skipped);
    }
}
