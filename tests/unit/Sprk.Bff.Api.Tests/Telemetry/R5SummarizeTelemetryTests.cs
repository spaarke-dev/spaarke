using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using FluentAssertions;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Telemetry;

/// <summary>
/// Unit tests for <see cref="R5SummarizeTelemetry"/> (task 008, D1-08).
///
/// These tests lock in the downstream-contract event schema for Phase 3 task 042 (D3-03)
/// Kusto dashboard authors:
/// - r5.summarize.invocation Counter increments by 1 per call
/// - dimensions: path, completion_status, tenant.id (optional)
/// - both invocation paths (agent_tool AND direct_endpoint) record to the SAME counter
/// - cardinality enforcement: invalid enum input throws ArgumentException at call site
///
/// Implementation uses <see cref="MeterListener"/> for in-process capture so the assertions
/// run without standing up the full OpenTelemetry exporter pipeline.
/// </summary>
public sealed class R5SummarizeTelemetryTests
{
    /// <summary>
    /// Captures all <c>long</c> measurements emitted by a target Meter for assertion.
    /// Filters on a single instrument name to keep test scope tight.
    /// </summary>
    private sealed class LongMeasurementCapture : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly string _meterName;
        private readonly string _instrumentName;

        public ConcurrentBag<(long Value, Dictionary<string, object?> Tags)> Measurements { get; } = new();

