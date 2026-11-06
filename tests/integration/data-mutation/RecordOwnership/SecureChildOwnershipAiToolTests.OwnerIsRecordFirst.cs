using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 017 review round 1 (F-1): the owner parity tests of the re-pointed callers put the
/// caller in <see cref="Directory.ChildBu"/>, the SAME unit as <c>OrdinaryMatter</c>, so they pass whether the owner is decided
/// record-first (the matter's team) or caller-first (the caller's unit's team). Here the matter belongs to ANOTHER unit than the
/// caller's, so the two answers differ, and each re-pointed caller (route, chat, budget inquiry, <c>email.draft</c>) must name
/// the MATTER's team. Must-fire: a <c>ResolveOwnerOverAsync</c> that gives <c>OwnerQuestionOf</c> no filed lookups, no pair
/// parent and no pair id (caller-first) makes every case below fail.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid OtherUnitMatter = Guid.Parse("a2460000-0000-4000-8000-0000000000d1");

    /// <summary>An ordinary matter in a unit BELOW the caller's (the caller is in <see cref="Directory.ChildBu"/>).</summary>
    private void TheMatterBelongsToAnotherUnitThanTheCaller() =>
        _world.WithRecord("sprk_matter", OtherUnitMatter, Directory.CustomerChildBu, isSecure: false,
            owningTeam: Directory.CustomerChildTeam);

    private static void TheOwnerIsTheMattersTeam(IReadOnlyDictionary<string, object?> fields)
    {
        Owner(fields).Should().Be(Directory.CustomerChildTeam, "record-first: the filed-under matter's unit's team");
        Owner(fields).Should().NotBe(Directory.ChildTeam, "not the caller's unit's team (caller-first)");
    }

    [Fact]
    public async Task OwnerIsRecordFirst_RouteCreate_OfATodoUnderAMatterInAnotherUnit_IsOwnedByTheMattersTeam_NotTheCallers()
    {
        TheMatterBelongsToAnotherUnitThanTheCaller();

        var route = await CreateChild("sprk_todo", new()
        {
            ["sprk_name"] = "Chase the revision",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OtherUnitMatter:D})",
        });

        Status(route).Should().Be(StatusCodes.Status201Created, Detail(route));
        TheOwnerIsTheMattersTeam(_appCreates.Should().ContainSingle().Subject.Fields);
    }

    [Fact]
    public async Task OwnerIsRecordFirst_ChatCreate_OfATodoUnderAMatterInAnotherUnit_IsOwnedByTheMattersTeam_NotTheCallers()
    {
        TheMatterBelongsToAnotherUnitThanTheCaller();

        var result = await CreateRecord("sprk_todo", Lookup("sprk_regardingmatter", "sprk_matter", OtherUnitMatter));

        result.Success.Should().BeTrue(result.ErrorMessage);
        TheOwnerIsTheMattersTeam(_appCreates.Should().ContainSingle().Subject.Fields);
    }

    [Fact]
    public async Task OwnerIsRecordFirst_BudgetInquiry_ForAMatterInAnotherUnit_IsOwnedByTheMattersTeam_NotTheCallers()
    {
        TheMatterBelongsToAnotherUnitThanTheCaller();
        var (executor, _) = InquiryExecutor();

        var result = await executor.ExecuteAsync(InquiryRequest(OtherUnitMatter), CancellationToken.None);

        result.Outcome.Should().Be(BudgetInquiryOutcome.Sent, result.Error);
        TheOwnerIsTheMattersTeam(_appCreates.Should().ContainSingle().Subject.Fields);
    }

    [Fact]
    public async Task OwnerIsRecordFirst_EmailDraft_RegardingAMatterInAnotherUnit_IsOwnedByTheMattersTeam_NotTheCallers()
    {
        TheMatterBelongsToAnotherUnitThanTheCaller();

        var result = await Draft(CoreAncestorResolverFixtures.Inert(), DraftCreation(), "sprk_matter", OtherUnitMatter);

        result.Success.Should().BeTrue(result.ErrorMessage);
        TheOwnerIsTheMattersTeam(TheDraftCreate());
    }
}
