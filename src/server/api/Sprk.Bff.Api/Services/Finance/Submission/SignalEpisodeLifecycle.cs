// ebilling-r1 task 101 part B2a (seam plumbing) - the option-a Signal lifecycle, extracted from InvoiceSubmissionAfterSteps (task 049 part D) so
// the submission's after-steps and the corrections re-evaluation (B2b) write a Signal through ONE implementation.
//
// Component Justification (CLAUDE.md section 11):
//   (1) Existing - InvoiceSubmissionAfterSteps held the lifecycle (WriteWithLifecycleAsync, LatestEpisodeAsync) as private members; SignalWriter writes
//       and closes one Signal and is used unchanged.
//   (2) Extension - this IS the existing code moved, not new surface: a second copy in the corrections service would let two lifecycles drift.
//       It takes the writer calls as functions (the shape the after-steps already use), so it needs no DI registration.
//   (3) Cost of doing nothing - B2b would have to copy the lifecycle or call into a private member of a 1,500-line step runner.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>One Signal's step: written, a retryable <paramref name="FailureCode"/>, or a permanent <paramref name="RefusedCode"/> (F1-3).</summary>
internal readonly record struct SignalStep(bool Wrote, string? FailureCode, string? RefusedCode = null);

/// <summary>What the lifecycle does when the latest episode of a (policy, subject) is already resolved.</summary>
internal enum ResolvedEpisodeRule
{
    /// <summary>Task 049: a resolved Signal with the SAME reason-set fingerprint is skipped (a resume never re-raises what was resolved); a different one opens episode n+1.</summary>
    SkipWhenSameFingerprint = 0,

    /// <summary>Task 101 B2b: a corrected value that raises the finding again reopens it - episode n+1 whatever the fingerprint.</summary>
    ReopenAsNextEpisode = 1,
}

/// <summary>
/// The option-a lifecycle (task 049 notes section 6) for one (policy, subject): the latest episode by the dedupe-key prefix, its status and its
/// stored reason-set fingerprint decide: none → episode 1; open + same → refresh it; open + different → Superseded, then episode n+1;
/// resolved → by <see cref="ResolvedEpisodeRule"/>. A skipped write (<see cref="SignalWriteResult.SkippedRefusalCode"/>) is permanent when its
/// code is in <see cref="PermanentSkipCodes"/> (F1-3: recorded, the run continues), otherwise a failure; an open Signal that changed while being
/// superseded is a failure: retry.
/// </summary>
internal sealed class SignalEpisodeLifecycle
{
    internal const string SignalWriteSkippedCode = "ebilling.submission.signal_write_skipped";
    internal const string SignalChangedCode = "ebilling.submission.signal_changed";

    private const string SignalEntity = "sprk_signal";

    private readonly IGenericEntityService _entities;
    private readonly Func<SignalWriteRequest, CancellationToken, Task<SignalWriteResult>> _writeSignal;
    private readonly Func<Guid, string, string, CancellationToken, Task<SignalCloseOutcome>> _supersedeSignal;
    private readonly ILogger _logger;

    public SignalEpisodeLifecycle(
        IGenericEntityService entities,
        Func<SignalWriteRequest, CancellationToken, Task<SignalWriteResult>> writeSignal,
        Func<Guid, string, string, CancellationToken, Task<SignalCloseOutcome>> supersedeSignal,
        ILogger logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _writeSignal = writeSignal ?? throw new ArgumentNullException(nameof(writeSignal));
        _supersedeSignal = supersedeSignal ?? throw new ArgumentNullException(nameof(supersedeSignal));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The <see cref="SignalWriteResult.SkippedRefusalCode"/> values a redelivery cannot change (F1-3): the reconcile met a Signal whose core
    /// record or no-core owner changed (<see cref="OntologyWriterFailureReason.CoreRecordChanged"/>, re-grouping is task 031's), and the
    /// ownership resolver's refusals of the data or configuration it read (<see cref="RecordOwnerRefusal"/>). Not here, so retried:
    /// <see cref="RecordOwnerRefusal.ParentUndetermined"/> (a derivation that FAILED, which a retry can pass) and any code not listed.
    /// </summary>
    internal static readonly IReadOnlySet<string> PermanentSkipCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        OntologyWriterFailureReason.CoreRecordChanged,
        RecordOwnerRefusal.ParentUnresolved,
        RecordOwnerRefusal.SecureParentNotIsolated,
        RecordOwnerRefusal.SecureOwnerTeamUnresolved,
        RecordOwnerRefusal.SecureBusinessUnitAmbiguous,
        RecordOwnerRefusal.NoDefaultOwnerTeam,
        RecordOwnerRefusal.ActingUserUnresolved,
        RecordOwnerRefusal.NoOwnerSource,
        RecordOwnerRefusal.RecordMissing,
    };

