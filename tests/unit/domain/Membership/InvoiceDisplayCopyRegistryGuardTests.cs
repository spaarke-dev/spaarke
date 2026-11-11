using FluentAssertions;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Services.Ai.Membership;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Membership;

/// <summary>
/// field-mapping-server-write-path-r1 task 032b-1 (design notes/task-032b-design.md §3, T-D2; UAC-r2 U3 (c)). The invoice's four
/// attorney and paralegal columns are DISPLAY COPIES: values copied from the core record that confer no access. So
/// <c>sprk_invoice</c> has no entry in the access-conferring registry, and no display copy is ever an Assigned-To column. Re-adding
/// the entry (or listing a display copy for any table) fails here.
/// </summary>
public class InvoiceDisplayCopyRegistryGuardTests
{
    private const string Invoice = "sprk_invoice";

    /// <summary>The guard: every display copy the classifier calls Assigned-To over <paramref name="registry"/>, as (table, column).</summary>
    private static IReadOnlyList<(string Table, string Column)> ConferringDisplayCopies(AccessConferringRegistry? registry)
    {
        var classify = PushRuleSelection.AssignedToColumnsOf(registry);
        return MembershipOptionsDefaults.DisplayCopyColumns
            .SelectMany(kv => kv.Value.Select(column => (Table: kv.Key, Column: column)))
            .Where(x => classify(x.Table, x.Column))
            .ToList();
    }

    private static AccessConferringRegistry Bound()
    {
        var options = new MembershipOptions();
        new MembershipOptionsDefaults().PostConfigure(null, options);
        return options.AccessConferringRoles;
    }

    /// <summary>The registry as it was before U3 (c): the canonical seed plus the old invoice entry.</summary>
    private static AccessConferringRegistry WithTheOldInvoiceEntry()
    {
        var registry = Bound();
        registry.Entities[Invoice] =
        [
            new AccessConferringColumn { Field = "sprk_assignedto1", IdentityType = "Contact" },
            new AccessConferringColumn { Field = "sprk_assignedto2", IdentityType = "Contact" },
            new AccessConferringColumn { Field = "sprk_assignedtoattorney1", IdentityType = "Contact" },
            new AccessConferringColumn { Field = "sprk_assignedtoattorney2", IdentityType = "Contact" },
            new AccessConferringColumn { Field = "sprk_assignedtoparalegal1", IdentityType = "Contact" },
            new AccessConferringColumn { Field = "sprk_assignedtoparalegal2", IdentityType = "Contact" },
        ];
        return registry;
    }

    [Fact]
    public void TheCanonicalRegistry_HasNoInvoiceEntry()
    {
        MembershipOptionsDefaults.CanonicalAccessConferringRegistry.Keys.Should().NotContain(
            k => string.Equals(k, Invoice, StringComparison.OrdinalIgnoreCase),
            "an invoice's attorney and paralegal columns are display copies (U3 (c)); re-adding the entry needs UAC-r2");
        Bound().Entities.Keys.Should().NotContain(k => string.Equals(k, Invoice, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NoDisplayCopy_IsAnAssignedToColumn_OfTheCanonicalOrTheBoundRegistry()
    {
        ConferringDisplayCopies(null).Should().BeEmpty("the unbound registry reads as the canonical one");
        ConferringDisplayCopies(Bound()).Should().BeEmpty();
    }

    [Fact]
    public void TheDisplayCopies_AreTheFourInvoiceColumnsTheSeededProfileFills()
    {
        MembershipOptionsDefaults.DisplayCopyColumns.Keys.Should().BeEquivalentTo([Invoice]);
        MembershipOptionsDefaults.DisplayCopyColumns[Invoice].Should().BeEquivalentTo(
            ["sprk_assignedtoattorney1", "sprk_assignedtoattorney2", "sprk_assignedtoparalegal1", "sprk_assignedtoparalegal2"]);
    }

    /// <summary>Must-fire: a registry seeded with the old invoice entry is caught, naming each display copy it confers through.</summary>
    [Fact]
    public void MustFire_ARegistryWithTheInvoiceEntry_IsCaught()
    {
        var seeded = WithTheOldInvoiceEntry();

        seeded.Entities.Keys.Should().Contain(Invoice);
        ConferringDisplayCopies(seeded).Should().BeEquivalentTo(new[]
        {
            (Invoice, "sprk_assignedtoattorney1"), (Invoice, "sprk_assignedtoattorney2"),
            (Invoice, "sprk_assignedtoparalegal1"), (Invoice, "sprk_assignedtoparalegal2"),
        });
    }

    /// <summary>Must-not-fire: the registry's other tables, and a root's own attorney column, are not display copies.</summary>
    [Fact]
    public void MustNotFire_TheRootsAssignedColumns_AreStillAssignedTo_AndNoDisplayCopy()
    {
        var classify = PushRuleSelection.AssignedToColumnsOf(Bound());

        classify("sprk_matter", "sprk_assignedattorney1").Should().BeTrue("the matter's columns still confer access");
        classify("sprk_event", "sprk_assignedto1").Should().BeTrue();
        MembershipOptionsDefaults.DisplayCopyColumns.Keys.Should().NotContain(
            Bound().Entities.Keys, "a table of the registry never carries display copies");
    }
}
