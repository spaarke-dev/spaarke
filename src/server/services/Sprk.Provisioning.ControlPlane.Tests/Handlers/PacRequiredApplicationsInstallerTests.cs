// -----------------------------------------------------------------------------
// PacRequiredApplicationsInstallerTests.cs
//
// HANDLER-07 LIVE-IMPL COVERAGE (Wave 2.5 pre-dispatch remediation
// 2026-08-27 — replaces the Wave 2 log-and-return-Success scaffold with the
// real `pac application install` + `pac application list` shell-outs).
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live pac CLI / no real
//   System.Diagnostics.Process spawn. A fake <see cref="IProcessRunner"/>
//   feeds the installer canned exit-codes + stdout so every failure mode is
//   deterministic. Live-Dataverse coverage belongs in env-guarded smoke
//   tests (F13 verification is: fresh Production env lacking
//   msft_PowerBI_Anchor → installer shells `pac application install` →
//   returns Success after simulated ~6min poll → H6 proceeds to
//   CanonicalSolutionCatalog resolve without MissingDependency; that path
//   is exercised end-to-end at the operator level via the
//   /provision-environment skill, not at CI).
//
// COVERAGE (F13 verbatim + punchlist HANDLER-07 proposed_fix + task-prompt tests):
//   T1  Happy path — install returns 0, list confirms on FIRST poll →
//       Success + `pac application install` invoked with exact argv
//       (--application-name / --environment).
//   T2  Happy path with delayed visibility — install returns 0, list
//       returns "not present" for 2 polls then "present" on 3rd poll →
//       Success (verifies the defense-in-depth poll loop for the rare
//       case where pac exits before Dataverse's async provisioning
//       settles). Task-prompt "(a) happy path completion after 3 polls".
//   T3  Poll-loop timeout — install returns 0, list NEVER returns present
//       → Failure with "did not appear ... polled N time(s)" diagnostic.
//       Task-prompt "(b) timeout after 10min" (compressed to ms via internal
//       ctor overload for fast tests).
//   T4  Install non-zero exit ("install error mid-poll") — pac install
//       returns exit 1 with a diagnostic-carrying stderr → Failure with
//       "exited 1" + stderr surfaced + list NOT called. Task-prompt
//       "(c) install error mid-poll".
//   T5  IProcessRunner throws TimeoutException on install (pac ran past
//       the per-app deadline mid-install) → Failure with per-app deadline
//       mentioned + operator-remediation hint + list NOT called.
//   T6  IProcessRunner throws InvalidOperationException on install (pac
//       binary not on PATH) → Failure with PATH-remediation hint + list
//       NOT called.
//   T7  Multi-app manifest — 2 apps, both need install; first fails
//       (exit 1) → Failure short-circuits before second app is touched
//       (regression guard for the per-manifest first-failure semantics).
//   T8  Empty manifest — installer returns Success without invoking pac
//       (defensive edge case; F13 canonical manifest is always non-empty
//       but the seam must tolerate empty gracefully).
//   T9  ContainsApp theory — canonical pac tabular shapes: header row,
//       dash separator, "Installed" state line, ordinal-ignore-case name
//       match, empty stdout, name-not-present.
//   T10 List-call TimeoutException mid-poll is inconclusive (not fatal)
//       — installer keeps polling until either the app appears or the
//       per-app deadline is reached (regression guard for the "individual
//       list-call timeout ≠ overall failure" rule in the SKILL header).
//   T11 F13 canonical value regression guard —
//       StaticRequiredApplicationsManifest carries msft_PowerBI_Anchor
//       (silent removal here would re-expose the
//       "MissingDependency: powerbimashupparameter" failure this handler
//       exists to eliminate).
//   T12 Per-list-call non-zero exit is inconclusive (auth revoked mid-run,
//       env torn down between install + list — deliberately treated as a
//       "keep polling" case rather than a hard failure per the SKILL
//       header's per-call classification).
//
// SF-N (fake-runner argv capture): every test that asserts on the shell-
// out argv verifies the pac command shape verbatim to catch any silent
// drift in the arg-name convention (`--application-name` vs `--name`,
// `--environment` vs `--url`) — the punchlist HANDLER-07 fix rides on the
// exact `--application-name / --environment` pair.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;
using Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class PacRequiredApplicationsInstallerTests
{
    private const string DvUrl = "https://acme.crm.dynamics.com/";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string ClientId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string ClientSecret = "test-secret-placeholder";
    private const string PowerBiAnchor = "msft_PowerBI_Anchor";  // F13 verbatim
    private const string SecondApp = "msft_SomeOtherApp";

    private static readonly TimeSpan FastPerAppDeadline = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FastPollInterval = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan FastListTimeout = TimeSpan.FromMilliseconds(200);

    // ---------- T1 happy path: install returns 0, list confirms on first poll ----------

    [Fact]
    public async Task T1_HappyPath_InstallReturnsZero_ListConfirmsFirstPoll_Success_WithExactArgv()
    {
        var runner = FakeProcessRunner.WithScript(
            installExitCode: 0,
            listResponses: new (int, string)[] { (0, BuildListStdoutWithApp(PowerBiAnchor)) });
        var installer = BuildInstaller(runner);

        var outcome = await installer.EnsureInstalledAsync(BuildRequest(), CancellationToken.None);

        var success = outcome.Should().BeOfType<RequiredApplicationsInstallOutcome.Success>().Subject;
        success.InstalledOrAlreadyPresent.Should().ContainSingle().Which.Should().Be(PowerBiAnchor);

        runner.Calls.Should().HaveCount(2, "1 install + 1 list");
        // Punchlist HANDLER-07 proposed_fix wire-shape guard:
        //   pac application install --application-name msft_PowerBI_Anchor --environment <url>
        runner.Calls[0].Args.Should().ContainInOrder(
            "application", "install",
            "--application-name", PowerBiAnchor,
            "--environment", DvUrl);
        runner.Calls[1].Args.Should().ContainInOrder(
            "application", "list",
            "--environment", DvUrl);
    }

    // ---------- T2 happy path with delayed visibility (3-poll scenario per task-prompt) ----------

    [Fact]
    public async Task T2_HappyPath_ListDelayedThreePolls_Success()
    {
        // pac install returned 0 (Dataverse accepted install request); Dataverse
        // async provisioning is still visible-lagging for 2 polls, then confirms.
        // Simulates the rare case where pac exits before list catches up.
        var listResponses = new (int, string)[]
        {
            (0, BuildListStdoutWithoutApp()),       // poll 1: not visible yet
            (0, BuildListStdoutWithoutApp()),       // poll 2: still not visible
            (0, BuildListStdoutWithApp(PowerBiAnchor)),  // poll 3: confirmed
        };
        var runner = FakeProcessRunner.WithScript(
            installExitCode: 0,
            listResponses: listResponses);
        var installer = BuildInstaller(runner);

        var outcome = await installer.EnsureInstalledAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<RequiredApplicationsInstallOutcome.Success>();
        runner.Calls.Should().HaveCount(4, "1 install + 3 list polls until the app appeared");
        runner.Calls.Skip(1).All(c => c.Args.Contains("list")).Should().BeTrue(
            "polls 1-3 are all list calls (the initial install is call 0)");
    }

    // ---------- T3 timeout: install returns 0 but list never confirms ----------

    [Fact]
    public async Task T3_InstallReturnsZero_ListNeverConfirms_DeadlineExceeded_Failure()
    {
        // list ALWAYS returns "not present" — installer must exhaust its
        // per-app deadline and return Failure. Uses a tight deadline so the
        // test finishes in ~1 s of real time.
        var runner = FakeProcessRunner.WithScript(
            installExitCode: 0,
            listResponses: new (int, string)[] { (0, BuildListStdoutWithoutApp()) },
            repeatLastListResponse: true);
        var installer = BuildInstaller(runner);

        var outcome = await installer.EnsureInstalledAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<RequiredApplicationsInstallOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain(PowerBiAnchor);
        failure.Diagnostic.Should().Contain("did not appear",
            "F13 diagnostic operator will see when Dataverse async provisioning stalls");
        failure.Diagnostic.Should().Contain(DvUrl,
            "operator needs the customer env URL to run the manual verify command");
        failure.Diagnostic.Should().Contain("polled",
            "poll count in diagnostic separates 'never confirmed' from 'install itself failed'");

        runner.Calls.Should().HaveCountGreaterThan(1, "1 install + N list polls");
        runner.Calls[0].Args.Should().Contain("install");
        runner.Calls.Skip(1).All(c => c.Args.Contains("list")).Should().BeTrue();
    }

    // ---------- T4 install error mid-poll (pac install exits non-zero) ----------

    [Fact]
    public async Task T4_InstallNonZeroExit_Failure_NoListCall()
    {
        // Task-prompt "(c) install error mid-poll": pac install itself
        // returns non-zero (Dataverse rejected the install request — e.g.,
        // app not available in this env's region, or ambient pac profile
        // untargeted).
        var runner = FakeProcessRunner.WithScript(
            installExitCode: 1,
            installStderr: "Error: Application 'msft_PowerBI_Anchor' is not available in tenant region 'CustomRegion1'.",
            listResponses: Array.Empty<(int, string)>());
        var installer = BuildInstaller(runner);

        var outcome = await installer.EnsureInstalledAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<RequiredApplicationsInstallOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("pac application install");
        failure.Diagnostic.Should().Contain(PowerBiAnchor);
        failure.Diagnostic.Should().Contain("exited 1");
        failure.Diagnostic.Should().Contain("not available in tenant region",
            "stderr surfaced verbatim (truncated) for operator triage");

        runner.Calls.Should().HaveCount(1, "list MUST NOT fire when install itself fails");
        runner.Calls[0].Args.Should().Contain("install");
    }

    // ---------- T5 install TimeoutException (per-app deadline exceeded) ----------

    [Fact]
    public async Task T5_InstallTimeoutException_Failure_MentionsPerAppDeadline_NoListCall()
    {
        var runner = FakeProcessRunner.WithInstallThrow(
            new TimeoutException("Process did not exit within 600 seconds."));
        var installer = BuildInstaller(runner);

        var outcome = await installer.EnsureInstalledAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<RequiredApplicationsInstallOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain(PowerBiAnchor);
        failure.Diagnostic.Should().Contain("deadline",
            "operator diagnostic must call out the per-app timeout");
        failure.Diagnostic.Should().Contain(DvUrl);
        failure.Diagnostic.Should().Contain("pre-install",
            "diagnostic points operator at the manual fallback command");

        runner.Calls.Should().HaveCount(1, "list MUST NOT fire when install times out");
    }

    // ---------- T6 install InvalidOperationException (pac binary missing) ----------

    [Fact]
    public async Task T6_InstallInvalidOperation_Failure_MentionsPathRemediation_NoListCall()
    {
        var runner = FakeProcessRunner.WithInstallThrow(
            new InvalidOperationException("failed to start 'pac': No such file or directory"));
        var installer = BuildInstaller(runner);

        var outcome = await installer.EnsureInstalledAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<RequiredApplicationsInstallOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("pac CLI");
        failure.Diagnostic.Should().Contain("PATH",
            "operator triage hint — check pac CLI on PATH or SolutionImportOptions:PacCliExecutable");

        runner.Calls.Should().HaveCount(1, "list MUST NOT fire when pac binary is missing");
    }

    // ---------- T7 multi-app manifest first-failure short-circuit ----------

    [Fact]
    public async Task T7_MultiApp_FirstFails_SecondNotTouched()
    {
        // Two-app manifest — first install fails, second must not be attempted
        // (regression guard for the per-manifest first-failure semantics: the
        // H6 gate diagnostic should name the FIRST missing app, not a compound
        // list built up while additional installs hang).
        var runner = FakeProcessRunner.WithScript(
            installExitCode: 1,
            installStderr: "Error: install rejected",
            listResponses: Array.Empty<(int, string)>());
        var installer = BuildInstaller(runner);
        var request = BuildRequest(apps: new[] { PowerBiAnchor, SecondApp });

        var outcome = await installer.EnsureInstalledAsync(request, CancellationToken.None);

        outcome.Should().BeOfType<RequiredApplicationsInstallOutcome.Failure>();
        runner.Calls.Should().HaveCount(1,
            "second app's install MUST NOT be invoked after first app fails");
        runner.Calls[0].Args.Should().Contain(PowerBiAnchor);
        runner.Calls.All(c => !c.Args.Contains(SecondApp)).Should().BeTrue();
    }

    // ---------- T8 empty manifest short-circuit ----------

    [Fact]
    public async Task T8_EmptyManifest_Success_NoShellOutsAtAll()
    {
        var runner = FakeProcessRunner.WithScript(
            installExitCode: 0,
            listResponses: Array.Empty<(int, string)>());
        var installer = BuildInstaller(runner);
        var request = BuildRequest(apps: Array.Empty<string>());

        var outcome = await installer.EnsureInstalledAsync(request, CancellationToken.None);

        outcome.Should().BeOfType<RequiredApplicationsInstallOutcome.Success>();
        runner.Calls.Should().BeEmpty("empty manifest short-circuits BEFORE any pac invocation");
    }

    // ---------- T9 ContainsApp theory ----------

    [Theory]
    [InlineData(
        "Application Name        Application Id                        Publisher            Version    State\n" +
        "---------------------  ------------------------------------- -------------------- --------- ---------\n" +
        "msft_PowerBI_Anchor    aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee  Microsoft Corporation 1.0.0.0   Installed\n",
        "msft_PowerBI_Anchor", true)]
    [InlineData(
        "msft_powerbi_anchor    xxx    yyy    Installed\n",  // lowercase match
        "msft_PowerBI_Anchor", true)]
    [InlineData(
        "OtherApp    xxx    yyy    Installed\n",
        "msft_PowerBI_Anchor", false)]
    [InlineData("", "msft_PowerBI_Anchor", false)]
    [InlineData("---------------------", "msft_PowerBI_Anchor", false)]  // separator-only
    public void T9_ContainsApp_Theory(string listOutput, string appName, bool expected)
    {
        PacRequiredApplicationsInstaller.ContainsApp(listOutput, appName).Should().Be(expected);
    }

    [Fact]
    public void T9b_ContainsApp_NullOrWhitespace_ReturnsFalse()
    {
        PacRequiredApplicationsInstaller.ContainsApp(null, "app").Should().BeFalse();
        PacRequiredApplicationsInstaller.ContainsApp("output", null!).Should().BeFalse();
        PacRequiredApplicationsInstaller.ContainsApp("output", "").Should().BeFalse();
        PacRequiredApplicationsInstaller.ContainsApp("   \n\t\n", "app").Should().BeFalse();
    }

    // ---------- T10 list-call TimeoutException is inconclusive (keeps polling) ----------

    [Fact]
    public async Task T10_ListTimeoutMidPoll_Inconclusive_KeepsPolling_EventuallyConfirms()
    {
        // First list throws TimeoutException (single-poll timeout, e.g., pac
        // hung briefly on a slow tenant); subsequent list confirms. Installer
        // MUST NOT fail on the individual list-call timeout — it's inconclusive
        // by design per the SKILL header.
        var runner = FakeProcessRunner.WithScript(
            installExitCode: 0,
            listResponses: new (int, string)[] { (0, BuildListStdoutWithApp(PowerBiAnchor)) },
            listThrowsOnCall: 1,  // first list call throws
            listThrowException: new TimeoutException("Process did not exit within 90 seconds."));
        var installer = BuildInstaller(runner);

        var outcome = await installer.EnsureInstalledAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<RequiredApplicationsInstallOutcome.Success>();
        runner.Calls.Should().HaveCount(3,
            "install + list (timeout, inconclusive) + list (confirms present) = 3 pac invocations");
    }

    // ---------- T11 F13 canonical manifest regression guard ----------

    [Fact]
    public void T11_F13CanonicalManifest_ContainsPowerBiAnchor()
    {
        // F13 verbatim: msft_PowerBI_Anchor is required to unblock SpaarkeMaster's
        // env-var dep on powerbimashupparameter. Silent removal here would
        // re-expose the "MissingDependency: powerbimashupparameter" failure
        // this handler exists to eliminate on every fresh Production-tier env.
        StaticRequiredApplicationsManifest.DefaultRequiredApplicationNames
            .Should().Contain(PowerBiAnchor);
    }

    // ---------- T12 per-list non-zero exit is inconclusive ----------

    [Fact]
    public async Task T12_ListNonZeroExitMidPoll_Inconclusive_KeepsPolling()
    {
        // First list returns exit 1 (e.g., transient auth revoked); subsequent
        // list returns exit 0 with the app present. Installer keeps polling
        // through the transient failure and eventually confirms Success.
        var runner = FakeProcessRunner.WithScript(
            installExitCode: 0,
            listResponses: new (int, string)[]
            {
                (1, string.Empty),                          // poll 1: exit 1 (auth glitch)
                (0, BuildListStdoutWithApp(PowerBiAnchor)), // poll 2: confirmed
            });
        var installer = BuildInstaller(runner);

        var outcome = await installer.EnsureInstalledAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<RequiredApplicationsInstallOutcome.Success>();
        runner.Calls.Should().HaveCount(3, "1 install + 2 list polls (first inconclusive, second confirms)");
    }

    // ---------- helpers ----------

    private static PacRequiredApplicationsInstaller BuildInstaller(IProcessRunner runner)
    {
        var options = Options.Create(new SolutionImportOptions
        {
            PacCliExecutable = "pac",
            // Validate() requires these; not exercised in unit tests but must be non-empty.
            ProvisioningArtifactsContainerUri = "https://test.blob.core.windows.net/artifacts",
            SolutionArtifactManifestBlobName = "test.json",
        });
        // Compressed cadence for fast tests — real production defaults are
        // 10min per-app deadline / 30s poll interval / 90s per-list timeout,
        // exercised via the public ctor path in production. We use the
        // internal ctor overload here to bring the timeout test row down
        // from 10 min to ~1 s of real time (the loop behavior + failure
        // classification are the observed properties, not the wall-clock).
        return new PacRequiredApplicationsInstaller(
            runner,
            options,
            TimeProvider.System,
            perAppDeadline: FastPerAppDeadline,
            pollInterval: FastPollInterval,
            listTimeout: FastListTimeout,
            NullLogger<PacRequiredApplicationsInstaller>.Instance);
    }

    private static RequiredApplicationsInstallRequest BuildRequest(IReadOnlyList<string>? apps = null)
        => new(
            TenantId: TenantId,
            ClientId: ClientId,
            ClientSecret: ClientSecret,
            TargetDataverseUrl: DvUrl,
            RequiredApplicationNames: apps ?? new[] { PowerBiAnchor });

    private static string BuildListStdoutWithApp(string appName)
    {
        // Mirrors real pac application list shape (canonical, from live
        // session field notes): header + separator + rows with State column.
        return
            "Application Name        Application Id                        Publisher            Version    State\n" +
            "----------------------- ------------------------------------- -------------------- --------- ---------\n" +
            $"{appName}    aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee  Microsoft Corporation 1.0.0.0   Installed\n" +
            "msft_Other              ffffffff-1111-2222-3333-444444444444  Microsoft Corporation 1.0.0.0   Available\n";
    }

    private static string BuildListStdoutWithoutApp()
    {
        // Fresh env — only "Available" apps, target not yet present.
        return
            "Application Name        Application Id                        Publisher            Version    State\n" +
            "----------------------- ------------------------------------- -------------------- --------- ---------\n" +
            "msft_Other              ffffffff-1111-2222-3333-444444444444  Microsoft Corporation 1.0.0.0   Available\n";
    }

    /// <summary>
    /// Fake <see cref="IProcessRunner"/> that routes calls to canned responses
    /// keyed on argv[0..1] = ("application", "install") vs ("application",
    /// "list"). Supports a scripted sequence of list responses so the 3-poll
    /// happy-path + timeout test rows can be expressed declaratively. Captures
    /// every call's argv for post-hoc argv-shape assertions.
    /// </summary>
    private sealed class FakeProcessRunner : IProcessRunner
    {
        public sealed record RecordedCall(string Executable, IReadOnlyList<string> Args);

        public List<RecordedCall> Calls { get; } = new();

        private int _installExitCode;
        private string _installStdout = string.Empty;
        private string _installStderr = string.Empty;
        private Exception? _installException;

        private (int ExitCode, string Stdout)[] _listResponses = Array.Empty<(int, string)>();
        private int _listCallIndex;
        private bool _repeatLastListResponse;
        private int _listThrowsOnCall = -1;  // 1-indexed
        private Exception? _listThrowException;

        public static FakeProcessRunner WithScript(
            int installExitCode,
            IReadOnlyList<(int ExitCode, string Stdout)> listResponses,
            string installStdout = "",
            string installStderr = "",
            bool repeatLastListResponse = false,
            int listThrowsOnCall = -1,
            Exception? listThrowException = null)
        {
            return new FakeProcessRunner
            {
                _installExitCode = installExitCode,
                _installStdout = installStdout,
                _installStderr = installStderr,
                _listResponses = listResponses.ToArray(),
                _repeatLastListResponse = repeatLastListResponse,
                _listThrowsOnCall = listThrowsOnCall,
                _listThrowException = listThrowException,
            };
        }

        public static FakeProcessRunner WithInstallThrow(Exception installException)
        {
            return new FakeProcessRunner
            {
                _installException = installException,
            };
        }

        public Task<ProcessResult> RunAsync(
            string executable,
            IReadOnlyList<string> args,
            IReadOnlyDictionary<string, string>? environment,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
        {
            Calls.Add(new RecordedCall(executable, args));

            var isInstall = args.Count >= 2 && args[0] == "application" && args[1] == "install";
            var isList = args.Count >= 2 && args[0] == "application" && args[1] == "list";

            if (isInstall)
            {
                if (_installException is not null) throw _installException;
                return Task.FromResult(new ProcessResult(_installExitCode, _installStdout, _installStderr));
            }

            if (isList)
            {
                // Track list-call ordinal (1-indexed) for scripted throw + response indexing.
                var listOrdinal = Calls.Count(c =>
                    c.Args.Count >= 2 && c.Args[0] == "application" && c.Args[1] == "list");

                if (_listThrowsOnCall == listOrdinal && _listThrowException is not null)
                {
                    throw _listThrowException;
                }

                if (_listResponses.Length == 0)
                {
                    throw new InvalidOperationException(
                        "FakeProcessRunner: no list responses scripted but list was invoked.");
                }

                var responseIndex = _listCallIndex;
                if (_repeatLastListResponse && responseIndex >= _listResponses.Length)
                {
                    responseIndex = _listResponses.Length - 1;
                }
                else if (responseIndex >= _listResponses.Length)
                {
                    throw new InvalidOperationException(
                        $"FakeProcessRunner: list was invoked {listOrdinal} times but only " +
                        $"{_listResponses.Length} list responses were scripted; enable repeatLastListResponse " +
                        "or supply more responses.");
                }
                _listCallIndex++;

                var (exit, stdout) = _listResponses[responseIndex];
                return Task.FromResult(new ProcessResult(exit, stdout, string.Empty));
            }

            throw new InvalidOperationException(
                $"FakeProcessRunner: unhandled argv shape [{string.Join(", ", args)}]. " +
                "Expected 'application install' or 'application list' as first two args.");
        }
    }
}
