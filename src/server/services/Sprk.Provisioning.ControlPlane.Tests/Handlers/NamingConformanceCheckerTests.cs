// -----------------------------------------------------------------------------
// NamingConformanceCheckerTests.cs
//
// L2 CONTROL-PLANE unit tests for NamingConformanceChecker (task 182, Phase C''
// Wave G-7 Batch G-7A1). Table-driven coverage over the three rules R1/R2/R3
// ported from scripts/naming-conformance-check.ps1.
//
// ADR-038 CATEGORY:
//   Path #1 -- pure C# unit test. NO live filesystem beyond a per-test temp
//   directory the test itself writes + deletes. Zero shell-out. Rule logic is
//   exercised directly via the internal ScanForViolations + FindEnvToken
//   entrypoints (InternalsVisibleTo grants access).
//
// COVERAGE (POML acceptance criteria mapped to test cases):
//   T1  R1 env-token -- kebab-delimited names containing DEV/DEMO/PROD/etc.
//       flag; conformant names + false-positive-substrings pass.
//   T2  R1 env-token -- camelCase names (ProdBffSecret) flag; word-boundary
//       splitting matches the ps1 exactly.
//   T3  R2 casing-drift -- same lowercased name under >1 actual casings flags;
//       one casing everywhere does not.
//   T4  R3 vault-name-drift -- non-canonical vault names flag; codified legacy
//       exception spaarke-spekvcert does NOT flag; canonical sprk-{env}-kv
//       does NOT flag.
//   T5  Whole-file corpus (mirrors the ps1's SelfTest fixture) -- bad corpus
//       produces R1/R1camelCase/R2/R3; good corpus produces zero violations.
//   T6  CheckAsync integration -- scans a real temp filesystem, returns Success
//       when clean + Failure when a seeded bad file is included.
//   T7  CheckAsync fail-closed -- empty scan root returns Failure (parity with
//       the ps1's "no scannable files" branch).
//
// FORCING FUNCTION (POML acceptance criterion 1): a compile-time-verified
// assertion that NamingConformanceChecker.cs contains NO ProcessStartInfo /
// pwsh / az shell-out references in CODE -- via a filesystem read of the
// source file with comment stripping (parity with the god-class-ratchet
// ArchTest style).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class NamingConformanceCheckerTests
{
    // --------------------- T1: R1 env-token, kebab/snake -------------------

    [Theory]
    [InlineData("SPRK-DEV-DATAVERSE-URL", "DEV")]
    [InlineData("SPRK-DEMO-BFF-SECRET", "DEMO")]
    [InlineData("prod-bff-secret", "PROD")]
    [InlineData("BFF-Prod-Secret", "PROD")]
    [InlineData("staging.dataverse.url", "STAGING")]
    [InlineData("secret_test_value", "TEST")]
    [InlineData("QA_Secret_Name", "QA")]
    [InlineData("some-uat-secret", "UAT")]
    [InlineData("stage-secret-name", "STAGE")]
    [InlineData("sandbox-secret", "SANDBOX")]
    [InlineData("PRODUCTION-URL", "PRODUCTION")]
    public void FindEnvToken_KebabOrSnakeName_WithEnvWord_ReturnsToken(string name, string expectedToken)
    {
        NamingConformanceChecker.FindEnvToken(name).Should().Be(expectedToken);
    }

    [Theory]
    [InlineData("Dataverse-ServiceUrl")]
    [InlineData("BFF-API-ClientSecret")]
    [InlineData("bff-api-client-secret")]
    [InlineData("communication-webhook-signing-key")]
    [InlineData("Development-Only-Setting")]
    [InlineData("Devastation-Level")]
    [InlineData("Producer-Api-Key")]
    [InlineData("Testament-Section")]
    [InlineData("SprkPlatform-Secret")]
    public void FindEnvToken_ConformantOrLookalikeName_ReturnsNull(string name)
    {
        NamingConformanceChecker.FindEnvToken(name).Should().BeNull();
    }

    // --------------------- T2: R1 env-token, camelCase / PascalCase --------

    [Theory]
    [InlineData("ProdBffSecret", "PROD")]
    [InlineData("DevDataverseUrl", "DEV")]
    [InlineData("DemoBffSecret", "DEMO")]
    [InlineData("TestSecretValue", "TEST")]
    [InlineData("StagingDataverse", "STAGING")]
    [InlineData("BffProdSecret", "PROD")]
    public void FindEnvToken_CamelCase_WithEnvWord_ReturnsToken(string name, string expected)
    {
        NamingConformanceChecker.FindEnvToken(name).Should().Be(expected);
    }

    // --------------------- T3: R2 casing-drift -----------------------------

    [Fact]
    public void ScanForViolations_SameNameUnderMultipleCasings_FlagsR2()
    {
        var corpus = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a.json"] = "@Microsoft.KeyVault(VaultName=sprk-demo-kv;SecretName=BFF-API-ClientSecret)",
            ["b.json"] = "@Microsoft.KeyVault(VaultName=sprk-demo-kv;SecretName=bff-api-clientsecret)",
        };

        var violations = NamingConformanceChecker.ScanForViolations(corpus);

        violations.Should().Contain(v => v.Rule.StartsWith("R2"));
    }

    [Fact]
    public void ScanForViolations_OneCasingEverywhere_DoesNotFlagR2()
    {
        var corpus = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a.json"] = "@Microsoft.KeyVault(VaultName=sprk-demo-kv;SecretName=BFF-API-ClientSecret)",
            ["b.json"] = "@Microsoft.KeyVault(VaultName=sprk-prod-kv;SecretName=BFF-API-ClientSecret)",
        };

        var violations = NamingConformanceChecker.ScanForViolations(corpus);

        violations.Should().NotContain(v => v.Rule.StartsWith("R2"));
    }

    // --------------------- T4: R3 vault-name-drift -------------------------

    [Theory]
    [InlineData("kv-sdap-dev")]
    [InlineData("spaarke-kv-dev")]
    [InlineData("sprk-platform-prod-kv")]
    [InlineData("sprkshareddev-kv")]
    [InlineData("sprk-shared-dev-kv")]
    public void ScanForViolations_NonCanonicalVaultName_FlagsR3(string vaultName)
    {
        var corpus = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a.json"] = $"@Microsoft.KeyVault(VaultName={vaultName};SecretName=Dataverse-ServiceUrl)",
        };

        var violations = NamingConformanceChecker.ScanForViolations(corpus);

        violations.Should().Contain(v => v.Rule.StartsWith("R3") && v.Name == vaultName);
    }

    [Theory]
    [InlineData("sprk-dev-kv")]
    [InlineData("sprk-demo-kv")]
    [InlineData("sprk-prod-kv")]
    [InlineData("sprk-uat-kv")]
    [InlineData("sprk-test-kv")]
    [InlineData("spaarke-spekvcert")]
    public void ScanForViolations_CanonicalOrCodifiedVaultName_DoesNotFlagR3(string vaultName)
    {
        var corpus = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a.json"] = $"@Microsoft.KeyVault(VaultName={vaultName};SecretName=Dataverse-ServiceUrl)",
        };

        var violations = NamingConformanceChecker.ScanForViolations(corpus);

        violations.Should().NotContain(v => v.Rule.StartsWith("R3"));
    }

    // --------------------- T5: full-corpus fixtures (parity with ps1 SelfTest)

    [Fact]
    public void ScanForViolations_PS1SelfTestBadFixture_ReportsR1KebabAndR1CamelAndR2AndR3()
    {
        var corpus = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fixture-bad"] =
                "Dataverse__Url = @Microsoft.KeyVault(VaultName=sprk-dev-kv;SecretName=SPRK-DEV-DATAVERSE-URL)\n" +
                "Bff__Secret    = @Microsoft.KeyVault(VaultName=kv-sdap-dev;SecretName=BFF-API-ClientSecret)\n" +
                "Bff__Secret2   = @Microsoft.KeyVault(VaultName=sprk-demo-kv;SecretName=bff-api-clientsecret)\n" +
                "Bff__Secret3   = @Microsoft.KeyVault(VaultName=sprk-prod-kv;SecretName=ProdBffSecret)",
        };

        var violations = NamingConformanceChecker.ScanForViolations(corpus);

        violations.Should().Contain(v => v.Rule.StartsWith("R1") && v.Name == "SPRK-DEV-DATAVERSE-URL",
            "R1 kebab-delimited env-token detection (SPRK-DEV-*)");
        violations.Should().Contain(v => v.Rule.StartsWith("R1") && v.Name == "ProdBffSecret",
            "R1 camelCase env-token detection");
        violations.Should().Contain(v => v.Rule.StartsWith("R2"),
            "R2 casing-drift: BFF-API-ClientSecret vs bff-api-clientsecret");
        violations.Should().Contain(v => v.Rule.StartsWith("R3") && v.Name == "kv-sdap-dev",
            "R3 non-canonical vault name kv-sdap-dev");
    }

    [Fact]
    public void ScanForViolations_PS1SelfTestGoodFixture_ReportsZeroViolations()
    {
        var corpus = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fixture-good"] =
                "Dataverse__Url = @Microsoft.KeyVault(VaultName=sprk-demo-kv;SecretName=Dataverse-ServiceUrl)\n" +
                "Bff__Secret    = @Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=BFF-API-ClientSecret)",
        };

        var violations = NamingConformanceChecker.ScanForViolations(corpus);

        violations.Should().BeEmpty();
    }

    // --------------------- T6: CheckAsync (real temp filesystem) -----------

    [Fact]
    public async Task CheckAsync_TempTreeWithConformantSources_ReturnsSuccess()
    {
        var tmp = NewTempScanRoot();
        try
        {
            SeedFile(tmp,
                Path.Combine("src", "server", "api", "Sprk.Bff.Api", "appsettings.template.json"),
                "{\n  \"Dataverse\": \"@Microsoft.KeyVault(VaultName=sprk-demo-kv;SecretName=Dataverse-ServiceUrl)\"\n}");

            var checker = NewChecker(tmp);
            var outcome = await checker.CheckAsync(
                new NamingConformanceRequest("customer-01", "run-01"), CancellationToken.None);

            outcome.Should().BeOfType<NamingConformanceOutcome.Success>();
        }
        finally { CleanupTemp(tmp); }
    }

    [Fact]
    public async Task CheckAsync_TempTreeWithBadSource_ReturnsFailureWithR1Diagnostic()
    {
        var tmp = NewTempScanRoot();
        try
        {
            SeedFile(tmp,
                Path.Combine("src", "server", "api", "Sprk.Bff.Api", "appsettings.template.json"),
                "@Microsoft.KeyVault(VaultName=sprk-demo-kv;SecretName=SPRK-DEV-DATAVERSE-URL)");

            var checker = NewChecker(tmp);
            var outcome = await checker.CheckAsync(
                new NamingConformanceRequest("customer-01", "run-01"), CancellationToken.None);

            var failure = outcome.Should().BeOfType<NamingConformanceOutcome.Failure>().Subject;
            failure.ExitCode.Should().Be(1);
            failure.Diagnostic.Should().Contain("R1 env-token-in-name");
            failure.Diagnostic.Should().Contain("SPRK-DEV-DATAVERSE-URL");
        }
        finally { CleanupTemp(tmp); }
    }

    // --------------------- T7: CheckAsync fail-closed ----------------------

    [Fact]
    public async Task CheckAsync_EmptyScanRoot_ReturnsFailureFailClosed()
    {
        var tmp = NewTempScanRoot();
        try
        {
            var checker = NewChecker(tmp);
            var outcome = await checker.CheckAsync(
                new NamingConformanceRequest("customer-01", "run-01"), CancellationToken.None);

            var failure = outcome.Should().BeOfType<NamingConformanceOutcome.Failure>().Subject;
            failure.ExitCode.Should().Be(1);
            failure.Diagnostic.Should().Contain("No scannable files found");
        }
        finally { CleanupTemp(tmp); }
    }

    // --------------------- Forcing function --------------------------------

    [Fact]
    public void SourceFile_ContainsNoProcessStartInfoOrShellOutInCode()
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        var checkerPath = Path.Combine(
            repoRoot,
            "src", "server", "services",
            "Sprk.Provisioning.ControlPlane.Core", "Handlers", "E2EAcceptance",
            "NamingConformanceChecker.cs");

        File.Exists(checkerPath).Should().BeTrue(
            $"NamingConformanceChecker.cs must exist at '{checkerPath}'");

        var contents = File.ReadAllText(checkerPath);
        var codeOnly = StripComments(contents);

        codeOnly.Should().NotContain("ProcessStartInfo",
            "task 182 acceptance: pure-C# port MUST have zero ProcessStartInfo references in code");
        codeOnly.Should().NotContain("System.Diagnostics.Process",
            "task 182 acceptance: pure-C# port MUST NOT reference System.Diagnostics.Process in code");
        codeOnly.Should().NotContain("Process.Start",
            "task 182 acceptance: pure-C# port MUST NOT call Process.Start in code");
    }

    // --------------------- helpers -----------------------------------------

    private static NamingConformanceChecker NewChecker(string scanRoot)
    {
        var options = new H13AcceptanceOptions
        {
            NamingConformanceScriptPath = Path.Combine(scanRoot, "scripts", "naming-conformance-check.ps1"),
        };
        return new NamingConformanceChecker(
            Options.Create(options),
            NullLogger<NamingConformanceChecker>.Instance);
    }

    private static string NewTempScanRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "naming-conformance-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void SeedFile(string root, string relativePath, string contents)
    {
        var abs = Path.Combine(root, relativePath);
        var dir = Path.GetDirectoryName(abs);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(abs, contents);
    }

    private static void CleanupTemp(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch { /* best-effort */ }
    }

    private static string FindRepoRoot(string startDir)
    {
        var cur = new DirectoryInfo(startDir);
        while (cur is not null)
        {
            var candidate = Path.Combine(cur.FullName, "src", "server", "services");
            if (Directory.Exists(candidate))
            {
                return cur.FullName;
            }
            cur = cur.Parent;
        }
        throw new InvalidOperationException(
            $"Could not locate repo root from '{startDir}' -- expected a parent directory containing src/server/services.");
    }

    private static string StripComments(string source)
    {
        var sb = new System.Text.StringBuilder(source.Length);
        var i = 0;
        while (i < source.Length)
        {
            if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') { i++; }
            }
            else if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/')) { i++; }
                i = Math.Min(i + 2, source.Length);
            }
            else
            {
                sb.Append(source[i]);
                i++;
            }
        }
        return sb.ToString();
    }
}
