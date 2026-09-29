using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/unsecure-project
///
/// Removes a project's Secure Project designation — the reverse of
/// <see cref="ProvisionProjectEndpoint"/>. design.md §5.1 calls the designation reversible, and spec
/// FR-28 counts the reverse path as part of the mechanism rather than a nice-to-have.
///
/// Sequence:
///   1. Read the project; a project that is not secure returns 200 having changed nothing (idempotent)
///   2. Resolve the new owner — request, else configuration, else the calling user
///   3. Assign ownership to that user, and verify by read-back
///   4. Revoke every POA share on the record
///   5. Clear <c>sprk_issecure</c>
///
/// ADR-001: Minimal API. ADR-003: fail closed — a step that cannot be verified is treated as failed.
/// ADR-008: authorization is the route group's delegation filter (FR-07 Write-on-record, as the caller).
/// ADR-010: concrete DI injections.
/// </summary>
/// <remarks>
/// <para><b>Ordering is the security-relevant part.</b> Ownership moves BEFORE the shares are revoked.
/// Reversed, there would be a window in which the record still sat on the memberless Secure Project
/// owner team with its shares already gone — reachable by nobody, which is the exact failure this
/// project exists to remove. Ownership first means someone can always see the record.</para>
///
/// <para><b>The flag is cleared last, and on purpose.</b> <c>sprk_issecure</c> is a label that other
/// surfaces read (the container resolver, the evaluator veto). Clearing it while the record was still
/// team-owned and share-gated would advertise "this is a normal project" about a record that still
/// behaved like a secure one.</para>
///
/// <para><b>Every share is revoked, not only the ones provisioning issued.</b> A secure project's
/// access came entirely from explicit shares. Once ownership and business-unit access apply normally,
/// a leftover POA row is a second access path that no longer appears in any UI that reasons about
/// secure projects — invisible access is worse than no access.</para>
/// </remarks>
public static class UnsecureProjectEndpoint
{
    private const string ProjectEntitySet = "sprk_projects";
    private const string ProjectEntityLogicalName = "sprk_project";
    private const string SystemUserEntitySet = "systemusers";

    /// <summary>
    /// Optional configuration naming the <c>systemuser</c> that un-secured projects land on.
    /// </summary>
    /// <remarks>
    /// Optional by design. Without it the record goes to the caller, who has already proven Write on
    /// it — a deterministic, auditable owner that needs no environment setup. An environment that
    /// wants un-secured matters to land on a fixed steward sets this instead.
    /// </remarks>
    internal const string UnsecureOwnerUserIdConfigKey = "SecureProject:UnsecureOwnerUserId";

    private const string ReasonKey = "reasonCode";

    internal const string ReasonProjectNotFound = "sdap.unsecure.project_not_found";
    internal const string ReasonOwnerUnresolved = "sdap.unsecure.owner_unresolved";
    internal const string ReasonOwnerAssignmentFailed = "sdap.unsecure.owner_assignment_failed";
    internal const string ReasonOwnerAssignmentNotApplied = "sdap.unsecure.owner_assignment_not_applied";
    internal const string ReasonFlagNotCleared = "sdap.unsecure.flag_not_cleared";

