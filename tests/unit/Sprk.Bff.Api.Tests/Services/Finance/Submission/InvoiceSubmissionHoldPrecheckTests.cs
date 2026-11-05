using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.Domain.Signals.Billing;
using Xunit;
using Harness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionServiceTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 082c-3 (brief 082c section 3(c) and section 7; External Access R4 item 4; uac-r2 conditions (a) to (d)): the advisory billing-terms
/// pre-check the external (SPA) intake runs on a received submission, and the provisional <c>warning</c> its 202 carries. The judgement is the
/// resolver's own (<see cref="ProcedureRulesOnDate.Gaps"/> for today); a fault never fails the 202 and never reaches a reader (C16).
/// </summary>
public sealed class InvoiceSubmissionHoldPrecheckTests
{
    private static readonly InvoiceCoreRecord Core = new(InvoiceCoreRecordKind.Matter, InvoiceSubmissionServiceTests.MatterId);

    /// <summary>The Procedure's only version ended long ago with no successor: today is after its last version (a gap), whatever today is.</summary>
    private static ProcedureResolution Held()
    {
        var kit = new ProcedureKit().Procedure("Ended", from: new DateOnly(2000, 1, 1), to: new DateOnly(2001, 1, 1));
        kit.Rule("Ended", "ENDED-INCREMENT", RuleType.TimeIncrement);
        return kit.Build();
    }

    /// <summary>An open-ended version in force from long ago: no gap today.</summary>
    private static ProcedureResolution NotHeld()
    {
        var kit = new ProcedureKit().Procedure("Open", from: new DateOnly(2000, 1, 1));
        kit.Rule("Open", "OPEN-INCREMENT", RuleType.TimeIncrement);
        return kit.Build();
    }

    private static readonly DateOnly Today = new(2026, 6, 1);

    // ── The judgement ──

    [Fact(DisplayName = "082c-3 must fire: an applicable Procedure with no version today gives the provisional warning (the status view's gap code, the approved 202 sentence)")]
    public void Precheck_HeldForTerms_GivesTheWarning()
    {
        var warning = SubmissionHoldWarning.Precheck(Held(), Today);

        warning.Should().NotBeNull();
        warning!.Code.Should().Be(SubmissionReasonCodes.ProcedureGap, "the same constant code as the status view's gap warning");
        warning.Message.Should().Be(SubmissionHoldWarning.ProcedureGapPrecheckMessage);
    }

    [Fact(DisplayName = "082c-3 must not fire: terms in force today give no warning; a record with no Procedure at all gives none")]
    public void Precheck_NotHeld_GivesNone()
    {
        SubmissionHoldWarning.Precheck(NotHeld(), Today).Should().BeNull();
        SubmissionHoldWarning.Precheck(new ProcedureKit().Build(), Today).Should().BeNull();
    }

    [Fact(DisplayName = "082c-3: the pre-check judges the date it is given (a Procedure that has not ended yet is held only after its end)")]
    public void Precheck_JudgesTheGivenDate()
    {
        var kit = new ProcedureKit().Procedure("Ends", from: new DateOnly(2026, 1, 1), to: new DateOnly(2026, 3, 1));
        kit.Rule("Ends", "ENDS-INCREMENT", RuleType.TimeIncrement);
        var resolution = kit.Build();

        SubmissionHoldWarning.Precheck(resolution, new DateOnly(2026, 2, 15)).Should().BeNull();
        SubmissionHoldWarning.Precheck(resolution, new DateOnly(2026, 3, 1)).Should().NotBeNull();
    }

    // ── Partner safety: the exact constant set (uac-r2 (c): one fixed sentence; no names, dates, counts, ids or rule codes) ──

