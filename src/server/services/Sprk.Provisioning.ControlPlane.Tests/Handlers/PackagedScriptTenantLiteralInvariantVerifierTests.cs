// -----------------------------------------------------------------------------
// PackagedScriptTenantLiteralInvariantVerifierTests.cs
//
// Unit tests over PackagedScriptTenantLiteralInvariantVerifier (task 170,
// Wave G-7 Batch G-7A1 — I1 packaged-scripts on-disk grep probe).
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. The probe touches the local filesystem
//   (Directory.GetFiles + File.ReadAllText) but never hits Azure / Graph /
//   Dataverse. Tests author temp .ps1 files under Path.GetTempPath() with
//   a unique GUID-suffixed directory (torn down in finally) — the exact
//   pattern the ArchTest I1_NoHardcodedTenantTests.
//   ScanForI1Offenders_TempScriptWithTenantDefault_ReportsFileAndLine test
//   uses, so we prove the runtime probe catches a REAL FILE, not only an
//   in-memory regex.
//
// COVERAGE (POML acceptance criteria mapped to test cases):
//   AC-1a  Clean scripts dir (compliant Mandatory=$true Param) → Passed(I1).
//   AC-1b  Clean scripts dir (empty-string default) → Passed(I1).
//   AC-2a  One offender file (pre-1834b77bc shape) → Failed(I1) with
//          {relPath}:{line} in the diagnostic.
//   AC-2b  Multiple offender files across nested subdirs → Failed(I1)
//          enumerating BOTH; deterministic ordinal ordering.
//   AC-3a  Non-tenant parameter (e.g. $SubscriptionId) with GUID default →
//          Passed(I1) — regex narrows to $*Tenant* names.
//   AC-3b  Tenant-shaped GUID in a function-body VARIABLE (outside Param())
//          → Passed(I1) — regex narrows to Param() blocks only.
//   AC-4a  Empty scriptsDirectory string → InfraFault(I1).
//   AC-4b  Non-existent scriptsDirectory → InfraFault(I1).
//   AC-4c  Existing scriptsDirectory with ZERO .ps1 files → InfraFault(I1)
//          (fail-LOUD; not silent-Pass — matches silent-fail discipline).
//   AC-5   VerifyAllAsync returns 5 outcomes in enum order — I1 is real,
//          I2-I5 are InfraFault with the wave-G7 deferral message.
//   AC-6   VerifyAllAsync happy path (clean dir) → I1 Passed, I2-I5 InfraFault.
//   AC-7   VerifyAllAsync sad path (offender dir) → I1 Failed, I2-I5 InfraFault
//          (I1 failure does NOT affect the I2-I5 outcomes).
//   AC-8   (intentionally omitted — ADR-038 / tests/CLAUDE.md B4 bans
//          constructor null-argument tests; language guarantee, not behavior.)
//   AC-9   Multi-tenant-named parameter variants (Case: $tenantId, $TENANTID,
//          $MyTenantThing) all caught.
//   AC-10  Balanced-paren extraction — Param block with nested parens in a
//          default expression does not prematurely terminate.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class PackagedScriptTenantLiteralInvariantVerifierTests
{
    // Tenant-shaped GUID used ONLY in temp .ps1 file content strings that we
    // write to disk and then scan. Deliberately not a real Spaarke tenant id.
    // Kept as a string literal — inside a C# test file, not inside a .ps1 file
    // under scripts/, so the Wave-C6 ArchTest never sees it.
    private const string SampleTenantShapedGuid = "11111111-2222-3333-4444-555555555555";
    private const string SecondSampleTenantShapedGuid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    private const string CustomerId = "acme";
    private const string RunId = "01j-i1-probe-run";
    private const string BffApiUrl = "https://sprk-bff-acme.azurewebsites.net";
    private const string SearchEndpoint = "https://sprk-acme-search.search.windows.net";
    private const string CosmosEndpoint = "https://sprk-acme-cosmos.documents.azure.com";
    private const string SampleSubscriptionId = "sub-acme-prod";
    private const string SampleTenantIdInEnvelope = "00000000-1111-2222-3333-444444444444";

    // -------------------------------------------------------------------------
    // AC-1: Clean directory → Passed(I1)
    // -------------------------------------------------------------------------

    [Fact]
    public void ProbeI1_CleanDirectoryWithMandatoryParam_ReturnsPassed()
    {
        RunInTempScriptsDir(dir =>
        {
            WriteScript(dir, "Provision-Customer.ps1",
                "param(\n" +
                "    [Parameter(Mandatory=$true)][string]$TenantId,\n" +
                "    [Parameter(Mandatory=$true)][string]$CustomerId\n" +
                ")\n" +
                "Write-Host \"provisioning $CustomerId in $TenantId\"\n");

            var outcome = PackagedScriptTenantLiteralInvariantVerifier.ProbeI1(dir);

            outcome.Should().BeOfType<InvariantVerificationOutcome.Passed>()
                .Which.Kind.Should().Be(InvariantKind.I1NoHardcodedTenant);
        });
    }

    [Fact]
    public void ProbeI1_CleanDirectoryWithEmptyStringDefault_ReturnsPassed()
    {
        RunInTempScriptsDir(dir =>
        {
            WriteScript(dir, "New-CustomerEnv.ps1",
                "param([string]$TenantId = '')\n" +
                "Write-Host \"tenant is $TenantId\"\n");

            var outcome = PackagedScriptTenantLiteralInvariantVerifier.ProbeI1(dir);

            outcome.Should().BeOfType<InvariantVerificationOutcome.Passed>();
        });
    }

    // -------------------------------------------------------------------------
    // AC-2: Offender → Failed(I1) with file:line diagnostic
    // -------------------------------------------------------------------------

    [Fact]
    public void ProbeI1_OneOffenderFile_ReturnsFailedWithFileAndLineDiagnostic()
    {
        RunInTempScriptsDir(dir =>
        {
            // Pre-1834b77bc shape — tenant-shaped default on line 3.
            var scriptText =
                "# Sample provisioning script.\n" +
                "param(\n" +
                "    [Parameter()][string]$TenantId = '" + SampleTenantShapedGuid + "'\n" +
                ")\n";
            WriteScript(dir, "Register-EntraAppRegistrations.ps1", scriptText);

            var outcome = PackagedScriptTenantLiteralInvariantVerifier.ProbeI1(dir);

            var failed = outcome.Should().BeOfType<InvariantVerificationOutcome.Failed>().Subject;
            failed.Kind.Should().Be(InvariantKind.I1NoHardcodedTenant);
            failed.Diagnostic.Should().Contain("Register-EntraAppRegistrations.ps1");
            failed.Diagnostic.Should().Contain(":3");
            failed.Diagnostic.Should().Contain("$TenantId");
            failed.Diagnostic.Should().Contain(SampleTenantShapedGuid);
            failed.Diagnostic.Should().Contain("§4D I1");
            failed.Diagnostic.Should().Contain("Mandatory=$true");
        });
    }

    [Fact]
    public void ProbeI1_MultipleOffendersAcrossNestedSubdirs_EnumeratesBothDeterministically()
    {
        RunInTempScriptsDir(dir =>
        {
            var subDir = Path.Combine(dir, "identity");
            Directory.CreateDirectory(subDir);

            WriteScript(subDir, "AaaFirst.ps1",
                "param([Parameter()][string]$TenantId = '" + SampleTenantShapedGuid + "')\n");
            WriteScript(dir, "ZzzSecond.ps1",
                "param([Parameter()][string]$MyTenantThing = '" + SecondSampleTenantShapedGuid + "')\n");

            var outcome = PackagedScriptTenantLiteralInvariantVerifier.ProbeI1(dir);

            var failed = outcome.Should().BeOfType<InvariantVerificationOutcome.Failed>().Subject;
            failed.Diagnostic.Should().Contain("Offenders (2)");
            failed.Diagnostic.Should().Contain("identity/AaaFirst.ps1");
            failed.Diagnostic.Should().Contain("ZzzSecond.ps1");
            failed.Diagnostic.Should().Contain(SampleTenantShapedGuid);
            failed.Diagnostic.Should().Contain(SecondSampleTenantShapedGuid);
            // Ordinal ordering — "identity/AaaFirst.ps1" starts with 'i',
            // "ZzzSecond.ps1" starts with 'Z' (uppercase Z < lowercase i in
            // ordinal), so ZzzSecond should appear first.
            failed.Diagnostic.IndexOf("ZzzSecond.ps1", StringComparison.Ordinal)
                .Should()
                .BeLessThan(failed.Diagnostic.IndexOf("identity/AaaFirst.ps1", StringComparison.Ordinal));
        });
    }

    // -------------------------------------------------------------------------
    // AC-3: False-positive discipline mirrored from the ArchTest
    // -------------------------------------------------------------------------

    [Fact]
    public void ProbeI1_NonTenantParameterWithGuidDefault_ReturnsPassed()
    {
        RunInTempScriptsDir(dir =>
        {
            // A subscription-ID default is legitimate for env-per-env ops
            // scripts — regex narrows to $*Tenant* names.
            WriteScript(dir, "Deploy-Env.ps1",
                "param([string]$SubscriptionId = '" + SampleTenantShapedGuid + "')\n");

            var outcome = PackagedScriptTenantLiteralInvariantVerifier.ProbeI1(dir);

            outcome.Should().BeOfType<InvariantVerificationOutcome.Passed>();
        });
    }

    [Fact]
    public void ProbeI1_TenantGuidInFunctionBodyVariable_ReturnsPassed()
    {
        RunInTempScriptsDir(dir =>
        {
            // A local variable in a function body is NOT a Param() default —
            // regex scoping mirrors the ArchTest.
            WriteScript(dir, "Ops-Utility.ps1",
                "param([Parameter(Mandatory=$true)][string]$TenantId)\n" +
                "$fallbackTenantId = '" + SampleTenantShapedGuid + "'\n" +
                "Write-Host \"using tenant $TenantId (fallback would be $fallbackTenantId)\"\n");

            var outcome = PackagedScriptTenantLiteralInvariantVerifier.ProbeI1(dir);

            outcome.Should().BeOfType<InvariantVerificationOutcome.Passed>();
        });
    }

    // -------------------------------------------------------------------------
    // AC-4: InfraFault paths (silent-fail-audit LOUD-FAIL discipline)
    // -------------------------------------------------------------------------

    [Fact]
    public void ProbeI1_EmptyScriptsDirectoryString_ReturnsInfraFault()
    {
        var outcome = PackagedScriptTenantLiteralInvariantVerifier.ProbeI1("");

        var infra = outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>().Subject;
        infra.Kind.Should().Be(InvariantKind.I1NoHardcodedTenant);
        infra.Diagnostic.Should().Contain("empty");
    }

    [Fact]
    public void ProbeI1_NonExistentScriptsDirectory_ReturnsInfraFault()
    {
        var missing = Path.Combine(Path.GetTempPath(),
            "spaarke-i1-does-not-exist-" + Guid.NewGuid().ToString("N"));

        var outcome = PackagedScriptTenantLiteralInvariantVerifier.ProbeI1(missing);

        var infra = outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>().Subject;
        infra.Diagnostic.Should().Contain("does not exist");
        infra.Diagnostic.Should().Contain(missing);
        infra.Diagnostic.Should().Contain("false Pass");
    }

    [Fact]
    public void ProbeI1_ScriptsDirectoryWithNoPs1Files_ReturnsInfraFaultNotSilentPass()
    {
        RunInTempScriptsDir(dir =>
        {
            // Populate with a non-.ps1 file so the directory is non-empty
            // but the .ps1 scan finds nothing.
            File.WriteAllText(Path.Combine(dir, "README.md"), "# Not a script");

            var outcome = PackagedScriptTenantLiteralInvariantVerifier.ProbeI1(dir);

            var infra = outcome.Should().BeOfType<InvariantVerificationOutcome.InfraFault>().Subject;
            infra.Diagnostic.Should().Contain("ZERO .ps1 files");
            infra.Diagnostic.Should().Contain("Resumable");
            infra.Diagnostic.Should().Contain("hollow scan");
        });
    }

    // -------------------------------------------------------------------------
    // AC-5..7: VerifyAllAsync (the interface contract)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task VerifyAllAsync_ReturnsFiveOutcomesInEnumOrder()
    {
        await RunInTempScriptsDirAsync(async dir =>
        {
            WriteScript(dir, "Provision-Customer.ps1",
                "param([Parameter(Mandatory=$true)][string]$TenantId)\n");

            var sut = new PackagedScriptTenantLiteralInvariantVerifier(
                NullLogger<PackagedScriptTenantLiteralInvariantVerifier>.Instance);

            var result = await sut.VerifyAllAsync(BuildRequest(dir), CancellationToken.None);

            result.Outcomes.Should().HaveCount(5);
            OutcomeKindOf(result.Outcomes[0]).Should().Be(InvariantKind.I1NoHardcodedTenant);
            OutcomeKindOf(result.Outcomes[1]).Should().Be(InvariantKind.I2AiSearchTenantFilter);
            OutcomeKindOf(result.Outcomes[2]).Should().Be(InvariantKind.I3CosmosPartitionKey);
            OutcomeKindOf(result.Outcomes[3]).Should().Be(InvariantKind.I4SpeContainerResolver);
            OutcomeKindOf(result.Outcomes[4]).Should().Be(InvariantKind.I5GraphTokenTenant);
        });
    }

    [Fact]
    public async Task VerifyAllAsync_HappyPath_I1PassedAndI2ThroughI5InfraFault()
    {
        await RunInTempScriptsDirAsync(async dir =>
        {
            WriteScript(dir, "Ok.ps1",
                "param([Parameter(Mandatory=$true)][string]$TenantId)\n");

            var sut = new PackagedScriptTenantLiteralInvariantVerifier(
                NullLogger<PackagedScriptTenantLiteralInvariantVerifier>.Instance);

            var result = await sut.VerifyAllAsync(BuildRequest(dir), CancellationToken.None);

            result.Outcomes[0].Should().BeOfType<InvariantVerificationOutcome.Passed>();
            for (int i = 1; i <= 4; i++)
            {
                var infra = result.Outcomes[i].Should()
                    .BeOfType<InvariantVerificationOutcome.InfraFault>().Subject;
                infra.Diagnostic.Should().Contain("task 170 landed the I1");
                infra.Diagnostic.Should().Contain("Wave G-7 sibling");
            }

            // Aggregate helpers reflect the mixed state honestly.
            result.AllInvariantsClear.Should().BeTrue(); // No Failed outcomes.
            result.AnyInfraFault.Should().BeTrue();
            result.FirstFailure.Should().BeNull();
        });
    }

    [Fact]
    public async Task VerifyAllAsync_SadPath_I1FailedAndI2ThroughI5Unaffected()
    {
        await RunInTempScriptsDirAsync(async dir =>
        {
            WriteScript(dir, "Bad.ps1",
                "param([Parameter()][string]$TenantId = '" + SampleTenantShapedGuid + "')\n");

            var sut = new PackagedScriptTenantLiteralInvariantVerifier(
                NullLogger<PackagedScriptTenantLiteralInvariantVerifier>.Instance);

            var result = await sut.VerifyAllAsync(BuildRequest(dir), CancellationToken.None);

            result.Outcomes[0].Should().BeOfType<InvariantVerificationOutcome.Failed>();
            for (int i = 1; i <= 4; i++)
            {
                result.Outcomes[i].Should().BeOfType<InvariantVerificationOutcome.InfraFault>();
            }

            result.AllInvariantsClear.Should().BeFalse();
            result.FirstFailure!.Kind.Should().Be(InvariantKind.I1NoHardcodedTenant);
        });
    }

    // -------------------------------------------------------------------------
    // AC-8: (Constructor null-arg test intentionally omitted — banned per
    // tests/CLAUDE.md B4 antipattern; ArgumentNullException.ThrowIfNull(logger)
    // in the ctor is a language guarantee, not a behavior to double-test.)
    // -------------------------------------------------------------------------

    // -------------------------------------------------------------------------
    // AC-9: Case + name variants
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("$tenantId")]            // camelCase
    [InlineData("$TENANTID")]            // ALL CAPS
    [InlineData("$MyTenantThing")]       // embedded name
    [InlineData("$CustomerTenantId")]    // multi-word
    public void ProbeI1_TenantNameVariants_AllCaught(string paramNameLiteral)
    {
        RunInTempScriptsDir(dir =>
        {
            WriteScript(dir, "NameVariant.ps1",
                "param([Parameter()][string]" + paramNameLiteral +
                " = '" + SampleTenantShapedGuid + "')\n");

            var outcome = PackagedScriptTenantLiteralInvariantVerifier.ProbeI1(dir);

            outcome.Should().BeOfType<InvariantVerificationOutcome.Failed>();
        });
    }

    // -------------------------------------------------------------------------
    // AC-10: Balanced-paren extraction robustness
    // -------------------------------------------------------------------------

    [Fact]
    public void ProbeI1_ParamBlockWithNestedParens_ExtractsCorrectlyAndCatchesOffender()
    {
        RunInTempScriptsDir(dir =>
        {
            // ValidateSet attribute on a preceding parameter has nested parens.
            WriteScript(dir, "Nested.ps1",
                "param(\n" +
                "    [ValidateSet('dev','prod','preview')][string]$Env = 'dev',\n" +
                "    [Parameter()][string]$TenantId = '" + SampleTenantShapedGuid + "'\n" +
                ")\n");

            var outcome = PackagedScriptTenantLiteralInvariantVerifier.ProbeI1(dir);

            outcome.Should().BeOfType<InvariantVerificationOutcome.Failed>()
                .Which.Diagnostic.Should().Contain("Nested.ps1");
        });
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static InvariantVerificationRequest BuildRequest(string scriptsDir) =>
        new(
            CustomerId: CustomerId,
            RunId: RunId,
            TenantId: SampleTenantIdInEnvelope,
            SubscriptionId: SampleSubscriptionId,
            AiSearchEndpoint: SearchEndpoint,
            CosmosEndpoint: CosmosEndpoint,
            BffApiUrl: BffApiUrl,
            ProvisioningScriptsDirectory: scriptsDir);

    private static InvariantKind OutcomeKindOf(InvariantVerificationOutcome o) => o switch
    {
        InvariantVerificationOutcome.Passed p => p.Kind,
        InvariantVerificationOutcome.Failed f => f.Kind,
        InvariantVerificationOutcome.InfraFault i => i.Kind,
        _ => throw new InvalidOperationException("unknown outcome"),
    };

    private static void WriteScript(string dir, string relativeName, string content)
    {
        var path = Path.Combine(dir, relativeName);
        var parent = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(parent))
        {
            Directory.CreateDirectory(parent);
        }
        File.WriteAllText(path, content);
    }

    private static void RunInTempScriptsDir(Action<string> body)
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "spaarke-i1-runtime-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            body(tempDir);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static async Task RunInTempScriptsDirAsync(Func<string, Task> body)
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "spaarke-i1-runtime-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            await body(tempDir);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
