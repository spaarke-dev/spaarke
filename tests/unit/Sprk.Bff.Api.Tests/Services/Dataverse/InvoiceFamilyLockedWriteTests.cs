using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using static Sprk.Bff.Api.Services.Dataverse.InvoiceFamilyLockedWrite;

namespace Sprk.Bff.Api.Tests.Services.Dataverse;

/// <summary>
/// #2799 (unified-access-control-r2): <see cref="InvoiceFamilyLockedWrite"/> — the invoice family's locked columns, checked AS
/// THE PERSON and written AS THE APPLICATION (G5). Must-fire: a person without Write (or Append) on the row, or without
/// AppendTo on the record a locked lookup now names, is refused with nothing written; an unanswered question is refused
/// with nothing written; an unknown id answers exactly as a record the person may not use. Must-not-fire: an allowed write
/// lands, its locked columns written by the application; a write with no locked column, and an application run, keep the
/// single write they had.
/// </summary>
public class InvoiceFamilyLockedWriteTests
{
    private static readonly Guid Person = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid InvoiceId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OtherInvoiceId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid LineId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid MatterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UnknownId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private const AccessRights RowRights = AccessRights.Read | AccessRights.Write | AccessRights.Append;
    private const AccessRights TargetRights = AccessRights.Read | AccessRights.AppendTo;

    /// <summary>The rights the person holds; a record not listed does not exist (None, as RetrievePrincipalAccess's 404).</summary>
    private sealed class Rights
    {
        private readonly Dictionary<(string, Guid), AccessRights?> _answers = new();
        public List<(string EntitySet, Guid Id)> Asked { get; } = new();

        public Rights On(string entitySet, Guid id, AccessRights? rights)
        {
            _answers[(entitySet, id)] = rights;
            return this;
        }

        public RightsQuestion Question => (entitySet, id, _) =>
        {
            Asked.Add((entitySet, id));
            return Task.FromResult(_answers.TryGetValue((entitySet, id), out var r) ? r : AccessRights.None);
        };
    }

