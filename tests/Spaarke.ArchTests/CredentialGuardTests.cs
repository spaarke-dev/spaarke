using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// FR-F1 (spaarke-auth-v4-dataverse-MI task 060) — the executable form of ADR-028 Amendment <b>A4</b>:
/// no type under <c>src/server/**</c> may bind a client secret for the BFF's own identity outside a
/// named, reasoned allowlist.
///
/// <para><b>Why this exists at all.</b> Three prior audits inventoried every secret consumer in this
/// codebase correctly — at <c>file:line</c> — and then concluded the secret could never be removed, on
/// one false sentence in a constraints document. The text was corrected on 2026-08-17. <b>But text is
/// what failed last time.</b> The project's distinguishing success criterion is that introducing a
/// deliberate ninth secret-bearing confidential client on a scratch branch must FAIL THE BUILD; this
/// file and <c>CredentialCensusTests</c> are that criterion. A prose rule that a future audit can
/// re-reason its way past is exactly the failure mode being engineered out.</para>
///
/// <para><b>Source scan, not assembly scan, and deliberately so.</b> The idiom to forbid is syntactic —
/// a <c>.WithClientSecret(...)</c> call, a <c>new ClientSecretCredential(...)</c>, an
/// <c>AuthType=ClientSecret</c> connection string. Two of those three are not distinguishable in IL from
/// their neighbours (a connection string is just a string), and it is the SOURCE that a future
/// contributor writes and a reviewer reads. Same style as <c>DataverseServiceClientDowncastTests</c> and
/// <c>ADR010_DITests</c>.</para>
///
/// <para><b>Note on the POML's canonical reference.</b> Task 060 was authored pointing at
/// <c>GodClassGuardTests.cs</c> for the ratchet convention. That file no longer exists — the God-class LOC
/// ratchet was RETIRED on 2026-08-20 (root CLAUDE.md §11.5) because it gated on line count, the wrong
/// instrument. The conventions followed here are therefore <c>LayerDependencyTests</c>'s negative-control
/// pattern and <c>DataverseServiceClientDowncastTests</c>'s source-scan pattern, both current.</para>
///
/// <para><b>This is a ratchet with a maintenance procedure, not a wall.</b> See
/// <see cref="Allowlist"/> — adding an entry is a deliberate, reviewed act that costs one sentence of
/// written justification. An allowlist entry without a reason is how the NEXT audit concludes the secret
/// is permanent, so the reason is enforced structurally: every entry carries one, and
/// <see cref="EveryAllowlistEntryCarriesAReasonAndAnAdrReference"/> fails if one is blank.</para>
/// </summary>
public class CredentialGuardTests
{
    // =============================================================================================
    // THE ALLOWLIST
    // ---------------------------------------------------------------------------------------------
    // MAINTENANCE PROCEDURE — read before adding an entry.
    //
    //   1. First ask whether the site needs its own credential at all. Since auth-v4 task 022 the BFF's
    //      own identity is served by exactly ONE binding point (OrderedCredentialClientProvider). If the
    //      new code authenticates AS THE BFF, it must go through that provider and needs NO entry here.
    //      Injecting IConfidentialClientProvider (or ConfidentialClientTokenCredential for app-only) is
    //      the answer, not an allowlist row.
    //
    //   2. An entry is justified only when the site authenticates as something that is NOT the BFF's own
    //      identity — another application's registration (ADR-028 E-1), or a genuinely separate service
    //      principal with its own secret.
    //
    //   3. Write the reason as a sentence a reviewer two years from now can evaluate, and cite the ADR
    //      clause. "Legacy" is not a reason. "Needed for now" is not a reason.
    //
    //   4. E-3 entries are TRANSITIONAL and time-boxed to this project. When task 033 removes
    //      BFF-API-ClientSecret, the E-3 row goes with it. If an E-3 row is still here after that task,
    //      the migration did not finish.
    //
    // =============================================================================================
    private static readonly IReadOnlyList<AllowlistEntry> Allowlist = new[]
    {
        new AllowlistEntry(
            FileName: "OrderedCredentialClientProvider.cs",
            Adr: "ADR-028 E-3",
            Reason:
                "THE sanctioned binding point for the BFF's own identity, and the reason every other site "
                + "could be removed. Ordered selection necessarily contains a .WithClientSecret call: the "
                + "secret is the transitional last option AND the rollback target, and a selector without "
                + "it cannot express the rollback NFR-06 depends on. This is CONSOLIDATION, not expansion "
                + "— auth-v4 task 022 removed nine sites and routed them through this one. Task 033 "
                + "deletes the branch and this entry with it."),

        new AllowlistEntry(
            FileName: "DataverseWebApiEnvVarValuesWriter.cs",
            Adr: "ADR-028 E-1",
            Reason:
                "Authenticates as the CUSTOMER's own Entra app registration, not the BFF's. TenantId, "
                + "ClientId and ClientSecret all arrive per-request on the handler's request record "
                + "(resolved from Key Vault upstream), because L2 provisions into an environment that "
                + "belongs to the customer's tenant. MI-FIC would have to be federated onto each "
                + "customer's registration, which is not ours to do — the same reasoning that "
                + "allowlists SpeAdminTokenProvider. Contrast DataverseRegistryConcurrencyStore, which "
                + "hit the ADMIN env as the BFF's OWN identity and was migrated to the L2 UAMI on "
                + "2026-08-27 rather than allowlisted."),

        new AllowlistEntry(
            FileName: "DataverseWebApiSolutionImporter.cs",
            Adr: "ADR-028 E-1",
            Reason:
                "Authenticates as the CUSTOMER's own Entra app registration, not the BFF's. TenantId, "
                + "ClientId and ClientSecret all arrive per-request on the handler's request record "
                + "(resolved from Key Vault upstream), because L2 provisions into an environment that "
                + "belongs to the customer's tenant. MI-FIC would have to be federated onto each "
                + "customer's registration, which is not ours to do — the same reasoning that "
                + "allowlists SpeAdminTokenProvider. Contrast DataverseRegistryConcurrencyStore, which "
                + "hit the ADMIN env as the BFF's OWN identity and was migrated to the L2 UAMI on "
                + "2026-08-27 rather than allowlisted."),

        new AllowlistEntry(
            FileName: "DataverseWebApiSolutionVerifier.cs",
            Adr: "ADR-028 E-1",
            Reason:
                "Authenticates as the CUSTOMER's own Entra app registration, not the BFF's. TenantId, "
                + "ClientId and ClientSecret all arrive per-request on the handler's request record "
                + "(resolved from Key Vault upstream), because L2 provisions into an environment that "
                + "belongs to the customer's tenant. MI-FIC would have to be federated onto each "
                + "customer's registration, which is not ours to do — the same reasoning that "
                + "allowlists SpeAdminTokenProvider. Contrast DataverseRegistryConcurrencyStore, which "
                + "hit the ADMIN env as the BFF's OWN identity and was migrated to the L2 UAMI on "
                + "2026-08-27 rather than allowlisted."),

        new AllowlistEntry(
            FileName: "SpeAdminTokenProvider.cs",
            Adr: "ADR-028 E-1",
            Reason:
                "Authenticates as OTHER APPLICATIONS' registrations, not the BFF's — per-container-type "
                + "owning apps whose secrets are fetched from Key Vault per request, by a secret NAME "
                + "held in Dataverse. Those identities are not ours to migrate; MI-FIC would have to be "
                + "federated onto each customer's own app registration."),

        new AllowlistEntry(
            FileName: "SpeAdminGraphService.cs",
            Adr: "ADR-028 E-1",
            Reason:
                "Same as SpeAdminTokenProvider — per-business-unit owning-app credentials, fetched from "
                + "Key Vault by a name resolved from Dataverse configuration. Not the BFF identity."),

        new AllowlistEntry(
            FileName: "ReportingEmbedService.cs",
            Adr: "ADR-028 (Workstream D deferral, DEF-001)",
            Reason:
                "Workstream D deferred 2026-08-19 -- Power BI not yet adopted at Spaarke; "
                + "PowerBi:ClientSecret is a separate secret from BFF-API-ClientSecret and does not gate "
                + "the OBO migration. Revisit when Power BI is adopted (tasks 040-042)."),

        new AllowlistEntry(
            FileName: "ReportingProfileManager.cs",
            Adr: "ADR-028 (Workstream D deferral, DEF-001)",
            Reason:
                "Workstream D deferred 2026-08-19 -- Power BI not yet adopted at Spaarke; "
                + "PowerBi:ClientSecret is a separate secret from BFF-API-ClientSecret and does not gate "
                + "the OBO migration. Revisit when Power BI is adopted (tasks 040-042)."),
    };

