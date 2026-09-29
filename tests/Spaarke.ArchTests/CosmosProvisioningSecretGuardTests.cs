using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Forcing-function (customer-provisioning-orchestration-r1 task 025 / spec.md FR-27 /
/// design.md §6.2 (B3) + §4D I3-adjacent). A structural guard that prevents cleartext
/// secrets from ever being persisted to Cosmos <c>ProvisioningRun</c> rows.
///
/// <para>
/// Two Facts, one positive control, two negative controls:
/// </para>
/// <list type="number">
///   <item><b>L2 type shape.</b> No type in the <c>Sprk.Provisioning.ControlPlane</c>
///     namespace may declare a string-typed property whose name is secret-shaped
///     (Secret / Password / Key / ClientSecret / ApiKey / ConnectionString / Token /
///     PrivateKey / Certificate / Credential / Bearer / AccessKey / AuthToken).
///     Compliant shape is <c>KeyVaultSecretRef</c> — a URI reference with no cleartext
///     value property.
///   </item>
///   <item><b>Fixture payloads.</b> No test-fixture file under <c>tests/**/fixtures/**</c>
///     that references the runs container may contain a cleartext secret literal
///     (Stripe <c>sk_</c>, GitHub <c>ghp_</c> / <c>pat_</c>, Slack <c>xoxb-</c> /
///     <c>xoxp-</c>, connection-string with <c>AccountKey=</c> / <c>SharedAccessKey=</c> /
///     <c>Server=... Password=</c>, or a 44+ character base64 blob shape). Secrets
///     wrapped in an <c>@Microsoft.KeyVault(...)</c> reference literal are compliant.
///   </item>
/// </list>
///
/// <para>
/// Severity of the invariant is CATASTROPHIC — Cosmos is a queryable audit log; a
/// cleartext secret persisted there leaks to anyone with Reader-role. Spec FR-27
/// acceptance ("no cleartext secret string matches any known secret pattern in
/// Cosmos rows"); design.md §6.2 (B3): "secrets stored as KV URI refs — resolved
/// at handler runtime via UAMI. No cleartext secrets in Cosmos."
/// </para>
///
/// <para>
/// This test is a KEEP-path structural invariant (ADR-038 §7 discipline): it does NOT
/// mock HttpMessageHandler, does NOT test runtime behavior, does NOT wire DI. It
/// asserts a compile-time / source-time shape only. Regex catalog is a LIVING list —
/// new secret prefixes discovered in the wild should be added here rather than
/// litigated in ad-hoc PR reviews (task POML 025 note).
/// </para>
///
/// <para>
/// <b>Assembly loading.</b> The L2 assembly is loaded at TEST RUNTIME (not compile time)
/// from <c>src/server/services/Sprk.Provisioning.ControlPlane/bin/**</c> via
/// <see cref="Assembly.LoadFrom(string)"/>. Rationale is documented in
/// <c>Spaarke.ArchTests.csproj</c>: L2 is a POCO surface whose NuGet stack evolves
/// through parallel Wave-2 sibling tasks (Cosmos SDK, Service Bus, AppInsights);
/// a compile-time ProjectReference would couple this ArchTest to the composite build
/// health of that fast-moving graph. This test enforces a shape invariant on the
/// PUBLISHED artifact, which is the right subject of an ArchTest.
/// </para>
///
/// <para>
/// Discoverability: rule + rationale in
/// <c>projects/customer-provisioning-orchestration-r1/spec.md</c> (FR-27),
/// <c>design.md</c> (§6.2, §4D), and the XML doc on <c>KeyVaultSecretRef</c> itself.
/// Failure messages name the exact type/property or fixture path so an editor never
/// has to guess what tripped the guard.
/// </para>
/// </summary>
public class CosmosProvisioningSecretGuardTests
{
    // -----------------------------------------------------------------------
    // Loaded-once L2 assembly (runtime reflection — see class-doc rationale)
    // -----------------------------------------------------------------------

    private const string L2AssemblyName = "Sprk.Provisioning.ControlPlane";
    private const string L2NamespacePrefix = "Sprk.Provisioning.ControlPlane";
    private const string CompliantSecretRefFullName = "Sprk.Provisioning.ControlPlane.Models.KeyVaultSecretRef";
    private const string L2RunParametersFullName = "Sprk.Provisioning.ControlPlane.Models.RunParameters";
    private const string L2SecretsPropertyName = "Secrets";

    /// <summary>
    /// Explicit exclusions from the L2 secret-shape scan. Types listed here are
    /// documented CLAUDE.md §6.5 Path A exceptions — runtime-transient shapes
    /// that carry plaintext secrets in local scope only, NEVER on Cosmos-
    /// persisted POCOs. Additions require:
    ///   1. A single-line comment naming the task ID and rationale.
    ///   2. Reviewer sign-off in the PR that adds the exclusion.
    ///   3. A follow-on note in the project's design.md ADR Tensions section.
    /// If the shape is a candidate for Path C (comply) — e.g. by refactoring to
    /// <see cref="Models.KeyVaultSecretRef"/> — prefer that over path A.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>KeyVaultSecretRef</b> — the compliant reference type itself; its
    /// <c>SecretName</c> property matches the secret-shape regex but holds a
    /// KV secret NAME, not a value. Excluded from day 1 of task 025.
    /// </para>
    /// <para>
    /// <b>SolutionImportOptions</b> (task 049, wave C4 Batch 3D) — IConfiguration
    /// binding POCO for the H6 handler. The <c>ClientSecret</c> property is
    /// bound by App Service at app-setting-binding time from an
    /// <c>@Microsoft.KeyVault(SecretUri=…)</c> reference (Wave C5 wiring per
    /// design.md §7.1). The plaintext value lives ONLY on this IOptions bag
    /// after bind time — it is never persisted to Cosmos (H6 passes it to a
    /// child pwsh process via env-var; no ProvisioningRun / InterStepState
    /// slot receives it). Path A exception with wave-C5 follow-on to consider
    /// upgrading to <see cref="Models.KeyVaultSecretRef"/> + a runtime
    /// secret-resolver seam (Path C) once the KV SDK dependency budget is
    /// evaluated for L2.
    /// </para>
    /// <para>
    /// <b>SolutionImportRequest</b> (task 049) — transient <see langword="record"/>
    /// carrying the plaintext client secret from the H6 handler to the
    /// <c>DeployDataverseSolutionsScriptImporter</c> shell-out. Instantiated
    /// per-invocation in method scope; never persisted to Cosmos. Plaintext IS
    /// unavoidable because <c>pac auth create --clientSecret</c> requires the
    /// value. Path A exception; Path C alternative (record carries a
    /// <see cref="Models.KeyVaultSecretRef"/> instead + runner resolves
    /// internally) is deferred to Wave C5 alongside the Options refactor.
    /// </para>
    /// <para>
    /// <b>EnvVarValuesOptions</b> (task 050, wave C4 Batch 3E) — direct parity
    /// with <c>SolutionImportOptions</c> above. IConfiguration binding POCO for
    /// the H7 handler. The <c>ClientSecret</c> property is bound by App Service
    /// at app-setting-binding time from an
    /// <c>@Microsoft.KeyVault(SecretUri=…)</c> reference (Wave C5 wiring per
    /// design.md §7.1). The plaintext value lives ONLY on this IOptions bag
    /// after bind time — it is never persisted to Cosmos (H7 hands it to
    /// <c>DataverseWebApiEnvVarValuesWriter</c> for an OAuth2
    /// client-credentials token acquisition, then discards). See the property's
    /// own XML doc: "this options-bound field is NOT persisted to Cosmos."
    /// Path A exception with Wave-C5 follow-on to consider upgrading to
    /// <see cref="Models.KeyVaultSecretRef"/> + a runtime secret-resolver seam
    /// (Path C) once task 084 (Phase H canonical secret-catalog manifest
    /// generator) provides the L2 KV resolver. Added by Wave 4 Batch 4A
    /// ArchTest debt remediation (2026-08-17).
    /// </para>
    /// <para>
    /// <b>EnvVarValuesWriteRequest</b> (task 050) — transient <see langword="record"/>
    /// carrying the plaintext client secret from the H7 handler to the
    /// <c>DataverseWebApiEnvVarValuesWriter</c>. Direct parity with
    /// <c>SolutionImportRequest</c> above. Instantiated per-invocation in method
    /// scope; never persisted to Cosmos. Plaintext IS unavoidable because
    /// the OAuth2 client-credentials token request requires the raw value.
    /// Path A exception; Path C alternative deferred to task 084 as above.
    /// Added by Wave 4 Batch 4A ArchTest debt remediation (2026-08-17).
    /// </para>
    /// <para>
    /// <b>CustomerRunGuardOptions</b> (task 059, Wave 4 Batch 4D) — IConfiguration
    /// binding POCO for the I5 concurrency guard's confidential-client auth to
    /// the admin Dataverse env (registry table home). Direct parity with the
    /// <c>SolutionImportOptions</c> / <c>EnvVarValuesOptions</c> precedent
    /// above. The <c>ClientSecret</c> property is populated by App Service at
    /// app-setting-binding time from an <c>@Microsoft.KeyVault(SecretUri=…)</c>
    /// reference. The property's own XML doc explicitly states "cleartext value
    /// NEVER traverses Cosmos or logs" — the options bag is transient in-process
    /// state used to construct <c>ClientSecretCredential</c> for token acquisition.
    /// Path A exception. Path C alternative (options carries a
    /// <see cref="Models.KeyVaultSecretRef"/> + runtime resolver seam) is
    /// deferred to a future task after task 084's KV resolver seam is available
    /// AND the L2 UAMI receives a Dataverse App User on the admin env
    /// (documented in CustomerRunGuardOptions XML doc "FUTURE MIGRATION"
    /// section — swap ClientSecretCredential for DefaultAzureCredential). Added
    /// by Wave 4 Batch 4D wrap-up (2026-08-18).
    /// </para>
    /// <para>
    /// ─────────── Added 2026-08-27, issue #839 adjudication (spaarkeai-compose-r8) ───────────
    /// </para>
    /// <para>
    /// This guard had been dark since the L2 project split and was repaired earlier the same day
    /// (see <see cref="LoadL2Assemblies"/>). Its first real run produced eight findings. Five were
    /// the regex matching the NAME of a secret rather than a value; those are now handled
    /// structurally by <see cref="SecretReferenceSuffix"/> and are NOT listed here — the whole
    /// point of that discriminator is to stop this list growing an entry per occurrence. The three
    /// below are genuine cleartext secrets, and each is excluded on the SAME basis as the
    /// <c>SolutionImportRequest</c> / <c>EnvVarValuesWriteRequest</c> precedent above: transient,
    /// method- or invocation-scoped, provably never written to a Cosmos document.
    /// </para>
    /// <para>
    /// <b>PendingKvSecretWrite</b> — <c>(VaultName, SecretName, Value)</c>. <c>Value</c> is the
    /// cleartext app-registration client secret, staged in memory by H3 and committed to Key Vault
    /// only after admin consent is verified (DS-4 §3 ordering). Excluded for <c>Value</c>, which the
    /// NEW secret-carrier rule catches — the guard previously reported only <c>SecretName</c> here,
    /// the one property on the record that is NOT sensitive. The type's own XML doc already states
    /// the invariant: "MUST NEVER be logged or persisted to Cosmos; it exists ONLY in memory for the
    /// duration of a single H3EntraAppRegHandler.HandleAsync invocation". Path C (a
    /// <see cref="Models.KeyVaultSecretRef"/> round-trip) is not available: the value does not exist
    /// in any vault yet — writing it there is what this record is for.
    /// </para>
    /// <para>
    /// <b>ExchangePolicySidecarClient+SharedSecretResolution</b> and
    /// <b>ExchangePolicySidecarReadClient+SharedSecretResolution</b> — a <c>private readonly struct</c>
    /// returned by <c>ResolveSharedSecretAsync</c>, holding either the resolved sidecar shared secret
    /// or a failure. Private, nested, method-local, never serialized; it exists to make
    /// "resolved-or-failed" a single return value instead of an out-parameter. Two entries because
    /// the read and write clients each declare their own nested copy.
    /// </para>
    /// <para>
    /// <b>SolutionVerificationRequest</b> — transient record carrying the plaintext client secret
    /// from H6 to the <c>pac solution list</c> shell-out. Exact parity with <c>SolutionImportRequest</c>
    /// above (same handler, same run, same <c>pac auth create --clientSecret</c> constraint: the CLI
    /// requires the value). Its absence from the original exclusion list alongside its sibling looks
    /// like an oversight when task 049 added the sibling, not a deliberate distinction.
    /// </para>
    /// <para>
    /// <b>PerEnvYamlEntry</b> and <b>PerEnvSettingEntry</b> — the H4b per-environment app-settings
    /// manifest rows. The flagged property is <c>Key</c>: the NAME of an App Service application
    /// setting (e.g. <c>ServiceBus__FullyQualifiedNamespace</c>). Bare <c>Key</c> gets no reference
    /// suffix and is genuinely ambiguous elsewhere in the estate — an "ApiKey"-style property called
    /// <c>Key</c> WOULD be a secret — so it is excluded per-type rather than by weakening the regex.
    /// Neither type carries the setting's value in a secret-bearing slot: values arrive via
    /// <c>LiteralValue</c> or by lookup in <c>envelope.Parameters.NonSecret</c>, which is
    /// non-secret by construction and by name.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> ExcludedTypeFullNames = new(StringComparer.Ordinal)
    {
        CompliantSecretRefFullName,
        "Sprk.Provisioning.ControlPlane.Handlers.SolutionImport.SolutionImportOptions",
        "Sprk.Provisioning.ControlPlane.Handlers.SolutionImport.SolutionImportRequest",
        "Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues.EnvVarValuesOptions",
        "Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues.EnvVarValuesWriteRequest",
        "Sprk.Provisioning.ControlPlane.Concurrency.CustomerRunGuardOptions",

        // #839 (2026-08-27) — genuine cleartext, provably transient. Rationale per type above.
        "Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg.PendingKvSecretWrite",
        "Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring.ExchangePolicySidecarClient+SharedSecretResolution",
        "Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring.ExchangePolicySidecarReadClient+SharedSecretResolution",
        "Sprk.Provisioning.ControlPlane.Handlers.SolutionImport.SolutionVerificationRequest",

        // #839 (2026-08-27) — `Key` is an app-setting NAME on both. Rationale per type above.
        "Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings.FilePerEnvSettingsManifest+PerEnvYamlEntry",
        "Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings.PerEnvSettingEntry",
    };

    /// <summary>
    /// Lazy-loaded L2 assembly. Preference: already-loaded in <see cref="AppDomain"/>;
    /// fallback: <see cref="Assembly.LoadFrom(string)"/> on the newest matching DLL under
    /// <c>src/server/services/Sprk.Provisioning.ControlPlane/bin/**</c>. If the DLL is
    /// absent, the loader throws with an actionable message (build L2 first).
    /// </summary>
    private static readonly Lazy<IReadOnlyList<Assembly>> L2Assemblies = new(LoadL2Assemblies);

    /// <summary>
    /// Loads EVERY <c>Sprk.Provisioning.ControlPlane*</c> assembly, not one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>REPAIRED 2026-08-27 (task 042 of sdap-SPE-admin-app-r2).</b> This loader looked for a single
    /// project directory, <c>src/server/services/Sprk.Provisioning.ControlPlane/</c>. **That directory
    /// does not exist.** L2 was split into <c>.Api</c>, <c>.Core</c>, <c>.Sidecar</c>, <c>.Worker</c>
    /// and <c>.Tests</c>, and the loader was never updated — so both Facts threw
    /// <see cref="FileNotFoundException"/> on every run.
    /// </para>
    /// <para>
    /// The failure mode is worth naming, because it is the one this repository keeps hitting: the guard
    /// did not report "I cannot check this." It reported a <b>test failure attributed to the invariant</b>
    /// — the error surfaced under the DisplayName "types have no string-typed secret-shape properties",
    /// so a reader would reasonably think the secret rule was being evaluated and had something to say.
    /// It was not being evaluated at all. This invariant is documented CATASTROPHIC (Cosmos is a
    /// queryable audit log; a cleartext secret there leaks to any Reader), and it has been dark since
    /// the split.
    /// </para>
    /// <para>
    /// The scan comment always said "every type declared in <c>Sprk.Provisioning.ControlPlane*</c>" —
    /// multi-assembly was the intent from the start; only the loader was single. Loading all of them
    /// restores the intended coverage rather than narrowing the rule to whichever project happens to
    /// hold <c>RunParameters</c> today.
    /// </para>
    /// <para>
    /// Still deliberately NOT a <c>&lt;ProjectReference&gt;</c> — see the note in
    /// <c>Spaarke.ArchTests.csproj</c> (top-level <c>Program</c> collision).
    /// </para>
    /// </remarks>
    private static IReadOnlyList<Assembly> LoadL2Assemblies()
    {
        var loaded = new Dictionary<string, Assembly>(StringComparer.Ordinal);

        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = a.GetName().Name;
            if (name is not null && name.StartsWith(L2AssemblyName, StringComparison.Ordinal))
                loaded[name] = a;
        }

        var repoRoot = ResolveRepoRoot();
        var servicesRoot = Path.Combine(repoRoot, "src", "server", "services");
        if (!Directory.Exists(servicesRoot))
        {
            throw new DirectoryNotFoundException(
                $"Services root not found at '{servicesRoot}'. The repository layout changed; " +
                $"this guard resolves L2 projects by convention from that directory.");
        }

        // Every L2 project EXCEPT the test project — test doubles legitimately hold fake secrets and
        // are not persisted to Cosmos, which is what this invariant protects.
        var projectDirs = Directory
            .EnumerateDirectories(servicesRoot, $"{L2AssemblyName}*", SearchOption.TopDirectoryOnly)
            .Where(d => !Path.GetFileName(d).EndsWith(".Tests", StringComparison.Ordinal))
            .ToList();

        if (projectDirs.Count == 0)
        {
            throw new DirectoryNotFoundException(
                $"No '{L2AssemblyName}*' project directories under '{servicesRoot}'. If L2 was renamed " +
                $"again, update {nameof(L2AssemblyName)} — do NOT delete this guard, the invariant it " +
                $"enforces is CATASTROPHIC-severity (cleartext secrets in a queryable Cosmos audit log).");
        }

        foreach (var projectDir in projectDirs)
        {
            var assemblyName = Path.GetFileName(projectDir);
            if (loaded.ContainsKey(assemblyName)) continue;

            var binRoot = Path.Combine(projectDir, "bin");
            if (!Directory.Exists(binRoot)) continue;

            var candidate = Directory
                .EnumerateFiles(binRoot, $"{assemblyName}.dll", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (candidate is not null)
                loaded[assemblyName] = Assembly.LoadFrom(candidate);
        }

        if (loaded.Count == 0)
        {
            throw new FileNotFoundException(
                $"No '{L2AssemblyName}*' assembly was found built. This ArchTest inspects the COMPILED " +
                $"L2 DLLs, so the projects must be built at least once:\n" +
                $"  dotnet build src/server/services/{L2AssemblyName}.Core/\n" +
                $"Found project directories: {string.Join(", ", projectDirs.Select(Path.GetFileName))}");
        }

        return loaded.Values.ToList();
    }

    /// <summary>
    /// <see cref="Assembly.GetTypes"/> throws <see cref="ReflectionTypeLoadException"/> when any single
    /// type fails to load — e.g. a reference the ArchTests project does not itself carry. Losing the
    /// whole assembly's types to one unloadable type would silently shrink this guard's coverage, which
    /// is exactly the failure this file was just repaired from. Scan what loaded.
    /// </summary>
    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    // -----------------------------------------------------------------------
    // Regex catalog (living list — extend when a new secret prefix is discovered)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Property names that suggest the property holds a secret VALUE (as opposed
    /// to a secret NAME or reference). Anchored at name-start per task POML 025
    /// constraint: <c>/^(Secret|Password|Key|ClientSecret|ApiKey|ConnectionString)/i</c>.
    /// Extended with adjacent secret shapes (Token, PrivateKey, Certificate,
    /// Credential, Bearer, AccessKey, AuthToken) that are equally CATASTROPHIC
    /// if persisted to Cosmos.
    /// </summary>
    /// <remarks>
    /// Start-anchored deliberately — properties like <c>IdempotencyKey</c>,
    /// <c>PartitionKey</c>, <c>JobId</c> contain "Key" or "Id" in the middle/end
    /// but are NOT secrets. Widening to a substring match would false-positive
    /// on those legitimate identifiers.
    /// <br/>
    /// The compliant <c>KeyVaultSecretRef</c> type is excluded from the scan
    /// (see <see cref="L2Types_HaveNoStringTypedSecretShapedProperties"/>) — its
    /// <c>SecretName</c> property matches this regex but holds a KV secret NAME,
    /// not a value.
    /// </remarks>
    private static readonly Regex SecretShapedPropertyName = new(
        @"^(Secret|Password|Key|ClientSecret|ApiKey|ConnectionString|Token|PrivateKey|Certificate|Credential|Bearer|AccessKey|AuthToken)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Suffixes that turn a secret-shaped name into a REFERENCE to a secret rather than the
    /// secret itself. <c>SecretName</c> is a Key Vault lookup key; <c>KeyVaultName</c> is a
    /// vault name; <c>KeyVaultReferenceIdentity</c> is an ARM resource id. None of them is a
    /// credential, and persisting any of them to Cosmos leaks nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Added 2026-08-27 (issue #839 adjudication).</b> The start-anchored name regex above was
    /// producing five findings that were all the same mistake — it was matching the <i>name of a
    /// secret</i> and reporting it as the <i>value of a secret</i>:
    /// <c>PerEnvYamlEntry.Key</c>, <c>PerEnvSettingEntry.Key</c>,
    /// <c>SlotKeyVaultRefSnapshot.KeyVaultReferenceIdentity</c>,
    /// <c>TrapVerificationRequest.KeyVaultName</c>, <c>PendingKvSecretWrite.SecretName</c>.
    /// </para>
    /// <para>
    /// The fix is deliberately NOT a narrower value regex. This detector is CATASTROPHIC-severity
    /// and a narrower value pattern is how it goes quiet. It is a name-vs-reference discriminator
    /// applied AFTER the value regex matches, so the set of shapes treated as secret-bearing is
    /// unchanged — only names that are self-evidently pointers stop being reported.
    /// </para>
    /// <para>
    /// The precedent already existed as a whole-type exclusion: <c>KeyVaultSecretRef</c> was
    /// excluded from day 1 solely because "its <c>SecretName</c> property matches the regex but
    /// holds a KV secret NAME, not a value". That reasoning was always general; it was applied
    /// one type at a time. Generalizing it stops the exclusion list from growing an entry per
    /// occurrence of a mistake the rule can simply stop making
    /// (CLAUDE.md §11 — extend the mechanism, don't accumulate entries).
    /// </para>
    /// </remarks>
    private static readonly Regex SecretReferenceSuffix = new(
        @"(Name|Id|Identity|Uri|Url|Ref|Reference|Path)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Bare value-property names. Meaningless in isolation — <c>Value</c> is the most common
    /// property name in any codebase — but decisive on a type that ALSO carries a secret
    /// reference: that pairing is the shape of a secret carrier.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This rule exists because of what the #839 adjudication found.</b>
    /// <c>PendingKvSecretWrite(string VaultName, string SecretName, string Value)</c> was
    /// reported by this guard — for <c>SecretName</c>, which is a lookup key and harmless. Its
    /// <c>Value</c> property, which its own XML doc calls "CLEARTEXT … MUST NEVER be logged or
    /// persisted to Cosmos", matched nothing and was invisible.
    /// </para>
    /// <para>
    /// So the guard was pointing at the safe half of a record and missing the dangerous half.
    /// Exempting reference-shaped names WITHOUT this rule would have removed the only signal
    /// that type produced at all — trading a false positive for a false negative, on the
    /// detector where a false negative is the expensive direction.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> PlainSecretValuePropertyNames =
        new(StringComparer.OrdinalIgnoreCase) { "Value", "SecretValue", "PlainText", "Plaintext" };

    /// <summary>
    /// Known secret-VALUE literal shapes. A fixture payload targeting the runs
    /// container that contains any of these NOT wrapped in
    /// <c>@Microsoft.KeyVault(...)</c> is a violation.
    /// </summary>
    private static readonly (string Name, Regex Pattern)[] SecretShapedValuePatterns =
    {
        ("Stripe secret key (sk_live_/sk_test_)", new Regex(@"\bsk_(?:live|test)_[A-Za-z0-9]{16,}", RegexOptions.Compiled)),
        ("Generic personal-access-token (pat_)",  new Regex(@"\bpat_[A-Za-z0-9]{16,}", RegexOptions.Compiled)),
        ("GitHub PAT (ghp_)",                     new Regex(@"\bghp_[A-Za-z0-9]{16,}", RegexOptions.Compiled)),
        ("Slack bot/user token (xoxb-/xoxp-)",    new Regex(@"\bxox[bp]-[A-Za-z0-9-]{16,}", RegexOptions.Compiled)),
        ("Azure Storage connection string (AccountKey=)",       new Regex(@"AccountKey=[A-Za-z0-9+/=]{40,}", RegexOptions.Compiled)),
        ("Service Bus connection string (SharedAccessKey=)",    new Regex(@"SharedAccessKey=[A-Za-z0-9+/=]{20,}", RegexOptions.Compiled)),
        ("SQL Server connection string (Server=...Password=)",  new Regex(@"Server=[^;""]+;[^""]*Password=[^;""\s]+", RegexOptions.Compiled)),
        // Azure primary/secondary key shape: standalone 44+ char base64 blob (POML explicit).
        // Word-anchored to avoid matching JWT payloads mid-string and to align with JSON
        // string-value boundaries (a JSON "..." value has word boundaries at the quotes).
        ("Azure primary/secondary access key (base64 44+ chars)", new Regex(@"\b[A-Za-z0-9+/]{44,}={0,2}\b", RegexOptions.Compiled)),
    };

    /// <summary>
    /// KV-URI wrapper — any secret-shaped value INSIDE this wrapper is a
    /// config-time reference, not a cleartext leak. Stripped from fixture
    /// content before value-pattern scanning.
    /// </summary>
    private static readonly Regex KeyVaultReferenceWrap = new(
        @"@Microsoft\.KeyVault\([^)]+\)",
        RegexOptions.Compiled);

    // -----------------------------------------------------------------------
    // FACT (a) — L2 type shape
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR-27: Sprk.Provisioning.ControlPlane types have no string-typed secret-shape properties (must use KeyVaultSecretRef)")]
    public void L2Types_HaveNoStringTypedSecretShapedProperties()
    {
        // Scan every type declared in Sprk.Provisioning.ControlPlane* — ALL of them, across every L2
        // assembly (repaired 2026-08-27; see LoadL2Assemblies).
        // Excluded: types in <see cref="ExcludedTypeFullNames"/> — the
        // KeyVaultSecretRef compliant type + documented Path A exceptions.
        var typesToScan = L2Assemblies.Value
            .SelectMany(SafeGetTypes)
            .Where(t => t.Namespace is not null
                        && t.Namespace.StartsWith(L2NamespacePrefix, StringComparison.Ordinal))
            .Where(t => t.FullName is null || !ExcludedTypeFullNames.Contains(t.FullName));

        var offenders = typesToScan.SelectMany(SecretShapeOffendersOn).ToList();

        Assert.True(
            offenders.Count == 0,
            "FR-27 (spec.md) / design.md §6.2 (B3) violation: string-typed secret-shape " +
            "property(ies) on Sprk.Provisioning.ControlPlane POCO(s). Cosmos rows are a " +
            "queryable audit log; cleartext secrets there are a CATASTROPHIC leak vector. " +
            "Route secrets through KeyVaultSecretRef references only. If this is a genuinely " +
            "new requirement, follow CLAUDE.md §6.5 (path A/B) before adding an exclusion.\n" +
            $"Offenders:\n{string.Join("\n", offenders.OrderBy(x => x, StringComparer.Ordinal))}");
    }

    /// <summary>
    /// The whole secret-shape rule for ONE type. Extracted so the negative controls below run the
    /// REAL predicate against sample types instead of re-implementing it — a control that asserts
    /// against its own copy of the logic proves only that the copy agrees with itself.
    /// </summary>
    private static IEnumerable<string> SecretShapeOffendersOn(Type type)
    {
        var props = type.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string))
            .ToList();

        // Does this type carry a POINTER to a secret (SecretName, KeyVaultName, …)? On its own
        // that is compliant. Combined with a bare Value property it is not — see
        // PlainSecretValuePropertyNames for why that pairing is the tell.
        var carriesSecretReference = props.Any(p =>
            SecretShapedPropertyName.IsMatch(p.Name) && SecretReferenceSuffix.IsMatch(p.Name));

        foreach (var prop in props)
        {
            // (A) Value-shaped name: secret-shaped AND not a reference suffix.
            if (SecretShapedPropertyName.IsMatch(prop.Name) &&
                !SecretReferenceSuffix.IsMatch(prop.Name))
            {
                yield return
                    $"{type.FullName}.{prop.Name} : string — secret-shaped name on a " +
                    $"Cosmos-persisted POCO. Use KeyVaultSecretRef (URI-only reference) " +
                    $"instead. See RunParameters.Secrets for the compliant pattern.";
                continue;
            }

            // (B) Secret-carrier shape: a bare value property on a type that also names a secret.
            // `Value` alone is noise; `SecretName` + `Value` is a credential in transit. This rule
            // is what keeps (A)'s reference exemption from opening a false negative —
            // PendingKvSecretWrite.Value is caught here, not by (A).
            if (carriesSecretReference && PlainSecretValuePropertyNames.Contains(prop.Name))
            {
                yield return
                    $"{type.FullName}.{prop.Name} : string — SECRET-CARRIER shape: this type " +
                    $"names a secret (a *Name/*Id reference property) AND carries a bare " +
                    $"'{prop.Name}' property, i.e. the credential itself. Use KeyVaultSecretRef " +
                    $"so the value is resolved at use time, or add a documented Path A " +
                    $"exclusion if the type is provably transient and never reaches Cosmos.";
            }
        }
    }

    // -----------------------------------------------------------------------
    // FACT (b) — fixture payload scan
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR-27: test-fixture payloads targeting the runs container carry no cleartext secret literals")]
    public void RunsFixturePayloads_HaveNoCleartextSecretLiterals()
    {
        var repoRoot = ResolveRepoRoot();
        var testsRoot = Path.Combine(repoRoot, "tests");
        if (!Directory.Exists(testsRoot))
        {
            // Nothing to scan (bootstrap / detached test-project layout). Pass.
            return;
        }

        // In-scope files: any .json under tests/**/fixtures/**.
        var fixtureFiles = Directory
            .EnumerateFiles(testsRoot, "*.json", SearchOption.AllDirectories)
            .Where(p => p.Replace('\\', '/').Contains("/fixtures/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Runs-container discriminators. A fixture is in-scope for this guard only if
        // it references the L2 provisioning shape — we do NOT scan unrelated fixtures
        // (e.g. a Compose payload with a legitimate base64 body). This keeps the
        // guard narrow + false-positive-free.
        var runsContainerNeedles = new[]
        {
            "ProvisioningRun",           // type name
            "spaarke-provisioning",      // account/database name (design.md §6.2)
            "\"currentPhase\"",          // canonical run-doc field
            "\"gateStates\"",            // canonical run-doc field
            "\"interStepState\"",        // canonical run-doc field
            "\"tenancyModel\"",          // canonical run-doc field
        };

        var offenders = new List<string>();
        foreach (var file in fixtureFiles)
        {
            var content = File.ReadAllText(file);
            var targetsRuns = runsContainerNeedles.Any(n =>
                content.Contains(n, StringComparison.Ordinal));
            if (!targetsRuns) continue;

            // Strip KV-wrapped tokens — anything inside @Microsoft.KeyVault(...) is a
            // config-time reference, not a persisted secret value.
            var stripped = KeyVaultReferenceWrap.Replace(content, "<KV_REF>");

            foreach (var (name, pattern) in SecretShapedValuePatterns)
            {
                var match = pattern.Match(stripped);
                if (!match.Success) continue;

                var rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                offenders.Add(
                    $"{rel} — matches [{name}] at char {match.Index} (fragment: " +
                    $"'{Snippet(match.Value)}'). Wrap the secret in " +
                    $"@Microsoft.KeyVault(SecretUri=https://.../secrets/.../) " +
                    $"or replace with a KeyVaultSecretRef stub.");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "FR-27 (spec.md) violation: fixture payload(s) targeting the runs container " +
            "contain cleartext secret literal(s). Fixtures round-trip into Cosmos-shape " +
            "types — a secret here becomes a persisted cleartext leak. Wrap in " +
            "@Microsoft.KeyVault(...) or use a KeyVaultSecretRef stub.\n" +
            $"Offenders:\n{string.Join("\n", offenders.OrderBy(x => x, StringComparer.Ordinal))}");
    }

    // -----------------------------------------------------------------------
    // POSITIVE control — the ONE compliant shape passes as expected
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR-27 positive: RunParameters.Secrets is IDictionary<string, KeyVaultSecretRef> (compliant shape)")]
    public void RunParameters_Secrets_IsKeyVaultSecretRefDictionary()
    {
        // The compliant-shape invariant: RunParameters.Secrets STRUCTURALLY prevents
        // cleartext string secrets by typing the value as KeyVaultSecretRef.
        // Task POML 025 constraint (line 43): "RunParameters.Secrets typed as
        // IDictionary<string, KeyVaultSecretRef> MUST PASS the test (compliant shape)."
        var runParameters = L2Assemblies.Value
            .Select(a => a.GetType(L2RunParametersFullName, throwOnError: false))
            .FirstOrDefault(t => t is not null);

        Assert.True(
            runParameters is not null,
            $"'{L2RunParametersFullName}' was not found in any loaded L2 assembly " +
            $"({string.Join(", ", L2Assemblies.Value.Select(a => a.GetName().Name))}). " +
            $"If the type moved, update {nameof(L2RunParametersFullName)} — the compliant-shape " +
            $"invariant still applies wherever it lives.");
        var prop = runParameters.GetProperty(L2SecretsPropertyName);
        Assert.NotNull(prop);

        var propType = prop!.PropertyType;
        Assert.True(
            propType.IsGenericType,
            $"RunParameters.Secrets must be a generic collection type, got '{propType.FullName}'.");
        Assert.Equal(typeof(IDictionary<,>), propType.GetGenericTypeDefinition());

        var genArgs = propType.GetGenericArguments();
        Assert.Equal(typeof(string), genArgs[0]);
        Assert.Equal(CompliantSecretRefFullName, genArgs[1].FullName);
    }

    // -----------------------------------------------------------------------
    // NEGATIVE controls — prove the rules would flag a real violation
    // (avoids the tautology anti-pattern per GodClassGuardTests convention)
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR-27 negative control: property-name regex flags each configured secret shape")]
    public void SecretShapedPropertyName_NegativeControl_FlagsKnownShapes()
    {
        // Prove the regex actually catches every shape the guard is supposed to catch.
        // If the regex is silently weakened during a refactor, this test fails first.
        var shouldMatch = new[]
        {
            "Secret", "Password", "Key", "ClientSecret", "ApiKey", "ConnectionString",
            "Token", "PrivateKey", "Certificate", "Credential", "Bearer", "AccessKey",
            "AuthToken",
            // Case-insensitive
            "secret", "PASSWORD", "clientSecret",
            // Prefix-anchored: a longer name that STARTS with a secret-shape is flagged
            "SecretValue", "PasswordHash", "PrivateKeyPem", "TokenLifetime",
        };

        foreach (var name in shouldMatch)
        {
            Assert.True(
                SecretShapedPropertyName.IsMatch(name),
                $"Regex should have flagged '{name}' as secret-shaped, but did not. " +
                $"The property-name catalog has silently weakened.");
        }
    }

    [Fact(DisplayName = "FR-27 negative control: property-name regex does NOT over-match benign identifiers")]
    public void SecretShapedPropertyName_NegativeControl_PermitsBenignIdentifiers()
    {
        // Prove the regex is start-anchored — names that MERELY contain 'Key' or 'Secret'
        // in the middle/end are legitimate identifiers (routing keys, partition keys, IDs)
        // and MUST NOT trip the guard.
        var shouldNotMatch = new[]
        {
            "IdempotencyKey", "PartitionKey", "RowKey",
            "JobId", "CustomerId", "RunId", "EnvironmentId", "SystemUserId",
            "VaultName", "VersionId", "Phase", "Reason",
            "OpenAiEndpoint", "AiSearchEndpoint", "CosmosEndpoint",
        };

        foreach (var name in shouldNotMatch)
        {
            Assert.False(
                SecretShapedPropertyName.IsMatch(name),
                $"Regex should NOT flag '{name}' — it is a benign identifier. " +
                $"The property-name catalog has silently over-broadened and will " +
                $"false-positive on legitimate L2 properties.");
        }
    }

    // -----------------------------------------------------------------------
    // NEGATIVE controls for the #839 name-vs-value discriminator + carrier rule
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sample shapes for the controls below. Never instantiated; inspected reflectively.
    /// These MUST be properties, not fields — the guard scans <see cref="Type.GetProperties()"/>,
    /// so field-shaped samples would make every control pass vacuously.
    /// </summary>
    private sealed class SampleKvReference     { public string? SecretName { get; set; } public string? KeyVaultName { get; set; } }
    private sealed class SampleSecretCarrier   { public string? SecretName { get; set; } public string? Value { get; set; } }
    private sealed class SampleCleartextSecret { public string? ClientSecret { get; set; } }
    private sealed class SampleBenignValueBag  { public string? Value { get; set; } public string? DisplayName { get; set; } }

    [Fact(DisplayName = "FR-27 negative control: a pure secret REFERENCE (SecretName/KeyVaultName) is not an offender")]
    public void SecretReferenceSuffix_ExemptsPointerShapes()
    {
        // The five #839 false positives were all this shape. A vault name and a secret name are
        // lookup keys; persisting them leaks nothing.
        Assert.Empty(SecretShapeOffendersOn(typeof(SampleKvReference)));
    }

    [Fact(DisplayName = "FR-27 negative control: the reference exemption does NOT weaken value detection")]
    public void SecretReferenceSuffix_StillFlagsCleartextValues()
    {
        // The discriminator must not become a way to smuggle a real secret past the guard.
        var offenders = SecretShapeOffendersOn(typeof(SampleCleartextSecret)).ToList();
        Assert.Single(offenders);
        Assert.Contains("ClientSecret", offenders[0], StringComparison.Ordinal);
    }

    [Fact(DisplayName = "FR-27 negative control: SecretName + Value (the PendingKvSecretWrite shape) IS an offender")]
    public void SecretCarrierRule_FlagsNamePlusValuePairing()
    {
        // This is the finding the guard used to miss entirely: it reported the harmless
        // SecretName and said nothing about the cleartext sitting next to it. If this control
        // ever goes green-by-emptiness, the carrier rule has been silently removed.
        var offenders = SecretShapeOffendersOn(typeof(SampleSecretCarrier)).ToList();
        Assert.Single(offenders);
        Assert.Contains("SECRET-CARRIER", offenders[0], StringComparison.Ordinal);
        Assert.Contains(".Value", offenders[0], StringComparison.Ordinal);
    }

    [Fact(DisplayName = "FR-27 negative control: a bare Value property with no secret reference is NOT an offender")]
    public void SecretCarrierRule_DoesNotOverMatchPlainValueProperties()
    {
        // `Value` is one of the most common property names there is. The carrier rule fires only
        // on the PAIRING; without this control, widening it to "any Value property" would look
        // like a stricter guard and would in practice make it unusable.
        Assert.Empty(SecretShapeOffendersOn(typeof(SampleBenignValueBag)));
    }

    // -----------------------------------------------------------------------
    // Documented failing sample (compile-time inert — see task 025 step 4)
    // -----------------------------------------------------------------------
    //
    // Below is what a VIOLATION would look like, wrapped in #if false so the
    // compiler never sees it. If this block were added AS A REAL TYPE inside the
    // Sprk.Provisioning.ControlPlane.Models namespace (i.e. in a .cs file under
    // src/server/services/Sprk.Provisioning.ControlPlane/Models/), the Fact
    // L2Types_HaveNoStringTypedSecretShapedProperties would FAIL with a diagnostic
    // naming this exact type + property. Task 025 step 5 requires exercising this
    // negative-verification pathway during authoring — see the task's completion
    // report for the manual reproduction cycle (add BadShape.cs to L2 Models,
    // rebuild L2, re-run this test, confirm failure, delete BadShape.cs).
    //
    // #if false
    // namespace Sprk.Provisioning.ControlPlane.Models.BadSamples
    // {
    //     public sealed class BadShape_CleartextClientSecret_ShouldFailGuard
    //     {
    //         public string ClientSecret { get; set; } = string.Empty; // <-- would trip
    //     }
    // }
    // #endif

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string Snippet(string s)
        => s.Length <= 24 ? s : $"{s[..12]}...{s[^8..]}";

    /// <summary>
    /// Walk up from the test binary directory to the repo root (dir containing
    /// both <c>src/</c> and <c>tests/</c>). Same pattern as
    /// <see cref="GodClassGuardTests"/> — works on Windows AND Linux CI.
    /// </summary>
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                Directory.Exists(Path.Combine(dir.FullName, "tests")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }
}
