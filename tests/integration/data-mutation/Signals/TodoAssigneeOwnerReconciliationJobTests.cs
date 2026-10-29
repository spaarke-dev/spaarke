using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// Ontology task 148 (D-135 item 2, follow the assignee) — <see cref="TodoAssigneeOwnerReconciliationJob"/> over the REAL
/// ownership resolver, the REAL core-ancestor resolver, the REAL <see cref="PersonalToDoAssignee"/> rule and uac-r2's REAL
/// <see cref="SecureChildReconciler.ReownOrdinaryRowAsync"/>, all over one in-memory Dataverse that evaluates every query
/// (<see cref="SecureChildShareWorld"/>). The owner write is the reconciler's conditional Web API PATCH, answered by
/// <see cref="OwnerWebApi"/> over the same rows. The cases are the POML's closed list.
/// </summary>
public class TodoAssigneeOwnerReconciliationJobTests
{
    private static readonly Guid GeneralTeam = SecureChildShareWorld.GeneralTeam;
    private static readonly Guid SecureTeam = SecureChildShareWorld.SecureTeam;
    private static readonly Guid OtherBu = Guid.Parse("0b000000-0000-4000-8000-0000000000b1");
    private static readonly Guid OtherBuTeam = Guid.Parse("0b000000-0000-4000-8000-0000000000b2");
    private static readonly Guid ThirdBu = Guid.Parse("0c000000-0000-4000-8000-0000000000c1");
    private static readonly Guid ThirdBuTeam = Guid.Parse("0c000000-0000-4000-8000-0000000000c2");
    private static readonly Guid SecureFamilyTeam = Guid.Parse("d9ec0b6f-0000-4000-8000-000000000004");

    private static readonly Guid Creator = Guid.Parse("14800000-0000-4000-8000-0000000000a1");
    private static readonly Guid Assignee = Guid.Parse("14800000-0000-4000-8000-0000000000a2");
    private static readonly Guid SecondUser = Guid.Parse("14800000-0000-4000-8000-0000000000a3");
    private static readonly Guid AssigneeContact = Guid.Parse("14800000-0000-4000-8000-0000000000c1");
    private static readonly Guid Todo = Guid.Parse("14800000-0000-4000-8000-000000000101");
    private static readonly Guid Matter = Guid.Parse("14800000-0000-4000-8000-0000000000d1");
    private static readonly Guid SecureMatter = Guid.Parse("14800000-0000-4000-8000-0000000000d2");
    private static readonly Guid Document = Guid.Parse("14800000-0000-4000-8000-0000000000e1");

    /// <summary>01:00 UTC: before the nightly hour, so a scheduled run is the watermark pass alone.</summary>
    private static readonly DateTimeOffset BeforeNightly = new(2026, 10, 14, 1, 0, 0, TimeSpan.Zero);

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard()
        .Add("businessunit", OtherBu, ("name", "Litigation"))
        .Team(OtherBuTeam, OtherBu, "Litigation", isDefault: true, teamType: 0)
        .Add("businessunit", ThirdBu, ("name", "Tax"))
        .Team(ThirdBuTeam, ThirdBu, "Tax", isDefault: true, teamType: 0)
        .Team(SecureFamilyTeam, SecureChildShareWorld.SecureBu, "Secure Matter Owners", isDefault: false, teamType: 0)
        .User(Creator, SecureChildShareWorld.GeneralBu)
        .User(Assignee, OtherBu)
        .User(SecondUser, OtherBu)
        .OrdinaryRoot("sprk_matter", Matter)
        .SecureRoot("sprk_matter", SecureMatter);

    private readonly AssignedAccessTestDoubles.FakeAssignedAccessStore _links = new();
    private readonly FakeTimeProvider _clock = new(BeforeNightly);
    private readonly OwnerWebApi _webApi;

    public TodoAssigneeOwnerReconciliationJobTests() => _webApi = new OwnerWebApi(() => _world);

