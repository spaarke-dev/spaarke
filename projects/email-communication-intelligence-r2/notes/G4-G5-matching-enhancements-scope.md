# G4 + G5 — Matching Enhancements Scope (eval harness + Tier-3 learned scorer + party graph)

> **Created**: 2026-09-03 · owner-directed ("scope G4 now"; "include G5, amend ADR-013 only if needed")
> **Parent**: `email-matching-and-triage-go-forward-plan.md` · **Architecture**: `docs/architecture/communication-intelligence-architecture.md` §4–§7 (13-rung Association Engine)
> **Key ADR finding**: **no ADR amendment is required** — see §0.

These two are one connected effort: **G4 builds the labeled dataset + measurement**, and that dataset is exactly what **G5's learned scorer trains + evaluates against**. Do G4 first; G5 depends on it.

---

## 0. ADR positioning (the #4 question, answered)

The belief that "ADR-013 forbids ML in matching" is a **misreading**. Verified against the ADRs:

- **ADR-013** (AI facade boundary) only requires AI/ML be reached via `Services/Ai/PublicContracts/` facades (not injected as internals). It says nothing against ML in matching.
- **ADR-045** (communication architecture) has exactly **one** binding rule here (line 49): *"MUST NOT auto-file on a semantic (rung 4) or AI (rung 5) match — those always land as Suggested/Ambiguous."* It does **not** forbid ML; it forbids ML/AI being the **auto-file authority**.

The affinity rung's `// no AI/ML — ADR-013` comment is a **conservative choice for that rung** (it stays deterministic frequency-counting to be fully auditable), not a blanket engine prohibition.

**Conclusion:** a Tier-3 learned scorer is compliant **as-is** if it (a) is reached via a `PublicContracts` facade and (b) emits **suggest-tier** matches (never auto-files). That guardrail is not a capability limit — for legal-matter association a probabilistic model silently auto-filing to the wrong matter is precisely the harm the doctrine prevents; the scorer can be arbitrarily sophisticated feeding the **ranking + suggestion** layer, where it adds the most value.

- **No ADR change to build G5 as designed below.**
- **The ONE case that WOULD need an ADR-045 amendment (§6.5 Path B):** allowing the learned scorer to *auto-file* above some very-high confidence. **Recommendation: do NOT** — keep AI/ML suggest-only; deterministic signals own auto-file. If the owner still wants it later, that is a scoped, explicit amendment with its own guardrail (e.g. a separate `LearnedAutoFileThreshold` kill-switch + a mandatory human-audit sample), not a silent relaxation.

---

## G4 — Tiered evaluation harness

**Goal:** measured, per-rung precision/recall for the Association Engine against a golden labeled set — the gate before any threshold / kill-switch / rung change. Today those are owner-judgment with no measured backstop.

### G4.1 Golden labeled dataset (the foundation — nothing else works without it)
- **Source:** `sprk_communication` rows that have a **human-confirmed** regarding (status Resolved *after* a human confirm, or an explicit override) → each is a labeled (envelope → correct record) pair. The affinity confirmation write (R-1) already marks human confirmations; reuse that signal to distinguish human-confirmed from engine-auto-filed.
- **Also capture negatives:** human **overrides** (engine said X, human chose Y) and **rejections** — the highest-value labels.
- **Storage:** a versioned fixture set under `tests/integration/seam/AssociationGolden/` (JSON: normalized envelope + expected target(s) + label provenance). De-identify per ADR-015 (no raw PII in the committed fixture — store hashed addresses + structural features, or keep the live-data variant tenant-scoped and out of git).
- **Size target:** start ~200 labeled cases spanning all 13 rungs' trigger conditions; grow from the confirmation stream.