    /// <summary>Every write sent: its fields and whether it ran as a person (the impersonated id) or the app (null).</summary>
    private static (Mock<IFieldMappingDataverseService> Records, List<(Dictionary<string, object?> Fields, Guid? As)> Writes) Records()
    {
        var writes = new List<(Dictionary<string, object?>, Guid?)>();
        var records = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        records.Setup(r => r.UpdateExistingRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object?>, Guid?, CancellationToken>((_, _, fields, asWho, _) =>
                writes.Add((new Dictionary<string, object?>(fields), asWho)))
            .Returns(Task.CompletedTask);
        return (records, writes);
    }

    private static Task<string?> Write(
        Mock<IFieldMappingDataverseService> records, string table, Guid id, Dictionary<string, object?> fields, Guid? asPerson, Rights rights) =>
        InvoiceFamilyLockedWrite.UpdateExistingSplitAsync(
            records.Object, table, id, fields, asPerson, notFoundAsReadFailed: asPerson is not null, rights.Question, CancellationToken.None);

    // ── The locked set ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LockedColumns_AreEveryLookupTheGuardDecidesAWriteOn_OverEveryFamilyTable()
    {
        var fromGuard = InvoiceFamilyCoreRecordGuard.CoreRecordLookups.Keys
            .SelectMany(t => InvoiceFamilyCoreRecordGuard.GuardedLookups(t)!.Select(g => $"{t}.{g.Column}->{g.Table}".ToLowerInvariant()))
            .OrderBy(s => s, StringComparer.Ordinal);

        LockedColumns.Select(c => $"{c.Table}.{c.Column}->{c.TargetTable}").Should().Equal(fromGuard);

        // The set the owner approved (main session, 2026-11-13): the core lookups plus every invoice link the guard compares.
        LockedColumns.Select(c => $"{c.Table}.{c.Column}").Should().BeEquivalentTo(
            "sprk_invoice.sprk_matter", "sprk_invoice.sprk_project", "sprk_invoice.sprk_regardingworkassignment", "sprk_invoice.sprk_supersedesinvoice",
            "sprk_billingevent.sprk_matter", "sprk_billingevent.sprk_project", "sprk_billingevent.sprk_regardingworkassignment", "sprk_billingevent.sprk_invoice",
            "sprk_invoicesubmission.sprk_regardingmatter", "sprk_invoicesubmission.sprk_regardingproject", "sprk_invoicesubmission.sprk_regardingworkassignment", "sprk_invoicesubmission.sprk_invoice",
            "sprk_invoiceapproval.sprk_regardingmatter", "sprk_invoiceapproval.sprk_regardingproject", "sprk_invoiceapproval.sprk_regardingworkassignment", "sprk_invoiceapproval.sprk_invoice",
            "sprk_invoicecorrection.sprk_regardingmatter", "sprk_invoicecorrection.sprk_regardingproject", "sprk_invoicecorrection.sprk_regardingworkassignment", "sprk_invoicecorrection.sprk_invoice",
            "sprk_externalidentifier.sprk_regardingmatter", "sprk_externalidentifier.sprk_regardingproject", "sprk_externalidentifier.sprk_regardingworkassignment", "sprk_externalidentifier.sprk_regardinginvoice");
    }

    [Fact]
    public void EntitySets_NameEveryLockedTableAndEveryTableALockedLookupNames()
    {
        LockedColumns.SelectMany(c => new[] { c.Table, c.TargetTable }).Distinct()
            .Should().OnlyContain(t => EntitySets.ContainsKey(t));
    }

    [Fact]
    public void Split_SetsAsideLockedColumnsAndTheirBinds_KeepsEverythingElse()
    {
        var (locked, others) = Split("sprk_billingevent", new Dictionary<string, object?>
        {
            ["sprk_Invoice@odata.bind"] = $"/sprk_invoices({InvoiceId})",
            ["sprk_matter"] = null,
            ["sprk_description"] = "x",
        });

        locked.Keys.Should().BeEquivalentTo("sprk_Invoice@odata.bind", "sprk_matter");
        others.Keys.Should().BeEquivalentTo("sprk_description");
        Split("sprk_todo", new Dictionary<string, object?> { ["sprk_regardingmatter"] = null }).Locked.Should().BeEmpty();
    }

    [Fact]
    public void SplitBody_SetsAsideLockedColumns_AndReturnsTheRestAsJson()
    {
        var (locked, rest) = SplitBody("sprk_invoice",
            $"{{\"sprk_Matter@odata.bind\":\"/sprk_matters({MatterId})\",\"sprk_name\":\"n\"}}");

        locked.Keys.Should().BeEquivalentTo("sprk_Matter@odata.bind");
        rest.Should().Be("{\"sprk_name\":\"n\"}");
        SplitBody("sprk_invoice", "{\"sprk_supersedesinvoice\":null}").RestBody.Should().BeNull();
    }

    // ── Must fire: refused, nothing written ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PersonWithoutWriteOnTheRow_IsRefused_NothingWritten()
    {
        var (records, writes) = Records();
        var rights = new Rights()
            .On("sprk_billingevents", LineId, AccessRights.Read | AccessRights.Append)
            .On("sprk_invoices", InvoiceId, TargetRights);

        var code = await Write(records, "sprk_billingevent", LineId,
            new() { ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId) }, Person, rights);

        code.Should().Be(LockedWriteDeniedCode);
        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task PersonWithoutAppendOnTheRow_SettingALookup_IsRefused_NothingWritten()
    {
        var (records, writes) = Records();
        var rights = new Rights()
            .On("sprk_billingevents", LineId, AccessRights.Read | AccessRights.Write)
            .On("sprk_invoices", InvoiceId, TargetRights);

        var code = await Write(records, "sprk_billingevent", LineId,
            new() { ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId) }, Person, rights);

        code.Should().Be(LockedWriteDeniedCode);
        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task PersonWithoutAppendToOnTheNewInvoice_IsRefused_NothingWritten()
    {
        var (records, writes) = Records();
        var rights = new Rights()
            .On("sprk_billingevents", LineId, RowRights)
            .On("sprk_invoices", InvoiceId, AccessRights.Read);

        var code = await Write(records, "sprk_billingevent", LineId,
            new() { ["sprk_Invoice@odata.bind"] = $"/sprk_invoices({InvoiceId})", ["sprk_description"] = "d" }, Person, rights);

        code.Should().Be(LockedWriteDeniedCode);
        writes.Should().BeEmpty("the person's other columns are not written either when the locked ones are refused");
    }

    [Fact]
    public async Task PersonWithoutAppendToOnTheNewCoreRecord_IsRefused_NothingWritten()
    {
        var (records, writes) = Records();
        var rights = new Rights()
            .On("sprk_invoices", InvoiceId, RowRights)
            .On("sprk_matters", MatterId, AccessRights.Read | AccessRights.Write);

        var code = await Write(records, "sprk_invoice", InvoiceId,
            new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) }, Person, rights);

