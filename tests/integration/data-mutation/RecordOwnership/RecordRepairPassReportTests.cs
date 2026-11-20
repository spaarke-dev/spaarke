using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 052d (task 044(d); ontology D-204, D-210; 052b K6, K8): the repair pass's report. For
/// Signal and Decision Record rows created at or after go-live it reports a row holding one half of the core pair only and an
/// update of either pair column read from the column audit history, writing nothing to either table; and it reports each swept
/// table's retry-set size and the ids abandoned in the run. Same harness as <see cref="RecordRepairPassJobTests"/>: the real job and
/// report over a doubled Dataverse boundary (<see cref="PassWorld"/>'s report rows and audit rows).
/// </summary>
public sealed partial class RecordRepairPassJobTests
{
    private static readonly string[] ReadOnlyCalls =
        [nameof(IGenericEntityService.GetCurrentIdentityIdAsync), nameof(IGenericEntityService.RetrieveMultipleAsync),
            nameof(IGenericEntityService.GetColumnMetadataAsync), nameof(IGenericEntityService.GetTableAuditMetadataAsync)];

    // =====================================================================================
    // AC1: half pair
    // =====================================================================================

    [Fact]
    public async Task Report_AHalfPair_IsReported_EitherHalf_AndAWholePairOrNeitherHalf_IsNot()
    {
        var typeOnly = _pass.CorePairRow("sprk_signal", withType: true, withId: false);
        var idOnly = _pass.CorePairRow("sprk_signal", withType: false, withId: true);
        _pass.CorePairRow("sprk_signal", withType: true, withId: true);
        _pass.CorePairRow("sprk_signal", withType: false, withId: false);
        var recordTypeOnly = _pass.CorePairRow("sprk_decisionrecord", withType: true, withId: false);
        _pass.CorePairRow("sprk_decisionrecord", withType: true, withId: true);

        var result = await _pass.RunAsync();

        result.Success.Should().BeTrue("a finding is reported, not a failure of the pass: " + result.ErrorMessage);
        PassWorld.Count(result, "corePairHalf").Should().Be(3);
        PassWorld.Findings(result).Should().BeEquivalentTo(new[]
        {
            ("sprk_signal", typeOnly, RecordRepairCorePairReport.FindingHalf, (string?)null, (string?)null),
            ("sprk_signal", idOnly, RecordRepairCorePairReport.FindingHalf, null, null),
            ("sprk_decisionrecord", recordTypeOnly, RecordRepairCorePairReport.FindingHalf, null, null),
        });
    }

    [Fact]
    public async Task Report_AHalfPairRowCreatedBeforeGoLive_OrAWholePair_IsNotReported_EvenWhenTheScanReturnsIt()
    {
        _pass.ReportScanIgnoresItsConditions = true;
        _pass.CorePairRow("sprk_signal", withType: true, withId: false, createdOn: PassWorld.GoLive.AddTicks(-1));
        _pass.CorePairRow("sprk_signal", withType: true, withId: true);
        _pass.CorePairRow("sprk_signal", withType: false, withId: false);

        var result = await _pass.RunAsync();

        PassWorld.Count(result, "corePairHalf").Should().Be(0, "the report asks both rules again over every row the scan returned");
        PassWorld.Findings(result).Should().BeEmpty();
    }

