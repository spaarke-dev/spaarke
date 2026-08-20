// -----------------------------------------------------------------------------
// DataverseWebApiFieldMappingSeeder.cs
//
// Production IAppConfigSeeder implementation for the FieldMapping scope
// (task 152, Wave G-5 Batch G-5B — H12b GREENFIELD seeder, not a port).
// Replaces the DeferredAppConfigSeeder no-op that previously covered this
// scope (task 004 §4b row 11 + §5b N3 delta) with a real Dataverse Web API
// upsert of the Spaarke Field Mapping Framework's default attorney-matrix
// configuration.
//
// WHY GREENFIELD (not a script port): DS-4 §3 confirmed field-mapping had
// NO shipping repo source of ANY kind (not even a pwsh script) — unlike the
// sibling DataGrid/WorkspaceLayout scopes task 151 ported. DS-1b §1's H12b
// row: "Must be authored from scratch anyway under every option; author
// directly as C# seeders, zero double-work."
//
// SEED CONTENT SOURCE (per the POML's escalation trigger — "if the default
// content is NOT clearly specified anywhere ... STOP and escalate" — it IS
// clearly specified here, so no escalation was needed):
//   docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md ("the initial
//   Matter→{Event, Invoice, Report Card} attorney-matrix configuration")
//   + docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md "Worked Example" section
//   (names the exact 8 Matter→Event Copy rules + documents the Invoice/
//   Report Card field-name divergence) + the same guide's "Seeding
//   programmatically" section (documents the exact @odata.bind nav-property
//   names this seeder uses). Every field name below was additionally
//   ground-truthed live against spaarkedev1 via the Dataverse MCP
//   (`describe('tables/sprk_matter')`, `tables/sprk_event`,
//   `tables/sprk_invoice`, `tables/sprk_reportcard`,
//   `tables/sprk_fieldmappingprofile`, `tables/sprk_fieldmappingrule`,
//   `tables/sprk_recordtype_ref`) AND against the 3 live, Active profiles +
//   their live rule rows (read_query against sprk_fieldmappingprofile /
//   sprk_fieldmappingrule) — the seed data below is a literal, verified
//   mirror of what is live today, not an invented approximation.
//
// SCOPE DECISION (documented, not silent — CLAUDE.md §6.5 spirit): a 4th
// active profile exists live in spaarkedev1 ("Matter to Work Assignment
// (Attorney Matrix)") that is NOT named anywhere in either doc's Worked
// Example / Overview text and carries only 1 of the 8 rules a complete
// attorney-matrix would have (and that single rule's own sprk_name string
// — "sprk_assignedattorney1 -> sprk_assignedtoattorney1" — does not match
// its actual sprk_targetfield value of "sprk_assignedattorney1", i.e. the
// live row's own label is already inconsistent with its data). This reads
// as in-progress/incomplete configuration, not a finished, doc-backed
// default. It is INTENTIONALLY EXCLUDED from this seeder's default set —
// only the 3 profiles the architecture doc's Worked Example explicitly
// documents are seeded. Follow-up: either complete + document the Work
// Assignment profile (a 4th FieldMappingSeedItem entry, same idiom) in a
// later task, or confirm it is dev-only cruft to be left unseeded by design.
//
// WHY IN-CODE, NOT AN EMBEDDED JSON RESOURCE (unlike the sibling task 151
// seeders): DataGrid/WorkspaceLayout each mirror an EXISTING external
// source-of-truth file (a shared-lib config JSON / scripts/system-
// layouts.json) that could independently drift from the seeder, so
// embedding a copy at build time keeps them in sync. Field-mapping has NO
// existing external source-of-truth artifact — the content below, ground-
// truthed directly against live Dataverse via MCP, IS the source of truth
// for this scope. Representing it as static C# data (rather than inventing
// a parallel JSON file with no other consumer) avoids an unnecessary extra
// artifact per CLAUDE.md §11 (extend, don't multiply near-duplicate
// mechanisms) while keeping the same "author directly as C# seeders" DS-1b
// directive.
//
// UPSERT SEMANTICS: profile-level and rule-level are both find-then-skip-if-
// found (parity with the sibling DataverseWebApiWorkspaceLayoutSeeder's
// default no-refresh behavior, NOT DataGrid's always-refresh behavior) —
// these are admin-authored config records per the Field Mapping Admin
// Guide ("the native form is the supported human path"); a customer admin
// may extend the seeded defaults (add more rules, tweak execution order),
// and a re-run of this seeder (e.g. on an idempotency retry) must never
// clobber that. If not found: POST a new profile bound via
// sprk_sourcerecordtype@odata.bind / sprk_targetrecordtype@odata.bind to
// the resolved sprk_recordtype_ref rows, then POST each of its rules bound
// via sprk_FieldMappingProfile@odata.bind (exact casing per the admin
// guide's ground-truthed seeding recipe — Dataverse nav-property binds are
// case-sensitive).
//
// FK PRE-REQUISITE (fail-loud, not silently skipped): a profile cannot be
// created until BOTH its source and target entities have a row in
// sprk_recordtype_ref (docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md
// Troubleshooting: "No sprk_recordtype_ref row exists for the source and/or
// target entity ... this happened for Report Card during initial seeding").
// This seeder does NOT create missing sprk_recordtype_ref rows itself (that
// is core reference-data schema-adjacent seeding out of this scope's
// declared 2-file surface, and every consumer of the entity — RegardingResolver,
// the ADR-024 resolver ecosystem — needs those rows to exist regardless of
// field-mapping) — a missing row returns AppConfigSeederResult.Failed with a
// diagnostic naming the exact remediation from the admin guide.
//
// AUTH: DefaultAzureCredential pinned to the L2 UAMI — same DAG-position
// rationale as the sibling task 151 seeders (H12b runs after H10 in
// design.md's handler DAG, so the UAMI is already a registered Dataverse
// Application User).
//
// NOT under test in the CI unit suite for the credential-acquisition path
// itself (real DefaultAzureCredential chain) — DataverseWebApiFieldMappingSeederTests
// injects a fake TokenCredential via the internal test-seam constructor,
// never Mock&lt;HttpMessageHandler&gt; (banned per ADR-038/testing.md).
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;

