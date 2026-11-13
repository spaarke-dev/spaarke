using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// spaarkeai-mcp-bff-api-r1 task 018 (spec FR-04 as amended 2026-11-08, owner ruling "option A widened"): the privilege rule of a Work
/// Item over its communications, with the caller's Dataverse client faked at the module boundary (<see cref="IDataverseUserClient"/>).
/// The same rule through the real worklist read and the Decide-lane page is <c>tests/integration/regression/Signals/DecideLanePagingTests.cs</c>.
/// </summary>
/// <remarks>Maintain-class (ADR-038, <c>tests/unit/domain/**</c>): the fail-closed rule is a confidentiality contract (ADR-015), and
/// ontology DI-143 (#2630) reuses the helper for the Decision Record snapshot.</remarks>
public sealed class WorkItemPrivilegeTests
{
    private const int None = 100000000;
    private const int PotentiallyPrivileged = 100000001;
    private const int Privileged = 100000002;

    /// <summary>Evidence lines as the evaluator writes them (SignalEvaluator: kind = the table without its "sprk_" prefix, ref = "{table}:{id}").</summary>
    private static string Evidence(params string[] refs) =>
        JsonSerializer.Serialize(refs.Select(r => new { kind = KindOf(r), @ref = r, tier = "Fact" }));

    private static string KindOf(string reference) =>
        reference.IndexOf(':') is var at and > 0 ? reference[..at].ToLowerInvariant().Replace("sprk_", string.Empty) : "billing.reference";

    // ── SourcesOf: what an item is about (no read) ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void SourcesOf_ACommunicationSubject_AndCommunicationWitnesses_AreTheSources_Deduplicated()
    {
        var subject = Guid.NewGuid();
        var witness = Guid.NewGuid();

        var sources = WorkItemPrivilege.SourcesOf("sprk_communication", subject,
            Evidence($"sprk_communication:{witness:D}", $"SPRK_Communication:{witness:D}", $"sprk_document:{Guid.NewGuid():D}", "billing.reference"));

        sources.Undetermined.Should().BeFalse();
        sources.CommunicationIds.Should().BeEquivalentTo([subject, witness]);
    }

    [Fact]
    public void SourcesOf_ANonCommunicationSubject_WithNoCommunicationEvidence_IsNone()
    {
        WorkItemPrivilege.SourcesOf("sprk_matter", Guid.NewGuid(), Evidence($"sprk_document:{Guid.NewGuid():D}"))
            .Should().Be(WorkItemPrivilegeSources.None);
        WorkItemPrivilege.SourcesOf("sprk_matter", Guid.NewGuid(), null).Should().Be(WorkItemPrivilegeSources.None);
        WorkItemPrivilege.SourcesOf(null, null, "[]").Should().Be(WorkItemPrivilegeSources.None);
    }

    [Fact]
    public void SourcesOf_AnUntypedSubject_IsReadAsACommunication_BecauseACommunicationHasNoCatalogRow()
    {
        // DI-63: sprk_communication has no sprk_recordtype_ref row, so a communication subject is an untyped pair. The read then decides:
        // returned = its classification; not returned = flagged.
        var subject = Guid.NewGuid();
        WorkItemPrivilege.SourcesOf(null, subject, null).Should().BeEquivalentTo(new WorkItemPrivilegeSources([subject], false));
    }

    [Fact]
    public void SourcesOf_ACommunicationSubjectWithNoId_IsUndetermined()
    {
        WorkItemPrivilege.SourcesOf("sprk_communication", null, null).Should().Be(WorkItemPrivilegeSources.Unknown);
    }

    [Theory]
    [InlineData("sprk_matter", "[{\"kind\":\"communication\",\"ref\":\"dataverse://sprk_communication/x\"}]")] // a communication line naming none
    [InlineData("sprk_matter", "[{\"kind\":\"communication\"}]")]
    [InlineData("sprk_matter", "{\"ref\":\"sprk_communication:x\"}")] // evidence that is not an array
    [InlineData("sprk_matter", "not json")]
    [InlineData("sprk_matter", "[{\"ref\":\"sprk_communication:not-a-guid\"}]")] // names a communication, but not which
    public void SourcesOf_WhatCannotBeTold_IsUndetermined(string subjectTable, string evidence)
    {
        WorkItemPrivilege.SourcesOf(subjectTable, Guid.NewGuid(), evidence).Should().Be(WorkItemPrivilegeSources.Unknown);
    }

