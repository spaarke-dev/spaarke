using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// ebilling-r1 task 130-0 (design.md:414): a partner sees only invoices of its own firms. Driven through the REAL
/// <see cref="ExternalDataService.HideInvoicesForAsync"/> over <see cref="InvoiceWorld"/>: the invoice's firm (<c>sprk_vendororg</c>) is read
/// in the firm-hold app-only batch, the caller's firms through the seam's <see cref="CallerFirmReader"/> (once per call). "No firm" and
/// "unknown" are distinct states: a caller known to have no firm sees no invoice (owner decision 2026-11-08, reason firm_not_callers, not
/// a fault); firms that cannot be read hide every asked invoice as a failed read.
/// </summary>
public sealed partial class ExternalInvoiceVisibilityTests
{
    private static readonly Guid FirmB = Guid.Parse("13000000-0000-4000-8000-0000000000b2");
    private static readonly Guid FirmC = Guid.Parse("13000000-0000-4000-8000-0000000000c3");

    private static string FirmNotCallersCode => InvoiceVisibilityResult.CodeOf(InvoiceHiddenReason.FirmNotCallers);

    private Guid InvoiceOfFirm(Guid? firm)
    {
        var invoice = _world.OwnedInvoice();
        _world.InvoiceFirm(invoice, firm);
        return invoice;
    }

    [Theory(DisplayName = "130-0 must fire (seed: the own-firms check dropped from DecideAsync): an invoice of a firm that is not the caller's is hidden; the caller's own firm's invoice stays visible")]
    [MemberData(nameof(ExternalCallers))]
    public async Task OwnFirms_AnotherFirmsInvoice_IsHidden_TheCallersOwnFirmsInvoice_IsVisible(string caller)
    {
        var own = InvoiceOfFirm(InvoiceWorld.Firm);
        var other = InvoiceOfFirm(FirmB);

        var decision = await Decide(caller, own, other);

        decision.IsVisible(own).Should().BeTrue(decision.HiddenSummary());
        decision.HiddenReason(other).Should().Be(InvoiceHiddenReason.FirmNotCallers);
        decision.Faulted.Should().BeFalse();
    }

    [Fact(DisplayName = "130-0 multi-firm caller: the invoices of each of the caller's firms are visible; a third firm's are hidden")]
    public async Task OwnFirms_AMultiFirmCaller_SeesEachOfItsFirmsInvoices_AndNotAThirdFirms()
    {
        _world.CallerFirmsAnswer = CallerFirms.From(new[] { InvoiceWorld.Firm, FirmB }, unreadable: false);
        var ofA = InvoiceOfFirm(InvoiceWorld.Firm);
        var ofB = InvoiceOfFirm(FirmB);
        var ofC = InvoiceOfFirm(FirmC);

        var decision = await Decide("contact", ofA, ofB, ofC);

        decision.IsVisible(ofA).Should().BeTrue(decision.HiddenSummary());
        decision.IsVisible(ofB).Should().BeTrue(decision.HiddenSummary());
        decision.HiddenReason(ofC).Should().Be(InvoiceHiddenReason.FirmNotCallers);
    }

    [Fact(DisplayName = "130-0 an invoice with no firm is still withheld: blank sprk_vendororg (firm_not_callers) and the firm-missing hold (firm_missing, as before)")]
    public async Task OwnFirms_AnInvoiceWithNoFirm_IsStillWithheld()
    {
        var blank = InvoiceOfFirm(null);
        var held = InvoiceOfFirm(null);
        _world.FirmHold(held, InvoiceFirmHold.InvoiceHoldValue);

        var decision = await Decide("contact", blank, held);

        decision.HiddenReason(blank).Should().Be(InvoiceHiddenReason.FirmNotCallers);
        decision.HiddenReason(held).Should().Be(InvoiceHiddenReason.FirmMissing, "the existing firm-hold rule decides first, unchanged");
        decision.Faulted.Should().BeFalse();
    }

    [Fact(DisplayName = "130-0 owner decision 2026-11-08: a caller known to be a member of no firm sees no invoice (firm_not_callers, not a fault), and nothing else is read")]
    public async Task OwnFirms_ACallerWithNoFirm_SeesNoInvoice_AndItIsNotAFault()
    {
        _world.CallerFirmsAnswer = CallerFirms.From(Array.Empty<Guid>(), unreadable: false);
        var own = InvoiceOfFirm(InvoiceWorld.Firm);
        var other = InvoiceOfFirm(FirmB);

        var decision = await Decide("contact", own, other);

        decision.IsVisible(own).Should().BeFalse();
        decision.IsVisible(other).Should().BeFalse();
        decision.Faulted.Should().BeFalse("no firm is a known answer, not a failed read");
        decision.HiddenSummary().Should().Be($"{FirmNotCallersCode}=2");
        _world.Urls.Should().BeEmpty("a caller with no firm causes no invoice read");
        _world.InvoiceReads.Should().BeEmpty();
    }

