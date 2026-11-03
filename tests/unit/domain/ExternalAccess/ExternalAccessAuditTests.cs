using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// Slice C2c: admin audit records are exported regardless of trace sampling. The external-access audit is written under
/// <see cref="ExternalAccessAudit.Category"/> (under <c>Sprk.Bff.Api</c>, so the configured level passes it) through
/// <see cref="ExternalAccessAudit.Write"/> (outside the request's trace, so the record carries no span and trace-based log
/// sampling does not apply).
/// </summary>
public class ExternalAccessAuditTests
{
    private static readonly ActivitySource Source = new("Sprk.Bff.Api.Tests.ExternalAccessAudit");

    // ── The helper ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Write_ClearsTheActivityForTheWrite_AndRestoresItAfter()
    {
        using var request = new Activity("request").Start();
        Activity? during = request;

        ExternalAccessAudit.Write(NullLoggerFor(), _ => during = Activity.Current);

        during.Should().BeNull("the record is written with no Activity");
        Activity.Current.Should().BeSameAs(request, "the request's Activity is restored");
    }

    [Fact]
    public void Write_RestoresTheActivity_WhenTheWriteThrows()
    {
        using var request = new Activity("request").Start();

        var act = () => ExternalAccessAudit.Write(NullLoggerFor(), _ => throw new InvalidOperationException("sink fault"));

        act.Should().Throw<InvalidOperationException>();
        Activity.Current.Should().BeSameAs(request, "the Activity is restored in a finally");
    }

    [Fact]
    public void Write_WithNoActivity_LeavesNone()
    {
        Activity.Current = null;

        ExternalAccessAudit.Write(NullLoggerFor(), _ => { });

        Activity.Current.Should().BeNull();
    }

    [Fact]
    public void TheCategory_IsUnderSprkBffApi_AndDedicatedToTheAudit()
    {
        ExternalAccessAudit.Category.Should().Be("Sprk.Bff.Api.Audit.ExternalAccess");
        ExternalAccessAudit.Category.Should().StartWith("Sprk.Bff.Api.");
    }

    // ── The exported log record (the OpenTelemetry SDK the host uses) ────────────────────────────────────────

    /// <summary>
    /// MUST FIRE: inside an active request Activity, sampled (Recorded) or not, the record written through the helper carries
    /// no trace id and no span id, so the exporter's trace-based log filter never applies to it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InsideARequestActivity_TheAuditRecord_CarriesNoSpanOrTraceId(bool recorded)
    {
        var records = new List<(ActivityTraceId TraceId, ActivitySpanId SpanId, string? Category)>();
        using var factory = OpenTelemetryFactory(records);
        using var request = StartRequestActivity(recorded);

        ExternalAccessAudit.Write(factory.CreateLogger(ExternalAccessAudit.Category), audit => audit.LogInformation("audited"));

        var record = records.Should().ContainSingle().Subject;
        record.Category.Should().Be(ExternalAccessAudit.Category);
        record.TraceId.Should().Be(default(ActivityTraceId));
        record.SpanId.Should().Be(default(ActivitySpanId));
        Activity.Current.Should().BeSameAs(request);
    }

    /// <summary>MUST NOT FIRE (the control): written without the helper, the record carries the request's span.</summary>
    [Fact]
    public void WithoutTheHelper_TheRecordCarriesTheRequestSpan()
    {
        var records = new List<(ActivityTraceId TraceId, ActivitySpanId SpanId, string? Category)>();
        using var factory = OpenTelemetryFactory(records);
        using var request = StartRequestActivity(recorded: false);

        factory.CreateLogger(ExternalAccessAudit.Category).LogInformation("not through the helper");

        var record = records.Should().ContainSingle().Subject;
        record.SpanId.Should().Be(request.SpanId);
        record.TraceId.Should().Be(request.TraceId);
    }

    // ── The configured level (the appsettings the host layers) ──────────────────────────────────────────────

    /// <summary>
    /// MUST FIRE guard: with the base settings and the production overlay layered as the host layers them, the audit
    /// category's effective minimum level is Information or lower, for the provider-wide rules and for the OpenTelemetry
    /// provider the host exports through. <c>appsettings.json</c> is not committed; it is produced from
    /// <c>appsettings.template.json</c> by replacing its deploy-time tokens, so the template is read with its unquoted tokens
    /// set to null (the Logging section carries none).
    /// </summary>
    [Fact]
    public void TheAuditCategory_IsEnabledAtInformation_UnderTheProductionSettings()
    {
        var level = EffectiveMinimumLevel(ProductionConfiguration(), ExternalAccessAudit.Category);

        ((int)level).Should().BeLessThanOrEqualTo((int)LogLevel.Information,
            "admin audit records are written at Information and must pass the configured level");
    }