    // ── Watermark pass ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "148: Assigned To changed to a user in another BU — the watermark pass re-owns the To Do's Signal and DR to that BU's team")]
    public async Task AssignedToChanged_WatermarkPass_ReownsSignalAndDecisionRecord()
    {
        PersonalToDo(modified: BeforeNightly.AddMinutes(-1));
        LinkAssignee(Eligible(Assignee));
        var signal = Row("sprk_signal", GeneralTeam);
        var record = Row("sprk_decisionrecord", GeneralTeam);

        var run = await Job(writes: "true").ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        Result(run).GetProperty("fullPass").GetBoolean().Should().BeFalse("before 02:00 a scheduled run is the watermark pass");
        Result(run).GetProperty("reowned").GetInt32().Should().Be(2);
        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(OtherBuTeam));
        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(OtherBuTeam));
    }

    // ── Nightly recompute (uac-r2 F2) ──────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "148 (uac-r2 F2): the assignee's BU changes with no To Do edit — only the nightly recompute re-owns")]
    public async Task AssigneeMovesBusinessUnit_NoToDoEdit_NightlyRecomputeReowns()
    {
        PersonalToDo(modified: BeforeNightly.AddDays(-10));
        LinkAssignee(Eligible(Assignee));
        var signal = Row("sprk_signal", OtherBuTeam);
        var record = Row("sprk_decisionrecord", OtherBuTeam);
        _world.Set("systemuser", Assignee, "businessunitid", new EntityReference("businessunit", ThirdBu));
        var job = Job(writes: "true");

        var watermarkOnly = await job.ExecuteAsync(Context(), CancellationToken.None);
        Result(watermarkOnly).GetProperty("scanned").GetInt32().Should().Be(0, "no To Do changed, so the watermark pass reads no row");
        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(OtherBuTeam));

        _clock.SetUtcNow(BeforeNightly.AddHours(2)); // 03:00 UTC
        var nightly = await job.ExecuteAsync(Context(), CancellationToken.None);

        nightly.Success.Should().BeTrue(nightly.ErrorMessage);
        Result(nightly).GetProperty("fullPass").GetBoolean().Should().BeTrue();
        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(ThirdBuTeam));
        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(ThirdBuTeam));

        _clock.Advance(TimeSpan.FromMinutes(5));
        var later = await job.ExecuteAsync(Context(), CancellationToken.None);
        Result(later).GetProperty("fullPass").GetBoolean().Should().BeFalse("the day's recompute is done");
    }

    // ── No single eligible assignee → the To Do's own team ────────────────────────────────────────────────────────────

    [Theory(DisplayName = "148: no single enabled assignee (none, several, disabled) — the To Do's own team, counted per reason")]
    [InlineData("none", "fallbackNoAssignee")]
    [InlineData("several", "fallbackAmbiguous")]
    [InlineData("disabled", "fallbackDisabled")]
    public async Task NoSingleEligibleAssignee_FallsBackToTheToDosOwnTeam(string shape, string counter)
    {
        PersonalToDo(modified: BeforeNightly.AddMinutes(-1), assigned: shape != "none");
        switch (shape)
        {
            case "several": LinkAssignee(Eligible(Assignee), Eligible(SecondUser)); break;
            case "disabled": LinkAssignee(Eligible(Assignee) with { IsDisabled = true }); break;
        }

        var signal = Row("sprk_signal", OtherBuTeam);
        var record = Row("sprk_decisionrecord", OtherBuTeam);

        var run = await Job(writes: "true").ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        Result(run).GetProperty(counter).GetInt32().Should().Be(1, "one To Do, counted once");
        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(GeneralTeam), "the creator's BU team owns the To Do");
        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
    }

    // ── Live filing (uac-r2 F1, owner K2) ──────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "148 (uac-r2 F1): a To Do filed under a core now, or a row stamped with a core — never re-owned")]
    [InlineData("todo-under-matter")]
    [InlineData("row-stamped")]
    public async Task CoredNow_IsNotReowned(string shape)
    {
        PersonalToDo(modified: BeforeNightly.AddMinutes(-1));
        LinkAssignee(Eligible(Assignee));
        if (shape == "todo-under-matter")
            _world.Set("sprk_todo", Todo, "sprk_regardingmatter", new EntityReference("sprk_matter", Matter));

        // Raised while the To Do was personal; its decision context names no core (sprk_corerecordid null). Owned by a team that
        // is neither the assignee's nor the To Do's, so any answer this job could give would move it.
        var signal = Row("sprk_signal", ThirdBuTeam,
            shape == "row-stamped" ? [("sprk_regardingmatter", new EntityReference("sprk_matter", Matter))] : []);

        var run = await Job(writes: "true").ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        Result(run).GetProperty("skippedCored").GetInt32().Should().Be(1);
        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(ThirdBuTeam));
        _webApi.ConditionalWrites.Should().BeEmpty("a cored row is the re-file rule's, never this job's");
    }

    [Fact(DisplayName = "148 (D-135 K2): a To Do filed under a non-core record is not personal — its rows take the To Do's own team, never the assignee's")]
    public async Task FiledUnderNonCoreRecord_RowsTakeTheToDosOwnTeam()
    {
        PersonalToDo(modified: BeforeNightly.AddMinutes(-1));
        LinkAssignee(Eligible(Assignee));
        _world.Add("sprk_document", Document, ("owningteam", new EntityReference("team", GeneralTeam)));
        _world.Set("sprk_todo", Todo, "sprk_regardingdocument", new EntityReference("sprk_document", Document));
        // Moved to the assignee's team while the To Do was personal; the To Do has since been filed under a document.
        var signal = Row("sprk_signal", OtherBuTeam);
        var record = Row("sprk_decisionrecord", OtherBuTeam);

        var run = await Job(writes: "true").ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        Result(run).GetProperty("filedNonCore").GetInt32().Should().Be(1, "one To Do, counted once");
        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(GeneralTeam), "the creator's BU team owns the To Do");
        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
    }

    [Fact(DisplayName = "148 (GitHub #2002): the scan resolving a To Do filed under no core logs no Warning — it writes nothing against the To Do")]
    public async Task FiledUnderNonCoreRecord_ResolutionIsNotAWarning()
    {
        PersonalToDo(modified: BeforeNightly.AddMinutes(-1));
        LinkAssignee(Eligible(Assignee));
        _world.Add("sprk_document", Document, ("owningteam", new EntityReference("team", GeneralTeam)));
        _world.Set("sprk_todo", Todo, "sprk_regardingdocument", new EntityReference("sprk_document", Document));
        Row("sprk_signal", OtherBuTeam);
        var log = new CapturingResolverLogger();

        var run = await Job(writes: "true", coreLogger: log).ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        Result(run).GetProperty("filedNonCore").GetInt32().Should().Be(1, "the To Do was resolved and has no core");
        log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning, "the resolver's no-ancestor line is Debug on a read-only scan");
        log.Entries.Should().Contain(e => e.Level == LogLevel.Debug && e.Message.Contains("carries no core-ancestor stamp"));
    }

    private sealed class CapturingResolverLogger : ILogger<CoreAncestorResolver>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact(DisplayName = "148 (uac-r2 F1 reverse): a To Do un-filed since its rows were cored — re-owned; the decision context is never read")]
    public async Task UnfiledSince_RowsComeIn_DecisionContextIgnored()
    {
        PersonalToDo(modified: BeforeNightly.AddMinutes(-1));
        LinkAssignee(Eligible(Assignee));
        // Cored when raised (the matter's team; the immutable decision context names the matter), live stamp since cleared.
        var signal = Row("sprk_signal", GeneralTeam, [("sprk_corerecordid", Matter.ToString("D"))]);
        var record = Row("sprk_decisionrecord", GeneralTeam, [("sprk_corerecordid", Matter.ToString("D"))]);

        var run = await Job(writes: "true").ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(OtherBuTeam));
        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(OtherBuTeam));
    }

    // ── Secure wins (D-134 item 7) ─────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "148: a secure parent, a Secure-team owner, or a UAC Secure refusal — never written, SkippedSecure")]
    [InlineData("secure-parent")]                 // mutation target: the secure-wins guard (Expected → IsSecureOwner)
    [InlineData("secure-owned")]
    [InlineData("refused-by-reown")]              // a per-family team in the Secure BU (the job's guard, as UAC's)
    [InlineData("record-resolved-secure-signal")] // the DR's resolved Signal sits under a secure matter (uac-r2 F1)
    [InlineData("secure-parent-non-core")]        // the same guards for a To Do filed under a non-core record (D-135 K2)
    [InlineData("secure-owned-non-core")]
    public async Task SecureCases_AreNeverWritten(string variant)
    {
        PersonalToDo(modified: BeforeNightly.AddMinutes(-1));
        LinkAssignee(Eligible(Assignee));
        var shape = variant.Replace("-non-core", string.Empty, StringComparison.Ordinal);
        if (shape != variant)
        {
            _world.Add("sprk_document", Document, ("owningteam", new EntityReference("team", GeneralTeam)));
            _world.Set("sprk_todo", Todo, "sprk_regardingdocument", new EntityReference("sprk_document", Document));
        }

        var table = shape == "record-resolved-secure-signal" ? "sprk_decisionrecord" : "sprk_signal";
        var owner = shape switch { "secure-owned" => SecureTeam, "refused-by-reown" => SecureFamilyTeam, _ => GeneralTeam };
        var id = Row(table, owner,
            shape == "secure-parent" ? [("sprk_matter", new EntityReference("sprk_matter", SecureMatter))] : []);
        if (shape == "record-resolved-secure-signal")
        {
            // An also-resolved Signal about the secure matter, linked to this record and listed in its sprk_steps.
            var resolved = Guid.NewGuid();
            _world.Add("sprk_signal", resolved, ("owningteam", new EntityReference("team", SecureTeam)),
                ("sprk_decisionrecord", new EntityReference("sprk_decisionrecord", id)),
                ("sprk_regardingrecordid", SecureMatter.ToString("D")),
                ("sprk_regardingmatter", new EntityReference("sprk_matter", SecureMatter)),
                ("sprk_matter", new EntityReference("sprk_matter", SecureMatter)), ("versionnumber", 1L));
            _world.Set("sprk_decisionrecord", id, "sprk_steps", Steps(resolved));
        }

        var run = await Job(writes: "true").ExecuteAsync(Context(), CancellationToken.None);

        Result(run).GetProperty("skippedSecure").GetInt32().Should().Be(1);
        Result(run).GetProperty("reowned").GetInt32().Should().Be(0);
        _world.OwnerOf(table, id).Should().Be(DataversePrincipalRef.Team(owner));
        _webApi.ConditionalWrites.Should().BeEmpty("nothing secure is ever written");
    }

    [Fact(DisplayName = "148 (uac-r2 K1): a row owned by a per-family team in the Secure BU — report-only does not list it as a would-be re-own")]
    public async Task SecureBusinessUnitTeamRow_IsSkipped_NotListedStale()
    {
        PersonalToDo(modified: BeforeNightly.AddMinutes(-1));
        LinkAssignee(Eligible(Assignee));
        var signal = Row("sprk_signal", SecureFamilyTeam); // not the named Secure owner team; its business unit is the Secure one

        var run = await Job(writes: null).ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        Result(run).GetProperty("skippedSecure").GetInt32().Should().Be(1);
        Result(run).GetProperty("stale").GetInt32().Should().Be(0, "ReownOrdinaryRowAsync would refuse it (RefusedSecure)");
        Result(run).GetProperty("sampleStale").GetArrayLength().Should().Be(0);
        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(SecureFamilyTeam));
    }

    [Fact(DisplayName = "148 (uac-r2 K4): the assignee's business-unit team is refused (assignee in the Secure BU) — not written, run partial with a reason")]
    public async Task AssigneeTeamRefused_RunIsPartial_WithAReason()
    {
        PersonalToDo(modified: BeforeNightly.AddMinutes(-1));
        LinkAssignee(Eligible(Assignee));
        _world.Set("systemuser", Assignee, "businessunitid", new EntityReference("businessunit", SecureChildShareWorld.SecureBu));
        var signal = Row("sprk_signal", GeneralTeam);

        var run = await Job(writes: "true").ExecuteAsync(Context(), CancellationToken.None);

        Result(run).GetProperty("assigneeTeamRefusals").GetInt32().Should().Be(1);
        Result(run).GetProperty("skippedRefused").GetInt32().Should().Be(1);
        Result(run).GetProperty("status").GetString().Should().Be(TodoAssigneeOwnerReconciliationJob.StatusPartial);
        run.ErrorMessage.Should().Contain("assignee's business-unit team was refused");
        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _webApi.ConditionalWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "148 (#2072/#1576): a DISABLED assignee systemuser resolves to no user — its business-unit team refused, not written, run partial")]
    public async Task DisabledAssigneeSystemUser_IsRefused_NotMoved()
    {
        PersonalToDo(modified: BeforeNightly.AddMinutes(-1));
        LinkAssignee(Eligible(Assignee)); // the link says enabled; the systemuser row itself is disabled
        _world.Set("systemuser", Assignee, "isdisabled", true);
        var signal = Row("sprk_signal", GeneralTeam);

        var run = await Job(writes: "true").ExecuteAsync(Context(), CancellationToken.None);

        Result(run).GetProperty("assigneeTeamRefusals").GetInt32().Should().Be(1);
        Result(run).GetProperty("status").GetString().Should().Be(TodoAssigneeOwnerReconciliationJob.StatusPartial);
        run.ErrorMessage.Should().Contain("assignee's business-unit team was refused");
        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(GeneralTeam), "a disabled user's BU never owns the To Do's rows");
        _webApi.ConditionalWrites.Should().BeEmpty();
    }

    [Theory(DisplayName = "148: the ownership resolver refuses the row's parents — never written, SkippedRefused; a configuration refusal makes the run partial")]
    [InlineData("parent-missing", TodoAssigneeOwnerReconciliationJob.StatusOk)]          // ParentUnresolved: one row's data
    [InlineData("secure-unit-ambiguous", TodoAssigneeOwnerReconciliationJob.StatusPartial)] // the tenant's configuration
    public async Task RefusedParents_AreNeverWritten(string shape, string status)
    {
        PersonalToDo(modified: BeforeNightly.AddMinutes(-1));
        LinkAssignee(Eligible(Assignee));
        var signal = Row("sprk_signal", GeneralTeam,
            shape == "parent-missing" ? [("sprk_matter", new EntityReference("sprk_matter", Guid.NewGuid()))] : []);
        if (shape == "secure-unit-ambiguous")
            _world.Add("businessunit", Guid.NewGuid(), ("name", SecureChildShareWorld.SecureBuName));

        var run = await Job(writes: "true").ExecuteAsync(Context(), CancellationToken.None);

        Result(run).GetProperty("skippedRefused").GetInt32().Should().Be(1);
        Result(run).GetProperty("status").GetString().Should().Be(status);
        Result(run).GetProperty("configurationRefusals").GetInt32().Should().Be(shape == "secure-unit-ambiguous" ? 1 : 0);
        if (status != TodoAssigneeOwnerReconciliationJob.StatusOk)
            run.ErrorMessage.Should().Contain("refused by configuration", "a run that is not a success says why");
        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _webApi.ConditionalWrites.Should().BeEmpty();
    }

    // ── Already right ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "148: a row already owned by the expected team — AlreadyApplied, no re-own call")]
    public async Task AlreadyOwnedByTheExpectedTeam_IsNotWritten()
    {
        PersonalToDo(modified: BeforeNightly.AddMinutes(-1));
        LinkAssignee(Eligible(Assignee));
        Row("sprk_signal", OtherBuTeam);
        Row("sprk_decisionrecord", OtherBuTeam);

        var run = await Job(writes: "true").ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        Result(run).GetProperty("alreadyApplied").GetInt32().Should().Be(2);
        Result(run).GetProperty("stale").GetInt32().Should().Be(0);
        _webApi.ConditionalWrites.Should().BeEmpty();
    }

    // ── UAC's typed outcomes ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "148: NotFound → SkippedGone, Conflict → SkippedConflict; neither fails the run, and the Conflict row is re-evaluated next run")]
    public async Task GoneAndConflict_DoNotFailTheRun_ConflictIsRetriedNextRun()
    {
        PersonalToDo(modified: BeforeNightly.AddMinutes(-10));
        LinkAssignee(Eligible(Assignee));
        var gone = Row("sprk_signal", GeneralTeam);
        var conflicted = Row("sprk_decisionrecord", GeneralTeam);
        _webApi.GoneOn.Add(gone);
        _webApi.ConflictOn.Add(conflicted);
        var job = Job(writes: "true");

        var first = await job.ExecuteAsync(Context(), CancellationToken.None);

        first.Success.Should().BeTrue(first.ErrorMessage);
        Result(first).GetProperty("skippedGone").GetInt32().Should().Be(1);
        Result(first).GetProperty("skippedConflict").GetInt32().Should().Be(1);
        Result(first).GetProperty("repairFailures").GetInt32().Should().Be(0);
        _world.OwnerOf("sprk_decisionrecord", conflicted).Should().Be(DataversePrincipalRef.Team(GeneralTeam));

        _world.Remove("sprk_signal", gone);
        _webApi.ConflictOn.Clear();
        _clock.Advance(TimeSpan.FromMinutes(5)); // the To Do is now older than the watermark: only the carry brings it back
        var second = await job.ExecuteAsync(Context(), CancellationToken.None);

        second.Success.Should().BeTrue(second.ErrorMessage);
        Result(second).GetProperty("recentToDos").GetInt32().Should().Be(0);
        Result(second).GetProperty("carriedToDos").GetInt32().Should().Be(1);
        _world.OwnerOf("sprk_decisionrecord", conflicted).Should().Be(DataversePrincipalRef.Team(OtherBuTeam));
    }

    // Beyond the POML's list (one-line justification): the brief binds "0x80040299 is a defined outcome, never a retry storm".
    [Fact(DisplayName = "148: a privilege refusal (the Web API's 403, 0x80040299) is AccessDenied and held back — one attempt, not one per tick")]
    public async Task AccessDenied_IsHeldBack_NotRetriedEveryTick()
    {
        PersonalToDo(modified: BeforeNightly.AddMinutes(-1));
        LinkAssignee(Eligible(Assignee));
        var signal = Row("sprk_signal", GeneralTeam);
        _webApi.DeniedOn.Add(signal);
        var job = Job(writes: "true");

        var first = await job.ExecuteAsync(Context(), CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(1)); // the To Do is still inside the watermark overlap: listed again
        var second = await job.ExecuteAsync(Context(), CancellationToken.None);

        Result(first).GetProperty("accessDenied").GetInt32().Should().Be(1);
        Result(first).GetProperty("status").GetString().Should().Be(TodoAssigneeOwnerReconciliationJob.StatusPartial);
        Result(second).GetProperty("accessDeniedHeld").GetInt32().Should().Be(1);
        _webApi.ConditionalWrites.Should().ContainSingle("the held row is not attempted again");
    }

    // ── Report-only ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "148: report-only by default (absent, false, unparseable) — no write, the would-be re-owns listed in sampleStale")]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("yes")]
    public async Task ReportOnly_WritesNothing_AndListsTheWouldBeReowns(string? writes)
    {
        PersonalToDo(modified: BeforeNightly.AddMinutes(-1));
        LinkAssignee(Eligible(Assignee));
        var signal = Row("sprk_signal", GeneralTeam);
        var record = Row("sprk_decisionrecord", GeneralTeam);

        var run = await Job(writes).ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeTrue(run.ErrorMessage);
        Result(run).GetProperty("mode").GetString().Should().Be("report-only");
        Result(run).GetProperty("stale").GetInt32().Should().Be(2);
        Result(run).GetProperty("sampleStale").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(
            $"sprk_signal:{signal:D}:{GeneralTeam:D}->{OtherBuTeam:D}",
            $"sprk_decisionrecord:{record:D}:{GeneralTeam:D}->{OtherBuTeam:D}");
        _webApi.ConditionalWrites.Should().BeEmpty();
        _world.OwnerWrites.Should().BeEmpty();
    }

    // ── Faults ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "148: a scan or resolve fault — run error/partial, never '0 stale', and no write for that To Do")]
    [InlineData("todo-listing", TodoAssigneeOwnerReconciliationJob.StatusError)]
    [InlineData("row-scan", TodoAssigneeOwnerReconciliationJob.StatusError)]
    [InlineData("assignee-read", TodoAssigneeOwnerReconciliationJob.StatusPartial)]
    [InlineData("parent-read", TodoAssigneeOwnerReconciliationJob.StatusPartial)] // the ownership resolver's parent read throws
    public async Task ScanOrResolveFault_IsNeverAClean_AndWritesNothing(string fault, string status)
    {
        PersonalToDo(modified: BeforeNightly.AddMinutes(-1));
        LinkAssignee(Eligible(Assignee));
        (string, object)[] parent = fault == "parent-read" ? [("sprk_matter", new EntityReference("sprk_matter", Matter))] : [];
        var signal = Row("sprk_signal", GeneralTeam, parent);
        var record = Row("sprk_decisionrecord", GeneralTeam, parent);
        switch (fault)
        {
            case "todo-listing": _world.FailingQueriesOf("sprk_todo"); break;
            case "row-scan": _world.FailingQueriesOf("sprk_signal"); break;
            case "assignee-read": _links.FailLinkRead = true; break;
            case "parent-read": _world.FailingQueriesOf("sprk_matter"); break;
        }

        var run = await Job(writes: "true").ExecuteAsync(Context(), CancellationToken.None);

        run.Success.Should().BeFalse("an unread or undecided To Do is not a clean run");
        run.ErrorMessage.Should().NotBeNullOrWhiteSpace("a run that is not a success says why (ADR-036)");
        Result(run).GetProperty("status").GetString().Should().Be(status);
        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _webApi.ConditionalWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "148: a To Do whose core cannot be derived for a data reason — refused, never written, not carried")]
    public async Task PermanentCoreRefusal_IsRefused_NotCarried()
    {
        PersonalToDo(modified: BeforeNightly.AddMinutes(-1));
        LinkAssignee(Eligible(Assignee));
        _world.Add("sprk_document", Document, ("owningteam", new EntityReference("team", GeneralTeam)));
        _world.Set("sprk_todo", Todo, "sprk_regardingdocument", new EntityReference("sprk_document", Document));
        var signal = Row("sprk_signal", OtherBuTeam);
        var job = Job(writes: "true", coreReadFindsNoRow: true); // CoreAncestorResult.IsPermanent

        var first = await job.ExecuteAsync(Context(), CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(5));
        var second = await job.ExecuteAsync(Context(), CancellationToken.None);

        Result(first).GetProperty("skippedRefused").GetInt32().Should().Be(1);
        Result(first).GetProperty("unverified").GetInt32().Should().Be(0, "a data condition is not a fault to retry");
        Result(second).GetProperty("carriedToDos").GetInt32().Should().Be(0, "a permanent refusal is not carried");
        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(OtherBuTeam));
        _webApi.ConditionalWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "148: a To Do's rows past the page bound — the rows read are decided, the run is partial, nothing is carried")]
    public async Task PageBound_DecidesTheRowsRead_RunIsPartial()
    {
        PersonalToDo(modified: BeforeNightly.AddMinutes(-1));
        LinkAssignee(Eligible(Assignee));
        var signals = new[] { Row("sprk_signal", GeneralTeam), Row("sprk_signal", GeneralTeam) };
        var job = Job(writes: "true", pageSize: 1, maxPages: 1);

        var run = await job.ExecuteAsync(Context(), CancellationToken.None);

        Result(run).GetProperty("truncated").GetBoolean().Should().BeTrue();
        Result(run).GetProperty("status").GetString().Should().Be(TodoAssigneeOwnerReconciliationJob.StatusPartial);
        Result(run).GetProperty("reowned").GetInt32().Should().Be(1, "the row that was read is decided, not dropped with its batch");
        signals.Count(s => _world.OwnerOf("sprk_signal", s) == DataversePrincipalRef.Team(OtherBuTeam)).Should().Be(1);
        _clock.Advance(TimeSpan.FromMinutes(5));
        var next = await job.ExecuteAsync(Context(), CancellationToken.None);
        Result(next).GetProperty("carriedToDos").GetInt32().Should().Be(0, "the rest waits for the nightly recompute");
    }

    [Fact(DisplayName = "148: the Signal table's columns cannot be probed — the nightly pass never writes a DR past its resolved Signals (194b R A6: read by id from its sprk_steps, so a secure one is still asked)")]
    public async Task SignalProbeFault_DecisionRecordIsNotWrittenPastItsResolvedSignals()
    {
        PersonalToDo(modified: BeforeNightly.AddDays(-10)); // outside the watermark: only the nightly pass reads its record
        LinkAssignee(Eligible(Assignee));
        var record = Row("sprk_decisionrecord", GeneralTeam);
        var resolved = Guid.NewGuid();
        _world.Add("sprk_signal", resolved, ("owningteam", new EntityReference("team", SecureTeam)),
            ("sprk_decisionrecord", new EntityReference("sprk_decisionrecord", record)),
            ("sprk_regardingrecordid", SecureMatter.ToString("D")),
            ("sprk_regardingmatter", new EntityReference("sprk_matter", SecureMatter)),
            ("sprk_matter", new EntityReference("sprk_matter", SecureMatter)), ("versionnumber", 1L));
        _world.Set("sprk_decisionrecord", record, "sprk_steps", Steps(resolved));
        _clock.SetUtcNow(BeforeNightly.AddHours(2)); // 03:00 UTC

        var run = await Job(writes: "true", failingProbeOf: "sprk_signal").ExecuteAsync(Context(), CancellationToken.None);

        Result(run).GetProperty("fullPass").GetBoolean().Should().BeTrue();
        Result(run).GetProperty("status").GetString().Should().Be(TodoAssigneeOwnerReconciliationJob.StatusError);
        Result(run).GetProperty("skippedSecure").GetInt32().Should().Be(1, "the resolved Signal is read by id, without the probe");
        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _webApi.ConditionalWrites.Should().BeEmpty();
    }

    // ── UAC task 194b fix round R (A6): the Signals a Decision Record resolved, from its sprk_steps ─────────────────────

    [Fact(DisplayName = "194b R (A6) must fire: a Signal closed during the review (listed in the record's sprk_steps, not linked to it) has a SECURE subject — the job never re-owns the Decision Record to an ordinary team: SkippedSecure")]
    public async Task ClosedSignalWithASecureSubject_DecisionRecordIsSkippedSecure_NeverReownedOrdinary()
    {
        PersonalToDo(modified: BeforeNightly.AddMinutes(-1));
        LinkAssignee(Eligible(Assignee));
        var record = Row("sprk_decisionrecord", GeneralTeam);
        _world.Set("sprk_decisionrecord", record, "sprk_steps", Steps(ClosedSignalAbout(SecureMatter)));

        var run = await Job(writes: "true").ExecuteAsync(Context(), CancellationToken.None);

        Result(run).GetProperty("skippedSecure").GetInt32().Should().Be(1);
        Result(run).GetProperty("reowned").GetInt32().Should().Be(0);
        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _webApi.ConditionalWrites.Should().BeEmpty("a Decision Record under a secure subject is never moved to an ordinary team");
    }

    [Fact(DisplayName = "194b R (A6) must not fire: a Signal closed during the review with an ORDINARY subject — the Decision Record follows the assignee")]
    public async Task ClosedSignalWithAnOrdinarySubject_DecisionRecordFollowsTheAssignee()
    {
        PersonalToDo(modified: BeforeNightly.AddMinutes(-1));
        LinkAssignee(Eligible(Assignee));
        var record = Row("sprk_decisionrecord", GeneralTeam);
        _world.Set("sprk_decisionrecord", record, "sprk_steps", Steps(ClosedSignalAbout(Matter)));

        var run = await Job(writes: "true").ExecuteAsync(Context(), CancellationToken.None);

        Result(run).GetProperty("reowned").GetInt32().Should().Be(1);
        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(OtherBuTeam));
    }

    [Theory(DisplayName = "194b R (A6) must fire: an INCOMPLETE question (a listed Signal gone, or no resolvedSignalIds) never writes an ordinary owner — not decided, the run partial; a secure subject in the known part is still SkippedSecure")]
    [InlineData("gone", false)]
    [InlineData("no-list", false)]
    [InlineData("gone", true)]
    public async Task IncompleteQuestion_NeverWritesAnOrdinaryOwner(string why, bool knownSecure)
    {
        PersonalToDo(modified: BeforeNightly.AddMinutes(-1));
        LinkAssignee(Eligible(Assignee));
        var record = Row("sprk_decisionrecord", GeneralTeam);
        var listed = new List<Guid> { Guid.NewGuid() }; // never added: gone
        if (knownSecure)
            listed.Add(ClosedSignalAbout(SecureMatter));
        _world.Set("sprk_decisionrecord", record, "sprk_steps",
            why == "no-list" ? "{\"reviewId\":\"x\"}" : Steps(listed.ToArray()));

        var run = await Job(writes: "true").ExecuteAsync(Context(), CancellationToken.None);

        Result(run).GetProperty("reowned").GetInt32().Should().Be(0);
        if (knownSecure)
        {
            Result(run).GetProperty("skippedSecure").GetInt32().Should().Be(1);
        }
        else
        {
            Result(run).GetProperty("unverified").GetInt32().Should().Be(1, "an ordinary owner is never written on part of the question");
            Result(run).GetProperty("status").GetString().Should().NotBe(TodoAssigneeOwnerReconciliationJob.StatusOk);
        }

        _world.OwnerOf("sprk_decisionrecord", record).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _webApi.ConditionalWrites.Should().BeEmpty();
    }

    // =====================================================================================================================
    // Fixtures
    // =====================================================================================================================

    /// <summary>A personal To Do (filed under nothing), owned by its creator in the general BU, assigned to the contact.</summary>
    private void PersonalToDo(DateTimeOffset modified, bool assigned = true)
    {
        _world.Add("sprk_todo", Todo, ("owninguser", new EntityReference("systemuser", Creator)));
        if (assigned)
            _world.Set("sprk_todo", Todo, "sprk_assignedto", new EntityReference("contact", AssigneeContact));
        _world.Modified("sprk_todo", Todo, modified.UtcDateTime);
    }

    private void LinkAssignee(params AssignedLinkCandidate[] users) => _links.UsersByLink[AssigneeContact] = users.ToList();

    private static AssignedLinkCandidate Eligible(Guid user) =>
        new(user, AssigneeContact, Oid: null, IsDisabled: false, AccessMode: 0, ApplicationId: null, IsExternal: false);

    /// <summary>A Signal or Decision Record about the To Do, owned by <paramref name="team"/>. A Decision Record carries the
    /// <c>sprk_steps</c> its writer writes, listing no other resolved Signal unless a test sets one (194b R, A6).</summary>
    private Guid Row(string table, Guid team, (string Column, object Value)[]? extra = null)
    {
        var id = Guid.NewGuid();
        var steps = table == "sprk_decisionrecord" ? new (string, object)[] { ("sprk_steps", Steps()) } : [];
        _world.Add(table, id,
            new (string, object)[]
            {
                ("sprk_regardingtodo", new EntityReference("sprk_todo", Todo)),
                ("owningteam", new EntityReference("team", team)),
                ("versionnumber", 1L),
            }.Concat(steps).Concat(extra ?? []).ToArray());
        return id;
    }

    /// <summary>A Decision Record's <c>sprk_steps</c> listing <paramref name="resolvedSignals"/> (DecisionRecordWriter's shape).</summary>
    private static string Steps(params Guid[] resolvedSignals) => System.Text.Json.JsonSerializer.Serialize(new
    {
        reviewId = Guid.NewGuid().ToString("D"),
        resolvedSignalIds = resolvedSignals.Select(s => s.ToString("D")),
        steps = Array.Empty<object>(),
    });

    /// <summary>A Signal about matter <paramref name="matter"/>, closed during the review: resolved and NOT linked to any
    /// record (no <c>sprk_decisionrecord</c>) — only a record's <c>sprk_steps</c> names it.</summary>
    private Guid ClosedSignalAbout(Guid matter)
    {
        var signal = Guid.NewGuid();
        _world.Add("sprk_signal", signal,
            ("owningteam", new EntityReference("team", GeneralTeam)),
            ("sprk_signalstatus", new OptionSetValue(SignalWriter.SignalStatusResolved)),
            ("sprk_regardingrecordid", matter.ToString("D")),
            ("sprk_regardingmatter", new EntityReference("sprk_matter", matter)),
            ("versionnumber", 1L));
        return signal;
    }

    private TodoAssigneeOwnerReconciliationJob Job(
        string? writes, string? failingProbeOf = null, bool coreReadFindsNoRow = false, int? pageSize = null, int? maxPages = null,
        ILogger<CoreAncestorResolver>? coreLogger = null)
    {
        var entities = SecureChildShareWorld.EntitiesOver(() => _world);
        entities
            .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string[] columns, CancellationToken _) =>
            {
                // The core-ancestor read of the To Do (its root columns, not the assignee helper's read): no row, a DATA
                // condition the resolver reports as permanent (CoreAncestorResult.IsPermanent).
                if (coreReadFindsNoRow && table == "sprk_todo" && !columns.Contains("sprk_assignedto"))
                    return Task.FromResult<Entity>(null!);
                var query = new QueryExpression(table) { ColumnSet = new ColumnSet(columns) };
                query.Criteria.AddCondition(table + "id", ConditionOperator.Equal, id);
                return _world.Answer(query).Entities.SingleOrDefault() is { } row
                    ? Task.FromResult(row)
                    : throw new InvalidOperationException($"Test: {table} {id} does not exist.");
            });

        var configuration = SecureChildShareWorld.Configuration();
        var ownership = new RecordOwnershipResolver(entities.Object, configuration, NullLogger<RecordOwnershipResolver>.Instance);
        var synchronizer = new SecureChildShareSynchronizer(entities.Object, new FakeRecordShareTable(),
            SecureChildShareWorld.NobodyWalled(), configuration, NullLogger<SecureChildShareSynchronizer>.Instance);
        var reconciler = new SecureChildReconciler(entities.Object, ownership, synchronizer, _webApi, configuration,
            NullLogger<SecureChildReconciler>.Instance, new Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld().Sharing);
        var probe = SecureChildShareWorld.ColumnProbe();
        CoreAncestorResolver.EntityColumnProbe columns = (table, ct) => table == failingProbeOf
            ? throw new InvalidOperationException($"Test: the column metadata of {table} is unavailable.")
            : probe(table, ct);
        var cores = new CoreAncestorResolver(entities.Object, columns, coreLogger ?? NullLogger<CoreAncestorResolver>.Instance);

        var services = new ServiceCollection();
        services.AddSingleton(entities.Object);
        services.AddScoped<AssignedAccessStore>(_ => _links);
        services.AddScoped(_ => reconciler);
        services.AddSingleton<IIdempotencyService>(new IdempotencyService(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<IdempotencyService>.Instance));
        services.AddSingleton<IDistributedCache>(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));

        var settings = new Dictionary<string, string?>();
        if (writes is not null)
            settings[TodoAssigneeOwnerReconciliationJob.WritesEnabledConfigKey] = writes;

        var job = new TodoAssigneeOwnerReconciliationJob(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), cores, ownership, _clock,
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            NullLogger<TodoAssigneeOwnerReconciliationJob>.Instance)
        {
            ScanPageSize = pageSize ?? TodoAssigneeOwnerReconciliationJob.PageSize,
            ScanMaxPages = maxPages ?? TodoAssigneeOwnerReconciliationJob.MaxPages,
        };
        return job;
    }

    private static JobRunContext Context() =>
        new(Guid.NewGuid(), "test-correlation", JobRunTrigger.Scheduled, new Dictionary<string, object>());

    private static JsonElement Result(JobRunResult run) => JsonDocument.Parse(run.ResultJson!).RootElement;

    /// <summary>
    /// The reconciler's conditional owner PATCH over the world (uac-r2's test double, plus three stub outcomes): a row in
    /// <see cref="GoneOn"/> answers 404 (NotFound), in <see cref="ConflictOn"/> 412 (Conflict), in <see cref="DeniedOn"/>
    /// Dataverse's privilege refusal (nothing applied; the read-back then reports Failed with the fault text).
    /// </summary>
    private sealed class OwnerWebApi : DataverseWebApiClient
    {
        private readonly Func<SecureChildShareWorld> _world;

        public OwnerWebApi(Func<SecureChildShareWorld> world)
            : base(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com" })
                    .Build(),
                NullLogger<DataverseWebApiClient>.Instance,
                NoCredential.Instance)
            => _world = world;

        public List<(string EntitySet, Guid Id, string ETag)> ConditionalWrites { get; } = new();
        public HashSet<Guid> GoneOn { get; } = new();
        public HashSet<Guid> ConflictOn { get; } = new();
        public HashSet<Guid> DeniedOn { get; } = new();

        public override Task UpdateIfMatchAsync(
            string entitySetName, Guid id, object entity, string etag, CancellationToken cancellationToken = default)
        {
            ConditionalWrites.Add((entitySetName, id, etag));
            var table = SecureChildLineage.Children.Values.Single(t => t.EntitySet == entitySetName).LogicalName;
            if (GoneOn.Contains(id) || !_world().Has(table, id))
                throw new KeyNotFoundException($"{entitySetName}({id}) was not found.");
            if (ConflictOn.Contains(id) || etag != $"W/\"{_world().ValueOf<long>(table, id, "versionnumber")}\"")
                throw new System.Data.DBConcurrencyException($"{entitySetName}({id}) changed since it was read.");
            if (DeniedOn.Contains(id))
            {
                // What the real client throws: UpdateIfMatchAsync ends in EnsureSuccessStatusCode, so the body's 0x80040299 is lost.
                throw new HttpRequestException("Response status code does not indicate success: 403 (Forbidden).", null,
                    System.Net.HttpStatusCode.Forbidden);
            }

            var fields = (IDictionary<string, object>)entity;
            var bind = (string)fields["ownerid@odata.bind"];
            var team = Guid.Parse(bind["/teams(".Length..^1]);
            _world().Update(table, id, new Dictionary<string, object> { ["ownerid"] = new EntityReference("team", team) });
            _world().Set(table, id, "versionnumber", _world().ValueOf<long>(table, id, "versionnumber") + 1);
            return Task.CompletedTask;
        }
    }

    private sealed class NoCredential : TokenCredential
    {
        public static readonly NoCredential Instance = new();

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException("This double issues no HTTP.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException("This double issues no HTTP.");
    }
}
