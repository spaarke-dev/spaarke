using Sprk.Bff.Api.Services.Communication.Engine;

namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Read-only projection of an <see cref="AssociationDecision"/> for the on-demand suggestion endpoint
/// (task 074, Path C). Returns what the Association Engine <b>would</b> file for a stored
/// <c>sprk_communication</c> — the ladder status, whether it is auto-file eligible, the reinforced
/// per-field candidate targets (with per-rung contributors), and the metadata-only structural signals —
/// <b>without writing to the record</b>. Projected from the engine's <see cref="AssociationProvenance"/>
/// (<see cref="CandidateTrace"/>/<see cref="SignalTrace"/>) so the wire contract stays decoupled from the
/// engine-internal provenance records. System.Text.Json-friendly (plain init records).
/// </summary>
public sealed record SuggestAssociationsResponse
{
    /// <summary>The communication the suggestions were evaluated for.</summary>
    public required Guid CommunicationId { get; init; }

    /// <summary>Ladder status label (<see cref="AssociationStatusCodes.Name"/>) the engine would assign.</summary>
    public required string Status { get; init; }

    /// <summary>
    /// <c>true</c> when the engine would auto-file (deterministic ≥ threshold, kill-switch on). Surfaced,
    /// not acted on — this endpoint never writes. Privilege, when flagged by an AI signal, is surfaced in
    /// <see cref="Signals"/>, never decided (NFR-07 / ADR-015).
    /// </summary>
    public required bool AutoFileEligible { get; init; }

    /// <summary>Reinforced per-field candidate targets (winner + any conflicting runner-up).</summary>
    public IReadOnlyList<SuggestedCandidate> Candidates { get; init; } = Array.Empty<SuggestedCandidate>();

    /// <summary>Metadata-only structural signals (category + obligations) recorded by the always-run detector pass.</summary>
    public IReadOnlyList<SuggestedSignal> Signals { get; init; } = Array.Empty<SuggestedSignal>();

    /// <summary>Projects an engine decision into the read-only wire contract.</summary>
    public static SuggestAssociationsResponse FromDecision(Guid communicationId, AssociationDecision decision) => new()
    {
        CommunicationId = communicationId,
        Status = AssociationStatusCodes.Name(decision.Status),
        AutoFileEligible = decision.AutoFiled,
        Candidates = decision.Provenance.Candidates
            .Select(c => new SuggestedCandidate
            {
                Field = c.Field,
                TargetEntity = c.TargetEntity,
                TargetId = c.TargetId,
                ReinforcedConfidence = c.ReinforcedConfidence,
                DeterministicConfidence = c.DeterministicConfidence,
                Written = c.Written,
                Conflict = c.Conflict,
                Contributors = c.Contributors
                    .Select(contrib => new SuggestedContributor
                    {
                        Rung = contrib.Rung,
                        Confidence = contrib.Confidence,
                        Provenance = contrib.Provenance,
                    })
                    .ToArray(),
            })
            .ToArray(),
        Signals = decision.Provenance.Signals
            .Select(s => new SuggestedSignal
            {
                Category = s.Category,
                Confidence = s.Confidence,
                Provenance = s.Provenance,
                Obligations = s.Obligations.ToArray(),
            })
            .ToArray(),
    };
}

/// <summary>A reinforced per-field candidate target the engine would write (or surface for confirmation).</summary>
public sealed record SuggestedCandidate
{
    public required string Field { get; init; }
    public required string TargetEntity { get; init; }
    public required string TargetId { get; init; }

    /// <summary>Reinforced full confidence (incl. any AI rung).</summary>
    public required double ReinforcedConfidence { get; init; }

    /// <summary>Reinforced deterministic-only confidence (rungs 0–3) — drives auto-file eligibility.</summary>
    public required double DeterministicConfidence { get; init; }

    /// <summary><c>true</c> if this candidate's target would be written to the record.</summary>
    public required bool Written { get; init; }

    /// <summary><c>true</c> if this candidate is part of a high-confidence conflict on its field.</summary>
    public required bool Conflict { get; init; }

    public IReadOnlyList<SuggestedContributor> Contributors { get; init; } = Array.Empty<SuggestedContributor>();
}

/// <summary>One rung's contribution to a candidate.</summary>
public sealed record SuggestedContributor
{
    public required string Rung { get; init; }
    public required double Confidence { get; init; }
    public required string Provenance { get; init; }
}

/// <summary>A metadata-only structural signal (category + obligations) with no regarding target.</summary>
public sealed record SuggestedSignal
{
    public required string Category { get; init; }
    public required double Confidence { get; init; }
    public required string Provenance { get; init; }
    public IReadOnlyList<string> Obligations { get; init; } = Array.Empty<string>();
}
