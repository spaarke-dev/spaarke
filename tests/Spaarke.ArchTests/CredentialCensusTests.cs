using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// FR-F2 (spaarke-auth-v4-dataverse-MI task 061) — the credential census. Every place the server
/// constructs a confidential client is listed here with its credential source and a one-line reason, and
/// the list is asserted against the source. An unlisted confidential client cannot appear silently.
///
/// <para><b>The specific failure this exists to prevent.</b> The origin assessment for this project
/// counted <b>five</b> confidential-client sites. The real number was <b>eight</b>. The two it missed —
/// <c>SpeAdminTokenProvider</c> and <c>SpeAdminGraphService</c> — are per-customer SpeAdmin paths that
/// simply were not in the seed's inventory, and the miss was discovered by a later audit rather than by
/// anything automatic. A census makes the COUNT itself the assertion, so the next miss is a build failure
/// instead of a discovery two projects later.</para>
///
/// <para><b>Relationship to <see cref="CredentialGuardTests"/>.</b> They are complementary and neither
/// subsumes the other. The guard bans <i>secret</i> bindings outside an allowlist — it says nothing about
/// a new confidential client built with a certificate or an assertion. This census counts <i>every</i>
/// confidential client regardless of credential kind — it says nothing about whether the credential is
/// appropriate. Together: nothing new appears unnoticed, and nothing new appears secret-bearing.</para>
///
/// <para><b>Scans ALL server assemblies, and that is load-bearing rather than thorough-by-habit.</b>
/// <c>ADR010_DITests</c> scans <c>typeof(Program).Assembly</c> — the BFF only — which is why the
/// cross-assembly <c>IClientAssertionProvider</c> seam (contract in <c>Spaarke.Dataverse</c>,
/// implementation in the BFF) was invisible to it at task 020, and why a ceiling raise that looked
/// necessary turned out not to be. A credential census with the same blind spot would under-report BY
/// CONSTRUCTION, because the entire seam this project builds is cross-assembly. Source scanning over
/// <c>src/server/**</c> covers all three projects inherently, and
/// <see cref="Census_FiresOnASiteOutsideTheBffAssembly"/> proves it rather than assuming it.</para>
///
/// <para><b>No DI resolution anywhere in this file</b> — ADR-038 ban <b>B3</b>. A census implemented as
/// <c>Assert.NotNull(services.GetRequiredService&lt;X&gt;())</c> would be the very anti-pattern this task
/// exists to prevent, and it would also be blind to any site that is not DI-registered — which is most of
/// them.</para>
/// </summary>
public class CredentialCensusTests
{
    // =============================================================================================
    // THE CENSUS
    // ---------------------------------------------------------------------------------------------
    // MAINTENANCE PROCEDURE — read before changing a number here.
    //
    //   1. A failure here is NOT a signal to update the number. It is a signal to ask why a new
    //      confidential client exists. Since auth-v4 task 022, code authenticating as the BFF's own
    //      identity goes through OrderedCredentialClientProvider and adds NO site. If your change added
    //      one, the first question is whether it should have.
    //
    //   2. If the site is justified, add or increment the entry AND write the reason — what identity it
    //      authenticates as, and why that identity cannot come from the provider. A census entry without
    //      a reason is just a bigger number, and a bigger number is what the previous inventory had.
    //
    //   3. Counts are per FILE, not a global total, deliberately. A global total is satisfiable by
    //      accident — remove one site, add another, and the census still passes while the estate has
    //      changed. Per-file counts localise the failure to the file that changed.
    //
    //   4. When task 033 removes BFF-API-ClientSecret, the provider's CredentialSource line changes from
    //      "MI-FIC, then certificate, then transitional secret" to drop the secret. If it still says
    //      "transitional secret" after 033, the migration did not finish.
    //
    // =============================================================================================
    private static readonly IReadOnlyList<CensusEntry> Census = new[]
    {
        new CensusEntry(
            FileName: "OrderedCredentialClientProvider.cs",
            Sites: 1,
            Identity: "The BFF's own app registration",
            CredentialSource: "Ordered selection: MI-FIC, then Key Vault certificate, then the transitional secret (ADR-028 A4 / E-3)",
            Reason:
                "THE consolidated site. Auth-v4 task 022 removed the four OBO clients and five app-only "
                + "credential constructions and routed all of them here. This entry is a REDUCTION from "
                + "the previous estate, not an addition to it."),

        new CensusEntry(
            FileName: "CiamGraphClientFactory.cs",
            Sites: 1,
            Identity: "The CIAM tenant's Graph provisioner app registration",
            CredentialSource: "Key Vault certificate (secret-free)",
            Reason:
                "The in-repo secret-free precedent that proved confidential auth without a secret was "
                + "already possible here. Different tenant and different app registration from the BFF's, "
                + "so it cannot use the BFF's credential provider."),

        new CensusEntry(
            FileName: "SpeAdminTokenProvider.cs",
            Sites: 1,
            Identity: "Per-container-type OWNING APPLICATION registrations (not the BFF's)",
            CredentialSource: "Client secret fetched from Key Vault per request, by a secret name held in Dataverse",
            Reason:
                "ADR-028 E-1. These are other customers' application identities; MI-FIC would have to be "
                + "federated onto each of their own app registrations, which is not this project's to do."),

        new CensusEntry(
            FileName: "SpeAdminGraphService.cs",
            Sites: 2,
            Identity: "Per-business-unit OWNING APPLICATION registrations (not the BFF's)",
            CredentialSource: "Client secret fetched from Key Vault, by a name resolved from Dataverse configuration",
            Reason:
                "ADR-028 E-1, same as SpeAdminTokenProvider. TWO sites in this file — the count is "
                + "explicit so that removing one and adding another elsewhere in the file cannot pass "
                + "unnoticed. These are Azure.Identity credentials rather than MSAL clients; the census "
                + "counts confidential clients by function, not by SDK."),

        new CensusEntry(
            FileName: "ReportingEmbedService.cs",
            Sites: 1,
            Identity: "The Power BI service principal (a genuinely separate identity)",
            CredentialSource: "PowerBi:ClientSecret — STILL SECRET-BEARING",
            Reason:
                "Workstream D deferred 2026-08-19 -- Power BI not yet adopted at Spaarke; "
                + "PowerBi:ClientSecret is a separate secret from BFF-API-ClientSecret and does not gate "
                + "the OBO migration. Revisit when Power BI is adopted (tasks 040-042)."),

        new CensusEntry(
            FileName: "ReportingProfileManager.cs",
            Sites: 1,
            Identity: "The Power BI service principal, using service-principal PROFILES",
            CredentialSource: "PowerBi:ClientSecret — STILL SECRET-BEARING",
            Reason:
                "Workstream D deferred 2026-08-19 -- Power BI not yet adopted at Spaarke; "
                + "PowerBi:ClientSecret is a separate secret from BFF-API-ClientSecret and does not gate "
                + "the OBO migration. Revisit when Power BI is adopted (tasks 040-042). Whether Power BI "
                + "service-principal profiles are even supported under a managed identity is still an "
                + "open question (DEF-001) and gates tasks 041-042."),
    };