    /// <summary>
    /// Writes <paramref name="request"/> through the lifecycle. <paramref name="contextLabel"/> and <paramref name="contextId"/> name the caller
    /// (for example <c>"Invoice submission"</c> and the submission id) in the log lines only.
    /// </summary>
    internal async Task<SignalStep> WriteAsync(
        SignalWriteRequest request,
        string fingerprint,
        string contextLabel,
        Guid contextId,
        ResolvedEpisodeRule resolvedRule,
        CancellationToken ct)
    {
        var latest = await LatestEpisodeAsync(request, ct).ConfigureAwait(false);
        var episode = 1;
        if (latest is { } existing)
        {
            var same = string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal);
            if (existing.IsResolved)
            {
                if (same && resolvedRule == ResolvedEpisodeRule.SkipWhenSameFingerprint)
                {
                    return new SignalStep(false, null);
                }

                episode = existing.Episode + 1;
            }
            else if (same)
            {
                episode = existing.Episode;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(existing.RowVersion))
                {
                    _logger.LogWarning("{Context} {ContextId}: Signal {SignalId} was read without a row version; not superseded", contextLabel, contextId, existing.Id);
                    return new SignalStep(false, SignalChangedCode);
                }

                var closed = await _supersedeSignal(existing.Id, existing.RowVersion, request.PolicyCode, ct).ConfigureAwait(false);
                if (closed != SignalCloseOutcome.Closed)
                {
                    return new SignalStep(false, SignalChangedCode);
                }

                episode = existing.Episode + 1;
            }
        }

        var result = await _writeSignal(request with { Episode = episode }, ct).ConfigureAwait(false);
        if (result.IsSkipped)
        {
            if (PermanentSkipCodes.Contains(result.SkippedRefusalCode!))
            {
                return new SignalStep(false, null, result.SkippedRefusalCode);
            }

            _logger.LogWarning("{Context} {ContextId}: the Signal of policy {PolicyCode} on {Subject} {SubjectId} was skipped ({Refusal})",
                contextLabel, contextId, request.PolicyCode, request.SubjectEntityLogicalName, request.SubjectId, result.SkippedRefusalCode);
            return new SignalStep(false, SignalWriteSkippedCode);
        }

        return new SignalStep(true, null);
    }

    /// <summary>The latest episode of one (policy, subject): its row, number, whether it is resolved, its stored fingerprint and row version.</summary>
    internal sealed record ExistingEpisode(Guid Id, int Episode, bool IsResolved, string? Fingerprint, string? RowVersion);

    private Task<ExistingEpisode?> LatestEpisodeAsync(SignalWriteRequest request, CancellationToken ct) =>
        LatestEpisodeAsync(request.PolicyCode, request.SubjectEntityLogicalName, request.SubjectId, ct);

    /// <summary>
    /// The latest episode of (<paramref name="policyCode"/>, <paramref name="subjectEntity"/>, <paramref name="subjectId"/>) by the dedupe-key prefix;
    /// null when none exists. Task 101 B2b-2 reads it to close a Signal whose condition a correction cleared.
    /// </summary>
    internal async Task<ExistingEpisode?> LatestEpisodeAsync(string policyCode, string subjectEntity, Guid subjectId, CancellationToken ct)
    {
        var prefix = SignalWriter.DedupeKeySubjectPrefix(policyCode, subjectEntity, subjectId.ToString("D").ToLowerInvariant());
        var query = new QueryExpression(SignalEntity)
        {
            ColumnSet = new ColumnSet("sprk_dedupekey", "sprk_signalstatus", "sprk_evidencerefs", "versionnumber"),
            NoLock = true,
        };
        query.Criteria.AddCondition("sprk_dedupekey", ConditionOperator.BeginsWith, prefix);
        var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);

        ExistingEpisode? latest = null;
        foreach (var row in rows.Entities)
        {
            if (!SignalWriter.TryParseDedupeKey(row.GetAttributeValue<string>("sprk_dedupekey"), out var code, out var entity, out var id, out var episode)
                || !string.Equals(code, policyCode, StringComparison.Ordinal)
                || !string.Equals(entity, subjectEntity, StringComparison.Ordinal) || id != subjectId)
            {
                continue;
            }

            if (latest is null || episode > latest.Episode)
            {
                latest = new ExistingEpisode(
                    row.Id,
                    episode,
                    row.GetAttributeValue<OptionSetValue>("sprk_signalstatus")?.Value == SignalWriter.SignalStatusResolved,
                    FingerprintOf(row.GetAttributeValue<string>("sprk_evidencerefs")),
                    row.RowVersion ?? (row.GetAttributeValue<long?>("versionnumber") is { } v ? v.ToString(CultureInfo.InvariantCulture) : null));
            }
        }

        return latest;
    }

    /// <summary>The <see cref="BillingFindingSignalMapper.FingerprintEvidenceKind"/> line's <c>ref</c> in a stored <c>sprk_evidencerefs</c>; null when absent or unreadable.</summary>
    internal static string? FingerprintOf(string? evidenceJson)
    {
        if (string.IsNullOrWhiteSpace(evidenceJson))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(evidenceJson) is JsonArray lines
                ? lines.OfType<JsonObject>()
                    .Where(l => Text(l, "kind") == BillingFindingSignalMapper.FingerprintEvidenceKind)
                    .Select(l => Text(l, "ref"))
                    .FirstOrDefault()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonObject node, string name) =>
        node.TryGetPropertyValue(name, out var value) && value is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;
}