    /// <summary>
    /// The three syntactic forms of "bind a client secret to a confidential credential" that exist in
    /// this codebase. All three are literal because all three are what a contributor actually types.
    /// </summary>
    private static readonly (string Pattern, string What)[] SecretBindings =
    {
        (".WithClientSecret(", "MSAL confidential client bound to a client secret"),
        ("new ClientSecretCredential(", "Azure.Identity credential bound to a client secret"),
        ("AuthType=ClientSecret", "Dataverse ServiceClient connection string bound to a client secret"),
    };

    // =============================================================================================
    // The ban
    // =============================================================================================

    [Fact(DisplayName = "FR-F1: no secret-bearing confidential credential under src/server/** outside the allowlist")]
    public void NoSecretBearingConfidentialClientOutsideTheAllowlist()
    {
        var violations = ScanServerSource();

        Assert.True(
            violations.Count == 0,
            "ADR-028 A4 violation: a confidential credential is bound to a CLIENT SECRET outside the "
            + "allowlist in this file.\n\n"
            + "If this code authenticates as the BFF's own identity, it must not construct a credential "
            + "at all — inject IConfidentialClientProvider (OBO / MSAL) or construct a "
            + "ConfidentialClientTokenCredential (app-only / Azure.Core) and let ordered selection choose. "
            + "That is the whole point of auth-v4: ONE binding point, so the credential can be changed in "
            + "configuration instead of in nine files.\n\n"
            + "If it authenticates as something else, add an allowlist entry WITH a written reason and an "
            + "ADR citation — see the MAINTENANCE PROCEDURE comment above the allowlist.\n\n"
            + "Offending sites:\n" + string.Join("\n", violations));
    }