    /// <summary>
    /// What counts as constructing a confidential client. Both SDKs, because both appear in this codebase
    /// and the distinction is an implementation detail of the site, not of the security property.
    /// </summary>
    private static readonly (Regex Pattern, string Kind)[] ConstructionForms =
    {
        (new Regex(@"ConfidentialClientApplicationBuilder\s*\.\s*Create\s*\(", RegexOptions.Compiled),
            "MSAL confidential client"),
        (new Regex(@"new\s+(ClientSecretCredential|ClientAssertionCredential|ClientCertificateCredential)\s*\(", RegexOptions.Compiled),
            "Azure.Identity confidential credential"),
    };

    // =============================================================================================

    [Fact(DisplayName = "FR-F2: every confidential-client construction site is in the census, with the expected count")]
    public void EveryConfidentialClientSiteIsCensused()
    {
        var actual = ScanSites();
        var expected = Census.ToDictionary(e => e.FileName, e => e.Sites, StringComparer.Ordinal);

        var problems = new List<string>();

        foreach (var (fileName, sites) in actual.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!expected.TryGetValue(fileName, out var expectedCount))
            {
                problems.Add(
                    $"UNLISTED confidential-client site in {fileName}:\n"
                    + string.Join("\n", sites.Select(s => $"    {s}")));
                continue;
            }

            if (sites.Count != expectedCount)
            {
                problems.Add(
                    $"COUNT CHANGED in {fileName}: census says {expectedCount}, source has {sites.Count}:\n"
                    + string.Join("\n", sites.Select(s => $"    {s}")));
            }
        }

