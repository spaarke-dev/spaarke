// unified-access-control-r2 task 190 (D7 T7b, #2126) — the email-archive → communication link backfill / safety-net sweep.
// The PRODUCTION job runs; the module boundary is a double: the Dataverse reads/writes (IGenericEntityService, an in-memory
// table store that honours the query's cursor, filters and bound, compares strings IGNORING CASE as Dataverse does, and
// applies a row-version conditional update as Dataverse does).

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Communication;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

public class EmailArchiveCommunicationLinkSweepJobTests
{
    private const string Rfc = "<msg-1@example.test>";

    private readonly Mock<IGenericEntityService> _entity = new();
    private readonly List<DataverseEntity> _documents = [];
    private readonly List<DataverseEntity> _communications = [];
    private readonly List<QueryExpression> _queries = [];
    private readonly List<(Guid Id, string Version, Dictionary<string, object> Fields)> _writes = [];
    private readonly Dictionary<string, string?> _settings = new();

    public EmailArchiveCommunicationLinkSweepJobTests()
    {
        _entity
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                _queries.Add(q);
                var isChildRead = q.EntityName == "sprk_document" && q.Criteria.Conditions.Any(c => c.Operator == ConditionOperator.In);
                if (FailCandidateRead && q.EntityName == "sprk_document" && !isChildRead)
                    throw new InvalidOperationException("simulated candidate read fault");

                var source = q.EntityName == "sprk_communication" ? _communications : _documents;
                return new EntityCollection(
                    source.Where(r => Matches(r, q.Criteria)).OrderBy(r => r.Id).Take(q.TopCount ?? int.MaxValue)
                        .Select(r => Copy(r, q.ColumnSet)).ToList());
            });

        // Dataverse's conditional update: applied only while the row's version is still the one given, else
        // ConcurrencyVersionMismatch (DataverseServiceClientImpl maps it to DBConcurrencyException).
        _entity
            .Setup(e => e.UpdateIfRowVersionMatchesAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string version, Dictionary<string, object> fields, CancellationToken _) =>
            {
                _writes.Add((id, version, fields));
                if (FailWriteFor == id)
                    throw new InvalidOperationException("simulated write fault");
                var row = _documents.Single(d => d.Id == id);
                if (row.GetAttributeValue<long>("versionnumber").ToString() != version)
                    throw new System.Data.DBConcurrencyException("changed since read");
                foreach (var (key, value) in fields)
                    row[key] = value;
                row["versionnumber"] = row.GetAttributeValue<long>("versionnumber") + 1;
                return Task.CompletedTask;
            });
    }

    private bool FailCandidateRead { get; set; }

    private Guid? FailWriteFor { get; set; }

    /// <summary>
    /// A detached copy, as a read returns: a later write to the store does not change what the job read. Only the columns
    /// the query asked for come back (as Dataverse returns them), so a query that forgets a column it reads fails its test.
    /// </summary>
    private static DataverseEntity Copy(DataverseEntity row, ColumnSet columns)
    {
        var copy = new DataverseEntity(row.LogicalName) { Id = row.Id };
        foreach (var attribute in row.Attributes)
        {
            if (columns.AllColumns || columns.Columns.Contains(attribute.Key))
                copy[attribute.Key] = attribute.Value;
        }

        return copy;
    }

    /// <summary>
    /// Evaluates the query's filter — only what the job's queries use: Equal (strings IGNORING CASE, as Dataverse does),
    /// GreaterThan (the id cursor), In (parent ids) and Null, under AND. An operator it does not know FAILS the test.
    /// </summary>
    private static bool Matches(DataverseEntity row, FilterExpression filter)
    {
        var results = filter.Conditions.Select(c => Matches(row, c)).Concat(filter.Filters.Select(f => Matches(row, f))).ToList();
        return filter.FilterOperator == LogicalOperator.Or ? results.Any(r => r) : results.All(r => r);
    }

    private static bool Matches(DataverseEntity row, ConditionExpression c)
    {
        var value = c.AttributeName is "sprk_documentid" or "sprk_communicationid" ? (object)row.Id
            : row.Contains(c.AttributeName) ? row[c.AttributeName] : null;
        if (value is EntityReference er)
            value = er.Id;

        return c.Operator switch
        {
            ConditionOperator.Equal when value is string s => string.Equals(s, c.Values[0] as string, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.Equal => Equals(value, c.Values[0]),
            ConditionOperator.GreaterThan => value is Guid g && g.CompareTo((Guid)c.Values[0]) > 0,
            ConditionOperator.In => value is Guid id && c.Values.Cast<Guid>().Contains(id),
            ConditionOperator.Null => value is null,
            _ => throw new NotSupportedException($"The test store does not evaluate {c.Operator} (on {c.AttributeName})."),
        };
    }

    // ── builders ──────────────────────────────────────────────────────────────────────────────────────────────────

    private static Guid Id(int n) => Guid.Parse($"19000000-0000-4000-9000-{n:D12}");

    /// <summary>An archive document id 1xx with key <paramref name="key"/>, unlinked unless <paramref name="linkedTo"/>.</summary>
    private DataverseEntity AddArchive(int n, string? key, Guid? linkedTo = null)
    {
        var document = new DataverseEntity("sprk_document") { Id = Id(100 + n) };
        document["sprk_isemailarchive"] = true;
        document["versionnumber"] = 1000L + n;
        if (key is not null)
            document["sprk_emailmessageid"] = key;
        if (linkedTo is { } c)
            document["sprk_relatedcommunication"] = new EntityReference("sprk_communication", c);
        _documents.Add(document);
        return document;
    }

    /// <summary>A child document id 5xx under archive 1<paramref name="parentN"/>.</summary>
    private void AddChild(int n, int parentN)
    {
        var child = new DataverseEntity("sprk_document") { Id = Id(500 + n) };
        child["sprk_isemailarchive"] = false;
        child["versionnumber"] = 5000L + n;
        child["sprk_parentdocument"] = new EntityReference("sprk_document", Id(100 + parentN));
        _documents.Add(child);
    }

    /// <summary>A communication id 3xx with internet message id <paramref name="key"/>.</summary>
    private DataverseEntity AddCommunication(int n, string? key)
    {
        var communication = new DataverseEntity("sprk_communication") { Id = Id(300 + n) };
        if (key is not null)
            communication["sprk_internetmessageid"] = key;
        _communications.Add(communication);
        return communication;
    }

    private Guid? LinkOf(int archiveN) =>
        _documents.Single(d => d.Id == Id(100 + archiveN)).GetAttributeValue<EntityReference>("sprk_relatedcommunication")?.Id;

    private async Task<JobRunResult> RunAsync(bool? apply = false, int? maxDocuments = null, int pageSize = 100,
        EmailArchiveCommunicationLinkSweepJob? job = null)
    {
        job ??= NewJob(apply, maxDocuments, pageSize);
        return await job.ExecuteAsync(
            new JobRunContext(Guid.NewGuid(), "corr-190", JobRunTrigger.ManualAdmin, new Dictionary<string, object>()),
            CancellationToken.None);
    }

    private EmailArchiveCommunicationLinkSweepJob NewJob(bool? apply = false, int? maxDocuments = null, int pageSize = 100)
    {
        if (apply is { } a) _settings[EmailArchiveCommunicationLinkSweepJob.ApplyKey] = a ? "true" : "false";
        if (maxDocuments is { } m) _settings[EmailArchiveCommunicationLinkSweepJob.MaxDocumentsPerRunKey] = m.ToString();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(_settings).Build();

        var services = new ServiceCollection();
        services.AddScoped(_ => _entity.Object);
        var provider = services.BuildServiceProvider();

        return new EmailArchiveCommunicationLinkSweepJob(
            provider.GetRequiredService<IServiceScopeFactory>(), configuration, new FakeTimeProvider(),
            NullLogger<EmailArchiveCommunicationLinkSweepJob>.Instance)
        { ScanPageSize = pageSize };
    }

    private static JsonElement Archives(JobRunResult result)
    {
        using var doc = JsonDocument.Parse(result.ResultJson!);
        return doc.RootElement.Clone().GetProperty("archives");
    }

    private static Guid[] Listed(JsonElement archives, string reason) =>
        archives.GetProperty("unmatched").GetProperty(reason).GetProperty("documentIds").EnumerateArray().Select(e => e.GetGuid()).ToArray();

    /// <summary>
    /// Exhaustive (F3): the job made query reads and NOTHING else on the Dataverse seam — no write of any shape (conditional,
    /// plain, bulk, create, delete, associate…), because every call that is not a verified read fails VerifyNoOtherCalls.
    /// </summary>
    private void VerifyNoWrite()
    {
        _entity.Verify(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _entity.VerifyNoOtherCalls();
    }

    // ── The match ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Apply_AnExactSingleMatch_IsLinked_ConditionallyOnTheVersionRead_AndNothingElseIsWritten()
    {
        AddArchive(1, Rfc);
        var communication = AddCommunication(1, Rfc);

        var result = await RunAsync(apply: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        LinkOf(1).Should().Be(communication.Id);
        var write = _writes.Should().ContainSingle().Subject;
        write.Id.Should().Be(Id(101));
        write.Version.Should().Be("1001", "the write is conditional on the version read with the candidate");
        write.Fields.Keys.Should().Equal(new[] { "sprk_relatedcommunication" }, "the link only — never sprk_accesspermission");
        write.Fields["sprk_relatedcommunication"].Should().BeOfType<EntityReference>()
            .Which.Should().Match<EntityReference>(r => r.LogicalName == "sprk_communication" && r.Id == communication.Id);

        var archives = Archives(result);
        archives.GetProperty("linked").GetInt32().Should().Be(1);
        archives.GetProperty("wouldLink").GetInt32().Should().Be(0);
        var listed = archives.GetProperty("linkedDocuments").EnumerateArray().Should().ContainSingle().Subject;
        listed.GetProperty("documentId").GetGuid().Should().Be(Id(101));
        listed.GetProperty("communicationId").GetGuid().Should().Be(communication.Id);
        _entity.Verify(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never,
            "never an unconditional write");
    }

    [Theory]
    [InlineData(null)]       // no setting at all
    [InlineData("false")]
    [InlineData("nonsense")]
    [InlineData("")]
    public async Task DryRun_IsTheDefault_CountsAnExactMatchAsWouldLink_AndWritesNothing(string? setting)
    {
        AddArchive(1, Rfc);
        AddCommunication(1, Rfc);
        if (setting is not null)
            _settings[EmailArchiveCommunicationLinkSweepJob.ApplyKey] = setting;

        var result = await RunAsync(apply: null);

        result.Success.Should().BeTrue(result.ErrorMessage);
        using var doc = JsonDocument.Parse(result.ResultJson!);
        doc.RootElement.GetProperty("mode").GetString().Should().Be("dry-run");
        var archives = Archives(result);
        archives.GetProperty("wouldLink").GetInt32().Should().Be(1);
        archives.GetProperty("linked").GetInt32().Should().Be(0);
        archives.GetProperty("linkedDocuments").GetArrayLength().Should().Be(1, "the dry run lists what it would link");
        LinkOf(1).Should().BeNull();
        VerifyNoWrite();
    }

    [Fact]
    public async Task ZeroMatches_NeverLink_AndAreReportedAsNoCommunication()
    {
        AddArchive(1, Rfc);
        AddCommunication(1, "<another@example.test>");

        var result = await RunAsync(apply: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var archives = Archives(result);
        archives.GetProperty("noCommunication").GetInt32().Should().Be(1);
        Listed(archives, "noCommunication").Should().Equal(Id(101));
        LinkOf(1).Should().BeNull();
        VerifyNoWrite();
    }

    [Fact]
    public async Task TwoMatches_NeverLink_AndAreReportedAsAmbiguous()
    {
        AddArchive(1, Rfc);
        AddCommunication(1, Rfc);
        AddCommunication(2, Rfc);

        var result = await RunAsync(apply: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var archives = Archives(result);
        archives.GetProperty("ambiguous").GetInt32().Should().Be(1);
        archives.GetProperty("linked").GetInt32().Should().Be(0);
        Listed(archives, "ambiguous").Should().Equal(Id(101));
        LinkOf(1).Should().BeNull();
        VerifyNoWrite();
    }

    [Fact]
    public async Task ASecondRowDifferingOnlyByCase_MakesTheKeyAmbiguous_NotLinked_EvenThoughOneRowIsAnExactMatch()
    {
        AddArchive(1, Rfc);
        AddCommunication(1, Rfc);
        AddCommunication(2, Rfc.ToUpperInvariant()); // Dataverse returns both: its equality ignores case

        var result = await RunAsync(apply: true);

        Archives(result).GetProperty("ambiguous").GetInt32().Should().Be(1);
        LinkOf(1).Should().BeNull();
        VerifyNoWrite();
    }

    [Fact]
    public async Task OneRowThatMatchesOnlyIgnoringCase_IsNoCommunication_NotLinked()
    {
        AddArchive(1, Rfc);
        AddCommunication(1, Rfc.ToUpperInvariant()); // the query returns it; an ordinal comparison does not accept it

        var result = await RunAsync(apply: true);

        var archives = Archives(result);
        archives.GetProperty("noCommunication").GetInt32().Should().Be(1);
        archives.GetProperty("ambiguous").GetInt32().Should().Be(0);
        LinkOf(1).Should().BeNull();
        VerifyNoWrite();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankKey_IsNoKey_AndNoCommunicationQueryIsMade(string? key)
    {
        AddArchive(1, key);
        AddCommunication(1, Rfc);

        var result = await RunAsync(apply: true);

        var archives = Archives(result);
        archives.GetProperty("noKey").GetInt32().Should().Be(1);
        Listed(archives, "noKey").Should().Equal(Id(101));
        _queries.Should().NotContain(q => q.EntityName == "sprk_communication");
        VerifyNoWrite();
    }

    [Theory]
    [InlineData("AAMkAGI2TG93AAA=")]
    [InlineData("AQMkADAwATM0MDAAMS1iZWE=")]
    [InlineData("AAMkAGI2@weird")] // an AAMk prefix is an item id even with an @
    public async Task AnExchangeItemIdKey_IsExchangeItemId_AndNoCommunicationQueryIsMade(string key)
    {
        AddArchive(1, key);
        AddCommunication(1, key); // even a communication carrying the same text is never queried for, nor linked

        var result = await RunAsync(apply: true);

        var archives = Archives(result);
        archives.GetProperty("exchangeItemId").GetInt32().Should().Be(1);
        Listed(archives, "exchangeItemId").Should().Equal(Id(101));
        _queries.Should().NotContain(q => q.EntityName == "sprk_communication");
        LinkOf(1).Should().BeNull();
        VerifyNoWrite();
    }

    [Fact]
    public async Task AnArchiveWithAParent_IsHasParent_AndIsNeverLinked_EvenOnAnExactMatch()
    {
        // K1 must fire: D7 takes the FIRST communication link on the walk, so an own link could pre-empt the parent's.
        AddArchive(9, "<parent@example.test>");
        var withParent = AddArchive(1, Rfc);
        withParent["sprk_parentdocument"] = new EntityReference("sprk_document", Id(109));
        AddCommunication(1, Rfc);

        var result = await RunAsync(apply: true);

        var archives = Archives(result);
        archives.GetProperty("hasParent").GetInt32().Should().Be(1);
        Listed(archives, "hasParent").Should().Equal(Id(101));
        LinkOf(1).Should().BeNull();
        _writes.Should().BeEmpty();
        _queries.Should().NotContain(q => q.EntityName == "sprk_communication"
            && q.Criteria.Conditions.Any(c => Equals(c.Values[0], Rfc)), "decided before the key: no communication query");
    }

    [Fact]
    public async Task TheSameArchiveWithoutAParent_IsLinked()
    {
        // K1 must not fire: the control for the test above.
        AddArchive(1, Rfc);
        AddCommunication(1, Rfc);

        var archives = Archives(await RunAsync(apply: true));

        archives.GetProperty("hasParent").GetInt32().Should().Be(0);
        archives.GetProperty("linked").GetInt32().Should().Be(1);
        LinkOf(1).Should().Be(Id(301));
    }

    [Fact]
    public async Task TheBatch_IsClampedToTheListCap_SoEveryListIsComplete_AndTheReportSaysSo()
    {
        var over = EmailArchiveCommunicationLinkSweepJob.MaxListedPerRun + 5;
        for (var n = 1; n <= over; n++)
            AddArchive(n, null);

        var result = await RunAsync(apply: false, maxDocuments: 1000);

        using var doc = JsonDocument.Parse(result.ResultJson!);
        var batch = doc.RootElement.GetProperty("batch");
        batch.GetProperty("configured").GetInt32().Should().Be(1000);
        batch.GetProperty("effective").GetInt32().Should().Be(EmailArchiveCommunicationLinkSweepJob.MaxListedPerRun);
        batch.GetProperty("clamped").GetBoolean().Should().BeTrue();
        var archives = Archives(result);
        archives.GetProperty("examined").GetInt32().Should().Be(EmailArchiveCommunicationLinkSweepJob.MaxListedPerRun);
        var noKey = archives.GetProperty("unmatched").GetProperty("noKey");
        noKey.GetProperty("listed").GetInt32().Should().Be(noKey.GetProperty("total").GetInt32(), "the list is complete");
    }

    [Fact]
    public async Task TheDefaultBatch_IsTheListCap_NotClamped()
    {
        AddArchive(1, null);

        var result = await RunAsync(apply: false);

        using var doc = JsonDocument.Parse(result.ResultJson!);
        var batch = doc.RootElement.GetProperty("batch");
        batch.GetProperty("effective").GetInt32().Should().Be(EmailArchiveCommunicationLinkSweepJob.MaxListedPerRun);
        batch.GetProperty("configured").GetInt32().Should().Be(EmailArchiveCommunicationLinkSweepJob.MaxListedPerRun);
        batch.GetProperty("clamped").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task AMatchedRowWhoseCommunicationAlreadyHasAnArchive_StillLinks_AndIsCounted()
    {
        // F1: an attribute of matched rows, not a reason. Archive 9 is already linked to communication 1 (any key).
        AddArchive(9, null, linkedTo: Id(301));
        AddArchive(1, Rfc);
        AddCommunication(1, Rfc);
        AddArchive(2, "<m2@example.test>");                  // matched, its communication has no archive yet
        AddCommunication(2, "<m2@example.test>");

        var result = await RunAsync(apply: true);

        result.Success.Should().BeTrue(result.ErrorMessage);
        var archives = Archives(result);
        archives.GetProperty("linked").GetInt32().Should().Be(2, "both still link");
        LinkOf(1).Should().Be(Id(301));
        LinkOf(2).Should().Be(Id(302));
        var already = archives.GetProperty("communicationAlreadyArchived");
        already.GetProperty("total").GetInt32().Should().Be(1);
        already.GetProperty("documentIds").EnumerateArray().Select(e => e.GetGuid()).Should().Equal(Id(101));
        archives.GetProperty("alreadyArchivedCheckFailed").GetBoolean().Should().BeFalse();
        _queries.Count(q => q.EntityName == "sprk_document" && q.Criteria.Conditions.Any(
            c => c.AttributeName == "sprk_relatedcommunication" && c.Operator == ConditionOperator.In))
            .Should().Be(1, "one read for the page, not one per row");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    public async Task AnUnusableBatchSetting_FallsBackToTheDefault_AndTheRunProgresses(string setting)
    {
        AddArchive(1, null);
        AddArchive(2, null);
        _settings[EmailArchiveCommunicationLinkSweepJob.MaxDocumentsPerRunKey] = setting;

        var result = await RunAsync(apply: false);

        using var doc = JsonDocument.Parse(result.ResultJson!);
        doc.RootElement.GetProperty("batch").GetProperty("effective").GetInt32().Should().Be(EmailArchiveCommunicationLinkSweepJob.MaxListedPerRun);
        Archives(result).GetProperty("examined").GetInt32().Should().Be(2);
        Archives(result).GetProperty("passComplete").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ARowWithNoRowVersion_IsCountedAsFailed_AndNeverWrittenUnconditionally()
    {
        var archive = AddArchive(1, Rfc);
        archive.Attributes.Remove("versionnumber");
        AddCommunication(1, Rfc);

        var result = await RunAsync(apply: true);

        result.Success.Should().BeFalse();
        var archives = Archives(result);
        archives.GetProperty("failed").GetInt32().Should().Be(1);
        archives.GetProperty("linked").GetInt32().Should().Be(0);
        LinkOf(1).Should().BeNull();
        VerifyNoWrite();
    }

    // ── Candidates ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AlreadyLinkedArchives_AndNonArchives_AreNotCandidates_AndAreNeverRepointed()
    {
        var other = Guid.NewGuid();
        AddArchive(1, Rfc, linkedTo: other);        // linked elsewhere: never re-pointed
        AddCommunication(1, Rfc);
        var plain = new DataverseEntity("sprk_document") { Id = Id(201) };
        plain["sprk_isemailarchive"] = false;
        plain["sprk_emailmessageid"] = Rfc;
        plain["versionnumber"] = 1L;
        _documents.Add(plain);

        var result = await RunAsync(apply: true);

        Archives(result).GetProperty("examined").GetInt32().Should().Be(0);
        LinkOf(1).Should().Be(other);
        VerifyNoWrite();
    }

    [Fact]
    public void TheCandidateQuery_IsUnlinkedArchivesOfAnyState_AfterTheCursor_InIdOrder_Bounded_WithTheRowVersion()
    {
        var after = Id(150);

        var query = EmailArchiveCommunicationLinkSweepJob.BuildCandidateQuery(after, 77);

        query.EntityName.Should().Be("sprk_document");
        query.TopCount.Should().Be(77);
        query.Criteria.Conditions.Should().Contain(c => c.AttributeName == "sprk_isemailarchive" && c.Operator == ConditionOperator.Equal && Equals(c.Values[0], true));
        query.Criteria.Conditions.Should().Contain(c => c.AttributeName == "sprk_relatedcommunication" && c.Operator == ConditionOperator.Null);
        query.Criteria.Conditions.Should().Contain(c => c.AttributeName == "sprk_documentid" && c.Operator == ConditionOperator.GreaterThan && Equals(c.Values[0], after));
        query.Criteria.Conditions.Should().NotContain(c => c.AttributeName == "statecode", "active or inactive");
        query.ColumnSet.Columns.Should().Contain(new[] { "sprk_emailmessageid", "sprk_parentdocument", "versionnumber" });
        query.Orders.Should().ContainSingle(o => o.AttributeName == "sprk_documentid" && o.OrderType == OrderType.Ascending);
    }

    [Fact]
    public void TheCommunicationQuery_IsBounded_SoMoreThanOneCanBeSeen()
    {
        var query = EmailArchiveCommunicationLinkSweepJob.BuildCommunicationQuery(Rfc);

        query.EntityName.Should().Be("sprk_communication");
        query.TopCount.Should().BeGreaterThan(1);
        query.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "sprk_internetmessageid" && c.Operator == ConditionOperator.Equal);
    }

    // ── Concurrency and idempotency ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ARowChangedSinceItWasRead_IsChangedSinceRead_AndTheLinkSetMeanwhileIsNotOverwritten()
    {
        AddArchive(1, Rfc);
        AddCommunication(1, Rfc);
        var meanwhile = Guid.NewGuid();
        // Between the candidate read and the write, the capture side links the document elsewhere (its version moves on).
        _entity
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_communication"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                var row = _documents.Single(d => d.Id == Id(101));
                row["sprk_relatedcommunication"] = new EntityReference("sprk_communication", meanwhile);
                row["versionnumber"] = 9999L;
                return new EntityCollection(_communications.Where(c => Matches(c, q.Criteria)).Select(c => Copy(c, q.ColumnSet)).ToList());
            });

        var result = await RunAsync(apply: true);

        result.Success.Should().BeFalse("a row left for the next pass is not ok");
        using var doc = JsonDocument.Parse(result.ResultJson!);
        doc.RootElement.GetProperty("status").GetString().Should().Be(EmailArchiveCommunicationLinkSweepJob.StatusPartial);
        var archives = Archives(result);
        archives.GetProperty("changedSinceRead").GetInt32().Should().Be(1);
        archives.GetProperty("linked").GetInt32().Should().Be(0);
        LinkOf(1).Should().Be(meanwhile, "the link someone else set stands");
    }

    [Fact]
    public async Task ASecondRun_LinksNothingNew_AndReportsTheSameUnmatchedSet()
    {
        AddArchive(1, Rfc);
        AddCommunication(1, Rfc);
        AddArchive(2, null);
        AddArchive(3, "AAMkAGI2TG93AAA=");
        AddArchive(4, "<none@example.test>");
        AddArchive(5, "<two@example.test>");
        AddCommunication(5, "<two@example.test>");
        AddCommunication(6, "<TWO@example.test>");
        var job = NewJob(apply: true);

        var first = Archives(await RunAsync(job: job));
        var second = Archives(await RunAsync(job: job));

        first.GetProperty("linked").GetInt32().Should().Be(1);
        second.GetProperty("linked").GetInt32().Should().Be(0, "a linked archive is no longer a candidate");
        second.GetProperty("examined").GetInt32().Should().Be(4);
        _writes.Should().ContainSingle("the second run writes nothing");
        foreach (var reason in new[] { "noKey", "exchangeItemId", "noCommunication", "ambiguous" })
            Listed(second, reason).Should().Equal(Listed(first, reason), reason);
        Listed(second, "noKey").Should().Equal(Id(102));
        Listed(second, "exchangeItemId").Should().Equal(Id(103));
        Listed(second, "noCommunication").Should().Equal(Id(104));
        Listed(second, "ambiguous").Should().Equal(Id(105));
    }

    // ── Children ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheReport_CountsTheChildrenTheLinkCovers_AndTheChildrenOfUnmatchedArchives()
    {
        AddArchive(1, Rfc);
        AddCommunication(1, Rfc);
        AddChild(1, parentN: 1);
        AddChild(2, parentN: 1);
        AddArchive(2, null);
        AddChild(3, parentN: 2);
        AddArchive(3, "AAMkAGI2TG93AAA=");
        AddChild(4, parentN: 3);
        AddChild(5, parentN: 3);
        AddChild(6, parentN: 3);
        var linkedElsewhere = AddArchive(4, Rfc, linkedTo: Guid.NewGuid()); // not a candidate: its children are not counted
        AddChild(7, parentN: 4);

        var archives = Archives(await RunAsync(apply: false));

        archives.GetProperty("childrenCovered").GetInt32().Should().Be(2);
        archives.GetProperty("childrenOfUnmatched").GetInt32().Should().Be(4);
        archives.GetProperty("childCountFailed").GetBoolean().Should().BeFalse();
        linkedElsewhere.Should().NotBeNull();
        VerifyNoWrite();
    }

    [Fact]
    public void TheChildQuery_ReadsTheParentColumn_ForTheGivenParents()
    {
        var query = EmailArchiveCommunicationLinkSweepJob.BuildChildQuery(new[] { Id(101), Id(102), Id(101) });

        query.EntityName.Should().Be("sprk_document");
        query.ColumnSet.Columns.Should().Contain("sprk_parentdocument");
        var parents = query.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "sprk_parentdocument" && c.Operator == ConditionOperator.In).Subject;
        parents.Values.Cast<Guid>().Should().BeEquivalentTo(new[] { Id(101), Id(102) });
    }

    // ── Paging ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheCap_IsHonoured_TheNextRunContinuesAfterTheLastRow_AndAPassThatEndsStartsOver()
    {
        for (var n = 1; n <= 5; n++)
            AddArchive(n, null); // five unmatched candidates (they stay candidates)
        var job = NewJob(apply: true, maxDocuments: 3, pageSize: 2);

        var first = Archives(await RunAsync(job: job));
        first.GetProperty("examined").GetInt32().Should().Be(3, "the cap");
        first.GetProperty("passComplete").GetBoolean().Should().BeFalse();

        var second = Archives(await RunAsync(job: job));
        second.GetProperty("startAfter").GetGuid().Should().Be(Id(103), "it continues after the last row of the first run");
        second.GetProperty("examined").GetInt32().Should().Be(2);
        second.GetProperty("passComplete").GetBoolean().Should().BeTrue();

        var third = Archives(await RunAsync(job: job));
        third.GetProperty("startAfter").ValueKind.Should().Be(JsonValueKind.Null, "a finished pass starts at the top");
        third.GetProperty("examined").GetInt32().Should().Be(3);
    }

    // ── Failures ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ACandidateReadThatFaults_IsAFailedRun_NeverNothingToDo()
    {
        AddArchive(1, Rfc);
        AddCommunication(1, Rfc);
        FailCandidateRead = true;

        var result = await RunAsync(apply: true);

        result.Success.Should().BeFalse();
        using var doc = JsonDocument.Parse(result.ResultJson!);
        doc.RootElement.GetProperty("status").GetString().Should().Be(EmailArchiveCommunicationLinkSweepJob.StatusError);
        Archives(result).GetProperty("enumerationFailed").GetBoolean().Should().BeTrue();
        result.ErrorMessage.Should().Contain("could not be read");
        VerifyNoWrite();
    }

    [Fact]
    public async Task AWriteThatFaultsOnOneRow_IsCounted_AndTheRunContinues()
    {
        for (var n = 1; n <= 3; n++)
        {
            AddArchive(n, $"<m{n}@example.test>");
            AddCommunication(n, $"<m{n}@example.test>");
        }
        FailWriteFor = Id(102);

        var result = await RunAsync(apply: true);

        result.Success.Should().BeFalse();
        using var doc = JsonDocument.Parse(result.ResultJson!);
        doc.RootElement.GetProperty("status").GetString().Should().Be(EmailArchiveCommunicationLinkSweepJob.StatusPartial);
        var archives = Archives(result);
        archives.GetProperty("failed").GetInt32().Should().Be(1);
        archives.GetProperty("linked").GetInt32().Should().Be(2);
        LinkOf(1).Should().Be(Id(301));
        LinkOf(2).Should().BeNull();
        LinkOf(3).Should().Be(Id(303), "the run went on past the failure");
    }

    [Fact]
    public async Task ACommunicationLookupThatFaults_IsCounted_NotLinked_AndTheRunContinues()
    {
        AddArchive(1, Rfc);
        AddArchive(2, "<m2@example.test>");
        AddCommunication(2, "<m2@example.test>");
        _entity
            .Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communication"
                    && q.Criteria.Conditions.Any(c => Equals(c.Values[0], Rfc))), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated lookup fault"));

        var result = await RunAsync(apply: true);

        result.Success.Should().BeFalse();
        var archives = Archives(result);
        archives.GetProperty("failed").GetInt32().Should().Be(1);
        archives.GetProperty("linked").GetInt32().Should().Be(1);
        LinkOf(1).Should().BeNull();
        LinkOf(2).Should().Be(Id(302));
    }

    [Fact]
    public void Classify_IsExactlyOneReasonPerKey()
    {
        DataverseEntity C(int n, string key) { var c = new DataverseEntity("sprk_communication") { Id = Id(n) }; c["sprk_internetmessageid"] = key; return c; }

        EmailArchiveCommunicationLinkSweepJob.Classify(Rfc, [C(1, Rfc)]).Should().Be((EmailArchiveCommunicationLinkSweepJob.LinkReason.Matched, Id(1)));
        EmailArchiveCommunicationLinkSweepJob.Classify(Rfc, []).Reason.Should().Be(EmailArchiveCommunicationLinkSweepJob.LinkReason.NoCommunication);
        EmailArchiveCommunicationLinkSweepJob.Classify(Rfc, [C(1, Rfc.ToUpperInvariant())]).Reason.Should().Be(EmailArchiveCommunicationLinkSweepJob.LinkReason.NoCommunication);
        EmailArchiveCommunicationLinkSweepJob.Classify(Rfc, [C(1, Rfc), C(2, Rfc)]).Reason.Should().Be(EmailArchiveCommunicationLinkSweepJob.LinkReason.Ambiguous);
        EmailArchiveCommunicationLinkSweepJob.Classify(" ", [C(1, Rfc)]).Reason.Should().Be(EmailArchiveCommunicationLinkSweepJob.LinkReason.NoKey);
        EmailArchiveCommunicationLinkSweepJob.Classify("AAMkAGI2", [C(1, "AAMkAGI2")]).Reason.Should().Be(EmailArchiveCommunicationLinkSweepJob.LinkReason.ExchangeItemId);
    }

    // ── Registration ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheJob_IsRegisteredThroughAddScheduledJob_Disabled_OnTheDailyCron()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCommunicationModule(configuration);
        using var provider = services.BuildServiceProvider();

        var registration = provider.GetServices<ScheduledJobRegistration>()
            .Single(r => r.Job.JobId == EmailArchiveCommunicationLinkSweepJob.JobIdConstant);

        registration.Enabled.Should().BeFalse("it runs only when an admin triggers or enables it");
        registration.CronSchedule.Should().Be("45 3 * * *");
        registration.Job.Should().BeOfType<EmailArchiveCommunicationLinkSweepJob>();
    }
}
