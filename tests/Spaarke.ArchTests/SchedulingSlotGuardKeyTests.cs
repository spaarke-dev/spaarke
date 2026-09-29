using System.Reflection;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ADR-036 A1 rule 2 (unified-access-control-r2 tasks 103 and #987): a non-production deployment slot runs no
/// scheduled jobs, because every slot-deploy path sets the BFF's slot-guard app setting, slot-sticky, to false.
/// </summary>
/// <remarks>
/// <para>The key is written in five places — the BFF reads it, four deploy paths set it. Nothing else keeps them in
/// step, and the failure is silent: rename the key in the BFF and every deploy path goes on setting a key nobody
/// reads, so staging slots run cron jobs against production data again. This test derives the key from the BFF
/// (<c>SchedulingModule.RunScheduledJobsSetting</c>, App Service form <c>:</c> → <c>__</c>) and requires every deploy
/// path to contain it.</para>
/// <para><b>MAINTENANCE PROCEDURE</b>: a failure names the deploy path that no longer sets the BFF's key. Fix that
/// path — do not remove it from <see cref="DeployPaths"/>. A NEW slot-deploy path (another script, workflow or
/// provisioning handler) must set the guard before its deploy and be added here in the same PR. Removing a path is
/// right only when that path no longer deploys to a slot at all.</para>
/// <para>Per <c>tests/CLAUDE.md</c> "Structural fitness functions" this file is MAINTAIN-class.</para>
/// </remarks>
public class SchedulingSlotGuardKeyTests
{
    /// <summary>Every path that deploys the BFF to a non-production slot, each with the reason it is listed.</summary>
    private static readonly IReadOnlyDictionary<string, string> DeployPaths = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["scripts/Deploy-BffApi.ps1"] = "-UseSlotDeploy sets the guard with --slot-settings before the staging deploy (task 103).",
        [".github/workflows/deploy-bff-api.yml"] = "The production pipeline's staging deploy sets it first (task 103 Step 9.5 H1).",
        ["infrastructure/bicep/modules/deployment-slot.bicep"] = "Lists it as slot-sticky and sets false on the slot (task 103).",
        ["src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BffDeploy/H9BffDeployHandler.cs"] =
            "The L2 H9 provisioning deploy sets it before its Kudu zip-deploy, and fails closed (#987).",
    };

    [Fact(DisplayName = "ADR-036 A1 rule 2: every slot-deploy path sets the slot-guard key the BFF actually reads")]
    public void EveryDeployPathSetsTheKeyTheBffReads()
    {
        var appServiceKey = ToAppServiceForm(BffSlotGuardKey());

        var files = DeployPaths.Keys
            .Select(path => (Path: path, Content: File.ReadAllText(System.IO.Path.Combine(SourceScan.RepoRoot, path))))
            .ToList();

        var missing = PathsNotSetting(files, appServiceKey);
        Assert.True(
            missing.Count == 0,
            $"These slot-deploy paths no longer set '{appServiceKey}', the key the BFF reads (SchedulingModule.RunScheduledJobsSetting). " +
            "A staging slot deployed through them runs scheduled jobs against production data (ADR-036 A1 rule 2): " +
            string.Join(", ", missing));
    }

    [Fact(DisplayName = "ADR-036 A1 rule 2: negative control — a deploy path still setting an old key name is flagged")]
    public void SlotGuardKey_NegativeControl_FlagsAPathSettingAnotherKey()
    {
        var missing = PathsNotSetting(
            new[]
            {
                ("deploy.ps1", "az webapp config appsettings set --slot staging --slot-settings Scheduling__RunCronJobs=false"),
                ("deploy.yml", "--slot-settings Scheduling__RunScheduledJobs=false"),
            },
            "Scheduling__RunScheduledJobs");

        Assert.Equal(new[] { "deploy.ps1" }, missing);
    }

    [Fact(DisplayName = "ADR-036 A1 rule 2: positive control — the BFF's key maps to its App Service form")]
    public void SlotGuardKey_PositiveControl_DerivesTheAppServiceFormFromTheBff()
    {
        var key = BffSlotGuardKey();

        Assert.Contains(':', key);
        Assert.Equal(key.Replace(":", "__", StringComparison.Ordinal), ToAppServiceForm(key));
        Assert.Empty(PathsNotSetting(new[] { ("deploy.ps1", $"--slot-settings {ToAppServiceForm(key)}=false") }, ToAppServiceForm(key)));
    }

    private static string BffSlotGuardKey()
    {
        var module = typeof(Program).Assembly.GetType("Sprk.Bff.Api.Infrastructure.DI.SchedulingModule")
            ?? throw new InvalidOperationException("SchedulingModule not found in the BFF assembly — update this test with it.");
        var field = module.GetField("RunScheduledJobsSetting", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("SchedulingModule.RunScheduledJobsSetting not found — the slot-guard key moved; update this test with it.");
        return (string)field.GetRawConstantValue()!;
    }

    /// <summary>Configuration key → App Service app-setting name (<c>:</c> is written as <c>__</c>).</summary>
    private static string ToAppServiceForm(string configurationKey) => configurationKey.Replace(":", "__", StringComparison.Ordinal);

    private static List<string> PathsNotSetting(IEnumerable<(string Path, string Content)> files, string appServiceKey)
        => files.Where(file => !file.Content.Contains(appServiceKey, StringComparison.Ordinal)).Select(file => file.Path).ToList();
}
