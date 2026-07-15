namespace Sprk.Bff.Api.Services.Communication.Engine;

/// <summary>
/// The machine-readable association-decision trail (R4 FR-11) serialized to
/// <c>sprk_associationprovenance</c> (JSON, ≤10000 chars). Records which rungs fired, the reinforced
/// per-field candidates + their per-rung contributors, the metadata-only structural signals, and the
/// final ladder decision (status, auto-file, threshold, kill-switch). Enables data-driven threshold
/// tuning from real hit/misfile outcomes (owner priority (e)).
/// </summary>
public sealed record AssociationProvenance
{
    /// <summary>Schema version of this provenance document.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Message direction the decision was made for (<c>Incoming</c>/<c>Outgoing</c>) — decision is direction-symmetric.</summary>
    public required string Direction { get; init; }

    /// <summary>The ladder decision.</summary>
    public required AssociationDecisionTrace Decision { get; init; }

    /// <summary>Distinct rung kinds that produced any match (writable or metadata-only), sorted.</summary>
    public required IReadOnlyList<string> RungsFired { get; init; }

    /// <summary>Per-field reinforced candidates (winner + any conflicting runner-up), with contributors.</summary>
    public required IReadOnlyList<CandidateTrace> Candidates { get; init; }

    /// <summary>Metadata-only structural signals (rung 3) — category + obligations, recorded regardless of the association decision (always-run pass).</summary>
    public required IReadOnlyList<SignalTrace> Signals { get; init; }
}

/// <summary>The final ladder decision.</summary>
public sealed record AssociationDecisionTrace
{
    public required string Status { get; init; }
    public required bool AutoFiled { get; init; }

    /// <summary>Effective kill-switch state for this decision (ADR-018).</summary>
    public required bool KillSwitchEnabled { get; init; }

    /// <summary>Effective auto-file threshold for this decision.</summary>
    public required double AutoFileThreshold { get; init; }

    /// <summary>Reinforced deterministic-only confidence of the strongest candidate (drives auto-file).</summary>
    public required double TopDeterministicConfidence { get; init; }

    /// <summary>Reinforced full confidence (incl. any AI rung) of the strongest candidate (drives Suggested/Pending).</summary>
    public required double TopConfidence { get; init; }

    /// <summary><c>true</c> if any AI rung (4–5) contributed to the strongest candidate.</summary>
    public required bool AiInvolved { get; init; }

    /// <summary>Human-readable reason the status was chosen (incl. "kill-switch off ⇒ suggested").</summary>
    public required string Reason { get; init; }
}

/// <summary>A reinforced per-field candidate target.</summary>
public sealed record CandidateTrace
{
    public required string Field { get; init; }
    public required string TargetEntity { get; init; }
    public required string TargetId { get; init; }

    /// <summary>Reinforced full confidence (noisy-OR across distinct contributing rung kinds).</summary>
    public required double ReinforcedConfidence { get; init; }

    /// <summary>Reinforced deterministic-only confidence (rungs 0–3).</summary>
    public required double DeterministicConfidence { get; init; }

    /// <summary><c>true</c> if this candidate's target was written to the record.</summary>
    public required bool Written { get; init; }

    /// <summary><c>true</c> if this candidate is part of a high-confidence conflict on its field.</summary>
    public required bool Conflict { get; init; }

    public required IReadOnlyList<ContributorTrace> Contributors { get; init; }
}

/// <summary>One rung's contribution to a candidate.</summary>
public sealed record ContributorTrace
{
    public required string Rung { get; init; }
    public required double Confidence { get; init; }
    public required string Provenance { get; init; }
}

/// <summary>A metadata-only structural signal (category + obligations) with no regarding target.</summary>
public sealed record SignalTrace
{
    public required string Category { get; init; }
    public required double Confidence { get; init; }
    public required string Provenance { get; init; }
    public required IReadOnlyList<string> Obligations { get; init; }
}
