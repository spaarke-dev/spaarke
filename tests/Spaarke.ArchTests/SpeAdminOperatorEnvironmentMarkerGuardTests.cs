using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 165, owner round 49 item 1 — the BFF deployment setting
/// <c>SpeAdmin:PlatformOperatorEnvironment</c> opens the SPE admin routes that span the whole SharePoint Embedded tenant
/// or a whole container type to a root-unit admin. It must reach ONLY Spaarke-operated environments: customer
/// environments never carry it. These guards keep every path that configures an App Service honest:
/// <list type="bullet">
///   <item>the marker is named only by the BFF itself and the Spaarke-environment deployment surface — never by customer
///   provisioning (the L2 control plane, the canonical app-settings catalog, the customer/Model 1 Bicep);</item>
///   <item><c>config/environments.json</c> declares it true only for the Spaarke-operated environments listed here;</item>
///   <item>no Bicep template emits it (master T249, 2026-10-05: the only stamp template is the customer stamp, and the
///   Spaarke Model 2 stack that carried the dev declaration is gone) — the marker reaches an App Service only through
///   <c>Deploy-BffApi.ps1</c>, bound to its registry entry's App Service (round 62 item 1).</item>
/// </list>
/// </summary>
public sealed class SpeAdminOperatorEnvironmentMarkerGuardTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly Regex Marker = new(@"PlatformOperatorEnvironment", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// The environments Spaarke itself operates. Today only <c>dev</c> (owner round 57 item 1): Spaarke's production
    /// operator environment does not exist yet (the stopped <c>spaarke-bff-prod</c> / "demo"), and the change that stands it
    /// up adds it here together with its registry key and its App Service setting (Deploy-BffApi.ps1 sets it).
    /// </summary>
    private static readonly HashSet<string> SpaarkeOperatedEnvironments = new(StringComparer.Ordinal) { "dev" };

    /// <summary>The registry key that declares an environment Spaarke-operated.</summary>
    private const string RegistryKey = "speAdminPlatformOperatorEnvironment";

    /// <summary>THE parse <c>scripts/Deploy-BffApi.ps1</c> uses (owner round 57 item 3).</summary>
    private const string MarkerModule = "scripts/common/SpeAdminOperatorMarker.ps1";

    /// <summary>Every file outside tests/docs/projects that may name the marker — the BFF and the Spaarke deployment surface.</summary>
    private static readonly HashSet<string> AllowedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "src/server/api/Sprk.Bff.Api/Configuration/SpeAdminOptions.cs",
        "src/server/api/Sprk.Bff.Api/Api/Filters/SpeAdminTenantScopeFilter.cs",
        "config/environments.json",
        "scripts/Deploy-BffApi.ps1",
        MarkerModule,
    };

    [Fact(DisplayName = "The operator-environment marker is named only by the BFF and the Spaarke-environment deployment surface")]
    public void TheMarker_IsNamedOnlyWhereSpaarkeEnvironmentsAreConfigured()
    {
        // Everything that can put an App Service setting on a BFF: server code (incl. the L2 control plane and its app-settings
        // handler), app settings files, scripts, Bicep, the environment registry, pipelines.
        var scan = new (string Root, string[] Patterns)[]
        {
            ("src", new[] { "*.cs", "*.ps1", "*.psm1", "*.bicep", "*.bicepparam", "appsettings*.json" }),
            ("scripts", new[] { "*.ps1", "*.psm1", "*.json", "*.yml", "*.yaml", "*.sh", "*.py" }),
            ("infrastructure", new[] { "*.bicep", "*.bicepparam", "*.json", "*.ps1" }),
            ("config", new[] { "*.json" }),
            (".github", new[] { "*.yml", "*.yaml" }),
        };
        var namers = scan
            .Where(s => Directory.Exists(Path.Combine(RepoRoot, s.Root)))
            .SelectMany(s => s.Patterns.SelectMany(e => Directory.EnumerateFiles(Path.Combine(RepoRoot, s.Root), e, SearchOption.AllDirectories)))
            .Where(f => !IsBuildOutput(f))
            .Select(f => (Rel: Rel(f), Text: File.ReadAllText(f)))
            .Where(f => Marker.IsMatch(f.Text))
            .Select(f => f.Rel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(namers.Count >= AllowedFiles.Count - 1, $"the scan must find the marker's own files (found {namers.Count})");
        var unexpected = namers.Where(f => !AllowedFiles.Contains(f) && !IsTestProject(f)).ToList();
        Assert.True(unexpected.Count == 0,
            "These files name SpeAdmin:PlatformOperatorEnvironment, which must reach ONLY Spaarke-operated environments — customer " +
            "provisioning (the L2 control plane, the app-settings catalog, the customer/Model 1 Bicep) must never emit it:\n  " +
            string.Join("\n  ", unexpected));
    }

    [Fact(DisplayName = "config/environments.json declares the marker true only for the Spaarke-operated environments")]
    public void TheEnvironmentRegistry_DeclaresTheMarkerOnlyForSpaarkeOperatedEnvironments()
    {
        using var registry = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "config", "environments.json")));

        var notBoolean = NonBooleanDeclarations(registry.RootElement);
        Assert.True(notBoolean.Count == 0,
            $"{RegistryKey} must be a JSON boolean (true or false, unquoted) — Deploy-BffApi.ps1 refuses anything else, and a " +
            "PowerShell [bool] cast would read the STRING \"false\" as true (owner round 57 item 3):\n  " +
            string.Join("\n  ", notBoolean));

        var declaredTrue = registry.RootElement.GetProperty("environments").EnumerateObject()
            .Where(e => e.Value.ValueKind == JsonValueKind.Object
                        && e.Value.TryGetProperty(RegistryKey, out var v)
                        && v.ValueKind == JsonValueKind.True)
            .Select(e => e.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(SpaarkeOperatedEnvironments, declaredTrue);

        var template = registry.RootElement.GetProperty("environments").GetProperty("_template");
        Assert.True(template.TryGetProperty(RegistryKey, out var t) && t.ValueKind == JsonValueKind.False,
            "the template a new CUSTOMER environment is copied from must say false");
    }

    [Fact(DisplayName = "The registry check refuses a marker declared as the STRING \"false\" (and every other non-boolean)")]
    public void TheRegistryCheck_RefusesAStringFalse()
    {
        using var registry = JsonDocument.Parse($$"""
            { "environments": {
                "string-false": { "{{RegistryKey}}": "false" },
                "string-true":  { "{{RegistryKey}}": "true" },
                "number":       { "{{RegistryKey}}": 0 },
                "null":         { "{{RegistryKey}}": null },
                "declared":     { "{{RegistryKey}}": true },
                "customer":     { "{{RegistryKey}}": false },
                "absent":       { }
            } }
            """);

        Assert.Equal(new[] { "string-false", "string-true", "number", "null" }, NonBooleanDeclarations(registry.RootElement));
    }

    /// <summary>
    /// The deploy script's own parse (owner round 57 item 3), run for real: <c>[bool]"false"</c> is <c>$true</c> in
    /// PowerShell, so a cast opened the tenant-/type-wide routes on whatever environment declared the string "false". THE
    /// parse accepts only a JSON boolean and THROWS on anything else, and <c>Deploy-BffApi.ps1</c> fails the deploy on that
    /// throw. Needs a PowerShell host (<c>pwsh</c>; <c>powershell.exe</c> on Windows) — the CI runner (ubuntu-latest)
    /// ships <c>pwsh</c>; a host without one FAILS this test rather than skipping it.
    /// </summary>
    [Fact(DisplayName = "Deploy-BffApi.ps1's marker parse refuses the STRING \"false\" and every other non-boolean")]
    public void TheDeployScriptsMarkerParse_RefusesAStringFalse_AndReadsOnlyAJsonBoolean()
    {
        // The script takes its answer from THE parse and never reads the key itself (a second read could cast again).
        // Source-level, comments stripped; it cannot see the key spelled by indirection — the module is the one reader.
        var deployScript = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "Deploy-BffApi.ps1"));
        var deployCode = Regex.Replace(Regex.Replace(deployScript, @"<#.*?#>", string.Empty, RegexOptions.Singleline),
            @"^\s*#.*$", string.Empty, RegexOptions.Multiline);
        Assert.Matches(new Regex(@"^\. \(Join-Path \$PSScriptRoot ""common/SpeAdminOperatorMarker\.ps1""\)\s*$", RegexOptions.Multiline), deployCode);
        Assert.Matches(new Regex(@"\$markerDeclared = Get-SpeAdminOperatorMarkerDeclaration\b"), deployCode);
        Assert.DoesNotMatch(new Regex(RegistryKey, RegexOptions.IgnoreCase), deployCode);

        var work = Path.Combine(Path.GetTempPath(), "spe-marker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var registryPath = Path.Combine(work, "environments.json");
            File.WriteAllText(registryPath, $$"""
                { "environments": {
                    "string-false": { "{{RegistryKey}}": "false" },
                    "string-true":  { "{{RegistryKey}}": "true" },
                    "number":       { "{{RegistryKey}}": 1 },
                    "null":         { "{{RegistryKey}}": null },
                    "array":        { "{{RegistryKey}}": [ false ] },
                    "declared":     { "{{RegistryKey}}": true },
                    "customer":     { "{{RegistryKey}}": false },
                    "absent":       { }
                } }
                """);

            var driver = Path.Combine(work, "drive.ps1");
            File.WriteAllText(driver, $$"""
                $ErrorActionPreference = 'Stop'
                . '{{Path.Combine(RepoRoot, MarkerModule).Replace("'", "''")}}'
                foreach ($name in @('string-false', 'string-true', 'number', 'null', 'array', 'declared', 'customer', 'absent', 'not-in-registry')) {
                    try {
                        $value = Get-SpeAdminOperatorMarkerDeclaration -RegistryPath '{{registryPath.Replace("'", "''")}}' -Environment $name
                        "$name=$($value.GetType().Name):$value"
                    } catch {
                        "$name=REFUSED"
                    }
                }
                """);

            var output = RunPowerShell(driver);

            Assert.Equal(
                new[]
                {
                    "string-false=REFUSED", "string-true=REFUSED", "number=REFUSED", "null=REFUSED", "array=REFUSED",
                    "declared=Boolean:True", "customer=Boolean:False", "absent=Boolean:False", "not-in-registry=Boolean:False",
                },
                output);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    /// <summary>
    /// Main-session round 62 item 1 (class a, fail-open): a marker declared TRUE is bound to the App Service its registry
    /// entry names. <c>Deploy-BffApi.ps1</c> reads the declaration by the <c>-Environment</c> label (default <c>dev</c>), so
    /// a deploy that named another App Service but left the label at its default would have set the marker there. The deploy
    /// calls THE module's <c>Assert-SpeAdminOperatorMarkerTarget</c> when the marker is declared, and the module refuses any
    /// other target — run for real through the same PowerShell driver as the parse above.
    /// </summary>
    [Fact(DisplayName = "Round 62 item 1: a declared operator marker is refused unless the deploy targets its registry entry's App Service")]
    public void ADeclaredMarker_IsRefused_UnlessTheDeployTargetsItsRegistryEntrysAppService()
    {
        var deployScript = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "Deploy-BffApi.ps1"));
        var deployCode = Regex.Replace(Regex.Replace(deployScript, @"<#.*?#>", string.Empty, RegexOptions.Singleline),
            @"^\s*#.*$", string.Empty, RegexOptions.Multiline);
        Assert.Matches(new Regex(
            @"if \(\$markerDeclared\) \{\s*try \{\s*Assert-SpeAdminOperatorMarkerTarget -RegistryPath \$markerConfigPath -Environment \$Environment\s*`?\s*-AppServiceName \$AppServiceName -ResourceGroupName \$ResourceGroupName",
            RegexOptions.Singleline), deployCode);
        Assert.True(
            deployCode.IndexOf("Assert-SpeAdminOperatorMarkerTarget", StringComparison.Ordinal)
                < deployCode.IndexOf("az webapp config appsettings set", StringComparison.Ordinal),
            "the target is checked BEFORE the marker is set");

        var work = Path.Combine(Path.GetTempPath(), "spe-marker-target-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var registryPath = Path.Combine(work, "environments.json");
            File.WriteAllText(registryPath, $$"""
                { "environments": {
                    "dev":      { "appServiceName": "spaarke-bff-dev", "resourceGroup": "rg-spaarke-dev", "{{RegistryKey}}": true },
                    "unnamed":  { "{{RegistryKey}}": true }
                } }
                """);

            var driver = Path.Combine(work, "drive.ps1");
            File.WriteAllText(driver, $$"""
                $ErrorActionPreference = 'Stop'
                . '{{Path.Combine(RepoRoot, MarkerModule).Replace("'", "''")}}'
                $cases = @(
                    @('same',           'dev',     'spaarke-bff-dev',        'rg-spaarke-dev'),
                    @('same-casing',    'dev',     'SPAARKE-BFF-DEV',        'RG-Spaarke-Dev'),
                    @('other-app',      'dev',     'spaarke-bff-customer1',  'rg-spaarke-dev'),
                    @('other-group',    'dev',     'spaarke-bff-dev',        'rg-customer1'),
                    @('both-other',     'dev',     'spaarke-bff-customer1',  'rg-customer1'),
                    @('entry-unnamed',  'unnamed', 'spaarke-bff-dev',        'rg-spaarke-dev')
                )
                foreach ($c in $cases) {
                    try {
                        Assert-SpeAdminOperatorMarkerTarget -RegistryPath '{{registryPath.Replace("'", "''")}}' -Environment $c[1] -AppServiceName $c[2] -ResourceGroupName $c[3]
                        "$($c[0])=BOUND"
                    } catch {
                        "$($c[0])=REFUSED"
                    }
                }
                """);

            var output = RunPowerShell(driver);

            Assert.Equal(
                new[]
                {
                    "same=BOUND", "same-casing=BOUND", "other-app=REFUSED", "other-group=REFUSED", "both-other=REFUSED",
                    "entry-unnamed=REFUSED",
                },
                output);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    /// <summary>The environments whose marker declaration is present but not a JSON boolean, in registry order.</summary>
    private static List<string> NonBooleanDeclarations(JsonElement registryRoot) =>
        registryRoot.GetProperty("environments").EnumerateObject()
            .Where(e => e.Value.ValueKind == JsonValueKind.Object
                        && e.Value.TryGetProperty(RegistryKey, out var v)
                        && v.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            .Select(e => e.Name)
            .ToList();

    /// <summary>Runs a script file in a PowerShell host; the trimmed, non-empty stdout lines. Fails on a non-zero exit.</summary>
    private static string[] RunPowerShell(string scriptPath)
    {
        var host = FindPowerShellHost();
        var psi = new System.Diagnostics.ProcessStartInfo(host)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath })
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = System.Diagnostics.Process.Start(psi)
                            ?? throw new InvalidOperationException($"Could not start '{host}'.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(120_000), $"'{host}' did not finish within 120 s");
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0, $"'{host}' exited {process.ExitCode}. stderr:\n{stderr}\nstdout:\n{stdout}");

        return stdout.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary><c>pwsh</c> on PATH; on Windows, <c>powershell.exe</c> as a fallback. No host is a failure, not a skip.</summary>
    private static string FindPowerShellHost()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "pwsh.exe", "powershell.exe" } : new[] { "pwsh" };
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var name in names)
        {
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(dir.Trim('"'), name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new InvalidOperationException(
            "No PowerShell host (pwsh, or powershell.exe on Windows) is on PATH — the deploy script's marker parse cannot be " +
            "proven without one. Install PowerShell 7.");
    }

    [Fact(DisplayName = "No Bicep template or parameter file emits the operator marker (master T249: only the customer stamp remains)")]
    public void NoBicepTemplate_EmitsTheOperatorMarker()
    {
        // Master T249 (2026-10-05) deleted the Model 2 stack (stacks/model2-full.bicep and its dev/staging/prod parameter
        // files) that carried dev's declaration; infrastructure/bicep/customer.bicep is the one stamp template, and a
        // customer stamp never carries the marker. So the marker reaches an App Service ONLY through Deploy-BffApi.ps1, from
        // the registry, bound to the entry's App Service (round 62 item 1). A template that named it would set it on a
        // stamp no registry entry describes.
        var naming = Directory.EnumerateFiles(Path.Combine(RepoRoot, "infrastructure"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".bicep", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".bicepparam", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Where(f => !IsBuildOutput(f) && Marker.IsMatch(File.ReadAllText(f)))
            .Select(Rel)
            .ToList();
        Assert.True(naming.Count == 0,
            "These infrastructure templates name the SPE admin operator marker. Only Deploy-BffApi.ps1 may set it, from "
            + "config/environments.json, for a Spaarke-operated environment:" + Environment.NewLine + "  "
            + string.Join(Environment.NewLine + "  ", naming));
    }

    private static bool IsTestProject(string rel) =>
        rel.Split('/').Any(s => s.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase));

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string Rel(string path) => Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
               ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
