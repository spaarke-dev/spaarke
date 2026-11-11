using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// ebilling-r1 task 130-0b step 2: the assistant tools' seam (<see cref="ExternalDataService.HideInvoiceFamilyRowsAsync"/>) decides a row of
/// EVERY child table with an invoice link by the invoices it links to. The tables come from <see cref="InvoiceLinks.Tables"/> (the
/// registries), never from a hand list, so a table added there is covered here without a test change. The handler tests cover the
/// table classes per tool.
/// </summary>
public sealed class InvoiceLinkedChildRowsTests
{
    /// <summary>Every child table with an invoice link: listed by <see cref="InvoiceLinks"/>, not a root, not in the invoice family.</summary>
    public static TheoryData<string> ChildTables
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var table in InvoiceLinks.Tables.Keys.Where(ExternalInvoiceVisibility.IsInvoiceLinkedChildTable).OrderBy(t => t, StringComparer.Ordinal))
                data.Add(table);
            return data;
        }
    }

    private static Task<IReadOnlySet<Guid>> HideAsync(InvoiceWorld world, InvoiceFamilyCaller caller, string table, params Guid[] rows) =>
        InvoiceFamilyHidingStubs.Real(world)(caller, table, rows, CancellationToken.None);

    [Fact(DisplayName = "130-0b step 2: the child tables are the InvoiceLinks tables that are neither a root nor in the invoice family, and include every class the tools return")]
    public void TheChildTables_AreDerivedFromInvoiceLinks()
    {
        var children = InvoiceLinks.Tables.Keys.Where(ExternalInvoiceVisibility.IsInvoiceLinkedChildTable).ToList();

        children.Should().Contain(new[] { "sprk_todo", "sprk_event", "sprk_document", "sprk_communication", "sprk_communicationthread", "sprk_analysis", "sprk_memo" });
        children.Should().NotContain(t => InvoiceLinks.IsRootWithInvoiceLink(t), "a root is never hidden by its invoice link (step 3 redacts it)");
        children.Should().NotContain(t => ExternalInvoiceVisibility.IsInvoiceFamilyTable(t), "an invoice-family row keeps the family path");
        ExternalInvoiceVisibility.IsInvoiceLinkedChildTable("sprk_workassignment").Should().BeFalse();
        ExternalInvoiceVisibility.IsInvoiceLinkedChildTable("sprk_invoicesubmission").Should().BeFalse();
        ExternalInvoiceVisibility.IsInvoiceLinkedChildTable("sprk_matter").Should().BeFalse();
        ExternalInvoiceVisibility.IsInvoiceLinkedChildTable(null).Should().BeFalse();
    }

    [Theory(DisplayName = "130-0b step 2 seam: on every child table, a row linked to another firm's invoice is hidden; its own firm's and an unlinked row are shown; one firm read")]
    [MemberData(nameof(ChildTables))]
    public async Task EveryChildTable_HidesARowOfAnotherFirmsInvoice_AndOnlyThatRow(string table)
    {
        var world = new InvoiceWorld();
        var own = world.OwnedInvoice();
        var other = world.OwnedInvoice();
        world.InvoiceFirm(other, InvoiceFamilyFirmWorld.OtherFirm);
        var column = InvoiceLinks.LinkColumnsOf(table)[0];
        var ownRow = world.LinkedChildRow(table, (column, own));
        var otherRow = world.LinkedChildRow(table, (column, other));
        var unlinked = world.LinkedChildRow(table);
        var caller = world.NewFamilyCaller();

        var hidden = await HideAsync(world, caller, table, ownRow, otherRow, unlinked);

        hidden.Should().BeEquivalentTo(new[] { otherRow });
        world.CallerFirmReads.Should().Be(1);
    }

    /// <summary>Task 130-0b step 3: every ROOT with an invoice link that <see cref="InvoiceLinks"/> lists (today the work assignment).</summary>
    public static TheoryData<string> RootTables
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var table in InvoiceLinks.Tables.Keys.Where(InvoiceLinks.IsRootWithInvoiceLink).OrderBy(t => t, StringComparer.Ordinal))
                data.Add(table);
            return data;
        }
    }

    [Theory(DisplayName = "130-0b step 3 seam: on every root with an invoice link, the seam returns the row linked to another firm's invoice (its link is blanked), never its own firm's or an unlinked row; one firm read")]
    [MemberData(nameof(RootTables))]
    public async Task EveryRoot_ReturnsTheRowWhoseLinkIsBlanked_AndOnlyThatRow(string table)
    {
        var world = new InvoiceWorld();
        var own = world.OwnedInvoice();
        var other = world.OwnedInvoice();
        world.InvoiceFirm(other, InvoiceFamilyFirmWorld.OtherFirm);
        var column = InvoiceLinks.LinkColumnsOf(table)[0];
        var ownRow = world.LinkedChildRow(table, (column, own));
        var otherRow = world.LinkedChildRow(table, (column, other));
        var unlinked = world.LinkedChildRow(table);

        var blanked = await HideAsync(world, world.NewFamilyCaller(), table, ownRow, otherRow, unlinked);

        blanked.Should().BeEquivalentTo(new[] { otherRow });
        world.CallerFirmReads.Should().Be(1);
        InvoiceLinks.Tables.Should().ContainKey("sprk_workassignment").WhoseValue.IsRoot.Should().BeTrue();
    }

    [Fact(DisplayName = "130-0b step 2 seam: one link read and one invoice decision per call, over the union of the rows' invoices")]
    public async Task OneLinkRead_AndOneDecision_PerCall()
    {
        var world = new InvoiceWorld();
        var a = world.OwnedInvoice();
        var b = world.OwnedInvoice();
        var rows = new[]
        {
            world.LinkedChildRow("sprk_document", ("sprk_invoice", a)),
            world.LinkedChildRow("sprk_document", ("sprk_relatedinvoice", b)),
            world.LinkedChildRow("sprk_document", ("sprk_invoice", a), ("sprk_relatedinvoice", b)),
        };

        var hidden = await HideAsync(world, world.NewFamilyCaller(), "sprk_document", rows);

        hidden.Should().BeEmpty();
        world.Reads("sprk_documents").Count(u => u.Contains("_sprk_relatedinvoice_value", StringComparison.Ordinal))
            .Should().Be(1, "the links of the page are read in one chunk");
        world.InvoiceReads.Should().ContainSingle("one decision: one firm-hold read for both invoices");
    }

    [Fact(DisplayName = "130-0b step 2 seam: a caller with no firm hides every invoice-linked row and shows an unlinked one")]
    public async Task ACallerWithNoFirm_HidesLinkedRows_ShowsUnlinked()
    {
        var world = new InvoiceWorld();
        var own = world.OwnedInvoice();
        var linked = world.LinkedChildRow("sprk_todo", ("sprk_regardinginvoice", own));
        var unlinked = world.LinkedChildRow("sprk_todo");

        var hidden = await HideAsync(world, new InvoiceFamilyCaller(null), "sprk_todo", linked, unlinked);

        hidden.Should().BeEquivalentTo(new[] { linked });
    }

    [Fact(DisplayName = "130-0b step 2 seam (F3b ruling): firms that cannot be read fail the call when a row links to an invoice; an unlinked row alone needs no firm")]
    public async Task UnreadableFirms_FailTheCall_WhenARowIsLinked()
    {
        var world = new InvoiceWorld { CallerFirmsFault = new TimeoutException("firms (test)") };
        var own = world.OwnedInvoice();
        var linked = world.LinkedChildRow("sprk_memo", ("sprk_regardinginvoice", own));
        var unlinked = world.LinkedChildRow("sprk_memo");

        var act = () => HideAsync(world, world.NewFamilyCaller(), "sprk_memo", linked, unlinked);
        await act.Should().ThrowAsync<ExternalDataService.DataverseUnavailableException>();

        var alone = await HideAsync(world, world.NewFamilyCaller(), "sprk_memo", unlinked);
        alone.Should().BeEmpty("no row links to an invoice: the rule does not apply, and no firm is read");
    }
}
