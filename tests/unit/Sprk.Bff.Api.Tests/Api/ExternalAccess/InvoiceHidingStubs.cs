using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// ebilling-r1 task 098: stand-ins for the (a′) invoice predicate's seams, for surface tests that are about something else
/// (the predicate itself is tested over the real reads in <c>ExternalInvoiceVisibilityTests</c>). Each records what it was
/// asked, so a test can prove a surface asked for the invoice it returned.
/// </summary>
internal sealed class InvoiceHidingStub
{
    private readonly HashSet<Guid> _hidden;
    private readonly bool _fault;
    private readonly ExternalInvoiceHiding? _real;
    private readonly List<Guid[]> _calls = new();

    private readonly HashSet<Guid> _perInvoiceFault = new();

    private InvoiceHidingStub(IEnumerable<Guid> hidden, bool fault, ExternalInvoiceHiding? real = null)
    {
        _hidden = hidden.ToHashSet();
        _fault = fault;
        _real = real;
    }

    /// <summary>
    /// Task 130-0 fix round 3: the decision was read but could not be made for exactly <paramref name="invoices"/> (a per-invoice
    /// <see cref="InvoiceHiddenReason.ReadFailed"/> with the result not faulted as a whole); every other asked invoice is visible.
    /// </summary>
    public static InvoiceHidingStub PerInvoiceFault(params Guid[] invoices)
    {
        var stub = new InvoiceHidingStub(Array.Empty<Guid>(), fault: false);
        stub._perInvoiceFault.UnionWith(invoices);
        return stub;
    }

    /// <summary>
    /// Task 130-0: the REAL caller-aware seam over <paramref name="world"/> (<see cref="Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.InvoiceWorld.Seam"/>:
    /// <see cref="ExternalDataService.HideInvoicesForAsync"/> with the world's caller firms), recording each call. For surface tests that
    /// must prove the own-firms rule reaches them.
    /// </summary>
    public static InvoiceHidingStub Real(Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.InvoiceWorld world) =>
        new(Array.Empty<Guid>(), fault: false, world.Seam);

    /// <summary>Hides nothing (every asked invoice is visible).</summary>
    public static InvoiceHidingStub NoneHidden() => new(Array.Empty<Guid>(), fault: false);

    /// <summary>Hides exactly <paramref name="invoices"/>.</summary>
    public static InvoiceHidingStub Hiding(params Guid[] invoices) => new(invoices, fault: false);

    /// <summary>The read failed: every asked invoice is hidden (<see cref="InvoiceVisibilityResult.Faulted"/>).</summary>
    public static InvoiceHidingStub Faulting() => new(Array.Empty<Guid>(), fault: true);

    /// <summary>Every call's asked invoices, in order.</summary>
    public IReadOnlyList<Guid[]> Calls => _calls;

    /// <summary>The caller-aware seam the external surfaces take.</summary>
    public ExternalInvoiceHiding Seam => (caller, invoiceIds, ct) =>
    {
        var asked = invoiceIds.ToArray();
        lock (_calls) _calls.Add(asked);
        if (_real is not null)
            return _real(caller, asked, ct);
        if (_fault)
            return Task.FromResult(InvoiceVisibilityResult.ReadFailed(asked));
        var hidden = asked.Where(_hidden.Contains).Distinct().ToDictionary(id => id, _ => InvoiceHiddenReason.SourceHidden);
        foreach (var id in asked.Where(_perInvoiceFault.Contains).Distinct())
            hidden[id] = InvoiceHiddenReason.ReadFailed;
        return Task.FromResult(new InvoiceVisibilityResult(asked, hidden, faulted: false));
    };
}

/// <summary>Task 098: stand-ins for the assistant tools' hide-only invoice-family seam.</summary>
internal static class InvoiceFamilyHidingStubs
{
    /// <summary>For a handler a test never lets reach the seam (an internal caller, a non-family table): any call fails.</summary>
    public static AppOnlyInvoiceFamilyHiding NeverCalled { get; } = (_, _, _, _) =>
        throw new InvalidOperationException("The invoice-family hiding was not expected to run (test).");

