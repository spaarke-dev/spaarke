// -----------------------------------------------------------------------------
// GraphAppRoleParityT3Probe.cs
//
// Task 178 (Phase C'' Wave G-7 Batch G-7A2.2 -- pipelined with H10 / task 143)
// -- REAL T3 probe replacing the single-outcome InfraFault branch in
// PlaceholderTrapVerifier for TrapKind.T3GraphAppRoleParity. Per DS-4 s6 T3
// row: "Graph /servicePrincipals/{id}/appRoleAssignments vs GraphAppRoles.cs
// (15 GUIDs must be present -- task 005 populated 14; task 144 added
// User.Invite.All)".
//
// STANDALONE CLASS (deliberately no shared interface dependency):
//   6 sibling Wave-G-7 trap tasks (171 T1, 177 T2, 178 T3 [this task], 180 T4,
//   172 T5, 175 T6) each author a per-probe class to disjoint files. Assembly
//   task 185 is the terminal owner that decides the composition surface --
//   whether a shared ITrapProbe seam or a monolithic swap of
//   PlaceholderTrapVerifier -- after ground-truthing what all 6 sibling probes
//   look like. Deliberately no `: ITrapProbe` here so task 185 has
//   architectural flexibility (parity with sibling probes' original standalone
//   posture -- see KeyVaultReferenceIdentityT1Probe.cs +
//   DataverseAppUserPairT2Probe.cs + T5SlotMiKvRbacTrapProbe.cs file headers).
//
// SIBLING-COORDINATION NOTE (parent dispatch 2026-08-20):
//   Parallel siblings simultaneously authoring per-probe classes for T4 (task
//   180) and I3-cost (task 183). This task writes to disjoint per-probe .cs
//   files to avoid guaranteed merge conflicts on the shared
//   PlaceholderTrapVerifier.cs / E2EAcceptanceModule.cs. The DI wire-up that
//   swaps this real probe into the aggregate verifier is DEFERRED to assembly
//   task 185. Zero shared-file coord touched by this task (per T1/T2/T5/T6
//   sibling precedent). This satisfies POML step 2's intent ("Register the
//   real verifier... for this SPECIFIC trap/invariant id only, other still-
//   placeholder ids are unaffected") -- task 185's aggregate composition IS
//   the mechanism that routes the T3 probe in place of the placeholder branch
//   without touching the other 5 branches. Documented explicitly here rather
//   than silently skipped.
//
// CATALOG SOURCE-OF-TRUTH (15 roles as of 2026-08-20):
//   IGraphAppRolesRegistry.GetAll() is the L2-local mirror of
//   Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles.All. Populated to 14
//   roles by task 005 (2026-08-17, live `az ad sp show` enumeration), then a
//   15th role (User.Invite.All) was added by task 144 (2026-08-20, H11
//   B2BGuest preset live verification). Task 143 corrected a WRONG GUID for
//   GroupMember.ReadWrite.All the same day. This probe uses the registry's
//   current .Count -- it does NOT hardcode "14" or "15" so the probe stays
//   correct as the catalog evolves (a compile-time reference, not a
//   memorized constant).
//
// PROBE CONTRACT:
//   T3 has ONE valid Passed shape -- the UAMI service principal in the
//   customer's tenant holds EVERY (Value, AppRoleId) pair in
//   IGraphAppRolesRegistry.GetAll() as an appRoleAssignment scoped to the
//   Microsoft Graph resource SP (00000003-...). Missing ANY role is Failed.
//
//   FAILED shapes -- the silent-fail traps this probe catches (per POML +
//   spec.md FR-33 T3 + design.md s4B T3 + parent-dispatch "task 143 lesson:
//   wrong-but-non-null GUID silently 400s Graph POST"):
//     * UAMI SP NOT REGISTERED -- customer tenant has NO servicePrincipal
//                                 with appId=UamiClientId. H2a's UAMI never
//                                 materialized as an SP in the customer
//                                 tenant, OR H10's grant loop was never
//                                 invoked. Downstream: every BFF app-only
//                                 Graph call 401/403s because there is no
//                                 principal to hold the roles.
//     * WRONG-BUT-NON-NULL GUID -- task 143's exact shape: a catalog entry
//                                 whose AppRoleId is a well-formed GUID but
//                                 doesn't correspond to a real Graph app-role
//                                 definition. H10's grant loop attempted a
//                                 POST with the wrong GUID, Graph returned
//                                 400/404, the grant never landed, and the
//                                 parity verifier reports the role as
//                                 missing. This probe's diagnostic explicitly
//                                 calls out the wrong-GUID diagnosis path
//                                 (per POML escalation trigger phrasing) so
//                                 the operator does NOT blindly retry the
//                                 grant loop against the same wrong catalog.
//     * MISSING GRANT           -- H10 was invoked but Graph transient failed
//                                 mid-loop; some roles landed, some did not.
//                                 Handler resume is safe (POSTs are
//                                 idempotent per H10 s12).
//
//   InfraFault -- probe could not verdict (H13 Resumable):
//     * Missing TenantId / UamiClientId (upstream H0.5 / H2a parameter guards
//       catch this earlier; defense-in-depth surfacing here as InfraFault
//       instead of throwing).
//     * NULL-AppRoleId escalation -- IGraphAppRolesRegistry has one or more
//       entries with null/whitespace AppRoleId. This mirrors H10's own
//       s8 escalation gate: since tasks 005/144 populated all 15 GUIDs, a
//       null here means the catalog was silently regressed. The probe REFUSES
//       to verdict because a null-AppRoleId entry cannot be verified as
//       "granted" (the verifier compares by AppRoleId). This is the "wrong-
//       catalog regression" belt-and-braces for task 067's nightly parity
//       ArchTest.
//     * Credential-factory throws / returns null -- injected factory failure.
//     * Graph token acquisition throws.
//     * Graph SP lookup HTTP non-2xx.
//     * IGraphAppRoleParityVerifier throws unexpectedly (its own contract is
//       to return Partial on internal failures, not throw -- a throw here
//       means the verifier itself is failing, not a T3 trigger).
//
// SEAM REUSE + PATTERN PARITY (per sibling T2 precedent):
//   Reuses IGraphAppRoleParityVerifier (H10's own T3 post-condition seam)
//   VERBATIM for the parity check itself -- one production impl
//   (GraphRestAppRoleParityVerifier) already registered globally in
//   Worker/Program.cs line 672. This mirrors task 177 (T2 probe) which
//   reused IDataverseAppUserVerifier verbatim for the systemusers?$filter
//   check.
//
//   The ONE additional step T3 needs -- resolving `UamiClientId` (App Id) ->
//   UAMI SP object id (Graph resource path segment) -- is done inline in
//   this probe because:
//     (a) TrapVerificationRequest exposes UamiClientId only, NOT the SP
//         object id. Adding UamiSpObjectId to TrapVerificationRequest is a
//         coordinating change with sibling T1/T2/T4/T5/T6 -- deliberately
//         out of scope per sibling-coordination convention (see T1 file
//         header "modifying that shared record is a coordinating change").
//     (b) H10's own handler side-steps the resolution because it already
//         has InterStepState.MiObjectId from H2a's Bicep output. H13's
//         TrapVerificationRequest was authored (task 055) before this
//         asymmetry was recognized.
//     (c) The lookup is one well-known Graph GET
//         (/servicePrincipals?$filter=appId eq '...') and is ~30 lines
//         inline -- authoring a new IUamiSpObjectIdResolver seam for a
//         single caller would be over-engineered.
//
// TESTABILITY (fake-transport pattern per parent-dispatch briefing):
//   The tenant-scoped credential factory is injected as
//   Func<string, TokenCredential> (parity with I5GraphTokenTenantScopeProbe,
//   task 179) so tests can supply a fake credential AND assert the factory
//   was called with the correct tenantId. Production wiring uses
//   tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions
//   { TenantId = tenantId }) -- the same lambda H10's GraphRestAppRoleGranter
//   already establishes (identical Azure.Identity idiom).
//
//   The HttpClient is injected as a plain HttpClient (test wiring passes
//   `new HttpClient(fakeHandler)`) so the probe's SP-resolution HTTP shape
//   is exercised end-to-end against canned bytes -- NEVER Mock<HttpMessageHandler>
//   per testing.md ADR-038 ban. The IGraphAppRoleParityVerifier dependency
//   is faked with a hand-rolled test double returning canned Verified /
//   Partial outcomes (parity with task 177's FakeVerifier for
//   IDataverseAppUserVerifier).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md s11):
//   Existing -- IE2ETrapVerifier / TrapVerificationOutcome / TrapKind
//     (task 055) + PlaceholderTrapVerifier's InfraFault-per-kind branch for
//     T3 + IGraphAppRoleParityVerifier + IGraphAppRolesRegistry (task 067)
//     + GraphRestAppRoleParityVerifier (production impl, registered
//     globally). This probe REUSES all of these verbatim -- no new shared
//     abstractions.
//   Extension -- ONE per-probe class covering ONE T3 branch; sibling probes
//     cover their own kinds; assembly task 185 composes.
//   Cost-of-doing-nothing -- H13's T3 branch returns InfraFault
//     UNCONDITIONALLY (Resumable) without this probe -- H13's acceptance
//     gate cannot go green for T3 (DS-4 s6 verbatim); the acceptance-target
//     transition (task 184/185/186) is blocked on EVERY probe individually
//     landing. Concrete downstream failure this probe catches: post-
//     provisioning, every BFF app-only Graph call for the customer 401/403s
//     until an operator manually diagnoses missing / wrong-GUID roles
//     (spec.md FR-33 T3 / design.md s4B T3). Task 143's discovery of a
//     wrong-but-non-null GUID in the catalog itself is the EXACT class of
//     silent-fail this probe's diagnostic guides the operator through.
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Real T3 (Graph app-role parity on the UAMI service principal) probe per
/// DS-4 s6. Produces exactly one <see cref="TrapVerificationOutcome"/> for
/// <see cref="TrapKind.T3GraphAppRoleParity"/>. Assembly task 185 composes
/// this + 5 sibling probes into the aggregate <see cref="IE2ETrapVerifier"/>
/// implementation that replaces <see cref="PlaceholderTrapVerifier"/>'s DI
/// registration.
/// </summary>
public sealed class GraphAppRoleParityT3Probe : ITrapProbe
{
    /// <summary>
    /// Graph default scope -- parity with the callers this probe reuses
    /// (<c>GraphRestAppRoleGranter.GraphScope</c>,
    /// <c>GraphRestAppRoleParityVerifier.GraphScope</c>,
    /// <c>I5GraphTokenTenantScopeProbe.GraphDefaultScope</c>).
    /// </summary>
    public const string GraphDefaultScope = "https://graph.microsoft.com/.default";

