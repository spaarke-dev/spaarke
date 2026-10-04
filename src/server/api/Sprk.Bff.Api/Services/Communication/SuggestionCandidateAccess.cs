using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// The Association Engine's suggestion preview, scoped to what the CALLER may read (unified-access-control-r2
/// task 161). Shared by <c>POST /api/communications/{id}/suggest-associations</c> and
/// <c>GET /api/office/communications/by-message-id/{internetMessageId}/suggestions</c>, so the two previews of the
/// same engine cannot disagree about what a caller is shown.
/// </summary>
/// <remarks>
/// <para><b>MOVED, not copied</b>, from <c>OfficeCommunicationsEndpoints.ResolveCandidateNamesAsync</c> (task 127,
/// #1020), which read each candidate's display name through the DELEGATED client and dropped the NAME of a record
/// the caller could not read — but still returned that record's id, entity, confidence and provenance in the
/// payload. Task 127's criterion ("the suggestions response contains no candidate the caller cannot read — neither
/// name nor id") was therefore not met by the code. Here an unreadable candidate is removed from the decision
/// itself.</para>
/// <para><b>Trimmed before the ladder, not after.</b> The candidates are admitted through
/// <see cref="IncomingAssociationResolver.EvaluateAsync(NormalizedMessage, AssociationContext, Func{IReadOnlyList{EntityReference}, CancellationToken, Task{IReadOnlyCollection{EntityReference}}}, CancellationToken)"/>,
/// so <c>Status</c>, <c>AutoFileEligible</c> and each candidate's <c>Conflict</c> / <c>Written</c> flag are computed
/// over the readable set only — an "Ambiguous" status cannot reveal a hidden second candidate. The engine's
/// evaluate path is not forked (ADR-045): the same rungs and ladder run; only the target set the final decision
/// sees is restricted.</para>
/// <para><b>Reads.</b> Each distinct candidate is read once, AS THE CALLER, exactly as task 127 did:
/// <c>{entitySet}({id})?$select={primaryName}</c> through <see cref="IDataverseUserClient"/> (OBO, no app-only
/// fallback). A candidate whose read does not succeed, or whose type has no entity set in
/// <see cref="RegardingNameFields"/>, is removed. A missing caller context (no bearer token / no OBO) fails the
/// whole request — never an empty, plausible-looking list.</para>
/// <para><b>No id survives elsewhere.</b> A structural signal or a contributor whose provenance text names a
/// removed candidate's id is removed with it, so a string search of the serialized response finds no removed id.</para>
/// <para><b>§11.</b> Existing: <c>ResolveCandidateNamesAsync</c> (private to the Office endpoints file). Extension:
/// this IS that method, moved so two endpoint files can share it — leaving it private would force a second copy in
/// <c>CommunicationEndpoints.cs</c>. Cost of doing nothing: the suggest route returns, and the Office route keeps
/// returning, the engine's suggested matters for records the caller cannot open.</para>
/// </remarks>
internal static class SuggestionCandidateAccess
{
    /// <summary>The caller-scoped preview: the trimmed engine projection plus the display names, keyed by candidate id.</summary>
    internal sealed record CallerScopedSuggestions(
        SuggestAssociationsResponse Suggestions,
        IReadOnlyDictionary<string, string> Names);

    /// <summary>
    /// Evaluates the engine for <paramref name="message"/> restricted to the regarding records the caller can read,
    /// and returns the projection with their display names.
    /// </summary>
    /// <exception cref="SdapProblemException">403 when the caller has no user context for the delegated reads.</exception>
    internal static async Task<CallerScopedSuggestions> EvaluateForCallerAsync(
        Guid communicationId,
        NormalizedMessage message,
        AssociationContext context,
        IncomingAssociationResolver associationResolver,
        IDataverseUserClient userClient,
        ILogger logger,
        CancellationToken ct)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var removedIds = new HashSet<Guid>();

