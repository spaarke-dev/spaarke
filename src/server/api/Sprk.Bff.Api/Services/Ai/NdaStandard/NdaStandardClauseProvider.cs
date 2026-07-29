using System.Text.RegularExpressions;

namespace Sprk.Bff.Api.Services.Ai.NdaStandard;

/// <summary>
/// One clause of the Spaarke Baseline NDA Standard (KNW-011, Part B). <paramref name="Ref"/> is the
/// canonical token (B1..B16); <paramref name="Title"/> is the clause name; <paramref name="Text"/> is
/// the human-readable standard position (Required / Acceptable / Red-flags) shown to a reviewer.
/// </summary>
public sealed record NdaStandardClause(string Ref, string Title, string Text);

/// <summary>
/// UAT round-3 D3 — serves the full text of a Spaarke NDA-standard clause by ref, so the NDA review's
/// in-document comment can show the standard behind each <c>standardRef</c> citation (e.g. "B5 - Use &amp;
/// disclosure obligations") on demand.
/// </summary>
/// <remarks>
/// The clause bodies are the authoritative Part B positions of the committed KNW-011 baseline
/// (<c>projects/ai-advanced-capabilities-nda-r1/notes/spaarke-nda-standard-baseline.md</c>). They are
/// embedded here as a static, deterministic dictionary rather than retrieved from the RAG index
/// (<c>spaarke-rag-references</c>): the index is CHUNKED for semantic grounding, so a single clause is
/// fragmented across chunks and can't be fetched exactly by id — the wrong tool for an exact
/// single-clause lookup (per the D3 terrain map). Baseline v0.1 is not yet counsel-ratified; when it is,
/// update both this provider and the markdown together (they are the same small source of truth).
///
/// <para><b>Ref normalization</b> (label drift): the review's <c>standardRef</c> ("B3 - Definition of
/// Confidential Information") and the prompt taxonomy ("B5 Use &amp; standard of care") differ in their
/// label suffix, so lookup keys ONLY on the leading <c>B\d+</c> token — case-insensitive, suffix
/// ignored.</para>
///
/// Stateless singleton (no I/O, no DI reach) — pure in-memory.
/// </remarks>
public sealed class NdaStandardClauseProvider
{
    private static readonly Regex RefToken = new(@"\bB(\d{1,2})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, NdaStandardClause> Clauses = new[]
    {
        new NdaStandardClause("B1", "Parties & mutuality",
            "Required: all parties and roles identified; mutual (two-way) unless a one-way flow is intended; affiliates addressed consistently. " +
            "Acceptable: unilateral where only one party discloses. " +
            "Red flags: missing/mismatched parties or affiliates → Medium; obligations inconsistent between parties in a “mutual” NDA → High."),
        new NdaStandardClause("B2", "Purpose (permitted use)",
            "Required: a narrow, specific purpose tied to the actual transaction (e.g. “to evaluate a potential supply relationship”). " +
            "Acceptable: a reasonably scoped business purpose. " +
            "Red flags: vague “internal business purposes” / open-ended purpose → High; a purpose so narrow it blocks legitimate use → Medium."),
        new NdaStandardClause("B3", "Definition of Confidential Information",
            "Required: broad enough to cover all forms — oral, visual, written, electronic, derived, pre-existing — not gated on being “marked confidential.” " +
            "Acceptable: a marking requirement only if paired with a catch-all for information a reasonable person would deem confidential. " +
            "Red flags: marking-only definition with no oral/unmarked protection → High; overbroad definition sweeping in public/non-sensitive info → Medium."),
        new NdaStandardClause("B4", "Exclusions / carve-outs",
            "Required: the four standard carve-outs — (a) public domain (no fault of recipient), (b) already known prior to disclosure, (c) independently developed without reference, (d) lawfully received from a third party without breach. " +
            "A “required by law” carve-out belongs in Compelled Disclosure (B7), not here. " +
            "Red flags: any of the four missing → Medium; an unreasonable proof burden on the recipient → Medium."),
        new NdaStandardClause("B5", "Use & disclosure obligations (standard of care)",
            "Required: use solely for the Purpose; protect with at least the recipient's own-information care and no less than reasonable care; no third-party disclosure except permitted recipients. " +
            "Acceptable: “reasonable care” alone. " +
            "Red flags: a weak/undefined standard of care → Medium; strict liability or obligations exceeding the own-information standard → High."),
        new NdaStandardClause("B6", "Permitted recipients",
            "Required: employees, agents, advisors, contractors, representatives with a reasonable need to know, each bound by no-less-protective obligations; the recipient remains responsible for their compliance. " +
            "Acceptable: add affiliates / financing sources where deal-relevant, still bound + need-to-know. " +
            "Red flags: no requirement that recipients be bound → High; recipient not responsible for its representatives' breach → High; missing access for advisors/affiliates needed to evaluate → Medium."),
        new NdaStandardClause("B7", "Compelled disclosure",
            "Required: permitted when required by law/court/regulator, conditioned on prompt notice (where lawful), reasonable cooperation, and disclosing only what is legally required. " +
            "Red flags: no notice/cooperation obligation → Medium; an impracticable “must resist all requests” burden → Medium."),
        new NdaStandardClause("B8", "Term & confidentiality (survival) period",
            "Required: distinguish the agreement term (disclosure window, typically 1–3 years) from the confidentiality period (survival, typically 3–5 years post-disclosure/termination); trade secrets protected for as long as they remain trade secrets (may be indefinite). " +
            "Acceptable: a fixed confidentiality term of 2–5 years; perpetual for trade secrets. " +
            "Red flags: obligations expire too soon for sensitive info → High; an indefinite/very long term for ordinary (non-trade-secret) info → Medium."),
        new NdaStandardClause("B9", "Return or destruction",
            "Required: on expiry/termination/request — cease use; return or destroy; certify in writing on request. Carve-out for automated backups / archival / legal-hold / compliance copies (which remain subject to confidentiality). " +
            "Red flags: no return/destruction obligation → Medium; no backup/legal-retention carve-out → Medium."),
        new NdaStandardClause("B10", "Residual knowledge",
            "Required (default company posture, disclosing-favorable): no broad residuals clause; unaided-memory residual rights are disfavored. " +
            "Acceptable: a narrow residual clause only where reciprocal and limited to unaided memory. " +
            "Red flags: a broad residual-knowledge right undermining protection → High; absence of any residual carve-out where the receiving side legitimately needs it → Low/Medium."),
        new NdaStandardClause("B11", "Non-solicit / standstill / restrictive covenants",
            "Required: an NDA should not smuggle in non-solicit, non-compete, standstill, or exclusivity unless intended and separately negotiated. " +
            "Red flags: a hidden restrictive covenant, long duration, broad covered persons, or unrelated restriction → High; absence where commercially expected → Low."),
        new NdaStandardClause("B12", "No warranty / no obligation",
            "Required: a disclaimer that Confidential Information is provided as-is, with no warranty of accuracy/completeness, and no obligation to proceed with the transaction. " +
            "Red flags: a missing disclaimer creating accuracy liability → Medium; a disclaimer that also excludes liability for fraud/intentional misrepresentation → Medium."),
        new NdaStandardClause("B13", "Remedies",
            "Required: acknowledgment that damages are inadequate and the disclosing party may seek injunctive/equitable relief (ideally without bond and without proving actual damages), in addition to other remedies. " +
            "Red flags: no injunctive-relief provision → Medium; automatic-injunction language, broad indemnities, or unsupported liquidated damages → High."),
        new NdaStandardClause("B14", "Assignment",
            "Required: no assignment without consent, but permit assignment to affiliates or in a merger/sale of the business. " +
            "Red flags: free assignment transferring obligations to unknown parties → Medium; a restriction blocking a legitimate affiliate/deal transfer → Low/Medium."),
        new NdaStandardClause("B15", "Governing law & dispute resolution",
            "Required: a named governing law + forum (company-preferred jurisdiction where negotiable); a symmetric process; service-of-process addressed. " +
            "Red flags: unfamiliar/unfavorable law, an asymmetric dispute process, an inconvenient forum, or missing service provisions → Medium."),
        new NdaStandardClause("B16", "Drafting-integrity checks",
            "Required (mechanical, flagged as separate findings): consistent defined terms and entity names; intact cross-references and numbering; no duplicated provisions, missing schedules/exhibits, or internal contradictions. " +
            "Each issue → Low–Medium depending on operative impact."),
    }.ToDictionary(c => c.Ref, StringComparer.OrdinalIgnoreCase);

    /// <summary>All clauses in canonical B1..B16 order (for a "list the standard" affordance / tests).</summary>
    public IReadOnlyList<NdaStandardClause> AllClauses { get; } =
        Clauses.Values.OrderBy(c => int.Parse(c.Ref[1..], System.Globalization.CultureInfo.InvariantCulture)).ToList();

    /// <summary>
    /// Resolve a clause by any citation form that contains its <c>B\d+</c> token ("B5", "B5 - Use &amp;
    /// disclosure obligations", "b5"). Returns null when the input has no B-token or names a clause
    /// outside B1..B16.
    /// </summary>
    public NdaStandardClause? TryResolve(string? clauseRef)
    {
        if (string.IsNullOrWhiteSpace(clauseRef)) return null;
        var match = RefToken.Match(clauseRef);
        if (!match.Success) return null;
        var token = "B" + match.Groups[1].Value.TrimStart('0');
        // TrimStart('0') guards against "B05"; a bare "B0" (token becomes "B") or an out-of-range number
        // simply misses the dictionary and returns null.
        return Clauses.TryGetValue(token, out var clause) ? clause : null;
    }
}
