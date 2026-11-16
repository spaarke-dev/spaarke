using System.ServiceModel;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;

namespace Sprk.Bff.Api.Tests.DataMutation.Signals;

/// <summary>
/// ebilling-r1 task 130e-1 (DEF-060, uac-r2 C1, C3, C4): <see cref="BillingFirmReasonReplay"/>, the firm reason of a Signal written from the
/// stored plan (after-commit steps, re-run, correction / Add line reconcile). The text is the plan entry's policy version's
/// <c>sprk_firmdescription</c> as stored now, validated with that version's own rule set and policy names; anything the planned request
/// carries is replaced. Over a strict fake of the Dataverse reads it makes.
/// </summary>
public class BillingFirmReasonReplayTests
{
    private static readonly Guid VersionId = Guid.Parse("130e0000-0000-4000-8000-0000000000c1");
    private static readonly Guid PolicyId = Guid.Parse("130e0000-0000-4000-8000-0000000000c2");
    private static readonly Guid RuleSetId = Guid.Parse("130e0000-0000-4000-8000-0000000000c3");

    private sealed class Store
    {
        public Mock<IGenericEntityService> Entities { get; } = new(MockBehavior.Strict);
        public CapturingLogger<BillingFirmReasonReplayTests> Log { get; } = new();

        public Store(string? text, string? policyName = "Expense Codes", string? ruleSetName = "Acme Billing Guidelines", bool policyLinked = true,
            Exception? versionFault = null)
        {
            var versionRead = Entities.Setup(e => e.RetrieveAsync(BillingFactLoader.VersionEntity, VersionId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()));
            if (versionFault is not null)
            {
                versionRead.ThrowsAsync(versionFault);
            }
            else
            {
                var version = new Entity(BillingFactLoader.VersionEntity, VersionId) { [BillingFactLoader.FirmDescriptionColumn] = text };
                if (policyLinked)
                {
                    version["sprk_policy"] = new EntityReference(BillingFactLoader.PolicyEntity, PolicyId);
                }

                versionRead.ReturnsAsync(version);
            }

            Entities.Setup(e => e.RetrieveAsync(BillingFactLoader.PolicyEntity, PolicyId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Entity(BillingFactLoader.PolicyEntity, PolicyId)
                {
                    [BillingFactLoader.NameColumn] = policyName,
                    ["sprk_ruleset"] = new EntityReference(BillingFactLoader.RuleSetEntity, RuleSetId),
                });
            Entities.Setup(e => e.RetrieveAsync(BillingFactLoader.RuleSetEntity, RuleSetId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Entity(BillingFactLoader.RuleSetEntity, RuleSetId) { [BillingFactLoader.NameColumn] = ruleSetName });
        }

        public BillingFirmReasonReplay Replay() => new(Entities.Object, Log);

        public int Reads(string entity) => Entities.Invocations.Count(i => i.Method.Name == "RetrieveAsync" && (string)i.Arguments[0] == entity);
    }

    private static SignalWriteRequest Planned(string? firmReason = null) => new(
        PolicyId, "BILL-EXP", VersionId, "sprk_billingevent", Guid.Parse("130e0000-0000-4000-8000-0000000000c4"), "INV-1 #1",
        "Expense not allowed", SignalWriter.LaneDecide, SignalWriter.SeverityWarning, "An expense is not allowed.", new Dictionary<string, object?>())
    {
        FirmReason = firmReason,
    };

    [Fact(DisplayName = "130e-1 C1 must-fire: the version's stored text replaces whatever the planned request carries; nothing else changes")]
    public async Task TheVersionsText_ReplacesThePlans()
    {
        var store = new Store("  Expenses of this kind are not reimbursed. ");
        var planned = Planned("PLANTED text.");

        var replayed = await store.Replay().ApplyAsync(planned, default);

        replayed.FirmReason.Should().Be("Expenses of this kind are not reimbursed.");
        replayed.Should().BeEquivalentTo(planned, o => o.Excluding(r => r.FirmReason));
    }

    [Theory(DisplayName = "130e-1 C1 must-not-fire: a version with no text gives no firm reason (a planted one is cleared) and reads no names")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NoStoredText_GivesNone(string? text)
    {
        var store = new Store(text);

        (await store.Replay().ApplyAsync(Planned("PLANTED text."), default)).FirmReason.Should().BeNull();

        store.Reads(BillingFactLoader.PolicyEntity).Should().Be(0);
        store.Log.Entries.Should().BeEmpty();
    }

    [Theory(DisplayName = "130e-1 C3/C4: a text naming its own policy or rule set (any case), or failing the rules, is dropped with a code-only Warning")]
    [InlineData("See EXPENSE codes.", FirmDescriptionRules.NamesTheRule)]
    [InlineData("Per the acme billing guidelines.", FirmDescriptionRules.NamesTheRule)]
    [InlineData("Not {allowed}.", FirmDescriptionRules.Braces)]
    [InlineData("Not\u2028allowed.", FirmDescriptionRules.InvisibleCharacter)]
    public async Task AnInvalidText_IsDropped(string text, string reason)
    {
        var store = new Store(text);

        (await store.Replay().ApplyAsync(Planned(), default)).FirmReason.Should().BeNull();

        var entry = store.Log.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain(FirmDescriptionRules.DroppedLogCode).And.Contain(reason).And.Contain(VersionId.ToString()).And.NotContain(text);
    }

    [Fact(DisplayName = "130e-1 C4 fail-closed: a version whose policy cannot be read for its name drops the text (names_unread)")]
    public async Task AVersionWithoutItsPolicy_DropsTheText()
    {
        var store = new Store("Expenses of this kind are not reimbursed.", policyLinked: false);

        (await store.Replay().ApplyAsync(Planned(), default)).FirmReason.Should().BeNull();

        store.Log.Entries.Should().ContainSingle().Which.Message.Should().Contain(BillingFirmReasonReplay.NamesUnreadReason);
    }

    [Fact(DisplayName = "130e-1 C3: a version that no longer exists drops the text (row_gone) and the Signal is still written; any other read fault propagates")]
    public async Task AGoneVersion_DropsTheText_AnotherFaultPropagates()
    {
        var gone = new FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040217) }, "Does Not Exist");
        var goneStore = new Store(null, versionFault: gone);
        (await goneStore.Replay().ApplyAsync(Planned("PLANTED text."), default)).FirmReason.Should().BeNull();
        goneStore.Log.Entries.Should().ContainSingle().Which.Message.Should().Contain(BillingFirmReasonReplay.RowGoneReason);

        var faulting = new Store(null, versionFault: new TimeoutException("transient"));
        var act = async () => await faulting.Replay().ApplyAsync(Planned(), default);
        await act.Should().ThrowAsync<TimeoutException>("a transient read is retried by the step, never read as no text");
    }

    [Fact(DisplayName = "130e-1: one replay reads a version once, however many entries cite it")]
    public async Task AVersion_IsReadOncePerRun()
    {
        var store = new Store("Expenses of this kind are not reimbursed.");
        var replay = store.Replay();

        await replay.ApplyAsync(Planned(), default);
        await replay.ApplyAsync(Planned("PLANTED text."), default);

        store.Reads(BillingFactLoader.VersionEntity).Should().Be(1);
        store.Reads(BillingFactLoader.PolicyEntity).Should().Be(1);
    }
}
