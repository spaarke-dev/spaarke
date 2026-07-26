# KNW-011 — Spaarke Baseline NDA Standard (v0.1)

> **External ID**: KNW-011
> **Content Type**: Reference
> **Tenant**: system
> **Domain**: legal
> **Keywords**: NDA, non-disclosure, confidentiality, review, standard, clause-rubric, risk-rating, redline
> **Created**: 2026-07-22
> **Task**: 012 (ai-advanced-capabilities-nda-r1)
> **Source**: `projects/ai-advanced-capabilities-nda-r1/notes/spaarke-nda-standard-baseline.md` (v0.1, synthesized)

---

## Status & provenance

**BASELINE — synthesized from open sources for bootstrapping `NDA-REVIEW@v1`. Not yet ratified by counsel.** Replace/refine each position with the company's actual standard when supplied.

This baseline is an **original synthesis**; it does not redistribute source agreements verbatim. Standard positions are informed by the Bonterms Mutual NDA and Common Paper Mutual NDA v1.0 (both CC BY 4.0), and by the Mike OSS `mike-workflows/nda-review` clause taxonomy (MIT). When surfacing content derived from these sources to users, retain attribution: "standard positions informed by Bonterms & Common Paper Mutual NDAs, CC BY 4.0."

**Disclaimer (surface to users when this reference is cited in output)**: AI-generated review; **not legal advice**. High/Critical findings and any redline the system is unsure about should be reviewed by counsel.

---

## Part A — Overall Risk Rubric

Each NDA gets one overall rating; each flagged section gets its own severity.

| Rating | Meaning |
|---|---|
| **Low** | Standard or minimal concern; safe to sign as-is or with trivial edits. |
| **Medium** | Manageable negotiation concern; sign after the noted edits. |
| **High** | Material legal / commercial / operational / enforceability concern requiring negotiation. |
| **Critical** | Severe issue potentially **blocking signature** until resolved. |

**Perspective rule**: the review is run from the **represented party's** perspective (default: Spaarke/the requester's company). Findings and redlines favor that party; adverse-only issues are omitted unless they also create represented-party risk.

---

## Part B — Clause-by-Clause Standard (the compliance rubric)

For each clause: **Required** = the company standard position · **Acceptable** = tolerable range · **Red flag → severity** = deviations to flag & redline.

### B1. Parties & mutuality
- **Required**: All parties and roles identified; mutual (two-way) unless a one-way flow is intended; affiliates addressed consistently.
- **Acceptable**: Unilateral where only one party discloses.
- **Red flags**: Missing/mismatched parties or affiliates → **Medium**; obligations inconsistent between parties in a "mutual" NDA → **High**.

### B2. Purpose (permitted use)
- **Required**: Narrow, specific purpose tied to the actual transaction (e.g., "to evaluate a potential supply relationship").
- **Acceptable**: Reasonably scoped business purpose.
- **Red flags**: Vague "internal business purposes" / open-ended purpose → **High** (receiving side may accept; disclosing side flag); purpose so narrow it blocks legitimate use → **Medium**.

### B3. Definition of Confidential Information
- **Required**: Broad enough to cover all forms — **oral, visual, written, electronic, derived, pre-existing** — not gated on "marked confidential."
- **Acceptable**: Marking requirement **only if** paired with a catch-all for information a reasonable person would deem confidential.
- **Red flags**: Marking-only definition with no oral/unmarked protection → **High** (disclosing side); overbroad definition sweeping in public/non-sensitive info → **Medium** (receiving side).

### B4. Exclusions / carve-outs
- **Required**: The four standard carve-outs — (a) public domain (no fault of recipient), (b) already known prior to disclosure, (c) independently developed without reference, (d) lawfully received from a third party without breach.
- **Acceptable**: The four above; a "required by law" carve-out belongs in Compelled Disclosure (B7), not here.
- **Red flags**: Any of the four missing → **Medium**; unreasonable proof burden on the recipient → **Medium**.

### B5. Use & disclosure obligations (standard of care)
- **Required**: Use **solely for the Purpose**; protect with at least the recipient's own-information care **and no less than reasonable care**; no third-party disclosure except permitted recipients.
- **Acceptable**: "Reasonable care" alone.
- **Red flags**: Weak/undefined standard of care → **Medium** (disclosing); **strict liability** or obligations exceeding own-information standard → **High** (receiving).

### B6. Permitted recipients
- **Required**: Employees, agents, advisors, contractors, representatives with a **reasonable need to know**, each bound by **no-less-protective** obligations; recipient **remains responsible** for their compliance.
- **Acceptable**: Add affiliates / financing sources where deal-relevant, still bound + need-to-know.
- **Red flags**: No requirement that recipients be bound → **High**; recipient not responsible for its representatives' breach → **High** (disclosing); missing access for advisors/affiliates needed to evaluate → **Medium** (receiving).

