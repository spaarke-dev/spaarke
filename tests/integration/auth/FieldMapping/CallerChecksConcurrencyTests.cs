using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.FieldMapping;

/// <summary>
/// field-mapping-server-write-path-r1 task 049 (NFR-02 Part A; UAC-r2's conditions, 2026-11-05): the caller checks of the
/// app-only write path (<see cref="CallerWriteAuthorization.CheckCallerMayCreateAsync"/>) ask the table's privileges, the
/// field-secured columns and AppendTo on every record named TOGETHER, at most
/// <see cref="CallerWriteAuthorization.MaxRecordsAskedAtOnce"/> records at once, and still answer exactly as when they were
/// asked one by one: the first refusal, failure or fault in that order answers — never the first to finish — and every
/// check has returned before anything is answered.
/// </summary>
/// <remarks>
/// The caller's Dataverse is a scripted <see cref="IDataverseUserClient"/> (the documented mock boundary) whose answers can
/// be delayed per record, so a check that is FIRST in order can be made to finish LAST. Part C's
/// <see cref="CallerMetadataReadMemo"/> is pinned at the end: table metadata is read once per create, access never.
/// </remarks>
public partial class CallerChecksConcurrencyTests
{
    private const string Table = "sprk_event";
    private static readonly Guid Me = Guid.Parse("49000000-0000-4000-8000-0000000000aa");
    private static readonly Guid PairType = Guid.Parse("49000000-0000-4000-8000-0000000000b1");
    private static readonly Guid PairRecord = Guid.Parse("49000000-0000-4000-8000-0000000000b2");

    private static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(300);

    private readonly ScriptedCaller _caller = new();

    // ── AC2: concurrency, and its bound ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AppendTo_OnFourRecords_IsAskedConcurrently_InAboutOneDelay_NotFour()
    {
        var lookups = Contacts(4, appendable: true);
        _caller.DelayAll = Slow;

        var clock = Stopwatch.StartNew();
        var outcome = await CallerWriteAuthorization.CheckCallerMayAppendToAsync(_caller, Me, lookups, CancellationToken.None);
        clock.Stop();

        outcome.Succeeded.Should().BeTrue();
        _caller.MaxInFlight.Should().Be(4, "the four records are asked at once");
        clock.Elapsed.Should().BeLessThan(Slow * 2.5, "one delay, not four (sequential took 4 x 300 ms)");
    }

