using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Time.Testing;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Sprk.Bff.Api.Services.Signals;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Seam.Signals;

/// <summary>
/// Real-Dataverse seam for <see cref="PredicateCompiler"/> (spec FR-06, FR-07; design.md §8.0.1; task 021).
/// Reads the DEPLOYED Path B rule body from <c>sprk_policyversion</c>, compiles it with the production compiler,
/// and runs the ONE resulting FetchXML query against the task 005 seed in <c>spaarkedev1</c> through the same SDK
/// call the BFF makes (<c>ServiceClient.RetrieveMultipleAsync(FetchExpression)</c>). Read-only: no row is written.
/// </summary>
/// <remarks>
/// <para><b>Why this file exists (ADR-038, project CLAUDE.md §5).</b> The domain tests pin entity names, column
/// names, join paths and option values; that is exactly the kind of test that once pinned a non-existent column for
/// months. This seam is the real-schema pairing: the query runs against Dataverse, and
/// <see cref="CompilerAllowLists_MatchTheLiveSchemaAndTheCallersReadDepth"/> checks every allow-listed name, join and
/// read depth against live metadata. A broken anti-join returns every row or no row — both look like a working
/// predicate — so each test asserts membership AND non-membership, never merely "it returned something".</para>
/// <para><b>Clock.</b> Compiled with a <see cref="FakeTimeProvider"/> pinned to <see cref="SeedAnchor"/>, the instant
/// the hand-verified reference is anchored at. The seed (2026-10-03) therefore stays inside the 30-day window for as
/// long as the rows exist, instead of silently aging out on 2026-11-02 and reading as a compiler regression.</para>
/// <para><b>Skip-via-return, opt-in.</b> Same convention as <c>SpeAdmin/LiveIntegrationFixture</c>: unless
/// <see cref="UrlEnvVar"/> is set, every test returns immediately and nothing touches the network, so CI's plain
/// <c>dotnet test</c> runs zero live operations — and a pass in CI means "skipped", not "verified". Set it to the
/// environment URL; set <see cref="CallerIdEnvVar"/> to the writer principal to evaluate AS it (without it, the run
/// is as the operator, who is likely not trimmed by read depth). The credential is <see cref="DefaultAzureCredential"/>.
/// The seed it relies on is recorded in <c>projects/spaarke-ontology-platform-r1/notes/seed-data-state.md</c>.</para>
/// <para><b>Run status.</b> 2026-10-04: written, not run (the workstation's <c>DefaultAzureCredential</c> could not
/// obtain a token; the compiled strings were executed via <c>pac org fetch</c>, reference notes §3b). <b>2026-10-07
/// (task 024): all 10 tests run live and pass as the writer principal</b>, with
/// <c>AZURE_TOKEN_CREDENTIALS=AzureCliCredential</c> narrowing the credential chain to the Azure CLI login
/// (<c>notes/024-progress.md</c>).</para>
/// </remarks>
[Trait("status", "repaired")]
[Trait("Category", "Live")]
public sealed class SignalPredicateTests : IClassFixture<SignalPredicateTests.LiveDataverse>
{
    public const string UrlEnvVar = "SIGNALS_LIVE_DATAVERSE_URL";

    /// <summary>Optional systemuserid to impersonate (see <see cref="LiveDataverse.InitializeAsync"/>).</summary>
    public const string CallerIdEnvVar = "SIGNALS_LIVE_CALLER_ID";

    /// <summary>The window anchor of the hand-verified reference (window start 2026-09-04T02:15:00Z).</summary>
    private static readonly DateTimeOffset SeedAnchor = new(2026, 10, 4, 2, 15, 0, TimeSpan.Zero);

