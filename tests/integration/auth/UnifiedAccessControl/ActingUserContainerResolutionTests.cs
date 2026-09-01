using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 — <c>RecordContainerResolver.ResolveForActingUserAsync</c>: the NO-RECORD
/// container question, answered server-side.
///
/// <para><b>What this path is for.</b> Task 076 established that twelve client sites resolve a container
/// BEFORE the owning record exists, and <c>composeEditor.registration.ts</c> shows matter-less drafting is
/// a designed flow rather than an edge case. Until this method existed, the only available answer was the
/// CLIENT resolving its own business-unit container and sending the id in the request body — the
/// client-named-container defect class this project exists to remove. There were SIX copies of that client
/// chain (two <c>xrmProvider.ts</c> implementations plus four wrappers).</para>
///
/// <para><b>The assertion that matters most is <see cref="ResolveForActingUser_LooksTheCallerUpByTheirEntraObjectId_NotBySystemUserId"/>.</b>
/// An Entra <c>oid</c> and a Dataverse <c>systemuserid</c> are different id spaces, and conflating them is
/// the exact defect class <c>CallerIdentityGuardTests.Rule2</c> (PR #840) was built to catch — a
/// <c>spaarkeai-compose-r8</c> disclosure came from comparing a resolved caller id against <c>ownerid</c>
/// without translation. This path must TRANSLATE (query <c>systemuser</c> by
/// <c>azureactivedirectoryobjectid</c>), never compare. A version that filtered on <c>systemuserid</c>
/// would return zero rows for every real caller and fail closed — safe, but it would break every
/// record-less upload, and the failure would look like a configuration problem.</para>
///
/// <para><b>Why the refusals are throws and not empty returns.</b> The resolver's fail-closed contract:
/// an indeterminate answer must never become "no container", because a call site can read that as "carry
/// on with the default". Exactly one empty result is legitimate — a business unit with no container
/// stamped, which is common (verified live 2026-08-27: three of six business units have
/// <c>sprk_containerid</c> unset) and mirrors the record path's identical case.</para>
///
/// <para><b>Mocking note.</b> This file uses NSubstitute to match its sibling
/// <c>RecordContainerResolverTests</c>, which substitutes the same two collaborators in the same idiom.
/// <c>tests/CLAUDE.md</c> names Moq as the codebase standard; introducing a second framework into one test
/// area would be worse than the deviation, and the substituted types are module boundaries
/// (<c>IGenericEntityService</c> / <c>ISecurableEntityRegistry</c>), not the class under test's internals.</para>
/// </summary>
public class ActingUserContainerResolutionTests
{
    private const string CallerOid = "7c9e6679-7425-40de-944b-e07fc1f90ae7";
    private const string BuContainer = "b!acting-user-bu-container-0000000";