    [Fact(DisplayName = "FR-F1: every allowlist entry carries a written reason and an ADR reference")]
    public void EveryAllowlistEntryCarriesAReasonAndAnAdrReference()
    {
        // The allowlist is the part of this mechanism that decays. An unexplained exemption is
        // indistinguishable from an oversight six months later, and "there was an exemption for it" is
        // how the previous audits reached NEVER-REMOVE. Enforced rather than trusted.
        var unexplained = Allowlist
            .Where(e => string.IsNullOrWhiteSpace(e.Reason)
                        || string.IsNullOrWhiteSpace(e.Adr)
                        || e.Reason.Trim().Length < 60)
            .Select(e => e.FileName)
            .ToList();

        Assert.True(
            unexplained.Count == 0,
            "Every credential-ban allowlist entry must carry a substantive written reason and an ADR "
            + "citation. Entries missing one: " + string.Join(", ", unexplained));
    }

    [Fact(DisplayName = "FR-F1: negative control — the detector fires on a seeded violation")]
    public void Detector_NegativeControl_FiresOnEachSeededForm()
    {
        // A detector nobody has seen fail is a detector nobody knows works. One seeded line per form.
        const string seededMsal = "        var app = builder.WithClientSecret(secret).Build();";
        const string seededAzureIdentity = "        _credential = new ClientSecretCredential(t, c, s);";
        const string seededConnString = "        var cs = $\"AuthType=ClientSecret;Url={url};ClientId={id}\";";

        Assert.NotNull(MatchSecretBinding(seededMsal));
        Assert.NotNull(MatchSecretBinding(seededAzureIdentity));
        Assert.NotNull(MatchSecretBinding(seededConnString));

        // And the negative half of the negative control: the detector must NOT fire on prose. Every
        // migrated file in auth-v4 discusses the credential it no longer constructs, and a detector that
        // flagged its own explanation would be suppressed within a week.
        Assert.Null(MatchSecretBinding("        /// removes the last inline <c>ClientSecretCredential</c>."));
        Assert.Null(MatchSecretBinding("        // Legacy path: ClientSecretCredential, removed by task 022."));
    }

