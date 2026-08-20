// -----------------------------------------------------------------------------
// GraphAppRoleParityTest.cs
//
// Nightly (NOT per-PR) T3 silent-fail-trap safety net for the 14-role Microsoft
// Graph application (app-only) permission catalog owned by the BFF's
// Infrastructure/Auth/GraphAppRoles.cs constant.
//
// Purpose (POML task 067 goal):
//   (a) Reflectively enumerate Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles.All
//       (14 (Value, DisplayName, AppRoleId, ...) entries as of 2026-08-17 per r1
//       task 005's live `az ad sp show` enumeration).
//   (b) Query the customer environment's UAMI service principal
//       (envelope: AZURE_TENANT_ID + UAMI_SP_OBJECT_ID) for its
//       appRoleAssignments via Microsoft.Graph SDK v6, using an EXPLICIT tenantId
//       (§4D I5 — no `.default`-only ambient credential; TenantId flows through
//       DefaultAzureCredentialOptions).
//   (c) Compare the two sets — filter the SP's assignments to Graph-resource
//       (appId 00000003-0000-0000-c000-000000000000) so unrelated grants (e.g.,
//       ARM, Dataverse, custom app-regs) are not falsely flagged as "extra".
//   (d) Fail with a diff-formatted message (Missing / Extra with displayName +
//       GUID) so the operator can act without a debugger.
//
// Cadence: NIGHTLY only. Per spec.md ADR-038 §7 rationale + the r3 coord PR
// deferral note (`projects/code-quality-and-assurance-r3/notes/task-042-063-ci-
// gate-wiring-deferral.md`), live-Graph parity is nightly-only because:
//   - Graph API latency (~200-500ms/call) + tenant-level rate limits make
//     per-PR runs pathological when N PRs land in a burst.
//   - The drift signal we catch (someone revokes a role in the Portal; H10 add
//     hasn't propagated) is slow-moving; a 24h detection window is sufficient.
// See notes/graph-app-role-parity-coord-pr.md for the workflow-wiring spec that
// the ci-cd-unit-test-remediation-r1 worktree owner applies.
//
// Skip semantics: the live test uses [SkippableFact]. When AZURE_TENANT_ID or
// UAMI_SP_OBJECT_ID is unset, Skip.If yields "Skipped, no test failure" — safe
// on dev machines AND if the project is ever accidentally included in a per-PR
// job.
//
// Error handling (NFR-09 — Graph SDK v6 / Kiota 2.0):
//   catch ODataError (NOT ServiceException); ResponseStatusCode is int;
//   ResponseHeaders is dict. Surface status code + first-line message in the
//   thrown InvalidOperationException so the nightly workflow log shows the
//   HTTP failure verbatim.
//
// L2 mirror drift-guard (bonus, per POML deviation notes): the
// Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity
// .L2GraphAppRolesRegistry maintains a compiled byte-for-byte mirror of
// GraphAppRoles.All (see IGraphAppRolesRegistry.cs header — its "DRIFT GUARD"
// note delegates this check to task 067). The second [Fact] method below
// executes UNCONDITIONALLY (no live creds) and catches BFF↔L2 mirror drift.
//
// KEEP-path note: this project's path is not one of ADR-038's 7 canonical KEEP
// paths (integration/auth, regression, data-mutation, tenant, contract, seam;
// unit/domain). The POML explicitly names the path `tests/integration/
// Sprk.Provisioning.ControlPlane.NightlyTests/**`; see task-067-deviations.md.
// -----------------------------------------------------------------------------

using System.Reflection;
using System.Text;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.NightlyTests;

public sealed class GraphAppRoleParityTest
{
    /// <summary>
    /// Envelope-var name for the Entra tenant id (§4D I1 no-hardcoded-default).
    /// The coord-PR spec (notes/graph-app-role-parity-coord-pr.md) wires this
    /// from the AZURE_TENANT_ID GitHub Actions secret on the nightly workflow.
    /// </summary>
    private const string TenantIdEnvVar = "AZURE_TENANT_ID";

    /// <summary>
    /// Envelope-var name for the target customer's UAMI service principal
    /// OBJECT id (not appId). The coord-PR spec wires this from the
    /// UAMI_SP_OBJECT_ID GitHub Actions secret.
    /// </summary>
    private const string UamiSpObjectIdEnvVar = "UAMI_SP_OBJECT_ID";

