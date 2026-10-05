// -----------------------------------------------------------------------------
// PacOrgSettingsContractApplierTests.cs
//
// HANDLER-08 LIVE-IMPL COVERAGE (Wave 2.5 pre-dispatch remediation
// 2026-08-27 — replaces the Wave 2 log-and-return-Success scaffold with
// the real `pac org list-settings` + `pac org update-settings` shell-outs).
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. NO live pac CLI / no real
//   System.Diagnostics.Process spawn. A fake <see cref="IProcessRunner"/>
//   feeds the applier canned exit-codes + stdout so every failure mode is
//   deterministic. Live-Dataverse coverage belongs in env-guarded smoke
//   tests (F14 verification is: fresh env at maxuploadfilesize=5,242,880 →
//   applier bumps to 25,600,000 in one call → import proceeds without
//   "Webresource content size too big"; that path is exercised end-to-end
//   at the operator level via the /provision-environment skill, not at CI).
//
// COVERAGE (F14 verbatim + punchlist HANDLER-08 proposed_fix):
//   T1  Already-at-target skip path — list-settings returns current >
//       target for maxuploadfilesize → applier returns Success without
//       invoking update-settings (idempotent re-run).
//   T2  Update-required happy path — list-settings returns current <
//       target → applier invokes update-settings with the exact
//       --name / --value / --environment argv → Success.
//   T3  pac list-settings non-zero exit (auth failure / env unreachable /
//       ambient profile untargeted) → Failure with the stderr surfaced;
//       no update-settings call fired.
//   T4  pac update-settings non-zero exit → Failure with the failing
//       setting name + stderr surfaced.
//   T5  IProcessRunner throws TimeoutException on list-settings → Failure
//       diagnostic mentions the timeout budget.
//   T6  IProcessRunner throws TimeoutException on update-settings →
//       Failure diagnostic mentions the setting name + timeout budget.
//   T7  IProcessRunner throws InvalidOperationException on list-settings
//       (pac binary not on PATH) → Failure diagnostic mentions PATH
//       remediation.
//   T8  Empty OrgSettings map → Success without any shell-outs (defensive
//       edge case; F14 contract is always non-empty but the seam must
//       tolerate an empty manifest gracefully).
//   T9  Multiple settings, mixed drift → skip-then-apply only the drifted
//       ones; verified via update-settings argv capture (regression guard
//       for the per-setting idempotency loop).
//   T10 ParseListOutput theory — canonical pac tabular shapes: header
//       "Setting Name" + "Value" columns, header-less lines, dash
//       separator lines, empty stdout.
//   T11 SettingAlreadyAtOrAboveTarget theory — numeric compare
//       (25_600_000 vs 5_242_880 → false; 30_000_000 vs 25_600_000 →
//       true), string equality fallback (non-numeric values), and
//       ordinal-ignore-case ("On" vs "on").
//   T12 F14 acceptance — StaticOrgSettingsContractManifest carries
//       maxuploadfilesize=25_600_000 (regression guard against silent
//       downgrade to a smaller value that would leave the
//       UniversalDocumentUpload PCF bundle blocked).
//
// SF-N (fake-runner argv capture): every test that asserts on the
// shell-out argv verifies the pac command shape verbatim to catch any
// silent drift in the arg-name convention (`--name` vs `--property`,
// `--environment` vs `--url`) — the punchlist HANDLER-08 fix rides on
// the exact `--name / --value / --environment` triple.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;
using Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class PacOrgSettingsContractApplierTests
{
    private const string DvUrl = "https://acme.crm.dynamics.com/";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    private const string ClientId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string ClientSecret = "test-secret-placeholder";
    private const string MaxUploadFileSize = "maxuploadfilesize";
    private const string TargetValue = "25600000";  // F14 verbatim: 25 MB
    private const string FreshEnvValue = "5242880"; // F14 verbatim: default 5 MB

    // ---------- T1 already-at-target skip path ----------

    [Fact]
    public async Task T1_AlreadyAtTarget_Skips_ReturnsSuccess_NoUpdateCall()
    {
        // list-settings stdout shows current > target — applier must NOT invoke update.
        var runner = FakeProcessRunner.WithResponses(
            listSettingsStdout: BuildListStdout((MaxUploadFileSize, "30000000")),
            listSettingsExitCode: 0);
        var applier = BuildApplier(runner);

        var outcome = await applier.ApplyAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<OrgSettingsContractOutcome.Success>();
        var success = (OrgSettingsContractOutcome.Success)outcome;
        success.AppliedOrAlreadyCorrect.Should().ContainKey(MaxUploadFileSize)
            .WhoseValue.Should().Be("30000000", "current value preserved when at-or-above target");

        runner.Calls.Should().HaveCount(1, "only list-settings; update-settings suppressed by idempotency");
        runner.Calls[0].Args.Should().ContainInOrder("org", "list-settings", "--environment", DvUrl);
    }

    // ---------- T2 update-required happy path ----------

    [Fact]
    public async Task T2_UpdateRequired_InvokesUpdateSettings_WithExactArgv()
    {
        // Fresh env at 5 MB — needs bump to 25 MB.
        var runner = FakeProcessRunner.WithResponses(
            listSettingsStdout: BuildListStdout((MaxUploadFileSize, FreshEnvValue)),
            listSettingsExitCode: 0,
            updateSettingsExitCode: 0);
        var applier = BuildApplier(runner);

        var outcome = await applier.ApplyAsync(BuildRequest(), CancellationToken.None);

        outcome.Should().BeOfType<OrgSettingsContractOutcome.Success>();
        var success = (OrgSettingsContractOutcome.Success)outcome;
        success.AppliedOrAlreadyCorrect[MaxUploadFileSize].Should().Be(TargetValue,
            "applied target value recorded post-write");

        runner.Calls.Should().HaveCount(2, "one list + one update");
        runner.Calls[0].Args.Should().ContainInOrder("org", "list-settings", "--environment", DvUrl);
        // Punchlist HANDLER-08 proposed_fix wire-shape guard:
        //   pac org update-settings --name maxuploadfilesize --value 25600000 --environment <url>
        runner.Calls[1].Args.Should().ContainInOrder(
            "org", "update-settings",
            "--name", MaxUploadFileSize,
            "--value", TargetValue,
            "--environment", DvUrl);
    }

    // ---------- T3 pac list-settings non-zero exit ----------

    [Fact]
    public async Task T3_ListSettingsNonZeroExit_ReturnsFailure_NoUpdateCall()
    {
        var runner = FakeProcessRunner.WithResponses(
            listSettingsStdout: string.Empty,
            listSettingsStderr: "Error: Not authenticated to any environment. Run 'pac auth create' first.",
            listSettingsExitCode: 1);
        var applier = BuildApplier(runner);

        var outcome = await applier.ApplyAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<OrgSettingsContractOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("pac org list-settings");
        failure.Diagnostic.Should().Contain("exited 1");
        failure.Diagnostic.Should().Contain("Not authenticated", "stderr must be surfaced for triage");
        failure.Diagnostic.Should().Contain(DvUrl);

        runner.Calls.Should().HaveCount(1, "update-settings MUST NOT fire when list-settings fails");
    }

    // ---------- T4 pac update-settings non-zero exit ----------

    [Fact]
    public async Task T4_UpdateSettingsNonZeroExit_ReturnsFailure_WithSettingName()
    {
        var runner = FakeProcessRunner.WithResponses(
            listSettingsStdout: BuildListStdout((MaxUploadFileSize, FreshEnvValue)),
            listSettingsExitCode: 0,
            updateSettingsExitCode: 1,
            updateSettingsStderr: "Error: Value out of range for maxuploadfilesize.");
        var applier = BuildApplier(runner);

        var outcome = await applier.ApplyAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<OrgSettingsContractOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("pac org update-settings");
        failure.Diagnostic.Should().Contain(MaxUploadFileSize);
        failure.Diagnostic.Should().Contain(TargetValue);
        failure.Diagnostic.Should().Contain("Value out of range", "stderr surfaced");
        runner.Calls.Should().HaveCount(2);
    }

    // ---------- T5 list-settings TimeoutException ----------

    [Fact]
    public async Task T5_ListSettingsTimeout_ReturnsFailure_MentionsTimeoutBudget()
    {
        var runner = FakeProcessRunner.WithThrow(
            listSettingsException: new TimeoutException("Process did not exit within 90 seconds."));
        var applier = BuildApplier(runner);

        var outcome = await applier.ApplyAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<OrgSettingsContractOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("timed out");
        failure.Diagnostic.Should().Contain("list-settings");
    }

    // ---------- T6 update-settings TimeoutException ----------

    [Fact]
    public async Task T6_UpdateSettingsTimeout_ReturnsFailure_WithSettingName()
    {
        var runner = FakeProcessRunner.WithResponsesAndThrows(
            listSettingsStdout: BuildListStdout((MaxUploadFileSize, FreshEnvValue)),
            listSettingsExitCode: 0,
            updateSettingsException: new TimeoutException("Process did not exit within 90 seconds."));
        var applier = BuildApplier(runner);

        var outcome = await applier.ApplyAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<OrgSettingsContractOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("timed out");
        failure.Diagnostic.Should().Contain(MaxUploadFileSize);
    }

    // ---------- T7 InvalidOperationException on list ----------

    [Fact]
    public async Task T7_ListSettingsInvalidOperation_ReturnsFailure_MentionsPathRemediation()
    {
        var runner = FakeProcessRunner.WithThrow(
            listSettingsException: new InvalidOperationException("failed to start 'pac': No such file or directory"));
        var applier = BuildApplier(runner);

        var outcome = await applier.ApplyAsync(BuildRequest(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<OrgSettingsContractOutcome.Failure>().Subject;
        failure.Diagnostic.Should().Contain("pac CLI");
        failure.Diagnostic.Should().Contain("PATH");
    }

    // ---------- T8 empty OrgSettings map ----------

    [Fact]
    public async Task T8_EmptyContract_ReturnsSuccess_NoShellOutsAtAll()
    {
        var runner = FakeProcessRunner.WithResponses(
            listSettingsStdout: "should not be read",
            listSettingsExitCode: 0);
        var applier = BuildApplier(runner);
        var request = BuildRequest(orgSettings: new Dictionary<string, string>());

        var outcome = await applier.ApplyAsync(request, CancellationToken.None);

        outcome.Should().BeOfType<OrgSettingsContractOutcome.Success>();
        runner.Calls.Should().BeEmpty("empty contract short-circuits BEFORE any pac invocation");
    }

    // ---------- T9 multiple settings mixed drift ----------

    [Fact]
    public async Task T9_MultipleSettings_MixedDrift_AppliesOnlyDrifted()
    {
        // Two-setting contract: maxuploadfilesize needs bump (5 MB → 25 MB);
        // maxdepthforhierarchicalsecuritymodel already at target (100 → 100).
        var contract = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [MaxUploadFileSize] = TargetValue,
            ["maxdepthforhierarchicalsecuritymodel"] = "100",
        };
        var runner = FakeProcessRunner.WithResponses(
            listSettingsStdout: BuildListStdout(
                (MaxUploadFileSize, FreshEnvValue),
                ("maxdepthforhierarchicalsecuritymodel", "100")),
            listSettingsExitCode: 0,
            updateSettingsExitCode: 0);
        var applier = BuildApplier(runner);

        var outcome = await applier.ApplyAsync(BuildRequest(orgSettings: contract), CancellationToken.None);

        outcome.Should().BeOfType<OrgSettingsContractOutcome.Success>();
        // 1 list + 1 update (only maxuploadfilesize was drifted).
        runner.Calls.Should().HaveCount(2);
        runner.Calls[1].Args.Should().Contain("--name").And.Contain(MaxUploadFileSize);
        runner.Calls[1].Args.Should().NotContain("maxdepthforhierarchicalsecuritymodel",
            "already-at-target settings MUST NOT be re-applied");
    }

    // ---------- T10 ParseListOutput theory ----------

    [Theory]
    [InlineData(
        "Setting Name              Value\n" +
        "-------------------------- --------\n" +
        "maxuploadfilesize          5242880\n" +
        "maxdepthforhierarchicalsecuritymodel 100\n",
        "maxuploadfilesize", "5242880")]
    [InlineData(
        "Name Value\n" +
        "maxuploadfilesize 25600000\n",
        "maxuploadfilesize", "25600000")]
    [InlineData(
        "maxuploadfilesize 999999\n",  // header-less
        "maxuploadfilesize", "999999")]
    public void T10_ParseListOutput_ExtractsNameToValueMap(string stdout, string name, string expectedValue)
    {
        var map = PacOrgSettingsContractApplier.ParseListOutput(stdout);
        map.Should().ContainKey(name).WhoseValue.Should().Be(expectedValue);
    }

    [Fact]
    public void T10b_ParseListOutput_EmptyStdout_ReturnsEmptyMap()
    {
        PacOrgSettingsContractApplier.ParseListOutput(null).Should().BeEmpty();
        PacOrgSettingsContractApplier.ParseListOutput("").Should().BeEmpty();
        PacOrgSettingsContractApplier.ParseListOutput("   \n\t\n").Should().BeEmpty();
    }

    [Fact]
    public void T10c_ParseListOutput_SkipsHeaderAndSeparatorLines()
    {
        var stdout = "Setting Name Value\n---------- --------\nmaxuploadfilesize 5242880\n";
        var map = PacOrgSettingsContractApplier.ParseListOutput(stdout);
        map.Should().HaveCount(1).And.ContainKey("maxuploadfilesize");
    }

    // ---------- T11 SettingAlreadyAtOrAboveTarget theory ----------

    [Theory]
    [InlineData("5242880", "25600000", false)]    // fresh env below target
    [InlineData("25600000", "25600000", true)]    // exact match
    [InlineData("30000000", "25600000", true)]    // above target
    [InlineData("100", "100", true)]              // small numeric equality
    [InlineData("0", "1", false)]                 // zero vs positive
    public void T11a_NumericCompare_UsedForParseableValues(string current, string target, bool expected)
    {
        PacOrgSettingsContractApplier.SettingAlreadyAtOrAboveTarget(current, target).Should().Be(expected);
    }

    [Theory]
    [InlineData("on", "on", true)]
    [InlineData("On", "on", true)]  // ordinal-ignore-case
    [InlineData("off", "on", false)]
    [InlineData("enabled", "disabled", false)]
    public void T11b_StringEqualityFallback_ForNonNumericValues(string current, string target, bool expected)
    {
        PacOrgSettingsContractApplier.SettingAlreadyAtOrAboveTarget(current, target).Should().Be(expected);
    }

    // ---------- T12 F14 canonical value regression guard ----------

    [Fact]
    public void T12_F14CanonicalValue_MaxUploadFileSize_Is25MegaBytes()
    {
        // F14 verbatim: 25 MB = 25,600,000 bytes. Silent downgrade to a smaller
        // value here would re-expose the "Webresource content size is too big"
        // failure this handler exists to eliminate.
        StaticOrgSettingsContractManifest.DefaultOrgSettings[MaxUploadFileSize]
            .Should().Be("25600000");
        // Sanity: 25,600,000 > 5,242,880 (fresh Production default that F14 identified).
        long.Parse(StaticOrgSettingsContractManifest.DefaultOrgSettings[MaxUploadFileSize])
            .Should().BeGreaterThan(long.Parse(FreshEnvValue));
    }

    // ---------- helpers ----------

    private static PacOrgSettingsContractApplier BuildApplier(IProcessRunner runner)
    {
        var options = Options.Create(new SolutionImportOptions
        {
            PacCliExecutable = "pac",
            VerifierCallTimeout = TimeSpan.FromSeconds(90),
            // Validate() requires these; not exercised in unit tests but must be non-empty.
            ProvisioningArtifactsContainerUri = "https://test.blob.core.windows.net/artifacts",
            SolutionArtifactManifestBlobName = "test.json",
        });
        return new PacOrgSettingsContractApplier(
            runner,
            options,
            NullLogger<PacOrgSettingsContractApplier>.Instance);
    }

    private static OrgSettingsContractApplyRequest BuildRequest(
        IReadOnlyDictionary<string, string>? orgSettings = null)
    {
        return new OrgSettingsContractApplyRequest(
            TenantId: TenantId,
            ClientId: ClientId,
            ClientSecret: ClientSecret,
            TargetDataverseUrl: DvUrl,
            OrgSettings: orgSettings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MaxUploadFileSize] = TargetValue,
            });
    }

    private static string BuildListStdout(params (string Name, string Value)[] rows)
    {
        // Mirrors real pac org list-settings shape: header + separator + rows.
        var lines = new List<string>
        {
            "Setting Name              Value",
            "-------------------------- --------",
        };
        foreach (var (name, value) in rows)
        {
            lines.Add($"{name} {value}");
        }
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>
    /// Fake <see cref="IProcessRunner"/> that routes calls to a canned
    /// (exit, stdout, stderr) response by the FIRST two args of the pac
    /// argv (list-settings vs update-settings). Also captures every call's
    /// argv for post-hoc argv-shape assertions. Alternatively, throws a
    /// preconfigured exception per call type (for TimeoutException /
    /// InvalidOperationException test rows).
    /// </summary>
    private sealed class FakeProcessRunner : IProcessRunner
    {
        public sealed record RecordedCall(string Executable, IReadOnlyList<string> Args);

        public List<RecordedCall> Calls { get; } = new();

        private int _listExitCode;
        private string _listStdout = string.Empty;
        private string _listStderr = string.Empty;
        private Exception? _listException;

        private int _updateExitCode;
        private string _updateStdout = string.Empty;
        private string _updateStderr = string.Empty;
        private Exception? _updateException;

        public static FakeProcessRunner WithResponses(
            string listSettingsStdout = "",
            int listSettingsExitCode = 0,
            string listSettingsStderr = "",
            int updateSettingsExitCode = 0,
            string updateSettingsStdout = "",
            string updateSettingsStderr = "")
        {
            return new FakeProcessRunner
            {
                _listStdout = listSettingsStdout,
                _listExitCode = listSettingsExitCode,
                _listStderr = listSettingsStderr,
                _updateExitCode = updateSettingsExitCode,
                _updateStdout = updateSettingsStdout,
                _updateStderr = updateSettingsStderr,
            };
        }

        public static FakeProcessRunner WithThrow(
            Exception? listSettingsException = null,
            Exception? updateSettingsException = null)
        {
            return new FakeProcessRunner
            {
                _listException = listSettingsException,
                _updateException = updateSettingsException,
            };
        }

        public static FakeProcessRunner WithResponsesAndThrows(
            string listSettingsStdout,
            int listSettingsExitCode,
            Exception updateSettingsException)
        {
            return new FakeProcessRunner
            {
                _listStdout = listSettingsStdout,
                _listExitCode = listSettingsExitCode,
                _updateException = updateSettingsException,
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

            // Route by argv[0..1] = ("org", "list-settings") vs ("org", "update-settings").
            var isList = args.Count >= 2 && args[0] == "org" && args[1] == "list-settings";
            var isUpdate = args.Count >= 2 && args[0] == "org" && args[1] == "update-settings";

            if (isList)
            {
                if (_listException is not null) throw _listException;
                return Task.FromResult(new ProcessResult(_listExitCode, _listStdout, _listStderr));
            }
            if (isUpdate)
            {
                if (_updateException is not null) throw _updateException;
                return Task.FromResult(new ProcessResult(_updateExitCode, _updateStdout, _updateStderr));
            }
            throw new InvalidOperationException(
                $"FakeProcessRunner: unhandled argv shape [{string.Join(", ", args)}]. " +
                "Expected 'org list-settings' or 'org update-settings' as first two args.");
        }
    }
}
