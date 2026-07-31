# KNW-012 — General Agreement Review Fallback Standard (v0.1)

> **External ID**: KNW-012
> **Content Type**: Reference
> **Tenant**: system
> **Domain**: legal
> **Keywords**: general, agreement, contract, fallback, clause-taxonomy, risk-posture, G1, G2, G3, G4, G5, G6, G7, G8, G9, G10, G11, G12, G13, G14, G15, G16
> **Created**: 2026-07-31
> **Task**: 003 (ai-advanced-capabilities-agreements-r1)
> **Source**: authored for this task — no prior baseline document existed for the `general` sub-domain.

---

## Status & scope — FALLBACK, NOT A FIRM STANDARD

**This is the deliberately lower-value fallback pack** (design Lens 3(d) value model: per-type packs — like the NDA
standard, KNW-011 — are the value; this pack exists only so a review can still run usefully when no dedicated
firm standard applies). It is retrieved for the `sprk_agreementtype` registry's `general` row: the classifier's
`sprk_isfallback=Yes` catch-all, used when an uploaded agreement is clearly a contract creating enforceable
obligations between parties, but its primary subject matter does not match one of the specific registered
sub-domains (nda, employment, lease, asset-purchase, services, licensing, vendor, partnership, loan), OR when
classification confidence fell below the confirm threshold and the reviewer proceeded without picking a specific
type.

**NO FIRM STANDARD — GENERAL REVIEW.** Unlike a type-specific pack, this pack does NOT carry ratified positions
negotiated for a particular deal type. It supplies broad, cross-agreement-type clause categories (G1–G16) and a
generic, commercially-conventional risk posture for each — enough for the reviewing Action to still produce a
structured, cited review, but every finding measured against this pack should be treated as **lower-confidence
than a type-specific finding** (there is no company-specific policy behind these positions, only general market
convention). When a per-type pack is later authored for this agreement's actual category (a sibling project,
out of scope here), that pack supersedes this fallback for that type.

**Disclaimer (surface to users when this reference is cited in output)**: AI-generated review; **not legal
advice**. Because there is no firm-specific standard behind a general-fallback finding, treat every High/Critical
finding measured against this pack as requiring counsel review even more than usual.

---

## Part A — Generic Risk Posture

Same Low/Medium/High/Critical severity scale as every Spaarke agreement review (see the Action's own Overall Risk
Rubric — not repeated here). Applied to a general-fallback review, severities should skew conservative: because
there is no firm-specific standard to compare against, only clearly conventional/market-standard positions should
be treated as Low risk; anything unusual, one-sided, or commercially aggressive should be flagged at least Medium
even without a specific type-standard to cite a deviation from, on the reasoning that a reviewing attorney should
see it regardless of whether a dedicated standard exists yet.

---

## Part B — General Clause Categories (the fallback taxonomy)

Each category below is identified by its canonical **Clause ID (G1..G16)** — cite this exact ID as `standardRef`
when a finding is measured against it. These are **broad categories common across commercial agreement types**,
not a negotiated company position — treat "Generic position" as market convention, not firm policy.

### G1. Parties & recitals
Generic position: all parties, their legal entity names/roles, and the deal's basic purpose/background are clearly stated and internally consistent.

---

### G2. Scope of agreement / subject matter
Generic position: the subject matter (what is being bought, sold, licensed, leased, performed, or exchanged) is defined clearly enough that both sides' core obligations are unambiguous.

---

### G3. Payment / consideration
Generic position: amount, currency, timing, and method of payment (or other consideration) are stated; late-payment consequences (interest, suspension) are reasonable and mutual in effect, not one-sided.

---

### G4. Term & termination
Generic position: a stated effective date and term (fixed, renewing, or evergreen); termination rights (for cause, for convenience, on notice) are reasonably balanced between the parties, with a workable notice period.

---

### G5. Confidentiality (if present)
Generic position: if the agreement includes a confidentiality clause (rather than being a dedicated NDA), it defines confidential information reasonably broadly, carries the standard carve-outs (public domain, independently known, independently developed, lawfully received from a third party), and survives termination for a reasonable period.

---

### G6. Representations & warranties
Generic position: each party makes reasonable, mutual representations (authority to sign, no conflicting obligations); warranties are proportionate to the deal's value and risk — not so broad they create undue liability, not so thin they leave no recourse for defects/non-performance.

---

### G7. Indemnification & limitation of liability
Generic position: indemnification obligations are mutual or at least proportionate to fault; a liability cap exists and is not so low it's illusory nor so absent that ordinary business risk becomes unbounded; carve-outs from any cap (e.g. gross negligence, willful misconduct, confidentiality breach) are standard, not exotic.

---

### G8. Insurance (if applicable)
Generic position: where the deal type conventionally carries insurance requirements (services, construction, vendor), coverage types/limits are commercially reasonable for the deal's scale and named-insured/certificate requirements are workable, not onerous.

---

### G9. Intellectual property / ownership
Generic position: ownership of pre-existing IP, newly created IP/work product, and any licenses granted are clearly allocated; a party is not inadvertently assigning or licensing away IP beyond what the deal requires.

---

### G10. Compliance with law
Generic position: a mutual obligation to comply with applicable law; any deal-specific regulatory regime (export control, data privacy, industry-specific rules) is addressed if the subject matter implicates it.

---

### G11. Assignment & subcontracting
Generic position: assignment requires the other party's consent (not unreasonably withheld), with a carve-out permitting assignment to an affiliate or in connection with a merger/sale/reorganization of the business; subcontracting (if relevant) does not relieve the assigning party of responsibility for performance.

---

### G12. Dispute resolution & governing law
Generic position: a named governing law and forum/venue (or arbitration mechanism); the process is symmetric between the parties, not one-sided.

---

### G13. Force majeure
Generic position: a force majeure clause excusing performance for events genuinely outside a party's control (natural disaster, war, government action), not stretched to cover ordinary business or market risk; payment obligations for amounts already due are typically NOT excused.

---

### G14. Notices
Generic position: a workable notice mechanism (address, method, effective-on-receipt terms) so neither party can claim it was never validly notified of something material (termination, breach, renewal).

---

### G15. Boilerplate (amendment, entire agreement, severability, waiver)
Generic position: amendments require signed writing by both parties; an entire-agreement clause supersedes prior negotiations/side agreements; severability preserves the rest of the agreement if one provision is struck; failure to enforce a right once is not a waiver of future enforcement.

---

### G16. Drafting-integrity checks (mechanical — flag as separate findings)
Same mechanical check as every Spaarke agreement review: inconsistent defined terms · inconsistent entity names · broken cross-references · numbering errors · duplicated provisions · missing schedules/exhibits · internal contradictions. Each → **Low–Medium** depending on operative impact.

---

## Part C — When this pack is insufficient

If the uploaded agreement's actual subject matter clearly matches one of the specific registered sub-domains
(nda, employment, lease, asset-purchase, services, licensing, vendor, partnership, loan) but was routed here
anyway (e.g. low classifier confidence), the reviewing attorney should be aware a more specific — and more
valuable — standard likely exists or should be authored, and that this review's findings carry the reduced
confidence described above. This pack does not attempt to cover type-specific substantive positions (e.g. NDA's
required carve-out set, or a lease's rent-escalation norms) — those live in their own dedicated packs.