/// <summary>
/// <see cref="IAppConfigSeeder"/> implementation that upserts (create-if-missing,
/// skip-if-present) the default Spaarke Field Mapping Framework attorney-matrix
/// configuration — 3 <c>sprk_fieldmappingprofile</c> rows (Matter→Event,
/// Matter→Invoice, Matter→Report Card) and their Copy rules — via direct
/// Dataverse Web API calls.
/// </summary>
public sealed class DataverseWebApiFieldMappingSeeder : IAppConfigSeeder
{
    /// <summary>Named HttpClient for outbound Dataverse Web API calls.</summary>
    public const string HttpClientName = "H12b.DataverseWebApiFieldMappingSeeder";

    private const string ODataVersion = "4.0";
    private const string MatterLogicalName = "sprk_matter";

    /// <summary>
    /// The default attorney-matrix profiles this seeder maintains. Ground-truthed
    /// live against spaarkedev1's Active sprk_fieldmappingprofile +
    /// sprk_fieldmappingrule rows (see file header). Exposed <c>internal</c> for
    /// direct unit testing.
    /// </summary>
    internal static readonly IReadOnlyList<FieldMappingProfileSeedItem> SeedProfiles = new[]
    {
        new FieldMappingProfileSeedItem(
            ProfileName: "Matter to Event (Attorney Matrix)",
            TargetEntityLogicalName: "sprk_event",
            Rules: new[]
            {
                new FieldMappingRuleSeedItem("sprk_assignedattorney1", "sprk_assignedattorney1", 1),
                new FieldMappingRuleSeedItem("sprk_assignedattorney2", "sprk_assignedattorney2", 2),
                new FieldMappingRuleSeedItem("sprk_assignedparalegal1", "sprk_assignedparalegal1", 3),
                new FieldMappingRuleSeedItem("sprk_assignedparalegal2", "sprk_assignedparalegal2", 4),
                new FieldMappingRuleSeedItem("sprk_assignedlawfirm1", "sprk_assignedlawfirm1", 5),
                new FieldMappingRuleSeedItem("sprk_assignedlawfirm2", "sprk_assignedlawfirm2", 6),
                new FieldMappingRuleSeedItem("sprk_assignedtoexternal", "sprk_assignedtoexternal", 7),
                new FieldMappingRuleSeedItem("sprk_assignedtointernal", "sprk_assignedtointernal", 8),
            }),
        new FieldMappingProfileSeedItem(
            ProfileName: "Matter to Invoice (Attorney Matrix)",
            TargetEntityLogicalName: "sprk_invoice",
            Rules: new[]
            {
                // Invoice renames attorney/paralegal fields; has NO law-firm field
                // and NO external/internal field at all (verified via MCP
                // describe('tables/sprk_invoice') — those 4 rules are correctly
                // omitted, not mapped to something that doesn't exist).
                new FieldMappingRuleSeedItem("sprk_assignedattorney1", "sprk_assignedtoattorney1", 1),
                new FieldMappingRuleSeedItem("sprk_assignedattorney2", "sprk_assignedtoattorney2", 2),
                new FieldMappingRuleSeedItem("sprk_assignedparalegal1", "sprk_assignedtoparalegal1", 3),
                new FieldMappingRuleSeedItem("sprk_assignedparalegal2", "sprk_assignedtoparalegal2", 4),
            }),
        new FieldMappingProfileSeedItem(
            ProfileName: "Matter to Report Card (Attorney Matrix)",
            TargetEntityLogicalName: "sprk_reportcard",
            Rules: new[]
            {
                // Report Card matches Matter's names for attorney/paralegal/
                // external/internal but renames law-firm 1 specifically
                // (sprk_assignedlawfirm1 -> sprk_assignedtolawfirm1); law-firm 2
                // keeps the same name. Verified via MCP describe('tables/sprk_reportcard').
                new FieldMappingRuleSeedItem("sprk_assignedattorney1", "sprk_assignedattorney1", 1),
                new FieldMappingRuleSeedItem("sprk_assignedattorney2", "sprk_assignedattorney2", 2),
                new FieldMappingRuleSeedItem("sprk_assignedparalegal1", "sprk_assignedparalegal1", 3),
                new FieldMappingRuleSeedItem("sprk_assignedparalegal2", "sprk_assignedparalegal2", 4),
                new FieldMappingRuleSeedItem("sprk_assignedlawfirm1", "sprk_assignedtolawfirm1", 5),
                new FieldMappingRuleSeedItem("sprk_assignedlawfirm2", "sprk_assignedlawfirm2", 6),
                new FieldMappingRuleSeedItem("sprk_assignedtoexternal", "sprk_assignedtoexternal", 7),
                new FieldMappingRuleSeedItem("sprk_assignedtointernal", "sprk_assignedtointernal", 8),
            }),
    };