    private static readonly IEnumerable<string> GraphDefaultScope =
        new[] { "https://graph.microsoft.com/.default" };

    // ----------------------------------------------------------------------
    // Test 1 (live, skippable): UAMI SP appRoleAssignments ↔ GraphAppRoles.cs.
    // ----------------------------------------------------------------------

    [SkippableFact]
    public async Task UamiServicePrincipal_AppRoleAssignments_MatchGraphAppRolesCatalog()
    {
        var tenantId = Environment.GetEnvironmentVariable(TenantIdEnvVar);
        var uamiSpObjectId = Environment.GetEnvironmentVariable(UamiSpObjectIdEnvVar);
        Skip.If(
            string.IsNullOrWhiteSpace(tenantId),
            $"Skipping live Graph parity test: {TenantIdEnvVar} not set. Nightly workflow must " +
            "supply this from a GitHub Actions secret; see notes/graph-app-role-parity-coord-pr.md.");
        Skip.If(
            string.IsNullOrWhiteSpace(uamiSpObjectId),
            $"Skipping live Graph parity test: {UamiSpObjectIdEnvVar} not set (target customer's " +
            "UAMI service principal OBJECT id — not appId).");

        // Reflectively read the BFF constant (POML step 1 + step 3). Reflection
        // — as opposed to a direct `GraphAppRoles.All` static reference — is
        // POML-mandated: it makes the test a genuine "constant scanner" that
        // fails LOUD (with a clear diagnostic) if the class/field is renamed,
        // rather than silently going stale.
        var expectedRoles = ReadExpectedRolesReflectively();
        expectedRoles.Should().HaveCount(
            14,
            "GraphAppRoles.All must enumerate exactly the 14 canonical roles "
            + "populated by r1 task 005 (5 SPE + 2 Directory + 4 Email + 3 Self-Service).");
        expectedRoles.Where(r => string.IsNullOrWhiteSpace(r.AppRoleId)).Should().BeEmpty(
            "All 14 AppRoleId GUIDs must be populated per H10 escalation gate "
            + "(spec.md MUST rule); r1 task 005 landed all 14 on 2026-08-17.");

        var graphResourceAppId = GetGraphResourceAppIdReflectively();

        // Explicit tenantId flow per §4D I5 — DefaultAzureCredentialOptions
        // .TenantId scopes token acquisition to the target tenant. Do NOT
        // rely on the `.default`-only ambient credential.
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            TenantId = tenantId,
        });

        var graph = new GraphServiceClient(credential, GraphDefaultScope);

        HashSet<string> actualGraphAppRoleIds;
        try
        {
            // (i) Resolve the Microsoft Graph resource SP id in THIS tenant so
            // we can filter the UAMI SP's assignments to Graph-only. Every
            // tenant surfaces Graph as a first-party SP with the well-known
            // appId 00000003-0000-0000-c000-000000000000 but a tenant-specific
            // object id — that object id is what appears as `resourceId` on
            // the assignment records.
            var graphSps = await graph.ServicePrincipals.GetAsync(rc =>
            {
                rc.QueryParameters.Filter = $"appId eq '{graphResourceAppId}'";
                rc.QueryParameters.Select = new[] { "id", "displayName" };
            });
            var graphSpIdRaw = graphSps?.Value?.FirstOrDefault()?.Id;
            if (string.IsNullOrWhiteSpace(graphSpIdRaw))
            {
                throw new InvalidOperationException(
                    $"Microsoft Graph resource service principal (appId={graphResourceAppId}) " +
                    $"was not found in tenant '{tenantId}'. Every Entra tenant should surface " +
                    "the first-party Graph SP; the tenant may be non-standard, or the credential " +
                    "lacks Directory.Read.All to enumerate service principals.");
            }

            // (ii) Query the UAMI SP's assignments and filter to Graph-scoped
            // entries only. Grants against unrelated resources (ARM, Dataverse,
            // custom app-regs) MUST NOT be flagged as "extra" — the parity
            // contract is with GraphAppRoles.cs, not the SP's whole grant list.
            var assignments = await graph.ServicePrincipals[uamiSpObjectId]
                .AppRoleAssignments
                .GetAsync();

            actualGraphAppRoleIds = new HashSet<string>(
                (assignments?.Value ?? new List<Microsoft.Graph.Models.AppRoleAssignment>())
                    .Where(a => string.Equals(
                        a.ResourceId?.ToString(),
                        graphSpIdRaw,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(a => a.AppRoleId?.ToString())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id!),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (ODataError ex)
        {
            // NFR-09: catch ODataError (Graph SDK v6 / Kiota 2.0 error type).
            // ResponseStatusCode is int; Error?.Message carries the OData
            // payload's first-line message. Surface both in the diagnostic so
            // the nightly workflow log shows the HTTP failure verbatim.
            throw new InvalidOperationException(
                $"Graph SDK v6 query failed for UAMI SP '{uamiSpObjectId}' in tenant '{tenantId}'. " +
                $"ResponseStatusCode={ex.ResponseStatusCode} " +
                $"Message={ex.Error?.Message ?? "(none)"} " +
                $"Code={ex.Error?.Code ?? "(none)"}. " +
                "Investigation: (1) does the workflow's managed identity have Directory.Read.All " +
                "+ Application.Read.All app roles? (2) is UAMI_SP_OBJECT_ID a service-principal " +
                "OBJECT id (not appId)? (3) is AZURE_TENANT_ID correct for THIS UAMI?",
                ex);
        }

        // Compute the diff (both directions) and format for operator triage.
        var expectedIdIndex = expectedRoles
            .Where(r => !string.IsNullOrWhiteSpace(r.AppRoleId))
            .ToDictionary(r => r.AppRoleId!, r => r, StringComparer.OrdinalIgnoreCase);

        var missing = expectedRoles
            .Where(r => !string.IsNullOrWhiteSpace(r.AppRoleId)
                        && !actualGraphAppRoleIds.Contains(r.AppRoleId!))
            .ToList();

        var extra = actualGraphAppRoleIds
            .Where(id => !expectedIdIndex.ContainsKey(id))
            .ToList();

        if (missing.Count == 0 && extra.Count == 0) return;

        var msg = FormatParityDiff(
            tenantId: tenantId!, uamiSpObjectId: uamiSpObjectId!,
            expectedRoles: expectedRoles, missing: missing, extra: extra);
        Assert.Fail(msg);
    }

    // ----------------------------------------------------------------------
    // Test 1b (live, skippable, task 143 addition): catalog GUID VALIDITY —
    // does each GraphAppRoles.cs (Value, AppRoleId) pair actually correspond
    // to a REAL Microsoft Graph app-only role definition on the Graph
    // resource SP? Test 1 (above) only checks whether a target SP HOLDS the
    // catalog's GUIDs as assignments; it never validates that the GUIDs
    // themselves are correct, so a wrong-but-populated GUID (a typo,
    // distinct from a null AppRoleId which the H10 escalation gate already
    // catches) would sail through Test 1 silently — the missing role would
    // just never be assignable, surfacing only much later as a repeated T3
    // Partial/RetryableWithCleanup failure with no obvious root cause.
    //
    // DISCOVERED LIVE (2026-08-20, task 143 H10 verification): exactly this
    // defect existed for GroupMember.ReadWrite.All — GraphAppRoles.cs (and
    // its L2GraphAppRolesRegistry mirror) carried AppRoleId
    // "...f871c94c6571", but the real Microsoft Graph resource SP's appRoles
    // collection defines that value at "...f871c94c6695" (last 4 hex chars
    // differ; the "6571" id does not exist on the SP at all). Fixed in the
    // SAME commit as this test; this test is the regression guard so the
    // class of defect cannot silently recur.
    //
    // Only needs AZURE_TENANT_ID (NOT UAMI_SP_OBJECT_ID — no target SP
    // required, only the tenant-invariant Graph resource SP itself), so it
    // has a LOWER bar to run than Test 1 and will exercise even before any
    // UAMI exists in a given environment.
    // ----------------------------------------------------------------------

    [SkippableFact]
    public async Task GraphAppRolesCatalog_AppRoleIds_MatchRealMicrosoftGraphAppRoleDefinitions()
    {
        var tenantId = Environment.GetEnvironmentVariable(TenantIdEnvVar);
        Skip.If(
            string.IsNullOrWhiteSpace(tenantId),
            $"Skipping live catalog-validity test: {TenantIdEnvVar} not set.");

        var expectedRoles = ReadExpectedRolesReflectively();
        var graphResourceAppId = GetGraphResourceAppIdReflectively();

        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });
        var graph = new GraphServiceClient(credential, GraphDefaultScope);

        Dictionary<string, string> realAppRolesByGuid;
        try
        {
            var graphSps = await graph.ServicePrincipals.GetAsync(rc =>
            {
                rc.QueryParameters.Filter = $"appId eq '{graphResourceAppId}'";
                rc.QueryParameters.Select = new[] { "id", "appRoles" };
            });
            var graphSp = graphSps?.Value?.FirstOrDefault();
            if (graphSp is null)
            {
                throw new InvalidOperationException(
                    $"Microsoft Graph resource service principal (appId={graphResourceAppId}) " +
                    $"was not found in tenant '{tenantId}'.");
            }

            realAppRolesByGuid = (graphSp.AppRoles ?? new List<Microsoft.Graph.Models.AppRole>())
                .Where(r => r.Id.HasValue && r.Value is not null)
                .ToDictionary(r => r.Id!.Value.ToString(), r => r.Value!, StringComparer.OrdinalIgnoreCase);
        }
        catch (ODataError ex)
        {
            throw new InvalidOperationException(
                $"Graph SDK v6 query failed resolving the Graph resource SP's appRoles in tenant '{tenantId}'. " +
                $"ResponseStatusCode={ex.ResponseStatusCode} Message={ex.Error?.Message ?? "(none)"}.",
                ex);
        }

        var mismatches = new List<string>();
        foreach (var role in expectedRoles)
        {
            if (string.IsNullOrWhiteSpace(role.AppRoleId))
            {
                continue; // null AppRoleId is the H10 escalation gate's concern, not this test's
            }
            if (!realAppRolesByGuid.TryGetValue(role.AppRoleId, out var realValue))
            {
                mismatches.Add($"{role.Value}: catalog AppRoleId '{role.AppRoleId}' does NOT exist on the " +
                    "Microsoft Graph resource SP at all (wrong GUID — check for a transcription error).");
            }
            else if (!string.Equals(realValue, role.Value, StringComparison.Ordinal))
            {
                mismatches.Add($"{role.Value}: catalog AppRoleId '{role.AppRoleId}' actually maps to " +
                    $"'{realValue}' on Microsoft Graph, not '{role.Value}' — catalog entry names the wrong role.");
            }
        }

        mismatches.Should().BeEmpty(
            "every populated AppRoleId in GraphAppRoles.cs must byte-match a REAL Microsoft Graph app-only " +
            "role definition (id -> value) on the well-known Graph resource SP; a wrong-but-non-null GUID " +
            "silently breaks every future grant attempt for that role. Mismatches:\n" +
            string.Join("\n", mismatches));
    }

    // ----------------------------------------------------------------------
    // Test 2 (unconditional): BFF GraphAppRoles.cs ↔ L2GraphAppRolesRegistry
    // mirror parity. Runs on every nightly invocation regardless of Graph
    // creds — this is a pure compile-time-referenced structural check that
    // the L2 mirror's own file header explicitly delegates to task 067.
    // ----------------------------------------------------------------------

    [Fact]
    public void L2GraphAppRolesRegistry_MirrorsBffGraphAppRolesConstant()
    {
        var bffRoles = ReadExpectedRolesReflectively();
        var bffGraphResourceAppId = GetGraphResourceAppIdReflectively();

        var registry = new L2GraphAppRolesRegistry();
        var l2Roles = registry.GetAll();

        registry.GraphResourceAppId.Should().Be(
            bffGraphResourceAppId,
            "L2GraphAppRolesRegistry.GraphResourceAppId must byte-match "
            + "GraphAppRoles.GraphResourceAppId — the well-known Microsoft "
            + "Graph resource SP appId is a tenant-invariant constant.");

        l2Roles.Should().HaveCount(
            bffRoles.Count,
            "L2 registry count must match BFF catalog count exactly — task 053 "
            + "created the L2 mirror as byte-for-byte parity per its file header.");

        // Build (Value, AppRoleId) dictionaries on each side and diff.
        var bffIndex = bffRoles.ToDictionary(
            r => r.Value, r => r.AppRoleId ?? string.Empty, StringComparer.Ordinal);
        var l2Index = l2Roles.ToDictionary(
            r => r.Value, r => r.AppRoleId ?? string.Empty, StringComparer.Ordinal);

        var bffOnly = bffIndex.Keys.Except(l2Index.Keys, StringComparer.Ordinal).ToList();
        var l2Only = l2Index.Keys.Except(bffIndex.Keys, StringComparer.Ordinal).ToList();
        var guidMismatches = bffIndex.Keys
            .Intersect(l2Index.Keys, StringComparer.Ordinal)
            .Where(v => !string.Equals(bffIndex[v], l2Index[v], StringComparison.OrdinalIgnoreCase))
            .Select(v => $"{v}: BFF='{bffIndex[v]}' L2='{l2Index[v]}'")
            .ToList();

        if (bffOnly.Count == 0 && l2Only.Count == 0 && guidMismatches.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine("BFF GraphAppRoles.All ↔ L2GraphAppRolesRegistry.GetAll() DRIFT DETECTED.");
        sb.AppendLine(
            "Per Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity.IGraphAppRolesRegistry"
            + " file header, this test (r1 task 067) is the drift-guard for the two-copy "
            + "catalog. Reconcile the L2 mirror to the BFF source of truth.");
        sb.AppendLine();
        if (bffOnly.Count > 0)
        {
            sb.AppendLine($"Present in BFF, missing from L2 mirror ({bffOnly.Count}):");
            foreach (var v in bffOnly) sb.AppendLine($"  - {v}");
            sb.AppendLine();
        }
        if (l2Only.Count > 0)
        {
            sb.AppendLine($"Present in L2 mirror, missing from BFF ({l2Only.Count}):");
            foreach (var v in l2Only) sb.AppendLine($"  - {v}");
            sb.AppendLine();
        }
        if (guidMismatches.Count > 0)
        {
            sb.AppendLine($"AppRoleId GUID mismatches ({guidMismatches.Count}):");
            foreach (var g in guidMismatches) sb.AppendLine($"  - {g}");
            sb.AppendLine();
        }
        sb.AppendLine("Remediation: edit src/server/services/Sprk.Provisioning.ControlPlane/"
                      + "Handlers/DataverseAppUserGraphParity/L2GraphAppRolesRegistry.cs to "
                      + "byte-match src/server/api/Sprk.Bff.Api/Infrastructure/Auth/GraphAppRoles.cs.");
        Assert.Fail(sb.ToString());
    }

    // ----------------------------------------------------------------------
    // Reflection helpers — POML-mandated. Reads GraphAppRoles.cs constant via
    // Assembly reflection so a rename of the class or its `All`/`GraphResourceAppId`
    // fields fails LOUD (with a clear diagnostic) instead of silently going stale.
    // ----------------------------------------------------------------------

    private const string GraphAppRolesTypeName = "Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles";
    private const string AllFieldName = "All";
    private const string GraphResourceAppIdFieldName = "GraphResourceAppId";

    private static IReadOnlyList<ExpectedRole> ReadExpectedRolesReflectively()
    {
        // typeof(GraphAppRoles) anchors the assembly discovery — a rename of
        // the class breaks the COMPILE (fastest possible feedback). Reflection
        // then enumerates the field, so a rename of `All` or a shape change to
        // the record entries fails at TEST time with a clear diagnostic.
        var assembly = typeof(GraphAppRoles).Assembly;
        var type = assembly.GetType(GraphAppRolesTypeName, throwOnError: false)
            ?? throw new InvalidOperationException(
                $"Type '{GraphAppRolesTypeName}' not found in assembly '{assembly.FullName}'. "
                + "Has the class been renamed or moved? Update task 067 test and re-baseline.");

        var field = type.GetField(AllFieldName,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"Field '{AllFieldName}' not found on '{GraphAppRolesTypeName}'. "
                + "Has the field been renamed? Update task 067 test and re-baseline.");

        var raw = field.GetValue(null)
            ?? throw new InvalidOperationException(
                $"Field '{GraphAppRolesTypeName}.{AllFieldName}' returned null.");

        var results = new List<ExpectedRole>();
        foreach (var entry in (System.Collections.IEnumerable)raw)
        {
            if (entry is null) continue;
            var entryType = entry.GetType();
            var value = ReadStringProperty(entryType, entry, "Value");
            var displayName = ReadStringProperty(entryType, entry, "DisplayName");
            var appRoleId = ReadNullableStringProperty(entryType, entry, "AppRoleId");
            results.Add(new ExpectedRole(value, displayName, appRoleId));
        }
        return results;
    }

    private static string GetGraphResourceAppIdReflectively()
    {
        var assembly = typeof(GraphAppRoles).Assembly;
        var type = assembly.GetType(GraphAppRolesTypeName, throwOnError: false)
            ?? throw new InvalidOperationException(
                $"Type '{GraphAppRolesTypeName}' not found in assembly '{assembly.FullName}'.");
        var field = type.GetField(GraphResourceAppIdFieldName,
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"Field '{GraphResourceAppIdFieldName}' not found on '{GraphAppRolesTypeName}'.");
        return (string?)field.GetValue(null)
            ?? throw new InvalidOperationException(
                $"Field '{GraphAppRolesTypeName}.{GraphResourceAppIdFieldName}' returned null.");
    }

    private static string ReadStringProperty(Type t, object instance, string propertyName)
    {
        var prop = t.GetProperty(propertyName)
            ?? throw new InvalidOperationException(
                $"Property '{propertyName}' not found on '{t.FullName}'. "
                + "Has the GraphAppRole record shape changed? Update task 067 test.");
        return (string?)prop.GetValue(instance)
            ?? throw new InvalidOperationException(
                $"Property '{t.FullName}.{propertyName}' returned null.");
    }

    private static string? ReadNullableStringProperty(Type t, object instance, string propertyName)
    {
        var prop = t.GetProperty(propertyName)
            ?? throw new InvalidOperationException(
                $"Property '{propertyName}' not found on '{t.FullName}'.");
        return (string?)prop.GetValue(instance);
    }

    private static string FormatParityDiff(
        string tenantId,
        string uamiSpObjectId,
        IReadOnlyList<ExpectedRole> expectedRoles,
        IReadOnlyList<ExpectedRole> missing,
        IReadOnlyList<string> extra)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            $"UAMI service principal '{uamiSpObjectId}' Graph app-role assignments diverge from "
            + "GraphAppRoles.cs (14-role canonical catalog).");
        sb.AppendLine($"Tenant: {tenantId}");
        sb.AppendLine(
            $"Expected: {expectedRoles.Count} roles from Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles.All"
            + $" (r1 task 005 populated all 14 GUIDs 2026-08-17).");
        sb.AppendLine();
        if (missing.Count > 0)
        {
            sb.AppendLine($"Missing roles ({missing.Count}) — must be GRANTED onto the UAMI SP:");
            foreach (var m in missing)
            {
                sb.AppendLine($"  - {m.DisplayName} ({m.AppRoleId})  [Value={m.Value}]");
            }
            sb.AppendLine();
        }
        if (extra.Count > 0)
        {
            sb.AppendLine(
                $"Extra roles ({extra.Count}) — assigned on the UAMI SP but NOT in "
                + "GraphAppRoles.cs (drift; investigate — someone added a role via Portal, "
                + "or GraphAppRoles.cs is missing a role that scripts have granted):");
            foreach (var e in extra) sb.AppendLine($"  - {e}");
            sb.AppendLine();
        }
        sb.AppendLine(
            "Remediation: run H10 handler for the target customer, OR manually replay via "
            + "`scripts/Grant-GraphAppRoles.ps1` (which reads GraphAppRoles.cs as authoritative). "
            + "For extras, verify GraphAppRoles.cs against the runbook (docs/guides/auth-deployment-setup.md §5).");
        return sb.ToString();
    }

    /// <summary>
    /// Test-local projection of the (Value, DisplayName, AppRoleId) triple —
    /// intentionally NOT taken as a compile-time reference to
    /// <c>Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles.GraphAppRole</c> so
    /// the reflective enumeration in <see cref="ReadExpectedRolesReflectively"/>
    /// remains the load-bearing shape probe. Same shape, distinct type.
    /// </summary>
    private sealed record ExpectedRole(string Value, string DisplayName, string? AppRoleId);
}
