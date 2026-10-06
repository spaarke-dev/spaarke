using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.Admin.Models;
using Sprk.Bff.Api.Services.Access;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Batch-4 integration — round 46 item 2: Make Secure ships (<c>Set-AccessRibbon.ps1 -Apply -SecureTransitionDeployed</c>)
/// only where its file backstop runs with its writes on. The check
/// (<c>infrastructure/dataverse/ribbon/AccessRibbons/SecureTransitionBackstopCheck.ps1</c>) runs here in <c>pwsh</c>
/// over the job-status answer, serialized from the endpoint's own <see cref="JobStatusDetail"/> the way the API serves
/// it, and the script's refusal to run that release without the BFF to check is pinned without any network call.
/// </summary>
/// <remarks>
/// Requires PowerShell 7 (<c>pwsh</c>) on the PATH, like <c>GrantProvenanceBackfillScriptTests</c>; its absence fails the
/// test with that message.
/// </remarks>
public class SecureTransitionBackstopCheckTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static string Status(
        bool enabled = true, string cron = "*/2 * * * *", string? relocationMode = SecureChildReconciliationJob.ModeWrite,
        bool withRun = true, string jobId = SecureChildReconciliationJob.JobIdConstant, bool runningFirst = false)
    {
        var report = relocationMode is null
            ? JsonSerializer.Serialize(new { mode = "write" }, Web)
            : JsonSerializer.Serialize(new { mode = "write", makeSecureRelocations = new { mode = relocationMode, moved = 0 } }, Web);
        var runs = new List<JobRunDetail>();
        if (runningFirst)
            runs.Add(new JobRunDetail(Guid.NewGuid(), "Scheduled", "c0", DateTimeOffset.UtcNow, null, "Running", null, null, null));
        if (withRun)
            runs.Add(new JobRunDetail(Guid.NewGuid(), "Scheduled", "c1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Succeeded",
                null, 0, 12, report));
        var status = new JobStatusDetail(jobId, "Secure Child Reconciliation", "", enabled, cron, null, null, null, null, runs);
        return JsonSerializer.Serialize(status, Web);
    }

    private static string[] Check(string statusJson)
    {
        var output = RunPwsh($$"""
            . (Join-Path '{{AccessRibbonsDir()}}' 'SecureTransitionBackstopCheck.ps1')
            $status = '{{statusJson.Replace("'", "''", StringComparison.Ordinal)}}' | ConvertFrom-Json
            ConvertTo-Json -InputObject @(Test-SecureTransitionBackstop -Status $status) -Compress
            """, expectSuccess: true);
        return JsonSerializer.Deserialize<string[]>(output)!;
    }

    [Fact(DisplayName = "Round 46 item 2: enabled every 2 minutes, latest run settled in write mode — Make Secure may ship")]
    public void AnEnabledJob_WhoseLatestRunSettledInWriteMode_Passes()
    {
        Check(Status()).Should().BeEmpty();
        Check(Status(runningFirst: true)).Should().BeEmpty("a run still in progress is skipped for the latest COMPLETED one");
    }

    [Theory(DisplayName = "Round 46 item 2: each way the backstop is not on refuses Make Secure")]
    [InlineData("disabled", "not enabled")]
    [InlineData("cron", "not every 2 minutes")]
    [InlineData("report-only", "not 'write'")]
    [InlineData("unavailable", "not 'write'")]
    [InlineData("no-relocations", "does not carry the wired backstop")]
    [InlineData("no-run", "no completed run")]
    [InlineData("other-job", "not secure-child-reconciliation")]
    public void EachWayTheBackstopIsNotOn_Refuses(string shape, string reason)
    {
        var status = shape switch
        {
            "disabled" => Status(enabled: false),
            "cron" => Status(cron: "0 3 * * *"),
            "report-only" => Status(relocationMode: SecureChildReconciliationJob.ModeReportOnly),
            "unavailable" => Status(relocationMode: "unavailable"),
            "no-relocations" => Status(relocationMode: null),
            "no-run" => Status(withRun: false),
            _ => Status(jobId: "document-container-migration"),
        };

        Check(status).Should().ContainSingle().Which.Should().Contain(reason);
    }

    [Fact(DisplayName = "Round 46 item 2: -Apply -SecureTransitionDeployed without the BFF to check is refused before anything is read or written")]
    public void TheRelease_WithoutTheBffToCheck_IsRefusedBeforeAnyCall()
    {
        var script = Path.Combine(AccessRibbonsDir(), "Set-AccessRibbon.ps1");
        var output = RunPwsh($$"""
            try {
                & '{{script}}' -EnvironmentUrl https://example.invalid -SolutionName SomeRibbons -SecureTransitionDeployed -Apply
                'NOT REFUSED'
            } catch { "REFUSED: $($_.Exception.Message)" }
            """, expectSuccess: true);

        output.Should().StartWith("REFUSED:").And.Contain("-BffBaseUrl and -ApiScope");
    }

    private static string AccessRibbonsDir() =>
        Path.Combine(RepoRoot(), "infrastructure", "dataverse", "ribbon", "AccessRibbons");

    private static string RunPwsh(string body, bool expectSuccess)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("$ErrorActionPreference = 'Stop'\n" + body);

        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException("PowerShell 7 (pwsh) must be on the PATH to exercise the release check.", ex);
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit(120_000).Should().BeTrue("pwsh must finish");
            if (expectSuccess)
                process.ExitCode.Should().Be(0, $"pwsh failed: {stderr.Result}");
            return stdout.Result.Trim();
        }
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
