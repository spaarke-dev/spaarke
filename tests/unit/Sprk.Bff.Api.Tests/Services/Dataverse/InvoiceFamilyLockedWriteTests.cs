using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
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
        var records = PersonIs(new Mock<IFieldMappingDataverseService>(MockBehavior.Strict), EnabledReadWrite);
        records.Setup(r => r.UpdateExistingRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object?>, Guid?, CancellationToken>((_, _, fields, asWho, _) =>
                writes.Add((new Dictionary<string, object?>(fields), asWho)))
            .Returns(Task.CompletedTask);
        return (records, writes);
    }

    /// <summary>Review 2 (R2-1): the person's own <c>systemuser</c> row as the app-only read answers it — enabled, Read-Write.</summary>
    private static Dictionary<string, object?> EnabledReadWrite => new() { ["isdisabled"] = false, ["accessmode"] = 0L };

    /// <summary>Every <c>systemuser</c> state read the check made (the id named).</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Mock<IFieldMappingDataverseService>, List<Guid>> StateReads = new();

    /// <summary>
    /// Review 2 (R2-1): the app-only read of the person's state (<c>systemusers({id})?$select=isdisabled,accessmode</c>) answers
    /// <paramref name="row"/> (an empty row: no such user, as the read answers a 404), or fails as <paramref name="fault"/>.
    /// </summary>
    private static Mock<IFieldMappingDataverseService> PersonIs(
        Mock<IFieldMappingDataverseService> records, Dictionary<string, object?> row, Exception? fault = null)
    {
        var reads = StateReads.GetOrCreateValue(records);
        records.Setup(r => r.RetrieveRecordFieldsAsync("systemuser", It.IsAny<Guid>(),
                It.Is<string[]>(c => c.SequenceEqual(PersonStateColumns)), It.IsAny<CancellationToken>()))
            .Returns<string, Guid, string[], CancellationToken>((_, id, _, _) =>
            {
                reads.Add(id);
                return fault is null ? Task.FromResult(new Dictionary<string, object?>(row)) : Task.FromException<Dictionary<string, object?>>(fault);
            });
        return records;
    }

    private static Task<string?> Write(
        Mock<IFieldMappingDataverseService> records, string table, Guid id, Dictionary<string, object?> fields, Guid? asPerson, Rights rights) =>
        InvoiceFamilyLockedWrite.UpdateExistingSplitAsync(
            records.Object, table, id, fields, asPerson, notFoundAsReadFailed: asPerson is not null, rights.Question,
            NullLogger.Instance, CancellationToken.None);

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
            Person, notFoundAsReadFailed: true, RightsOfPrincipal(probe: null, Person), NullLogger.Instance, CancellationToken.None);

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

    // ── Review 1 (F3): a key the guard does not read as the locked column is refused (fail closed) ──────────────────

    [Theory]
    [InlineData("_sprk_invoice_value")] // the read form of the lookup
    [InlineData("sprk_invoice@foo")]    // any other annotation
    [InlineData("sprk_invoice@odata.bind@odata.bind")]
    public async Task LockedColumnInAKeyFormTheGuardDoesNotRead_IsRefused_NothingWrittenOrAsked(string key)
    {
        var (records, writes) = Records();
        var rights = new Rights()
            .On("sprk_billingevents", LineId, RowRights)
            .On("sprk_invoices", InvoiceId, TargetRights);

        var code = await Write(records, "sprk_billingevent", LineId,
            new() { [key] = new EntityReference("sprk_invoice", InvoiceId), ["sprk_description"] = "d" }, Person, rights);

        code.Should().Be(LockedWriteDeniedCode);
        writes.Should().BeEmpty("neither the person's columns nor the locked one are written");
        rights.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task Caller_LockedColumnInAKeyFormTheGuardDoesNotRead_IsDenied_NothingWritten()
    {
        var person = new PersonClient().Rights(InvoiceId, "ReadAccess, WriteAccess, AppendAccess").Rights(MatterId, "AppendToAccess");
        var (app, writes) = Records();

        (await PatchAsCaller(person, app, "{\"_sprk_matter_value\":null,\"sprk_name\":\"n\"}")).Kind.Should().Be(CallerPatchKind.Denied);

        person.Patches.Should().BeEmpty();
        writes.Should().BeEmpty();
    }

    /// <summary>
    /// The forms the guard reads as the column (<see cref="InvoiceFamilyCoreRecordGuard.ColumnOfKey"/>): the logical name and
    /// <c>{navigation}@odata.bind</c>. The guard compares both parts without regard to case (it lower-cases the column and
    /// matches the suffix ignoring case), so a differently cased bind is the guard's column too and is checked, not refused here.
    /// </summary>
    [Theory]
    [InlineData("sprk_invoice", false)]
    [InlineData("sprk_Invoice@odata.bind", true)]
    [InlineData("SPRK_Invoice@odata.bind", true)]
    public async Task LockedColumnInAKeyFormTheGuardReads_IsChecked_AndWrittenByTheApplication(string key, bool bind)
    {
        var (records, writes) = Records();
        var rights = new Rights()
            .On("sprk_billingevents", LineId, RowRights)
            .On("sprk_invoices", InvoiceId, TargetRights);
        object value = bind ? $"/sprk_invoices({InvoiceId})" : new EntityReference("sprk_invoice", InvoiceId);

        var code = await Write(records, "sprk_billingevent", LineId, new() { [key] = value }, Person, rights);

        code.Should().BeNull();
        writes.Should().ContainSingle().Which.As.Should().BeNull();
        rights.Asked.Should().Equal(("sprk_billingevents", LineId), ("sprk_invoices", InvoiceId));
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
        var records = PersonIs(new Mock<IFieldMappingDataverseService>(MockBehavior.Strict), EnabledReadWrite);
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

        (await CheckPersonMayWriteAsync("sprk_externalidentifier", LineId, locked, _ => Task.FromResult(LockedWriteCheck.Allowed),
                rights.Question, CancellationToken.None))
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

    // ── Decision 3 (K-1): the application's write fails after the person's landed — never success ─────────────────────

    /// <summary>A recorder whose application write (no impersonation) fails as <paramref name="fault"/>; the person's lands.</summary>
    private static (Mock<IFieldMappingDataverseService> Records, List<Guid?> Writes) AppWriteFails(Exception fault)
    {
        var writes = new List<Guid?>();
        var records = PersonIs(new Mock<IFieldMappingDataverseService>(MockBehavior.Strict), EnabledReadWrite);
        records.Setup(r => r.UpdateExistingRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Returns<string, Guid, Dictionary<string, object?>, Guid?, CancellationToken>((_, _, _, asWho, _) =>
            {
                writes.Add(asWho);
                return asWho is null ? Task.FromException(fault) : Task.CompletedTask;
            });
        return (records, writes);
    }

    [Theory]
    [InlineData("fault")]
    [InlineData("gone")]
    public async Task MixedWrite_ApplicationWriteFailsAfterThePersons_IsPartlySaved_NeverSuccess_LoggedWithTheRecord(string how)
    {
        var (records, writes) = AppWriteFails(how == "gone" ? new KeyNotFoundException() : new HttpRequestException("secret detail"));
        var rights = new Rights().On("sprk_invoices", InvoiceId, RowRights).On("sprk_matters", MatterId, TargetRights);
        var logger = new CapturingLogger();

        var code = await InvoiceFamilyLockedWrite.UpdateExistingSplitAsync(
            records.Object, "sprk_invoice", InvoiceId,
            new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId), ["sprk_name"] = "n" },
            Person, notFoundAsReadFailed: true, rights.Question, logger, CancellationToken.None, traceId: "trace-1");

        code.Should().Be(LockedWritePartlySavedCode);
        writes.Should().Equal(Person, null);
        var warning = logger.Entries.Should().ContainSingle(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning).Subject;
        warning.Message.Should().Contain(InvoiceId.ToString()).And.Contain("trace-1").And.NotContain("secret detail");
        RefusalException("sprk_invoice", code!).Message.Should().Be(PartlySavedMessage).And.NotContain("secret detail");
    }

    [Fact]
    public async Task LockedOnlyWrite_ApplicationWriteFaults_PropagatesAsBefore_NothingElseWritten()
    {
        var (records, writes) = AppWriteFails(new HttpRequestException("boom"));
        var rights = new Rights().On("sprk_invoices", InvoiceId, RowRights).On("sprk_matters", MatterId, TargetRights);

        var act = () => Write(records, "sprk_invoice", InvoiceId,
            new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) }, Person, rights);

        await act.Should().ThrowAsync<HttpRequestException>();
        writes.Should().Equal(new Guid?[] { null });
    }

    // ── Review 1 (F4): optimistic concurrency — the row version is not a person's column ─────────────────────────────────

    /// <summary><see cref="Records"/> plus the conditional application write, recorded with its version (or failing as <paramref name="fault"/>).</summary>
    private static (Mock<IFieldMappingDataverseService> Records, List<(Dictionary<string, object?> Fields, Guid? As)> Writes,
        List<(Dictionary<string, object?> Fields, long Version)> Conditional) RecordsWithVersion(Exception? fault = null)
    {
        var (records, writes) = Records();
        var conditional = new List<(Dictionary<string, object?>, long)>();
        records.Setup(r => r.UpdateRecordFieldsIfUnchangedAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns<string, Guid, Dictionary<string, object?>, long, CancellationToken>((_, _, fields, version, _) =>
            {
                conditional.Add((new Dictionary<string, object?>(fields), version));
                return fault is null ? Task.CompletedTask : Task.FromException(fault);
            });
        return (records, writes, conditional);
    }

    private static Task<string?> WriteOnVersion(Mock<IFieldMappingDataverseService> records, Dictionary<string, object?> fields, long version) =>
        InvoiceFamilyLockedWrite.UpdateExistingSplitAsync(
            records.Object, "sprk_invoice", InvoiceId, fields, Person, notFoundAsReadFailed: true,
            new Rights().On("sprk_invoices", InvoiceId, RowRights).On("sprk_matters", MatterId, TargetRights).Question,
            NullLogger.Instance, CancellationToken.None, expectedVersion: version);

    [Fact]
    public async Task Optimistic_PersonsPartIsTheVersionAlone_NoPersonWrite_OneApplicationWriteOnThatVersion()
    {
        var (records, writes, conditional) = RecordsWithVersion();

        var code = await WriteOnVersion(records,
            new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId), [RowVersionColumn] = 42L }, 42L);

        code.Should().BeNull();
        writes.Should().BeEmpty("a write of the row version alone is no write of the person's");
        conditional.Should().ContainSingle();
        conditional[0].Version.Should().Be(42L);
        conditional[0].Fields.Keys.Should().BeEquivalentTo("sprk_matter");
    }

    [Fact]
    public async Task Optimistic_VersionOnly_RowChangedSince_IsTheConflict_NothingWritten()
    {
        var (records, writes, _) = RecordsWithVersion(new System.Data.DBConcurrencyException("changed"));

        var act = () => WriteOnVersion(records,
            new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId), [RowVersionColumn] = 42L }, 42L);

        await act.Should().ThrowAsync<System.Data.DBConcurrencyException>();
        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Optimistic_VersionOnly_ApplicationWriteFaults_IsNeverPartlySaved()
    {
        var (records, writes, _) = RecordsWithVersion(new HttpRequestException("boom"));

        var act = () => WriteOnVersion(records,
            new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId), [RowVersionColumn] = 42L }, 42L);

        await act.Should().ThrowAsync<HttpRequestException>("nothing else was saved, so the answer is not \"partly saved\"");
        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Optimistic_PersonsPartHasOtherColumns_ThePersonsWriteCarriesTheVersion_ThenTheApplications()
    {
        var (records, writes, conditional) = RecordsWithVersion();

        var code = await WriteOnVersion(records,
            new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId), ["sprk_name"] = "n", [RowVersionColumn] = 42L }, 42L);

        code.Should().BeNull();
        conditional.Should().BeEmpty();
        writes.Should().HaveCount(2);
        writes[0].As.Should().Be(Person);
        writes[0].Fields.Should().Contain(RowVersionColumn, 42L).And.ContainKey("sprk_name").And.HaveCount(2);
        writes[1].As.Should().BeNull();
        writes[1].Fields.Keys.Should().BeEquivalentTo("sprk_matter");
    }

    // ── Review 1 (F1): once the person's write landed, the request cannot cancel the application's ─────────────────────

    /// <summary>
    /// A recorder that cancels <paramref name="request"/> when the person's write lands, and whose application write fails as a
    /// real client would when its token is cancelled.
    /// </summary>
    private static (Mock<IFieldMappingDataverseService> Records, List<Guid?> Writes) CancelledAfterThePersonsWrite(CancellationTokenSource request)
    {
        var writes = new List<Guid?>();
        var records = PersonIs(new Mock<IFieldMappingDataverseService>(MockBehavior.Strict), EnabledReadWrite);
        records.Setup(r => r.UpdateExistingRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Returns<string, Guid, Dictionary<string, object?>, Guid?, CancellationToken>((_, _, _, asWho, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                writes.Add(asWho);
                if (asWho is not null)
                    request.Cancel(); // the client goes away once the person's write has landed
                return Task.CompletedTask;
            });
        return (records, writes);
    }

    [Fact]
    public async Task MixedWrite_RequestCancelledAfterThePersonsWrite_TheApplicationsWriteStillRuns()
    {
        using var request = new CancellationTokenSource();
        var (records, writes) = CancelledAfterThePersonsWrite(request);
        var rights = new Rights().On("sprk_invoices", InvoiceId, RowRights).On("sprk_matters", MatterId, TargetRights);

        var code = await InvoiceFamilyLockedWrite.UpdateExistingSplitAsync(
            records.Object, "sprk_invoice", InvoiceId,
            new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId), ["sprk_name"] = "n" },
            Person, notFoundAsReadFailed: true, rights.Question, NullLogger.Instance, request.Token);

        code.Should().BeNull("the person's write landed, so the locked columns are written whatever the request does");
        writes.Should().Equal(Person, null);
    }

    [Fact]
    public async Task Caller_RequestCancelledAfterThePersonsPatch_TheApplicationsWriteStillRuns()
    {
        using var request = new CancellationTokenSource();
        var person = new PersonClient { OnPatch = request.Cancel }
            .Rights(InvoiceId, "ReadAccess, WriteAccess, AppendAccess").Rights(MatterId, "AppendToAccess");
        var (app, writes) = CancelledAfterThePersonsWrite(request);

        var answer = await PatchAsCallerAsync(person, app.Object, "sprk_invoice", "sprk_invoices", InvoiceId, LockedAndOtherBody,
            new CapturingLogger(), "trace-1", request.Token);

        answer.Kind.Should().Be(CallerPatchKind.Written);
        person.Patches.Should().ContainSingle();
        writes.Should().Equal(new Guid?[] { null });
    }

    [Fact]
    public void RefusalException_ForAGuardCode_IsTheGuardsCodeOnlyRefusal()
    {
        RefusalException("sprk_invoice", LockedWriteDeniedCode).Message
            .Should().Be(InvoiceFamilyCoreRecordGuard.CodeOnlyRefusalMessage(LockedWriteDeniedCode));
    }

    // ── Review 2 (R2-1): the person's own state — enabled, an access mode that writes ─────────────────────────────────────

    private static Rights AllAllowed() =>
        new Rights().On("sprk_invoices", InvoiceId, RowRights).On("sprk_matters", MatterId, TargetRights);

    private static Dictionary<string, object?> LockedOnly => new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId) };

    [Fact]
    public async Task PersonState_Disabled_IsDenied_NothingWrittenOrAsked()
    {
        var (records, writes) = Records();
        PersonIs(records, new() { ["isdisabled"] = true, ["accessmode"] = 0L });
        var rights = AllAllowed();

        (await Write(records, "sprk_invoice", InvoiceId, LockedOnly, Person, rights)).Should().Be(LockedWriteDeniedCode);

        writes.Should().BeEmpty();
        rights.Asked.Should().BeEmpty("the person's state is asked first");
        StateReads.GetOrCreateValue(records).Should().Equal(Person);
    }

    [Theory]
    [InlineData(1L)] // Administrative
    [InlineData(2L)] // Read
    [InlineData(3L)] // Support User
    [InlineData(5L)] // Delegated Admin
    public async Task PersonState_AnAccessModeThatDoesNotWrite_IsDenied_NothingWritten(long accessMode)
    {
        var (records, writes) = Records();
        PersonIs(records, new() { ["isdisabled"] = false, ["accessmode"] = accessMode });

        (await Write(records, "sprk_invoice", InvoiceId, LockedOnly, Person, AllAllowed())).Should().Be(LockedWriteDeniedCode);

        writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0L)] // Read-Write
    [InlineData(4L)] // Non-interactive
    public async Task PersonState_EnabledWithAWritingAccessMode_IsAllowed_AskedOncePerWrite(long accessMode)
    {
        var (records, writes) = Records();
        PersonIs(records, new() { ["isdisabled"] = false, ["accessmode"] = accessMode });
        var rights = new Rights().On("sprk_invoices", InvoiceId, RowRights)
            .On("sprk_matters", MatterId, TargetRights).On("sprk_invoices", OtherInvoiceId, TargetRights);

        // Two locked columns, one write: the state is read once.
        (await Write(records, "sprk_invoice", InvoiceId,
                new() { ["sprk_matter"] = new EntityReference("sprk_matter", MatterId), ["sprk_supersedesinvoice"] = new EntityReference("sprk_invoice", OtherInvoiceId) },
                Person, rights))
            .Should().BeNull();

        writes.Should().ContainSingle().Which.As.Should().BeNull();
        StateReads.GetOrCreateValue(records).Should().Equal(Person);
    }

    [Fact]
    public async Task PersonState_NoSuchUser_IsDenied_ByteIdenticalToAnUnknownRecord()
    {
        var (missingUser, missingUserWrites) = Records();
        PersonIs(missingUser, new()); // the app-only read answers no columns for a 404
        var (unknownRecord, unknownRecordWrites) = Records();
        var unknownRights = new Rights().On("sprk_invoices", InvoiceId, RowRights); // UnknownId: no rights (404)
        var unknownFields = new Dictionary<string, object?> { ["sprk_matter"] = new EntityReference("sprk_matter", UnknownId) };

        var a = await Write(missingUser, "sprk_invoice", InvoiceId, LockedOnly, Person, AllAllowed());
        var b = await Write(unknownRecord, "sprk_invoice", InvoiceId, unknownFields, Person, unknownRights);

        a.Should().Be(b).And.Be(LockedWriteDeniedCode);
        RefusalException("sprk_invoice", a!).Message.Should().Be(RefusalException("sprk_invoice", b!).Message);
        missingUserWrites.Should().BeEmpty();
        unknownRecordWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, 0L)]      // isdisabled unreadable
    [InlineData(false, null)]   // accessmode unreadable
    [InlineData("false", 0L)]   // not a boolean
    public async Task PersonState_AnUnreadableValue_IsDenied(object? isDisabled, object? accessMode)
    {
        var (records, writes) = Records();
        PersonIs(records, new() { ["isdisabled"] = isDisabled, ["accessmode"] = accessMode });

        (await Write(records, "sprk_invoice", InvoiceId, LockedOnly, Person, AllAllowed())).Should().Be(LockedWriteDeniedCode);
        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task PersonState_ReadFaults_IsUnavailable_NothingWrittenOrAsked()
    {
        var (records, writes) = Records();
        PersonIs(records, EnabledReadWrite, new HttpRequestException("503 at 10.0.0.9"));
        var rights = AllAllowed();
        var logger = new CapturingLogger();

        var code = await InvoiceFamilyLockedWrite.UpdateExistingSplitAsync(
            records.Object, "sprk_invoice", InvoiceId, LockedOnly, Person, notFoundAsReadFailed: true, rights.Question, logger,
            CancellationToken.None);

        code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        writes.Should().BeEmpty();
        rights.Asked.Should().BeEmpty();
        logger.Entries.Should().ContainSingle(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning)
            .Which.Message.Should().NotContain("10.0.0.9");
    }

    [Fact]
    public async Task PersonState_NotAskedWithoutALockedColumn_OrOnAnApplicationRun()
    {
        var (records, writes) = Records();
        PersonIs(records, new() { ["isdisabled"] = true, ["accessmode"] = 0L });

        (await Write(records, "sprk_invoice", InvoiceId, new() { ["sprk_name"] = "n" }, Person, AllAllowed())).Should().BeNull();
        (await Write(records, "sprk_invoice", InvoiceId, LockedOnly, asPerson: null, AllAllowed())).Should().BeNull();

        writes.Select(w => w.As).Should().Equal(Person, null);
        StateReads.GetOrCreateValue(records).Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, 0L, false)]
    [InlineData(false, 2L, false)]
    [InlineData(false, 1L, false)]
    [InlineData(false, 0L, true)]
    [InlineData(false, 4L, true)]
    public async Task Caller_PersonState_DecidesBeforeTheRights(bool disabled, long accessMode, bool allowed)
    {
        var person = new PersonClient().Rights(InvoiceId, "ReadAccess, WriteAccess, AppendAccess").Rights(MatterId, "AppendToAccess");
        var (app, writes) = Records();
        PersonIs(app, new() { ["isdisabled"] = disabled, ["accessmode"] = accessMode });

        (await PatchAsCaller(person, app, LockedAndOtherBody)).Kind.Should().Be(allowed ? CallerPatchKind.Written : CallerPatchKind.Denied);

        StateReads.GetOrCreateValue(app).Should().Equal(Person);
        if (!allowed)
        {
            person.Patches.Should().BeEmpty();
            person.AccessAsked.Should().Be(0);
            writes.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Caller_PersonState_NoSuchUser_IsDenied_AsAnUnknownRecord_ReadFault_IsUnavailable()
    {
        var person = new PersonClient().Rights(InvoiceId, "ReadAccess, WriteAccess, AppendAccess").Rights(MatterId, "AppendToAccess");
        var (missing, missingWrites) = Records();
        PersonIs(missing, new());
        var unknown = new PersonClient().Rights(InvoiceId, "ReadAccess, WriteAccess, AppendAccess"); // UnknownId: 404
        var (app, _) = Records();

        var a = await PatchAsCaller(person, missing, LockedAndOtherBody);
        var b = await PatchAsCaller(unknown, app, $"{{\"sprk_Matter@odata.bind\":\"/sprk_matters({UnknownId})\",\"sprk_name\":\"n\"}}");
        a.Should().Be(b);
        a.Kind.Should().Be(CallerPatchKind.Denied);
        missingWrites.Should().BeEmpty();

        var (faulting, faultingWrites) = Records();
        PersonIs(faulting, EnabledReadWrite, new HttpRequestException("boom"));
        (await PatchAsCaller(person, faulting, LockedAndOtherBody)).Kind.Should().Be(CallerPatchKind.Unavailable);
        faultingWrites.Should().BeEmpty();
        person.Patches.Should().BeEmpty();
    }

    // ── The OBO paths: RightsOfCaller and PatchAsCallerAsync ─────────────────────────────────────────────────────────

    /// <summary>A person's own Dataverse client: WhoAmI, RetrievePrincipalAccess answers per record, and every PATCH recorded.</summary>
    internal sealed class PersonClient : IDataverseUserClient
    {
        private readonly Dictionary<Guid, DataverseUserResponse> _access = new();
        public List<(string Path, string Body)> Patches { get; } = new();
        public DataverseUserResponse PatchAnswer { get; set; } = DataverseUserResponse.Ok(204, null);
        public int AccessAsked { get; private set; }

        public PersonClient Rights(Guid id, string rights)
        {
            _access[id] = DataverseUserResponse.Ok(200, JsonDocument.Parse($"{{\"AccessRights\":\"{rights}\"}}").RootElement.Clone());
            return this;
        }

        public PersonClient Status(Guid id, int status)
        {
            _access[id] = DataverseUserResponse.Fail(status, "E", "e");
            return this;
        }

        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
        {
            if (relativePath == "WhoAmI()")
                return Task.FromResult(DataverseUserResponse.Ok(200, JsonDocument.Parse($"{{\"UserId\":\"{Person}\"}}").RootElement.Clone()));

            AccessAsked++;
            relativePath.Should().StartWith($"systemusers({Person:D})/Microsoft.Dynamics.CRM.RetrievePrincipalAccess");
            var target = Uri.UnescapeDataString(relativePath);
            var answer = _access.FirstOrDefault(a => target.Contains(a.Key.ToString("D"), StringComparison.Ordinal));
            return Task.FromResult(answer.Value ?? DataverseUserResponse.Fail(404, "NotFound", "not found"));
        }

        /// <summary>Run after each PATCH is recorded (review 1: the client goes away once it has landed).</summary>
        public Action? OnPatch { get; init; }

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken)
        {
            Patches.Add((relativePath, jsonBody));
            OnPatch?.Invoke();
            return Task.FromResult(PatchAnswer);
        }

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static readonly string LockedAndOtherBody =
        $"{{\"sprk_Matter@odata.bind\":\"/sprk_matters({MatterId})\",\"sprk_name\":\"n\"}}";

    private static Task<CallerPatch> PatchAsCaller(PersonClient person, Mock<IFieldMappingDataverseService>? app, string body,
        string table = "sprk_invoice", Guid? id = null, CapturingLogger? logger = null) =>
        PatchAsCallerAsync(person, app?.Object, table, table + "s", id ?? InvoiceId, body, logger ?? new CapturingLogger(), "trace-1",
            CancellationToken.None);

    [Fact]
    public async Task Caller_NoLockedColumn_IsTheirOwnSinglePatch_Unchanged_NothingAsked()
    {
        var person = new PersonClient();
        const string body = "{\"sprk_name\":\"n\",\"sprk_amount\":1}";

        var answer = await PatchAsCaller(person, app: null, body);

        answer.Kind.Should().Be(CallerPatchKind.Written);
        person.Patches.Should().Equal(($"sprk_invoices({InvoiceId:D})", body));
        person.AccessAsked.Should().Be(0);
    }

    [Fact]
    public async Task Caller_NoLockedColumn_TheirOwnFailureIsTheirs()
    {
        var failure = DataverseUserResponse.Fail(403, "AccessDenied", "no");
        var person = new PersonClient { PatchAnswer = failure };

        var answer = await PatchAsCaller(person, app: null, "{\"sprk_name\":\"n\"}");

        answer.Should().Be(new CallerPatch(CallerPatchKind.ClientFailure, failure));
    }

    [Fact]
    public async Task Caller_Allowed_OtherColumnsAsThePerson_ThenLockedAsTheApplication()
    {
        var person = new PersonClient().Rights(InvoiceId, "ReadAccess, WriteAccess, AppendAccess").Rights(MatterId, "ReadAccess, AppendToAccess");
        var (app, writes) = Records();

        var answer = await PatchAsCaller(person, app, LockedAndOtherBody);

        answer.Kind.Should().Be(CallerPatchKind.Written);
        person.Patches.Should().Equal(($"sprk_invoices({InvoiceId:D})", "{\"sprk_name\":\"n\"}"));
        writes.Should().ContainSingle();
        writes[0].As.Should().BeNull("the application writes the locked column");
        writes[0].Fields.Keys.Should().BeEquivalentTo("sprk_Matter@odata.bind");
    }

    [Theory]
    [InlineData("ReadAccess, AppendAccess", "ReadAccess, AppendToAccess")] // no Write on the row
    [InlineData("ReadAccess, WriteAccess", "ReadAccess, AppendToAccess")]  // no Append on the row
    [InlineData("ReadAccess, WriteAccess, AppendAccess", "ReadAccess")]    // no AppendTo on the new core record
    public async Task Caller_LacksARight_IsDenied_NothingWritten(string onRow, string onMatter)
    {
        var person = new PersonClient().Rights(InvoiceId, onRow).Rights(MatterId, onMatter);
        var (app, writes) = Records();

        (await PatchAsCaller(person, app, LockedAndOtherBody)).Kind.Should().Be(CallerPatchKind.Denied);

        person.Patches.Should().BeEmpty();
        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Caller_LacksAppendToOnTheNewInvoice_IsDenied_NothingWritten()
    {
        var person = new PersonClient().Rights(LineId, "ReadAccess, WriteAccess, AppendAccess").Rights(InvoiceId, "ReadAccess");
        var (app, writes) = Records();

        (await PatchAsCaller(person, app, $"{{\"sprk_Invoice@odata.bind\":\"/sprk_invoices({InvoiceId})\"}}", "sprk_billingevent", LineId))
            .Kind.Should().Be(CallerPatchKind.Denied);

        person.Patches.Should().BeEmpty();
        writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(0)]
    public async Task Caller_ProbeUnanswered_IsUnavailable_NothingWritten(int status)
    {
        var person = new PersonClient().Status(InvoiceId, status);
        var (app, writes) = Records();

        (await PatchAsCaller(person, app, LockedAndOtherBody)).Kind.Should().Be(CallerPatchKind.Unavailable);

        person.Patches.Should().BeEmpty();
        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Caller_NoApplicationWriter_IsUnavailable_NothingWrittenOrAsked()
    {
        var person = new PersonClient();

        (await PatchAsCaller(person, app: null, LockedAndOtherBody)).Kind.Should().Be(CallerPatchKind.Unavailable);

        person.Patches.Should().BeEmpty();
        person.AccessAsked.Should().Be(0);
    }

    [Fact]
    public async Task Caller_UnknownRecord_AnswersExactlyAsARecordTheyMayNotUse()
    {
        var unknown = new PersonClient().Rights(InvoiceId, "ReadAccess, WriteAccess, AppendAccess"); // UnknownId: 404
        var forbidden = new PersonClient().Rights(InvoiceId, "ReadAccess, WriteAccess, AppendAccess").Status(UnknownId, 403);
        var (app, writes) = Records();
        var body = $"{{\"sprk_Matter@odata.bind\":\"/sprk_matters({UnknownId})\"}}";

        var a = await PatchAsCaller(unknown, app, body);
        var b = await PatchAsCaller(forbidden, app, body);

        a.Should().Be(b);
        a.Kind.Should().Be(CallerPatchKind.Denied);
        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Caller_ApplicationWriteFailsAfterThePersons_IsPartlySaved_LoggedWithTheRecord()
    {
        var person = new PersonClient().Rights(InvoiceId, "ReadAccess, WriteAccess, AppendAccess").Rights(MatterId, "AppendToAccess");
        var (app, _) = AppWriteFails(new HttpRequestException("secret detail"));
        var logger = new CapturingLogger();

        var answer = await PatchAsCaller(person, app, LockedAndOtherBody, logger: logger);

        answer.Kind.Should().Be(CallerPatchKind.PartlySaved);
        person.Patches.Should().ContainSingle();
        var warning = logger.Entries.Should().ContainSingle(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning).Subject;
        warning.Message.Should().Contain(InvoiceId.ToString()).And.Contain("trace-1").And.NotContain("secret detail");
    }

    [Fact]
    public async Task Caller_LockedOnly_ApplicationWriteFindsTheRowGone_IsDenied_NothingWritten()
    {
        var person = new PersonClient().Rights(InvoiceId, "ReadAccess, WriteAccess, AppendAccess").Rights(MatterId, "AppendToAccess");
        var (app, _) = AppWriteFails(new KeyNotFoundException());

        (await PatchAsCaller(person, app, $"{{\"sprk_Matter@odata.bind\":\"/sprk_matters({MatterId})\"}}"))
            .Kind.Should().Be(CallerPatchKind.Denied);

        person.Patches.Should().BeEmpty();
    }

    /// <summary>Every log entry, formatted.</summary>
    internal sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