    [Fact]
    public async Task AppendTo_OnEightRecords_AsksAtMostFourAtOnce()
    {
        var lookups = Contacts(8, appendable: true);
        _caller.DelayAll = TimeSpan.FromMilliseconds(150);

        var clock = Stopwatch.StartNew();
        var outcome = await CallerWriteAuthorization.CheckCallerMayAppendToAsync(_caller, Me, lookups, CancellationToken.None);
        clock.Stop();

        outcome.Succeeded.Should().BeTrue();
        CallerWriteAuthorization.MaxRecordsAskedAtOnce.Should().Be(4);
        _caller.MaxInFlight.Should().Be(4, "UAC-r2: a create naming many records does not flood Dataverse");
        clock.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(290), "eight records at four at a time are two waves");
        _caller.Asked.Count(p => p.Contains("RetrievePrincipalAccess", StringComparison.Ordinal)).Should().Be(8);
    }

    [Fact]
    public async Task TheWholeCheck_TakesItsLongestChain_NotTheSumOfItsReads()
    {
        // Sequentially: metadata, privileges, columns and three records = six reads (6 x 300 ms). Together: the longest
        // chain, metadata then privileges (2 x 300 ms).
        var item = Item(Contacts(3, appendable: true));
        _caller.DelayAll = Slow;

        var clock = Stopwatch.StartNew();
        var outcome = await CallerWriteAuthorization.CheckCallerMayCreateAsync(_caller, Me, Table, item, null, CancellationToken.None);
        clock.Stop();

        outcome.Succeeded.Should().BeTrue();
        clock.Elapsed.Should().BeLessThan(Slow * 4, "about two delays, not six");
    }

    // ── AC1 / NFR-07: the same refusal answers as before, whichever check finishes first ───────────────────────────

    [Fact]
    public async Task TwoRecordsRefused_TheFirstInOrderAnswers_EvenWhenItFinishesLast()
    {
        var lookups = Contacts(2, appendable: false);
        _caller.DelayFor[lookups[0].RecordId] = Slow;

        var outcome = await CallerWriteAuthorization.CheckCallerMayAppendToAsync(_caller, Me, lookups, CancellationToken.None);

        ShouldBeTheUniformNotFound(outcome, lookups[0].Column);
    }

    [Fact]
    public async Task ARecordRefused_BeforeARecordWhoseCheckFaults_TheRefusalAnswers()
    {
        var lookups = Contacts(2, appendable: false);
        _caller.DelayFor[lookups[0].RecordId] = Slow;
        _caller.ThrowOn = path => path.Contains(lookups[1].RecordId.ToString("D"), StringComparison.Ordinal)
            ? new InvalidOperationException("the second record's check faulted")
            : null;

        var outcome = await CallerWriteAuthorization.CheckCallerMayAppendToAsync(_caller, Me, lookups, CancellationToken.None);

        ShouldBeTheUniformNotFound(outcome, lookups[0].Column);
    }

    [Fact]
    public async Task ARecordWhoseCheckFaults_BeforeARecordRefused_TheFaultPropagates_FailClosed()
    {
        var lookups = Contacts(2, appendable: false);
        _caller.DelayFor[lookups[0].RecordId] = Slow;
        _caller.ThrowOn = path => path.Contains(lookups[0].RecordId.ToString("D"), StringComparison.Ordinal)
            ? new InvalidOperationException("the first record's check faulted")
            : null;

        var act = () => CallerWriteAuthorization.CheckCallerMayAppendToAsync(_caller, Me, lookups, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("the first record's check faulted");
    }

    [Fact]
    public async Task AnUnknownRecord_AndADeniedOne_GetTheSameAnswer()
    {
        var denied = Contacts(1, appendable: false);
        var unknown = Contacts(1, appendable: false, exists: false);

        var deniedOutcome = await CallerWriteAuthorization.CheckCallerMayAppendToAsync(_caller, Me, denied, CancellationToken.None);
        var unknownOutcome = await CallerWriteAuthorization.CheckCallerMayAppendToAsync(_caller, Me, unknown, CancellationToken.None);

        unknownOutcome.Should().BeEquivalentTo(deniedOutcome);
        ShouldBeTheUniformNotFound(unknownOutcome, unknown[0].Column);
    }

    [Fact]
    public async Task EveryCheckHasReturned_BeforeTheAnswer_EvenWhenTheFirstRefusesAtOnce()
    {
        var lookups = new[] { Contacts(1, appendable: false)[0], Contacts(1, appendable: true)[0] };
        _caller.DelayFor[lookups[1].RecordId] = Slow;

        var clock = Stopwatch.StartNew();
        var outcome = await CallerWriteAuthorization.CheckCallerMayAppendToAsync(_caller, Me, lookups, CancellationToken.None);
        clock.Stop();

        ShouldBeTheUniformNotFound(outcome, lookups[0].Column);
        _caller.InFlight.Should().Be(0, "UAC-r2: nothing answers (and so nothing is written) before every check has returned");
        clock.Elapsed.Should().BeGreaterThanOrEqualTo(Slow - TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public async Task NoCreatePrivilege_WithASecuredColumnAndARefusedRecord_TheCreatePrivilegeAnswers()
    {
        _caller.Held.Remove($"prvCreate{Table}");
        _caller.Secured.Add("sprk_description");
        _caller.DelayAll = Slow; // the privilege chain is two reads: it finishes last
        var item = Item(Contacts(1, appendable: false), "sprk_description");

        var outcome = await CallerWriteAuthorization.CheckCallerMayCreateAsync(_caller, Me, Table, item, null, CancellationToken.None);

        outcome.Denied.Should().Be($"You do not have permission to create records in '{Table}' (prvCreate{Table}).");
        outcome.RefusedColumn.Should().BeNull();
    }

    [Fact]
    public async Task TheTableMetadataUnreadable_WithARefusedRecord_TheFailureAnswers()
    {
        _caller.MetadataFailure = DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "unavailable");
        _caller.DelayFor[Guid.Empty] = Slow; // the metadata read (keyed by no record) finishes last
        var item = Item(Contacts(1, appendable: false));

        var outcome = await CallerWriteAuthorization.CheckCallerMayCreateAsync(_caller, Me, Table, item, null, CancellationToken.None);

        outcome.ClientFailure.Should().BeSameAs(_caller.MetadataFailure);
        outcome.Denied.Should().BeNull();
    }

    [Fact]
    public async Task ASecuredColumn_WithARefusedRecord_TheSecuredColumnAnswers()
    {
        _caller.Secured.Add("sprk_description");
        _caller.DelayFor[Guid.Empty] = Slow;
        var item = Item(Contacts(1, appendable: false), "sprk_description");

        var outcome = await CallerWriteAuthorization.CheckCallerMayCreateAsync(_caller, Me, Table, item, null, CancellationToken.None);

        outcome.Denied.Should().Be("Column 'sprk_description' is field-secured and cannot be set on a record the application creates for you.");
    }

    [Fact]
    public async Task APairTypeTheCallerCannotRead_WithARefusedRecord_ThePairAnswers()
    {
        var contact = Contacts(1, appendable: false)[0];
        var typeLookup = new DataverseWriteItemMapper.MappedLookup(
            SecureRootInheritance.PairTypeColumn, "sprk_recordtype_ref", "sprk_recordtype_refs", PairType);
        var item = new DataverseWriteItemMapper.MappedItem(
            JsonSerializer.Serialize(new Dictionary<string, object> { [SecureRootInheritance.PairIdColumn] = PairRecord.ToString("D") }),
            [SecureRootInheritance.PairIdColumn, SecureRootInheritance.PairTypeColumn, contact.Column])
        {
            Lookups = [typeLookup, contact],
        };

        var outcome = await CallerWriteAuthorization.CheckCallerMayCreateAsync(_caller, Me, Table, item, null, CancellationToken.None);

        outcome.Denied.Should().Be("You do not have permission to file this record under the record type it names.");
        outcome.RefusedColumn.Should().Be(SecureRootInheritance.PairIdColumn);
    }

    [Fact]
    public async Task APairIdThatIsNotARecordId_WithARefusedRecord_TheInvalidPairAnswers()
    {
        var contact = Contacts(1, appendable: false)[0];
        var item = new DataverseWriteItemMapper.MappedItem(
            JsonSerializer.Serialize(new Dictionary<string, object> { [SecureRootInheritance.PairIdColumn] = 5 }),
            [SecureRootInheritance.PairIdColumn, contact.Column])
        {
            Lookups = [contact],
        };

        var outcome = await CallerWriteAuthorization.CheckCallerMayCreateAsync(_caller, Me, Table, item, null, CancellationToken.None);

        outcome.Invalid.Should().Be($"'{SecureRootInheritance.PairIdColumn}' must hold the id of the record this row is filed under.");
        outcome.Denied.Should().BeNull();
    }

    [Fact]
    public async Task ARecordWhoseCheckFaults_WithNoCreatePrivilege_TheRefusalAnswers_NotTheFault()
    {
        _caller.Held.Remove($"prvCreate{Table}");
        _caller.DelayFor[Guid.Empty] = Slow;
        var lookups = Contacts(1, appendable: true);
        _caller.ThrowOn = path => path.Contains("RetrievePrincipalAccess", StringComparison.Ordinal)
            ? new InvalidOperationException("a record check faulted")
            : null;

        var outcome = await CallerWriteAuthorization.CheckCallerMayCreateAsync(_caller, Me, Table, Item(lookups), null, CancellationToken.None);

        outcome.Denied.Should().StartWith($"You do not have permission to create records in '{Table}'");
    }

    [Fact]
    public async Task TheTableMetadataReadFaults_WithARefusedRecord_TheFaultPropagates_FailClosed()
    {
        _caller.ThrowOn = path => path.Contains("$select=LogicalName,Privileges", StringComparison.Ordinal)
            ? new InvalidOperationException("the metadata read faulted")
            : null;

        var act = () => CallerWriteAuthorization.CheckCallerMayCreateAsync(
            _caller, Me, Table, Item(Contacts(1, appendable: false)), null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("the metadata read faulted");
    }

    // ── Part C: one create reads a table's metadata once, and still asks every access question ────────────────────

    [Fact]
    public async Task TheMemo_AnswersARepeatedMetadataRead_FromTheFirstAnswer()
    {
        var memo = new CallerMetadataReadMemo(_caller);
        const string path = "EntityDefinitions(LogicalName='sprk_event')?$select=LogicalName,Privileges";

        var first = await memo.GetAsync(path, CancellationToken.None);
        var second = await memo.GetAsync(path, CancellationToken.None);

        _caller.Asked.Count(p => p == path).Should().Be(1);
        second.IsSuccess.Should().BeTrue();
        second.Body!.Value.GetRawText().Should().Be(first.Body!.Value.GetRawText());
    }

    [Fact]
    public async Task TheMemo_NeverKeepsAnAccessAnswer_OrAFailure()
    {
        var memo = new CallerMetadataReadMemo(_caller);
        var record = Contacts(1, appendable: true)[0];
        _caller.MetadataFailure = DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "unavailable");

        for (var i = 0; i < 2; i++)
        {
            await CallerWriteAuthorization.RightsOnAsync(memo, Me, record.RelatedEntitySet, record.RecordId, CancellationToken.None);
            await memo.GetAsync("EntityDefinitions(LogicalName='sprk_event')?$select=LogicalName,Privileges", CancellationToken.None);
        }

        _caller.Asked.Count(p => p.Contains("RetrievePrincipalAccess", StringComparison.Ordinal)).Should().Be(2, "ADR-009: no access answer is kept");
        _caller.Asked.Count(p => p.Contains("$select=LogicalName,Privileges", StringComparison.Ordinal)).Should().Be(2, "a failure is asked again");
    }

    [Fact]
    public async Task TheMemo_ForgetsEverything_OnAWrite()
    {
        var memo = new CallerMetadataReadMemo(_caller);
        const string path = "EntityDefinitions(LogicalName='sprk_event')?$select=LogicalName,Privileges";

        await memo.GetAsync(path, CancellationToken.None);
        await memo.PatchAsync("sprk_events(00000000-0000-0000-0000-000000000001)", "{}", CancellationToken.None);
        await memo.GetAsync(path, CancellationToken.None);

        _caller.Asked.Count(p => p == path).Should().Be(2);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────

    private DataverseWriteItemMapper.MappedLookup[] Contacts(int count, bool appendable, bool exists = true)
    {
        var lookups = new DataverseWriteItemMapper.MappedLookup[count];
        for (var i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
            if (exists)
                _caller.Existing.Add(id);
            if (appendable)
                _caller.Appendable.Add(id);
            lookups[i] = new DataverseWriteItemMapper.MappedLookup($"sprk_contact{i + 1}", "contact", "contacts", id);
        }

        return lookups;
    }

    private static DataverseWriteItemMapper.MappedItem Item(
        IReadOnlyList<DataverseWriteItemMapper.MappedLookup> lookups, params string[] columns) =>
        new("{}", [.. columns, .. lookups.Select(l => l.Column)]) { Lookups = [.. lookups] };

    private static void ShouldBeTheUniformNotFound(OwnedChildWrite.Outcome outcome, string column)
    {
        outcome.Denied.Should().Be("You do not have permission to file this record under the contact it names.");
        outcome.ParentUnavailable.Should().BeTrue();
        outcome.RefusedColumn.Should().Be(column);
    }

    /// <summary>
    /// The caller's Dataverse, scripted and thread-safe: table metadata and held privileges for <see cref="Table"/>,
    /// field-secured columns, contacts that exist and that the caller may append to. Each read can be delayed (all of them,
    /// or by the record id in its path; <see cref="Guid.Empty"/> keys the table-level reads) and made to throw; the most
    /// reads in flight at once is recorded.
    /// </summary>
    private sealed partial class ScriptedCaller : IDataverseUserClient
    {
        private int _inFlight;
        private int _maxInFlight;

        public HashSet<string> Held { get; } = new(StringComparer.OrdinalIgnoreCase) { $"prvCreate{Table}", $"prvAppend{Table}" };

        public HashSet<string> Secured { get; } = new(StringComparer.Ordinal);

        public ConcurrentBag<Guid> Existing { get; } = [];

        public ConcurrentBag<Guid> Appendable { get; } = [];

        public ConcurrentDictionary<Guid, TimeSpan> DelayFor { get; } = new();

        public TimeSpan DelayAll { get; set; }

        public DataverseUserResponse? MetadataFailure { get; set; }

        public Func<string, Exception?>? ThrowOn { get; set; }

        public ConcurrentQueue<string> Asked { get; } = new();

        public int InFlight => Volatile.Read(ref _inFlight);

        public int MaxInFlight => Volatile.Read(ref _maxInFlight);

        [GeneratedRegex(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")]
        private static partial Regex RecordId();

        [GeneratedRegex(@"LogicalName eq '(?<c>[a-z0-9_]+)'")]
        private static partial Regex AttributeNamed();

        public async Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
        {
            var path = Uri.UnescapeDataString(relativePath);
            Asked.Enqueue(path);
            var now = Interlocked.Increment(ref _inFlight);
            int seen;
            while (now > (seen = Volatile.Read(ref _maxInFlight)) && Interlocked.CompareExchange(ref _maxInFlight, now, seen) != seen)
            {
            }

            try
            {
                var record = RecordOf(path);
                var delay = DelayFor.TryGetValue(record, out var own) ? own : DelayAll;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken);
                if (ThrowOn?.Invoke(path) is { } fault)
                    throw fault;
                return Answer(path, record);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        /// <summary>The record a read is about (the first id after the caller's own), or <see cref="Guid.Empty"/>.</summary>
        private static Guid RecordOf(string path) =>
            RecordId().Matches(path).Select(m => Guid.Parse(m.Value)).FirstOrDefault(id => id != Me);

        private DataverseUserResponse Answer(string path, Guid record)
        {
            if (path.StartsWith($"EntityDefinitions(LogicalName='{Table}')?$select=LogicalName,Privileges", StringComparison.Ordinal))
            {
                return MetadataFailure ?? Ok(new
                {
                    LogicalName = Table,
                    Privileges = new[]
                    {
                        new { PrivilegeType = "Create", Name = $"prvCreate{Table}" },
                        new { PrivilegeType = "Append", Name = $"prvAppend{Table}" },
                    },
                });
            }

            if (path.Contains("RetrieveUserSetOfPrivilegesByNames", StringComparison.Ordinal))
                return Ok(new { RolePrivileges = Held.Select(name => new { PrivilegeName = name, Depth = "Global" }).ToArray() });

            if (path.StartsWith($"EntityDefinitions(LogicalName='{Table}')/Attributes", StringComparison.Ordinal))
            {
                return Ok(new
                {
                    value = AttributeNamed().Matches(path)
                        .Select(m => new { LogicalName = m.Groups["c"].Value, IsSecured = Secured.Contains(m.Groups["c"].Value) })
                        .ToArray(),
                });
            }

            if (path.Contains("RetrievePrincipalAccess", StringComparison.Ordinal))
            {
                return !Existing.Contains(record)
                    ? DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "not found")
                    : Ok(new { AccessRights = Appendable.Contains(record) ? "ReadAccess, AppendToAccess" : "ReadAccess" });
            }

            if (path.StartsWith("EntityDefinitions(LogicalName='contact')", StringComparison.Ordinal))
            {
                return Ok(new
                {
                    LogicalName = "contact",
                    OwnershipType = "UserOwned",
                    PrimaryIdAttribute = "contactid",
                    Privileges = new[] { new { PrivilegeType = "AppendTo", Name = "prvAppendTocontact" } },
                });
            }

            // Anything else (a record type's catalog row, among others) is not the caller's to read.
            return DataverseUserResponse.Fail(404, DataverseUserClientErrorCodes.NotFound, "not found");
        }

        private static DataverseUserResponse Ok(object body) => DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(body));

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the checks never write");

        public Task<DataverseUserResponse> PostAsync(
            string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the checks never write");

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken) =>
            Task.FromResult(DataverseUserResponse.Ok(204, body: null));

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the checks never write");
    }
}