    public static RouteGroupBuilder MapUnsecureProjectEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/unsecure-project", UnsecureProjectAsync)
            .WithName("UnsecureProject")
            .WithSummary("Remove a project's Secure Project designation")
            .WithDescription(
                "Reassigns ownership off the Secure Project owner team, revokes the record's explicit " +
                "shares and clears sprk_issecure. Idempotent: a project that is already not secure " +
                "returns 200 having changed nothing.")
            .Produces<UnsecureProjectResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    private static async Task<IResult> UnsecureProjectAsync(
        UnsecureProjectRequest request,
        DataverseWebApiClient dataverseClient,
        IDataverseRecordShareService recordShare,
        CallerRecordAccessProbe callerAccessProbe,
        IConfiguration configuration,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (request.ProjectId == Guid.Empty)
            return Problem(StatusCodes.Status400BadRequest, "Bad Request",
                "ProjectId is required and must be a valid GUID.", httpContext.TraceIdentifier);

        var traceId = httpContext.TraceIdentifier;

        // ── Step 1: Read the project ─────────────────────────────────────────
        ProjectSecurityRow? project;
        try
        {
            var rows = await dataverseClient.QueryAsync<ProjectSecurityRow>(
                ProjectEntitySet,
                filter: $"sprk_projectid eq {request.ProjectId}",
                select: "sprk_projectid,sprk_issecure",
                top: 1,
                cancellationToken: ct);

            project = rows.FirstOrDefault();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[UNSECURE] Could not read project {ProjectId}. TraceId={TraceId}", request.ProjectId, traceId);

            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                "The project could not be read from Dataverse.", traceId,
                (ReasonKey, ReasonProjectNotFound));
        }

        if (project is null)
            return Problem(StatusCodes.Status404NotFound, "Not Found",
                $"Project {request.ProjectId} was not found.", traceId, (ReasonKey, ReasonProjectNotFound));

        // Idempotency: nothing to undo. Returning 200 rather than 409 because the caller's intent
        // ("this project should not be secure") is already satisfied — a repeat is not a conflict.
        if (project.sprk_issecure != true)
        {
            logger.LogInformation(
                "[UNSECURE] Project {ProjectId} is already not secure; nothing to do. TraceId={TraceId}",
                request.ProjectId, traceId);

            return TypedResults.Ok(new UnsecureProjectResponse(
                ProjectId: request.ProjectId,
                NewOwnerSystemUserId: Guid.Empty,
                SharesRevoked: 0,
                AlreadyUnsecure: true,
                // NOT true. No sweep runs on this path, so this response cannot vouch that the record
                // carries no shares — and claiming it could would reinstate ISS-018 precisely here: an
                // operator who reads sweepComplete=false retries, the flag is now clear, and they would
                // be told "complete sweep of zero" while the surviving rows are still in place.
                SweepComplete: null));
        }

        // ── Step 2: Resolve the new owner ────────────────────────────────────
        var newOwnerId = await ResolveNewOwnerAsync(
            request, configuration, callerAccessProbe, httpContext, ct);

        if (newOwnerId is null || newOwnerId == Guid.Empty)
        {
            logger.LogError(
                "[UNSECURE] No owner could be resolved for project {ProjectId} — the request named " +
                "none, '{ConfigKey}' is unset, and the caller's systemuserid could not be established. " +
                "Refusing: handing the record to nobody would strand it exactly as it is. TraceId={TraceId}",
                request.ProjectId, UnsecureOwnerUserIdConfigKey, traceId);

            return Problem(StatusCodes.Status403Forbidden, "Forbidden",
                "No owner could be determined for the un-secured project. Name one in the request, or " +
                $"configure '{UnsecureOwnerUserIdConfigKey}'.",
                traceId, (ReasonKey, ReasonOwnerUnresolved));
        }