        public LongMeasurementCapture(string meterName, string instrumentName)
        {
            _meterName = meterName;
            _instrumentName = instrumentName;

            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == _meterName && instrument.Name == _instrumentName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };

            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                var tagDict = new Dictionary<string, object?>(tags.Length, StringComparer.Ordinal);
                foreach (var kv in tags)
                {
                    tagDict[kv.Key] = kv.Value;
                }
                Measurements.Add((value, tagDict));
            });

            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>
    /// Captures all <c>double</c> measurements emitted by a target Meter for assertion.
    /// </summary>
    private sealed class DoubleMeasurementCapture : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly string _meterName;
        private readonly string _instrumentName;

        public ConcurrentBag<(double Value, Dictionary<string, object?> Tags)> Measurements { get; } = new();

        public DoubleMeasurementCapture(string meterName, string instrumentName)
        {
            _meterName = meterName;
            _instrumentName = instrumentName;

            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == _meterName && instrument.Name == _instrumentName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };

            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            {
                var tagDict = new Dictionary<string, object?>(tags.Length, StringComparer.Ordinal);
                foreach (var kv in tags)
                {
                    tagDict[kv.Key] = kv.Value;
                }
                Measurements.Add((value, tagDict));
            });

            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Happy-path counter emission with bounded dimensions
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RecordSummarizeInvocation_WithValidEnums_EmitsExpectedCounter()
    {
        // Arrange — listener must be constructed BEFORE the telemetry singleton so the
        // InstrumentPublished callback fires when CreateCounter runs in the ctor.
        using var capture = new LongMeasurementCapture(R5SummarizeTelemetry.MeterName, "r5.summarize.invocation");
        using var telemetry = new R5SummarizeTelemetry();

        // Act
        telemetry.RecordSummarizeInvocation(
            path: "agent_tool",
            completionStatus: "success",
            fileCount: 3,
            totalTokens: 1500,
            latencyMs: 850.5,
            tenantId: "tenant-abc");

        // Assert — exactly one measurement of value 1 with the locked dimension set.
        capture.Measurements.Should().HaveCount(1);
        var (value, tags) = capture.Measurements.Single();
        value.Should().Be(1);
        tags.Should().ContainKey("path").WhoseValue.Should().Be("agent_tool");
        tags.Should().ContainKey("completion_status").WhoseValue.Should().Be("success");
        tags.Should().ContainKey("tenant.id").WhoseValue.Should().Be("tenant-abc");
        // Schema lock: NO sessionId, NO correlation IDs, NO user IDs, NO file names as dimensions.
        tags.Should().NotContainKey("sessionId");
        tags.Should().NotContainKey("correlation_id");
        tags.Should().NotContainKey("user_id");
        tags.Should().NotContainKey("file_name");
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Cardinality safety — out-of-enum inputs throw
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RecordSummarizeInvocation_WithInvalidPath_ThrowsArgumentException()
    {
        using var telemetry = new R5SummarizeTelemetry();

        var act = () => telemetry.RecordSummarizeInvocation(
            path: "browser",
            completionStatus: "success",
            fileCount: 1,
            totalTokens: 100,
            latencyMs: 10.0);

        act.Should().Throw<ArgumentException>()
            .Where(e => e.ParamName == "path")
            .WithMessage("*Invalid R5 Summarize invocation path*");
    }

    [Fact]
    public void RecordSummarizeInvocation_WithInvalidCompletionStatus_ThrowsArgumentException()
    {
        using var telemetry = new R5SummarizeTelemetry();

        var act = () => telemetry.RecordSummarizeInvocation(
            path: "agent_tool",
            completionStatus: "partial",
            fileCount: 1,
            totalTokens: 100,
            latencyMs: 10.0);

        act.Should().Throw<ArgumentException>()
            .Where(e => e.ParamName == "completionStatus")
            .WithMessage("*Invalid R5 Summarize completion_status*");
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Optional tenantId discipline
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RecordSummarizeInvocation_WithNullTenantId_DoesNotEmitTenantDimension()
    {
        using var capture = new LongMeasurementCapture(R5SummarizeTelemetry.MeterName, "r5.summarize.invocation");
        using var telemetry = new R5SummarizeTelemetry();

        telemetry.RecordSummarizeInvocation(
            path: "direct_endpoint",
            completionStatus: "success",
            fileCount: 1,
            totalTokens: 200,
            latencyMs: 50.0,
            tenantId: null);

        capture.Measurements.Should().HaveCount(1);
        var (_, tags) = capture.Measurements.Single();
        tags.Should().NotContainKey("tenant.id");
        tags.Should().ContainKey("path");
        tags.Should().ContainKey("completion_status");
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Session-files index size histogram + phase enum
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RecordSessionFilesIndexSize_WithValidPhase_RecordsHistogram()
    {
        using var capture = new LongMeasurementCapture(R5SummarizeTelemetry.MeterName, "r5.session_files.index_size");
        using var telemetry = new R5SummarizeTelemetry();

        telemetry.RecordSessionFilesIndexSize(phase: "post_write", documentCount: 42, tenantId: "tenant-xyz");

        capture.Measurements.Should().HaveCount(1);
        var (value, tags) = capture.Measurements.Single();
        value.Should().Be(42);
        tags.Should().ContainKey("phase").WhoseValue.Should().Be("post_write");
        tags.Should().ContainKey("tenant.id").WhoseValue.Should().Be("tenant-xyz");
    }

    [Fact]
    public void RecordSessionFilesIndexSize_WithInvalidPhase_ThrowsArgumentException()
    {
        using var telemetry = new R5SummarizeTelemetry();

        var act = () => telemetry.RecordSessionFilesIndexSize(phase: "midstream", documentCount: 10);

        act.Should().Throw<ArgumentException>()
            .Where(e => e.ParamName == "phase")
            .WithMessage("*Invalid R5 session-files phase*");
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Load-bearing invariant — both invocation paths feed the SAME counter
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BothInvocationPaths_RecordViaSameCounter()
    {
        // This is the single-event-stream invariant the task ships. Phase 3 D3-03 dashboards
        // depend on it: dashboards filter by `path` to see path mix, but the underlying
        // counter is one. A regression here (e.g. accidentally splitting into two counters)
        // would invalidate every downstream Kusto query that aggregates by completion_status
        // across both paths.

        using var capture = new LongMeasurementCapture(R5SummarizeTelemetry.MeterName, "r5.summarize.invocation");
        using var telemetry = new R5SummarizeTelemetry();

        telemetry.RecordSummarizeInvocation(
            path: "agent_tool",
            completionStatus: "success",
            fileCount: 2,
            totalTokens: 800,
            latencyMs: 400.0,
            tenantId: "tenant-1");

        telemetry.RecordSummarizeInvocation(
            path: "direct_endpoint",
            completionStatus: "success",
            fileCount: 1,
            totalTokens: 200,
            latencyMs: 120.0,
            tenantId: "tenant-1");

        // Both calls must produce measurements on the SAME instrument (single capture).
        capture.Measurements.Should().HaveCount(2);
        capture.Measurements.Select(m => (string)m.Tags["path"]!).Should().BeEquivalentTo(new[]
        {
            "agent_tool",
            "direct_endpoint",
        });
        capture.Measurements.Sum(m => m.Value).Should().Be(2,
            "the single canonical r5.summarize.invocation counter is incremented by both paths");
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Histograms keyed on the same tag set as the invocation counter
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RecordSummarizeInvocation_AlsoEmits_FileCount_TotalTokens_Latency_Histograms()
    {
        using var fileCountCapture = new LongMeasurementCapture(R5SummarizeTelemetry.MeterName, "r5.summarize.file_count");
        using var totalTokensCapture = new LongMeasurementCapture(R5SummarizeTelemetry.MeterName, "r5.summarize.total_tokens");
        using var latencyCapture = new DoubleMeasurementCapture(R5SummarizeTelemetry.MeterName, "r5.summarize.latency_ms");
        using var telemetry = new R5SummarizeTelemetry();

        telemetry.RecordSummarizeInvocation(
            path: "direct_endpoint",
            completionStatus: "success",
            fileCount: 5,
            totalTokens: 2000,
            latencyMs: 950.25,
            tenantId: "tenant-multi");

        fileCountCapture.Measurements.Should().ContainSingle().Which.Value.Should().Be(5);
        totalTokensCapture.Measurements.Should().ContainSingle().Which.Value.Should().Be(2000);
        latencyCapture.Measurements.Should().ContainSingle().Which.Value.Should().Be(950.25);

        // All three histograms carry the same locked tag set.
        foreach (var tags in new[]
                 {
                     fileCountCapture.Measurements.Single().Tags,
                     totalTokensCapture.Measurements.Single().Tags,
                     latencyCapture.Measurements.Single().Tags,
                 })
        {
            tags.Should().ContainKey("path").WhoseValue.Should().Be("direct_endpoint");
            tags.Should().ContainKey("completion_status").WhoseValue.Should().Be("success");
            tags.Should().ContainKey("tenant.id").WhoseValue.Should().Be("tenant-multi");
        }
    }
}