    [Theory(DisplayName = "130-0 fail closed: a caller-firm read that throws, answers unreadable or answers null hides every asked invoice as a failed read (never as 'no firm')")]
    [InlineData("throws")]
    [InlineData("unreadable")]
    [InlineData("null")]
    public async Task OwnFirms_UnknownFirms_HideEveryInvoice_AsAFailedRead(string kind)
    {
        switch (kind)
        {
            case "throws": _world.CallerFirmsFault = new TimeoutException("The membership read failed (test)."); break;
            case "unreadable": _world.CallerFirmsAnswer = CallerFirms.From(new[] { InvoiceWorld.Firm }, unreadable: true); break;
            default: _world.CallerFirmsNull = true; break;
        }

        var own = InvoiceOfFirm(InvoiceWorld.Firm);

        var decision = await Decide("contact", own);

        decision.IsVisible(own).Should().BeFalse();
        decision.Faulted.Should().BeTrue("unknown firms are a failed read, not 'no firm'");
        decision.HiddenReason(own).Should().Be(InvoiceHiddenReason.ReadFailed);
        decision.HiddenSummary().Should().NotContain(FirmNotCallersCode);
    }

    [Fact(DisplayName = "130-0 the caller's firms are read ONCE per call, and each invoice's firm in the firm-hold batch (one read per chunk, no extra query)")]
    public async Task OwnFirms_OneCallerFirmReadPerCall_AndTheInvoiceFirmIsReadInTheFirmHoldBatch()
    {
        var invoices = Enumerable.Range(0, ExternalInvoiceVisibility.ChunkSize * 2 + 1).Select(_ => InvoiceOfFirm(InvoiceWorld.Firm)).ToArray();

        var decision = await Decide("contact", invoices);

        invoices.Should().OnlyContain(id => decision.IsVisible(id), decision.HiddenSummary());
        _world.CallerFirmReads.Should().Be(1, "one firm read per call, not per invoice or per chunk");
        _world.InvoiceReads.Should().HaveCount(3, "the invoice firm rides on the firm-hold read: one per chunk");
        _world.InvoiceReads.Should().OnlyContain(q =>
            q.ColumnSet.Columns.Contains(InvoiceFirmHold.InvoiceHoldColumn) && q.ColumnSet.Columns.Contains("sprk_vendororg"));
    }

    [Fact(DisplayName = "130-0 an internal systemuser hides nothing and causes no firm read")]
    public async Task OwnFirms_AnInternalSystemuser_HidesNothing_AndReadsNoFirm()
    {
        var other = InvoiceOfFirm(FirmB);

        var decision = await Decide("internal-systemuser", other);

        decision.IsVisible(other).Should().BeTrue();
        _world.CallerFirmReads.Should().Be(0);
        _world.InvoiceReads.Should().BeEmpty();
    }

    [Fact(DisplayName = "130-0 an external caller's firms are read (the internal check runs first, then one firm read)")]
    public async Task OwnFirms_AnExternalCaller_ReadsItsFirmsOnce()
    {
        await Decide("flagged-systemuser", InvoiceOfFirm(InvoiceWorld.Firm));

        _world.CallerFirmReads.Should().Be(1);
    }

    [Fact(DisplayName = "130-0 'no firm' and 'unknown' are distinct states: an empty set is not a fault, and a fault is not an empty set")]
    public void CallerFirms_KeepsNoFirmAndUnknownApart()
    {
        CallerFirms.From(Array.Empty<Guid>(), unreadable: false).State.Should().Be(CallerFirmsState.NoFirm);
        CallerFirms.From(new[] { Guid.Empty }, unreadable: false).State.Should().Be(CallerFirmsState.NoFirm);
        CallerFirms.From(Array.Empty<Guid>(), unreadable: true).State.Should().Be(CallerFirmsState.Unknown);
        CallerFirms.From(null, unreadable: false).State.Should().Be(CallerFirmsState.Unknown);
        CallerFirms.From(new[] { FirmB }, unreadable: true).State.Should().Be(CallerFirmsState.Unknown);
        var known = CallerFirms.From(new[] { FirmB, FirmC, FirmB }, unreadable: false);
        known.State.Should().Be(CallerFirmsState.Known);
        known.Firms.Should().BeEquivalentTo(new[] { FirmB, FirmC });
    }

