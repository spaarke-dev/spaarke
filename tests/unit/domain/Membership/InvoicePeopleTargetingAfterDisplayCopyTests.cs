using System.Text.RegularExpressions;
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
/// field-mapping-server-write-path-r1 tasks 032b-1 (U7) and 032b-1b (owner decision 2026-11-08: invoice Assigned To targets
/// people without conferring access). With <c>sprk_invoice</c> out of the access-conferring registry, the people-targeting
/// surface (the Daily Briefing's invoice channel reads it through <c>PeopleTargetedSet</c>; the portfolio and the playbook
/// membership node read the same resolver) selects an invoice FOR a person through Created By, a user owner, or the invoice's
/// <c>sprk_assignedto1/2</c> (a contact linked to that person, read from <c>MembershipOptionsDefaults.TargetingOnlyColumns</c>)
/// - never through the four attorney and paralegal display copies. The Assigned To columns grant no access. The assertions are
/// on the emitted FetchXML, which is what Dataverse evaluates.
/// </summary>
public class InvoicePeopleTargetingAfterDisplayCopyTests
{
    private static readonly Guid CallerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CallerContactId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid InvoiceId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly string[] AssignedToColumns = ["sprk_assignedto1", "sprk_assignedto2"];

    private static readonly string[] DisplayCopyColumns =
    [
        "sprk_assignedtoattorney1", "sprk_assignedtoattorney2", "sprk_assignedtoparalegal1", "sprk_assignedtoparalegal2",
    ];

    private static List<MembershipDescriptor> InvoiceDescriptors()
    {
        var descriptors = new List<MembershipDescriptor> { D("ownerid", "owner", "SystemUser") };
        descriptors.AddRange(AssignedToColumns.Concat(DisplayCopyColumns).Select(c => D(c, c, "Contact")));
        return descriptors;
    }

    [Theory]
    [InlineData("sprk_assignedto1")]
    [InlineData("sprk_assignedto2")]
    public async Task AnInvoice_IsForTheUserWhoseContactIsNamedInAssignedTo(string column)
    {
        var fetch = await PeopleFetchAsync("sprk_invoice", InvoiceDescriptors());

        fetch.Should().Contain(Condition(column, CallerContactId), $"{column} targets the person its contact represents");
    }

    [Fact]
    public async Task AnInvoice_StillTargetsThroughOwnerAndCreator()
    {
        var fetch = await PeopleFetchAsync("sprk_invoice", InvoiceDescriptors());

        fetch.Should().Contain(Condition("ownerid", CallerId));
        fetch.Should().Contain(Condition("createdby", CallerId));
    }

    /// <summary>Must-not-fire: the four attorney and paralegal columns are display copies and select nothing (U7).</summary>
    [Fact]
    public async Task AnInvoice_NeverTargetsThroughTheFourDisplayCopyColumns()
    {
        var fetch = await PeopleFetchAsync("sprk_invoice", InvoiceDescriptors());

        foreach (var column in DisplayCopyColumns)
        {
            fetch.Should().NotContain($"'{column}'", $"{column} is a display copy and selects nothing (U7)");
        }

        // The caller's contact is bound exactly twice: sprk_assignedto1 and sprk_assignedto2, nothing else.
        Regex.Matches(fetch, CallerContactId.ToString("D")).Count.Should().Be(2);
    }

    /// <summary>Must-not-fire: with no contact linked to the user, an Assigned To term binds nothing (no email or name fallback).</summary>
    [Fact]
    public async Task AnInvoice_WithNoLinkedContact_BindsNoAssignedToTerm()
    {
        var fetch = await PeopleFetchAsync("sprk_invoice", InvoiceDescriptors(), linkedContact: false);

        // The columns are still selected (the descriptors exist), but nothing is compared against them.
        fetch.Should().NotContain("<condition attribute='sprk_assignedto1'");
        fetch.Should().NotContain("<condition attribute='sprk_assignedto2'");
        fetch.Should().NotContain(CallerContactId.ToString("D"));
        fetch.Should().Contain(Condition("createdby", CallerId));
    }

    /// <summary>The Daily Briefing's read: <c>PeopleTargetedSet</c> over the real resolver returns an invoice whose Assigned To names the user's contact.</summary>
    [Theory]
    [InlineData("sprk_assignedto1")]
    [InlineData("sprk_assignedto2")]
    public async Task TheDailyBriefingRead_ReturnsAnInvoice_WhoseAssignedToIsTheUsersContact(string column)
    {
        var row = new Entity("sprk_invoice") { Id = InvoiceId };
        row[column] = new EntityReference("contact", CallerContactId);

        var sut = Resolver("sprk_invoice", InvoiceDescriptors(), linkedContact: true, rows: [row], out _);
        var result = await PeopleTargetedSet.ResolveAsync(sut, CallerId, "sprk_invoice", NullLogger.Instance, CancellationToken.None);

        result.Complete.Should().BeTrue();
        result.Ids.Should().ContainSingle().Which.Should().Be(InvoiceId);
    }