        // ── Step 3: Assign ownership, and verify it applied ──────────────────
        try
        {
            await dataverseClient.UpdateAsync(
                ProjectEntitySet,
                request.ProjectId,
                new Dictionary<string, object?>
                {
                    ["ownerid@odata.bind"] = $"/{SystemUserEntitySet}({newOwnerId})"
                },
                ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[UNSECURE] Dataverse refused the ownership assignment of project {ProjectId} to user " +
                "{OwnerId}. TraceId={TraceId}", request.ProjectId, newOwnerId, traceId);

            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                "Ownership could not be reassigned, so the secure designation was left in place.",
                traceId, (ReasonKey, ReasonOwnerAssignmentFailed));
        }

        // Read-back, for the same reason provisioning does it: an accepted PATCH is not proof that an
        // owner navigation property was applied rather than ignored.
        try
        {
            var rows = await dataverseClient.QueryAsync<ProjectSecurityRow>(
                ProjectEntitySet,
                filter: $"sprk_projectid eq {request.ProjectId}",
                select: "sprk_projectid,_owninguser_value",
                top: 1,
                cancellationToken: ct);

            var reread = rows.FirstOrDefault();
            if (reread?._owninguser_value != newOwnerId)
            {
                logger.LogError(
                    "[UNSECURE] Ownership read-back FAILED for project {ProjectId}: expected owning user " +
                    "{OwnerId}, found {ActualOwnerId}. Leaving the project secure. TraceId={TraceId}",
                    request.ProjectId, newOwnerId, reread?._owninguser_value, traceId);

                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    "The ownership reassignment was accepted but did not take effect, so the secure " +
                    "designation was left in place.",
                    traceId, (ReasonKey, ReasonOwnerAssignmentNotApplied));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[UNSECURE] Could not verify the ownership reassignment of project {ProjectId}. " +
                "Treating it as failed. TraceId={TraceId}", request.ProjectId, traceId);

            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                "The ownership reassignment could not be verified, so the secure designation was left " +
                "in place.", traceId, (ReasonKey, ReasonOwnerAssignmentNotApplied));
        }

        // ── Step 4: Revoke the explicit shares ───────────────────────────────
        var sweep = await RevokeAllSharesAsync(
            recordShare, request.ProjectId, logger, traceId, ct);

        // ── Step 5: Clear the flag ───────────────────────────────────────────
        try
        {
            await dataverseClient.UpdateAsync(
                ProjectEntitySet,
                request.ProjectId,
                new Dictionary<string, object?> { ["sprk_issecure"] = false },
                ct);
        }
        catch (Exception ex)
        {
            // Ownership already moved and the sweep has run, so the record IS reachable and no longer
            // isolated — but it still advertises itself as secure, and the sweep may not have removed
            // everything (see sweepComplete). Report loudly: this is a half-applied state an operator
            // must finish, and reporting 200 would hide it.
            logger.LogError(ex,
                "[UNSECURE] Project {ProjectId} was reassigned to {OwnerId} and had {Count} share(s) " +
                "revoked (sweepComplete={SweepComplete}), but sprk_issecure could NOT be cleared. The " +
                "project is no longer isolated yet still reads as secure. TraceId={TraceId}",
                request.ProjectId, newOwnerId, sweep.Revoked, sweep.Complete, traceId);

            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                sweep.Complete
                    ? "Ownership was reassigned and every share revoked, but the secure flag could " +
                      "not be cleared. The project is no longer isolated but still reads as secure — " +
                      "clear sprk_issecure manually, or retry."
                    // Prose and machine-readable extension must not disagree: claiming "shares revoked"
                    // here while sweepComplete=false would hand the human reader the pre-fix claim.
                    : "Ownership was reassigned, but the share sweep could NOT account for every share " +
                      "AND the secure flag could not be cleared. The project is no longer isolated, " +
                      "still reads as secure, and may retain shares — clear sprk_issecure manually and " +
                      "check the record's remaining shares.",
                traceId,
                (ReasonKey, ReasonFlagNotCleared),
                ("newOwnerSystemUserId", newOwnerId),
                ("sharesRevoked", sweep.Revoked),
                ("sweepComplete", sweep.Complete));
        }

        logger.LogInformation(
            "[UNSECURE] Project {ProjectId} un-secured: owner={OwnerId}, sharesRevoked={Count}, " +
            "sweepComplete={SweepComplete}. TraceId={TraceId}",
            request.ProjectId, newOwnerId, sweep.Revoked, sweep.Complete, traceId);

        return TypedResults.Ok(new UnsecureProjectResponse(
            ProjectId: request.ProjectId,
            NewOwnerSystemUserId: newOwnerId.Value,
            SharesRevoked: sweep.Revoked,
            AlreadyUnsecure: false,
            SweepComplete: sweep.Complete));
    }

    /// <summary>
    /// The owner an un-secured project lands on: the request's nomination, else configuration, else
    /// the calling user.
    /// </summary>
    private static async Task<Guid?> ResolveNewOwnerAsync(
        UnsecureProjectRequest request,
        IConfiguration configuration,
        CallerRecordAccessProbe callerAccessProbe,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (request.ReassignToSystemUserId is { } requested && requested != Guid.Empty)
            return requested;

        if (Guid.TryParse(configuration[UnsecureOwnerUserIdConfigKey], out var configured)
            && configured != Guid.Empty)
        {
            return configured;
        }

        var callerToken = TokenHelper.ExtractBearerTokenOrNull(httpContext);

        return await callerAccessProbe.GetCallerSystemUserIdAsync(callerToken, ct);
    }

    /// <summary>
    /// The outcome of the share sweep: how many rows were revoked, and whether the enumeration that
    /// drove it accounted for every share on the record.
    /// </summary>
    /// <remarks>
    /// A bare count cannot express "I removed none because there were none" separately from "I removed
    /// none because I could not see them" — which is the whole of ISS-018. The two facts travel
    /// together or the count is not evidence of anything.
    /// </remarks>
    private readonly record struct ShareSweep(int Revoked, bool Complete);

    /// <summary>
    /// Removes every POA share on the project, reporting how many were removed AND whether that is
    /// the complete set.
    /// </summary>
    /// <remarks>
    /// <para>Best-effort per share and never fatal. Ownership has already moved by the time this runs,
    /// so the record is reachable regardless; a share that survives is a stale access path to report,
    /// not a reason to abandon a reassignment that already succeeded. Each failure is logged with its
    /// principal so an operator can finish the job.</para>
    ///
    /// <para><b>The enumeration is STRICT, with a soft fallback</b> (ISS-018 / #995, task 108). It was
    /// <see cref="IDataverseRecordShareService.GetPrincipalAccessAsync"/>, which answers an EMPTY LIST
    /// when the read fails — so a failed read was indistinguishable from "no shares", nothing was
    /// revoked, and the endpoint answered success with <c>sharesRevoked = 0</c>. The <c>catch</c> below
    /// could not fire for that case either: the soft read swallows a non-success status and an
    /// unreadable object type code internally, and only a transport-level throw ever reached it. An
    /// unsecure could therefore leave every share in place, silently.</para>
    ///
    /// <para><b>Why partial progress, rather than refusing.</b> The strict read refuses an incomplete
    /// answer — more than one page of shares, or a row whose principal or mask will not parse. For a
    /// "remove every share" sweep that is not a reason to stop: ownership has already moved and been
    /// read back, so refusing outright would leave ALL shares in place, while sweeping what can be
    /// enumerated removes some stale access. So the strict read decides COMPLETENESS and the soft read
    /// supplies whatever rows it can. What is never allowed is reporting the shortfall as success.</para>
    ///
    /// <para><b>The secure flag is still cleared on an incomplete sweep, deliberately.</b> Per ADR-003
    /// <c>sprk_issecure</c> suppresses the derived-member and org-expansion terms; it does NOT suppress
    /// explicit grants or Dataverse's own answer. A surviving POA row IS Dataverse's answer, so leaving
    /// the flag set buys no protection <i>against the surviving share</i>, while manufacturing the
    /// half-applied "no longer isolated yet still reads as secure" state this endpoint already treats as
    /// a defect. The honest report is a cleared flag plus <c>sweepComplete = false</c>.</para>
    ///
    /// <para><b>Clearing the flag is not free, though</b> — it is simply not a mitigation for THIS risk.
    /// <c>sprk_issecure</c> also drives container placement (<c>SecureContainerDecision</c>): a secure
    /// record gets its own container, a non-secure one may fall back to the owning business unit's
    /// SHARED container, and SPE permissions are additive-only, so that is not retractable later. That
    /// consequence is intended for a completed unsecure and is accepted here for an incomplete one,
    /// because the alternative — a record that is owner-reassigned but still flagged secure — is the
    /// half-applied state above.</para>
    /// </remarks>
    private static async Task<ShareSweep> RevokeAllSharesAsync(
        IDataverseRecordShareService recordShare,
        Guid projectId,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        var (shares, complete) = await EnumerateSharesAsync(recordShare, projectId, logger, traceId, ct);

        var revoked = 0;
        foreach (var share in shares)
        {
            try
            {
                await recordShare.RevokeAccessAsync(ProjectEntitySet, projectId, share.Principal, ct);
                revoked++;
            }
            catch (Exception ex)
            {
                // Unchanged: best-effort per share, logged with its principal, never fatal. What is new
                // is that a share we failed to remove stops the sweep claiming completeness — the
                // record is demonstrably not fully revoked.
                complete = false;

                logger.LogWarning(ex,
                    "[UNSECURE] Could not revoke the {Kind} share for {PrincipalId} on project " +
                    "{ProjectId}; the sweep is reported as INCOMPLETE. TraceId={TraceId}",
                    share.Principal.Kind, share.Principal.Id, projectId, traceId);
            }
        }

        return new ShareSweep(revoked, complete);
    }

    /// <summary>
    /// The record's shares, and whether that list is known to be complete: the strict read's answer,
    /// else whatever the soft read can still parse, else nothing.
    /// </summary>
    /// <remarks>
    /// Split out of <see cref="RevokeAllSharesAsync"/> so the sweep reads as enumerate → revoke → report.
    /// <para><b>No cancellation filter on these catches, deliberately.</b> A draft guarded them with
    /// <c>when (!ct.IsCancellationRequested)</c>; review showed that tests the TOKEN's state rather than
    /// the exception's identity, so a genuine read failure that merely coincided with a client
    /// disconnect would be swallowed with no fallback and no warning. It also removed a diagnostic: a
    /// cancelled read previously fell through to the flag-clear, which threw on the dead token and
    /// produced the "half-applied state" error the operator needs. Both are the opposite of this task's
    /// goal, so the catches stay unfiltered.</para>
    /// </remarks>
    private static async Task<(IReadOnlyList<DataversePrincipalAccess> Shares, bool Complete)> EnumerateSharesAsync(
        IDataverseRecordShareService recordShare,
        Guid projectId,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        try
        {
            var strict = await recordShare.GetPrincipalAccessOrThrowAsync(
                ProjectEntityLogicalName, projectId, ct);

            return (strict, true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[UNSECURE] The shares on project {ProjectId} could not be read COMPLETELY. Sweeping " +
                "only the rows that can be enumerated and reporting the sweep as INCOMPLETE — a " +
                "surviving share is a stale access path an operator must remove. TraceId={TraceId}",
                projectId, traceId);
        }

        try
        {
            var soft = await recordShare.GetPrincipalAccessAsync(ProjectEntityLogicalName, projectId, ct);

            return (soft, false);
        }
        catch (Exception fallbackEx)
        {
            logger.LogWarning(fallbackEx,
                "[UNSECURE] No shares on project {ProjectId} could be enumerated at all; NONE were " +
                "revoked. This is not a clean sweep. TraceId={TraceId}", projectId, traceId);

            return (Array.Empty<DataversePrincipalAccess>(), false);
        }
    }

    private static IResult Problem(
        int statusCode,
        string title,
        string detail,
        string traceId,
        params (string Key, object? Value)[] extensions)
    {
        var members = new Dictionary<string, object?> { ["traceId"] = traceId };
        foreach (var (key, value) in extensions)
            members[key] = value;

        return Results.Problem(statusCode: statusCode, title: title, detail: detail, extensions: members);
    }

    /// <summary>The columns this endpoint reads from <c>sprk_project</c>.</summary>
    private sealed class ProjectSecurityRow
    {
        [JsonPropertyName("sprk_projectid")]
        public Guid? sprk_projectid { get; set; }

        [JsonPropertyName("sprk_issecure")]
        public bool? sprk_issecure { get; set; }

        [JsonPropertyName("_owninguser_value")]
        public Guid? _owninguser_value { get; set; }
    }
}