        code.Should().Be(LockedWriteDeniedCode);
        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task UnansweredQuestionOnTheRow_IsReadFailed_NothingWritten()
    {
        var (records, writes) = Records();
        var rights = new Rights()
            .On("sprk_invoices", InvoiceId, null)
            .On("sprk_invoices", OtherInvoiceId, TargetRights);

        var code = await Write(records, "sprk_invoice", InvoiceId,
            new() { ["sprk_supersedesinvoice"] = new EntityReference("sprk_invoice", OtherInvoiceId) }, Person, rights);

        code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task UnansweredQuestionOnTheNamedRecord_IsReadFailed_NothingWritten()
    {
        var (records, writes) = Records();
        var rights = new Rights()
            .On("sprk_invoices", InvoiceId, RowRights)
            .On("sprk_invoices", OtherInvoiceId, null);

        var code = await Write(records, "sprk_invoice", InvoiceId,
            new() { ["sprk_supersedesinvoice"] = new EntityReference("sprk_invoice", OtherInvoiceId), ["sprk_name"] = "n" }, Person, rights);

        code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task NoProbe_EveryQuestionIsUnanswered_NothingWritten()
    {
        var (records, writes) = Records();

        var code = await InvoiceFamilyLockedWrite.UpdateExistingSplitAsync(
            records.Object, "sprk_invoice", InvoiceId, new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) },
            Person, notFoundAsReadFailed: true, RightsOfPrincipal(probe: null, Person), CancellationToken.None);

        code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownRecord_AnswersExactlyAsARecordThePersonMayNotUse()
    {
        // The new invoice exists but the person may not append to it, versus an id that names no row (None, as a 404).
        var (records, writes) = Records();
        var rights = new Rights()
            .On("sprk_billingevents", LineId, RowRights)
            .On("sprk_invoices", OtherInvoiceId, AccessRights.Read);

        var unappendable = await Write(records, "sprk_billingevent", LineId,
            new() { ["sprk_invoice"] = new EntityReference("sprk_invoice", OtherInvoiceId) }, Person, rights);
        var unknown = await Write(records, "sprk_billingevent", LineId,
            new() { ["sprk_invoice"] = new EntityReference("sprk_invoice", UnknownId) }, Person, rights);

        unknown.Should().Be(unappendable).And.Be(LockedWriteDeniedCode);
        InvoiceFamilyCoreRecordGuard.RefusalException("sprk_billingevent", unknown!).Message
            .Should().Be(InvoiceFamilyCoreRecordGuard.RefusalException("sprk_billingevent", unappendable!).Message);
        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownRow_IsRefused_NothingWritten()
    {
        var (records, writes) = Records();
        var rights = new Rights().On("sprk_matters", MatterId, TargetRights);

        var code = await Write(records, "sprk_invoice", UnknownId,
            new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) }, Person, rights);

        code.Should().Be(LockedWriteDeniedCode);
        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task LockedValueThatNamesNoRecord_IsRefused_NothingWrittenOrAsked()
    {
        var (records, writes) = Records();
        var rights = new Rights().On("sprk_billingevents", LineId, RowRights);

        var code = await Write(records, "sprk_billingevent", LineId,
            new() { ["sprk_Invoice@odata.bind"] = "not a bind" }, Person, rights);

        code.Should().Be(LockedWriteDeniedCode);
        writes.Should().BeEmpty();
        rights.Asked.Should().BeEmpty();
    }

    // ── Must not fire: written, and by the right identity ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AllowedLockedOnlyWrite_IsWrittenByTheApplication_NotAsThePerson()
    {
        var (records, writes) = Records();
        var rights = new Rights()
            .On("sprk_billingevents", LineId, RowRights)
            .On("sprk_invoices", InvoiceId, TargetRights);

        var code = await Write(records, "sprk_billingevent", LineId,
            new() { ["sprk_Invoice@odata.bind"] = $"/sprk_invoices({InvoiceId})" }, Person, rights);

        code.Should().BeNull();
        writes.Should().ContainSingle();
        writes[0].As.Should().BeNull("the application writes the locked column");
        writes[0].Fields.Keys.Should().BeEquivalentTo("sprk_Invoice@odata.bind");
        rights.Asked.Should().Equal(("sprk_billingevents", LineId), ("sprk_invoices", InvoiceId));
    }

    [Fact]
    public async Task AllowedMixedWrite_OtherColumnsAsThePersonFirst_ThenLockedAsTheApplication()
    {
        var (records, writes) = Records();
        var rights = new Rights()
            .On("sprk_invoicecorrections", LineId, RowRights)
            .On("sprk_matters", MatterId, TargetRights);

        var code = await Write(records, "sprk_invoicecorrection", LineId,
            new() { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId), ["sprk_reason"] = "r" }, Person, rights);

        code.Should().BeNull();
        writes.Should().HaveCount(2);
        writes[0].As.Should().Be(Person);
        writes[0].Fields.Keys.Should().BeEquivalentTo("sprk_reason");
        writes[1].As.Should().BeNull();
        writes[1].Fields.Keys.Should().BeEquivalentTo("sprk_regardingmatter");
    }