    [Fact(DisplayName = "082c-3 partner-safe: the 202 warning is the exact approved constant pair, whatever the Procedure's name, dates and rule codes are")]
    public void TheWarning_IsAnExactConstantPair_NoNamesDatesOrCodes()
    {
        const string ApprovedCode = "ebilling.submission.procedure_gap";
        const string ApprovedMessage =
            "As of today, no billing terms are set up for the record you chose. If the invoice's work dates are not covered either, it will be held for review, not rejected.";

        var kit = new ProcedureKit().Procedure("Secret Counterparty Terms", from: new DateOnly(2000, 1, 1), to: new DateOnly(2001, 2, 3));
        kit.Rule("Secret Counterparty Terms", "SECRET-RATE-CAP", RuleType.TimeIncrement);
        var secret = SubmissionHoldWarning.Precheck(kit.Build(), Today)!;
        var plain = SubmissionHoldWarning.Precheck(Held(), Today)!;

        foreach (var warning in new[] { secret, plain, SubmissionHoldWarning.ForPrecheck() })
        {
            warning.Should().Be(new SubmissionHoldWarning(ApprovedCode, ApprovedMessage));
        }

        var json = JsonSerializer.Serialize(secret, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.Should().NotContain("Secret").And.NotContain("SECRET").And.NotContain("2001").And.NotContain("2000");
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("code", "message");
        ReaderSentinel.Scan(json).Should().BeEmpty("no BRR- code and no id-shaped value");
    }

    [Fact(DisplayName = "082c-3 wire shape: the 202 carries `warning {code, message}` only when the pre-check set it; without it the body is byte-for-byte the four fields")]
    public void TheAcceptedBody_CarriesWarningOnlyWhenSet()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var id = Guid.NewGuid();
        var without = new InvoiceSubmissionAccepted(id, id, "INV-1.1", "/s");
        var with = without with { Warning = SubmissionHoldWarning.ForPrecheck() };

        using var plain = JsonDocument.Parse(JsonSerializer.Serialize(without, web));
        plain.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("submissionId", "jobId", "trackingNumber", "statusUrl");

        using var warned = JsonDocument.Parse(JsonSerializer.Serialize(with, web));
        warned.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("submissionId", "jobId", "trackingNumber", "statusUrl", "warning");
        warned.RootElement.GetProperty("warning").EnumerateObject().Select(p => p.Name).Should().Equal("code", "message");
    }

    // ── The service: reads for the given record and firm, fails quiet ──

    [Fact(DisplayName = "082c-3 must fire: the service returns the warning for a held record and reads the resolution for exactly the record and firm it was given")]
    public async Task Service_Held_GivesTheWarning_ForTheGivenRecordAndFirm()
    {
        var h = new Harness { ProcedureRead = Held };

        var warning = await h.Build().PrecheckHoldWarningAsync(Core, InvoiceSubmissionServiceTests.FirmId, CancellationToken.None);

        warning.Should().Be(SubmissionHoldWarning.ForPrecheck());
        h.PrecheckReads.Should().ContainSingle().Which.Should().Be(
            (new CoreRecordReference(CoreRecordKind.Matter, InvoiceSubmissionServiceTests.MatterId), InvoiceSubmissionServiceTests.FirmId));
    }

    [Theory(DisplayName = "082c-3: the core record kinds map one to one (a project and a work assignment are read as themselves)")]
    [InlineData(InvoiceCoreRecordKind.Matter, CoreRecordKind.Matter)]
    [InlineData(InvoiceCoreRecordKind.Project, CoreRecordKind.Project)]
    [InlineData(InvoiceCoreRecordKind.WorkAssignment, CoreRecordKind.WorkAssignment)]
    public async Task Service_ReadsTheKindOfTheRecord(InvoiceCoreRecordKind kind, CoreRecordKind expected)
    {
        var h = new Harness { ProcedureRead = NotHeld };
        var id = Guid.NewGuid();

        await h.Build().PrecheckHoldWarningAsync(new InvoiceCoreRecord(kind, id), InvoiceSubmissionServiceTests.FirmId, CancellationToken.None);

        h.PrecheckReads.Should().ContainSingle().Which.Core.Should().Be(new CoreRecordReference(expected, id));
    }

    [Fact(DisplayName = "082c-3 must not fire: a record whose terms are in force gives no warning")]
    public async Task Service_NotHeld_GivesNone()
    {
        var h = new Harness { ProcedureRead = NotHeld };

        (await h.Build().PrecheckHoldWarningAsync(Core, InvoiceSubmissionServiceTests.FirmId, CancellationToken.None)).Should().BeNull();
    }

    [Fact(DisplayName = "082c-3 fail quiet (seed 1): a read fault gives no warning and no throw; the log carries the fault's type name only, never its message")]
    public async Task Service_AReadFault_GivesNoWarning_AndLogsTheTypeNameOnly()
    {
        const string Secret = "SECRET-RECORD-NAME 6f1c0d7e-aaaa-4bbb-8ccc-0123456789ab";
        var log = new RecordingLogger();
        var h = new Harness { ProcedureRead = () => throw new InvalidOperationException(Secret) };

        var warning = await h.Build(log).PrecheckHoldWarningAsync(Core, InvoiceSubmissionServiceTests.FirmId, CancellationToken.None);

        warning.Should().BeNull();
        var entry = log.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain(nameof(InvalidOperationException));
        entry.Message.Should().NotContain("SECRET").And.NotContain("6f1c0d7e");
        entry.Exception.Should().BeNull("the exception object (and so its message and stack) is never handed to the logger");
    }