    /// <summary>Must-not-fire: an invoice whose only link to the user is a display-copy attorney or paralegal column is not in the Daily Briefing's set.</summary>
    [Theory]
    [InlineData("sprk_assignedtoattorney1")]
    [InlineData("sprk_assignedtoattorney2")]
    [InlineData("sprk_assignedtoparalegal1")]
    [InlineData("sprk_assignedtoparalegal2")]
    public async Task TheDailyBriefingRead_DoesNotReturnAnInvoice_NamedOnlyInADisplayCopyColumn(string column)
    {
        var row = new Entity("sprk_invoice") { Id = InvoiceId };
        row[column] = new EntityReference("contact", CallerContactId);

        var sut = Resolver("sprk_invoice", InvoiceDescriptors(), linkedContact: true, rows: [row], out _);
        var result = await PeopleTargetedSet.ResolveAsync(sut, CallerId, "sprk_invoice", NullLogger.Instance, CancellationToken.None);

        result.Ids.Should().BeEmpty();
    }

    /// <summary>The Assigned To targeting grants nothing: neither access plane resolves an invoice through the targeting-only columns.</summary>
    [Fact]
    public async Task NoAccessGrantResults_FromInvoiceAssignedTo()
    {
        var sut = Resolver("sprk_invoice", InvoiceDescriptors(), linkedContact: true, rows: [], out var captured);

        var systemUserPlane = await sut.ResolveAsync(
            CallerId, "sprk_invoice", new MembershipResolveOptions(AccessConferringOnly: true), CancellationToken.None);
        var contactPlane = await sut.ResolveByContactAsync(
            CallerContactId, "sprk_invoice", options: null, CancellationToken.None);

        contactPlane.Ids.Should().BeEmpty();
        systemUserPlane.Ids.Should().BeEmpty();
        string.Concat(captured).Should().NotContain("sprk_assignedto", "the access planes never read the targeting-only columns");
        MembershipOptionsDefaults.CanonicalAccessConferringRegistry.Keys.Should().NotContain("sprk_invoice");
    }

    [Fact]
    public async Task Control_AMatter_IsStillForThePersonNamedInItsAssignedColumn()
    {
        var fetch = await PeopleFetchAsync("sprk_matter",
            [D("ownerid", "owner", "SystemUser"), D("sprk_assignedattorney1", "assignedAttorney", "Contact")]);

        fetch.Should().Contain(Condition("sprk_assignedattorney1", CallerContactId));
    }

    private static async Task<string> PeopleFetchAsync(
        string entity, IReadOnlyList<MembershipDescriptor> descriptors, bool linkedContact = true)
    {
        var sut = Resolver(entity, descriptors, linkedContact, rows: [], out var captured);

        await sut.ResolveAsync(CallerId, entity, MembershipResolveOptions.People, CancellationToken.None);

        return captured.Should().ContainSingle().Subject;
    }

    private static MembershipResolverService Resolver(
        string entity, IReadOnlyList<MembershipDescriptor> descriptors, bool linkedContact, IReadOnlyList<Entity> rows,
        out List<string> captured)
    {
        var discovery = new Mock<IMembershipFieldDiscoveryService>();
        discovery
            .Setup(d => d.DiscoverAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DiscoveryResult(entity, DateTimeOffset.UtcNow, descriptors.ToArray(),
                Array.Empty<IgnoredField>(), Array.Empty<IgnoredField>()));

        var identity = new Mock<IIdentityNormalizationService>();
        identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PersonIdentity(
            SystemUserId: CallerId, ContactId: linkedContact ? CallerContactId : null, PrimaryEmail: "ada.member@spaarke.dev",
            TeamIds: Array.Empty<Guid>(), BusinessUnitId: Guid.NewGuid(), AccountId: null, OrganizationIds: Array.Empty<Guid>()));

        var queries = new List<string>();
        captured = queries;
        var dataverse = new Mock<IDataverseService>();
        dataverse
            .Setup(x => x.RetrieveAsync("systemuser", CallerId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("systemuser") { Id = CallerId }); // a human: no applicationid
        dataverse
            .Setup(x => x.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FetchExpression fe, CancellationToken _) =>
            {
                queries.Add(fe.Query);
                // Dataverse's answer: only the rows the emitted filter selects, i.e. a row carrying a reference that the
                // query compares (attribute + value) - so a column the query never names selects nothing.
                var collection = new EntityCollection();
                collection.Entities.AddRange(rows.Where(r => r.Attributes.Any(a =>
                    a.Value is EntityReference reference
                    && fe.Query.Contains($"<condition attribute='{a.Key}' operator='eq' value='{reference.Id:D}' />"))));
                return collection;
            });

        var options = new MembershipOptions();
        new MembershipOptionsDefaults().PostConfigure(name: null, options);

        return new MembershipResolverService(
            discovery.Object, identity.Object, dataverse.Object, new Mock<ITenantCache>().Object, Options.Create(options),
            NullLogger<MembershipResolverService>.Instance);
    }

    private static string Condition(string field, Guid value) =>
        $"<condition attribute='{field}' operator='eq' value='{value:D}' />";

    private static MembershipDescriptor D(string field, string role, string identityType) =>
        new(field, role, identityType, identityType.ToLowerInvariant(), "auto");
}