    // ── FlaggedAsync: the caller's read and the rule ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Privileged, true)] // (a)
    [InlineData(PotentiallyPrivileged, true)] // (a)
    [InlineData(None, false)] // (c)
    [InlineData(null, false)] // (c)
    [InlineData(100000099, true)] // an unrecognised value fails closed
    public async Task AnItem_IsFlagged_ByItsCommunicationsClassification(int? classification, bool flagged)
    {
        var communication = Guid.NewGuid();
        var caller = new FakeCaller();
        caller.Rows[communication] = classification;

        var result = await Judge(caller, ("item", new WorkItemPrivilegeSources([communication], false)));

        result.Contains("item").Should().Be(flagged);
    }

    [Fact]
    public async Task AnItem_IsFlagged_WhenAnyOneOfItsCommunicationsIsPrivileged_AndOnlyThen() // (b), (c)
    {
        var plain = Guid.NewGuid();
        var unclassified = Guid.NewGuid();
        var privileged = Guid.NewGuid();
        var caller = new FakeCaller();
        caller.Rows[plain] = None;
        caller.Rows[unclassified] = null;
        caller.Rows[privileged] = Privileged;

        var result = await Judge(caller,
            ("witness-privileged", new WorkItemPrivilegeSources([plain, privileged], false)),
            ("all-plain", new WorkItemPrivilegeSources([plain, unclassified], false)));

        result.Should().BeEquivalentTo(["witness-privileged"]);
    }

    [Fact]
    public async Task ACommunicationTheCallerCannotRead_FlagsItsItem() // (e) not returned
    {
        var readable = Guid.NewGuid();
        var unreadable = Guid.NewGuid();
        var caller = new FakeCaller();
        caller.Rows[readable] = None;

        var result = await Judge(caller,
            ("unreadable", new WorkItemPrivilegeSources([readable, unreadable], false)),
            ("readable", new WorkItemPrivilegeSources([readable], false)));

        result.Should().BeEquivalentTo(["unreadable"]);
        caller.Logs.Should().BeEmpty("an unreadable row is a verdict, not a fault");
    }

    [Fact]
    public async Task NoReadOnTheTable_403_FlagsEveryItemThatNamesOne_AndIsNotLoggedAsAFault() // (e) 403
    {
        var caller = new FakeCaller { Answer = _ => DataverseUserResponse.Fail(403, DataverseUserClientErrorCodes.AccessDenied, "denied") };

        var result = await Judge(caller,
            ("communication", new WorkItemPrivilegeSources([Guid.NewGuid()], false)),
            ("none", WorkItemPrivilegeSources.None));

        result.Should().BeEquivalentTo(["communication"]);
        caller.Logs.Should().BeEmpty();
    }

    public static TheoryData<string> Faults => new() { "500", "401", "429", "unparseable", "nextLink", "throw" };