### B7. Compelled disclosure
- **Required**: Permitted when required by law/court/regulator, **conditioned on** prompt notice (where lawful), reasonable cooperation, and disclosing only what is legally required.
- **Red flags**: No notice/cooperation obligation → **Medium**; impracticable "must resist all requests" burden → **Medium** (receiving).

### B8. Term & confidentiality (survival) period
- **Required**: Distinguish **agreement term** (disclosure window, typically **1–3 years**) from **confidentiality period** (survival, typically **3–5 years** post-disclosure/termination); **trade secrets protected for as long as they remain trade secrets** (may be indefinite).
- **Acceptable**: Fixed confidentiality term (market norm — ~74% fixed) 2–5 yrs; perpetual for trade secrets.
- **Red flags**: Obligations expire too soon for sensitive info → **High** (disclosing); indefinite/very long term for ordinary (non-trade-secret) info → **Medium** (receiving).

### B9. Return or destruction
- **Required**: On expiry/termination/request: cease use; return or destroy; certify in writing on request. **Carve-out** for automated backups / archival / legal-hold / compliance copies (which remain subject to confidentiality).
- **Red flags**: No return/destruction obligation → **Medium** (disclosing); no backup/legal-retention carve-out → **Medium** (receiving).

### B10. Residual knowledge
- **Required (default company posture: disclosing-favorable)**: **No broad residuals clause.** Unaided-memory residual rights are disfavored.
- **Acceptable**: A narrow residual clause only where reciprocal and limited to unaided memory.
- **Red flags**: Broad residual-knowledge right undermining protection → **High** (disclosing); absence of any residual carve-out where receiving legitimately needs it → **Low/Medium** (receiving).

### B11. Non-solicit / standstill / restrictive covenants
- **Required**: An NDA should **not** smuggle in non-solicit, non-compete, standstill, or exclusivity unless intended and separately negotiated.
- **Red flags**: Hidden restrictive covenant, long duration, broad covered persons, or unrelated restriction → **High**; absence where commercially expected → **Low** (disclosing).

### B12. No warranty / no obligation
- **Required**: Disclaimer that Confidential Information is provided **as-is**, no warranty of accuracy/completeness, and no obligation to proceed with the transaction.
- **Red flags**: Missing disclaimer creating accuracy liability → **Medium** (disclosing); disclaimer that also excludes liability for fraud/intentional misrepresentation → **Medium** (receiving).

### B13. Remedies
- **Required**: Acknowledgment that damages are inadequate and the disclosing party may seek **injunctive/equitable relief** (ideally **without bond and without proving actual damages**), in addition to other remedies.
- **Red flags**: No injunctive-relief provision → **Medium** (disclosing); **automatic injunction** language, broad indemnities, or unsupported liquidated damages → **High** (receiving).

### B14. Assignment
- **Required**: No assignment without consent; **permit assignment to affiliates or in a merger/sale** of the business.
- **Red flags**: Free assignment transferring obligations to unknown parties → **Medium** (disclosing); restriction blocking legitimate affiliate/deal transfer → **Low/Medium** (receiving).

### B15. Governing law & dispute resolution
- **Required**: A named governing law + forum (company-preferred jurisdiction where negotiable); symmetric process; service-of-process addressed.
- **Red flags**: Unfamiliar/unfavorable law, asymmetric dispute process, inconvenient forum, missing service provisions → **Medium**.

### B16. Drafting-integrity checks (mechanical — flag as separate findings)
Inconsistent defined terms · inconsistent entity names · broken cross-references · numbering errors · duplicated provisions · missing schedules/exhibits · internal contradictions. Each → **Low–Medium** depending on operative impact.

---

## Part C — Required Terms for a Compliant NDA

A compliant company NDA MUST contain: mutual-by-default framing (B1); narrow Purpose (B2); broad multi-form CI definition (B3); the four carve-outs (B4); solely-for-Purpose use + reasonable/own-care standard (B5); bound, need-to-know permitted recipients with recipient responsibility (B6); compelled-disclosure with notice+cooperation (B7); term 1–3 yr + confidentiality 3–5 yr with trade-secret survival (B8); return/destruction with backup carve-out (B9); no broad residuals (B10); no smuggled restrictive covenants (B11); as-is/no-obligation disclaimer (B12); injunctive-relief remedy (B13); consent-based assignment with affiliate/M&A permission (B14); governing law + forum (B15).

**Draft parameters collected from requester**: parties (names/entities), mutual vs. one-way, Purpose (specific), Effective Date, Term, Confidentiality Period, governing law/forum. Missing parameters are **requested, never invented** (leave a clear placeholder).

---

## Ratification checklist (before v1.0)
- [ ] Counsel confirms/overrides each B-clause "Required" position + severity.
- [ ] Set company defaults: governing law/forum (B15), standard term & confidentiality period (B8), residuals posture (B10).
- [ ] Confirm mutual NDA as the default company template; supply the actual company NDA template for `NDA-DRAFT@v1`.
- [ ] Approve the risk-weighting → overall-rating aggregation logic.
