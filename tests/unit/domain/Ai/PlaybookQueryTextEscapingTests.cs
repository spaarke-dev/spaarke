using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Ai;

/// <summary>
/// The ROOT-CAUSE half of the playbook-parameter fix (unified-access-control-r2 task 164, owner round 16 item 3): every
/// value Layer 1 substitutes into a query-text position (QueryDataverse <c>fetchXml</c>, IndexRetrieve <c>filter</c>) is
/// escaped for that language AT THE SUBSTITUTION POINT, so no parameter — and no node output — can change the query's
/// structure, whatever its key. Before the fix, a caller running a PUBLIC notification playbook through
/// <c>/execute</c> could put FetchXML into its app-only query with <c>parameters.timeWindowHours</c>.
/// </summary>
public class PlaybookQueryTextEscapingTests
{
    private static readonly ITemplateEngine Engine = new TemplateEngine(NullLogger<TemplateEngine>.Instance);

    private const string FetchConfig =
        "{\"entityLogicalName\":\"sprk_event\",\"fetchXml\":\"<fetch top='50'><entity name='sprk_event'><filter type='and'>" +
        "<condition attribute='modifiedon' operator='last-x-hours' value='{{timeWindowHours}}'/>" +
        "</filter></entity></fetch>\",\"description\":\"events in the last {{timeWindowHours}} hours\"}";

    private static string Render(string config, Dictionary<string, object?> context, ExecutorType? executorType) =>
        PlaybookOrchestrationService.RenderConfigJsonStructurally(config, context, Engine, executorType);

    private static string FetchXmlOf(string renderedConfig)
    {
        using var doc = JsonDocument.Parse(renderedConfig);
        return doc.RootElement.GetProperty("fetchXml").GetString()!;
    }

    [Theory]
    [InlineData("24'/><condition attribute='ownerid' operator='ne' value='x")]
    [InlineData("24\"/><condition attribute=\"ownerid\" operator=\"ne\" value=\"x")]
    [InlineData("24' operator='ne")]
    [InlineData("<fetch/>&amp;")]
    public void A_ParameterValue_CannotChangeTheFetchXmlStructure(string injected)
    {
        var context = new Dictionary<string, object?> { ["timeWindowHours"] = injected };

        var fetchXml = FetchXmlOf(Render(FetchConfig, context, ExecutorType.QueryDataverse));

        var conditions = XDocument.Parse(fetchXml).Descendants("condition").ToList();
        conditions.Should().ContainSingle("an escaped value cannot add or close an element");
        conditions[0].Attribute("operator")!.Value.Should().Be("last-x-hours", "an escaped value cannot rewrite an attribute");
        conditions[0].Attribute("value")!.Value.Should().Be(injected, "the parser decodes the escaped value back to the exact text");
    }

    [Fact]
    public void A_NodeOutput_IsEscapedToo_ThroughAHelper()
    {
        const string config =
            "{\"fetchXml\":\"<fetch><entity name='sprk_matter'><filter><condition attribute='sprk_mattername' operator='eq' " +
            "value='{{default lookup.name 'none'}}'/></filter></entity></fetch>\"}";
        var context = new Dictionary<string, object?>
        {
            ["lookup"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["name"] = "O'Brien & Sons <LLP>" },
        };

        var fetchXml = FetchXmlOf(Render(config, context, ExecutorType.QueryDataverse));

        XDocument.Parse(fetchXml).Descendants("condition").Single().Attribute("value")!.Value.Should().Be("O'Brien & Sons <LLP>");
    }