    /// <summary>
    /// The guard's must-fire controls: raising <c>Sprk.Bff.Api</c>, or a more specific prefix of the audit category, to
    /// Warning makes the category's effective level Warning, which the guard above rejects. Raising an unrelated category
    /// leaves it at Information.
    /// </summary>
    [Theory]
    [InlineData("Logging:LogLevel:Sprk.Bff.Api", "Warning", LogLevel.Warning)]
    [InlineData("Logging:LogLevel:Sprk.Bff.Api.Audit", "Warning", LogLevel.Warning)]
    [InlineData("Logging:OpenTelemetry:LogLevel:Sprk.Bff.Api", "Warning", LogLevel.Warning)]
    [InlineData("Logging:LogLevel:Sprk.Bff.Api.Services", "Warning", LogLevel.Information)]
    [InlineData("Logging:LogLevel:Default", "Error", LogLevel.Information)]
    public void TheGuard_SeesAnOverride_ThatRaisesTheAuditCategory(string key, string value, LogLevel expected)
    {
        var config = new ConfigurationBuilder()
            .AddConfiguration(ProductionConfiguration())
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();

        EffectiveMinimumLevel(config, ExternalAccessAudit.Category).Should().Be(expected);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────────────────

    private static ILogger NullLoggerFor() => Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    private static Activity StartRequestActivity(bool recorded)
    {
        var activity = new Activity("request");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.ActivityTraceFlags = recorded ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;
        return activity.Start();
    }

    private static ILoggerFactory OpenTelemetryFactory(
        List<(ActivityTraceId TraceId, ActivitySpanId SpanId, string? Category)> records)
        => LoggerFactory.Create(builder => builder.AddOpenTelemetry(options => options.AddProcessor(new CapturingProcessor(records))));

    private sealed class CapturingProcessor(List<(ActivityTraceId TraceId, ActivitySpanId SpanId, string? Category)> records)
        : BaseProcessor<LogRecord>
    {
        public override void OnEnd(LogRecord data) => records.Add((data.TraceId, data.SpanId, data.CategoryName));
    }

    /// <summary>The lowest level a logger of <paramref name="category"/> is enabled at, as the host's filter rules decide.</summary>
    private static LogLevel EffectiveMinimumLevel(IConfiguration config, string category)
    {
        using var factory = LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(config.GetSection("Logging"));
            builder.AddProvider(new OpenTelemetryAliasedProbe());
        });
        var logger = factory.CreateLogger(category);
        foreach (var level in Enum.GetValues<LogLevel>().Where(l => l != LogLevel.None))
        {
            if (logger.IsEnabled(level))
                return level;
        }

        return LogLevel.None;
    }

    /// <summary>
    /// A provider under the OpenTelemetry provider's alias, so provider-specific rules for it apply as in the host. Its loggers
    /// accept every level, so only the configured filter rules decide.
    /// </summary>
    [ProviderAlias("OpenTelemetry")]
    private sealed class OpenTelemetryAliasedProbe : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new AcceptAll();

        public void Dispose()
        {
        }

        private sealed class AcceptAll : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
            }
        }
    }

    private static IConfiguration ProductionConfiguration()
    {
        var api = ApiDirectory();
        var baseSettings = Regex.Replace(
            File.ReadAllText(Path.Combine(api, "appsettings.template.json")), @"(?<=:\s*)#\{[A-Z0-9_]+\}#", "null");
        return new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(baseSettings)))
            .AddJsonFile(Path.Combine(api, "appsettings.Production.json.template"), optional: false)
            .Build();
    }

    private static string ApiDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "appsettings.template.json")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the settings templates are part of the repository checkout");
        return Path.Combine(dir!.FullName, "src", "server", "api", "Sprk.Bff.Api");
    }
}

/// <summary>
/// Slice C2c: a logger factory that captures every record with its category, event id, level, message and the
/// <see cref="Activity.Current"/> at the moment it was written (what the exporter's log record captures its trace context from).
/// </summary>
public sealed class AuditLogCapture : ILoggerProvider
{
    public AuditLogCapture()
    {
        Factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(this));
    }

    /// <summary>The factory to hand to the code under test.</summary>
    public ILoggerFactory Factory { get; }

    public List<(string Category, EventId EventId, LogLevel Level, string Message, Activity? ActivityAtWrite)> Records { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(AuditLogCapture owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner.Records)
                owner.Records.Add((category, eventId, logLevel, formatter(state, exception), Activity.Current));
        }
    }
}
