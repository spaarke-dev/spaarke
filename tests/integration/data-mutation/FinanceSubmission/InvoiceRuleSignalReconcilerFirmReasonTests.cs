using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 130e-1 fix round 1 (F2c, uac-r2 C1): the correction / Add line reconcile writes a raised rule Signal from the stored plan entry
/// (<see cref="InvoiceRuleSignalReconciler.ReconcileRuleAsync"/>), so its firm reason is re-derived from the entry's policy version, never taken from
/// the plan. Removing that replay call fails <see cref="Reconcile_WritesTheVersionsText_NotThePlans"/>. Over a fake of the version, policy and
/// rule set reads; the Signal the lifecycle hands the writer is recorded.
/// </summary>
public sealed class InvoiceRuleSignalReconcilerFirmReasonTests
{
    private static readonly Guid InvoiceId = Guid.Parse("130e0000-0000-4000-8000-0000000002f1");
    private static readonly Guid SubjectId = Guid.Parse("130e0000-0000-4000-8000-0000000002f2");
    private static readonly Guid VersionId = Guid.Parse("130e0000-0000-4000-8000-0000000002f3");
    private static readonly Guid PolicyId = Guid.Parse("130e0000-0000-4000-8000-0000000002f4");
    private static readonly Guid RuleSetId = Guid.Parse("130e0000-0000-4000-8000-0000000002f5");
    private const string Fingerprint = "fp-130e";

    private static (InvoiceRuleSignalReconciler Reconciler, List<SignalWriteRequest> Writes) Reconciler(string? storedText)
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection([]));
        entities.Setup(e => e.RetrieveAsync(BillingFactLoader.VersionEntity, VersionId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity(BillingFactLoader.VersionEntity, VersionId)
            {
                [BillingFactLoader.FirmDescriptionColumn] = storedText,
                ["sprk_policy"] = new EntityReference(BillingFactLoader.PolicyEntity, PolicyId),
            });
        entities.Setup(e => e.RetrieveAsync(BillingFactLoader.PolicyEntity, PolicyId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity(BillingFactLoader.PolicyEntity, PolicyId)
            {
                [BillingFactLoader.NameColumn] = "Late time",
                ["sprk_ruleset"] = new EntityReference(BillingFactLoader.RuleSetEntity, RuleSetId),
            });
        entities.Setup(e => e.RetrieveAsync(BillingFactLoader.RuleSetEntity, RuleSetId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity(BillingFactLoader.RuleSetEntity, RuleSetId) { [BillingFactLoader.NameColumn] = "Acme guidelines" });

        var writes = new List<SignalWriteRequest>();
        var lifecycle = new SignalEpisodeLifecycle(
            entities.Object,
            (request, _) =>
            {
                writes.Add(request);
                return Task.FromResult(new SignalWriteResult(Guid.NewGuid(), true, Guid.NewGuid(), Guid.NewGuid()));
            },
            (_, _, _, _) => throw new InvalidOperationException("no supersede is expected"),
            NullLogger.Instance);
        var reconciler = new InvoiceRuleSignalReconciler(
            entities.Object, lifecycle, (_, _, _, _, _) => throw new InvalidOperationException("no close is expected"), NullLogger.Instance,
            SignalLogContext.AddLine, InvoiceId, field: null);
        return (reconciler, writes);
    }

    private static JsonObject Root(JsonObject entry) => new() { ["signalPlan"] = new JsonArray(entry) };

    private static JsonObject Entry(string? plantedFirmReason, bool heldFallback = false)
    {
        var entry = new JsonObject
        {
            ["policyId"] = PolicyId.ToString("D"),
            ["policyCode"] = "BILL-LATE",
            ["policyVersionId"] = VersionId.ToString("D"),
            ["subjectEntity"] = "sprk_billingevent",
            ["subjectId"] = SubjectId.ToString("D"),
            ["subjectName"] = "INV-1 #1",
            ["headline"] = "Late time entry",
            ["lane"] = SignalWriter.LaneDecide,
            ["severity"] = SignalWriter.SeverityWarning,
            ["template"] = heldFallback ? BillingFindingSignalMapper.HeldSentenceNoValue : "A time entry is late.",
            ["facts"] = new JsonObject(),
            ["fingerprint"] = Fingerprint,
        };
        if (plantedFirmReason is not null)
        {
            entry["firmReason"] = plantedFirmReason;
        }

        if (heldFallback)
        {
            entry[InvoiceSubmissionPersister.HeldFallbackMember] = true;
        }

        return entry;
    }

    private static JsonObject Raise() => new()
    {
        ["policyCode"] = "BILL-LATE",
        ["subjectEntity"] = "sprk_billingevent",
        ["subjectId"] = SubjectId.ToString("D"),
        ["raiseFingerprint"] = Fingerprint,
    };

    [Fact(DisplayName = "130e-1 F2c must-fire: a reconciled raise carries its version's stored firm description, not a firmReason planted in the plan")]
    public async Task Reconcile_WritesTheVersionsText_NotThePlans()
    {
        var (reconciler, writes) = Reconciler("  Late entries are not billable.  ");

        await reconciler.ReconcileRuleAsync(Root(Entry("PLANTED text from the plan.")), Raise(), CancellationToken.None);

        writes.Should().ContainSingle().Which.FirmReason.Should().Be("Late entries are not billable.");
    }

    [Fact(DisplayName = "130e-1 F2c must-not-fire: a version with no text gives a reconciled Signal with none, whatever the plan says")]
    public async Task Reconcile_WithNoStoredText_WritesNone()
    {
        var (reconciler, writes) = Reconciler(null);

        await reconciler.ReconcileRuleAsync(Root(Entry("PLANTED text from the plan.")), Raise(), CancellationToken.None);

        writes.Should().ContainSingle().Which.FirmReason.Should().BeNull();
    }

    [Fact(DisplayName = "130e-1 F2c + O2: a reconciled held-fallback entry carries no firm reason although its version has a valid one")]
    public async Task Reconcile_OfAHeldFallbackEntry_WritesNone()
    {
        var (reconciler, writes) = Reconciler("Late entries are not billable.");

        await reconciler.ReconcileRuleAsync(Root(Entry(null, heldFallback: true)), Raise(), CancellationToken.None);

        writes.Should().ContainSingle().Which.FirmReason.Should().BeNull();
    }
}
