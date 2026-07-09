using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Chat.Gate;

/// <summary>
/// Structural provenance of the request being gated (Policy v2 note §3). This is a DATA
/// flag the platform already knows from HOW the invocation arrived — never a re-derivation
/// from message text. Document-derived content is NEVER read as a user utterance (E-3).
/// </summary>
public enum GateRequestSource
{
    /// <summary>A Click-path invocation (<c>invoke(bindingId, args)</c>) — user-explicit by construction.</summary>
    Click,

    /// <summary>The user's typed utterance in THIS turn (the only text the classifier may treat as intent).</summary>
    UserUtterance,

    /// <summary>A model-initiated tool call in a later turn (no fresh user utterance this turn).</summary>
    ModelInitiated,

    /// <summary>Content lifted from an uploaded / retrieved document — untrusted, non-utterance (E-3).</summary>
    DocumentContent,

    /// <summary>Content lifted from a prior tool result — non-utterance origin.</summary>
    ToolResult,
}

/// <summary>
/// What the immediately-preceding model turn proposed (Policy v2 note E-1). The affirmation
/// binds to the proposal ONLY when it named exactly one concrete action with complete args.
/// </summary>
public sealed record GateProposalContext
{
    /// <summary>Number of concrete actions the preceding model turn proposed.</summary>
    public int ConcreteActionCount { get; init; }

    /// <summary>True when the single proposed action carried complete args.</summary>
    public bool ArgsComplete { get; init; }
}

/// <summary>
/// The deterministic origin-classifier input (Policy v2 note §3). Every field is a structural
/// fact the platform already holds — provenance flags, the ledger's recorded origin, the
/// dispatch's own enumeration. The classifier reads these; it performs NO intent detection
/// (ADR-039 MUST NOT — that is the ONE agent loop's job, upstream).
/// </summary>
public sealed record GateOriginRequest
{
    /// <summary>Structural provenance of the request.</summary>
    public required GateRequestSource Source { get; init; }

    /// <summary>
    /// E-4: true when the user's utterance in THIS turn named this capability's action verb +
    /// its enumerated invocation. This is a STRUCTURAL fact from the ONE dispatch's enumeration
    /// (which capability it selected from the utterance), not an NLP re-parse here. Only ever
    /// consulted for <see cref="GateRequestSource.UserUtterance"/>.
    /// </summary>
    public bool UtteranceEnumeratesCapability { get; init; }

    /// <summary>
    /// E-1: true when the utterance is a bare affirmation ("go ahead" / "yes" / "do it"),
    /// as classified by the deterministic vocabulary in
    /// <see cref="Sprk.Bff.Api.Services.Ai.Chat.ElicitationTurnRouter.ClassifyBareAffirmation"/>.
    /// </summary>
    public bool IsBareAffirmation { get; init; }

    /// <summary>E-5: true when this utterance is the answer to a pending elicitation.</summary>
    public bool IsElicitationAnswer { get; init; }

    /// <summary>E-1: the immediately-preceding model proposal, when the utterance is a bare affirmation.</summary>
    public GateProposalContext? PrecedingProposal { get; init; }

    /// <summary>
    /// E-5: the origin recorded on the original request's Gate ledger entry — inherited by an
    /// elicitation answer rather than re-classified from scratch. Null ⇒ fail-closed inferred.
    /// </summary>
    public GateRequestOrigin? OriginalRequestOrigin { get; init; }

    /// <summary>
    /// E-2: a prior <see cref="GateRequestOrigin.Explicit"/> classification recorded for the
    /// SAME (capability, args) identity, when a later model-initiated call re-invokes it. Null
    /// when there is no prior explicit classification to carry forward.
    /// </summary>
    public GateRequestOrigin? PriorExplicitForSameCapabilityArgs { get; init; }

    /// <summary>
    /// E-2: true when a USER turn has intervened since the prior classification — which RESETS
    /// explicitness (the prior explicit no longer carries forward).
    /// </summary>
    public bool UserTurnSincePriorClassification { get; init; }
}