        foreach (var fileName in expected.Keys.Where(f => !actual.ContainsKey(f)))
        {
            problems.Add(
                $"CENSUSED BUT ABSENT: {fileName} is in the census but has no confidential-client site. "
                + "If it was migrated or deleted, remove the entry — a census that over-reports is as "
                + "misleading as one that under-reports.");
        }

        Assert.True(
            problems.Count == 0,
            "The credential census does not match the source.\n\n"
            + "A failure here is NOT a prompt to update the number. The origin assessment for this "
            + "project counted five confidential-client sites when there were eight, and the two it "
            + "missed were found by a later audit rather than by anything automatic. Ask first whether "
            + "the new site should exist: since auth-v4 task 022, code that authenticates as the BFF's "
            + "own identity goes through OrderedCredentialClientProvider and adds no site.\n\n"
            + "If it is justified, add the entry WITH its identity, credential source and reason — see "
            + "the MAINTENANCE PROCEDURE above the census.\n\n"
            + string.Join("\n\n", problems));
    }

    [Fact(DisplayName = "FR-F2: every census entry carries an identity, a credential source and a reason")]
    public void EveryCensusEntryIsExplained()
    {
        // Without this, the census degrades into a list of numbers — which is exactly what the previous
        // inventory was, and it was wrong by three.
        var unexplained = Census
            .Where(e => string.IsNullOrWhiteSpace(e.Identity)
                        || string.IsNullOrWhiteSpace(e.CredentialSource)
                        || string.IsNullOrWhiteSpace(e.Reason)
                        || e.Reason.Trim().Length < 60)
            .Select(e => e.FileName)
            .ToList();

        Assert.True(
            unexplained.Count == 0,
            "Every census entry must name the identity it authenticates as, its credential source, and a "
            + "substantive reason it cannot come from the shared provider. Entries missing one: "
            + string.Join(", ", unexplained));
    }

    [Fact(DisplayName = "FR-F2: the census records that the Power BI sites are STILL secret-bearing")]
    public void CensusTellsTheTruthAboutWhatIsStillSecretBearing()
    {
        // The census's honesty is its whole value. Power BI was DEFERRED, not migrated, and a census that
        // quietly counted it as done would recreate the exact condition this project exists to fix: a
        // document asserting the estate is clean when it is not.
        var powerBi = Census
            .Where(e => e.FileName is "ReportingEmbedService.cs" or "ReportingProfileManager.cs")
            .ToList();

        Assert.Equal(2, powerBi.Count);

        foreach (var entry in powerBi)
        {
            Assert.Contains("STILL SECRET-BEARING", entry.CredentialSource, StringComparison.Ordinal);
            Assert.Contains("Workstream D deferred 2026-08-19", entry.Reason, StringComparison.Ordinal);
            Assert.Contains("tasks 040-042", entry.Reason, StringComparison.Ordinal);
        }
    }

    // =============================================================================================
    // Negative controls
    // =============================================================================================

    [Fact(DisplayName = "FR-F2: negative control — the detector finds both construction forms")]
    public void Detector_NegativeControl_FindsBothForms()
    {
        var msal = new[]
        {
            "    var app = ConfidentialClientApplicationBuilder",
            "        .Create(clientId)",
            "        .WithClientSecret(secret)",
            "        .Build();",
        };

        var azureIdentity = new[] { "    var cred = new ClientSecretCredential(t, c, s);" };

        Assert.Single(FindSites(msal));
        Assert.Single(FindSites(azureIdentity));

        // And the two shapes that must NOT count. The first is a real line in
        // OrderedCredentialClientProvider — a record parameter naming the builder TYPE without
        // constructing anything. Counting it would inflate the census against its own sanctioned site.
        Assert.Empty(FindSites(new[]
        {
            "        Func<ConfidentialClientApplicationBuilder, ConfidentialClientApplicationBuilder> Apply,",
        }));
        Assert.Empty(FindSites(new[]
        {
            "        // var app = ConfidentialClientApplicationBuilder.Create(id).Build();",
            "        /// <see cref=\"ConfidentialClientApplicationBuilder\"/>.Create is documented here.",
        }));
    }

    [Fact(DisplayName = "FR-F2: negative control — a construction site OUTSIDE the BFF assembly is counted")]
    public void Census_FiresOnASiteOutsideTheBffAssembly()
    {
        // The blind-spot control, booked by task 020. ADR010_DITests scans typeof(Program).Assembly, so a
        // site in Spaarke.Dataverse or Spaarke.Core is invisible to it — and the whole credential seam
        // this project builds is cross-assembly (IConfidentialClientProvider is declared in
        // Spaarke.Dataverse, ConfidentialClientTokenCredential lives there too). An assembly-scoped
        // census would pass this project while missing the parts of it that matter most.
        //
        // Asserted two ways. First, that the scan actually reaches those directories at all — a census
        // that silently enumerated zero files would pass every other test in this class.
        var scannedRoots = SourceScan.ServerSourceFiles()
            .Select(SourceScan.Relative)
            .ToList();

        Assert.Contains(scannedRoots, f => f.Contains("Spaarke.Dataverse", StringComparison.Ordinal));
        Assert.Contains(scannedRoots, f => f.Contains("Spaarke.Core", StringComparison.Ordinal));
        Assert.Contains(scannedRoots, f => f.Contains("Sprk.Bff.Api", StringComparison.Ordinal));

        // Second, that a site with a Spaarke.Dataverse shape is detected by the same detector the census
        // uses — the shared-library credential shape, not a BFF one.
        var sharedLibrarySite = new[]
        {
            "namespace Spaarke.Dataverse;",
            "internal static class ScratchSharedLibraryClient",
            "{",
            "    public static IConfidentialClientApplication Build(string t, string c, string s) =>",
            "        ConfidentialClientApplicationBuilder",
            "            .Create(c)",
            "            .WithClientSecret(s)",
            "            .Build();",
            "}",
        };

        Assert.Single(FindSites(sharedLibrarySite));
    }

    // =============================================================================================
    // Machinery — source analysis only. No DI container is constructed or resolved in this file.
    // =============================================================================================

    private sealed record CensusEntry(
        string FileName,
        int Sites,
        string Identity,
        string CredentialSource,
        string Reason);

    /// <summary>File name → the construction sites found in it, as <c>line: kind</c> strings.</summary>
    private static Dictionary<string, List<string>> ScanSites()
    {
        var found = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var sites = FindSites(File.ReadAllLines(file));
            if (sites.Count > 0)
            {
                found[Path.GetFileName(file)] = sites;
            }
        }

        return found;
    }

    /// <summary>
    /// Construction sites in one file's lines. Matched over the whole comment-stripped text rather than
    /// line by line or statement by statement: a fluent builder chain puts the type on one line and
    /// <c>.Create(</c> on the next, and the provider's chain contains an interpolated string whose brace
    /// a statement splitter treats as a boundary. Whole-text matching sidesteps both.
    /// </summary>
    private static List<string> FindSites(IReadOnlyList<string> lines)
    {
        var text = SourceScan.CodeText(lines);
        var sites = new List<string>();

        foreach (var (pattern, kind) in ConstructionForms)
        {
            foreach (Match match in pattern.Matches(text))
            {
                sites.Add($"line {SourceScan.LineOf(text, match.Index)}: {kind} -- {match.Value.Trim()}");
            }
        }

        return sites;
    }
}