    [Fact(DisplayName = "FR-F1: negative control — certificate and assertion credentials are NOT flagged")]
    public void Detector_DoesNotFireOnSecretFreeCredentials()
    {
        // CiamGraphClientFactory (certificate) and ManagedIdentityAssertionProvider (MI-FIC) are the two
        // in-repo secret-FREE confidential credentials. A ban that flagged them would be pushing code
        // back toward the secret.
        Assert.Null(MatchSecretBinding("        builder = builder.WithCertificate(x509);"));
        Assert.Null(MatchSecretBinding("        builder = builder.WithClientAssertion(GetAssertionAsync);"));
        Assert.Null(MatchSecretBinding("        var cred = new ClientAssertionCredential(t, c, cb);"));

        // Belt and braces against the real files, so this cannot pass on a stub while failing on disk.
        foreach (var file in new[] { "CiamGraphClientFactory.cs", "ManagedIdentityAssertionProvider.cs" })
        {
            var hits = ScanServerSource().Where(v => v.Contains(file, StringComparison.Ordinal)).ToList();
            Assert.True(hits.Count == 0, $"{file} must not be flagged — it is secret-free. Hits:\n{string.Join("\n", hits)}");
        }
    }

    // =============================================================================================
    // Booked from task 010 — the OBO / app-only decoupling in DataverseAccessDataSource
    // =============================================================================================

    [Fact(DisplayName = "FR-A1: DataverseAccessDataSource never gates DELEGATED access on the managed-identity flag")]
    public void DelegatedAccessIsNotGatedOnTheManagedIdentityFlag()
    {
        // Task 010 separated two concerns that had shared one `if`: the APP-ONLY credential (correctly
        // gated on Graph:ManagedIdentity:Enabled) and the OBO path (which must NOT be, because
        // DefaultAzureCredential cannot perform an OBO exchange and the flag says nothing about
        // delegated access).
        //
        // The regression this guards is a plausible "simplification" back into one if/else: doing that
        // leaves the OBO side unset whenever managed identity is enabled, which SILENTLY DISABLES
        // delegated access — a total fail-closed outage on every document and AI endpoint that runs an
        // authorization filter. It reads like tidying up.
        //
        // Task 010 deferred this guard here because it could not live in a seam test: the fields are
        // private (ADR-038 ban B8 forbids reflection) and the class swallows credential errors into
        // AccessRights.None, so the selection is not observable behaviourally. Source analysis is both
        // the sanctioned shape and the stronger one — it fails at the SHAPE level rather than on one
        // sampled configuration.
        var path = Path.Combine(SourceScan.RepoRoot, "src", "server", "shared", "Spaarke.Dataverse", "DataverseAccessDataSource.cs");
        Assert.True(File.Exists(path), $"DataverseAccessDataSource.cs not found at {path}");

        var lines = File.ReadAllLines(path);

        // (a) The predicate that decides whether delegated access is possible must not consult the flag.
        var oboPredicate = string.Join(
            " ",
            ExtractMemberBody(lines, "private bool OboAvailable"));
        Assert.False(
            oboPredicate.Contains("useManagedIdentity", StringComparison.Ordinal)
            || oboPredicate.Contains("ManagedIdentity:Enabled", StringComparison.Ordinal),
            "OboAvailable must not depend on Graph:ManagedIdentity:Enabled. Delegated access is "
            + "independent of the app-only credential choice — re-coupling them disables OBO whenever "
            + "managed identity is enabled. See notes/decisions/010-credential-gating.md §4.");

        // (b) The OBO fields must not be assigned inside the app-only branch, which is the other way the
        //     re-entanglement arrives ("_confidentialClients = null" in the managed-identity arm).
        var branch = ExtractBranchSpan(lines, "if (useManagedIdentity)");
        var reassigned = branch
            .Select((line, i) => (Text: SourceScan.StripLineComment(line), Index: i))
            .Where(l => Regex.IsMatch(l.Text, @"\b(_confidentialClients|_tenantId|_clientId)\s*=[^=]"))
            .Select(l => l.Text.Trim())
            .ToList();

        Assert.True(
            reassigned.Count == 0,
            "The OBO identity/provider fields are assigned inside the managed-identity if/else. That is "
            + "the re-entanglement task 010 separated: it disables delegated access whenever managed "
            + "identity is enabled, and fails CLOSED for every user. Offending lines:\n"
            + string.Join("\n", reassigned));
    }