    [Fact]
    public async Task WriteWithNoLockedColumn_KeepsThePersonsSingleWrite_NothingAsked()
    {
        var (records, writes) = Records();
        var rights = new Rights();

        var code = await Write(records, "sprk_invoice", InvoiceId, new() { ["sprk_name"] = "n" }, Person, rights);

        code.Should().BeNull();
        writes.Should().ContainSingle().Which.As.Should().Be(Person);
        rights.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplicationRun_KeepsItsSingleWrite_NothingAsked()
    {
        var (records, writes) = Records();
        var rights = new Rights();

        var code = await Write(records, "sprk_invoice", InvoiceId,
            new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId), ["sprk_name"] = "n" }, asPerson: null, rights);

        code.Should().BeNull();
        writes.Should().ContainSingle();
        writes[0].As.Should().BeNull();
        writes[0].Fields.Keys.Should().BeEquivalentTo("sprk_matter", "sprk_name");
        rights.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task AClear_NeedsWriteOnTheRowOnly_NoRecordAsked()
    {
        var (records, writes) = Records();
        var rights = new Rights().On("sprk_invoices", InvoiceId, AccessRights.Read | AccessRights.Write);

        var code = await Write(records, "sprk_invoice", InvoiceId, new() { ["sprk_supersedesinvoice"] = null }, Person, rights);

        code.Should().BeNull();
        writes.Should().ContainSingle().Which.As.Should().BeNull();
        rights.Asked.Should().Equal(("sprk_invoices", InvoiceId));
    }

    [Fact]
    public async Task PersonsOwnWriteFindsTheRowGone_NothingLockedWritten()
    {
        var records = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        records.Setup(r => r.UpdateExistingRecordFieldsAsync("sprk_invoice", InvoiceId, It.IsAny<Dictionary<string, object?>>(), Person, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());
        var rights = new Rights()
            .On("sprk_invoices", InvoiceId, RowRights)
            .On("sprk_matters", MatterId, TargetRights);

        var code = await Write(records, "sprk_invoice", InvoiceId,
            new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId), ["sprk_name"] = "n" }, Person, rights);

        code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        records.Verify(r => r.UpdateExistingRecordFieldsAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckFromAJsonBody_ReadsJsonBinds()
    {
        var rights = new Rights()
            .On("sprk_externalidentifiers", LineId, RowRights)
            .On("sprk_invoices", InvoiceId, TargetRights);
        var (locked, _) = SplitBody("sprk_externalidentifier",
            $"{{\"sprk_RegardingInvoice@odata.bind\":\"/sprk_invoices({InvoiceId})\",\"sprk_value\":\"v\"}}");

        (await CheckPersonMayWriteAsync("sprk_externalidentifier", LineId, locked, rights.Question, CancellationToken.None))
            .Should().Be(LockedWriteCheck.Allowed);
        rights.Asked.Should().Equal(("sprk_externalidentifiers", LineId), ("sprk_invoices", InvoiceId));
    }

    [Fact]
    public async Task RightsOfPrincipal_AsksTheProbeAboutThatPerson()
    {
        var probe = new Mock<CallerRecordAccessProbe>(
            MockBehavior.Strict,
            new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance, null!, null!);
        probe.Setup(p => p.GetPrincipalRightsAppOnlyOrUnknownAsync(Person, "sprk_invoices", InvoiceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Write);

        (await RightsOfPrincipal(probe.Object, Person)("sprk_invoices", InvoiceId, CancellationToken.None))
            .Should().Be(AccessRights.Write);
    }
}
