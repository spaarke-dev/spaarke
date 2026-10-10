using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// #1387 / ISS-010: the category NAME the classifier emits is resolved back to a <c>sprk_triagecategory</c> row only
/// among rows the classifier was OFFERED (statecode active AND sprk_enabled). The category double here EVALUATES the
/// query's conditions against a row set (and throws on a condition it does not understand), so a query that omits the
/// predicate returns the disabled / inactive row and the assertion fails.
/// </summary>
public sealed class TriageCategoryOfferedRowSeamTests
{
    private sealed record CategoryRow(Guid Id, string Name, int StateCode, bool Enabled);

    private static readonly CategoryRow Live = new(Guid.NewGuid(), "Court / Filing", 0, true);
    private static readonly CategoryRow Disabled = new(Guid.NewGuid(), "Marketing / Promo", 0, false);
    private static readonly CategoryRow Inactive = new(Guid.NewGuid(), "Legacy / Notice", 1, true);
    private static readonly CategoryRow[] Rows = [Live, Disabled, Inactive];

    /// <summary>Same persisted-signal provenance the persistence seam uses, so the triage step runs and persists.</summary>
    private const string Provenance =
        """
        {"version":1,"direction":"Incoming","decision":{"status":"Resolved","autoFiled":true,"killSwitchEnabled":true,"autoFileThreshold":0.85,"topDeterministicConfidence":0.8,"topConfidence":0.8,"aiInvolved":false,"reason":"test"},"rungsFired":["ExplicitReference"],"candidates":[],"signals":[{"category":"court-notice","confidence":0.6,"provenance":"ai-classify:category=court-notice:urgency=urgent:types=[sprk_matter]:actions=[calendar-deadline]:Court deadline.","obligations":["respond-by-deadline"]}]}
        """;

    private static EntityCollection Evaluate(QueryExpression q)
    {
        q.EntityName.Should().Be("sprk_triagecategory");
        q.Criteria.Filters.Should().BeEmpty("the test evaluator understands a flat AND of conditions only");
        q.Criteria.FilterOperator.Should().Be(LogicalOperator.And);

        IEnumerable<CategoryRow> rows = Rows;
        foreach (var c in q.Criteria.Conditions)
        {
            c.Operator.Should().Be(ConditionOperator.Equal);
            var v = c.Values.Single();
            rows = c.AttributeName switch
            {
                "sprk_name" => rows.Where(r => string.Equals(r.Name, (string)v, StringComparison.OrdinalIgnoreCase)),
                "statecode" => rows.Where(r => r.StateCode == Convert.ToInt32(v)),
                "sprk_enabled" => rows.Where(r => r.Enabled == (bool)v),
                _ => throw new InvalidOperationException($"unexpected condition on {c.AttributeName}"),
            };
        }

        var result = new EntityCollection();
        foreach (var r in rows.Take(q.TopCount ?? int.MaxValue))
        {
            var e = new Entity("sprk_triagecategory", r.Id);
            e["sprk_name"] = r.Name;
            result.Entities.Add(e);
        }
        return result;
    }

    private static async Task<Dictionary<string, object>?> RunAsync(string categoryName)
    {
        Dictionary<string, object>? captured = null;
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entityService
            .Setup(s => s.RetrieveAsync("sprk_communication", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var e = new Entity("sprk_communication", Guid.NewGuid());
                e["sprk_associationprovenance"] = Provenance;
                return e;
            });
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) => Evaluate(q));
        entityService
            .Setup(s => s.UpdateAsync("sprk_communication", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, f, _) => captured = f)
            .Returns(Task.CompletedTask);

        var triageAi = new Mock<ICommunicationTriageAi>(MockBehavior.Loose);
        triageAi
            .Setup(t => t.TriageAsync(It.IsAny<CommunicationTriageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommunicationTriageResult(categoryName, "Summary.", Array.Empty<string>(), "Medium", "Pending"));

        var sut = new CommunicationEnrichmentService(
            EnrichmentScopeFactoryStub.Create(
                new Mock<IPostUploadIndexingEnqueuer>(MockBehavior.Loose).Object,
                triageAi.Object, new NullCommunicationProposeAi(), new NullCommunicationCreateTaskAi()),
            entityService.Object,
            new ConfigurationBuilder().Build(),
            new Mock<ICommunicationAssessedProducer>(MockBehavior.Loose).Object,
            new Mock<IActionSeam>(MockBehavior.Loose).Object,
            TestRoutingGate.Disabled(),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            Mock.Of<IFieldMappingDataverseService>(),
            NullLogger<CommunicationEnrichmentService>.Instance);

        await sut.EnrichAsync(Guid.NewGuid(), CommunicationDirection.Incoming,
            new NormalizedMessage
            {
                Direction = CommunicationDirection.Incoming,
                From = "sender@example.com",
                To = new[] { "reviewer@example.com" },
                Subject = "s",
                BodyText = "b",
            },
            archivedDocumentId: null, CancellationToken.None);
        return captured;
    }

    [Theory]
    [InlineData("Court / Filing")] // exact
    [InlineData("court/filing")]   // normalized fallback
    public async Task EnabledActiveCategory_Resolves(string name)
    {
        var fields = await RunAsync(name);
        fields.Should().NotBeNull();
        ((EntityReference)fields!["sprk_triagecategory"]).Id.Should().Be(Live.Id);
    }

    [Theory]
    [InlineData("Marketing / Promo")] // disabled, exact
    [InlineData("marketing/promo")]   // disabled, normalized fallback
    [InlineData("Legacy / Notice")]   // inactive, exact
    [InlineData("legacy/notice")]     // inactive, normalized fallback
    public async Task DisabledOrInactiveCategory_LeavesCategoryUnset(string name)
    {
        var fields = await RunAsync(name);
        fields.Should().NotBeNull("the other triage fields still persist");
        fields!.Should().NotContainKey("sprk_triagecategory", "a row the classifier was never offered must not be saved");
    }

    [Fact]
    public void RowPredicate_IsTheSameDefinitionAsTheChoicesRead()
    {
        var q = new QueryExpression("sprk_triagecategory");
        LookupChoicesResolver.AddOfferedRowPredicate(q.Criteria, "sprk_triagecategory");

        LookupChoicesResolver.AdditionalFilterFor("sprk_triagecategory").Should().Be("sprk_enabled eq true");
        q.Criteria.Conditions.Select(c => (c.AttributeName, c.Values.Single()))
            .Should().BeEquivalentTo(new[] { ("statecode", (object)0), ("sprk_enabled", (object)true) });

        var other = new QueryExpression("sprk_other");
        LookupChoicesResolver.AddOfferedRowPredicate(other.Criteria, "sprk_other");
        other.Criteria.Conditions.Select(c => c.AttributeName).Should().Equal("statecode");
    }
}