    // task 004 / task 005 seed (spaarkedev1). GUIDs are record ids, not secrets.
    private static readonly Guid PolicyVersionId = Guid.Parse("42b3e716-61bf-f111-aaaf-0022482913fc");
    private static readonly Guid PositiveMatter = Guid.Parse("2444af6d-e1f2-f011-8406-7ced8d1dc988");      // REAL-2026-123456.01
    private static readonly Guid NegativeControl1 = Guid.Parse("b68299c6-bafb-f011-8407-7c1e520aa4df");   // REAL-2026-123456.02 — in-window revision
    private static readonly Guid NegativeControl2 = Guid.Parse("d14d79f4-8fbf-f111-aaaf-0022482913fc");   // NC2 — no qualifying communication

    private readonly LiveDataverse _dv;
    private readonly ITestOutputHelper _out;
    private readonly PredicateCompiler _compiler = new(new RuleBodySchemaValidator(), new FakeTimeProvider(SeedAnchor));

    public SignalPredicateTests(LiveDataverse dv, ITestOutputHelper output)
    {
        _dv = dv;
        _out = output;
    }

    [Fact]
    public async Task DeployedPathBBody_ReturnsThePositiveMatter_AndNeitherNegativeControl()
    {
        if (!_dv.IsLive) return;

        var matters = await RunAsync(_compiler.Compile(await LiveBodyAsync()));

        matters.Should().Contain(PositiveMatter);
        matters.Should().NotContain(NegativeControl1, "a budget revision exists inside the window (notExists fails)");
        matters.Should().NotContain(NegativeControl2, "no qualifying communication exists (exists fails)");
    }

    [Fact]
    public async Task DeployedPathBBody_NarrowedToOneMatter_AnswersPerMatter()
    {
        if (!_dv.IsLive) return;

        var ruleBody = await LiveBodyAsync();

        (await RunAsync(_compiler.Compile(ruleBody, PositiveMatter))).Should().Equal(PositiveMatter);
        (await RunAsync(_compiler.Compile(ruleBody, NegativeControl1))).Should().BeEmpty();
        (await RunAsync(_compiler.Compile(ruleBody, NegativeControl2))).Should().BeEmpty();
    }

    [Fact]
    public async Task EmptyExistsSource_ReturnsNothing_NotEverything()
    {
        if (!_dv.IsLive) return;

        // The live body with its exists source emptied (a category id no row carries). An inverted join returns every
        // matter; a correct one returns none.
        var body = Mutate(await LiveBodyAsync(), exists: f => f["sprk_triagecategory"] = new JsonArray(Guid.NewGuid().ToString()));

        (await RunAsync(_compiler.Compile(body))).Should().BeEmpty();
    }

    [Fact]
    public async Task EmptyNotExistsSource_ReturnsExactlyTheExistsSet_NotEverythingAndNotNothing()
    {
        if (!_dv.IsLive) return;

        var live = await LiveBodyAsync();
        var existsOnly = await RunAsync(_compiler.Compile(Mutate(live, dropNotExists: true)));
        // notExists over a source no row can match: every subject passes that conjunct, so the answer must be exactly
        // the exists set.
        var body = Mutate(live, notExists: f =>
        {
            f.Clear();
            f["sprk_budgetrevisionid"] = Guid.NewGuid().ToString();
        });

        var matters = await RunAsync(_compiler.Compile(body));

        existsOnly.Should().Contain(new[] { PositiveMatter, NegativeControl1 });
        matters.Should().BeEquivalentTo(existsOnly);
        matters.Count.Should().BeLessThan(await _dv.CountMattersAsync());
    }

    [Fact]
    public async Task NotExistsWindow_IsAppliedInTheJoin_SoAnOutOfWindowRevisionDoesNotSuppress()
    {
        if (!_dv.IsLive) return;

        // Revision window = ">= now" (the pinned anchor, after NC1's 2026-10-03T14:00Z revision): NC1's revision is
        // OUT of this window, so NC1 must come back. Had the window leaked into the WHERE clause, every matter would vanish.
        var body = Mutate(await LiveBodyAsync(), notExists: f => f["sprk_revisedon"] = new JsonObject { [">="] = "now" });

        var matters = await RunAsync(_compiler.Compile(body));

        matters.Should().Contain(new[] { PositiveMatter, NegativeControl1 });
        matters.Should().NotContain(NegativeControl2);
    }