### G4.2 Eval runner
- A harness that runs `IncomingAssociationResolver.EvaluateAsync` (the write-free path — already exists) over each labeled case and records, **per rung**: fired / matched / confidence, and the final decision vs the label.
- **Metrics per rung + overall:** precision, recall, and the confusion of status bands (Resolved/Suggested/Ambiguous/PendingReview vs. the human label). Break out by record type (matter/project/invoice) — thresholds are already per-type-tunable.
- **Output:** a report artifact (markdown + JSON), **observation-only, never a CI gate** (ADR-038 coverage-is-observation discipline). Run on demand + optionally nightly.

### G4.3 Invariant test (cheap, high-value, do even if the harness slips)
- A seam test asserting **"no lower-numbered rung's confident result is silently overridden by a higher-numbered rung"** (the matching-approaches doc's "no silent override" governance rule) — the mapper already reinforces rather than overrides, so this locks that behavior against regression.

### G4 sizing / sequencing
- G4.3 is small (1 seam test). G4.1 + G4.2 are a **mini-project** (~1 focused task each). **No existing labeled dataset** → G4.1 is the real cost. Recommend: build G4.1 from the confirmation stream first (it also feeds G5), then G4.2, then wire the report.

---

## G5 — Tier-3 learned scorer + party/relationship graph

**Goal:** close the one genuinely-incomplete tier (statistical linkage) beyond today's affinity frequency-count, and give Tier-2 the "which of counterparty X's 3 matters?" resolving power.

### G5.1 Party / relationship graph (Tier-2 substrate — do this first; it's ADR-clean and non-ML)
- Today Tier-2 is a flat participant index (`sprk_communicationparticipant`) + affinity frequency store. A **relationship graph** (`Person ↔ Organization ↔ Matter/Project/Invoice`, built from existing Dataverse relationships) lets `ParticipantCorrelationRung` resolve *which* of a counterparty's active matters an email belongs to, instead of surfacing all of them.
- **Build:** a read-model over existing Dataverse associations (no new AI). Feeds the participant rung's candidate ranking. Pure deterministic graph traversal — **no ADR tension**.

### G5.2 Learned scorer (the ML piece — suggest-tier, facade-routed)
- **Model:** start with a **transparent weighted logistic model / gradient-boosted tree** over the signal features the rungs already compute (domain partial-match, name similarity Jaro-Winkler, temporal proximity to matter activity, participant overlap, attachment patterns, affinity count, graph distance from G5.1). Fellegi-Sunter-style is the classical fit.
- **Training data:** the G4.1 golden set (that's the dependency — no labels, no scorer).
- **Placement:** a **new AI-tier rung** (`RungKind.LearnedLinkage`), reached via a new `PublicContracts` facade (ADR-013), emitting **suggest-tier** matches only (ADR-045). It joins the noisy-OR aggregation like `SemanticMatch` does — improving ranking + surfacing candidates, never auto-filing.
- **Auditability:** persist the feature vector + score in provenance (the engine already serializes per-candidate contributors) so every suggestion is explainable — critical for a legal product and for the ADR-015 "AI flags, never decides" posture.
- **Feedback loop:** confirmations/overrides (via R-1 + G4.1) retrain the model → the loop the matching-approaches doc asked for, now closed end-to-end.

### G5 sizing / sequencing
1. **G5.1 party graph** (deterministic, ADR-clean) — buildable now; improves Tier-2 immediately.
2. **G4.1 labeled set** — prerequisite for G5.2.
3. **G5.2 learned scorer** — after G4.1 has enough labels; ship behind a per-tenant kill-switch (ADR-018), suggest-only.

---

## Recommended order across G4+G5

1. **G4.3** invariant test (cheap, now).
2. **G5.1** party/relationship graph (ADR-clean, improves Tier-2 now).
3. **G4.1** golden labeled set (from the R-1 confirmation stream) — foundation for G4.2 + G5.2.
4. **G4.2** eval runner + report.
5. **G5.2** learned scorer (suggest-tier, facade, kill-switch) once labels suffice.

**No ADR amendment required for any of the above.** The only amendment-gated option (learned-scorer auto-file) is explicitly **not recommended** and deferred.