/// <summary>
/// The deterministic, fail-closed request-origin classifier (Policy v2 note §3 + E-1..E-6).
/// Origin ∈ {<see cref="GateRequestOrigin.Explicit"/>, <see cref="GateRequestOrigin.Inferred"/>};
/// the fail-closed default is <see cref="GateRequestOrigin.Inferred"/>. The model NEVER decides
/// its own request's origin — every branch below is a switch over structural flags.
///
/// <para>
/// <b>Layering (E-3)</b>: this classifier reads ONLY user-utterance-sourced structure. It never
/// reads document-derived content as a user utterance. Injection detection is a SEPARATE overlay
/// (<c>ConfirmationPolicyEngine</c> overlay 1) that runs AFTER this and can override an
/// <c>Explicit</c> result — origin and injection are layered, never merged.
/// </para>
///
/// <para>
/// <b>Component Justification (CLAUDE.md §11)</b>: (1) <i>Existing</i> —
/// <see cref="Sprk.Bff.Api.Services.Ai.Chat.ElicitationTurnRouter"/> owns deterministic
/// answer-vs-command disambiguation but not origin classification; nothing else classifies
/// request origin. (2) <i>Extension</i> — the affirmation VOCABULARY is added to
/// ElicitationTurnRouter (its natural home) and reused here; the tree itself is new because
/// no prior surface produces a <c>GateRequestOrigin</c>. (3) <i>Cost of doing nothing</i> —
/// without a deterministic classifier, origin would be a model opinion (the exact ADR-039
/// second-intent mechanism FR-A1-03 bans) or a blanket "always confirm" that re-installs the
/// R3-1 friction.
/// </para>
/// </summary>
public static class RequestOriginClassifier
{
    /// <summary>
    /// Classifies request origin deterministically (fail-closed <see cref="GateRequestOrigin.Inferred"/>).
    /// </summary>
    public static GateRequestOrigin Classify(GateOriginRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Source switch
        {
            // Click path — user-explicit by construction.
            GateRequestSource.Click => GateRequestOrigin.Explicit,

            // E-3: untrusted / non-utterance origins are NEVER read as a user utterance.
            GateRequestSource.DocumentContent or GateRequestSource.ToolResult => GateRequestOrigin.Inferred,

            // Text path — classify from turn STRUCTURE, never model opinion.
            GateRequestSource.UserUtterance => ClassifyUtterance(request),

            // E-2: a later model-initiated call inherits a prior explicit ONLY for the same
            // (capability, args) AND only if no user turn has intervened; else fail-closed.
            GateRequestSource.ModelInitiated =>
                request.PriorExplicitForSameCapabilityArgs == GateRequestOrigin.Explicit &&
                !request.UserTurnSincePriorClassification
                    ? GateRequestOrigin.Explicit
                    : GateRequestOrigin.Inferred,

            _ => GateRequestOrigin.Inferred, // fail-closed
        };
    }

    private static GateRequestOrigin ClassifyUtterance(GateOriginRequest r)
    {
        // E-1: a bare affirmation is explicit IFF it binds to a single, complete proposal.
        if (r.IsBareAffirmation)
        {
            return ClassifyAffirmation(r.PrecedingProposal);
        }

        // E-5: an elicitation answer inherits the original request's ledger origin.
        if (r.IsElicitationAnswer)
        {
            return r.OriginalRequestOrigin ?? GateRequestOrigin.Inferred;
        }

        // E-4: explicit for the enumerated action set only (the dispatch's own enumeration).
        if (r.UtteranceEnumeratesCapability)
        {
            return GateRequestOrigin.Explicit;
        }

        // Anything else in a user utterance is undecidable ⇒ fail-closed.
        return GateRequestOrigin.Inferred;
    }

    /// <summary>
    /// E-1 ruling: a bare affirmation is <see cref="GateRequestOrigin.Explicit"/> IFF the
    /// immediately-preceding model turn proposed <b>exactly one</b> concrete action with
    /// <b>complete</b> args. Two proposals, zero proposals, or incomplete args ⇒
    /// <see cref="GateRequestOrigin.Inferred"/> ⇒ confirm.
    /// </summary>
    public static GateRequestOrigin ClassifyAffirmation(GateProposalContext? proposal) =>
        proposal is { ConcreteActionCount: 1, ArgsComplete: true }
            ? GateRequestOrigin.Explicit
            : GateRequestOrigin.Inferred;
}
