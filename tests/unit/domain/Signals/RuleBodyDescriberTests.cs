using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Services.Communication; // CapturingLogger<T>
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Behaviour tests for <see cref="RuleBodyDescriber"/> (ontology platform R1 task 026; spec FR-48/FR-55/FR-57; v4 #11).
/// Maintain-class (ADR-038, <c>tests/unit/domain/**</c>). The category and event-type NAMES asserted here are paired with
/// the live <c>sprk_triagecategory</c> / <c>sprk_eventtype_ref</c> rows by
/// <c>tests/integration/seam/Signals/RuleBodyDescriberSeamTests.cs</c> (project rule: a test asserting reference-row
/// names must touch the real schema).
/// </summary>
[Trait("status", "new")]
public class RuleBodyDescriberTests
{
    private static readonly Guid FeeCategory = Guid.Parse("8b62dd84-1fbc-f111-aaaf-3833c5e9614d");
    private static readonly Guid ScopeCategory = Guid.Parse("8d62dd84-1fbc-f111-aaaf-3833c5e9614d");
    private static readonly Guid TaskEventType = Guid.Parse("124f5fc9-98ff-f011-8406-7c1e525abd8b");

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "tests", "fixtures", "signals", name));

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

    /// <summary>A describer whose reference-table reads return the given (table, id, name) rows; every query is recorded.</summary>
    private static (RuleBodyDescriber Describer, List<QueryExpression> Queries) Describer(
        params (string Table, Guid Id, string Name)[] rows) => Describer(NullLogger<RuleBodyDescriber>.Instance, rows);

    private static (RuleBodyDescriber Describer, List<QueryExpression> Queries) Describer(
        ILogger<RuleBodyDescriber> logger, params (string Table, Guid Id, string Name)[] rows)
    {
        var queries = new List<QueryExpression>();
        var entities = Substitute.For<IGenericEntityService>();
        entities.RetrieveMultipleAsync(Arg.Any<QueryExpression>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var query = call.Arg<QueryExpression>();
            queries.Add(query);
            var collection = new EntityCollection();
            foreach (var row in rows.Where(r => r.Table == query.EntityName))
            {
                collection.Entities.Add(new Entity(row.Table, row.Id) { ["sprk_name"] = row.Name });
            }

            return Task.FromResult(collection);
        });

        var schema = new RuleBodySchemaValidator();
        var compiler = new PredicateCompiler(schema, new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero)));
        var validator = new PolicyVersionValidator(schema, compiler, NullLogger<PolicyVersionValidator>.Instance);
        return (new RuleBodyDescriber(validator, entities, logger), queries);
    }

    private static (RuleBodyDescriber Describer, List<QueryExpression> Queries) WithAllRows() => Describer(
        ("sprk_triagecategory", FeeCategory, "Fee / rate change"),
        ("sprk_triagecategory", ScopeCategory, "Scope / budget change"),
        ("sprk_eventtype_ref", TaskEventType, "Task"));

    [Fact]
    public async Task PathBBody_IsTwoClauses_TheFirstAClassificationNamingBothCategories_TheSecondNoBudgetRevisionInTheWindow()
    {
        var (describer, _) = WithAllRows();

        var result = await describer.DescribeAsync(Fixture("pathb-existence.rulebody.json"), CancellationToken.None);

        result.IsRefused.Should().BeFalse();
        var clauses = result.Description!.Clauses;
        clauses.Select(c => c.ClauseId).Should().Equal("all[0]", "all[1]");

        clauses[0].Kind.Should().Be("Exists");
        clauses[0].Text.Should().Contain("classified as Fee / rate change or Scope / budget change");
        clauses[0].Text.Should().Contain("received date is on or after 30 days ago");
        clauses[0].Text.Should().Contain("review outcome is not 100000003");
        clauses[0].Formal.Should().Be(
            "exists sprk_communication via sprk_regardingmatter where sprk_triagecategory in (" + FeeCategory + ", " + ScopeCategory + ")"
            + " and sprk_receiveddate >= now-30d and sprk_reviewoutcome <> 100000003");

        clauses[1].Kind.Should().Be("NotExists");
        clauses[1].Text.Should().Be("No budget revision exists for the matter where revised on is on or after 30 days ago.");
        clauses[1].Formal.Should().Be("notExists sprk_budgetrevision via sprk_matter where sprk_revisedon >= now-30d");
    }

    [Fact]
    public async Task EmptyWhen_ProducesNoSubjectLine()
    {
        var (describer, _) = WithAllRows();

        var result = await describer.DescribeAsync(Fixture("pathb-existence.rulebody.json"), CancellationToken.None);

        result.Description!.Clauses.Should().NotContain(c => c.ClauseId == "when");
    }

    [Fact]
    public async Task EveryNameIsReadInOneBatchPerReferenceTable_NotOnePerValue()
    {
        var (describer, queries) = WithAllRows();

        await describer.DescribeAsync(Fixture("pathb-existence.rulebody.json"), CancellationToken.None);

        queries.Should().ContainSingle("both categories share one reference table, so one batched read");
        queries[0].EntityName.Should().Be("sprk_triagecategory");
    }

    [Theory]
    [InlineData("do-overdue-task", "Each event where event type is Task, status is 659490001, and due date is before 5 days ago.",
        "sprk_event where sprk_eventtype_ref = 124f5fc9-98ff-f011-8406-7c1e525abd8b and statuscode = 659490001 and sprk_duedate < now-5d")]
    [InlineData("do-task-due-within-3-days", "Each event where event type is Task, status is 659490001, and due date is on or after now and is on or before 3 days from now.",
        "sprk_event where sprk_eventtype_ref = 124f5fc9-98ff-f011-8406-7c1e525abd8b and statuscode = 659490001 and sprk_duedate >= now and sprk_duedate <= now+3d")]
    [InlineData("do-workassignment-past-due", "Each work assignment where state is 0 and response due date is before now.",
        "sprk_workassignment where statecode = 0 and sprk_responseduedate < now")]
    public async Task EachDoFixture_IsOneLine_NamingTheSubject_TheDateColumn_AndTheRelativeDate(string fixture, string text, string formal)
    {
        var (describer, _) = WithAllRows();

        var result = await describer.DescribeAsync(Fixture(fixture + ".rulebody.json"), CancellationToken.None);

        var line = result.Description!.Clauses.Should().ContainSingle().Subject;
        line.ClauseId.Should().Be("when");
        line.Kind.Should().Be("Subject");
        line.Text.Should().Be(text);
        line.Formal.Should().Be(formal);
    }

    [Theory]
    [InlineData("""{"type":"Existence","subject":"sprk_servicerequest","when":{"statecode":0},"all":[]}""")]   // not in the allow-list
    [InlineData("""{"type":"Existence","subject":"sprk_event","when":{},"all":[]}""")]                          // matches every row
    [InlineData("""{"type":"Threshold","subject":"sprk_event","when":{"statecode":0},"all":[]}""")]              // a shape the compiler does not accept yet
    [InlineData("{not json")]
    [InlineData("")]
    public async Task ABodyTheCompilerRefuses_IsRefusedByTheDescriberToo_AndNoNameIsRead(string body)
    {
        var (describer, queries) = WithAllRows();

        var result = await describer.DescribeAsync(body, CancellationToken.None);

        result.IsRefused.Should().BeTrue();
        result.Description.Should().BeNull();
        result.Refusal.Should().Contain("compiler");
        queries.Should().BeEmpty();
    }

    [Fact]
    public async Task ANameReadFault_IsARefusal_NotAnException_LoggedWithAStableEventId_AndMetered()
    {
        var logger = new CapturingLogger<RuleBodyDescriber>();
        var entities = Substitute.For<IGenericEntityService>();
        entities.RetrieveMultipleAsync(Arg.Any<QueryExpression>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("dataverse is down"));
        var schema = new RuleBodySchemaValidator();
        var validator = new PolicyVersionValidator(
            schema, new PredicateCompiler(schema, new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero))),
            NullLogger<PolicyVersionValidator>.Instance);
        var describer = new RuleBodyDescriber(validator, entities, logger);

        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, reasons) = ListenRefusals(scope);
        using var _ = listener;

        var result = await describer.DescribeAsync(Fixture("pathb-existence.rulebody.json"), CancellationToken.None);

        result.IsRefused.Should().BeTrue();
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.EventId.Id.Should().Be(OntologyWriterEvents.RuleDescriptionRefused.Id);
        entry.Field("Reason").Should().Be(RuleDescriptionRefusalReason.LookupReadFailed);
        entry.Message.Should().NotContain("dataverse is down");
        reasons().Should().Equal(RuleDescriptionRefusalReason.LookupReadFailed);
    }

    [Fact]
    public async Task ACancelledRead_StillThrows_ItIsNotARefusal()
    {
        var entities = Substitute.For<IGenericEntityService>();
        entities.RetrieveMultipleAsync(Arg.Any<QueryExpression>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        var schema = new RuleBodySchemaValidator();
        var validator = new PolicyVersionValidator(
            schema, new PredicateCompiler(schema, TimeProvider.System), NullLogger<PolicyVersionValidator>.Instance);
        var describer = new RuleBodyDescriber(validator, entities, NullLogger<RuleBodyDescriber>.Instance);

        var act = () => describer.DescribeAsync(Fixture("pathb-existence.rulebody.json"), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ALookupValueWithNoNameRow_RefusesTheDescription_RatherThanShowingABareId()
    {
        var (describer, _) = Describer(("sprk_triagecategory", FeeCategory, "Fee / rate change")); // Scope row missing

        var result = await describer.DescribeAsync(Fixture("pathb-existence.rulebody.json"), CancellationToken.None);

        result.IsRefused.Should().BeTrue();
        result.Refusal.Should().Contain("does not resolve to a name");
    }

    [Fact]
    public async Task DescribingTwiceGivesTheSameWords_AndThePlainTextCarriesNoGuid()
    {
        var (describer, _) = WithAllRows();

        var first = await describer.DescribeAsync(Fixture("pathb-existence.rulebody.json"), CancellationToken.None);
        var second = await describer.DescribeAsync(Fixture("pathb-existence.rulebody.json"), CancellationToken.None);

        second.Description!.Clauses.Should().BeEquivalentTo(first.Description!.Clauses, o => o.WithStrictOrdering());
        first.Description.Clauses.Select(c => c.Text).Should().OnlyContain(t => !t.Contains("8b62dd84") && !t.Contains("124f5fc9"));
    }

    // OntologyWriterTelemetry's Meter is process-global; scope the listener per test (same pattern as DecisionActionCatalogTests).
    private static readonly AsyncLocal<Guid> MetricScope = new();

    private static (System.Diagnostics.Metrics.MeterListener Listener, Func<IReadOnlyList<string>> Reasons) ListenRefusals(Guid scope)
    {
        var reasons = new List<string>();
        var listener = new System.Diagnostics.Metrics.MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == OntologyWriterTelemetry.MeterName && instrument.Name == "ontology.ruledescription.refused")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            if (MetricScope.Value != scope) return;
            foreach (var tag in tags)
            {
                if (tag.Key == "reason" && tag.Value is string reason)
                {
                    lock (reasons) { reasons.Add(reason); }
                }
            }
        });
        listener.Start();
        return (listener, () => { lock (reasons) { return reasons.ToArray(); } });
    }
}