    [Fact]
    public void LegitimateValues_RenderTheSameText_AsBefore()
    {
        var matterIds = new List<object?> { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() };
        var context = new Dictionary<string, object?>
        {
            ["timeWindowHours"] = "24",
            ["todayUtc"] = "2026-10-04T08:00:00Z",
            ["myMatters"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["ids"] = matterIds },
        };
        const string config =
            "{\"fetchXml\":\"<fetch><entity name='sprk_event'><filter><condition attribute='modifiedon' operator='last-x-hours' " +
            "value='{{timeWindowHours}}'/><condition attribute='sprk_duedate' operator='lt' value='{{todayUtc}}'/>" +
            "<condition attribute='sprk_regardingmatter' operator='in' value='{{joinIds myMatters.ids}}'/></filter></entity></fetch>\"}";

        var escaped = Render(config, context, ExecutorType.QueryDataverse);
        var unescaped = Render(config, context, executorType: null);

        escaped.Should().Be(unescaped, "GUIDs, integers and ISO dates contain nothing either escape changes");
    }

    [Fact]
    public void A_NonQueryProperty_OfTheSameNode_IsNotEscaped()
    {
        var context = new Dictionary<string, object?> { ["timeWindowHours"] = "a & b" };

        using var doc = JsonDocument.Parse(Render(FetchConfig, context, ExecutorType.QueryDataverse));

        doc.RootElement.GetProperty("description").GetString().Should().Be("events in the last a & b hours");
        doc.RootElement.GetProperty("fetchXml").GetString().Should().Contain("a &amp; b");
    }

    [Fact]
    public void An_Executor_WithNoQueryTextPosition_RendersUnescaped()
    {
        var context = new Dictionary<string, object?> { ["timeWindowHours"] = "a & b" };

        FetchXmlOf(Render(FetchConfig, context, ExecutorType.AiAnalysis)).Should().Contain("value='a & b'");
    }

    [Fact]
    public void The_IndexRetrieveFilter_EscapesForAnODataStringLiteral()
    {
        const string config = "{\"indexName\":\"spaarke-insights-index\",\"filter\":\"matterId eq '{{matterId}}'\",\"vectorQuery\":\"{{matterId}} themes\"}";
        var context = new Dictionary<string, object?> { ["matterId"] = "x' or tenantId ne 'y" };

        using var doc = JsonDocument.Parse(Render(config, context, ExecutorType.IndexRetrieve));

        doc.RootElement.GetProperty("filter").GetString().Should().Be("matterId eq 'x'' or tenantId ne ''y'");
        doc.RootElement.GetProperty("vectorQuery").GetString().Should().Be("x' or tenantId ne 'y themes", "search text is not a filter");
    }

