using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.DecisionCapabilityFixtures;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// spaarkeai-mcp-bff-api-r1 task 016 (design §8 B-3, spec FR-07): the plan-driven lookup search. The plan, not the caller, picks the table
/// and its scope; budgets are the Signal's matter's only (D-18); every read is the caller's; the typed text never widens the filter; at
/// most 25 candidates; a fault is a failure, never a partial list. The Signal access check and the plan resolver are the real services
/// over the module-boundary fakes of <see cref="DecisionCapabilityFixtures"/>.
/// </summary>
public sealed class DecisionLookupSearchTests
{
    // ── Each action type searches the table its plan names ──────────────────────────────────────────────────────

    public static TheoryData<string, string, string, string, string> DecideLaneLookups => new()
    {
        // plan, parameter code, typed text, table, entity set read
        { """{"actions":["revise-budget"]}""", "budget", "Out", "sprk_budget", "sprk_budgets?" },
        { """{"actions":["send-budget-inquiry"]}""", "outsideFirm", "Ac", "sprk_organization", "sprk_organizations?" },
        { """{"actions":["assign-task"]}""", "assignee", "Ac", "contact", "contacts?" },
        { """{"actions":["assign-work"]}""", "assignee", "Ac", "contact", "contacts?" },
        { """{"actions":["approve-variance"],"nextSteps":["add-todo"]}""", "assignee", "Ac", "contact", "contacts?" },
    };