    // =============================================================================================
    // Booked from task 023 — no managed identity may be resolved BY NAME
    // =============================================================================================

    [Fact(DisplayName = "FR-B4: no managed identity is resolved by NAME anywhere in src/")]
    public void NoManagedIdentityIsResolvedByName()
    {
        // The dev subscription holds FIVE user-assigned managed identities, and one of them is called
        // `spaarke-bff-identity` — as though it were the BFF's — while NOT being attached to the BFF.
        // The BFF's actual identity is `mi-bff-api-dev`. Anything resolving an identity by name picks the
        // decoy and fails only at token exchange, with an error naming neither identity.
        //
        // Task 023 VERIFIED that the runtime does not do this today (a grep of src/ returned exactly one
        // hit: a doc comment warning about the decoy). But that was a fact about the source on one day,
        // not a guard. This is the guard.
        var names = new[] { "spaarke-bff-identity", "mi-bff-api-dev" };
        var violations = new List<string>();

        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var code = SourceScan.StripLineComment(lines[i]);
                if (names.Any(n => code.Contains(n, StringComparison.OrdinalIgnoreCase)))
                {
                    violations.Add($"{SourceScan.Relative(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "A managed identity is referenced by NAME in executable code. Resolve managed identities by "
            + "clientId / resource ID from configuration — five UAMIs exist in the dev subscription and "
            + "one is named like the BFF's without being attached to it, so a name lookup silently binds "
            + "the wrong identity. See notes/decisions/023-identity-conflation.md §2.\n"
            + string.Join("\n", violations));
    }

    // =============================================================================================
    // Booked from task 020 — the signed assertion must be reused, not re-minted per call
    // =============================================================================================

    [Fact(DisplayName = "FR-B1: ManagedIdentityClientAssertion is held in a readonly field, never built per call")]
    public void ManagedIdentityClientAssertionIsConstructedOncePerProvider()
    {
        // ManagedIdentityClientAssertion caches its signed assertion until expiry — that caching is the
        // reason the singleton registration is load-bearing rather than decorative. Constructing one
        // inside GetAssertionAsync would re-mint on every token acquisition (an IMDS round trip per OBO
        // exchange) while every test and every health check still passed.
        //
        // Expressed as "must be assigned to a READONLY field" rather than as "must not appear in a method
        // body", because readonly is COMPILER-ENFORCED to mean declaration-initializer-or-constructor.
        // That makes this check precise instead of a brace-counting approximation of the C# grammar.
        // Task 020 deferred it here: the DI half is ADR-038 ban B3 and the per-instance half is what the
        // compiler already guarantees, so a runtime test asserts either a banned shape or a tautology.
        var violations = new List<string>();

        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var lines = File.ReadAllLines(file);
            var readonlyFields = ReadonlyFieldNames(lines);

            foreach (var (statement, line) in SourceScan.Statements(lines))
            {
                if (!statement.Contains("new ManagedIdentityClientAssertion(", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!IsBoundToReadonlyField(statement, readonlyFields))
                {
                    violations.Add($"{SourceScan.Relative(file)}:{line}: {statement.Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "ManagedIdentityClientAssertion must be held in a readonly field (declaration initializer or "
            + "constructor), never constructed inside a method body. A per-call construction re-mints a "
            + "signed assertion on every token acquisition, converting the singleton registration from "
            + "load-bearing to decorative — the regression ADR-028 A4's 'reuse the instance' rule exists "
            + "to prevent, and one that no functional test would notice.\n"
            + string.Join("\n", violations));
    }

    [Fact(DisplayName = "FR-B1: negative control — a per-call assertion construction is flagged")]
    public void AssertionReuseDetector_NegativeControl()
    {
        // The scratch provider task 060's acceptance criteria call for, expressed as source rather than
        // as a real file: a provider that news up the assertion inside GetAssertionAsync must fail.
        var scratch = new[]
        {
            "public sealed class ScratchProvider : IClientAssertionProvider",
            "{",
            "    private readonly string _clientId = \"x\";",
            "    public Task<string> GetAssertionAsync(CancellationToken ct)",
            "    {",
            "        var assertion = new ManagedIdentityClientAssertion(_clientId);",
            "        return assertion.GetSignedAssertionAsync(ct);",
            "    }",
            "}",
        };

        var readonlyFields = ReadonlyFieldNames(scratch);
        var offending = SourceScan.Statements(scratch)
            .Where(s => s.Statement.Contains("new ManagedIdentityClientAssertion(", StringComparison.Ordinal))
            .Where(s => !IsBoundToReadonlyField(s.Statement, readonlyFields))
            .ToList();

        Assert.True(offending.Count == 1,
            "the detector must flag a per-call assertion construction; it found " + offending.Count);

        // And the positive control — the SANCTIONED shape must not be flagged, including the multi-line
        // ternary the real provider actually uses. Line-scoped analysis failed exactly here.
        var sanctioned = new[]
        {
            "    private readonly ManagedIdentityClientAssertion _assertion;",
            "    public ManagedIdentityAssertionProvider(string clientId, string? tokenExchangeUrl)",
            "    {",
            "        _assertion = string.IsNullOrWhiteSpace(tokenExchangeUrl)",
            "            ? new ManagedIdentityClientAssertion(clientId)",
            "            : new ManagedIdentityClientAssertion(clientId, tokenExchangeUrl);",
            "    }",
        };

        var sanctionedFields = ReadonlyFieldNames(sanctioned);
        var falsePositives = SourceScan.Statements(sanctioned)
            .Where(s => s.Statement.Contains("new ManagedIdentityClientAssertion(", StringComparison.Ordinal))
            .Where(s => !IsBoundToReadonlyField(s.Statement, sanctionedFields))
            .ToList();

        Assert.True(falsePositives.Count == 0,
            "the detector must NOT flag a readonly field initialised through a multi-line ternary — the "
            + "shape ManagedIdentityAssertionProvider actually uses. Flagged: "
            + string.Join(" | ", falsePositives.Select(f => f.Statement.Trim())));
    }

    // =============================================================================================
    // Machinery
    // =============================================================================================

    private sealed record AllowlistEntry(string FileName, string Adr, string Reason);

    private static List<string> ScanServerSource()
    {
        var violations = new List<string>();
        var allowed = Allowlist.Select(e => e.FileName).ToHashSet(StringComparer.Ordinal);

        foreach (var file in SourceScan.ServerSourceFiles())
        {
            if (allowed.Contains(Path.GetFileName(file)))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var what = MatchSecretBinding(lines[i]);
                if (what is not null)
                {
                    violations.Add($"{SourceScan.Relative(file)}:{i + 1}: {what} -- {lines[i].Trim()}");
                }
            }
        }

        return violations;
    }

    /// <summary>
    /// Returns a description of the secret binding on this line, or <c>null</c>. Comments are stripped
    /// first: every file auth-v4 migrated explains the credential it no longer constructs, and a
    /// detector that flagged its own rationale would be suppressed rather than obeyed.
    /// </summary>
    private static string? MatchSecretBinding(string line)
    {
        var code = SourceScan.StripLineComment(line);
        foreach (var (pattern, what) in SecretBindings)
        {
            if (code.Contains(pattern, StringComparison.Ordinal))
            {
                return what;
            }
        }

        return null;
    }

    /// <summary>Names of fields declared <c>readonly</c> in this file.</summary>
    private static HashSet<string> ReadonlyFieldNames(IReadOnlyList<string> lines)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var m = Regex.Match(SourceScan.StripLineComment(line), @"\breadonly\s+[\w<>,.?\[\]]+\s+(\w+)\s*[;=]");
            if (m.Success)
            {
                names.Add(m.Groups[1].Value);
            }
        }

        return names;
    }

    /// <summary>
    /// True when the STATEMENT initialises a readonly field — either in the field's own declaration, or
    /// by assigning to one, which the compiler permits only in a declaration initializer or a
    /// constructor. That compiler guarantee is what makes this check exact rather than a brace-counting
    /// approximation of the C# grammar.
    ///
    /// <para>Statement-scoped rather than line-scoped, deliberately. The real construction in
    /// <c>ManagedIdentityAssertionProvider</c> is a multi-line ternary — the assignment target is on one
    /// line and both <c>new</c> expressions are on the next two. A line-scoped check reported that
    /// sanctioned code as a violation, which is the shape of false positive that gets a guard deleted
    /// rather than obeyed. Caught by running it, not by reading it.</para>
    /// </summary>
    private static bool IsBoundToReadonlyField(string statement, HashSet<string> readonlyFields)
    {
        if (statement.Contains("readonly", StringComparison.Ordinal))
        {
            return true;
        }

        var assignment = Regex.Match(statement, @"^\s*(\w+)\s*=[^=]");
        return assignment.Success && readonlyFields.Contains(assignment.Groups[1].Value);
    }


    /// <summary>Lines of the member whose declaration starts with <paramref name="declaration"/>.</summary>
    private static IReadOnlyList<string> ExtractMemberBody(IReadOnlyList<string> lines, string declaration)
    {
        var start = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains(declaration, StringComparison.Ordinal))
            {
                start = i;
                break;
            }
        }

        Assert.True(start >= 0, $"member not found: {declaration}. If it was renamed, update this guard "
                                + "rather than deleting it — the invariant it protects still holds.");

        var body = new List<string>();
        for (var i = start; i < lines.Count; i++)
        {
            body.Add(lines[i]);
            var code = SourceScan.StripLineComment(lines[i]);
            if (i > start && (code.TrimEnd().EndsWith(";", StringComparison.Ordinal)
                              || code.Contains('}', StringComparison.Ordinal)))
            {
                break;
            }
        }

        return body;
    }

    /// <summary>Lines spanned by the <c>if</c> (and its <c>else</c>) beginning at <paramref name="ifLine"/>.</summary>
    private static IReadOnlyList<string> ExtractBranchSpan(IReadOnlyList<string> lines, string ifLine)
    {
        var start = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains(ifLine, StringComparison.Ordinal))
            {
                start = i;
                break;
            }
        }

        Assert.True(start >= 0, $"branch not found: {ifLine}. If the app-only branch was restructured, "
                                + "update this guard rather than deleting it.");

        var span = new List<string>();
        var depth = 0;
        var opened = false;

        for (var i = start; i < lines.Count; i++)
        {
            var code = SourceScan.StripLineComment(lines[i]);
            span.Add(lines[i]);
            depth += code.Count(c => c == '{');
            depth -= code.Count(c => c == '}');
            if (code.Contains('{', StringComparison.Ordinal))
            {
                opened = true;
            }

            if (opened && depth == 0)
            {
                var next = i + 1 < lines.Count ? SourceScan.StripLineComment(lines[i + 1]).Trim() : string.Empty;
                if (!next.StartsWith("else", StringComparison.Ordinal))
                {
                    break;
                }
            }
        }

        return span;
    }
}