    [Fact(DisplayName = "130-0 production wiring: AddExternalAccess binds the invoice seam to the caller's firms (one membership read per call), so another firm's invoice is hidden and the caller's own is visible")]
    public async Task TheInvoiceSeam_IsRegisteredByAddExternalAccess_WithTheCallersFirms()
    {
        var contact = Guid.NewGuid();
        var own = InvoiceOfFirm(InvoiceWorld.Firm);
        var other = InvoiceOfFirm(FirmB);
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(services);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            services, new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
                .Build());
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<Azure.Core.TokenCredential>(
            services, new Sprk.Bff.Api.Tests.Api.ExternalAccess.ExternalRecordDocumentRoutesTests.InMemoryExternalDataService.TestCredential());
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>(
            services, new Sprk.Bff.Api.Tests.Infrastructure.Cache.InMemoryTenantCache());
        Microsoft.Extensions.DependencyInjection.HttpServiceCollectionExtensions.AddHttpContextAccessor(services);

        // The app-only firm-hold read: it must select the invoice's firm, and answers each invoice with its firm.
        var filing = new Moq.Mock<Spaarke.Dataverse.IGenericEntityService>(Moq.MockBehavior.Strict);
        filing.Setup(d => d.RetrieveMultipleAsync(
                Moq.It.Is<Microsoft.Xrm.Sdk.Query.QueryExpression>(q => q.EntityName == "sprk_invoice"
                    && q.ColumnSet.Columns.Contains("sprk_firmhold") && q.ColumnSet.Columns.Contains("sprk_vendororg")),
                Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.EntityCollection(new List<Microsoft.Xrm.Sdk.Entity>
            {
                new("sprk_invoice", own) { ["sprk_vendororg"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_organization", InvoiceWorld.Firm) },
                new("sprk_invoice", other) { ["sprk_vendororg"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_organization", FirmB) },
            }));
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, filing.Object);
        services.AddExternalAccess();
        Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions.AddHttpClient<ExternalDataService>(services)
            .ConfigurePrimaryHttpMessageHandler(() => _world);

        // The caller's firms, through the one membership read the submission routes and /me use.
        var participations = new Moq.Mock<ExternalParticipationService>(
            new HttpClient(), Moq.Mock.Of<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection().Build(),
            Moq.Mock.Of<Azure.Core.TokenCredential>(), Moq.Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ExternalParticipationService>.Instance, null!);
        participations.Setup(p => p.QueryContactStateAsync(contact, Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContactRecordState.Active);
        participations.Setup(p => p.ReadOrganizationMembershipsAsync(contact, Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveOrgMemberships(new[] { InvoiceWorld.Firm }, Array.Empty<Guid>(), Unreadable: false));
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, participations.Object);

        await using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        await using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateAsyncScope(provider);
        var seam = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ExternalInvoiceHiding>(scope.ServiceProvider);

        var decision = await seam(new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = contact,
            Email = "partner@firm.test",
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
        }, new[] { own, other }, CancellationToken.None);

        decision.Faulted.Should().BeFalse(decision.HiddenSummary());
        decision.IsVisible(own).Should().BeTrue(decision.HiddenSummary());
        decision.HiddenReason(other).Should().Be(InvoiceHiddenReason.FirmNotCallers);
        participations.Verify(p => p.ReadOrganizationMembershipsAsync(contact, Moq.It.IsAny<CancellationToken>()), Moq.Times.Once());
    }

    [Fact(DisplayName = "130-0 the submission seam does not apply the own-firms rule here (its filter scopes by firm)")]
    public async Task OwnFirms_TheSubmissionSeam_IsUnchanged()
    {
        var other = InvoiceOfFirm(FirmB);
        var submission = InvoiceWorld.SubmissionFor(_world.OwnDocumentOf(other));

        var decision = await _world.NewService(_identity.Object).HideSubmissionsForAsync(Caller("contact"), new[]
        {
            new InvoiceSubmissionView
            {
                SubmissionId = submission, InvoiceId = other, ChannelValue = InvoiceWorld.SpaChannel, FirmId = InvoiceWorld.Firm,
            },
        }, _world.RootFlags);

        decision.HiddenSummary().Should().NotContain(FirmNotCallersCode);
        _world.CallerFirmReads.Should().Be(0);
    }
}