    private readonly HttpClient _httpClient;
    private readonly AppConfigSeedOptions _options;
    private readonly Func<string, TokenCredential> _credentialFactory;
    private readonly ILogger<DataverseWebApiFieldMappingSeeder> _logger;

    /// <inheritdoc/>
    public string ScopeName => AppConfigSeedScopes.FieldMapping;

    /// <summary>Constructs the seeder bound to a typed <see cref="HttpClient"/> (production).</summary>
    public DataverseWebApiFieldMappingSeeder(
        HttpClient httpClient,
        IOptions<AppConfigSeedOptions> options,
        ILogger<DataverseWebApiFieldMappingSeeder> logger)
        : this(httpClient, options, logger,
              tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }))
    {
    }

    /// <summary>
    /// Test seam constructor — injects a <paramref name="credentialFactory"/>
    /// so tests never invoke the real DefaultAzureCredential chain.
    /// </summary>
    internal DataverseWebApiFieldMappingSeeder(
        HttpClient httpClient,
        IOptions<AppConfigSeedOptions> options,
        ILogger<DataverseWebApiFieldMappingSeeder> logger,
        Func<string, TokenCredential> credentialFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(credentialFactory);
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _credentialFactory = credentialFactory;
        _httpClient.Timeout = _options.DataverseRequestTimeout;
    }

    /// <inheritdoc/>
    public async Task<AppConfigSeederResult> SeedAsync(
        AppConfigSeedInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.TargetDataverseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.TenantId);

        if (!Uri.TryCreate(input.TargetDataverseUrl, UriKind.Absolute, out var envUri))
        {
            return AppConfigSeederResult.Failed(
                $"field-mapping seed FAILED — target Dataverse URL '{input.TargetDataverseUrl}' is not a valid absolute URI.");
        }

        AccessToken token;
        try
        {
            var scope = $"{new Uri(envUri, "/")}".TrimEnd('/') + "/.default";
            var credential = _credentialFactory(input.TenantId);
            token = await credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "H12b field-mapping seeder token acquisition failed for env={EnvUrl}", input.TargetDataverseUrl);
            return AppConfigSeederResult.Failed(
                $"field-mapping seed FAILED — token acquisition error: {ex.GetType().Name}: {ex.Message}");
        }

        var recordTypeCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var processed = new List<string>(SeedProfiles.Count);

        foreach (var profile in SeedProfiles)
        {
            var (failure, outcome) = await UpsertProfileAsync(
                envUri, token.Token, profile, recordTypeCache, cancellationToken).ConfigureAwait(false);
            if (failure is not null)
            {
                var diagnostic =
                    $"field-mapping seed FAILED for profile '{profile.ProfileName}': {failure.Diagnostic} " +
                    (processed.Count > 0
                        ? $"({processed.Count} profile(s) already processed this invocation: {string.Join(", ", processed)}.) "
                        : string.Empty) +
                    "Every upsert is find-by-name -> create-if-missing (skip if present), so a full retry is safe.";
                return AppConfigSeederResult.Failed(diagnostic, failure.Evidence);
            }
            processed.Add($"{profile.ProfileName} ({outcome})");
        }

        var okDiagnostic = $"field-mapping seed OK — {processed.Count} profile(s) processed: {string.Join("; ", processed)}.";
        return AppConfigSeederResult.Ok(okDiagnostic, BuildEvidence(processed));
    }

    /// <summary>
    /// Resolves both recordtype_ref GUIDs (source=Matter, target=profile's
    /// target entity), then finds-or-creates the profile, then finds-or-creates
    /// each of its rules. Returns (null, "created N rule(s)"|"skipped (exists);
    /// M rule(s) processed") on success or (Failed, "") on a classified failure.
    /// </summary>
    private async Task<(AppConfigSeederResult? Failure, string Outcome)> UpsertProfileAsync(
        Uri envUri,
        string bearerToken,
        FieldMappingProfileSeedItem profile,
        Dictionary<string, string> recordTypeCache,
        CancellationToken cancellationToken)
    {
        var sourceIdResult = await ResolveRecordTypeRefIdAsync(
            envUri, bearerToken, MatterLogicalName, recordTypeCache, cancellationToken).ConfigureAwait(false);
        if (sourceIdResult.Failure is not null)
        {
            return (sourceIdResult.Failure, string.Empty);
        }

        var targetIdResult = await ResolveRecordTypeRefIdAsync(
            envUri, bearerToken, profile.TargetEntityLogicalName, recordTypeCache, cancellationToken).ConfigureAwait(false);
        if (targetIdResult.Failure is not null)
        {
            return (targetIdResult.Failure, string.Empty);
        }

        var profileIdResult = await FindOrCreateProfileAsync(
            envUri, bearerToken, profile.ProfileName, sourceIdResult.Id!, targetIdResult.Id!, cancellationToken).ConfigureAwait(false);
        if (profileIdResult.Failure is not null)
        {
            return (profileIdResult.Failure, string.Empty);
        }

        var ruleOutcomes = new List<string>(profile.Rules.Count);
        foreach (var rule in profile.Rules)
        {
            var (ruleFailure, ruleOutcome) = await FindOrCreateRuleAsync(
                envUri, bearerToken, profileIdResult.Id!, profile.ProfileName, rule, cancellationToken).ConfigureAwait(false);
            if (ruleFailure is not null)
            {
                return (AppConfigSeederResult.Failed(
                    $"rule '{rule.SourceField} -> {rule.TargetField}' FAILED: {ruleFailure.Diagnostic} " +
                    (ruleOutcomes.Count > 0
                        ? $"({ruleOutcomes.Count} rule(s) already processed for this profile: {string.Join(", ", ruleOutcomes)}.)"
                        : string.Empty),
                    ruleFailure.Evidence), string.Empty);
            }
            ruleOutcomes.Add($"{rule.SourceField}->{rule.TargetField} ({ruleOutcome})");
        }

        return (null, $"{profileIdResult.Outcome}; {ruleOutcomes.Count} rule(s): {string.Join(", ", ruleOutcomes)}");
    }

    /// <summary>
    /// Resolves a <c>sprk_recordtype_ref</c> row's GUID by logical name,
    /// caching the result for the lifetime of one <see cref="SeedAsync"/>
    /// invocation (the same source entity — Matter — is reused across all 3
    /// profiles). Fails loud (never silently invents/skips) when the row is
    /// missing — see file header FK PRE-REQUISITE note.
    /// </summary>
    private async Task<(string? Id, AppConfigSeederResult? Failure)> ResolveRecordTypeRefIdAsync(
        Uri envUri, string bearerToken, string logicalName, Dictionary<string, string> cache, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(logicalName, out var cachedId))
        {
            return (cachedId, null);
        }

        var filter = $"sprk_recordlogicalname eq '{logicalName.Replace("'", "''", StringComparison.Ordinal)}'";
        var getUri = new Uri(envUri,
            $"/api/data/v9.2/sprk_recordtype_refs?$filter={Uri.EscapeDataString(filter)}" +
            "&$select=sprk_recordtype_refid,sprk_recordlogicalname");

        JsonDocument getDoc;
        try
        {
            using var getRequest = BuildRequest(HttpMethod.Get, getUri, bearerToken);
            using var getResponse = await _httpClient.SendAsync(getRequest, cancellationToken).ConfigureAwait(false);
            if (!getResponse.IsSuccessStatusCode)
            {
                var body = await getResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return (null, AppConfigSeederResult.Failed(
                    $"recordtype_ref lookup GET for '{logicalName}' returned {(int)getResponse.StatusCode} " +
                    $"{getResponse.StatusCode}. Body: {Truncate(body, 400)}"));
            }
            var text = await getResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            getDoc = JsonDocument.Parse(text);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, AppConfigSeederResult.Failed(
                $"recordtype_ref lookup GET for '{logicalName}' infrastructure error: {ex.GetType().Name}: {ex.Message}"));
        }

        using (getDoc)
        {
            var values = getDoc.RootElement.GetProperty("value");
            if (values.GetArrayLength() == 0)
            {
                return (null, AppConfigSeederResult.Failed(
                    $"no sprk_recordtype_ref row exists for logical name '{logicalName}'. Per " +
                    "docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md Troubleshooting, a target/source entity must have a " +
                    "sprk_recordtype_ref row before a field-mapping profile can reference it — a developer must add " +
                    "the missing registry row (this happened for Report Card during the framework's initial live " +
                    "seeding and is a known onboarding step, not an error in this seeder)."));
            }
            var id = values[0].GetProperty("sprk_recordtype_refid").GetString()!;
            cache[logicalName] = id;
            return (id, null);
        }
    }

    /// <summary>
    /// Finds an existing profile by <c>sprk_name</c>; if absent, POSTs a new
    /// one bound to the resolved source/target recordtype_ref rows. Skip-if-
    /// found (parity with the sibling WorkspaceLayout seeder's default
    /// no-refresh behavior — see file header).
    /// </summary>
    private async Task<(string? Id, string Outcome, AppConfigSeederResult? Failure)> FindOrCreateProfileAsync(
        Uri envUri, string bearerToken, string profileName, string sourceRecordTypeId, string targetRecordTypeId,
        CancellationToken cancellationToken)
    {
        var escapedName = profileName.Replace("'", "''", StringComparison.Ordinal);
        var getUri = new Uri(envUri,
            $"/api/data/v9.2/sprk_fieldmappingprofiles?$filter={Uri.EscapeDataString($"sprk_name eq '{escapedName}'")}" +
            "&$select=sprk_fieldmappingprofileid,sprk_name");

        JsonDocument getDoc;
        try
        {
            using var getRequest = BuildRequest(HttpMethod.Get, getUri, bearerToken);
            using var getResponse = await _httpClient.SendAsync(getRequest, cancellationToken).ConfigureAwait(false);
            if (!getResponse.IsSuccessStatusCode)
            {
                var body = await getResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return (null, string.Empty, AppConfigSeederResult.Failed(
                    $"profile lookup GET returned {(int)getResponse.StatusCode} {getResponse.StatusCode}. " +
                    $"Body: {Truncate(body, 400)}"));
            }
            var text = await getResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            getDoc = JsonDocument.Parse(text);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, string.Empty, AppConfigSeederResult.Failed(
                $"profile lookup GET infrastructure error: {ex.GetType().Name}: {ex.Message}"));
        }

        using (getDoc)
        {
            var values = getDoc.RootElement.GetProperty("value");
            if (values.GetArrayLength() > 0)
            {
                var existingId = values[0].GetProperty("sprk_fieldmappingprofileid").GetString()!;
                return (existingId, $"skipped (exists: {existingId})", null);
            }
        }

        var createBody = new Dictionary<string, object?>
        {
            ["sprk_name"] = profileName,
            ["sprk_sourcerecordtype@odata.bind"] = $"/sprk_recordtype_refs({sourceRecordTypeId})",
            ["sprk_targetrecordtype@odata.bind"] = $"/sprk_recordtype_refs({targetRecordTypeId})",
        };

        var postUri = new Uri(envUri, "/api/data/v9.2/sprk_fieldmappingprofiles");
        using var postRequest = BuildRequest(HttpMethod.Post, postUri, bearerToken);
        postRequest.Content = JsonContent.Create(createBody);
        postRequest.Headers.Add("Prefer", "return=representation");

        using var postResponse = await _httpClient.SendAsync(postRequest, cancellationToken).ConfigureAwait(false);
        if (!postResponse.IsSuccessStatusCode)
        {
            var body = await postResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return (null, string.Empty, AppConfigSeederResult.Failed(
                $"POST sprk_fieldmappingprofiles returned {(int)postResponse.StatusCode} {postResponse.StatusCode}. " +
                $"Body: {Truncate(body, 400)}"));
        }

        var createdText = await postResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        string createdId;
        try
        {
            using var createdDoc = JsonDocument.Parse(createdText);
            createdId = createdDoc.RootElement.TryGetProperty("sprk_fieldmappingprofileid", out var idEl)
                ? idEl.GetString() ?? "(unknown)"
                : "(unknown)";
        }
        catch (JsonException)
        {
            createdId = "(unknown)";
        }
        return (createdId, $"created {createdId}", null);
    }

    /// <summary>
    /// Finds an existing rule by (profile, source field, target field); if
    /// absent, POSTs a new Copy rule bound to the profile. Skip-if-found
    /// (same rationale as the profile-level upsert).
    /// </summary>
    private async Task<(AppConfigSeederResult? Failure, string Outcome)> FindOrCreateRuleAsync(
        Uri envUri, string bearerToken, string profileId, string profileName,
        FieldMappingRuleSeedItem rule, CancellationToken cancellationToken)
    {
        var filter =
            $"sprk_fieldmappingprofile eq {profileId} and " +
            $"sprk_sourcefield eq '{rule.SourceField.Replace("'", "''", StringComparison.Ordinal)}' and " +
            $"sprk_targetfield eq '{rule.TargetField.Replace("'", "''", StringComparison.Ordinal)}'";
        var getUri = new Uri(envUri,
            $"/api/data/v9.2/sprk_fieldmappingrules?$filter={Uri.EscapeDataString(filter)}" +
            "&$select=sprk_fieldmappingruleid");

        JsonDocument getDoc;
        try
        {
            using var getRequest = BuildRequest(HttpMethod.Get, getUri, bearerToken);
            using var getResponse = await _httpClient.SendAsync(getRequest, cancellationToken).ConfigureAwait(false);
            if (!getResponse.IsSuccessStatusCode)
            {
                var body = await getResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return (AppConfigSeederResult.Failed(
                    $"rule lookup GET returned {(int)getResponse.StatusCode} {getResponse.StatusCode}. " +
                    $"Body: {Truncate(body, 400)}"), string.Empty);
            }
            var text = await getResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            getDoc = JsonDocument.Parse(text);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (AppConfigSeederResult.Failed(
                $"rule lookup GET infrastructure error: {ex.GetType().Name}: {ex.Message}"), string.Empty);
        }

        using (getDoc)
        {
            var values = getDoc.RootElement.GetProperty("value");
            if (values.GetArrayLength() > 0)
            {
                var existingId = values[0].GetProperty("sprk_fieldmappingruleid").GetString()!;
                return (null, $"skipped (exists: {existingId})");
            }
        }

        var createBody = new Dictionary<string, object?>
        {
            ["sprk_name"] = $"{rule.SourceField} -> {rule.TargetField} ({profileName})",
            ["sprk_FieldMappingProfile@odata.bind"] = $"/sprk_fieldmappingprofiles({profileId})",
            ["sprk_mapping_type"] = 0, // Copy — per FIELD-MAPPING-ADMIN-GUIDE.md Configuration Enum Reference
            ["sprk_sourcefield"] = rule.SourceField,
            ["sprk_targetfield"] = rule.TargetField,
            ["sprk_sourcefieldtype"] = 1, // Lookup
            ["sprk_targetfieldtype"] = 1, // Lookup
            ["sprk_executionorder"] = rule.ExecutionOrder,
            ["sprk_isactive"] = true,
        };

        var postUri = new Uri(envUri, "/api/data/v9.2/sprk_fieldmappingrules");
        using var postRequest = BuildRequest(HttpMethod.Post, postUri, bearerToken);
        postRequest.Content = JsonContent.Create(createBody);
        postRequest.Headers.Add("Prefer", "return=representation");

        using var postResponse = await _httpClient.SendAsync(postRequest, cancellationToken).ConfigureAwait(false);
        if (!postResponse.IsSuccessStatusCode)
        {
            var body = await postResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return (AppConfigSeederResult.Failed(
                $"POST sprk_fieldmappingrules returned {(int)postResponse.StatusCode} {postResponse.StatusCode}. " +
                $"Body: {Truncate(body, 400)}"), string.Empty);
        }

        var createdText = await postResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        string createdId;
        try
        {
            using var createdDoc = JsonDocument.Parse(createdText);
            createdId = createdDoc.RootElement.TryGetProperty("sprk_fieldmappingruleid", out var idEl)
                ? idEl.GetString() ?? "(unknown)"
                : "(unknown)";
        }
        catch (JsonException)
        {
            createdId = "(unknown)";
        }
        return (null, $"created {createdId}");
    }

    private static JsonElement BuildEvidence(IReadOnlyList<string> processed)
    {
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { profiles = processed }));
        return doc.RootElement.Clone();
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, Uri uri, string bearerToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("OData-Version", ODataVersion);
        request.Headers.Add("OData-MaxVersion", ODataVersion);
        return request;
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";

    /// <summary>One default field-mapping profile this seeder maintains. Exposed <c>internal</c> so tests can construct fixtures directly.</summary>
    internal sealed record FieldMappingProfileSeedItem(
        string ProfileName,
        string TargetEntityLogicalName,
        IReadOnlyList<FieldMappingRuleSeedItem> Rules);

    /// <summary>One Copy rule within a profile. Exposed <c>internal</c> so tests can construct fixtures directly.</summary>
    internal sealed record FieldMappingRuleSeedItem(string SourceField, string TargetField, int ExecutionOrder);
}