    /// <summary>
    /// Task 130-0b step 2: the handler tests' default. An invoice-family table may not reach the seam (any call fails, as
    /// <see cref="NeverCalled"/>); a CHILD table with an invoice link (<see cref="ExternalInvoiceVisibility.IsInvoiceLinkedChildTable"/>)
    /// now does for every external caller, and here hides nothing, so a test about another guard on a document, analysis or
    /// communication is unchanged. The step 2 tests set the real seam (<see cref="InvoiceFamilyFirmWorld.Seam"/>) or
    /// <see cref="NeverCalled"/> explicitly.
    /// </summary>
    public static AppOnlyInvoiceFamilyHiding ChildTablesShown { get; } = (_, table, _, _) =>
        ExternalInvoiceVisibility.IsInvoiceLinkedChildTable(table)
            ? Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>())
            : throw new InvalidOperationException("The invoice-family hiding was not expected to run (test).");

    /// <summary>Hides exactly <paramref name="rows"/> (of any table); records each call as "table:count".</summary>
    public static AppOnlyInvoiceFamilyHiding Hiding(List<string>? calls, params Guid[] rows)
    {
        var hidden = rows.ToHashSet();
        return (_, table, rowIds, _) =>
        {
            lock (hidden) calls?.Add($"{table}:{rowIds.Count}");
            return Task.FromResult<IReadOnlySet<Guid>>(rowIds.Where(hidden.Contains).ToHashSet());
        };
    }

    /// <summary>
    /// Review round 5: the REAL seam, <see cref="ExternalDataService.HideInvoiceFamilyRowsAsync"/> over the app-only Web API double
    /// <paramref name="world"/>, for tool tests that must prove the decision itself. Task 130-0b: the caller's firms are the world's
    /// (<see cref="InvoiceWorld.CallerFirmsAnswer"/>, its firm by default). <see cref="InvoiceFamilyFirmWorld"/> drives the seam as
    /// <c>AddExternalAccess</c> registers it.
    /// </summary>
    public static AppOnlyInvoiceFamilyHiding Real(InvoiceWorld world) =>
        (caller, table, rowIds, ct) => world.NewService(identity: null).HideInvoiceFamilyRowsAsync(
            caller, table, rowIds, world.RootFlags, world.SystemUserFirms, ct);

    /// <summary>A read fault: the seam throws, so the tool call fails with no data.</summary>
    public static AppOnlyInvoiceFamilyHiding Throwing { get; } = (_, _, _, _) =>
        throw new ExternalDataService.DataverseUnavailableException("The invoice-family read failed (test).");
}

/// <summary>
/// ebilling-r1 task 130-0b: the assistant tools' family seam AS <c>AddExternalAccess</c> REGISTERS IT (the real
/// <see cref="ExternalDataService.HideInvoiceFamilyRowsAsync"/>, with the caller's firms read through
/// <c>ExternalInvoiceSubmissionAuthorizationFilter.ReadSystemUserFirmsAsync</c>), over an <see cref="InvoiceWorld"/>. The caller is one
/// systemuser; <see cref="CallerIs"/> decides what its identity resolution and its contact answer. Every identity read is counted.
/// </summary>
internal sealed class InvoiceFamilyFirmWorld : IAsyncDisposable
{
    /// <summary>A firm that is not the caller's.</summary>
    public static readonly Guid OtherFirm = Guid.Parse("13000000-0000-4000-8000-0000000000b2");

    /// <summary>The caller kinds known to be a member of NO firm: no linked contact, an inactive linked contact. They see no invoice.</summary>
    public static TheoryData<string> CallersWithNoFirm => new() { "no-contact", "inactive-contact" };

    /// <summary>F3b: the caller kinds whose firms are UNKNOWN (the contact-binding read failed). The call fails (a tool error) instead
    /// of answering "no invoice": an unknown firm read is not a caller with no firm.</summary>
    public static TheoryData<string> CallersWithUnknownFirm => new() { "contact-binding-fault" };

    private readonly Guid _systemUser;
    private readonly Guid _contact = Guid.NewGuid();
    private ContactRecordState _contactState = ContactRecordState.Active;
    private Microsoft.Extensions.DependencyInjection.ServiceProvider? _provider;
    private Microsoft.Extensions.DependencyInjection.AsyncServiceScope? _scope;