    /// <summary>Which s4B trap this probe verdicts. Exposed for task 185's aggregate composition to route by kind.</summary>
    public TrapKind Kind => TrapKind.T3GraphAppRoleParity;

    private readonly IGraphAppRoleParityVerifier _parityVerifier;
    private readonly IGraphAppRolesRegistry _rolesRegistry;
    private readonly HttpClient _httpClient;
    private readonly Func<string, TokenCredential> _tenantScopedCredentialFactory;
    private readonly ILogger<GraphAppRoleParityT3Probe> _logger;

    /// <summary>
    /// Production constructor. Wires the tenant-scoped credential factory to
    /// <c>tenantId =&gt; new DefaultAzureCredential(new DefaultAzureCredentialOptions
    /// { TenantId = tenantId })</c> -- the pattern task 130 established and
    /// I5GraphTokenTenantScopeProbe (task 179) mirrors.
    /// </summary>
    public GraphAppRoleParityT3Probe(
        IGraphAppRoleParityVerifier parityVerifier,
        IGraphAppRolesRegistry rolesRegistry,
        HttpClient httpClient,
        ILogger<GraphAppRoleParityT3Probe> logger)
        : this(parityVerifier, rolesRegistry, httpClient,
              tenantId => new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId }),
              logger)
    {
    }

    /// <summary>
    /// Testing constructor -- accepts an arbitrary credential factory so unit
    /// tests can inject a fake <see cref="TokenCredential"/> AND assert the
    /// factory was called with the correct tenantId.
    /// </summary>
    public GraphAppRoleParityT3Probe(
        IGraphAppRoleParityVerifier parityVerifier,
        IGraphAppRolesRegistry rolesRegistry,
        HttpClient httpClient,
        Func<string, TokenCredential> tenantScopedCredentialFactory,
        ILogger<GraphAppRoleParityT3Probe> logger)
    {
        ArgumentNullException.ThrowIfNull(parityVerifier);
        ArgumentNullException.ThrowIfNull(rolesRegistry);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(tenantScopedCredentialFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _parityVerifier = parityVerifier;
        _rolesRegistry = rolesRegistry;
        _httpClient = httpClient;
        _tenantScopedCredentialFactory = tenantScopedCredentialFactory;
        _logger = logger;
    }

    /// <summary>
    /// Runs the T3 probe. Never throws for probe-level fault modes -- returns
    /// <see cref="TrapVerificationOutcome.InfraFault"/> instead so H13 can
    /// classify Resumable per its s4C rollback table. Cancellation
    /// (<see cref="OperationCanceledException"/>) propagates.
    /// </summary>
    public async Task<TrapVerificationOutcome> ProbeAsync(
        TrapVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // (1) Input guards -- defense-in-depth over H13's own parameter
        //     guards. Missing input surfaces as InfraFault (Resumable) rather
        //     than throwing (parity with sibling T1 / T2 / T5 / T6 probes).
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                "T3 probe: request.TenantId is empty. s4D I1 forbids default-tenant fallback -- " +
                "upstream handler (H0.5 for Model 2, L2 endpoint for Model 1) MUST populate before H13. " +
                "The probe refuses to acquire a Graph token against an ambient tenant.");
        }
        if (string.IsNullOrWhiteSpace(request.UamiClientId))
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                "T3 probe: request.UamiClientId is empty. InterStepState.miClientId is populated " +
                "by H2a (uami.bicep) -- that upstream handler must complete before H13 can verify " +
                "the T3 app-role grant set.");
        }

        // (2) Escalation gate mirroring H10 s8 (spec.md MUST rule): a null /
        //     whitespace AppRoleId in the mirrored catalog means Phase A/task-144
        //     GUID completion has been silently regressed. The probe REFUSES to
        //     verdict because it cannot verify a null-AppRoleId entry as
        //     "granted" (comparison is by AppRoleId). Belt-and-braces for
        //     task 067's nightly parity ArchTest between BFF-side GraphAppRoles.cs
        //     and L2GraphAppRolesRegistry.cs.
        var expectedRoles = _rolesRegistry.GetAll();
        var nullGuidRoles = expectedRoles
            .Where(r => string.IsNullOrWhiteSpace(r.AppRoleId))
            .Select(r => r.Value)
            .ToList();
        if (nullGuidRoles.Count > 0)
        {
            var diagnostic =
                $"T3 probe cannot verdict -- IGraphAppRolesRegistry has {nullGuidRoles.Count} of " +
                $"{expectedRoles.Count} entries with null/whitespace AppRoleId " +
                $"({string.Join(", ", nullGuidRoles)}). Post-tasks-005/144, the catalog is fully " +
                "populated (15/15 GUIDs live-verified against tenant a221a95e-...) -- a null here " +
                "means the catalog was silently regressed. Cannot verdict: an entry without an " +
                "AppRoleId cannot be compared to the UAMI's granted appRoleAssignments. Operator: " +
                "restore the missing GUIDs from git history OR re-run live `az ad sp show " +
                "--id 00000003-0000-0000-c000-000000000000` enumeration to repopulate. Handler " +
                "classifies Resumable.";
            _logger.LogWarning("T3 probe InfraFault -- catalog regression: {Diagnostic}", diagnostic);
            return new TrapVerificationOutcome.InfraFault(Kind, diagnostic);
        }

        _logger.LogInformation(
            "T3 probe starting: customerId={CustomerId} runId={RunId} tenantId={TenantId} " +
            "uamiClientId={UamiClientId} expectedRoleCount={ExpectedRoleCount}",
            request.CustomerId, request.RunId, request.TenantId, request.UamiClientId, expectedRoles.Count);

        // (3) Build a tenant-scoped credential via the injected factory.
        //     Passing the requested tenantId is itself part of the I5 invariant
        //     (FR-32) H13's own composite invariant probe covers separately --
        //     this probe consumes the SAME idiom, not verifying it.
        TokenCredential? credential;
        try
        {
            credential = _tenantScopedCredentialFactory(request.TenantId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T3 verdict deferred: tenant-scoped credential factory threw for tenantId='{request.TenantId}': " +
                $"{ex.GetType().Name}: {ex.Message}. Cannot acquire Graph token for the UAMI SP lookup.");
        }
        if (credential is null)
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T3 verdict deferred: tenant-scoped credential factory returned null for " +
                $"tenantId='{request.TenantId}'. Cannot acquire Graph token for the UAMI SP lookup.");
        }

        // (4) Acquire a Graph token for the UAMI SP lookup step.
        AccessToken token;
        try
        {
            token = await credential
                .GetTokenAsync(new TokenRequestContext(new[] { GraphDefaultScope }), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "T3 probe InfraFault -- Graph token acquisition threw: customerId={CustomerId} tenantId={TenantId}",
                request.CustomerId, request.TenantId);
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T3 verdict deferred: Graph token acquisition threw for tenantId='{request.TenantId}': " +
                $"{ex.GetType().Name}: {ex.Message}. Verify the L2 UAMI's federated-credential mapping " +
                $"admits token issuance for tenant '{request.TenantId}'.");
        }
        if (string.IsNullOrWhiteSpace(token.Token))
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T3 verdict deferred: Graph token acquisition returned an empty token for " +
                $"tenantId='{request.TenantId}'. Cannot proceed to UAMI SP lookup.");
        }

        // (5) Resolve UAMI SP object id from client id (see file header
        //     "SEAM REUSE" for why this step is inline rather than delegated).
        string? uamiSpObjectId;
        try
        {
            uamiSpObjectId = await ResolveUamiServicePrincipalObjectIdAsync(
                token, request.UamiClientId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "T3 probe InfraFault -- UAMI SP lookup HTTP failed: customerId={CustomerId} " +
                "uamiClientId={UamiClientId}",
                request.CustomerId, request.UamiClientId);
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T3 verdict deferred: UAMI SP lookup HTTP call failed for uamiClientId=" +
                $"'{request.UamiClientId}' in tenant '{request.TenantId}': {ex.GetType().Name}: {ex.Message}. " +
                "Verify Graph reachability from the L2 host and that the acquired token carries the " +
                "Directory.Read.All (or equivalent) app-role required for /servicePrincipals reads.");
        }

        if (uamiSpObjectId is null)
        {
            // UAMI SP not found -- this IS a T3 trap. Distinguished from
            // "grants missing" so the operator diagnosis path is unambiguous:
            // either H2a's UAMI never registered as an SP in the customer
            // tenant (Model 2 cross-tenant provisioning failure), or a
            // parameter drift is passing a stale UamiClientId.
            var diagnostic =
                $"T3 silent-fail trap MANIFESTED (UAMI SP not registered): tenant '{request.TenantId}' " +
                $"has ZERO servicePrincipals with appId='{request.UamiClientId}'. " +
                $"H2a's UAMI has NOT materialized as an SP in the customer's Entra tenant, OR the " +
                $"UamiClientId parameter propagated to H13 is stale/incorrect. Downstream: every " +
                $"BFF app-only Graph call for this customer fails 401/403 (no principal exists to " +
                $"hold the {expectedRoles.Count} expected Graph app-roles). Operator diagnosis: " +
                $"live-verify via `az ad sp list --filter \"appId eq '{request.UamiClientId}'\" " +
                $"--query \"[].{{id:id,tenant:appOwnerOrganizationId}}\"` against the customer tenant. " +
                $"H10 cannot recover from this state on its own -- either the UAMI provisioning " +
                $"(H2a's uami.bicep) needs to be re-run, or the InterStepState.miClientId propagation " +
                $"needs debugging.";
            _logger.LogWarning("T3 probe FAILED (UAMI SP missing): {Diagnostic}", diagnostic);
            return new TrapVerificationOutcome.Failed(Kind, diagnostic);
        }

        // (6) Delegate to the H10 parity verifier for the actual
        //     appRoleAssignments read + catalog compare. Reuses the exact same
        //     seam H10's own T3 post-condition check uses (H10 s13) -- so if
        //     H10 reported success but H13 later verdicts Failed, it is
        //     GUARANTEED not a divergence between two different query
        //     implementations.
        GraphAppRoleParityResult parityResult;
        try
        {
            parityResult = await _parityVerifier
                .VerifyAsync(uamiSpObjectId, request.TenantId, expectedRoles, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "T3 probe InfraFault -- parity verifier threw unexpectedly: customerId={CustomerId} " +
                "uamiSpObjectId={UamiSpObjectId}",
                request.CustomerId, uamiSpObjectId);
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T3 verdict deferred: IGraphAppRoleParityVerifier.VerifyAsync threw " +
                $"{ex.GetType().Name}: {ex.Message}. The verifier's contract is to return Partial on " +
                "internal failures rather than throw -- a throw here means the verifier itself is " +
                "failing (network / auth / other) rather than a T3 trigger. Handler classifies Resumable.");
        }

        switch (parityResult)
        {
            case GraphAppRoleParityResult.Verified verified:
                _logger.LogInformation(
                    "T3 probe PASSED: {GrantedCount} of {ExpectedCount} Graph app-roles granted on " +
                    "UAMI SP objectId={UamiSpObjectId} in tenant={TenantId}",
                    verified.GrantedCount, expectedRoles.Count, uamiSpObjectId, request.TenantId);
                return new TrapVerificationOutcome.Passed(Kind);

            case GraphAppRoleParityResult.Partial partial:
                // Silent-fail-catch diagnostic: distinguishes the wrong-GUID
                // shape (task 143's exact lesson) from the plain missing-grant
                // shape without silently conflating them.
                var missingList = string.Join(", ", partial.MissingRoleValues);
                var diagnostic =
                    $"T3 silent-fail trap MANIFESTED on UAMI SP objectId='{uamiSpObjectId}' " +
                    $"(appId='{request.UamiClientId}') in tenant='{request.TenantId}': " +
                    $"{partial.MissingRoleValues.Count} of {partial.ExpectedCount} expected Graph " +
                    $"app-roles are NOT granted ({partial.GrantedCount} granted). Missing: {missingList}. " +
                    "Two silent-fail classes to distinguish before retrying:\n" +
                    " (a) WRONG-BUT-NON-NULL GUID (task 143's exact shape): a catalog entry's " +
                    "AppRoleId is a well-formed GUID that does NOT correspond to a real Graph " +
                    "app-role definition. H10's grant loop attempted POST /appRoleAssignments with " +
                    "the wrong GUID, Graph returned 400/404, the grant never landed. Diagnose via " +
                    "live re-enumeration: `az ad sp show --id 00000003-0000-0000-c000-000000000000 " +
                    "--query \"appRoles[?value=='<name>'].id\"` for each MISSING role; compare " +
                    "against L2GraphAppRolesRegistry.cs + Sprk.Bff.Api.Infrastructure.Auth." +
                    "GraphAppRoles.cs and reconcile any mismatch (edit BOTH mirrors in lockstep). " +
                    "Do NOT blindly retry the grant loop -- the wrong GUID will fail identically. " +
                    "task 067's nightly parity ArchTest is the ongoing forcing-function guard here.\n" +
                    " (b) PLAIN MISSING GRANT: Graph transient failure mid-loop; some grants landed, " +
                    "some did not. Re-invoking H10 is safe (POSTs are individually idempotent per " +
                    "H10 s12) and will land the still-missing grants. Confirm this class by " +
                    "verifying live `az ad sp show` GUIDs match the catalog BEFORE retrying.";
                _logger.LogWarning("T3 probe FAILED (parity mismatch): {Diagnostic}", diagnostic);
                return new TrapVerificationOutcome.Failed(Kind, diagnostic);

            default:
                // Defensive: GraphAppRoleParityResult is a discriminated union
                // whose subclasses are exhaustively Verified + Partial. A
                // future addition surfacing here means seam evolution the
                // probe hasn't caught up to -- InfraFault rather than crash.
                return new TrapVerificationOutcome.InfraFault(Kind,
                    $"T3 verdict deferred: IGraphAppRoleParityVerifier returned an unrecognized " +
                    $"result type '{parityResult.GetType().Name}'. Probe author must update the " +
                    "verdict switch to handle the new case.");
        }
    }

    /// <summary>
    /// Resolves a UAMI application (client) id to its service-principal object
    /// id in the target tenant via a Graph <c>/v1.0/servicePrincipals?$filter=
    /// appId eq '{clientId}'&amp;$select=id</c> query. Returns <c>null</c>
    /// when the tenant has ZERO matching SPs (a legitimate T3 trap -- see file
    /// header). Throws for HTTP non-success (caller catches + surfaces as
    /// InfraFault). Exposed <c>internal</c> so tests can validate the query
    /// shape directly against the fake HttpMessageHandler.
    /// </summary>
    internal async Task<string?> ResolveUamiServicePrincipalObjectIdAsync(
        AccessToken token, string uamiClientId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uamiClientId);

        var filter = Uri.EscapeDataString($"appId eq '{uamiClientId}'");
        var uri = new Uri(
            $"https://graph.microsoft.com/v1.0/servicePrincipals?$filter={filter}&$select=id");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);
        var text = await httpResponse.Content
            .ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GET {uri} failed: {(int)httpResponse.StatusCode} {httpResponse.StatusCode}. " +
                $"Body: {Truncate(text, 300)}");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        if (!doc.RootElement.TryGetProperty("value", out var values)
            || values.ValueKind != JsonValueKind.Array
            || values.GetArrayLength() == 0)
        {
            return null;
        }

        // Graph's servicePrincipals filter is exact on appId; multiple matches
        // are not expected but if returned we take the first (parity with H10's
        // GraphRestAppRoleGranter.ResolveGraphResourceSpIdAsync's behavior).
        if (!values[0].TryGetProperty("id", out var idElement))
        {
            return null;
        }
        var spId = idElement.GetString();
        return string.IsNullOrWhiteSpace(spId) ? null : spId;
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";
}
