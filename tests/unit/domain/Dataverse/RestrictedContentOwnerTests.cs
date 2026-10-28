// unified-access-control-r2 task 194b R4b-2 (owner round 131, the 086 ruling) — the hard-deny tables (sprk_storedfile,
// sprk_procedureassignment, sprk_ruleset, sprk_policy, sprk_policyversion): where a row's ordinary answer would be a business
// unit's default team, it goes to the restricted-content owner team instead (root business unit, no members); a Secure answer
// and an answer that keeps the creator are unchanged; a team that cannot be used refuses (fail closed). The REAL resolver over
// the shared ownership directory.

using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using FakeDirectory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

[Trait("status", "task-194b-uac-r2")]
public class RestrictedContentOwnerTests
{
    private static readonly Guid GeneralBu = FakeDirectory.GeneralBu;
    private static readonly Guid ChildBu = FakeDirectory.ChildBu;
    private static readonly Guid SecureBu = FakeDirectory.SecureBu;
    private static readonly Guid GeneralTeam = FakeDirectory.GeneralTeam;
    private static readonly Guid ChildTeam = FakeDirectory.ChildTeam;
    private static readonly Guid SecureNamedTeam = FakeDirectory.SecureNamedTeam;
    private static readonly Guid ContentTeam = FakeDirectory.RestrictedContentTeam;

    private static readonly Guid OrdinaryMatter = Guid.Parse("194b0086-0000-4000-8000-0000000000a1");
    private static readonly Guid SecureMatter = Guid.Parse("194b0086-0000-4000-8000-0000000000c1");
    private static readonly Guid Policy = Guid.Parse("194b0086-0000-4000-8000-0000000000d1");

    private static FakeDirectory World(FakeDirectory? directory = null) => (directory ?? FakeDirectory.Standard())
        .WithRecord("sprk_matter", OrdinaryMatter, ChildBu, owningTeam: ChildTeam)
        .WithRecord("sprk_matter", SecureMatter, SecureBu, isSecure: true, owningTeam: SecureNamedTeam);

    private static Entity Row(string table, params (string Column, string Target, Guid Id)[] lookups)
    {
        var row = new Entity(table, Guid.NewGuid());
        foreach (var (column, target, id) in lookups)
            row[column] = new EntityReference(target, id);
        return row;
    }

    private static Task<RecordOwnerResolution> AskAsync(FakeDirectory directory, RecordOwnershipContext context) =>
        directory.Resolver().ResolveOwnerAsync(context, CancellationToken.None);

    // ── The set and the question ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheHardDenySet_IsExactlyTheFiveTables_AndTheBuildersNameTheRowsTable()
    {
        RecordOwnershipResolver.RestrictedContentTables.Should().BeEquivalentTo(
            "sprk_storedfile", "sprk_procedureassignment", "sprk_ruleset", "sprk_policy", "sprk_policyversion");
        RecordOwnershipResolver.IsRestrictedContentTable("SPRK_Policy ").Should().BeTrue();
        RecordOwnershipResolver.IsRestrictedContentTable("sprk_document").Should().BeFalse();
        RecordOwnershipResolver.IsRestrictedContentTable(null).Should().BeFalse();

        RecordOwnershipContext.ForChild(Row("sprk_policy", ("sprk_matter", "sprk_matter", OrdinaryMatter)))
            .ChildEntityLogicalName.Should().Be("sprk_policy");
        new RecordOwnershipContext { Parents = new[] { new RecordOwnershipParent("sprk_matter", OrdinaryMatter) } }
            .ChildEntityLogicalName.Should().BeNull("a hand-built question names no table: its answers are as they were");
    }

    [Fact]
    public async Task TheReconcilersQuestion_NamesTheRowsTable()
    {
        var row = Row("sprk_policyversion", ("sprk_policy", "sprk_policy", Policy));

        var question = await RecordOwnershipContext.ForExistingRowAsync(
            row, new ExistingRowReads(Moq.Mock.Of<Spaarke.Dataverse.IGenericEntityService>(), null, null), CancellationToken.None);

        question.Context.ChildEntityLogicalName.Should().Be("sprk_policyversion");
    }

