using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Ontology task 233 (D-232, #3275): <see cref="WorkItemPrivilege.FlaggedAppOnlyAsync{TKey}"/>, the same rule as the caller read
/// (<see cref="WorkItemPrivilegeTests"/>) read with the application identity for the nightly's ConditionCleared record. The job's end-to-end
/// cases are <c>SignalReevaluationJobTests.ConditionClearedPrivilege.cs</c>; these pin the read's own fail-closed edges and its batching.
/// </summary>
/// <remarks>Maintain-class (ADR-038, <c>tests/unit/domain/**</c>): the fail-closed rule is a confidentiality contract (ADR-015).</remarks>
public sealed class WorkItemPrivilegeAppOnlyTests
{
    private const int None = 100000000;
    private const int PotentiallyPrivileged = 100000001;
    private const int Privileged = 100000002;

    [Fact]
    public async Task EachClassification_FlagsAsTheCallerRuleDoes_AndAMissingRowFlags()
    {
        var app = Application.Create();
        Guid Row(int? classification)
        {
            var id = Guid.NewGuid();
            app.Rows[id] = classification;
            return id;
        }

        var none = Row(None);
        var unclassified = Row(null);
        var potentially = Row(PotentiallyPrivileged);
        var privileged = Row(Privileged);
        var unrecognised = Row(999);

        var result = await Judge(app,
            ("none", new([none], false)),
            ("unclassified", new([unclassified], false)),
            ("potentially", new([none, potentially], false)),
            ("privileged", new([privileged], false)),
            ("unrecognised", new([unrecognised], false)),
            ("missing", new([Guid.NewGuid()], false)),
            ("no-communication", WorkItemPrivilegeSources.None),
            ("undetermined", WorkItemPrivilegeSources.Unknown));

        result.Should().BeEquivalentTo(["potentially", "privileged", "unrecognised", "missing", "undetermined"]);
        app.Queries.Should().ContainSingle("one batched read for every item");
        app.Queries[0].ColumnSet.Columns.Should().BeEquivalentTo("sprk_communicationid", "sprk_privilegeclassification");
    }

    [Fact]
    public async Task NoItemNamesACommunication_NothingIsRead()
    {
        var app = Application.Create();

        var result = await Judge(app, ("none", WorkItemPrivilegeSources.None), ("unknown", WorkItemPrivilegeSources.Unknown));

        result.Should().BeEquivalentTo(["unknown"]);
        app.Queries.Should().BeEmpty();
    }

    [Theory]
    [InlineData("throw")]
    [InlineData("moreRecords")]
    public async Task AFault_FlagsEveryItemThatNeededTheRead_ButNotAnItemWithNoCommunication(string fault)
    {
        var app = Application.Create();
        var communication = Guid.NewGuid();
        app.Rows[communication] = None;
        if (fault == "throw") app.Fault = new TimeoutException("Simulated read timeout.");
        else app.MoreRecords = true;

        var result = await Judge(app, ("communication", new([communication], false)), ("none", WorkItemPrivilegeSources.None));

        result.Should().BeEquivalentTo(["communication"], "a fault is never 'not privileged' (fail closed)");
    }

    [Fact]
    public async Task ACancelledRead_IsRethrown()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var app = Application.Create();
        app.Fault = new OperationCanceledException();

        var act = () => new WorkItemPrivilege(NullLogger<WorkItemPrivilege>.Instance).FlaggedAppOnlyAsync(
            (IGenericEntityService)app, new Dictionary<string, WorkItemPrivilegeSources> { ["x"] = new([Guid.NewGuid()], false) }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ManyIds_AreReadInListsOfAtMost200()
    {
        var app = Application.Create();
        var items = Enumerable.Range(0, 50).Select(n =>
        {
            var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
            ids.ForEach(id => app.Rows[id] = None);
            return ($"item{n}", new WorkItemPrivilegeSources(ids, false));
        }).ToArray();

        (await Judge(app, items)).Should().BeEmpty();
        app.Queries.Select(q => q.Criteria.Conditions.Single().Values.Count).Should().Equal(200, 50);
    }

    private static Task<IReadOnlySet<string>> Judge(Application app, params (string Key, WorkItemPrivilegeSources Sources)[] items) =>
        new WorkItemPrivilege(NullLogger<WorkItemPrivilege>.Instance).FlaggedAppOnlyAsync(
            (IGenericEntityService)app, items.ToDictionary(i => i.Key, i => i.Sources), CancellationToken.None);

    /// <summary>The application-identity Dataverse service, answering only <c>RetrieveMultipleAsync</c> on <c>sprk_communication</c>
    /// from <see cref="Rows"/> (absent = the row does not exist). Every other member throws: the rule never writes.</summary>
    public class Application : DispatchProxy
    {
        public Dictionary<Guid, int?> Rows { get; } = [];
        public List<QueryExpression> Queries { get; } = [];
        public Exception? Fault { get; set; }
        public bool MoreRecords { get; set; }

        public static Application Create() => (Application)(object)Create<IGenericEntityService, Application>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IGenericEntityService.RetrieveMultipleAsync) || args?[0] is not QueryExpression query
                || query.EntityName != "sprk_communication")
            {
                throw new InvalidOperationException($"Unexpected call: {targetMethod?.Name}.");
            }

            Queries.Add(query);
            if (Fault is { } fault) return Task.FromException<EntityCollection>(fault);
            var asked = query.Criteria.Conditions.Single(c => c.AttributeName == "sprk_communicationid" && c.Operator == ConditionOperator.In)
                .Values.OfType<Guid>();
            var result = new EntityCollection { MoreRecords = MoreRecords };
            foreach (var id in asked.Where(Rows.ContainsKey))
            {
                result.Entities.Add(new Entity("sprk_communication", id)
                {
                    ["sprk_privilegeclassification"] = Rows[id] is { } value ? new OptionSetValue(value) : null,
                });
            }

            return Task.FromResult(result);
        }
    }
}
