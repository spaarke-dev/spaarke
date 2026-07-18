using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Communication.Access;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Security-enforcement behavior of <see cref="CommunicationAccessFilter"/> AFTER the 2026-07-16 impersonation
/// rework (FR-08 / NFR-06). Record-level read access is now enforced by Dataverse IMPERSONATION (the endpoint
/// issues the sprk_communication query with MSCRMCallerID = caller systemuserid), so the rows the filter receives
/// are ALREADY exactly what the caller may see. The filter therefore only applies the two Spaarke business rules
/// impersonation does not cover, and these are the closed set of cases that would leak content if they regressed:
/// <list type="bullet">
///   <item>an INTERNAL-ONLY message (sprk_isinternalonly) is INVISIBLE to a non-internal caller (D-05);</item>
///   <item>DEFAULT-DENY / fail closed: an unreadable internal-only flag hides the row from a non-internal caller;</item>
///   <item>PRIVILEGE never gates a read and rides along as metadata — the filter takes NO AI dependency, so "no AI
///         at read time" (ADR-015) is structural;</item>
///   <item>the filter never re-computes record access: rows Dataverse returned to an internal caller pass through.</item>
/// </list>
/// The hand-computed membership ∪ overlay-grant union + point-forward privacy tests from task 042 are DROPPED —
/// that logic is retired for reads (record access is Dataverse's job via impersonation).
/// </summary>
public class CommunicationAccessFilterTests
{
    private static readonly Guid CallerId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private const int PrivilegeNone = 100000000;
    private const int PrivilegePrivileged = 100000002;

    private static CommunicationAccessFilter Sut() =>
        new(Mock.Of<ILogger<CommunicationAccessFilter>>());

    private static CommunicationAccessContext Caller(bool isInternal) => new(CallerId, isInternal);

    /// <summary>An impersonated row. <paramref name="internalOnly"/> = null omits the flag (unreadable case).</summary>
    private static Entity Message(bool? internalOnly = false, int privilege = PrivilegeNone)
    {
        var e = new Entity("sprk_communication", Guid.NewGuid())
        {
            ["sprk_privilegeclassification"] = new OptionSetValue(privilege),
        };
        if (internalOnly is not null)
            e["sprk_isinternalonly"] = internalOnly.Value;
        return e;
    }

    // ─────────────────────────── internal-only (D-05) ───────────────────────────

    [Fact]
    public void EvaluateMessage_InternalOnlyMessage_NonInternalCaller_IsHidden()
    {
        var decision = Sut().EvaluateMessage(Caller(isInternal: false), Message(internalOnly: true));

        decision.IsVisible.Should().BeFalse();
        decision.DenyReason.Should().Be("internal-only");
    }

    [Fact]
    public void EvaluateMessage_InternalOnlyMessage_InternalCaller_IsVisible()
    {
        var decision = Sut().EvaluateMessage(Caller(isInternal: true), Message(internalOnly: true));

        decision.IsVisible.Should().BeTrue();
    }

    [Fact]
    public void EvaluateMessage_NonInternalOnlyMessage_NonInternalCaller_IsVisible()
    {
        // A row Dataverse returned that is NOT internal-only is visible to a non-internal caller.
        var decision = Sut().EvaluateMessage(Caller(isInternal: false), Message(internalOnly: false));

        decision.IsVisible.Should().BeTrue();
    }

    // ─────────────────────────── fail-closed (NFR-06) ───────────────────────────

    [Fact]
    public void EvaluateMessage_InternalOnlyFlagUnreadable_NonInternalCaller_IsHidden_FailClosed()
    {
        // The endpoint always $selects the flag; if it is nonetheless absent, a non-internal caller must NOT see it.
        var decision = Sut().EvaluateMessage(Caller(isInternal: false), Message(internalOnly: null));

        decision.IsVisible.Should().BeFalse();
        decision.DenyReason.Should().Be("internal-only");
    }

    [Fact]
    public void EvaluateMessage_InternalOnlyFlagUnreadable_InternalCaller_IsVisible()
    {
        // An internal caller is past the internal-only gate regardless of the flag.
        var decision = Sut().EvaluateMessage(Caller(isInternal: true), Message(internalOnly: null));

        decision.IsVisible.Should().BeTrue();
    }

    // ─────────────────────────── privilege (ADR-015) ───────────────────────────

    [Fact]
    public void FilterMessages_Privilege_IsComposedMetadata_AndDoesNotGateTheRead()
    {
        // Two impersonated rows to an internal caller — one None, one Privileged — differ only in privilege.
        // Both are visible (privilege never gates); the privileged one carries its classification as metadata.
        // The SUT has NO AI dependency, so "no AI at read time" (ADR-015) is structural.
        var none = Message(internalOnly: false, privilege: PrivilegeNone);
        var privileged = Message(internalOnly: false, privilege: PrivilegePrivileged);

        var result = Sut().FilterMessages(Caller(isInternal: true), new[] { none, privileged });

        result.VisibleMessages.Should().HaveCount(2);
        result.Decisions.Single(d => d.Message == privileged).Decision.Privilege
            .Should().Be(CommunicationPrivilegeClassification.Privileged);
        result.Decisions.Single(d => d.Message == none).Decision.Privilege
            .Should().Be(CommunicationPrivilegeClassification.None);
    }

    // ─────────────────────────── pass-through of impersonated rows ───────────────────────────

    [Fact]
    public void FilterMessages_MixedSet_HidesOnlyInternalOnlyRows_FromNonInternalCaller()
    {
        // A non-internal caller sees the clean rows Dataverse returned but not the internal-only one.
        var open1 = Message(internalOnly: false);
        var internalOnly = Message(internalOnly: true, privilege: PrivilegePrivileged);
        var open2 = Message(internalOnly: false, privilege: PrivilegePrivileged);

        var result = Sut().FilterMessages(Caller(isInternal: false), new[] { open1, internalOnly, open2 });

        result.VisibleMessages.Should().BeEquivalentTo(new[] { open1, open2 });
        result.Decisions.Single(d => d.Message == internalOnly).Decision.DenyReason.Should().Be("internal-only");
    }

    [Fact]
    public void FilterMessages_InternalCaller_AllImpersonatedRowsPassThrough()
    {
        // The filter never re-computes record access: an internal caller sees every row Dataverse already returned,
        // input order preserved (privilege + internal-only do not hide anything from an internal caller).
        var rows = new[]
        {
            Message(internalOnly: false, privilege: PrivilegeNone),
            Message(internalOnly: true, privilege: PrivilegePrivileged),
            Message(internalOnly: false, privilege: PrivilegePrivileged),
        };

        var result = Sut().FilterMessages(Caller(isInternal: true), rows);

        result.VisibleMessages.Should().Equal(rows);
    }
}