    [Fact]
    public async Task Report_TheQueries_AreBoundedByGoLiveAndTheSettlePoint_AndAskForUpdatesOnly()
    {
        var row = _pass.CorePairRow("sprk_signal", withType: true, withId: true);
        _pass.PairAudit("sprk_signal", row, ",31,");

        await _pass.RunAsync();

        var until = (PassWorld.Now - RecordRepairPassJob.SettleTime).UtcDateTime;
        var half = _pass.ReportQueries.First(q => q.EntityName == "sprk_signal" && q.Criteria.Filters.Count > 0);
        half.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "createdon" && c.Operator == ConditionOperator.GreaterEqual)
            .Which.Values.Single().Should().Be(PassWorld.GoLive.UtcDateTime);
        half.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "createdon" && c.Operator == ConditionOperator.LessThan)
            .Which.Values.Single().Should().Be(until);
        half.PageInfo.Count.Should().Be(RecordRepairCorePairReport.MaxRowsPerScan);

        var audit = _pass.ReportQueries.First(q => q.EntityName == "audit");
        audit.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "objecttypecode").Which.Values.Single()
            .Should().Be(PassWorld.SignalTypeCode, "Dataverse filters an audit by the table's numeric object type code, read from its metadata");
        audit.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "operation").Which.Values
            .Should().BeEquivalentTo(new object[] { RecordRepairCorePairReport.AuditOperationUpdate, RecordRepairCorePairReport.AuditOperationUpsert });
        audit.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "createdon" && c.Operator == ConditionOperator.GreaterEqual)
            .Which.Values.Single().Should().Be(PassWorld.GoLive.UtcDateTime, "the audit window starts at go-live, whenever the row was created");
        audit.Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "createdon" && c.Operator == ConditionOperator.LessThan)
            .Which.Values.Single().Should().Be(until);
        audit.PageInfo.Count.Should().Be(RecordRepairCorePairReport.MaxAuditRowsPerScan);
    }

    // =====================================================================================
    // Review fix round 1: the audit read as Dataverse takes it; auditing must be on; upserts
    // =====================================================================================

    [Fact]
    public async Task Report_TheAuditRead_FiltersByEachTablesNumericObjectTypeCode_ReadFromItsMetadata()
    {
        // The double refuses (throws) an audit query whose objecttypecode value is not an int, as Dataverse does (HTTP 400).
        var signal = _pass.CorePairRow("sprk_signal", withType: true, withId: true);
        var record = _pass.CorePairRow("sprk_decisionrecord", withType: true, withId: true);
        _pass.PairAudit("sprk_signal", signal, $",{PassWorld.TypeColumnNumber},");
        _pass.PairAudit("sprk_decisionrecord", record, $",{PassWorld.IdColumnNumber},");

        var result = await _pass.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        PassWorld.Count(result, "corePairChanged").Should().Be(2, "each table's history is found by that table's own code");
        _pass.ReportQueries.Where(q => q.EntityName == "audit")
            .Select(q => q.Criteria.Conditions.Single(c => c.AttributeName == "objecttypecode").Values.Single())
            .Should().Equal(PassWorld.SignalTypeCode, PassWorld.DecisionRecordTypeCode);
        PassWorld.SignalTypeCode.Should().NotBe(PassWorld.DecisionRecordTypeCode);
    }

    [Fact]
    public async Task Report_ATableWhoseObjectTypeCodeIsNotStated_IsAnAuditFault_NeverNoChanges()
    {
        _pass.NoObjectTypeCode = true;
        var row = _pass.CorePairRow("sprk_signal", withType: true, withId: true);
        _pass.PairAudit("sprk_signal", row, $",{PassWorld.TypeColumnNumber},");

        var result = await _pass.RunAsync();

        PassWorld.Status(result).Should().Be(RecordRepairPassJob.StatusPartial);
        PassWorld.Count(result, "reportFailures").Should().Be(2, "one per table");
        PassWorld.Count(result, "corePairChanged").Should().Be(0);
        _pass.ReportQueries.Should().NotContain(q => q.EntityName == "audit", "no history is read without the code");
        using var state = JsonDocument.Parse(_pass.ReportState("sprk_signal")!);
        state.RootElement.GetProperty("auditWatermark").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("the organization")]
    [InlineData("the table")]
    [InlineData("sprk_corerecordtype")]
    [InlineData("sprk_corerecordid")]
    [InlineData("the organization, not stated")]
    [InlineData("the table, not stated")]
    [InlineData("the column, not stated")]
    [InlineData("the type column, not stated")]
    public async Task Report_AuditingNotOn_AtTheOrganizationTheTableOrEitherPairColumn_IsAnAuditFault_NeverNoChanges(string off)
    {
        var row = _pass.CorePairRow("sprk_signal", withType: true, withId: true);
        _pass.PairAudit("sprk_signal", row, $",{PassWorld.TypeColumnNumber},");
        var halfPair = _pass.CorePairRow("sprk_signal", withType: true, withId: false);
        switch (off)
        {
            case "the organization":
                _pass.OrganizationAudit = false;
                break;
            case "the organization, not stated":
                _pass.OrganizationAudit = null;
                break;
            case "the table":
                _pass.TableAuditOff["sprk_signal"] = false;
                break;
            case "the table, not stated":
                _pass.TableAuditOff["sprk_signal"] = null;
                break;
            case "the column, not stated":
                _pass.ColumnAuditOff["sprk_signal.sprk_corerecordid"] = null;
                break;
            case "the type column, not stated":
                _pass.ColumnAuditOff["sprk_signal.sprk_corerecordtype"] = null;
                break;
            default:
                _pass.ColumnAuditOff["sprk_signal." + off] = false;
                break;
        }

        var faulted = await _pass.RunAsync();

        PassWorld.Status(faulted).Should().Be(RecordRepairPassJob.StatusPartial);
        PassWorld.Count(faulted, "reportFailures").Should().Be(off.StartsWith("the organization", StringComparison.Ordinal) ? 2 : 1,
            "an organization-wide flag faults each table once; the others fault the one table");
        PassWorld.Count(faulted, "corePairChanged").Should().Be(0, "an unrecorded history is not 'no change'");
        _pass.Log.Entries.Count(e => e.Level == LogLevel.Error && e.Message.Contains("auditing is not on"))
            .Should().Be(off.StartsWith("the organization", StringComparison.Ordinal) ? 2 : 1, "logged once per table per run");
        _pass.ReportQueries.Where(q => q.EntityName == "audit").Should().NotContain(
            q => q.Criteria.Conditions.Any(c => c.Values.Contains(PassWorld.SignalTypeCode)), "no history of the table is read while auditing is off");
        using (var state = JsonDocument.Parse(_pass.ReportState("sprk_signal")!))
            state.RootElement.GetProperty("auditWatermark").ValueKind.Should().Be(JsonValueKind.Null, "the window is not passed");
        PassWorld.Findings(faulted).Should().ContainSingle(f => f.Id == halfPair, "the half-pair scan does not depend on auditing");

        _pass.OrganizationAudit = true;
        _pass.TableAuditOff.Clear();
        _pass.ColumnAuditOff.Clear();
        _pass.Time.Advance(TimeSpan.FromMinutes(5));
        var next = await _pass.RunAsync();

        PassWorld.Count(next, "corePairChanged").Should().Be(1, "with auditing on again, the window is read from where it stood");
        next.Success.Should().BeTrue(next.ErrorMessage);
    }

    [Fact]
    public async Task Report_AuditingOff_IsAFault_EvenWhenTheWindowHoldsNoAuditRows()
    {
        _pass.OrganizationAudit = false;

        var result = await _pass.RunAsync();

        PassWorld.Status(result).Should().Be(RecordRepairPassJob.StatusPartial, "the check does not wait for an audit row to exist");
        PassWorld.Count(result, "reportFailures").Should().Be(2);
    }

    [Fact]
    public async Task Report_TheOrganizationsAuditSettingThatCannotBeRead_IsAnAuditFault_NeverAuditingOn()
    {
        _pass.OrganizationFault = new TimeoutException("Test: the read faulted.");
        var row = _pass.CorePairRow("sprk_signal", withType: true, withId: true);
        _pass.PairAudit("sprk_signal", row, $",{PassWorld.TypeColumnNumber},");

        var result = await _pass.RunAsync();

        PassWorld.Status(result).Should().Be(RecordRepairPassJob.StatusPartial);
        PassWorld.Count(result, "reportFailures").Should().Be(2);
        PassWorld.Count(result, "corePairChanged").Should().Be(0);
        _pass.ReportQueries.Should().NotContain(q => q.EntityName == "audit");
    }

    [Fact]
    public async Task Report_AnUpsertThatChangedAPairColumn_IsReported_AndAnUpsertThatCreatedTheRow_IsNot()
    {
        var created = PassWorld.Now.AddHours(-2);
        var changed = _pass.CorePairRow("sprk_signal", withType: true, withId: true, createdOn: created);
        var creation = _pass.CorePairRow("sprk_signal", withType: true, withId: true, createdOn: created);
        var creationUndecided = _pass.CorePairRow("sprk_signal", withType: true, withId: true, createdOn: created);
        _pass.PairAudit("sprk_signal", changed, $",{PassWorld.TypeColumnNumber},", at: created.AddMinutes(30), operation: 5);
        _pass.PairAudit("sprk_signal", creation, $",{PassWorld.TypeColumnNumber},{PassWorld.IdColumnNumber},", at: created, operation: 5);
        _pass.PairAudit("sprk_signal", creationUndecided, null, at: created.AddSeconds(-1), operation: 5);

        var result = await _pass.RunAsync();

        PassWorld.Findings(result).Should().ContainSingle().Which.Should().Be(
            ("sprk_signal", changed, RecordRepairCorePairReport.FindingChanged, created.AddMinutes(30).ToString("O"), PassWorld.Editor.ToString("D")));
        PassWorld.Count(result, "corePairUndecided").Should().Be(0, "an upsert that is the creation is not read at all");
        PassWorld.Count(result, "reportAuditRowsRead").Should().Be(3, "the query asks for updates and upserts");
    }

    [Fact]
    public async Task Report_AnUpdateAuditAtTheRowsCreationTime_IsStillAChange_OnlyAnUpsertIsReadAsACreation()
    {
        var created = PassWorld.Now.AddHours(-2);
        var row = _pass.CorePairRow("sprk_signal", withType: true, withId: true, createdOn: created);
        _pass.PairAudit("sprk_signal", row, $",{PassWorld.TypeColumnNumber},", at: created, operation: 2);

        var result = await _pass.RunAsync();

        PassWorld.Count(result, "corePairChanged").Should().Be(1);
    }

    // =====================================================================================
    // AC2: changed after create (column audit history)
    // =====================================================================================

    [Fact]
    public async Task Report_AnUpdateOfEitherPairColumn_IsReported_WithTheAuditTimeAndTheChangingUser()
    {
        var signal = _pass.CorePairRow("sprk_signal", withType: true, withId: true);
        var record = _pass.CorePairRow("sprk_decisionrecord", withType: true, withId: true);
        var typeChangedAt = PassWorld.Now.AddMinutes(-40);
        var idChangedAt = PassWorld.Now.AddMinutes(-20);
        _pass.PairAudit("sprk_signal", signal, $",{PassWorld.TypeColumnNumber},", at: typeChangedAt);
        _pass.PairAudit("sprk_decisionrecord", record, $",{PassWorld.OtherColumnNumber},{PassWorld.IdColumnNumber},", at: idChangedAt);

        var result = await _pass.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        PassWorld.Count(result, "corePairChanged").Should().Be(2);
        PassWorld.Findings(result).Should().BeEquivalentTo(new[]
        {
            ("sprk_signal", signal, RecordRepairCorePairReport.FindingChanged, (string?)typeChangedAt.ToString("O"), (string?)PassWorld.Editor.ToString("D")),
            ("sprk_decisionrecord", record, RecordRepairCorePairReport.FindingChanged, idChangedAt.ToString("O"), PassWorld.Editor.ToString("D")),
        });
        _pass.Log.Entries.Where(e => e.Message.Contains(RecordRepairCorePairReport.FindingChanged)).Should().HaveCount(2)
            .And.OnlyContain(e => e.Level == LogLevel.Warning && e.Message.Contains(PassWorld.Editor.ToString("D")));
    }

    [Fact]
    public async Task Report_AnUpdateOfOtherColumnsOnly_ACreate_AndAChangeToARowCreatedBeforeGoLive_AreNotReported()
    {
        var unchanged = _pass.CorePairRow("sprk_signal", withType: true, withId: true);
        var old = _pass.CorePairRow("sprk_signal", withType: true, withId: true, createdOn: PassWorld.GoLive.AddDays(-3));
        _pass.PairAudit("sprk_signal", unchanged, $",{PassWorld.OtherColumnNumber},5,");
        _pass.PairAudit("sprk_signal", unchanged, $",{PassWorld.TypeColumnNumber},{PassWorld.IdColumnNumber},", operation: 1);
        _pass.PairAudit("sprk_signal", old, $",{PassWorld.TypeColumnNumber},");

        var result = await _pass.RunAsync();

        PassWorld.Count(result, "corePairChanged").Should().Be(0);
        PassWorld.Count(result, "corePairUndecided").Should().Be(0);
        PassWorld.Findings(result).Should().BeEmpty();
        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(",31,not-a-number,")]
    public async Task Report_AnUpdateWhoseChangedColumnsCannotBeRead_IsReportedUndecided_NeverNoChange(string? mask)
    {
        var row = _pass.CorePairRow("sprk_signal", withType: true, withId: true);
        _pass.PairAudit("sprk_signal", row, mask);

        var result = await _pass.RunAsync();

        PassWorld.Count(result, "corePairUndecided").Should().Be(1);
        PassWorld.Findings(result).Should().ContainSingle().Which.Should().Be(
            ("sprk_signal", row, RecordRepairCorePairReport.FindingChangeUndecided, PassWorld.Now.AddMinutes(-30).ToString("O"),
                PassWorld.Editor.ToString("D")));
    }

    // =====================================================================================
    // AC3 (must fire): no write, in any mode
    // =====================================================================================

    [Fact]
    public async Task Report_SendsNoWriteToEitherTable_InAScheduledOrAnOnDemandRun()
    {
        foreach (var table in RecordRepairCorePairReport.Tables)
        {
            _pass.CorePairRow(table, withType: true, withId: false);
            _pass.CorePairRow(table, withType: false, withId: true);
            var changed = _pass.CorePairRow(table, withType: true, withId: true);
            _pass.PairAudit(table, changed, $",{PassWorld.TypeColumnNumber},");
            _pass.PairAudit(table, changed, null);
            _pass.PairAudit(table, Guid.NewGuid(), $",{PassWorld.IdColumnNumber},");
        }

        _pass.NativeEvent();

        var scheduled = await _pass.RunAsync();
        var reportQueries = _pass.ReportQueries.Count;
        var onDemand = await RunOnDemandAsync(OnDemand("sprk_event"));

        World.Writes.Should().NotContain(w => RecordRepairCorePairReport.Tables.Contains(w.Table));
        _pass.EntityMocks.SelectMany(m => m.Invocations).Select(i => i.Method.Name).Should().OnlyContain(
            name => ReadOnlyCalls.Contains(name), "the report sends reads only");

        PassWorld.Count(scheduled, "corePairHalf").Should().Be(4);
        PassWorld.Count(scheduled, "corePairChanged").Should().Be(2);
        PassWorld.Count(scheduled, "corePairUndecided").Should().Be(2);
        PassWorld.Count(scheduled, "corePairRowsUnread").Should().Be(2);
        onDemand.Success.Should().BeTrue(onDemand.ErrorMessage);
        _pass.ReportQueries.Should().HaveCount(reportQueries, "an on-demand run repairs one table and does not report");
    }

    // =====================================================================================
    // AC4: its own state; once per window
    // =====================================================================================

    [Fact]
    public async Task Report_KeepsItsOwnState_AndNeverMovesTheRepairWatermarkSweepOrRetrySet()
    {
        const string repairState = """{"watermark":"2026-11-10T00:00:00+00:00","sweepUntil":"2026-11-11T00:00:00+00:00","sweepAfter":"a052d000-0000-4000-8000-000000000001","retry":{"a052d000-0000-4000-8000-000000000002":3}}""";
        foreach (var table in new[] { "sprk_event", "sprk_signal", "sprk_decisionrecord" })
            _pass.SeedState(table, repairState);
        var row = _pass.CorePairRow("sprk_signal", withType: true, withId: false);
        _pass.PairAudit("sprk_signal", row, $",{PassWorld.TypeColumnNumber},");

        var report = await _pass.RunReportOnlyAsync();

        (report.Half, report.Changed).Should().Be((1, 1));
        foreach (var table in new[] { "sprk_event", "sprk_signal", "sprk_decisionrecord" })
            _pass.State(table).Should().Be(repairState, $"{table}'s repair state is not the report's");
        foreach (var table in RecordRepairCorePairReport.Tables)
        {
            using var state = JsonDocument.Parse(_pass.ReportState(table)!);
            state.RootElement.GetProperty("halfWatermark").GetDateTimeOffset().Should().Be(PassWorld.Now - RecordRepairPassJob.SettleTime);
            state.RootElement.GetProperty("auditWatermark").GetDateTimeOffset().Should().Be(PassWorld.Now - RecordRepairPassJob.SettleTime);
        }
    }

    [Fact]
    public async Task Report_ASecondRunOverTheSameWindow_ReportsNothingNew_ThoughTheOverlapReadsTheRowsAgain()
    {
        // Inside the 15-minute overlap the next window re-reads: the handled keys keep each finding to one report.
        var half = _pass.CorePairRow("sprk_signal", withType: true, withId: false, createdOn: PassWorld.Now.AddMinutes(-5));
        var changed = _pass.CorePairRow("sprk_decisionrecord", withType: true, withId: true);
        _pass.PairAudit("sprk_decisionrecord", changed, $",{PassWorld.IdColumnNumber},", at: PassWorld.Now.AddMinutes(-5));

        var first = await _pass.RunAsync();
        _pass.Time.Advance(TimeSpan.FromMinutes(5));
        var second = await _pass.RunAsync();
        _pass.Time.Advance(TimeSpan.FromMinutes(20));
        var third = await _pass.RunAsync();

        (PassWorld.Count(first, "corePairHalf"), PassWorld.Count(first, "corePairChanged")).Should().Be((1, 1));
        PassWorld.Count(second, "reportRowsRead").Should().Be(1, "the overlap reads the half-pair row again");
        PassWorld.Count(second, "reportAuditRowsRead").Should().Be(1, "and the audit row");
        (PassWorld.Count(second, "corePairHalf"), PassWorld.Count(second, "corePairChanged")).Should().Be((0, 0));
        (PassWorld.Count(third, "corePairHalf"), PassWorld.Count(third, "corePairChanged")).Should().Be((0, 0));
        _pass.Log.Entries.Where(e => e.Message.Contains(half.ToString("D")) || e.Message.Contains(changed.ToString("D")))
            .Should().HaveCount(2, "one line per finding, once");
    }

    // =====================================================================================
    // AC5: bounded
    // =====================================================================================

    [Fact]
    public async Task Report_MoreHalfPairRowsThanTheCap_IsReportedTruncated_ThePartialRunContinuesNextRun()
    {
        for (var i = 0; i <= RecordRepairCorePairReport.MaxRowsPerScan; i++)
            _pass.CorePairRow("sprk_signal", withType: true, withId: false);

        var first = await _pass.RunAsync();
        _pass.Time.Advance(TimeSpan.FromMinutes(5));
        var second = await _pass.RunAsync();

        first.Success.Should().BeFalse("a cap reached is a partial run");
        PassWorld.Status(first).Should().Be(RecordRepairPassJob.StatusPartial);
        PassWorld.Count(first, "reportTruncated").Should().Be(1);
        PassWorld.Count(first, "corePairHalf").Should().Be(RecordRepairCorePairReport.MaxRowsPerScan);
        PassWorld.Findings(first).Should().HaveCount(RecordRepairCorePairReport.MaxFindingsInResult);
        PassWorld.Flag(first, "corePairFindingsTruncated").Should().BeTrue();
        PassWorld.Count(second, "corePairHalf").Should().Be(1, "the next run continues after the last row read");
        PassWorld.Count(second, "reportTruncated").Should().Be(0);
        second.Success.Should().BeTrue(second.ErrorMessage);
    }

    [Fact]
    public async Task Report_MoreAuditRowsThanTheCap_IsReportedTruncated_ThePartialRunContinuesNextRun()
    {
        var row = _pass.CorePairRow("sprk_signal", withType: true, withId: true);
        var ids = Enumerable.Range(0, RecordRepairCorePairReport.MaxAuditRowsPerScan + 1).Select(_ => Guid.NewGuid()).Order().ToList();
        foreach (var id in ids.SkipLast(1))
            _pass.PairAudit("sprk_signal", row, $",{PassWorld.OtherColumnNumber},", auditId: id);
        _pass.PairAudit("sprk_signal", row, $",{PassWorld.TypeColumnNumber},", auditId: ids[^1]);

        var first = await _pass.RunAsync();
        _pass.Time.Advance(TimeSpan.FromMinutes(5));
        var second = await _pass.RunAsync();

        first.Success.Should().BeFalse();
        PassWorld.Count(first, "reportTruncated").Should().Be(1);
        PassWorld.Count(first, "reportAuditRowsRead").Should().Be(RecordRepairCorePairReport.MaxAuditRowsPerScan);
        PassWorld.Count(first, "corePairChanged").Should().Be(0);
        PassWorld.Count(second, "corePairChanged").Should().Be(1, "the next run reads past the cap");
        second.Success.Should().BeTrue(second.ErrorMessage);
    }

    // =====================================================================================
    // AC6: fails closed
    // =====================================================================================

    [Theory]
    [InlineData("the audit read")]
    [InlineData("the column numbers")]
    [InlineData("the changed rows")]
    public async Task Report_AFaultReadingTheChanges_IsAFault_NeverNoChanges_AndTheAuditWatermarkStays(string fault)
    {
        var row = _pass.CorePairRow("sprk_signal", withType: true, withId: true);
        _pass.PairAudit("sprk_signal", row, $",{PassWorld.TypeColumnNumber},");
        var error = new TimeoutException("Test: the read faulted.");
        switch (fault)
        {
            case "the audit read":
                _pass.AuditFault = error;
                break;
            case "the column numbers":
                _pass.MetadataFault = error;
                break;
            default:
                _pass.RowReadFault = error;
                break;
        }

        var faulted = await _pass.RunAsync();

        faulted.Success.Should().BeFalse();
        PassWorld.Status(faulted).Should().Be(RecordRepairPassJob.StatusPartial);
        PassWorld.Count(faulted, "reportFailures").Should().BeGreaterThan(0);
        PassWorld.Count(faulted, "corePairChanged").Should().Be(0);
        _pass.Log.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("no change is assumed"));
        using (var state = JsonDocument.Parse(_pass.ReportState("sprk_signal")!))
            state.RootElement.GetProperty("auditWatermark").ValueKind.Should().Be(JsonValueKind.Null);

        (_pass.AuditFault, _pass.MetadataFault, _pass.RowReadFault) = (null, null, null);
        _pass.Time.Advance(TimeSpan.FromMinutes(5));
        var next = await _pass.RunAsync();

        PassWorld.Count(next, "corePairChanged").Should().Be(1, "the window is read again from where it stood");
    }

    // =====================================================================================
    // AC7: retry set and abandoned rows
    // =====================================================================================

    [Fact]
    public async Task Report_TheResultAndTheHeartbeat_CarryEachTablesRetrySetSize_AndTheIdsAbandonedThisRun()
    {
        var id = _pass.NativeEvent();
        World.WriteFault = new TimeoutException("Test: the write faulted.");

        var first = await _pass.RunAsync();
        PassWorld.RetrySet(first, "sprk_event").Should().BeEquivalentTo((1, Array.Empty<string>()));
        PassWorld.RetrySet(first, "contact").Should().BeEquivalentTo((0, Array.Empty<string>()));
        _pass.Log.Entries.Last(e => e.Message.Contains("heartbeat")).Message
            .Should().Contain("retrySets=sprk_event=1 ").And.Contain("abandonedIds=- ");

        JobRunResult last = first;
        for (var run = 2; run <= RecordRepairPassJob.MaxAttempts; run++)
            last = await _pass.RunAsync();

        PassWorld.RetrySet(last, "sprk_event").Should().BeEquivalentTo((0, new[] { id.ToString("D") }));
        _pass.Log.Entries.Last(e => e.Message.Contains("heartbeat")).Message
            .Should().Contain("retrySets=- ").And.Contain($"abandonedIds=sprk_event:{id:D} ");

        var after = await _pass.RunAsync();
        PassWorld.RetrySet(after, "sprk_event").Should().BeEquivalentTo((0, Array.Empty<string>()), "abandoned in an earlier run, not this one");
    }

    // =====================================================================================
    // AC8: privacy
    // =====================================================================================

    [Fact]
    public async Task Report_NoEntryOrLogLine_CarriesAColumnValueOrACoreRecordId()
    {
        _pass.CorePairRow("sprk_signal", withType: true, withId: false);
        _pass.CorePairRow("sprk_decisionrecord", withType: false, withId: true);
        var changed = _pass.CorePairRow("sprk_signal", withType: true, withId: true);
        _pass.PairAudit("sprk_signal", changed, $",{PassWorld.TypeColumnNumber},");

        var result = await _pass.RunAsync();

        PassWorld.Findings(result).Should().HaveCount(3);
        var secrets = new[] { PassWorld.CoreRecordId.ToString("D"), PassWorld.CoreRecordId.ToString("N"), PassWorld.CoreTypeRef.ToString("D") };
        foreach (var text in _pass.Log.Entries.Select(e => e.Message).Append(result.ResultJson!).Append(result.ErrorMessage ?? string.Empty))
            secrets.Should().OnlyContain(s => !text.Contains(s, StringComparison.OrdinalIgnoreCase));

        using var json = JsonDocument.Parse(result.ResultJson!);
        foreach (var finding in json.RootElement.GetProperty("corePairFindings").EnumerateArray())
            finding.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["table", "id", "finding", "auditTime", "changedBy"]);
    }

    // =====================================================================================
    // Harness: the report's rows, audit rows and column numbers
    // =====================================================================================

    private sealed partial class PassWorld
    {
        public const int TypeColumnNumber = 31;
        public const int IdColumnNumber = 32;
        public const int OtherColumnNumber = 40;
        public static readonly Guid WriterIdentity = Guid.Parse("a052d000-0000-4000-8000-0000000000e1");
        public static readonly Guid Editor = Guid.Parse("a052d000-0000-4000-8000-0000000000e2");
        public static readonly Guid CoreTypeRef = Guid.Parse("a052d000-0000-4000-8000-0000000000c1");
        public static readonly Guid CoreRecordId = Guid.Parse("a052d000-0000-4000-8000-0000000000c2");

        /// <summary>Every <c>IGenericEntityService</c> double a job was built with (the invocations prove what was sent).</summary>
        public List<Mock<IGenericEntityService>> EntityMocks { get; } = [];

        /// <summary>The Signal and Decision Record rows the report reads.</summary>
        public List<Entity> ReportRows { get; } = [];

        /// <summary>The audit rows the report reads.</summary>
        public List<Entity> AuditRows { get; } = [];

        /// <summary>Every query the report sent.</summary>
        public List<QueryExpression> ReportQueries { get; } = [];

        /// <summary>When set, the half-pair scan returns every row of the table (a query that did not hold).</summary>
        public bool ReportScanIgnoresItsConditions { get; set; }

        public Exception? AuditFault { get; set; }

        public Exception? MetadataFault { get; set; }

        public Exception? RowReadFault { get; set; }

        /// <summary>The dev object type codes of the two tables (a custom table's code differs per environment).</summary>
        public const int SignalTypeCode = 11003;
        public const int DecisionRecordTypeCode = 11002;

        public static int TypeCodeOf(string table) => table == "sprk_signal" ? SignalTypeCode : DecisionRecordTypeCode;

        /// <summary>The organization's <c>isauditenabled</c>: on, off, or <c>null</c> (not stated).</summary>
        public bool? OrganizationAudit { get; set; } = true;

        public Exception? OrganizationFault { get; set; }

        /// <summary>A table's audit flag when it is not on: <c>false</c> (off) or <c>null</c> (not stated). Absent = on.</summary>
        public Dictionary<string, bool?> TableAuditOff { get; } = [];

        /// <summary>A pair column's audit flag when it is not on, keyed <c>table.column</c>: <c>false</c> or <c>null</c>. Absent = on.</summary>
        public Dictionary<string, bool?> ColumnAuditOff { get; } = [];

        /// <summary>When set, the tables' metadata states no object type code.</summary>
        public bool NoObjectTypeCode { get; set; }

        private TableAuditMetadata ReportTable(string table)
        {
            if (MetadataFault is { } fault)
                throw fault;

            return new TableAuditMetadata(NoObjectTypeCode ? null : TypeCodeOf(table), TableAuditOff.TryGetValue(table, out var flag) ? flag : true);
        }

        private bool? ColumnAudit(string table, string column) => ColumnAuditOff.TryGetValue(table + "." + column, out var flag) ? flag : true;

        public string? ReportState(string table) => _cache.GetString(RecordRepairCorePairReport.StateKeyOf(table));

        /// <summary>A Signal or Decision Record row created by its writer, holding the pair halves asked for.</summary>
        public Guid CorePairRow(string table, bool withType, bool withId, DateTimeOffset? createdOn = null)
        {
            var id = Guid.NewGuid();
            var row = new Entity(table, id)
            {
                [table + "id"] = id,
                ["createdon"] = (createdOn ?? Now.AddHours(-1)).UtcDateTime,
                ["createdby"] = new EntityReference("systemuser", WriterIdentity),
                ["sprk_name"] = "a value the report never repeats",
            };
            if (withType)
                row["sprk_corerecordtype"] = new EntityReference("sprk_recordtype_ref", CoreTypeRef);
            if (withId)
                row["sprk_corerecordid"] = CoreRecordId.ToString("D");
            ReportRows.Add(row);
            return id;
        }

        /// <summary>An audit row of <paramref name="table"/>'s row <paramref name="rowId"/> (operation 2 = Update).</summary>
        public Guid PairAudit(string table, Guid rowId, string? mask, DateTimeOffset? at = null, int operation = 2, Guid? auditId = null)
        {
            var id = auditId ?? Guid.NewGuid();
            AuditRows.Add(new Entity("audit", id)
            {
                ["auditid"] = id,
                ["createdon"] = (at ?? Now.AddMinutes(-30)).UtcDateTime,
                ["objectid"] = new EntityReference(table, rowId),
                ["objecttypecode"] = TypeCodeOf(table),
                ["userid"] = new EntityReference("systemuser", Editor),
                ["attributemask"] = mask,
                ["operation"] = new OptionSetValue(operation),
            });
            return id;
        }

        /// <summary>The report alone, over this world's store and claims (no repair).</summary>
        public Task<RecordRepairCorePairReport.Result> RunReportOnlyAsync()
        {
            Job();
            return new RecordRepairCorePairReport(EntityMocks[^1].Object, Claims, _cache, Context(), Log,
                    new JobRunContext(Guid.NewGuid(), CorrelationId, JobRunTrigger.Scheduled, new Dictionary<string, object>()))
                .RunAsync(Now - RecordRepairPassJob.SettleTime, CancellationToken.None);
        }

        private IReadOnlyDictionary<string, ColumnMetadata> ReportColumns(string table)
        {
            if (MetadataFault is { } fault)
                throw fault;

            return new Dictionary<string, ColumnMetadata>(StringComparer.OrdinalIgnoreCase)
            {
                ["sprk_corerecordtype"] = new("sprk_corerecordtype", "Lookup", true, false, null, ColumnNumber: TypeColumnNumber,
                    IsAuditEnabled: ColumnAudit(table, "sprk_corerecordtype")),
                ["sprk_corerecordid"] = new("sprk_corerecordid", "String", true, false, 100, ColumnNumber: IdColumnNumber,
                    IsAuditEnabled: ColumnAudit(table, "sprk_corerecordid")),
                ["sprk_name"] = new("sprk_name", "String", true, false, 100, ColumnNumber: OtherColumnNumber, IsAuditEnabled: true),
            };
        }

        private EntityCollection ReportScan(QueryExpression query)
        {
            ReportQueries.Add(query);
            if (query.EntityName == RecordRepairCorePairReport.OrganizationTable)
            {
                if (OrganizationFault is { } organizationFault)
                    throw organizationFault;

                var organization = new Entity(RecordRepairCorePairReport.OrganizationTable, Guid.NewGuid());
                if (OrganizationAudit is { } on)
                    organization[RecordRepairCorePairReport.OrganizationAuditColumn] = on;
                return new EntityCollection([organization]) { EntityName = query.EntityName };
            }

            var isAudit = query.EntityName == RecordRepairCorePairReport.AuditTable;
            if (isAudit && query.Criteria.Conditions.Single(c => c.AttributeName == "objecttypecode").Values[0] is not int)
            {
                // As Dataverse answers a logical name there: HTTP 400, the fault the report must never meet.
                throw new InvalidOperationException("Test: HTTP 400, Expected type of attribute value: System.Int32 (objecttypecode).");
            }

            var idColumn = isAudit ? "auditid" : query.EntityName + "id";
            var isRowRead = !isAudit && query.Criteria.Conditions.Any(c => c.Operator == ConditionOperator.In);
            if (isAudit && AuditFault is { } auditFault)
                throw auditFault;
            if (isRowRead && RowReadFault is { } rowFault)
                throw rowFault;

            var ignore = ReportScanIgnoresItsConditions && !isAudit && !isRowRead;
            var rows = (isAudit ? AuditRows : ReportRows.Where(r => r.LogicalName == query.EntityName))
                .Where(row => ignore
                    ? query.Criteria.Conditions.Where(c => c.AttributeName == idColumn).All(c => ReportHolds(row, c, idColumn))
                    : Matches(row, query.Criteria, idColumn))
                .OrderBy(row => row.Id)
                .ToList();

            var cap = query.PageInfo?.Count is > 0 and var count ? count : int.MaxValue;
            var page = rows.Take(cap).Select(row =>
            {
                var copy = new Entity(row.LogicalName, row.Id);
                foreach (var column in query.ColumnSet.Columns.Where(row.Attributes.ContainsKey))
                    copy[column] = row[column];
                return copy;
            }).ToList();

            return new EntityCollection(page) { EntityName = query.EntityName, MoreRecords = rows.Count > cap };
        }

        private static bool Matches(Entity row, FilterExpression filter, string idColumn)
        {
            var answers = filter.Conditions.Select(c => ReportHolds(row, c, idColumn))
                .Concat(filter.Filters.Select(f => Matches(row, f, idColumn)))
                .ToList();
            return filter.FilterOperator == LogicalOperator.And ? answers.All(a => a) : answers.Any(a => a);
        }

        private static bool ReportHolds(Entity row, ConditionExpression condition, string idColumn)
        {
            row.Attributes.TryGetValue(condition.AttributeName, out var value);
            return condition.Operator switch
            {
                ConditionOperator.GreaterEqual => value is DateTime at && at >= (DateTime)condition.Values[0],
                ConditionOperator.LessThan => value is DateTime at && at < (DateTime)condition.Values[0],
                ConditionOperator.Null => value is null,
                ConditionOperator.NotNull => value is not null,
                ConditionOperator.Equal => value switch
                {
                    OptionSetValue option => option.Value == Convert.ToInt32(condition.Values[0], System.Globalization.CultureInfo.InvariantCulture),
                    int number => number == Convert.ToInt32(condition.Values[0], System.Globalization.CultureInfo.InvariantCulture),
                    string text => text == (string)condition.Values[0],
                    _ => false,
                },
                ConditionOperator.In when condition.AttributeName == "operation" =>
                    value is OptionSetValue option && condition.Values.Any(v => option.Value == Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture)),
                ConditionOperator.GreaterThan when condition.AttributeName == idColumn => row.Id.CompareTo((Guid)condition.Values[0]) > 0,
                ConditionOperator.In when condition.AttributeName == idColumn => condition.Values.Cast<Guid>().Contains(row.Id),
                _ => throw new NotSupportedException($"Test: the report scan does not model {condition.AttributeName} {condition.Operator}."),
            };
        }

        /// <summary>The result's findings as (table, id, finding, auditTime, changedBy).</summary>
        public static List<(string Table, Guid Id, string Finding, string? AuditTime, string? ChangedBy)> Findings(JobRunResult result)
        {
            using var json = JsonDocument.Parse(result.ResultJson!);
            return json.RootElement.GetProperty("corePairFindings").EnumerateArray()
                .Select(f => (f.GetProperty("table").GetString()!, Guid.Parse(f.GetProperty("id").GetString()!), f.GetProperty("finding").GetString()!,
                    f.GetProperty("auditTime").GetString(), f.GetProperty("changedBy").GetString()))
                .ToList();
        }

        public static bool Flag(JobRunResult result, string name)
        {
            using var json = JsonDocument.Parse(result.ResultJson!);
            return json.RootElement.GetProperty(name).GetBoolean();
        }

        /// <summary>A table's retry-set entry in the result: (size, abandoned ids).</summary>
        public static (int Size, string[] Abandoned) RetrySet(JobRunResult result, string table)
        {
            using var json = JsonDocument.Parse(result.ResultJson!);
            var entry = json.RootElement.GetProperty("retrySets").EnumerateArray().Single(e => e.GetProperty("table").GetString() == table);
            return (entry.GetProperty("size").GetInt32(),
                entry.GetProperty("abandoned").EnumerateArray().Select(a => a.GetString()!).ToArray());
        }
    }
}
