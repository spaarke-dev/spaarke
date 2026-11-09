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

    private InvoiceHidingStub(IEnumerable<Guid> hidden, bool fault, ExternalInvoiceHiding? real = null)
    {
        _hidden = hidden.ToHashSet();
        _fault = fault;
        _real = real;
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
        return Task.FromResult(new InvoiceVisibilityResult(asked, hidden, faulted: false));
    };
}

/// <summary>Task 098: stand-ins for the assistant tools' hide-only invoice-family seam.</summary>
internal static class InvoiceFamilyHidingStubs
{
    /// <summary>For a handler a test never lets reach the seam (an internal caller, a non-family table): any call fails.</summary>
    public static AppOnlyInvoiceFamilyHiding NeverCalled { get; } = (_, _, _, _) =>
        throw new InvalidOperationException("The invoice-family hiding was not expected to run (test).");

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

    /// <summary>The caller kinds with no usable firm: no linked contact, a contact-binding read fault, an inactive linked contact.</summary>
    public static TheoryData<string> CallersWithNoFirm => new() { "no-contact", "contact-binding-fault", "inactive-contact" };

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