    public InvoiceFamilyFirmWorld(Guid systemUser)
    {
        _systemUser = systemUser;
        Participations = new Mock<ExternalParticipationService>(
            new HttpClient(), Mock.Of<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection().Build(), Mock.Of<Azure.Core.TokenCredential>(),
            Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ExternalParticipationService>.Instance, null!);
        Participations.Setup(p => p.QueryContactStateAsync(_contact, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _contactState);
        Participations.Setup(p => p.ReadOrganizationMembershipsAsync(_contact, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveOrgMemberships(new[] { InvoiceWorld.Firm }, Array.Empty<Guid>(), Unreadable: false));
        CallerIs("own-firm");
    }

    /// <summary>The app-only Web API and firm-hold double the seam reads.</summary>
    public InvoiceWorld World { get; } = new();

    /// <summary>The identity resolution (systemuser to contact), strict: only the caller's systemuser is answered.</summary>
    public Mock<Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService> Identity { get; } = new(MockBehavior.Strict);

    /// <summary>The contact's state and memberships (the one firm definition's reads).</summary>
    public Mock<ExternalParticipationService> Participations { get; }

    /// <summary>
    /// The caller: "own-firm" (a linked Active contact, member of <see cref="InvoiceWorld.Firm"/>), "no-contact", "contact-binding-fault"
    /// (the contact-binding read failed) or "inactive-contact" (a linked contact that is not Active).
    /// </summary>
    public void CallerIs(string kind)
    {
        var person = kind switch
        {
            "own-firm" or "inactive-contact" => new Sprk.Bff.Api.Services.Ai.Membership.Models.PersonIdentity(_systemUser, ContactId: _contact),
            "no-contact" => new Sprk.Bff.Api.Services.Ai.Membership.Models.PersonIdentity(_systemUser),
            "contact-binding-fault" => new Sprk.Bff.Api.Services.Ai.Membership.Models.PersonIdentity(_systemUser, ContactId: _contact)
            {
                Faults = Sprk.Bff.Api.Services.Ai.Membership.Models.IdentityReadFaults.ContactBinding,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown caller kind (test)."),
        };
        _contactState = kind == "inactive-contact" ? ContactRecordState.Inactive : ContactRecordState.Active;
        Identity.Setup(i => i.ResolveAsync(_systemUser, It.IsAny<CancellationToken>())).ReturnsAsync(person);
    }

    /// <summary>How many times the caller's firms were read (each read starts with one identity resolution).</summary>
    public int FirmReads => Identity.Invocations.Count(i =>
        i.Method.Name == nameof(Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService.ResolveAsync));

    /// <summary>How many times any contact membership was read.</summary>
    public int MembershipReads => Participations.Invocations.Count(i =>
        i.Method.Name == nameof(ExternalParticipationService.ReadOrganizationMembershipsAsync));

    /// <summary>An invoice of <paramref name="firm"/> that is otherwise visible (its own submission on the Spa channel, no source).</summary>
    public Guid InvoiceOfFirm(Guid firm)
    {
        var invoice = World.OwnedInvoice();
        World.InvoiceFirm(invoice, firm);
        return invoice;
    }

    /// <summary>
    /// An invoice of the caller's firm (so the invoice itself is visible), and its own submission on the Spa channel with no document,
    /// whose own firm (<c>sprk_vendororg</c>) is <paramref name="firm"/>; returns the submission's id.
    /// </summary>
    public Guid SubmissionOfFirm(Guid firm)
    {
        var invoice = World.NewInvoice();
        var submission = World.Submission(invoice, document: null, channel: InvoiceWorld.SpaChannel);
        World.SetRow("sprk_invoicesubmissions", "sprk_invoicesubmissionid", submission, "_sprk_vendororg_value", firm.ToString());
        return submission;
    }

    // ── Task 130-0b step 2: child rows with an invoice link ────────────────────────────────────────────────────────────

    /// <summary>A row of <paramref name="linked"/>'s table whose link column names <paramref name="invoice"/> (a GUID, or raw text).</summary>
    public Guid LinkedRow(InvoiceLinkedCase linked, object invoice) =>
        World.LinkedChildRow(linked.Table, (linked.LinkColumn, invoice));

    /// <summary>A row of <paramref name="linked"/>'s table with no invoice link.</summary>
    public Guid UnlinkedRow(InvoiceLinkedCase linked) => World.LinkedChildRow(linked.Table);

    /// <summary>
    /// The fault <paramref name="kind"/>: "link-read" (the app-only read of the table's invoice links fails) or "decision" (the invoice
    /// decision's Stored File read fails, after the links and the firm hold are read).
    /// </summary>
    public void Fail(InvoiceLinkedCase linked, string kind) =>
        World.Faults[kind switch
        {
            "link-read" => linked.EntitySet,
            "decision" => "sprk_storedfiles",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown fault (test)."),
        }] = System.Net.HttpStatusCode.InternalServerError;

    /// <summary>The seam <c>AddExternalAccess</c> registers, resolved from one provider over this world.</summary>
    public AppOnlyInvoiceFamilyHiding Seam
    {
        get
        {
            if (_scope is null)
            {
                var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
                Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(services);
                Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
                    services, new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
                        .Build());
                Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<Azure.Core.TokenCredential>(
                    services, new ExternalRecordDocumentRoutesTests.InMemoryExternalDataService.TestCredential());
                Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>(
                    services, new Sprk.Bff.Api.Tests.Infrastructure.Cache.InMemoryTenantCache());
                Microsoft.Extensions.DependencyInjection.HttpServiceCollectionExtensions.AddHttpContextAccessor(services);
                Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, World.FilingReader);
                Sprk.Bff.Api.Infrastructure.DI.ExternalAccessModule.AddExternalAccess(services);
                Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions.AddHttpClient<ExternalDataService>(services)
                    .ConfigurePrimaryHttpMessageHandler(() => World);
                Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, Participations.Object);
                Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, Identity.Object);
                _provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
                _scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateAsyncScope(_provider);
            }

            return Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<AppOnlyInvoiceFamilyHiding>(_scope.Value.ServiceProvider);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_scope is { } scope)
            await scope.DisposeAsync();
        if (_provider is not null)
            await _provider.DisposeAsync();
    }
}