    [Fact]
    public async Task CompilerAllowLists_MatchTheLiveSchemaAndTheCallersReadDepth()
    {
        if (!_dv.IsLive) return;

        // Every verified join: the path is a Lookup on the clause entity that targets the subject.
        foreach (var ((subject, related), path) in PredicateCompiler.VerifiedJoins)
        {
            var attribute = await _dv.GetAttributeAsync(related, path);
            attribute.Should().BeOfType<LookupAttributeMetadata>($"{related}.{path} must be a lookup");
            ((LookupAttributeMetadata)attribute).Targets.Should().Contain(subject, $"{related}.{path} must target {subject}");
        }

        // Every allow-listed entity: the {logicalname}id primary-key convention the compiler relies on holds, and the
        // calling principal (the writer, when CallerIdEnvVar is set) reads it at Global depth.
        var principal = await _dv.WhoAmIAsync();
        _out.WriteLine($"principal: {principal}");
        foreach (var entity in PredicateCompiler.EvaluatorGlobalReadableEntities)
        {
            (await _dv.GetPrimaryIdAttributeAsync(entity)).Should().Be(entity + "id");
            (await _dv.GetReadDepthAsync(principal, entity)).Should().Be(PrivilegeDepth.Global, $"{entity} must be Global-read for {principal}");
        }
    }

    // ── Task 024 (D-16, D-40): the three Do-lane rules, compiled and run against the hand-written references ─

    /// <summary>The instant the Do-rule references are anchored at (notes/024-progress.md).</summary>
    private static readonly DateTimeOffset DoAnchor = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    // fixture, subject, a row that must come back, a row that must not (dev data as of 2026-10-07)
    [InlineData("do-overdue-task", "sprk_event", "edfef460-43bc-f111-aaaf-0022482913fc", "7300ed8f-8fbf-f111-aaaf-0022482913fc")] // due 09-30 / 10-04
    [InlineData("do-task-due-within-3-days", "sprk_event", "08021954-a0c1-f111-a05c-0022482913fc", "edfef460-43bc-f111-aaaf-0022482913fc")] // due 10-07 / overdue
    [InlineData("do-workassignment-past-due", "sprk_workassignment", "2dec2df5-551e-f111-88b3-7ced8d1dc988", "425fa95a-3060-f111-ab0b-7c1e521b425f")] // due 03-05 / no due date
    public async Task DoRuleFixture_ReturnsExactlyTheRowsOfTheHandWrittenFetchXml(string fixture, string subject, string inRow, string outRow)
    {
        if (!_dv.IsLive) return;

        var compiled = new PredicateCompiler(new RuleBodySchemaValidator(), new FakeTimeProvider(DoAnchor))
            .Compile(File.ReadAllText(FixturePath(fixture + ".rulebody.json")));
        var reference = File.ReadAllText(FixturePath(fixture + ".reference.fetchxml"));

        var fromCompiler = await RunAsync(compiled);
        var fromReference = await _dv.RunFetchAsync(reference, subject + "id");

        fromCompiler.Should().BeEquivalentTo(fromReference);
        fromCompiler.Should().Contain(Guid.Parse(inRow)).And.NotContain(Guid.Parse(outRow));
        fromCompiler.Count.Should().BeLessThan(await _dv.CountAsync(subject), "a rule that returns every row is not a filter");
    }