    // ── The answer ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sprk_policy")]
    [InlineData("sprk_storedfile")]
    [InlineData("sprk_procedureassignment")]
    [InlineData("sprk_ruleset")]
    public async Task AHardDenyRowUnderAnOrdinaryMatter_IsOwnedByTheRestrictedContentTeam_InPlaceOfTheDefaultTeam(string table)
    {
        var resolution = await AskAsync(World(), RecordOwnershipContext.ForChild(Row(table, ("sprk_matter", "sprk_matter", OrdinaryMatter))));

        resolution.OwningTeamId.Should().Be(ContentTeam, "the ordinary answer (the matter's default team) is replaced");
        resolution.IsSecureOwner.Should().BeFalse();
        resolution.InPlaceOfDefaultTeamId.Should().Be(ChildTeam, "the default team it replaced is recorded");
    }

    [Fact]
    public async Task APolicyVersionOfARestrictedPolicy_IsOwnedByTheRestrictedContentTeam()
    {
        var directory = World().WithRecord("sprk_policy", Policy, GeneralBu, owningTeam: ContentTeam,
            extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", OrdinaryMatter) });

        var resolution = await AskAsync(directory, RecordOwnershipContext.ForChild(Row("sprk_policyversion", ("sprk_policy", "sprk_policy", Policy))));

        resolution.OwningTeamId.Should().Be(ContentTeam);
        resolution.InPlaceOfDefaultTeamId.Should().Be(GeneralTeam, "its parent's business unit (the root) default team");
    }

    [Fact]
    public async Task AHardDenyRowUnderASecureMatter_IsOwnedByTheSecureTeam()
    {
        var resolution = await AskAsync(World(), RecordOwnershipContext.ForChild(Row("sprk_policy", ("sprk_matter", "sprk_matter", SecureMatter))));

        resolution.OwningTeamId.Should().Be(SecureNamedTeam);
        resolution.IsSecureOwner.Should().BeTrue();
        resolution.InPlaceOfDefaultTeamId.Should().BeNull();
    }

    [Fact]
    public async Task ARowOfAnotherTable_KeepsTheDefaultTeam()
    {
        var resolution = await AskAsync(World(), RecordOwnershipContext.ForChild(Row("sprk_event", ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter))));

        resolution.OwningTeamId.Should().Be(ChildTeam);
        resolution.InPlaceOfDefaultTeamId.Should().BeNull();
    }

    [Fact]
    public async Task AnUnfiledHardDenyRow_ThatKeepsItsCreator_IsUnchanged_AndOneWithACaller_TakesTheRestrictedContentTeam()
    {
        var kept = await AskAsync(World(), RecordOwnershipContext.ForChild(Row("sprk_policy")) with { WhenUnfiled = UnfiledOwnership.KeepCreator });
        var acting = await AskAsync(World(),
            RecordOwnershipContext.ForChild(Row("sprk_policy")) with { CallerObjectId = FakeDirectory.CallerOid });

        kept.Outcome.Should().Be(RecordOwnerOutcome.Unchanged, "a kept creator is never replaced (rule 1)");
        acting.OwningTeamId.Should().Be(ContentTeam, "the acting user's business-unit default team is the ordinary answer it replaces");
        acting.InPlaceOfDefaultTeamId.Should().Be(GeneralTeam);
    }

    // ── Fail closed ──────────────────────────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> UnusableTeams => new() { "missing", "member", "outside-root", "two-roots" };