    private static readonly Guid BusinessUnitId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact(DisplayName = "No record: the acting user's business-unit container is derived SERVER-side")]
    public async Task ResolveForActingUser_WhenTheBusinessUnitHasAContainer_ResolvesToIt()
    {
        var resolver = Build(userRows: [UserRow(BusinessUnitId)], buContainer: BuContainer);

        var decision = await resolver.ResolveForActingUserAsync(CallerOid);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedFallback);
        decision.ContainerId.Should().Be(
            BuContainer,
            "this is the whole point of the method — the SERVER answers the no-record container question, so "
            + "the client no longer has to send a container id it chose itself");
    }

    /// <summary>
    /// The id-space assertion. See the class remarks — this is the one that guards the #840 Rule 2 class.
    /// </summary>
    [Fact(DisplayName = "No record: the caller is looked up BY ENTRA OID, not by systemuserid (id-space translation)")]
    public async Task ResolveForActingUser_LooksTheCallerUpByTheirEntraObjectId_NotBySystemUserId()
    {
        var captured = new List<QueryExpression>();
        var resolver = Build(userRows: [UserRow(BusinessUnitId)], buContainer: BuContainer, capture: captured);

        await resolver.ResolveForActingUserAsync(CallerOid);

        var userQuery = captured.Should().ContainSingle(q => q.EntityName == "systemuser").Subject;

        var conditions = userQuery.Criteria.Conditions;

        conditions.Should().ContainSingle(c => c.AttributeName == "azureactivedirectoryobjectid",
            "the Entra oid is a LOOKUP KEY on the column Dataverse stores it in — this query is what "
            + "translates an oid into a systemuser, and translation is what #840 Rule 2 requires");

        conditions.Should().NotContain(c => c.AttributeName == "systemuserid",
            "filtering systemuserid by an oid compares two different id spaces. It would match nothing, fail "
            + "closed for every real caller, and read as a configuration fault rather than a code defect");

        conditions.Single(c => c.AttributeName == "azureactivedirectoryobjectid")
            .Values.Should().Contain(Guid.Parse(CallerOid),
                "the oid must reach the filter as a Guid — the column is a uniqueidentifier, and a string "
                + "would either throw or silently fail to match");
    }

    [Fact(DisplayName = "No record: a caller with NO Dataverse user is REFUSED, never given a default container")]
    public async Task ResolveForActingUser_WhenNoDataverseUserMatchesTheOid_Refuses()
    {
        var resolver = Build(userRows: [], buContainer: BuContainer);

        var act = () => resolver.ResolveForActingUserAsync(CallerOid);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.Code.Should().Be("acting_user_not_resolvable",
                "an unresolvable caller is an INDETERMINATE answer. Returning Unresolved would let a call "
                + "site treat it as 'no container configured' and carry on");
    }

    /// <summary>
    /// The ambiguity control. Two Dataverse users for one Entra oid means two candidate business units, so
    /// there are two candidate containers — and picking one silently is how content lands in the wrong place.
    /// </summary>
    [Fact(DisplayName = "No record: an oid matching MORE THAN ONE Dataverse user is REFUSED, not silently won")]
    public async Task ResolveForActingUser_WhenTheOidMatchesMultipleUsers_RefusesRatherThanChoosing()
    {
        var second = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var resolver = Build(
            userRows: [UserRow(BusinessUnitId), UserRow(second)], buContainer: BuContainer);

        var act = () => resolver.ResolveForActingUserAsync(CallerOid);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.Code.Should().Be("acting_user_ambiguous",
                "two users means two business units means two containers. A TOP 1 query would have picked "
                + "a winner by whatever order Dataverse happened to return");
    }

    [Fact(DisplayName = "No record: a Dataverse user with no business unit is REFUSED")]
    public async Task ResolveForActingUser_WhenTheUserHasNoBusinessUnit_Refuses()
    {
        var resolver = Build(userRows: [UserRow(businessUnitId: null)], buContainer: BuContainer);

        var act = () => resolver.ResolveForActingUserAsync(CallerOid);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.Code.Should().Be("acting_user_not_resolvable");
    }

    /// <summary>
    /// The ONE legitimate empty result — and the negative control for the refusals above. Without this the
    /// suite would be consistent with "throw on everything that is not a happy path", which would break the
    /// common case of a business unit that simply has no container stamped.
    /// </summary>
    [Fact(DisplayName = "No record: a business unit with NO container stamped yields Unresolved, NOT an exception")]
    public async Task ResolveForActingUser_WhenTheBusinessUnitHasNoContainer_IsUnresolvedNotAnError()
    {
        var resolver = Build(userRows: [UserRow(BusinessUnitId)], buContainer: null);

        var decision = await resolver.ResolveForActingUserAsync(CallerOid);

        decision.Outcome.Should().Be(
            ContainerDecisionOutcome.Unresolved,
            "three of six business units have no container stamped (verified live 2026-08-27). Throwing here "
            + "would turn a normal configuration state into an error");
        decision.ContainerId.Should().BeNull();
    }

    [Theory(DisplayName = "No record: an unusable caller id is REFUSED before any query runs")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task ResolveForActingUser_WithAnUnusableCallerId_Refuses(string? oid)
    {
        var captured = new List<QueryExpression>();
        var resolver = Build(userRows: [UserRow(BusinessUnitId)], buContainer: BuContainer, capture: captured);

        var act = () => resolver.ResolveForActingUserAsync(oid);

        await act.Should().ThrowAsync<SdapProblemException>();

        captured.Should().BeEmpty(
            "an unusable identity must be refused BEFORE the query. Guid.Empty in particular is the "
            + "fail-closed-by-construction case DataverseImpersonation already refuses");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers — a fixture scoped to the systemuser + businessunit reads this path makes.
    // Deliberately NOT the sibling file's Build(): that one answers RetrieveMultipleAsync with CLAIMANT
    // rows for the reverse lookup, which would silently satisfy a systemuser query with the wrong data.
    // ─────────────────────────────────────────────────────────────────────────────

    private static Entity UserRow(Guid? businessUnitId)
    {
        var row = new Entity("systemuser", Guid.NewGuid());

        if (businessUnitId is { } id)
        {
            row["businessunitid"] = new EntityReference("businessunit", id);
        }

        return row;
    }

    private static RecordContainerResolver Build(
        Entity[] userRows,
        string? buContainer,
        List<QueryExpression>? capture = null)
    {
        var registry = Substitute.For<ISecurableEntityRegistry>();
        var svc = Substitute.For<IGenericEntityService>();

        svc.RetrieveMultipleAsync(Arg.Any<QueryExpression>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var query = call.Arg<QueryExpression>();
                capture?.Add(query);

                var collection = new EntityCollection();

                if (query.EntityName == "systemuser")
                {
                    // TopCount is honoured so the production cap is exercised rather than bypassed — the
                    // ambiguity test supplies two rows and the query asks for two.
                    var cap = query.TopCount ?? int.MaxValue;
                    foreach (var row in userRows.Take(cap))
                    {
                        collection.Entities.Add(row);
                    }
                }

                return Task.FromResult(collection);
            });

        // The business-unit read. Returns a row that carries the container only when one was supplied, so
        // the "no container stamped" case is an ABSENT attribute rather than an empty string — which is how
        // Dataverse actually reports it (null-valued properties are omitted from the response).
        svc.RetrieveAsync("businessunit", Arg.Any<Guid>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var row = new Entity("businessunit", call.ArgAt<Guid>(1));

                if (!string.IsNullOrWhiteSpace(buContainer))
                {
                    row["sprk_containerid"] = buContainer;
                }

                return Task.FromResult(row);
            });

        return new RecordContainerResolver(
            registry, svc, NullLogger<RecordContainerResolver>.Instance);
    }
}