        var decision = await associationResolver.EvaluateAsync(
            message,
            context,
            async (proposed, innerCt) =>
            {
                var admitted = new List<EntityReference>(proposed.Count);
                foreach (var target in proposed)
                {
                    var (readable, name) = await ReadAsCallerAsync(userClient, target, logger, innerCt);
                    if (!readable)
                    {
                        removedIds.Add(target.Id);
                        continue;
                    }

                    admitted.Add(target);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names[target.Id.ToString("D")] = name;
                    }
                }

                return admitted;
            },
            ct);

        var projection = SuggestAssociationsResponse.FromDecision(communicationId, decision);
        return new CallerScopedSuggestions(Scrub(projection, removedIds), names);
    }

    /// <summary>
    /// Reads one candidate AS THE CALLER. <c>(true, name)</c> when the read succeeds (the name may be blank);
    /// <c>(false, null)</c> when the type has no entity set, the id is empty, or Dataverse refuses or cannot find the
    /// row. Throws when there is no caller context at all.
    /// </summary>
    private static async Task<(bool Readable, string? Name)> ReadAsCallerAsync(
        IDataverseUserClient userClient, EntityReference target, ILogger logger, CancellationToken ct)
    {
        var logicalName = (target.LogicalName ?? string.Empty).Trim().ToLowerInvariant();
        var entitySet = RegardingNameFields.EntitySetName(logicalName);
        var nameField = RegardingNameFields.PrimaryNameField(logicalName);
        if (entitySet is null || nameField is null || target.Id == Guid.Empty)
        {
            logger.LogDebug("Suggestion candidate type {Entity} has no live-verified entity set; removing it", logicalName);
            return (false, null);
        }

        var response = await userClient.GetAsync(
            $"{entitySet}({target.Id})?$select={Uri.EscapeDataString(nameField)}", ct);

        if (response.IsSuccess)
        {
            return (true, response.Body is { } row ? ReadString(row, nameField) : null);
        }

        if (response.ErrorCode is DataverseUserClientErrorCodes.UserContextRequired
            or DataverseUserClientErrorCodes.OboNotConfigured
            or DataverseUserClientErrorCodes.OboExchangeFailed)
        {
            // Fail closed: without the caller's own context nothing can be shown — never an empty list that reads
            // as "the engine found nothing".
            throw new SdapProblemException(
                code: "SUGGESTIONS_CALLER_CONTEXT_REQUIRED",
                title: "Forbidden",
                detail: "The suggestions cannot be evaluated without the caller's own Dataverse context.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        // 403/404 (and any other refusal) is the control working: the caller may not read this candidate.
        logger.LogDebug(
            "Suggestion candidate {Entity} {Id} not readable by caller (HTTP {StatusCode}); removing it",
            logicalName, target.Id, response.StatusCode);
        return (false, null);
    }

    /// <summary>Removes every signal and contributor whose text names a removed candidate's id.</summary>
    private static SuggestAssociationsResponse Scrub(SuggestAssociationsResponse response, IReadOnlySet<Guid> removedIds)
    {
        if (removedIds.Count == 0)
        {
            return response;
        }

        var needles = removedIds
            .SelectMany(id => new[] { id.ToString("D"), id.ToString("N") })
            .ToArray();

        bool Mentions(string? text) =>
            !string.IsNullOrEmpty(text) && needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));

        return response with
        {
            Candidates = response.Candidates
                .Where(c => !Guid.TryParse(c.TargetId, out var id) || !removedIds.Contains(id))
                .Select(c => c with
                {
                    Contributors = c.Contributors.Where(k => !Mentions(k.Provenance)).ToArray(),
                })
                .ToArray(),
            Signals = response.Signals
                .Where(s => !Mentions(s.Provenance) && !Mentions(s.Category) && !s.Obligations.Any(Mentions))
                .ToArray(),
        };
    }

    private static string? ReadString(JsonElement row, string property)
        => row.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