    [Theory]
    [MemberData(nameof(UnusableTeams))]
    public async Task AnUnusableRestrictedContentTeam_Refuses_NeverTheDefaultTeam(string why)
    {
        var directory = why switch
        {
            "missing" => World(FakeDirectory.Standard(withRestrictedContentTeam: false)),
            "member" => World().WithTeamMember(ContentTeam, FakeDirectory.CallerUserId),
            "outside-root" => World(FakeDirectory.Standard(withRestrictedContentTeam: false)
                .WithTeam(ContentTeam, ChildBu, isDefault: false, teamType: 0, RestrictedContentOwnerTeam.DefaultOwnerTeamName)),
            "two-roots" => World().WithBusinessUnit(Guid.NewGuid(), "A second root"),
            _ => throw new ArgumentOutOfRangeException(nameof(why)),
        };

        var resolution = await AskAsync(directory, RecordOwnershipContext.ForChild(Row("sprk_policy", ("sprk_matter", "sprk_matter", OrdinaryMatter))));

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Refused, why);
        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.RestrictedContentOwnerTeamUnresolved);
        resolution.Reason.Should().Contain(why switch
        {
            "missing" => nameof(RestrictedContentOwnerTeamStatus.OwnerTeamNotFound),
            "member" => nameof(RestrictedContentOwnerTeamStatus.OwnerTeamHasMembers),
            "outside-root" => nameof(RestrictedContentOwnerTeamStatus.OwnerTeamNotInRootBusinessUnit),
            _ => nameof(RestrictedContentOwnerTeamStatus.RootBusinessUnitAmbiguous),
        });
    }

    [Fact]
    public async Task AnUnusableRestrictedContentTeam_RefusesNothingElse()
    {
        var resolution = await AskAsync(World(FakeDirectory.Standard(withRestrictedContentTeam: false)),
            RecordOwnershipContext.ForChild(Row("sprk_event", ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter))));

        resolution.OwningTeamId.Should().Be(ChildTeam);
    }

    [Fact]
    public async Task TheOntologyPublishQuestion_NamesNoTable_SoItsAnswerIsAsBefore_EvenWithNoUsableTeam()
    {
        // OntologyAdminAuthoringService.PublishVersionAsync asks a hand-built question (the rule and its matters, KeepCreator)
        // and binds no owner on an ordinary answer: the version stays its author's (rule 1). Naming no table, that question is
        // never answered with, or refused for, the restricted-content team.
        var directory = World(FakeDirectory.Standard(withRestrictedContentTeam: false)).WithRecord("sprk_policy", Policy, ChildBu,
            owningTeam: ChildTeam, extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", OrdinaryMatter) });

        var resolution = await AskAsync(directory, new RecordOwnershipContext
        {
            Parents = new[] { new RecordOwnershipParent("sprk_policy", Policy), new RecordOwnershipParent("sprk_matter", OrdinaryMatter) },
            WhenUnfiled = UnfiledOwnership.KeepCreator,
        });

        resolution.OwningTeamId.Should().Be(ChildTeam);
        resolution.IsSecureOwner.Should().BeFalse();
    }

    [Fact]
    public async Task TheTeamIsIdentifiedOncePerPass_WhenTheQuestionCarriesThePassCache()
    {
        var directory = World();
        var cache = new RecordOwnerLookupCache();
        var question = RecordOwnershipContext.ForChild(Row("sprk_policy", ("sprk_matter", "sprk_matter", OrdinaryMatter))) with { LookupCache = cache };

        (await AskAsync(directory, question)).OwningTeamId.Should().Be(ContentTeam);
        cache.RestrictedContentOwnerTeam.Should().NotBeNull();
        cache.RestrictedContentOwnerTeam!.MembersFound.Should().Be(0, "the member check the pass reports");

        directory.WithTeamMember(ContentTeam, FakeDirectory.CallerUserId);
        (await AskAsync(directory, question)).OwningTeamId.Should().Be(ContentTeam, "the pass's answer, not read again");
        (await AskAsync(directory, question with { LookupCache = new RecordOwnerLookupCache() })).IsRefused
            .Should().BeTrue("the next pass reads the team again and sees its member");
    }

    // ── The re-file ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReFilingAPolicyToAnotherOrdinaryMatter_KeepsItWithTheRestrictedContentTeam()
    {
        var otherMatter = Guid.NewGuid();
        var directory = World()
            .WithRecord("sprk_matter", otherMatter, GeneralBu, owningTeam: GeneralTeam)
            .WithRecord("sprk_policy", Policy, GeneralBu, owningTeam: ContentTeam,
                extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", OrdinaryMatter) });
        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_policy",
            RecordId = Policy,
            ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_matter"] = new EntityReference("sprk_matter", otherMatter) },
        };

        var resolution = await directory.Resolver().ReparentAsync(
            request,
            _ =>
            {
                directory.Write("sprk_policy", Policy, new Dictionary<string, object?> { ["sprk_matter"] = new EntityReference("sprk_matter", otherMatter) });
                return Task.CompletedTask;
            },
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ContentTeam);
        directory.Assignments.Should().NotContain(a => a.TeamId == GeneralTeam || a.TeamId == ChildTeam);
    }

    /// <summary>R4b-2b (rule 1): a row of <paramref name="table"/> owned by its author (the caller), filed under the ordinary
    /// matter, re-filed under <paramref name="to"/>.</summary>
    private static async Task<(FakeDirectory Directory, RecordOwnerResolution Resolution)> ReFileAuthorOwnedAsync(
        string table, string column, Guid to, FakeDirectory? seeded = null)
    {
        var directory = (seeded ?? World()).WithRecord(table, Policy, GeneralBu, extra: new()
        {
            [column] = new EntityReference("sprk_matter", OrdinaryMatter),
            ["owninguser"] = new EntityReference("systemuser", FakeDirectory.CallerUserId),
            ["ownerid"] = new EntityReference("systemuser", FakeDirectory.CallerUserId),
        });
        var request = new RecordReparent
        {
            EntityLogicalName = table,
            RecordId = Policy,
            ParentChanges = new Dictionary<string, EntityReference?> { [column] = new EntityReference("sprk_matter", to) },
        };

        var resolution = await directory.Resolver().ReparentAsync(
            request,
            _ =>
            {
                directory.Write(table, Policy, new Dictionary<string, object?> { [column] = new EntityReference("sprk_matter", to) });
                return Task.CompletedTask;
            },
            CancellationToken.None);
        return (directory, resolution);
    }

    [Fact]
    public async Task ReFilingAnAuthorOwnedPolicyToAnotherOrdinaryMatter_KeepsItsAuthor()
    {
        var otherMatter = Guid.NewGuid();
        var (directory, resolution) = await ReFileAuthorOwnedAsync(
            "sprk_policy", "sprk_matter", otherMatter, World().WithRecord("sprk_matter", otherMatter, GeneralBu, owningTeam: GeneralTeam));

        resolution.IsOwned.Should().BeFalse("rule 1: the content team never replaces a kept creator");
        resolution.Reason.Should().Be(RecordOwnershipContext.AuthorKeptRestrictedContentReason);
        directory.Assignments.Should().BeEmpty("the author keeps the row, as the ordinary pass keeps it");
    }

    [Fact]
    public async Task ReFilingAnAuthorOwnedPolicyUnderASecureMatter_StillTakesItIntoIsolation()
    {
        var (directory, resolution) = await ReFileAuthorOwnedAsync("sprk_policy", "sprk_matter", SecureMatter);

        resolution.OwningTeamId.Should().Be(SecureNamedTeam, "a Secure answer is never a kept creator's");
        directory.Assignments.Should().ContainSingle().Which.TeamId.Should().Be(SecureNamedTeam);
    }

    [Fact]
    public async Task ReFilingAnAuthorOwnedRowOfAnotherTable_IsStillReOwnedToTheDefaultTeam()
    {
        var otherMatter = Guid.NewGuid();
        var (directory, resolution) = await ReFileAuthorOwnedAsync(
            "sprk_event", "sprk_regardingmatter", otherMatter,
            World().WithRecord("sprk_matter", otherMatter, GeneralBu, owningTeam: GeneralTeam));

        resolution.OwningTeamId.Should().Be(GeneralTeam, "the kept-author rule is the hard-deny tables' only");
        directory.Assignments.Should().ContainSingle().Which.TeamId.Should().Be(GeneralTeam);
    }
}