    [Fact(DisplayName = "082c-3 fail quiet: an inconsistent resolution (the resolver's own fault on today) gives no warning and no throw")]
    public async Task Service_AResolutionFault_GivesNoWarning()
    {
        // The pre-check's own date is today: the resolver faults on a date with two versions of one Procedure in force (I-P1).
        var kit = new ProcedureKit().Procedure("Twice", from: new DateOnly(2000, 1, 1)).NextVersion("Twice", 2, new DateOnly(2000, 6, 1));
        var log = new RecordingLogger();
        var h = new Harness { ProcedureRead = kit.Build };

        var warning = await h.Build(log).PrecheckHoldWarningAsync(Core, InvoiceSubmissionServiceTests.FirmId, CancellationToken.None);

        warning.Should().BeNull();
        log.Entries.Should().ContainSingle().Which.Message.Should().Contain(nameof(ProcedureResolutionFaultException));
    }

    [Fact(DisplayName = "082c-3 F1: a caller that went away is not swallowed: the cancellation propagates (and is not logged as a fault)")]
    public async Task Service_ACancelledCaller_Propagates_AndLogsNothing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var log = new RecordingLogger();
        var h = new Harness { ProcedureRead = () => throw new OperationCanceledException(cts.Token) };

        var act = () => h.Build(log).PrecheckHoldWarningAsync(Core, InvoiceSubmissionServiceTests.FirmId, cts.Token).WaitAsync(Bound);