    [Theory]
    [MemberData(nameof(DecideLaneLookups))]
    public async Task EachActionType_SearchesTheTableItsPlanNames_AsTheCaller_CappedAt25(string plan, string code, string query, string table, string set)
    {
        var (search, world, users, _) = Search(plan);

        var result = await search.SearchAsync(SignedIn(), SignalId, code, query, CancellationToken.None);

        result.Outcome.Should().Be(DecisionLookupOutcome.Ok);
        result.Response!.Entity.Should().Be(table);
        result.Response.ParameterCode.Should().Be(code);
        result.Response.Candidates.Select(c => c.Name).Should().NotBeEmpty().And.Equal(world.ExpectedNames(set, query).Take(DecisionLookupSearch.MaxResults));
        world.Paths.Should().ContainSingle().Which.Should().StartWith(set).And.EndWith("&$top=25");
        users.Verify(u => u.GetAsync(It.Is<string>(p => p.StartsWith(set, StringComparison.Ordinal)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Reassign_TheDoLanePlansAssignee_SearchesContacts()
    {
        // The Do lane's lookup parameter, from the plan alone (the Do-lane "mine" access path is DoLanePrivacyTests'; the read is shared).
        var plan = DecisionPlanService.Resolve("""{"actions":["reassign","mark-complete"]}""", DecisionLane.Do).Plan!;

        DecisionLookupSearch.TargetOf(plan, "assignee").Should().Be(DecisionLookupSearch.LookupTable.Contact);
        DecisionLookupSearch.TargetOf(plan, "note").Should().BeNull("a text parameter is not searchable");
    }

    [Fact]
    public async Task Contacts_CarryTheirEmail_AndOnlyActiveContactsAreAsked()
    {
        var (search, world, _, _) = Search("""{"actions":["assign-task"]}""");

        var result = await search.SearchAsync(SignedIn(), SignalId, "assignee", "Ac", CancellationToken.None);

        result.Response!.Candidates.Should().Contain(c => c.Name == "Ada Acme" && c.Email == "ada@acme.example");
        world.Paths.Single().Should().Contain("statecode eq 0", "only active contacts are offered");
    }

    // ── D-18: a budget belongs to the Signal's matter ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Other matter budget")] // the other matter's budget, typed exactly
    [InlineData("")]                    // no text: every budget the search can see
    public async Task ABudgetOfAnotherMatter_NeverAppears_EvenWhenTheQueryMatchesItExactly(string query)
    {
        var (search, world, _, _) = Search("""{"actions":["revise-budget"]}""");

        var result = await search.SearchAsync(SignedIn(), SignalId, "budget", query, CancellationToken.None);

        result.Outcome.Should().Be(DecisionLookupOutcome.Ok);
        result.Response!.Candidates.Select(c => c.Id).Should().NotContain(DecisionLookupWorld.OtherMatterBudget,
            "the search is scoped to the Signal's matter on the server, whatever was typed");
        world.Paths.Single().Should().Contain($"_sprk_matter_value eq {MatterId:D}");
        if (query.Length == 0)
        {
            result.Response.Candidates.Select(c => c.Id).Should().Equal([DecisionLookupWorld.MatterBudget],
                "with no text the matter's own budgets are listed (the Console pre-selects a single one)");
        }
    }

    [Fact]
    public void ABudgetSearch_ForASignalWithoutAMatter_ReadsNothing()
    {
        DecisionLookupSearch.PathFor(DecisionLookupSearch.LookupTable.MatterBudget, null, "Budget").Should().BeNull();
        DecisionLookupSearch.PathFor(DecisionLookupSearch.LookupTable.MatterBudget, Guid.Empty, "Budget").Should().BeNull();
    }

    // ── As the caller ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EveryLookupRow_IsReadAsTheCaller_AndNothingIsReadAppOnly()
    {
        // Row security is Dataverse's: a record the caller cannot read is left out because the read is the caller's (the world mirrors
        // that by never answering UnreadableContact). What this test proves is the half the search owns: the lookup rows come only from the
        // caller client, and the BFF's own identity (the strict app-only mock) reads nothing but the Signal's access and plan configuration.
        var (search, world, _, entities) = Search("""{"actions":["assign-task"]}""");

        var result = await search.SearchAsync(SignedIn(), SignalId, "assignee", "Ac", CancellationToken.None);

        result.Outcome.Should().Be(DecisionLookupOutcome.Ok);
        world.Paths.Should().ContainSingle().Which.Should().StartWith("contacts?");
        entities.Verify(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never,
            "no lookup row is read with the BFF's own identity");
        entities.Invocations.SelectMany(i => i.Arguments).OfType<string>()
            .Should().NotContain(new[] { "contact", "contacts", "sprk_budget", "sprk_budgets", "sprk_organization", "sprk_organizations" },
                "the app-only client touches configuration only (record type, policy version), never a lookup table");
    }

    // ── The typed text ──────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("o'brien")]
    [InlineData("x') or (statecode eq 1")]
    [InlineData("Ac') or contains(fullname,'")]
    [InlineData("a&$top=5000")]
    [InlineData("50% #1 + ?")]
    public async Task QueryText_WithQuotesOrODataOperators_IsOneEscapedLiteral_AndNeverWidensTheSearch(string query)
    {
        foreach (var (plan, code) in new[] { ("""{"actions":["revise-budget"]}""", "budget"), ("""{"actions":["assign-task"]}""", "assignee") })
        {
            var (search, world, _, _) = Search(plan);

            var result = await search.SearchAsync(SignedIn(), SignalId, code, query, CancellationToken.None);

            result.Outcome.Should().Be(DecisionLookupOutcome.Ok);
            var path = world.Paths.Single();
            DecisionLookupWorld.LiteralsOf(path).Should().OnlyContain(text => text == query, "the text is one literal, decoded back unchanged");
            Regex.Matches(path, @"contains\(\w+,'([^']*)'\)").Select(m => m.Groups[1].Value).Should().OnlyContain(
                raw => Regex.IsMatch(raw, "^([A-Za-z0-9._~-]|%[0-9A-F]{2})*$"),
                "inside the literal only URL-unreserved characters and %XX escapes: no quote, '&', '#', '?', '+' or bare '%'");
            Regex.Matches(path, Regex.Escape("$top=")).Should().ContainSingle("the text cannot add a query option");
            path.Should().EndWith("&$top=25");
            path.Count(ch => ch == '&').Should().Be(3, "$select, $filter, $orderby and $top only");
            if (code == "budget")
            {
                path.Should().Contain($"_sprk_matter_value eq {MatterId:D}");
                result.Response!.Candidates.Select(c => c.Id).Should().NotContain(DecisionLookupWorld.OtherMatterBudget);
            }
        }
    }

    [Theory]
    [InlineData("assignee", "%%")]           // LIKE "any text": would list every contact
    [InlineData("outsideFirm", "__")]        // LIKE "any two characters": would list every firm
    [InlineData("assignee", "a%e")]          // would match "Ada Acme"
    [InlineData("outsideFirm", "[A]corn")]   // a LIKE character class: would match "Acorn Partners"
    public async Task LikeWildcardsInTheText_AreLiteralCharacters_AndNeverWidenTheSearch(string code, string query)
    {
        var (search, world, _, _) = Search("""{"actions":["send-budget-inquiry","assign-task"]}""");

        var result = await search.SearchAsync(SignedIn(), SignalId, code, query, CancellationToken.None);

        result.Outcome.Should().Be(DecisionLookupOutcome.Ok);
        result.Response!.Candidates.Should().BeEmpty("no name contains the typed characters themselves");
        DecisionLookupWorld.LiteralsOf(world.Paths.Single()).Should().OnlyContain(text => text == query);
    }

    [Fact]
    public async Task ALiteralPercentInTheText_FindsTheNameThatContainsIt()
    {
        var (search, world, _, _) = Search("""{"actions":["send-budget-inquiry"]}""");

        var result = await search.SearchAsync(SignedIn(), SignalId, "outsideFirm", "50%", CancellationToken.None);

        result.Response!.Candidates.Select(c => c.Name).Should().Equal(["Rate 50% Partners"]);
        world.Paths.Single().Should().Contain(Uri.EscapeDataString("50[%]"));
    }

    [Fact]
    public async Task AQueryLongerThanTheLimit_IsAnInvalidParameter_AndNothingIsSearched()
    {
        var (search, world, _, _) = Search("""{"actions":["assign-task"]}""");

        var result = await search.SearchAsync(SignedIn(), SignalId, "assignee", new string('a', DecisionLookupSearch.MaxQueryLength + 1), CancellationToken.None);

        result.Should().Be(DecisionLookupResult.InvalidParameter);
        world.Paths.Should().BeEmpty();
    }

    [Theory]
    [InlineData("assignee", "")]
    [InlineData("assignee", "A")]
    [InlineData("outsideFirm", " a ")]
    public async Task AContactOrFirmSearch_ShorterThanTwoCharacters_ReturnsNone_WithoutARead(string code, string query)
    {
        var (search, world, _, _) = Search("""{"actions":["send-budget-inquiry","assign-task"]}""");

        var result = await search.SearchAsync(SignedIn(), SignalId, code, query, CancellationToken.None);

        result.Outcome.Should().Be(DecisionLookupOutcome.Ok);
        result.Response!.Candidates.Should().BeEmpty();
        world.Paths.Should().BeEmpty();
    }

    // ── The cap ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AtMost25Candidates_EvenWhenDataverseReturnsMore()
    {
        var (search, world, _, _) = Search("""{"actions":["send-budget-inquiry"]}""");
        world.IgnoreTop = true;

        var result = await search.SearchAsync(SignedIn(), SignalId, "outsideFirm", "Firm", CancellationToken.None);

        world.ExpectedNames("sprk_organizations?", "Firm").Count.Should().BeGreaterThan(DecisionLookupSearch.MaxResults);
        result.Response!.Candidates.Should().HaveCount(DecisionLookupSearch.MaxResults);
    }

    // ── Negatives: the plan decides what may be searched ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("contactid")]   // not a parameter of the plan
    [InlineData("budget")]      // a parameter of an action this plan does not offer
    [InlineData("amount")]      // a parameter of the plan, but not a person or a lookup
    [InlineData("Assignee")]    // codes are exact
    [InlineData(null)]
    [InlineData(" ")]
    public async Task AParameterCodeNotSearchableInThePlan_IsInvalidParameter_AndNothingIsSearched(string? code)
    {
        var (search, world, _, _) = Search("""{"actions":["assign-task","approve-variance"]}""");

        var result = await search.SearchAsync(SignedIn(), SignalId, code, "Ac", CancellationToken.None);

        result.Should().Be(DecisionLookupResult.InvalidParameter);
        world.Paths.Should().BeEmpty();
        var problem = DecisionResultMapper.ToHttp(result, SignedIn()).Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails;
        (problem.Status, problem.Extensions["reasonCode"]).Should().Be((422, (object?)"ontology.decision.invalid_parameter"));
        var tool = DecisionResultMapper.ToToolError(result)!;
        (tool.ReasonCode, tool.Status).Should().Be(("ontology.decision.invalid_parameter", 422), "REST and MCP share one vocabulary");
    }

    [Fact]
    public async Task APlanThatDoesNotResolve_IsThePlanRoutesRefusal()
    {
        var (search, world, _, _) = Search("""{"actions":["not-an-action"]}""");

        var result = await search.SearchAsync(SignedIn(), SignalId, "assignee", "Ac", CancellationToken.None);

        result.Outcome.Should().Be(DecisionLookupOutcome.Refused);
        world.Paths.Should().BeEmpty();
        var problem = DecisionResultMapper.ToHttp(result, SignedIn()).Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails;
        (problem.Status, problem.Extensions["reasonCode"]).Should().Be((422, (object?)DecisionPlanEndpoints.PlanRefusedReasonCode));
    }

    // ── Fail closed ─────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(500)]
    [InlineData(429)]
    [InlineData(403)]
    [InlineData(-1)]  // the read throws
    [InlineData(-2)]  // a 200 without a row list
    public async Task ASearchThatFails_IsUnavailable_NeverAPartialList(int status)
    {
        var (search, world, _, _) = Search("""{"actions":["revise-budget"]}""");
        world.Fault = status;

        var result = await search.SearchAsync(SignedIn(), SignalId, "budget", "Out", CancellationToken.None);

        result.Should().Be(DecisionLookupResult.Unavailable);
        result.Response.Should().BeNull();
        var problem = DecisionResultMapper.ToHttp(result, SignedIn()).Should().BeOfType<ProblemHttpResult>().Subject.ProblemDetails;
        (problem.Status, problem.Extensions["reasonCode"]).Should().Be((503, (object?)DecisionPlanEndpoints.AccessUnavailableReasonCode));
        DecisionResultMapper.ToToolError(result)!.Status.Should().Be(503);
    }

    [Fact]
    public async Task APlanThatCannotBeRead_IsUnavailable_AndNothingIsSearched()
    {
        var (access, _, users, entities) = DecideSignalOnAMatter();
        var world = new DecisionLookupWorld(users);
        var plans = new DecisionPlanService(entities.Object, NullLogger<DecisionPlanService>.Instance);
        entities.Setup(e => e.RetrieveAsync("sprk_policyversion", VersionId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException());

        var result = await new DecisionLookupSearch(access, plans, users.Object, NullLogger<DecisionLookupSearch>.Instance)
            .SearchAsync(SignedIn(), SignalId, "budget", "Out", CancellationToken.None);

        result.Should().Be(DecisionLookupResult.Unavailable);
        world.Paths.Should().BeEmpty();
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────────────────

    private static (DecisionLookupSearch Search, DecisionLookupWorld World, Mock<IDataverseUserClient> Users, Mock<IGenericEntityService> Entities)
        Search(string plan)
    {
        var (access, plans, users, entities) = DecideSignalOnAMatter(plan: plan);
        var world = new DecisionLookupWorld(users);
        return (new DecisionLookupSearch(access, plans, users.Object, NullLogger<DecisionLookupSearch>.Instance), world, users, entities);
    }
}

/// <summary>
/// Task 016: the caller's view of Dataverse for the lookup tables, on the strict caller client of <see cref="DecisionCapabilityFixtures"/>.
/// It evaluates the request it gets as Dataverse would (the matter filter, the name filter, <c>$top</c>), and returns only rows the caller
/// may read, so what a test sees is what the search's own request admits, not what the test chose: drop the matter clause from the
/// request and the other matter's budget comes back.
/// </summary>
internal sealed class DecisionLookupWorld
{
    public static readonly Guid MatterBudget = Guid.Parse("01600000-0000-4000-8000-0000000000c1");
    public static readonly Guid OtherMatterBudget = Guid.Parse("01600000-0000-4000-8000-0000000000c2");
    public static readonly Guid UnreadableContact = Guid.Parse("01600000-0000-4000-8000-0000000000d2");
    private static readonly Guid OtherMatter = Guid.Parse("01600000-0000-4000-8000-0000000000b1");

    private sealed record Row(Guid Id, string Name, Guid? Matter = null, string? Email = null, bool CallerReads = true);

    private readonly Dictionary<string, List<Row>> _rows = new()
    {
        ["sprk_budgets?"] =
        [
            new(MatterBudget, "Outside counsel 2026", DecisionCapabilityFixtures.MatterId),
            new(OtherMatterBudget, "Other matter budget", OtherMatter),
        ],
        ["contacts?"] =
        [
            new(Guid.Parse("01600000-0000-4000-8000-0000000000d1"), "Ada Acme", Email: "ada@acme.example"),
            new(UnreadableContact, "Acton Secret", Email: "acton@acme.example", CallerReads: false),
            new(Guid.Parse("01600000-0000-4000-8000-0000000000d3"), "Zed Other"),
        ],
        ["sprk_organizations?"] = Enumerable.Range(1, 30)
            .Select(i => new Row(Guid.Parse($"01600000-0000-4000-8000-0000000001{i:D2}"), $"Acme Firm {i:D2}"))
            .Prepend(new Row(Guid.Parse("01600000-0000-4000-8000-0000000000e1"), "Acorn Partners"))
            .Append(new Row(Guid.Parse("01600000-0000-4000-8000-0000000000e2"), "Rate 50% Partners"))
            .ToList(),
    };

    public DecisionLookupWorld(Mock<IDataverseUserClient> users)
    {
        foreach (var set in _rows.Keys)
        {
            users.Setup(u => u.GetAsync(It.Is<string>(p => p.StartsWith(set, StringComparison.Ordinal)), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken _) => Answer(set, path));
        }
    }

    /// <summary>Every lookup request the search sent, in order.</summary>
    public List<string> Paths { get; } = [];

    /// <summary>Answer every row the filter admits, ignoring <c>$top</c> (a server that does not cap).</summary>
    public bool IgnoreTop { get; set; }

    /// <summary>0: answer. A status: fail with it. -1: throw. -2: a 200 with no row list.</summary>
    public int Fault { get; set; }

    /// <summary>The names a correct search of <paramref name="set"/> for <paramref name="text"/> returns, in order.</summary>
    public List<string> ExpectedNames(string set, string text) =>
        _rows[set].Where(r => r.CallerReads && (set != "sprk_budgets?" || r.Matter == DecisionCapabilityFixtures.MatterId))
            .Where(r => r.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || (r.Email?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(r => r.Name, StringComparer.Ordinal)
            .Select(r => r.Name)
            .ToList();

    /// <summary>
    /// Every <c>contains(column,'…')</c> argument of a request path as the <c>LIKE</c> pattern Dataverse runs: URL-decoded, then OData
    /// quote doubling undone. <c>%</c>, <c>_</c> and <c>[…]</c> are still pattern syntax here.
    /// </summary>
    public static List<string> PatternsOf(string path) =>
        Regex.Matches(path, @"contains\((\w+),'([^']*)'\)")
            .Select(m => Uri.UnescapeDataString(m.Groups[2].Value).Replace("''", "'", StringComparison.Ordinal))
            .ToList();

    /// <summary>Every <c>contains()</c> argument as the characters it matches literally: the patterns with their <c>[x]</c> escapes undone.</summary>
    public static List<string> LiteralsOf(string path) =>
        PatternsOf(path).Select(p => Regex.Replace(p, @"\[([\[%_])\]", "$1")).ToList();

    /// <summary>Whether <paramref name="value"/> contains <paramref name="pattern"/> as SQL <c>LIKE '%pattern%'</c> evaluates it.</summary>
    private static bool LikeContains(string value, string pattern)
    {
        var regex = new System.Text.StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            var close = pattern[i] == '[' ? pattern.IndexOf(']', i + 1) : -1;
            if (close > i + 1)
            {
                regex.Append('[').Append(pattern[(i + 1)..close].Replace(@"\", @"\\", StringComparison.Ordinal)).Append(']');
                i = close;
            }
            else
            {
                regex.Append(pattern[i] switch { '%' => ".*", '_' => ".", var c => Regex.Escape(c.ToString()) });
            }
        }

        return Regex.IsMatch(value, regex.ToString(), RegexOptions.IgnoreCase | RegexOptions.Singleline);
    }

    private Task<DataverseUserResponse> Answer(string set, string path)
    {
        Paths.Add(path);
        switch (Fault)
        {
            case -1:
                throw new HttpRequestException("transport");
            case -2:
                return Task.FromResult(DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { error = "none" })));
            case > 0:
                return Task.FromResult(DataverseUserResponse.Fail(Fault, DataverseUserClientErrorCodes.ServiceError, "fault"));
        }

        var matter = Regex.Match(path, @"_sprk_matter_value eq ([0-9a-fA-F-]{36})");
        var patterns = PatternsOf(path);
        var top = Regex.Match(path, @"\$top=(\d+)");
        var rows = _rows[set]
            .Where(r => r.CallerReads)
            .Where(r => !matter.Success || r.Matter == Guid.Parse(matter.Groups[1].Value))
            .Where(r => patterns.Count == 0 || patterns.Any(p => LikeContains(r.Name, p) || (r.Email is { } email && LikeContains(email, p))))
            .OrderBy(r => r.Name, StringComparer.Ordinal)
            .Take(!IgnoreTop && top.Success ? int.Parse(top.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : int.MaxValue)
            .Select(r => set switch
            {
                "contacts?" => (object)new Dictionary<string, object?> { ["contactid"] = r.Id, ["fullname"] = r.Name, ["emailaddress1"] = r.Email },
                "sprk_budgets?" => new Dictionary<string, object?> { ["sprk_budgetid"] = r.Id, ["sprk_name"] = r.Name },
                _ => new Dictionary<string, object?> { ["sprk_organizationid"] = r.Id, ["sprk_organizationname"] = r.Name },
            })
            .ToList();
        return Task.FromResult(DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { value = rows })));
    }
}
