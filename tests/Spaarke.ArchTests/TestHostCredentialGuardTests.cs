using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Every <c>WebApplicationFactory&lt;Program&gt;</c> test fixture must replace the host's
/// <c>TokenCredential</c> with the stub, so no test host ever authenticates for real.
///
/// <para><b>The defect.</b> <c>Program.cs</c> registers <c>TokenCredential</c> as
/// <c>ManagedIdentityCredentialFactory.Create(...)</c> — a <c>DefaultAzureCredential</c>. A fixture
/// that does not replace it inherits the real one, and the first outbound-authenticating request in
/// that host runs the real probe chain: environment, workload identity, IMDS (169.254.169.254),
/// Azure CLI. Off Azure, the IMDS leg does not fail fast and the request blocks until
/// <c>HttpClient</c>'s 100-second default timeout aborts it.</para>
///
/// <para><b>Why this needs a guard rather than a convention.</b> The symptom does not point at the
/// cause. <c>DefaultAzureCredential</c> caches which source answered, so only the FIRST caller in a
/// host pays: the failing set rotates between runs, every failure lasts ~100s no matter what it
/// asserts, and a test can pass in the full suite while failing on its own. Read individually they
/// look like unrelated assertion bugs — which is how one root cause was investigated as five
/// separate problems across two sessions before the durations were compared. A fixture that omits
/// one line is invisible until it costs someone a day.</para>
///
/// <para>Same instrument and reasoning as <see cref="SessionOwnershipGuardTests"/>: the fix is one
/// line per fixture, and one line per fixture is precisely what decays.</para>
/// </summary>
public class TestHostCredentialGuardTests
{
    private static readonly string TestsRoot = Path.Combine(SourceScan.RepoRoot, "tests");

    /// <summary>A type declaring itself a <c>WebApplicationFactory&lt;Program&gt;</c> subclass.</summary>
    private static readonly Regex FactoryDeclaration = new(
        @":\s*WebApplicationFactory<\s*Program\s*>", RegexOptions.Compiled);

    private const string StubCall = "UseStubTokenCredential()";

    [Fact]
    public void EveryTestHostFactoryStubsItsTokenCredential()
    {
        var offenders = new List<string>();

        foreach (var file in TestSourceFiles())
        {
            var code = File.ReadAllText(file);

            // Comment-only mentions are not declarations. Several files DESCRIBE using the real app
            // via WebApplicationFactory<Program> in prose while their fixture lives elsewhere; the
            // declaration regex requires the `: WebApplicationFactory<Program>` base-type syntax, so
            // those are correctly ignored rather than reported as missing a call they cannot make.
            if (!FactoryDeclaration.IsMatch(code))
            {
                continue;
            }

            if (!code.Contains(StubCall, StringComparison.Ordinal))
            {
                offenders.Add(Path.GetRelativePath(SourceScan.RepoRoot, file).Replace('\\', '/'));
            }
        }

        Assert.True(offenders.Count == 0,
            "Test host factory/factories do not stub TokenCredential, so they will authenticate for\n"
            + "REAL. Symptom: one test in the host hangs ~100s (HttpClient's default timeout) and the\n"
            + "rest pass — and WHICH test is arbitrary, because DefaultAzureCredential caches after the\n"
            + "first call. Add the call inside ConfigureTestServices:\n\n"
            + "    builder.ConfigureTestServices(services =>\n"
            + "    {\n"
            + "        services.UseStubTokenCredential();   // <-- this\n"
            + "        ...\n"
            + "    });\n\n"
            + "See tests/integration/Shared/TestTokenCredential.cs for the full diagnosis.\n\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void NegativeControl_TheDeclarationDetectorMatchesTheRealShapes()
    {
        // A guard that matches nothing passes forever. These are the declaration forms in the tree.
        Assert.Matches(FactoryDeclaration, "public sealed class ComposeCreateOnSaveFixture : WebApplicationFactory<Program>");
        Assert.Matches(FactoryDeclaration, "public class CustomWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime");
        Assert.Matches(FactoryDeclaration, "internal sealed class F : WebApplicationFactory< Program >");

        // And must NOT fire on prose that merely names the type — the three files that do this are
        // real, and reporting them would train the next reader to add exemptions to a guard rather
        // than fix it.
        Assert.DoesNotMatch(FactoryDeclaration, "// Uses real WebApplicationFactory<Program> — no Mock<HttpMessageHandler>.");
        Assert.DoesNotMatch(FactoryDeclaration, "// CI runners + WebApplicationFactory<Program> fixtures use");
    }

    [Fact]
    public void TheGuardActuallyScansFixtures()
    {
        // Guards against the vacuous pass: if TestsRoot were wrong or the enumeration empty, the rule
        // above would be green while checking nothing at all.
        var declaring = TestSourceFiles()
            .Count(f => FactoryDeclaration.IsMatch(File.ReadAllText(f)));

        Assert.True(declaring >= 40,
            $"Expected the tree to contain many WebApplicationFactory<Program> fixtures; found {declaring}. "
            + "Either the test-source enumeration is broken (making the rule above vacuous) or the "
            + "fixtures were consolidated — verify which before lowering this floor.");
    }

    private static IEnumerable<string> TestSourceFiles()
        => Directory
            .EnumerateFiles(TestsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
}
