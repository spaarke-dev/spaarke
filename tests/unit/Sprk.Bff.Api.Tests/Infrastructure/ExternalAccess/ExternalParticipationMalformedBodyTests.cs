// GitHub #2845 review, item 4 — a 2xx body WITHOUT a `value` array is not a Dataverse answer.
//
// NoAccessListReader.ParseRows read that shape as an empty page ("no denials"); the same shape was also read as an empty
// answer by three reads of ExternalParticipationService that restrict access:
//   - the contact's organization memberships (the deny veto's organization subjects) — empty = no organization wall;
//   - the id-column read behind the No Access share enforcer (walled contacts; secure / unflagged roots referencing an
//     organization) — empty = nothing to enforce;
//   - an organization's member page (the grant-cache invalidation after an organization grant changes) — empty = nobody
//     to invalidate;
//   - the colleague-by-email membership read (review pass 2) — empty = "not a member", where a real colleague must get the
//     503 fault instead.
// Each must report the read as unknown, through the signal its callers already treat as a fault. Must-not-fire control:
// an EMPTY value array is a real answer ("none"). Only the HTTP exchange is substituted (a hand-written handler, never
// Mock<HttpMessageHandler>: testing.md B1); the request building, parsing and fault contract are the real code.

using System.Net;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

public sealed class ExternalParticipationMalformedBodyTests
{
    private static readonly Guid Contact = Guid.Parse("28450000-0000-4000-8000-0000000004c1");
    private static readonly Guid Organization = Guid.Parse("28450000-0000-4000-8000-0000000004f1");

    public static TheoryData<string> BodiesWithoutAValueArray => new() { "{}", "{\"value\":null}", "{\"value\":{}}" };

    [Theory]
    [MemberData(nameof(BodiesWithoutAValueArray))]
    public async Task OrganizationMemberships_A2xxWithoutAValueArray_IsUnreadable(string body)
    {
        var memberships = await Service(body).ReadOrganizationMembershipsAsync(Contact, CancellationToken.None);

        memberships.Unreadable.Should().BeTrue("the deny veto must deny every candidate, not read 'no organizations'");
        memberships.WallSubjectOrganizationIds.Should().BeEmpty();
        memberships.ConferringOrganizationIds.Should().BeEmpty();
    }

    [Fact]
    public async Task OrganizationMemberships_AnEmptyValueArray_IsAProvenNone()
    {
        var memberships = await Service("{\"value\":[]}").ReadOrganizationMembershipsAsync(Contact, CancellationToken.None);

        memberships.Unreadable.Should().BeFalse();
        memberships.WallSubjectOrganizationIds.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(BodiesWithoutAValueArray))]
    public async Task ShareEnforcerReads_A2xxWithoutAValueArray_Throw(string body)
    {
        var service = Service(body);

        await FluentActions.Awaiting(() => service.FindWallMemberContactsAsync(Organization, 10, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>("'nobody is walled' cannot be read from no answer");
        await FluentActions.Awaiting(() => service.FindSecureRootsReferencingOrganizationAsync(
                "sprk_matter", Organization, 10, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => service.FindUnflaggedRootsReferencingOrganizationAsync(
                "sprk_matter", Organization, 10, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ShareEnforcerReads_AnEmptyValueArray_AreAProvenNone()
    {
        var (contacts, truncated) = await Service("{\"value\":[]}").FindWallMemberContactsAsync(Organization, 10, CancellationToken.None);

        contacts.Should().BeEmpty();
        truncated.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(BodiesWithoutAValueArray))]
    public async Task OrganizationMemberWalk_A2xxWithoutAValueArray_IsIncomplete(string body)
    {
        var walk = await Service(body).EnumerateActiveOrganizationMembersAsync(Organization, CancellationToken.None);

        walk.Complete.Should().BeFalse("members that were not read are not invalidated, and the walk must say so");
    }

    [Fact]
    public async Task OrganizationMemberWalk_AnEmptyValueArray_IsComplete()
    {
        var walk = await Service("{\"value\":[]}").EnumerateActiveOrganizationMembersAsync(Organization, CancellationToken.None);

        walk.Complete.Should().BeTrue();
        walk.ContactIds.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(BodiesWithoutAValueArray))]
    public async Task ColleaguesByEmail_A2xxWithoutAValueArray_IsUnreadable_NeverNobody(string body)
    {
        var match = await Service(body).FindConferringMembersByEmailAsync(
            new[] { Organization }, "colleague@firm.example", CancellationToken.None);

        match.Unreadable.Should().BeTrue("the caller must report the fault (503), not 'not a member'");
        match.ContactIds.Should().BeEmpty();
    }

    [Fact]
    public async Task ColleaguesByEmail_AnEmptyValueArray_IsAProvenNone()
    {
        var match = await Service("{\"value\":[]}").FindConferringMembersByEmailAsync(
            new[] { Organization }, "colleague@firm.example", CancellationToken.None);

        match.Unreadable.Should().BeFalse("an empty array is a real answer: no colleagues");
        match.ContactIds.Should().BeEmpty();
    }

    private static ExternalParticipationService Service(string body)
        => new(
            new HttpClient(new OkHandler(body)),
            Mock.Of<ITenantCache>(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com" })
                .Build(),
            new StaticTokenCredential(),
            Mock.Of<IHttpContextAccessor>(),
            NullLogger<ExternalParticipationService>.Instance,
            filing: null);

    /// <summary>Answers every request 200 with <paramref name="body"/>.</summary>
    private sealed class OkHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static AccessToken Token => new("test-token-not-a-credential", DateTimeOffset.UtcNow.AddHours(1));

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(Token);
    }
}