    [Fact]
    public async Task DateOnlyColumns_MatchTheLiveSchema_InBothDirections()
    {
        if (!_dv.IsLive) return;

        // Every sprk_ column with Format = DateOnly on an allow-listed table, read from live metadata. A column missing
        // from the catalog would be judged as an instant by the evaluator (off by a day near midnight, D-25).
        var live = new HashSet<(string, string)>();
        foreach (var entity in PredicateCompiler.EvaluatorGlobalReadableEntities)
        {
            foreach (var column in await _dv.GetDateOnlyColumnsAsync(entity))
            {
                live.Add((entity, column));
            }
        }

        PredicateCompiler.DateOnlyColumns.Should().BeEquivalentTo(live);
    }

    private static string FixturePath(string name)
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Program.cs")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("repository root not found"), "tests", "fixtures", "signals", name);
    }

    private async Task<string> LiveBodyAsync()
    {
        _out.WriteLine($"principal: {await _dv.WhoAmIAsync()}");
        return await _dv.ReadRuleBodyAsync(PolicyVersionId);
    }

    /// <summary>Variant of the DEPLOYED body (review F19): a stale hand copy cannot drift from what is live.</summary>
    private static string Mutate(string body, Action<JsonObject>? exists = null, Action<JsonObject>? notExists = null, bool dropNotExists = false)
    {
        var root = JsonNode.Parse(body)!.AsObject();
        var all = root["all"]!.AsArray();
        foreach (var clause in all.ToList())
        {
            var filter = clause!["filter"]!.AsObject();
            if (clause["exists"] is not null) exists?.Invoke(filter);
            if (clause["notExists"] is not null)
            {
                if (dropNotExists) all.Remove(clause);
                else notExists?.Invoke(filter);
            }
        }

        return root.ToJsonString();
    }

    private async Task<IReadOnlyList<Guid>> RunAsync(CompiledPredicate compiled)
    {
        var ids = await _dv.RunAsync(compiled);
        _out.WriteLine(compiled.FetchXml);
        _out.WriteLine($"-> {ids.Count} row(s): {string.Join(", ", ids)}");
        return ids;
    }

    /// <summary>One live <see cref="ServiceClient"/> per test class, or nothing at all when not opted in.</summary>
    public sealed class LiveDataverse : IAsyncLifetime
    {
        private ServiceClient? _client;

        public bool IsLive { get; } = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(UrlEnvVar));

        public Task InitializeAsync()
        {
            if (!IsLive)
            {
                return Task.CompletedTask;
            }

            var url = new Uri(Environment.GetEnvironmentVariable(UrlEnvVar)!.TrimEnd('/'));
            var scope = $"{url.GetLeftPart(UriPartial.Authority)}/.default";
            var credential = new DefaultAzureCredential();

            _client = new ServiceClient(
                instanceUrl: url,
                tokenProviderFunction: _ => Task.FromResult(
                    credential.GetToken(new TokenRequestContext(new[] { scope }), CancellationToken.None).Token),
                useUniqueInstance: true);

            if (!_client.IsReady)
            {
                throw new InvalidOperationException($"Dataverse ServiceClient not ready: {_client.LastError}");
            }

            // Optional: evaluate AS the principal that will run predicates in production (MSCRMCallerID), so
            // Dataverse trims rows by ITS read depth, not the operator's. The writer is
            // 3121bf1b-9fbf-f111-aaaf-0022482913fc (# mi-ontology-writer-dev, notes/security-roles.md §9).
            var callerId = Environment.GetEnvironmentVariable(CallerIdEnvVar);
            if (!string.IsNullOrWhiteSpace(callerId))
            {
                _client.CallerId = Guid.Parse(callerId);
            }

            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<Guid>> RunAsync(CompiledPredicate compiled)
        {
            var result = await _client!.RetrieveMultipleAsync(new FetchExpression(compiled.FetchXml));
            result.MoreRecords.Should().BeFalse("the seed is far below one page; paging is the evaluator's job (task 031)");
            return result.Entities.Select(e => e.GetAttributeValue<Guid>(compiled.SubjectIdAttribute)).ToList();
        }

        /// <summary>Runs a hand-written reference query (task 024) through the same SDK call.</summary>
        public async Task<IReadOnlyList<Guid>> RunFetchAsync(string fetchXml, string idAttribute)
        {
            var result = await _client!.RetrieveMultipleAsync(new FetchExpression(fetchXml));
            result.MoreRecords.Should().BeFalse("the dev data is far below one page");
            return result.Entities.Select(e => e.GetAttributeValue<Guid>(idAttribute)).ToList();
        }

        public async Task<int> CountAsync(string entity)
        {
            var result = await _client!.RetrieveMultipleAsync(new FetchExpression(
                $"""<fetch aggregate="true"><entity name="{entity}"><attribute name="{entity}id" alias="n" aggregate="count" /></entity></fetch>"""));
            return (int)((AliasedValue)result.Entities[0]["n"]).Value;
        }

        /// <summary>The <c>sprk_</c> columns of <paramref name="entity"/> whose Format is DateOnly (task 024).</summary>
        public async Task<IReadOnlyList<string>> GetDateOnlyColumnsAsync(string entity)
        {
            var attributes = ((RetrieveEntityResponse)await _client!.ExecuteAsync(
                new RetrieveEntityRequest { LogicalName = entity, EntityFilters = EntityFilters.Attributes })).EntityMetadata.Attributes;
            return attributes.OfType<DateTimeAttributeMetadata>()
                .Where(a => a.LogicalName.StartsWith("sprk_", StringComparison.Ordinal) && a.Format == DateTimeFormat.DateOnly)
                .Select(a => a.LogicalName)
                .ToList();
        }

        public async Task<string> ReadRuleBodyAsync(Guid policyVersionId)
        {
            var row = await _client!.RetrieveAsync("sprk_policyversion", policyVersionId, new ColumnSet("sprk_rulebody"));
            return row.GetAttributeValue<string>("sprk_rulebody");
        }

        public async Task<int> CountMattersAsync()
        {
            var result = await _client!.RetrieveMultipleAsync(new FetchExpression(
                """<fetch aggregate="true"><entity name="sprk_matter"><attribute name="sprk_matterid" alias="n" aggregate="count" /></entity></fetch>"""));
            return (int)((AliasedValue)result.Entities[0]["n"]).Value;
        }

        public async Task<Guid> WhoAmIAsync() =>
            ((WhoAmIResponse)await _client!.ExecuteAsync(new WhoAmIRequest())).UserId;

        public async Task<AttributeMetadata> GetAttributeAsync(string entity, string attribute) =>
            ((RetrieveAttributeResponse)await _client!.ExecuteAsync(
                new RetrieveAttributeRequest { EntityLogicalName = entity, LogicalName = attribute })).AttributeMetadata;

        public async Task<string> GetPrimaryIdAttributeAsync(string entity) =>
            ((RetrieveEntityResponse)await _client!.ExecuteAsync(
                new RetrieveEntityRequest { LogicalName = entity, EntityFilters = EntityFilters.Entity })).EntityMetadata.PrimaryIdAttribute;

        public async Task<PrivilegeDepth?> GetReadDepthAsync(Guid userId, string entity)
        {
            var privileges = ((RetrieveEntityResponse)await _client!.ExecuteAsync(
                new RetrieveEntityRequest { LogicalName = entity, EntityFilters = EntityFilters.Privileges })).EntityMetadata.Privileges;
            var readPrivilegeId = privileges.Single(p => p.PrivilegeType == PrivilegeType.Read).PrivilegeId;

            var userPrivileges = ((RetrieveUserPrivilegesResponse)await _client.ExecuteAsync(
                new RetrieveUserPrivilegesRequest { UserId = userId })).RolePrivileges;
            return userPrivileges.Where(p => p.PrivilegeId == readPrivilegeId).Select(p => (PrivilegeDepth?)p.Depth).Max();
        }

        public Task DisposeAsync()
        {
            _client?.Dispose();
            return Task.CompletedTask;
        }
    }
}
