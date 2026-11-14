using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.FieldMapping;

/// <summary>
/// #2799 (unified-access-control-r2), chunk 3, C7: a LOCKED invoice-family column (<see cref="InvoiceFamilyLockedWrite.LockedColumns"/>)
/// is field-secured so that only this server writes it. A create the application writes for a person no longer refuses it as
/// "field-secured" — the same exemption as the project filing pair — because what is asked before it is written is still asked AS
/// THE PERSON: the table's Create and Append, and AppendTo on every record a lookup names, the locked lookup included. Must fire:
/// the person without AppendTo on the record the locked lookup names is refused; a secured column that is NOT locked is still
/// refused. Must not fire: the allowed create passes, and the locked column is never asked about as a secured column.
/// </summary>
public partial class CallerChecksConcurrencyTests
{
    private const string LockedFamilyTable = "sprk_invoiceapproval";
    private const string LockedLookup = "sprk_regardingmatter";

    /// <summary>A create of an invoice approval naming a matter through the locked lookup, plus <paramref name="columns"/>.</summary>
    private DataverseWriteItemMapper.MappedItem LockedFamilyCreate(bool appendable, params string[] columns)
    {
        InvoiceFamilyLockedWrite.LockedColumnsOf(LockedFamilyTable).Should().ContainKey(LockedLookup, "the column under test is locked");
        _caller.TableName = LockedFamilyTable;
        _caller.RelatedTables.Add("sprk_matter");
        _caller.Held.UnionWith([$"prvCreate{LockedFamilyTable}", $"prvAppend{LockedFamilyTable}"]);
        foreach (var locked in InvoiceFamilyLockedWrite.LockedColumnsOf(LockedFamilyTable).Keys)
            _caller.Secured.Add(locked); // the lock as the field-security profile applies it

        var matter = Guid.NewGuid();
        _caller.Existing.Add(matter);
        if (appendable)
            _caller.Appendable.Add(matter);
        return Item([new DataverseWriteItemMapper.MappedLookup(LockedLookup, "sprk_matter", "sprk_matters", matter)], columns);
    }

    [Fact]
    public async Task Locked_ACreateNamingAnAppendableRecordThroughALockedLookup_IsAllowed_TheLockedColumnNotAskedAsSecured()
    {
        var item = LockedFamilyCreate(appendable: true, "sprk_name");

        var outcome = await CallerWriteAuthorization.CheckCallerMayCreateAsync(_caller, Me, LockedFamilyTable, item, null, CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Denied);
        var columnQuestions = _caller.Asked.Where(p => p.Contains("/Attributes", StringComparison.Ordinal)).ToList();
        columnQuestions.Should().ContainSingle().Which.Should().Contain("'sprk_name'").And.NotContain(LockedLookup);
        _caller.Asked.Should().Contain(p => p.Contains("RetrievePrincipalAccess", StringComparison.Ordinal)
            && p.Contains(item.Lookups[0].RecordId.ToString("D"), StringComparison.Ordinal), "AppendTo is asked as the person");
    }

    [Fact]
    public async Task Locked_ACreateNamingARecordThePersonMayNotAppendTo_ThroughALockedLookup_IsRefused()
    {
        var item = LockedFamilyCreate(appendable: false, "sprk_name");

        var outcome = await CallerWriteAuthorization.CheckCallerMayCreateAsync(_caller, Me, LockedFamilyTable, item, null, CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.Denied.Should().Be("You do not have permission to file this record under the sprk_matter it names.");
        outcome.ParentUnavailable.Should().BeTrue();
        outcome.RefusedColumn.Should().Be(LockedLookup);
    }

    [Fact]
    public async Task Locked_ACreateNamingAnUnknownRecordThroughALockedLookup_AnswersAsOneThePersonMayNotAppendTo()
    {
        var denied = await CallerWriteAuthorization.CheckCallerMayCreateAsync(
            _caller, Me, LockedFamilyTable, LockedFamilyCreate(appendable: false), null, CancellationToken.None);
        var unknownItem = LockedFamilyCreate(appendable: false);
        _caller.Existing.Clear();

        var unknown = await CallerWriteAuthorization.CheckCallerMayCreateAsync(_caller, Me, LockedFamilyTable, unknownItem, null, CancellationToken.None);

        unknown.Should().BeEquivalentTo(denied);
    }

    [Fact]
    public async Task Locked_ASecuredColumnThatIsNotLocked_OnTheSameCreate_IsStillRefused()
    {
        var item = LockedFamilyCreate(appendable: true, "sprk_name");
        _caller.Secured.Add("sprk_name");

        var outcome = await CallerWriteAuthorization.CheckCallerMayCreateAsync(_caller, Me, LockedFamilyTable, item, null, CancellationToken.None);

        outcome.Denied.Should().Be("Column 'sprk_name' is field-secured and cannot be set on a record the application creates for you.");
    }
}
