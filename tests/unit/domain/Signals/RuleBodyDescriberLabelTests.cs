using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.Models;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Services.Communication; // CapturingLogger<T>
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Ontology platform R1 task 210 (#3040 DI-169, #3020 DI-165): <see cref="RuleBodyDescriber"/> names columns by their display names and
/// choice values by their option labels. Maintain-class (ADR-038, <c>tests/unit/domain/**</c>).
/// </summary>
/// <remarks>
/// <para><b>Paired with the real schema</b> (project testing rule): the display names are compared with the solution source
/// (<c>src/dataverse/solutions/SpaarkeMaster/Entities/*/Entity.xml</c>), and the option labels the label tests expect are read from the same
/// source and served through the real <see cref="MetadataService"/> over its cache, so a label asserted here is the one the schema defines.</para>
/// <para><b>Hot path.</b> The label tests prove the describer reaches Dataverse metadata only through <see cref="MetadataService"/>'s shared
/// cache (a warm cache answers with no Dataverse call), reads nothing when the body tests no choice value, and shows the number (never a
/// refusal) when the labels cannot be read.</para>
/// </remarks>
[Trait("status", "new")]
public class RuleBodyDescriberLabelTests
{
    private const string MetadataCacheKeyPrefix = "sdap:dv:entitymetadata:v2:"; // MetadataService's key; a drift fails the label tests

    private static readonly Guid TaskEventType = Guid.Parse("124f5fc9-98ff-f011-8406-7c1e525abd8b");

    // ── column display names (DI-169) ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryAuthorableColumn_HasADisplayName_AndItIsTheSolutionSourcesDisplayName()
    {
        PolicyVersionValidator.AuthorableColumns
            .Where(c => !RuleBodyDescriber.ColumnDisplayNames.ContainsKey(c))
            .Select(c => $"{c.Entity}.{c.Column}")
            .Should().BeEmpty("every column a rule may name must have a display name, or the wizard and the sentence show its logical name");

        foreach (var ((entity, column), label) in RuleBodyDescriber.ColumnDisplayNames)
        {
            PolicyVersionValidator.AuthorableColumns.Should().Contain((entity, column),
                $"{entity}.{column}: the table lists only authorable columns, so it cannot drift into a second allow-list");
            DisplayNameInSolution(entity, column).Should().Be(label, $"{entity}.{column} is shown by the display name the solution defines");
        }
    }

    public static TheoryData<string, string> AuthorableColumns()
    {
        var data = new TheoryData<string, string>();
        foreach (var (entity, column) in PolicyVersionValidator.AuthorableColumns.OrderBy(c => c.Entity).ThenBy(c => c.Column))
        {
            data.Add(entity, column);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AuthorableColumns))]
    public async Task EveryAuthorableColumn_InAPlainSentence_IsNamedByItsDisplayName_NeverItsLogicalName(string entity, string column)
    {
        var describer = Describer(scopes: null);
        var body = PredicateCompiler.ThresholdColumns.Contains((entity, column))
            ? """{"type":"Threshold","subject":"@E","threshold":{"field":"@C","operator":">=","value":1}}"""
                .Replace("@E", entity, StringComparison.Ordinal).Replace("@C", column, StringComparison.Ordinal)
            : """{"type":"Existence","subject":"@E","when":{"@C":{"notNull":true}},"all":[]}"""
                .Replace("@E", entity, StringComparison.Ordinal).Replace("@C", column, StringComparison.Ordinal);

        var result = await describer.DescribeAsync(body, CancellationToken.None);

        result.IsRefused.Should().BeFalse(result.Refusal);
        var text = string.Join(" ", result.Description!.Clauses.Select(c => c.Text));
        text.Should().NotContain(column, "the plain line names the column by its display name");
        string.Join(" ", result.Description.Clauses.Select(c => c.Formal)).Should().Contain(column, "the formal line keeps the logical name");
    }

    [Theory]
    [InlineData("sprk_matter", "sprk_openeddate", "it has an opened date")] // vowel-initial label
    [InlineData("sprk_event", "sprk_approveddate", "it has an approved date")]
    [InlineData("sprk_event", "sprk_duedate", "it has a due date")] // consonant-initial label
    [InlineData("sprk_matter", "createdon", "it has a created on")]
    public async Task ANotNullTest_TakesTheArticleItsLabelNeeds(string entity, string column, string expected)
    {
        var describer = Describer(scopes: null);
        var body = """{"type":"Existence","subject":"@E","when":{"@C":{"notNull":true}},"all":[]}"""
            .Replace("@E", entity, StringComparison.Ordinal).Replace("@C", column, StringComparison.Ordinal);

        var result = await describer.DescribeAsync(body, CancellationToken.None);

        result.IsRefused.Should().BeFalse(result.Refusal);
        var line = result.Description!.Clauses.Should().ContainSingle().Subject;
        line.Text.Should().Contain(expected);
        line.Formal.Should().Contain($"{column} is not null", "the formal line keeps the logical name");
    }

    [Fact]
    public async Task ACreatedOnTest_ReadsCreatedOn_AndTheFormalLineKeepsCreatedon()
    {
        var describer = Describer(scopes: null);

        var result = await describer.DescribeAsync(
            """{"type":"Existence","subject":"sprk_matter","when":{"createdon":{"<=":"now-5d"}},"all":[]}""", CancellationToken.None);

        var line = result.Description!.Clauses.Should().ContainSingle().Subject;
        line.Text.Should().Be("Each matter where created on is on or before 5 days ago.");
        line.Formal.Should().Be("sprk_matter where createdon <= now-5d");
    }

    // ── choice labels (DI-165) ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AStatusReasonTest_ReadsByItsOptionLabel_AndTheFormalLineKeepsTheNumber()
    {
        var rig = new MetadataRig().Warm("sprk_event", "statuscode");
        var describer = Describer(rig.Scopes, rows: [("sprk_eventtype_ref", TaskEventType, "Task")]);

        var result = await describer.DescribeAsync(
            """{"type":"Existence","subject":"sprk_event","when":{"sprk_eventtype_ref":"124f5fc9-98ff-f011-8406-7c1e525abd8b","statuscode":659490001,"sprk_duedate":{"<":"now-5d"}},"all":[]}""",
            CancellationToken.None);

        var line = result.Description!.Clauses.Should().ContainSingle().Subject;
        var open = OptionLabelInSolution("sprk_event", "statuscode", 659490001);
        open.Should().Be("Open");
        line.Text.Should().Be($"Each event where event type is Task, status is {open}, and due date is before 5 days ago.");
        line.Formal.Should().Be(
            "sprk_event where sprk_eventtype_ref = 124f5fc9-98ff-f011-8406-7c1e525abd8b and statuscode = 659490001 and sprk_duedate < now-5d");
        rig.Dataverse.Invocations.Should().BeEmpty("a warm shared cache answers without any Dataverse metadata call");
    }

    [Fact]
    public async Task AStatusReasonList_AndANotEqualTest_ReadByLabel()
    {
        var rig = new MetadataRig().Warm("sprk_todo", "statuscode");
        var describer = Describer(rig.Scopes);

        var list = await describer.DescribeAsync(
            """{"type":"Existence","subject":"sprk_todo","when":{"statuscode":[1,659490001]},"all":[]}""", CancellationToken.None);
        var notEqual = await describer.DescribeAsync(
            """{"type":"Existence","subject":"sprk_todo","when":{"statuscode":{"<>":2}},"all":[]}""", CancellationToken.None);

        list.Description!.Clauses.Single().Text.Should().Be(
            $"Each To Do where status is {OptionLabelInSolution("sprk_todo", "statuscode", 1)} or {OptionLabelInSolution("sprk_todo", "statuscode", 659490001)}.");
        list.Description.Clauses.Single().Formal.Should().Be("sprk_todo where statuscode in (1, 659490001)");
        notEqual.Description!.Clauses.Single().Text.Should().Be(
            $"Each To Do where status is not {OptionLabelInSolution("sprk_todo", "statuscode", 2)}.");
        notEqual.Description.Clauses.Single().Formal.Should().Be("sprk_todo where statuscode <> 2");
    }

    [Fact]
    public async Task APicklistInARelatedClause_ReadsByLabel()
    {
        var rig = new MetadataRig().Warm("sprk_communication", "sprk_reviewoutcome");
        var describer = Describer(rig.Scopes);

        var result = await describer.DescribeAsync(
            """{"type":"Existence","subject":"sprk_matter","all":[{"exists":"sprk_communication","path":"sprk_regardingmatter","filter":{"sprk_reviewoutcome":{"<>":100000003},"sprk_receiveddate":{">=":"now-30d"}}}]}""",
            CancellationToken.None);

        var dismiss = OptionLabelInSolution("sprk_communication", "sprk_reviewoutcome", 100000003);
        result.Description!.Clauses.Single().Text.Should().Be(
            $"A communication exists for the matter where review outcome is not {dismiss} and received date is on or after 30 days ago.");
        result.Description.Clauses.Single().Formal.Should().Contain("sprk_reviewoutcome <> 100000003");
    }

    [Fact]
    public async Task AValueWithNoOptionLabel_IsShownAsItsNumber_NotInvented()
    {
        var rig = new MetadataRig().Warm("sprk_event", "statuscode");
        var describer = Describer(rig.Scopes);

        var result = await describer.DescribeAsync(
            """{"type":"Existence","subject":"sprk_event","when":{"statuscode":999999999},"all":[]}""", CancellationToken.None);

        result.Description!.Clauses.Single().Text.Should().Be("Each event where status is 999999999.");
    }

    [Fact]
    public async Task WhenTheLabelsCannotBeRead_TheNumberIsShown_TheDescriptionIsNotRefused_AndTheFaultIsLoggedByTypeOnly()
    {
        var rig = new MetadataRig(); // cold cache; the fake Dataverse cannot serve metadata, so MetadataService throws
        var logger = new CapturingLogger<RuleBodyDescriber>();
        var describer = Describer(rig.Scopes, logger);

        var result = await describer.DescribeAsync(
            """{"type":"Existence","subject":"sprk_event","when":{"statuscode":659490001},"all":[]}""", CancellationToken.None);

        result.IsRefused.Should().BeFalse("labels are an extra; the sentence still states the test, with the number");
        result.Description!.Clauses.Single().Text.Should().Be("Each event where status is 659490001.");
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.EventId.Id.Should().Be(OntologyWriterEvents.RuleDescriptionLabelsUnavailable.Id);
        entry.Field("Entity").Should().Be("sprk_event");
        entry.Field("ExceptionType").Should().Be(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task ACallerCancellationWhileReadingLabels_StillThrows()
    {
        var rig = new MetadataRig();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => Describer(rig.Scopes).DescribeAsync(
            """{"type":"Existence","subject":"sprk_event","when":{"statuscode":659490001},"all":[]}""", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("""{"type":"Existence","subject":"sprk_matter","when":{"statecode":0,"createdon":{"<=":"now-5d"}},"all":[]}""")]
    [InlineData("""{"type":"Existence","subject":"sprk_event","when":{"sprk_eventtype_ref":"124f5fc9-98ff-f011-8406-7c1e525abd8b","sprk_duedate":{"<":"now"}},"all":[]}""")]
    [InlineData("""{"type":"Threshold","subject":"sprk_matter","when":{"statecode":0},"threshold":{"field":"sprk_totalbudget","operator":">=","value":5}}""")]
    public async Task ABodyThatTestsNoChoiceValue_ReadsNoMetadataAtAll(string body)
    {
        var rig = new MetadataRig();
        var describer = Describer(rig.Scopes, rows: [("sprk_eventtype_ref", TaskEventType, "Task")]);

        var result = await describer.DescribeAsync(body, CancellationToken.None);

        result.IsRefused.Should().BeFalse(result.Refusal);
        rig.CacheReads.Should().Be(0, "statecode, dates, lookups and Threshold quantities need no option label");
        rig.Dataverse.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task AComparisonOnAChoiceColumn_KeepsTheNumber_ItIsNotAnEquality()
    {
        var rig = new MetadataRig().Warm("sprk_event", "statuscode");

        var result = await Describer(rig.Scopes).DescribeAsync(
            """{"type":"Existence","subject":"sprk_event","when":{"statuscode":{">=":659490001}},"all":[]}""", CancellationToken.None);

        result.Description!.Clauses.Single().Text.Should().Be("Each event where status is at least 659490001.");
    }

    [Fact]
    public async Task TheDiResolvedDescriber_UsesTheScopedMetadataService()
    {
        // The production registration (SignalsModule: AddSingleton<RuleBodyDescriber>()) must pick the scope-factory constructor, or every
        // decision reads "status is 659490001" again with every unit test green.
        var rig = new MetadataRig().Warm("sprk_event", "statuscode");
        var services = rig.Services;
        var schema = new RuleBodySchemaValidator();
        services.AddSingleton(new PolicyVersionValidator(
            schema, new PredicateCompiler(schema, new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero))),
            NullLogger<PolicyVersionValidator>.Instance));
        services.AddSingleton(new Mock<IGenericEntityService>().Object);
        services.AddSingleton<RuleBodyDescriber>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        var result = await provider.GetRequiredService<RuleBodyDescriber>().DescribeAsync(
            """{"type":"Existence","subject":"sprk_event","when":{"statuscode":659490001},"all":[]}""", CancellationToken.None);

        result.Description!.Clauses.Single().Text.Should().Be("Each event where status is Open.");
    }

    // ── rig ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static RuleBodyDescriber Describer(
        IServiceScopeFactory? scopes, ILogger<RuleBodyDescriber>? logger = null, (string Table, Guid Id, string Name)[]? rows = null)
    {
        rows ??= [];
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) =>
            {
                var collection = new EntityCollection();
                foreach (var row in rows.Where(r => r.Table == query.EntityName))
                {
                    collection.Entities.Add(new Entity(row.Table, row.Id) { ["sprk_name"] = row.Name });
                }

                return Task.FromResult(collection);
            });
        var schema = new RuleBodySchemaValidator();
        var validator = new PolicyVersionValidator(
            schema, new PredicateCompiler(schema, new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero))),
            NullLogger<PolicyVersionValidator>.Instance);
        return new RuleBodyDescriber(validator, entities.Object, logger ?? NullLogger<RuleBodyDescriber>.Instance, scopes);
    }

    /// <summary>The real <see cref="MetadataService"/> over an in-memory distributed cache, with a fake Dataverse that cannot serve
    /// metadata (a cache miss throws, as an unreachable Dataverse would). <see cref="Warm"/> seeds a table's cached copy with the option
    /// labels the solution source defines.</summary>
    private sealed class MetadataRig
    {
        private readonly CountingCache _cache = new();

        public MetadataRig()
        {
            Services.AddLogging();
            Services.AddSingleton<IDistributedCache>(_cache);
            Services.AddSingleton(Dataverse.Object);
            Services.AddScoped<MetadataService>();
        }

        public ServiceCollection Services { get; } = new();

        public Mock<IDataverseService> Dataverse { get; } = new();

        public int CacheReads => _cache.Reads;

        public IServiceScopeFactory Scopes => Services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        public MetadataRig Warm(string entity, string column)
        {
            var dto = new EntityMetadataDto(entity, entity + "id", "sprk_name",
                [new AttributeDto(column, "Status", null, false, false, new OptionSetDto(OptionsInSolution(entity, column)
                    .Select(o => new OptionDto(o.Value, o.Label, null)).ToList()))]);
            _cache.Inner.SetString(MetadataCacheKeyPrefix + entity,
                JsonSerializer.Serialize(dto, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return this;
        }
    }

    private sealed class CountingCache : IDistributedCache
    {
        public MemoryDistributedCache Inner { get; } = new(Options.Create(new MemoryDistributedCacheOptions()));

        public int Reads { get; private set; }

        public byte[]? Get(string key) { Reads++; return Inner.Get(key); }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) { Reads++; return Inner.GetAsync(key, token); }

        public void Refresh(string key) => Inner.Refresh(key);

        public Task RefreshAsync(string key, CancellationToken token = default) => Inner.RefreshAsync(key, token);

        public void Remove(string key) => Inner.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default) => Inner.RemoveAsync(key, token);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => Inner.Set(key, value, options);

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            Inner.SetAsync(key, value, options, token);
    }

    // ── the solution source ─────────────────────────────────────────────────────────────────────────────────────────────

    private static XElement Attribute(string entity, string column)
    {
        var entities = Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster", "Entities");
        var folder = Directory.GetDirectories(entities).Single(d => string.Equals(Path.GetFileName(d), entity, StringComparison.OrdinalIgnoreCase));
        return XDocument.Load(Path.Combine(folder, "Entity.xml")).Descendants("attribute")
            .Single(a => (string?)a.Element("LogicalName") == column);
    }

    private static string? DisplayNameInSolution(string entity, string column) =>
        (string?)Attribute(entity, column).Element("displaynames")?.Elements("displayname").First().Attribute("description");

    private static List<(int Value, string Label)> OptionsInSolution(string entity, string column) =>
        Attribute(entity, column).Element("optionset")!.Elements().SelectMany(e => e.Elements())
            .Where(o => o.Name.LocalName is "option" or "status")
            .Select(o => (int.Parse((string)o.Attribute("value")!, CultureInfo.InvariantCulture),
                (string)o.Element("labels")!.Elements("label").First().Attribute("description")!))
            .ToList();

    private static string OptionLabelInSolution(string entity, string column, int value) =>
        OptionsInSolution(entity, column).Single(o => o.Value == value).Label;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Program.cs")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