    [Theory]
    [MemberData(nameof(Faults))]
    public async Task AReadFault_FlagsEveryItemThatNeededTheRead_ButNotAnItemWithNoCommunication(string fault) // (d)
    {
        var communication = Guid.NewGuid();
        var caller = new FakeCaller();
        caller.Rows[communication] = None;
        caller.Answer = fault switch
        {
            "500" => _ => DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "boom"),
            "401" => _ => DataverseUserResponse.Fail(401, DataverseUserClientErrorCodes.AccessDenied, "token expired"),
            "429" => _ => DataverseUserResponse.Fail(429, DataverseUserClientErrorCodes.RateLimited, "throttled"),
            "unparseable" => _ => DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { rows = 1 })),
            "nextLink" => path => DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["value"] = caller.RowsFor(path),
                ["@odata.nextLink"] = "https://example/next",
            })),
            _ => _ => throw new TimeoutException("read timed out"),
        };

        var result = await Judge(caller,
            ("communication", new WorkItemPrivilegeSources([communication], false)),
            ("none", WorkItemPrivilegeSources.None));

        result.Should().BeEquivalentTo(["communication"], "a fault is never 'not privileged' (fail closed)");
        caller.Logs.Should().ContainSingle().Which.Should().Contain("sprk_communication").And.NotContain(communication.ToString());
    }

    [Fact]
    public async Task ACancelledRead_IsRethrown_NotTakenAsAFault()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var caller = new FakeCaller { Answer = _ => throw new OperationCanceledException() };

        var act = () => new WorkItemPrivilege(caller.Logger).FlaggedAsync(
            caller, new Dictionary<string, WorkItemPrivilegeSources> { ["x"] = new([Guid.NewGuid()], false) }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ItemsWithNoCommunication_AreNotFlagged_AndNothingIsRead_AndUndetermined_IsFlaggedWithoutARead() // (f)
    {
        var caller = new FakeCaller();

        var result = await Judge(caller,
            ("none", WorkItemPrivilegeSources.None),
            ("unknown", WorkItemPrivilegeSources.Unknown));

        result.Should().BeEquivalentTo(["unknown"]);
        caller.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task OneBatchedRead_ForEveryItem_InListsOfAtMost200DistinctIds() // (g)
    {
        var shared = Guid.NewGuid();
        var caller = new FakeCaller();
        caller.Rows[shared] = None;
        var items = Enumerable.Range(0, 50).Select(n =>
        {
            var own = Guid.NewGuid();
            caller.Rows[own] = None;
            return ($"item{n}", new WorkItemPrivilegeSources([own, shared], false));
        }).ToArray();

        (await Judge(caller, items)).Should().BeEmpty();
        caller.Requests.Should().ContainSingle("51 distinct ids are one read, never one per item")
            .Which.Should().StartWith("sprk_communications?$select=sprk_communicationid,sprk_privilegeclassification&$filter=");
        Regex.Matches(caller.Requests[0], "'[0-9a-f-]{36}'").Should().HaveCount(51);

        var many = new FakeCaller();
        var manyItems = Enumerable.Range(0, 50).Select(n =>
        {
            var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
            ids.ForEach(id => many.Rows[id] = None);
            return ($"item{n}", new WorkItemPrivilegeSources(ids, false));
        }).ToArray();

        (await Judge(many, manyItems)).Should().BeEmpty();
        many.Requests.Select(r => Regex.Matches(r, "'[0-9a-f-]{36}'").Count).Should().Equal(200, 50);
    }

    private static Task<IReadOnlySet<string>> Judge(FakeCaller caller, params (string Key, WorkItemPrivilegeSources Sources)[] items) =>
        new WorkItemPrivilege(caller.Logger).FlaggedAsync(caller, items.ToDictionary(i => i.Key, i => i.Sources), CancellationToken.None);

    /// <summary>The caller's Dataverse client: answers the In-list read from <see cref="Rows"/> (a row the caller reads, with its
    /// classification; absent = the caller cannot read it), unless <see cref="Answer"/> overrides it.</summary>
    private sealed class FakeCaller : IDataverseUserClient
    {
        public Dictionary<Guid, int?> Rows { get; } = [];
        public List<string> Requests { get; } = [];
        public List<string> Logs { get; } = [];
        public Func<string, DataverseUserResponse>? Answer { get; set; }
        public ILogger<WorkItemPrivilege> Logger => new CapturingLogger(Logs);

        public List<Dictionary<string, object?>> RowsFor(string path) =>
            Regex.Matches(path, "'([0-9a-f-]{36})'").Select(m => Guid.Parse(m.Groups[1].Value))
                .Where(Rows.ContainsKey)
                .Select(id => new Dictionary<string, object?> { ["sprk_communicationid"] = id.ToString("D"), ["sprk_privilegeclassification"] = Rows[id] })
                .ToList();

        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
        {
            Requests.Add(relativePath);
            return Task.FromResult(Answer is { } answer
                ? answer(relativePath)
                : DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { value = RowsFor(relativePath) })));
        }

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("never writes");

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("never writes");

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("never writes");

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("never writes");
    }

    private sealed class CapturingLogger(List<string> logs) : ILogger<WorkItemPrivilege>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning) logs.Add(formatter(state, exception));
        }
    }
}
