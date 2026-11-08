using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Membership;

/// <summary>
/// field-mapping-server-write-path-r1 task 032b-1, U7 (owner-accepted 2026-11-08). With <c>sprk_invoice</c> out of the
/// access-conferring registry, the people-targeting surface — the Daily Briefing's invoice channel reads it through
/// <c>PeopleTargetedSet</c> — selects an invoice FOR a person through Created By or a user owner only, never through the invoice's
/// attorney, paralegal or Assigned To columns. The matter keeps selecting through its own Assigned columns (control).
/// The assertions are on the emitted FetchXML, which is what Dataverse evaluates.
/// </summary>
public class InvoicePeopleTargetingAfterDisplayCopyTests
{
    private static readonly Guid CallerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CallerContactId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly string[] InvoiceAssignedColumns =
    [
        "sprk_assignedto1", "sprk_assignedto2", "sprk_assignedtoattorney1", "sprk_assignedtoattorney2",
        "sprk_assignedtoparalegal1", "sprk_assignedtoparalegal2",
    ];

    [Fact]
    public async Task AnInvoice_IsForAPerson_ThroughCreatedByAndOwner_NeverThroughItsAssignedColumns()
    {
        var descriptors = new List<MembershipDescriptor> { D("ownerid", "owner", "SystemUser") };
        descriptors.AddRange(InvoiceAssignedColumns.Select(c => D(c, c, "Contact")));

        var fetch = await PeopleFetchAsync("sprk_invoice", descriptors);

        fetch.Should().Contain(Condition("ownerid", CallerId));
        fetch.Should().Contain(Condition("createdby", CallerId));
        foreach (var column in InvoiceAssignedColumns)
        {
            fetch.Should().NotContain($"'{column}'", $"{column} is a display copy now and selects nothing (U7)");
        }

        fetch.Should().NotContain(CallerContactId.ToString("D"), "no invoice term binds the caller's linked contact");
    }

    [Fact]
    public async Task Control_AMatter_IsStillForThePersonNamedInItsAssignedColumn()
    {
        var fetch = await PeopleFetchAsync("sprk_matter",
            [D("ownerid", "owner", "SystemUser"), D("sprk_assignedattorney1", "assignedAttorney", "Contact")]);

        fetch.Should().Contain(Condition("sprk_assignedattorney1", CallerContactId));
    }

    private static async Task<string> PeopleFetchAsync(string entity, IReadOnlyList<MembershipDescriptor> descriptors)
    {
        var discovery = new Mock<IMembershipFieldDiscoveryService>();
        discovery
            .Setup(d => d.DiscoverAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DiscoveryResult(entity, DateTimeOffset.UtcNow, descriptors.ToArray(),
                Array.Empty<IgnoredField>(), Array.Empty<IgnoredField>()));

        var identity = new Mock<IIdentityNormalizationService>();
        identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PersonIdentity(
            SystemUserId: CallerId, ContactId: CallerContactId, PrimaryEmail: "ada.member@spaarke.dev",
            TeamIds: Array.Empty<Guid>(), BusinessUnitId: Guid.NewGuid(), AccountId: null, OrganizationIds: Array.Empty<Guid>()));

        var captured = new List<string>();
        var dataverse = new Mock<IDataverseService>();
        dataverse
            .Setup(x => x.RetrieveAsync("systemuser", CallerId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("systemuser") { Id = CallerId }); // a human: no applicationid
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FetchExpression fe, CancellationToken _) =>
            {
                captured.Add(fe.Query);
                return new EntityCollection();
            });

        var options = new MembershipOptions();
        new MembershipOptionsDefaults().PostConfigure(name: null, options);

        var sut = new MembershipResolverService(
            discovery.Object, identity.Object, dataverse.Object, new Mock<ITenantCache>().Object, Options.Create(options),
            NullLogger<MembershipResolverService>.Instance);

        await sut.ResolveAsync(CallerId, entity, MembershipResolveOptions.People, CancellationToken.None);

        return captured.Should().ContainSingle().Subject;
    }

    private static string Condition(string field, Guid value) =>
        $"<condition attribute='{field}' operator='eq' value='{value:D}' />";

    private static MembershipDescriptor D(string field, string role, string identityType) =>
        new(field, role, identityType, identityType.ToLowerInvariant(), "auto");
}
