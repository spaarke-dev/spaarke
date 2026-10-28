// unified-access-control-r2 task 194b R4b-2 (owner round 131, the 086 ruling) and R4b-2c (owner round 136) — the hard-deny tables
// (sprk_storedfile, sprk_procedureassignment, sprk_ruleset, sprk_policy, sprk_policyversion): where a row's ordinary answer would
// be a business unit's default team, it goes to the restricted-content owner team instead — the one in the CUSTOMER's top
// business unit above the answer (the root's direct child), no members; a Secure answer and an answer that keeps the creator are
// unchanged; an answer in the root itself, a top unit that is the Secure Record unit, a parent chain that cannot be read or
// loops, and a team that cannot be used each refuse (fail closed). The REAL resolver over the shared ownership directory.

using FluentAssertions;
using Microsoft.Extensions.Logging;
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
    private static readonly Guid CustomerChildBu = FakeDirectory.CustomerChildBu;
    private static readonly Guid CustomerChildTeam = FakeDirectory.CustomerChildTeam;

    private static readonly Guid OrdinaryMatter = Guid.Parse("194b0086-0000-4000-8000-0000000000a1");
    private static readonly Guid SecureMatter = Guid.Parse("194b0086-0000-4000-8000-0000000000c1");
    private static readonly Guid Policy = Guid.Parse("194b0086-0000-4000-8000-0000000000d1");

    /// <summary>R4b-2c: a matter owned in a business unit of <see cref="CustomerChildBu"/> (below the customer's top unit).</summary>
    private static readonly Guid CustomerChildMatter = Guid.Parse("194b0086-0000-4000-8000-0000000000a2");

    /// <summary>R4b-2c: a matter owned in the ROOT business unit itself.</summary>
    private static readonly Guid RootMatter = Guid.Parse("194b0086-0000-4000-8000-0000000000a3");

    /// <summary>R4b-2c: a second customer's top business unit (a direct child of the root), its default team, its own
    /// restricted-content owner team, and a matter owned there.</summary>
    private static readonly Guid OtherCustomerBu = Guid.Parse("194b0086-0000-4000-8000-0000000000b1");
    private static readonly Guid OtherCustomerTeam = Guid.Parse("194b0086-0000-4000-8000-0000000000b2");
    private static readonly Guid OtherContentTeam = Guid.Parse("194b0086-0000-4000-8000-0000000000b3");
    private static readonly Guid OtherCustomerMatter = Guid.Parse("194b0086-0000-4000-8000-0000000000b4");

    /// <summary>R4b-2c: a user homed below the customer's top unit.</summary>
    private static readonly Guid CustomerUser = Guid.Parse("194b0086-0000-4000-8000-0000000000e1");
    private static readonly Guid CustomerUserOid = Guid.Parse("194b0086-0000-4000-8000-0000000000e2");

    private static FakeDirectory World(FakeDirectory? directory = null) => (directory ?? FakeDirectory.Standard())
        .WithRecord("sprk_matter", OrdinaryMatter, ChildBu, owningTeam: ChildTeam)
        .WithRecord("sprk_matter", SecureMatter, SecureBu, isSecure: true, owningTeam: SecureNamedTeam)
        .WithRecord("sprk_matter", CustomerChildMatter, CustomerChildBu, owningTeam: CustomerChildTeam)
        .WithRecord("sprk_matter", RootMatter, GeneralBu, owningTeam: GeneralTeam);

    /// <summary>A business unit, its default team and a matter it owns, for the fail-closed shapes.</summary>
    private static FakeDirectory WithMatterIn(FakeDirectory directory, Guid businessUnit, Guid? parent, Guid matter)
    {
        var team = Guid.NewGuid();
        return directory
            .WithBusinessUnit(businessUnit, "Unit " + businessUnit.ToString("N")[..6], parent)
            .WithTeam(team, businessUnit, isDefault: true, teamType: 0, "Unit default")
            .WithRecord("sprk_matter", matter, businessUnit, owningTeam: team);
    }

    private static Entity Row(string table, params (string Column, string Target, Guid Id)[] lookups)
    {
        var row = new Entity(table, Guid.NewGuid());
        foreach (var (column, target, id) in lookups)
            row[column] = new EntityReference(target, id);
        return row;
    }

    private static RecordOwnershipContext PolicyUnder(Guid matter) =>
        RecordOwnershipContext.ForChild(Row("sprk_policy", ("sprk_matter", "sprk_matter", matter)));

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

    [Fact(DisplayName = "R4b-2c must fire: an answer BELOW the customer's top unit takes that customer's team (the walk up to the root's direct child)")]
    public async Task AHardDenyRowUnderAMatterBelowTheCustomersTopUnit_IsOwnedByThatCustomersTeam()
    {
        var resolution = await AskAsync(World(), PolicyUnder(CustomerChildMatter));

        resolution.OwningTeamId.Should().Be(ContentTeam, "the team of the root's direct child above the answer");
        resolution.InPlaceOfDefaultTeamId.Should().Be(CustomerChildTeam, "the default team of the answer's own unit");
    }

    [Fact(DisplayName = "R4b-2c must fire: two customers' top units each own their own rows — never one customer's team for the other's")]
    public async Task TwoCustomerBusinessUnits_EachHaveTheirOwnTeam()
    {
        var directory = World()
            .WithBusinessUnit(OtherCustomerBu, "Customer Two", parent: GeneralBu)
            .WithTeam(OtherCustomerTeam, OtherCustomerBu, isDefault: true, teamType: 0, "Customer Two")
            .WithTeam(OtherContentTeam, OtherCustomerBu, isDefault: false, teamType: 0, RestrictedContentOwnerTeam.DefaultOwnerTeamName)
            .WithRecord("sprk_matter", OtherCustomerMatter, OtherCustomerBu, owningTeam: OtherCustomerTeam);
        var cache = new RecordOwnerLookupCache();

        var first = await AskAsync(directory, PolicyUnder(OrdinaryMatter) with { LookupCache = cache });
        var second = await AskAsync(directory, PolicyUnder(OtherCustomerMatter) with { LookupCache = cache });

        first.OwningTeamId.Should().Be(ContentTeam);
        second.OwningTeamId.Should().Be(OtherContentTeam, "the other customer's own team (the name matches in both units)");
        cache.RestrictedContentOwnerTeams.Select(t => (t.CustomerBusinessUnitId, t.OwnerTeamId)).Should().BeEquivalentTo(
            new (Guid?, Guid?)[] { (ChildBu, ContentTeam), (OtherCustomerBu, OtherContentTeam) }, "the pass reports each customer's team");
    }

    [Fact]
    public async Task APolicyVersionOfARestrictedPolicy_IsOwnedByTheRestrictedContentTeam()
    {
        var directory = World().WithRecord("sprk_policy", Policy, ChildBu, owningTeam: ContentTeam,
            extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", OrdinaryMatter) });

        var resolution = await AskAsync(directory, RecordOwnershipContext.ForChild(Row("sprk_policyversion", ("sprk_policy", "sprk_policy", Policy))));

        resolution.OwningTeamId.Should().Be(ContentTeam);
        resolution.InPlaceOfDefaultTeamId.Should().Be(ChildTeam, "its parent's business unit (the customer's) default team");
    }

    [Fact]
    public async Task AHardDenyRowUnderASecureMatter_IsOwnedByTheSecureTeam()
    {
        var resolution = await AskAsync(World(), PolicyUnder(SecureMatter));

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
    public async Task AnUnfiledHardDenyRow_ThatKeepsItsCreator_IsUnchanged_AndOneWithACustomerCaller_TakesTheRestrictedContentTeam()
    {
        var directory = World().WithUser(CustomerUser, CustomerUserOid, CustomerChildBu);

        var kept = await AskAsync(directory, RecordOwnershipContext.ForChild(Row("sprk_policy")) with { WhenUnfiled = UnfiledOwnership.KeepCreator });
        var acting = await AskAsync(directory,
            RecordOwnershipContext.ForChild(Row("sprk_policy")) with { CallerObjectId = CustomerUserOid });

        kept.Outcome.Should().Be(RecordOwnerOutcome.Unchanged, "a kept creator is never replaced (rule 1)");
        acting.OwningTeamId.Should().Be(ContentTeam, "the acting user's business-unit default team is the ordinary answer it replaces");
        acting.InPlaceOfDefaultTeamId.Should().Be(CustomerChildTeam);
    }

    [Fact(DisplayName = "R4b-2c must fire: an unfiled hard-deny row created by a user homed in the ROOT is refused — customer records never go to the root")]
    public async Task AnUnfiledHardDenyRow_ByAUserHomedInTheRoot_Refuses()
    {
        var acting = await AskAsync(World(),
            RecordOwnershipContext.ForChild(Row("sprk_policy")) with { CallerObjectId = FakeDirectory.CallerOid });

        acting.Outcome.Should().Be(RecordOwnerOutcome.Refused);
        acting.RefusalCode.Should().Be(RecordOwnerRefusal.RestrictedContentOwnerTeamUnresolved);
        acting.Reason.Should().Contain(nameof(RestrictedContentOwnerTeamStatus.AnswerIsRootBusinessUnit));
    }

    // ── Fail closed ──────────────────────────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> UnusableTeams => new()
    {
        "missing", "member", "only-in-root", "two", "answer-in-root", "under-secure", "loop", "unreadable",
    };

    [Theory]
    [MemberData(nameof(UnusableTeams))]
    public async Task AnUnusableRestrictedContentTeam_Refuses_NeverTheDefaultTeam(string why)
    {
        var matter = OrdinaryMatter;
        var (loopA, loopB, orphan, underSecure) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var directory = why switch
        {
            "missing" => World(FakeDirectory.Standard(withRestrictedContentTeam: false)),
            "member" => World().WithTeamMember(ContentTeam, FakeDirectory.CallerUserId),
            "only-in-root" => World(FakeDirectory.Standard(withRestrictedContentTeam: false)
                .WithTeam(ContentTeam, GeneralBu, isDefault: false, teamType: 0, RestrictedContentOwnerTeam.DefaultOwnerTeamName)),
            "two" => World().WithTeam(Guid.NewGuid(), ChildBu, isDefault: false, teamType: 0, RestrictedContentOwnerTeam.DefaultOwnerTeamName),
            "answer-in-root" => World(),
            "under-secure" => WithMatterIn(World(), underSecure, SecureBu, matter = Guid.NewGuid()),
            "loop" => WithMatterIn(World(), loopA, loopB, matter = Guid.NewGuid()).WithBusinessUnit(loopB, "Loop B", parent: loopA),
            "unreadable" => WithMatterIn(World(), Guid.NewGuid(), orphan, matter = Guid.NewGuid()),
            _ => throw new ArgumentOutOfRangeException(nameof(why)),
        };
        if (why == "answer-in-root")
            matter = RootMatter;

        var resolution = await AskAsync(directory, PolicyUnder(matter));

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Refused, why);
        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.RestrictedContentOwnerTeamUnresolved);
        resolution.Reason.Should().Contain(why switch
        {
            "missing" or "only-in-root" => nameof(RestrictedContentOwnerTeamStatus.OwnerTeamNotFound),
            "member" => nameof(RestrictedContentOwnerTeamStatus.OwnerTeamHasMembers),
            "two" => nameof(RestrictedContentOwnerTeamStatus.OwnerTeamAmbiguous),
            "answer-in-root" => nameof(RestrictedContentOwnerTeamStatus.AnswerIsRootBusinessUnit),
            "under-secure" => nameof(RestrictedContentOwnerTeamStatus.CustomerBusinessUnitIsSecureRecord),
            "loop" => nameof(RestrictedContentOwnerTeamStatus.ParentChainLoops),
            _ => nameof(RestrictedContentOwnerTeamStatus.ParentChainUnreadable),
        });
    }

    [Fact(DisplayName = "R4b-2c: a refusal logs at Error with the status and the business units — and names nobody")]
    public async Task ARefusal_LogsAnError_ThatNamesNobody()
    {
        var log = new RecordingLog();
        var directory = World().WithTeamMember(ContentTeam, FakeDirectory.CallerUserId);

        await directory.Resolver(logger: log).ResolveOwnerAsync(PolicyUnder(OrdinaryMatter), CancellationToken.None);

        var error = log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Which.Message;
        error.Should().Contain(nameof(RestrictedContentOwnerTeamStatus.OwnerTeamHasMembers)).And.Contain(ChildBu.ToString());
        error.Should().NotContain(FakeDirectory.CallerUserId.ToString()).And.NotContain(FakeDirectory.CallerOid.ToString());
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
    public async Task TheWalkAndTheTeamAreReadOncePerPass_WhenTheQuestionCarriesThePassCache()
    {
        var directory = World();
        var cache = new RecordOwnerLookupCache();
        var question = PolicyUnder(OrdinaryMatter) with { LookupCache = cache };

        (await AskAsync(directory, question)).OwningTeamId.Should().Be(ContentTeam);
        cache.RestrictedContentOwnerTeams.Should().ContainSingle().Which.MembersFound.Should().Be(0, "the member check the pass reports");
        directory.BusinessUnitIdReads.Should().Be(2, "the walk: the matter's unit, then its parent (the root)");
        var memberReads = directory.QueriedEntities.Count(e => e == "teammembership");

        directory.WithTeamMember(ContentTeam, FakeDirectory.CallerUserId);
        (await AskAsync(directory, question)).OwningTeamId.Should().Be(ContentTeam, "the pass's answer, not read again");
        (await AskAsync(directory, PolicyUnder(CustomerChildMatter) with { LookupCache = cache })).OwningTeamId
            .Should().Be(ContentTeam, "another answer unit under the same customer: its walk is read, the team is the pass's");
        directory.QueriedEntities.Count(e => e == "teammembership").Should().Be(memberReads, "the team is identified once per customer unit");
        directory.BusinessUnitIdReads.Should().Be(2 + 3, "the first walk is the pass's; the new answer unit is walked (three units)");
        cache.RestrictedContentOwnerTeams.Should().ContainSingle("one customer unit, one team");

        (await AskAsync(directory, question with { LookupCache = new RecordOwnerLookupCache() })).IsRefused
            .Should().BeTrue("the next pass reads the team again and sees its member");
    }

    // ── The re-file ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReFilingAPolicyToAnotherOrdinaryMatter_KeepsItWithTheRestrictedContentTeam()
    {
        var directory = World()
            .WithRecord("sprk_policy", Policy, ChildBu, owningTeam: ContentTeam,
                extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", OrdinaryMatter) });
        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_policy",
            RecordId = Policy,
            ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_matter"] = new EntityReference("sprk_matter", CustomerChildMatter) },
        };

        var resolution = await directory.Resolver().ReparentAsync(
            request,
            _ =>
            {
                directory.Write("sprk_policy", Policy, new Dictionary<string, object?> { ["sprk_matter"] = new EntityReference("sprk_matter", CustomerChildMatter) });
                return Task.CompletedTask;
            },
            CancellationToken.None);

        resolution.OwningTeamId.Should().Be(ContentTeam);
        directory.Assignments.Should().NotContain(a => a.TeamId == GeneralTeam || a.TeamId == ChildTeam || a.TeamId == CustomerChildTeam);
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
        var (directory, resolution) = await ReFileAuthorOwnedAsync("sprk_policy", "sprk_matter", CustomerChildMatter);

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
        var (directory, resolution) = await ReFileAuthorOwnedAsync("sprk_event", "sprk_regardingmatter", RootMatter);

        resolution.OwningTeamId.Should().Be(GeneralTeam, "the kept-author rule is the hard-deny tables' only");
        directory.Assignments.Should().ContainSingle().Which.TeamId.Should().Be(GeneralTeam);
    }

    [Fact(DisplayName = "R4b-2c rule 1: an author-owned hard-deny row re-filed while the content team is missing keeps its author — written, not refused")]
    public async Task ReFilingAnAuthorOwnedPolicy_WhenTheContentTeamIsMissing_KeepsItsAuthor_AndWritesTheChange()
    {
        var (directory, resolution) = await ReFileAuthorOwnedAsync(
            "sprk_policy", "sprk_matter", CustomerChildMatter, World(FakeDirectory.Standard(withRestrictedContentTeam: false)));

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged, "author wins: the content team is never asked for in place of an author");
        resolution.Reason.Should().Be(RecordOwnershipContext.AuthorKeptRestrictedContentReason);
        directory.Assignments.Should().BeEmpty();
        directory.Row("sprk_policy", Policy).GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(CustomerChildMatter);
    }

    [Fact(DisplayName = "R4b-2c rule 1: the content team becoming unusable before the settle leaves an author-owned row with its author — no throw")]
    public async Task ReFilingAnAuthorOwnedPolicy_WhenTheContentTeamBecomesUnusableBeforeTheSettle_KeepsItsAuthor_WithoutThrowing()
    {
        var directory = World().WithRecord("sprk_policy", Policy, GeneralBu, extra: new()
        {
            ["sprk_matter"] = new EntityReference("sprk_matter", OrdinaryMatter),
            ["owninguser"] = new EntityReference("systemuser", FakeDirectory.CallerUserId),
            ["ownerid"] = new EntityReference("systemuser", FakeDirectory.CallerUserId),
        });
        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_policy",
            RecordId = Policy,
            ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_matter"] = new EntityReference("sprk_matter", CustomerChildMatter) },
        };

        var act = () => directory.Resolver().ReparentAsync(
            request,
            _ =>
            {
                directory.Write("sprk_policy", Policy, new Dictionary<string, object?> { ["sprk_matter"] = new EntityReference("sprk_matter", CustomerChildMatter) });
                directory.WithTeamMember(ContentTeam, FakeDirectory.CallerUserId); // the team is unusable from here on
                return Task.CompletedTask;
            },
            CancellationToken.None);

        var resolution = (await act.Should().NotThrowAsync()).Subject;
        resolution.Outcome.Should().Be(RecordOwnerOutcome.Unchanged);
        resolution.Reason.Should().Be(RecordOwnershipContext.AuthorKeptRestrictedContentReason);
        directory.Assignments.Should().BeEmpty();
        directory.Row("sprk_policy", Policy).GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(CustomerChildMatter);
    }

    [Fact(DisplayName = "R4b-2c must fire: a default-team-owned hard-deny row re-filed while the content team is missing is refused — the change is not written")]
    public async Task ReFilingADefaultTeamOwnedPolicy_WhenTheContentTeamIsMissing_Refuses()
    {
        var directory = World(FakeDirectory.Standard(withRestrictedContentTeam: false))
            .WithRecord("sprk_policy", Policy, ChildBu, owningTeam: ChildTeam,
                extra: new() { ["sprk_matter"] = new EntityReference("sprk_matter", OrdinaryMatter) });
        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_policy",
            RecordId = Policy,
            ParentChanges = new Dictionary<string, EntityReference?> { ["sprk_matter"] = new EntityReference("sprk_matter", CustomerChildMatter) },
        };
        var written = false;

        var resolution = await directory.Resolver().ReparentAsync(
            request,
            _ =>
            {
                written = true;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        resolution.Outcome.Should().Be(RecordOwnerOutcome.Refused);
        resolution.RefusalCode.Should().Be(RecordOwnerRefusal.RestrictedContentOwnerTeamUnresolved);
        written.Should().BeFalse();
        directory.Assignments.Should().BeEmpty();
    }

    private sealed class RecordingLog : ILogger<RecordOwnershipResolver>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