    [Fact]
    public void The_NestedWrapperConfigFormat_IsEscapedAtTheInnerLevel()
    {
        var wrapper = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["__actionType"] = 51,
            ["configJson"] = FetchConfig,
        });
        var injected = "24'/><condition attribute='ownerid' operator='ne' value='x";
        var context = new Dictionary<string, object?> { ["timeWindowHours"] = injected };

        var rendered = Render(wrapper, context, ExecutorType.QueryDataverse);

        using var outer = JsonDocument.Parse(rendered);
        var inner = outer.RootElement.GetProperty("configJson").GetString()!;
        var fetchXml = FetchXmlOf(inner);
        XDocument.Parse(fetchXml).Descendants("condition").Should().ContainSingle();
    }

    [Fact]
    public void EscapeForQueryText_CopiesEveryShape_AndLeavesTheOriginalUntouched()
    {
        var original = new Dictionary<string, object?>
        {
            ["text"] = "a<b",
            ["number"] = 5L,
            ["list"] = new List<object?> { "c&d", 1 },
            ["bag"] = new { id = "e\"f", count = 2 },
        };

        var escaped = PlaybookTemplateContextBuilder.EscapeForQueryText(original, PlaybookTemplateContextBuilder.QueryTextLanguage.FetchXml);

        escaped["text"].Should().Be("a&lt;b");
        escaped["number"].Should().Be(5L);
        ((List<object?>)escaped["list"]!).Should().Equal("c&amp;d", 1);
        ((Dictionary<string, object?>)escaped["bag"]!)["id"].Should().Be("e&quot;f");
        original["text"].Should().Be("a<b");
    }

    [Fact]
    public void EscapeForQueryText_KeepsSelfReferences_TheShapeTheContextBuilderProduces()
    {
        // PlaybookTemplateContextBuilder exposes a node's output dictionary under its own "output" key so both
        // {{node.x}} and {{node.output.x}} resolve — a dictionary that contains itself.
        var membership = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ids"] = new List<object?> { Guid.NewGuid().ToString() },
            ["label"] = "a&b",
        };
        membership["output"] = membership;
        var context = new Dictionary<string, object?> { ["myMatters"] = membership };

        var escaped = PlaybookTemplateContextBuilder.EscapeForQueryText(context, PlaybookTemplateContextBuilder.QueryTextLanguage.FetchXml);

        var copy = (Dictionary<string, object?>)escaped["myMatters"]!;
        copy["output"].Should().BeSameAs(copy, "the copy keeps the self-reference instead of recursing without end");
        copy["label"].Should().Be("a&amp;b");
        Render("{\"fetchXml\":\"value='{{joinIds myMatters.output.ids}}'\"}", context, ExecutorType.QueryDataverse)
            .Should().Contain(((List<object?>)membership["ids"]!)[0]!.ToString());
    }

    // =========================================================================================
    // The position table is closed: pinned exactly, and complete against the executors' own schemas.
    // =========================================================================================

    [Fact]
    public void QueryTextPositions_AreExactlyFetchXmlAndTheIndexFilter()
    {
        PlaybookOrchestrationService.QueryTextPositions.Keys.Should().BeEquivalentTo(
            new[] { ExecutorType.QueryDataverse, ExecutorType.IndexRetrieve });
        PlaybookOrchestrationService.QueryTextPositions[ExecutorType.QueryDataverse].Should().BeEquivalentTo(
            new Dictionary<string, PlaybookTemplateContextBuilder.QueryTextLanguage>
            {
                ["fetchXml"] = PlaybookTemplateContextBuilder.QueryTextLanguage.FetchXml,
            });
        PlaybookOrchestrationService.QueryTextPositions[ExecutorType.IndexRetrieve].Should().BeEquivalentTo(
            new Dictionary<string, PlaybookTemplateContextBuilder.QueryTextLanguage>
            {
                ["filter"] = PlaybookTemplateContextBuilder.QueryTextLanguage.OData,
            });
    }

    [Fact]
    public void EveryExecutorConfigField_DescribedAsQueryText_IsAQueryTextPosition()
    {
        // GetConfigSchema returns a static schema on every executor, so an uninitialized instance (no constructor
        // dependencies) can answer it.
        var executorTypes = typeof(INodeExecutor).Assembly.GetTypes()
            .Where(t => typeof(INodeExecutor).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
            .ToList();
        executorTypes.Should().NotBeEmpty();

        var missing = new List<string>();
        foreach (var type in executorTypes)
        {
            if (type.GetMethod(nameof(INodeExecutor.GetConfigSchema),
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly) is null)
            {
                continue; // the interface default: an empty placeholder schema with no fields
            }

            var schema = ((INodeExecutor)RuntimeHelpers.GetUninitializedObject(type)).GetConfigSchema();
            foreach (var field in schema.Fields)
            {
                var description = field.Description ?? string.Empty;
                var isQueryText = description.Contains("query string", StringComparison.OrdinalIgnoreCase)
                                  || description.Contains("OData filter clause", StringComparison.OrdinalIgnoreCase);
                if (!isQueryText)
                {
                    continue;
                }

                var executor = (ExecutorType)schema.ExecutorTypeValue;
                if (!PlaybookOrchestrationService.QueryTextPositions.TryGetValue(executor, out var positions)
                    || !positions.ContainsKey(field.Name))
                {
                    missing.Add($"{type.Name}.{field.Name}");
                }
            }
        }

        missing.Should().BeEmpty(
            "a config field the executor runs as query text must be escaped at the substitution point — add it to " +
            "PlaybookOrchestrationService.QueryTextPositions with its language");
    }
}