/// <summary>
/// Task 130-0b step 2: one class of CHILD row with an invoice link, as the handler tests name it: its table and the one link column
/// the test sets (a document twice: through <c>sprk_invoice</c> and through <c>sprk_relatedinvoice</c>). The read shape comes from
/// <see cref="InvoiceLinks.Tables"/>, never from the test.
/// </summary>
internal sealed record InvoiceLinkedCase(string Key, string Table, string LinkColumn)
{
    /// <summary>Every class: to-do, event, document (both links), communication, analysis, memo.</summary>
    public static IReadOnlyList<InvoiceLinkedCase> All { get; } = new InvoiceLinkedCase[]
    {
        new("todo", "sprk_todo", "sprk_regardinginvoice"),
        new("event", "sprk_event", "sprk_regardinginvoice"),
        new("document-invoice", "sprk_document", "sprk_invoice"),
        new("document-relatedinvoice", "sprk_document", "sprk_relatedinvoice"),
        new("communication", "sprk_communication", "sprk_regardinginvoice"),
        new("analysis", "sprk_analysis", "sprk_regardinginvoice"),
        new("memo", "sprk_memo", "sprk_regardinginvoice"),
    };

    /// <summary>
    /// Task 130-0b step 3: the ROOT with an invoice link (the work assignment through <c>sprk_regardinginvoice</c>). Not in
    /// <see cref="All"/>: a root row is never hidden; its invoice link is blanked when that invoice is not visible.
    /// </summary>
    public static InvoiceLinkedCase Root { get; } = new("workassignment", "sprk_workassignment", "sprk_regardinginvoice");

    /// <summary>The class named <paramref name="key"/>.</summary>
    public static InvoiceLinkedCase Of(string key) => All.Single(c => c.Key == key);

    /// <summary>Every class's key (the tools that can return every class: read_query and describe).</summary>
    public static TheoryData<string> Keys => Data(All);

    /// <summary>
    /// Every class but the communication: search_data drops every communication-family hit for an external caller (ISS-010) before any
    /// invoice rule, so it never returns one.
    /// </summary>
    public static TheoryData<string> SearchableKeys => Data(All.Where(c => c.Table != "sprk_communication"));

    /// <summary>Every class crossed with each fault ("link-read", "decision").</summary>
    public static TheoryData<string, string> KeysAndFaults => Faults(All);

    /// <summary><see cref="SearchableKeys"/> crossed with each fault.</summary>
    public static TheoryData<string, string> SearchableKeysAndFaults => Faults(All.Where(c => c.Table != "sprk_communication"));

    public string EntitySet => InvoiceLinks.Tables[Table].EntitySet!;

    public string IdColumn => InvoiceLinks.Tables[Table].IdColumn;

    private static TheoryData<string> Data(IEnumerable<InvoiceLinkedCase> cases)
    {
        var data = new TheoryData<string>();
        foreach (var c in cases)
            data.Add(c.Key);
        return data;
    }

    private static TheoryData<string, string> Faults(IEnumerable<InvoiceLinkedCase> cases)
    {
        var data = new TheoryData<string, string>();
        foreach (var c in cases)
        {
            data.Add(c.Key, "link-read");
            data.Add(c.Key, "decision");
        }

        return data;
    }
}
