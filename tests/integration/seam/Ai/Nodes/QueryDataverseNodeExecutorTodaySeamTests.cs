// spaarke-ontology-platform-r1 task 098 — {{todayUtc}} in a QueryDataverse node is the RUN USER's local calendar date.
//
// The live "Query Overdue Tasks" node filters `sprk_duedate lt {{todayUtc}}` (and sprk_finalduedate) — Date Only columns.
// When the run carries no scheduler-supplied todayUtc parameter, this executor fills the variable itself; it took
// DateTime.UtcNow's date, so at 21:00 Eastern (the UTC day already tomorrow) a task due today matched as overdue.
// Pinned at 2026-10-06T01:00Z = 21:00 on Oct 5 in New York.

using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Ai.Nodes;

[Trait("status", "task-098-ontology-r1")]
public class QueryDataverseNodeExecutorTodaySeamTests
{
    private static readonly DateTimeOffset EveningEastern = DateTimeOffset.Parse("2026-10-06T01:00:00Z", CultureInfo.InvariantCulture);
    private static readonly Guid UserId = Guid.Parse("1d02f31c-1872-f011-b4cb-7c1e52671ad0");

    private const string OverdueFetch =
        "<fetch top=\"50\"><entity name=\"sprk_event\"><attribute name=\"sprk_eventid\"/><filter type=\"or\">"
        + "<condition attribute=\"sprk_duedate\" operator=\"lt\" value=\"{{todayUtc}}\"/>"
        + "<condition attribute=\"sprk_finalduedate\" operator=\"lt\" value=\"{{todayUtc}}\"/></filter></entity></fetch>";

    [Fact]
    public async Task TodayUtc_IsTheRunUsersLocalDate()
    {
        var (executor, fetches) = Executor(timeZoneCode: 35, standardName: "Eastern Standard Time");

        var result = await executor.ExecuteAsync(Context(UserId), CancellationToken.None);

        result.Success.Should().BeTrue();
        fetches.Should().ContainSingle().Which.Should()
            .Contain("value=\"2026-10-05\"", "Oct 5 is the user's today at 21:00 Eastern")
            .And.NotContain("2026-10-06");
    }

    [Fact]
    public async Task TodayUtc_WithoutARunUser_IsTheUtcDate()
    {
        var (executor, fetches) = Executor(timeZoneCode: 35, standardName: "Eastern Standard Time");

        await executor.ExecuteAsync(Context(userId: null), CancellationToken.None);

        fetches.Should().ContainSingle().Which.Should().Contain("value=\"2026-10-06\"");
    }

    private static (QueryDataverseNodeExecutor Executor, List<string> Fetches) Executor(int timeZoneCode, string standardName)
    {
        var fetches = new List<string>();
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveAsync("usersettings", UserId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("usersettings", UserId) { ["timezonecode"] = timeZoneCode });
        entities.Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "timezonedefinition"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { new("timezonedefinition") { ["standardname"] = standardName } }));
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Callback<FetchExpression, CancellationToken>((f, _) => fetches.Add(f.Query))
            .ReturnsAsync(new EntityCollection());
        var executor = new QueryDataverseNodeExecutor(
            new TemplateEngine(NullLogger<TemplateEngine>.Instance), entities.Object,
            NullLogger<QueryDataverseNodeExecutor>.Instance, new FakeTimeProvider(EveningEastern));
        return (executor, fetches);
    }

    private static NodeExecutionContext Context(Guid? userId)
    {
        var actionId = Guid.NewGuid();
        var config = System.Text.Json.JsonSerializer.Serialize(new { entityLogicalName = "sprk_event", fetchXml = OverdueFetch });
        return new NodeExecutionContext
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            Node = new PlaybookNodeDto
            {
                Id = Guid.NewGuid(),
                PlaybookId = Guid.NewGuid(),
                ActionId = actionId,
                Name = "Query Overdue Tasks",
                ExecutionOrder = 1,
                OutputVariable = "overdueQuery",
                ConfigJson = config,
                IsActive = true,
            },
            Action = new AnalysisAction { Id = actionId, Name = "Query Overdue Tasks" },
            ExecutorType = ExecutorType.QueryDataverse,
            Scopes = new ResolvedScopes([], [], []),
            TenantId = "test-tenant",
            UserId = userId,
        };
    }
}