        await act.Should().ThrowAsync<OperationCanceledException>();
        log.Entries.Should().BeEmpty();
    }

    [Fact(DisplayName = "082c-3 F1: the caller's token cancelled WHILE the read is pending propagates (the original token, not the budget's, decides)")]
    public async Task Service_TheCallerAbortsDuringTheRead_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var log = new RecordingLogger();
        var h = new Harness { ProcedureReadAsync = NeverCompletes, PrecheckTimeLimit = TimeSpan.FromMinutes(5) };

        var pending = h.Build(log).PrecheckHoldWarningAsync(Core, InvoiceSubmissionServiceTests.FirmId, cts.Token);
        pending.IsCompleted.Should().BeFalse("the read has not completed and the budget is far away");
        cts.Cancel();

        var act = () => pending.WaitAsync(Bound);
        await act.Should().ThrowAsync<OperationCanceledException>();
        log.Entries.Should().BeEmpty();
    }

    /// <summary>The bound on every awaited call of these tests: a regression fails red (a TimeoutException) instead of hanging the run.</summary>
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    /// <summary>
    /// A read that never completes and IGNORES the token it was given, as the Dataverse SDK's channel call does (it takes no token): only a
    /// wait the service itself bounds can cut it off.
    /// </summary>
    private static Task<ProcedureResolution> NeverCompletes(CancellationToken token) =>
        new TaskCompletionSource<ProcedureResolution>(TaskCreationOptions.RunContinuationsAsynchronously).Task;

    [Fact(DisplayName = "082c-3 F1 fail quiet (seed 2): a read that never completes is cut off at the budget: no warning, no throw, the fault's type name logged and nothing else")]
    public async Task Service_AReadThatNeverCompletes_TimesOut_NoWarning_NoThrow()
    {
        var log = new RecordingLogger();
        var h = new Harness { ProcedureReadAsync = NeverCompletes, PrecheckTimeLimit = TimeSpan.FromMilliseconds(50) };

        var warning = await h.Build(log).PrecheckHoldWarningAsync(Core, InvoiceSubmissionServiceTests.FirmId, CancellationToken.None).WaitAsync(Bound);

        warning.Should().BeNull();
        var entry = log.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain(nameof(TimeoutException));
        entry.Exception.Should().BeNull();
    }

    [Fact(DisplayName = "082c-3 F1: the budget token is cancelled when the wait times out, so an abandoned read that does honour it stops at its next check")]
    public async Task Service_ATimeout_CancelsTheAbandonedReadsToken()
    {
        CancellationToken seen = default;
        var h = new Harness
        {
            ProcedureReadAsync = token =>
            {
                seen = token;
                return new TaskCompletionSource<ProcedureResolution>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
            },
            PrecheckTimeLimit = TimeSpan.FromMilliseconds(50),
        };

        (await h.Build().PrecheckHoldWarningAsync(Core, InvoiceSubmissionServiceTests.FirmId, CancellationToken.None).WaitAsync(Bound)).Should().BeNull();

        seen.IsCancellationRequested.Should().BeTrue();
    }

    [Fact(DisplayName = "082c-3 F1: an abandoned read that faults after the timeout is observed: it raises no UnobservedTaskException")]
    public async Task Service_AnAbandonedRead_ThatFaultsLater_RaisesNoUnobservedTaskException()
    {
        const string Marker = "ABANDONED-READ-FAULT-082c3-F1";
        var unobserved = new List<Exception>();
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e) => unobserved.AddRange(e.Exception.Flatten().InnerExceptions);

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            // Everything that references the abandoned task lives and dies inside this call, so it is collectable when it returns.
            await TimeOutThenFaultTheAbandonedReadAsync(Marker).WaitAsync(Bound);

            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }

        unobserved.Where(e => e.Message == Marker).Should().BeEmpty("the abandoned read's late fault is observed by the service");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static async Task TimeOutThenFaultTheAbandonedReadAsync(string marker)
    {
        var read = new TaskCompletionSource<ProcedureResolution>(TaskCreationOptions.RunContinuationsAsynchronously);
        var h = new Harness { ProcedureReadAsync = _ => read.Task, PrecheckTimeLimit = TimeSpan.FromMilliseconds(50) };

        (await h.Build().PrecheckHoldWarningAsync(Core, InvoiceSubmissionServiceTests.FirmId, CancellationToken.None)).Should().BeNull();

        read.SetException(new InvalidOperationException(marker));
    }

    [Fact(DisplayName = "082c-3 F1: the budget is a fixed two seconds, applied by default")]
    public void TheBudget_IsTwoSeconds_ByDefault()
    {
        InvoiceSubmissionService.PrecheckTimeout.Should().Be(TimeSpan.FromSeconds(2));
        new Harness().Build().PrecheckTimeLimit.Should().Be(InvoiceSubmissionService.PrecheckTimeout);
    }

    // ── The real read: the resolver over an in-memory Dataverse (the loader's own call, not the seam) ──

    private static readonly Guid Firm = Guid.Parse("082c3000-0000-4000-8000-0000000000f1");
    private static readonly Guid Matter = Guid.Parse("082c3000-0000-4000-8000-0000000000a1");

    /// <summary>A matter under an AllRecords Procedure whose only version covers <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static SecureChildShareWorld WorldWith(DateOnly from, DateOnly? to)
    {
        var world = SecureChildShareWorld.Standard();
        var recordType = Guid.Parse("082c3000-0000-4000-8000-0000000000e1");
        world.Add(SecureRootInheritance.RecordTypeRefEntity, recordType, ("sprk_recordlogicalname", "sprk_matter"));
        world.Add(SecureRootInheritance.Matter, Matter, ("sprk_name", "A matter"));

        var procedure = Guid.NewGuid();
        world.Add(ProcedureEntity.Entity, procedure,
            (ProcedureEntity.AreaColumn, new OptionSetValue(RuleDomains.PolicyDomainBilling)),
            (ProcedureEntity.StatusColumn, new OptionSetValue(ProcedureEntity.StatusActive)),
            (ProcedureEntity.NameColumn, "Terms"),
            (ProcedureEntity.AppliesToColumn, new OptionSetValue((int)ProcedureAppliesTo.AllRecords)));
        var set = Guid.NewGuid();
        world.Add("sprk_ruleset", set,
            ("sprk_procedure", new EntityReference(ProcedureEntity.Entity, procedure)),
            (RuleDomains.PolicyDomainColumn, new OptionSetValue(RuleDomains.PolicyDomainBilling)), ("statecode", new OptionSetValue(0)));
        var version = Guid.NewGuid();
        var columns = new List<(string, object)>
        {
            (ProcedureEntity.VersionProcedureColumn, new EntityReference(ProcedureEntity.Entity, procedure)),
            (ProcedureEntity.VersionNumberColumn, 1),
            (ProcedureEntity.VersionStatusColumn, new OptionSetValue(ProcedureEntity.VersionStatusActive)),
            (ProcedureEntity.VersionEffectiveFromColumn, from.ToDateTime(TimeOnly.MinValue)),
        };
        if (to is { } end) columns.Add((ProcedureEntity.VersionEffectiveToColumn, end.ToDateTime(TimeOnly.MinValue)));
        world.Add(ProcedureEntity.VersionEntity, version, columns.ToArray());
        return world;
    }

    private static InvoiceSubmissionService ServiceOver(SecureChildShareWorld world) =>
        new Harness().BuildReal(SecureChildShareWorld.EntitiesOver(() => world).Object);

    [Fact(DisplayName = "082c-3 the real read: a Procedure that ended gives the warning today; one in force gives none (the resolver over an in-memory Dataverse)")]
    public async Task RealRead_EndedProcedure_IsHeldToday_OpenProcedure_IsNot()
    {
        var core = new InvoiceCoreRecord(InvoiceCoreRecordKind.Matter, Matter);

        var ended = ServiceOver(WorldWith(new DateOnly(2000, 1, 1), new DateOnly(2001, 1, 1)));
        (await ended.PrecheckHoldWarningAsync(core, Firm, CancellationToken.None)).Should().Be(SubmissionHoldWarning.ForPrecheck());

        var open = ServiceOver(WorldWith(new DateOnly(2000, 1, 1), null));
        (await open.PrecheckHoldWarningAsync(core, Firm, CancellationToken.None)).Should().BeNull();
    }

    private sealed class RecordingLogger : ILogger<InvoiceSubmissionService>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
