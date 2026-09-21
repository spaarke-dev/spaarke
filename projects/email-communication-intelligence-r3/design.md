# Email Communication Intelligence — R3 — Design Charter

> **Status**: DRAFT — for owner review before `/design-to-spec` → `/project-pipeline`.
> **Date**: 2026-09-07
> **Builds on**: `email-communication-intelligence-r2` (shipped, merged to master). R3 extends the R2/R1 **13-rung Association Engine**, its provenance/reconciliation surface, and its triage layer — it does **not** re-architect them.
> **Seed inputs** (all authored in r2, carried here verbatim by reference):
> - `projects/email-communication-intelligence-r2/notes/email-r3-candidate-backlog.md` — the reconciliation-card + matching-signal backlog from r2 UAT (the `Fw: PAT-942665` patent-intake scenario).
> - `projects/email-communication-intelligence-r2/notes/G4-G5-matching-enhancements-scope.md` — the eval-harness + learned-scorer + party-graph scope, incl. the ADR positioning.
> - `projects/email-communication-intelligence-r2/notes/email-matching-and-triage-go-forward-plan.md` — the P1/P2/G1–G5 tracker + the email-metadata-facets open decision.

---

## 0. Thesis — what R3 is, in one paragraph

R1 taught Spaarke to **understand** an email; R2 hardened **trusted capture, dedup, and made the intelligence visible** (the reconciliation surface). R3 makes the matching **honest, measurable, and legible**: it (1) fixes the one signal-quality defect r2 UAT surfaced — "related to X" is a strong *relevance* signal that today gets demoted because one confidence number does double duty (ranking **and** auto-file safety); (2) builds the **evaluation harness** that must exist before any confidence/threshold change is made by feel; (3) closes the last genuinely-incomplete matching tier with a **party/relationship graph + a suggest-only learned scorer**; and (4) finishes the card-identity work r2 started by wiring **real record names** onto the thread/attachment cards that today fall back to a type+reason label. Every item is additive to the existing engine and surface — no new engine, no revived node graph, regarding writes still only via `RegardingFieldMap` (ADR-024/045).

**R3 is NOT:** a re-architecture, a new matching ladder (the "5-tier ladder" idea is **already built** as the 13-rung engine — see §7), a new `IEmailFilterService` (**does not exist; do not build** — §7), or a relaxation of the AI/ML-never-auto-files invariant (ADR-045 stays intact — §4).

---

## 1. Why now — what R2 left on the table

### 1.1 The concrete UAT evidence (the `Fw: PAT-942665` patent-intake capture)

A patent-intake email ("new patent application… related to both PAT-942665 and PAT-942404… associate to either record") produced **four** `sprk_regardingmatter` candidates. Resolved to names (from `email-r3-candidate-backlog.md`):

| Rank | Matter # | Name | Score | Signal | On the strip? |
|---|---|---|---|---|---|
| 1 | PAT-897705 | Methods and Systems for Customized Network | 100% | ThreadContinuity (forwarded thread's parent) | ✅ |
| 2 | REAL-2026-123456.02 | **Real Estate Transaction Matter** (noise) | 99.65% | attached invoice PDF + `REAL-*` body ref | ✅ |
| 3 | PAT-942665 | Patent Application 19183531 — Elisa Liardo | 98.95% | ExplicitRef **capped 0.65** + subject name-match 0.97 | ✅ |
| 4 | PAT-942404 | Targeted Protein Degradation Patent | 96.5% | ExplicitRef **capped 0.65** + body name-match 0.90 | ❌ **hidden** |

Three defects fell out of that one capture. **Two are already fixed in r2** and are recorded here only as closed context:

- **R3-CARD-1 (SHIPPED in r2, PR #951)** — cards no longer show a raw GUID; name-matched cards use the embedded provenance name, GUID-only (thread/attachment) matches show the match reason + entity type.
- **R3-CARD-2 (SHIPPED in r2, PR #951)** — the top-3 strip cap that hid PAT-942404 is fixed with a "See all (N)" modal listing every above-floor candidate, each confirmable through the same additive write path.

**What remains for R3** is the harder, engine-and-measurement half.

### 1.2 The three structural gaps R3 addresses

**Gap 1 — a strong relevance signal is demoted by a dual-purpose number.** `IdentifierReverseLookupRung.ReferencedNotFiledCap` (FR-12) caps a well-formed identifier from **0.90 → 0.65** when `NewRecordIntentDetector` sees "new … related to X" — a **misfile guard** so "new matter related to LIT-123456" doesn't *auto-file* onto LIT-123456. But that single number does **two jobs at once** — *ranking* AND *auto-file eligibility* — so protecting auto-file also demotes the rank of a genuinely-relevant record. The owner's UAT point: *"a signal 'related to' … should be a **high** signal — it's telling what is related to."*

**Gap 2 — no measured backstop for matching quality.** Every threshold, kill-switch, and cap (the 0.85 auto-file bar, the C-1 narrowing, the 0.65 cap above) is **owner judgment with no precision/recall measurement**. There is no golden labeled set and no eval runner. Changing Gap-1's cap "by feel" is exactly the move this project must not make — so the harness is a **prerequisite**, not a nice-to-have.

**Gap 3 — the last matching tier is incomplete, and thread/attachment cards still can't show real names.** Tier-2 is a flat participant index + affinity frequency count; it can say "this address belongs to counterparty X" but not "X is active on 3 matters — which one." And the R3-CARD-1 fix deliberately *deferred* real names for GUID-only cards to R3 (they need host `resolveDisplayName` wiring the shared lib doesn't have).

---

## 2. Proposed scope — four workstreams

### Workstream S — Signal quality: "related to X" ranks high, stays auto-file-safe (R3-SIGNAL-1)
- **S1 · Split ranking from auto-file eligibility in FR-12.** Today one capped confidence (0.65) governs both. Separate them so a "related to X" identifier match **ranks high as a suggestion** (surfaces prominently on the strip / "See all") while remaining **auto-file-ineligible** (the misfile guard ADR-045 actually cares about is preserved). Mechanism options to evaluate (design-time, gated on G4): a distinct `suggestRank` vs `autoFileConfidence` on the candidate; or an `autoFileEligible:false` flag decoupled from the reinforced confidence used for ordering. **Do NOT change the constant silently** — this is an ADR-045 / FR-12 change surfaced per CLAUDE.md §6.5 (see §4), and its before/after is **measured on G4's golden set**, never tuned by feel.
- **S2 · Provenance legibility for the demotion.** When a candidate is auto-file-demoted-but-relevant, the provenance/card reason should say so ("referenced, not filed — suggested for review") so the reviewer understands *why* a high-relevance record isn't auto-filed. Pure surface/provenance-string change; no engine-behavior change.

### Workstream G4 — Measurement: the tiered evaluation harness (the gate for Workstream S)
- **G4.3 · Invariant test (cheap, do first).** A seam test asserting **no lower-numbered rung's confident result is silently overridden by a higher-numbered rung** (the "no silent override" governance rule). The mapper already *reinforces* rather than overrides — this locks that against regression. Small, high-value, independent of the dataset.
- **G4.1 · Golden labeled dataset (the foundation).** Build (envelope → correct record) pairs from **human-confirmed** `sprk_communication` regarding decisions (status Resolved *after* a human confirm — reuse the R-1 affinity-confirmation signal to distinguish human-confirmed from engine-auto-filed) **and** the highest-value negatives: human **overrides** (engine said X, human chose Y) and rejections. Store as a versioned fixture set under `tests/integration/seam/AssociationGolden/` (JSON: normalized envelope + expected target(s) + label provenance), **de-identified per ADR-015** (hashed addresses + structural features, or a tenant-scoped live-data variant kept out of git). Target ~200 cases spanning all 13 rungs; grow from the confirmation stream.
- **G4.2 · Eval runner + report.** Run the **write-free** `IncomingAssociationResolver.EvaluateAsync` over each labeled case; record per-rung fired/matched/confidence and final decision vs label; compute **per-rung + overall precision/recall** and the status-band confusion (Resolved/Suggested/Ambiguous/PendingReview vs human label), broken out by record type. Output a markdown+JSON artifact, **observation-only, never a CI gate** (ADR-038 coverage-is-observation). On demand + optionally nightly.

### Workstream G5 — Learned matching: party graph + suggest-only scorer
- **G5.1 · Party / relationship graph (deterministic, ADR-clean — do first).** A read-model over existing Dataverse relationships (`Person ↔ Organization ↔ Matter/Project/Invoice`) so `ParticipantCorrelationRung` can resolve *which* of counterparty X's active matters an email belongs to, instead of surfacing all of them. Pure deterministic graph traversal — **no AI, no ADR tension** — buildable independently of G4.
- **G5.2 · Tier-3 learned scorer (suggest-tier, facade-routed).** A **transparent** weighted-logistic / gradient-boosted model (Fellegi-Sunter-style) over signal features the rungs already compute (domain partial-match, Jaro-Winkler name similarity, temporal proximity to matter activity, participant overlap, attachment patterns, affinity count, G5.1 graph distance). Placed as a **new AI-tier rung** (`RungKind.LearnedLinkage`) reached via a **new `Services/Ai/PublicContracts/` facade** (ADR-013), emitting **suggest-tier matches only** (ADR-045) that join the noisy-OR aggregation like `SemanticMatch` — **improving ranking + surfacing candidates, never auto-filing.** Persist the feature vector + score in provenance (auditability / "AI flags, never decides", ADR-015). Trained on G4.1; ship behind a per-tenant kill-switch (ADR-018). **Depends on G4.1** (no labels, no scorer).

### Workstream C — Card fidelity carry-over: real names on GUID-only cards
- **C1 · Host `resolveDisplayName` wiring.** R3-CARD-1 shows a match-reason + entity-type label for thread/attachment candidates whose only identity is a GUID (no embedded `name="…"`). R3 wires the host-injected `resolveDisplayName(entity, id)` (already a typed prop on `EmailAssociationsAndTracking.types`, currently unwired) through the reconciliation hosts (the `CommunicationReconciliation` code page + the SpaarkeAi `communications-reconciliation` widget) so those cards show the **real record name + number**. Shared-lib is host-injected/context-agnostic (ADR-012) — the resolver is supplied by each host, not baked into the lib. Dual-consumer rebuild + `/conflict-check` on `Spaarke.Communication.Components`.

### Decision D — Email-metadata facets (OPEN owner decision — not yet in/out)
- Captured `.eml` is already semantic-searchable (verified live in r2), but email-native metadata (**from / to / cc / thread / sent-date**) is **flattened into free text, not indexed as structured filterable facets**. `TextExtractorService` produces an `EmailMetadata` object that `FileIndexingService` currently discards. Adding sender/date/thread **filtering** = extend `KnowledgeDocument` + the `spaarke-files-index` schema + carry `EmailMetadata` through indexing → **an index-schema migration + full reindex** (non-trivial). **This is an explicit owner Y/N decision** — it was *not* part of the "send .eml to the index" ask, and it carries a reindex cost. If **yes**, it becomes Workstream F with its own migration plan; if **no**, it stays documented here as considered-and-declined.

---

## 3. Deep-dive — the design questions

### 3.1 Why the harness must precede the signal change
Workstream S changes a confidence cap that governs auto-file safety. Changing it without measurement risks trading one misfile mode for another (demote too little → the "new matter related to LIT-123456" auto-files onto LIT-123456 again; demote too much → the relevant record sinks below the fold). G4.2's per-rung precision/recall on the golden set is the only honest way to see which. **Sequence: G4.3 (invariant) → G4.1 (labels) → G4.2 (runner) → then S1 tuned against it.** S2 (provenance legibility) and G5.1 (party graph) are ADR-clean and can proceed in parallel without waiting on labels.

### 3.2 Why G5.2 needs no ADR amendment (the misreading, corrected)
The belief that "ADR-013 forbids ML in matching" is a **misreading** (verified in `G4-G5-matching-enhancements-scope.md` §0). **ADR-013** only requires AI/ML be reached via `Services/Ai/PublicContracts/` facades — it says nothing against ML in matching. **ADR-045**'s one binding rule here is *"MUST NOT auto-file on a semantic (rung 4) or AI (rung 5) match — those always land Suggested/Ambiguous."* It forbids ML being the **auto-file authority**, not ML itself. A Tier-3 learned scorer is compliant **as-is** if it (a) is facade-routed and (b) emits suggest-tier only. That guardrail is not a capability limit — for legal-matter association, a probabilistic model silently auto-filing to the wrong matter is precisely the harm the doctrine prevents; the scorer can be arbitrarily sophisticated feeding the **ranking + suggestion** layer, where it adds the most value. **The only amendment-gated option** — letting the learned scorer *auto-file* above some very-high confidence — is explicitly **not recommended** and deferred (§7).

### 3.3 The affinity loop is already closed
G3 (close the affinity feedback loop) is **already done** (R-1, 2026-08-06): `POST /api/communications/{id}/confirm-affinity` + `AffinityConfirmationRecorder` + client wiring + tests shipped. G5.2's retraining consumes that same confirmation stream + the G4.1 labels — the loop the matching-approaches analysis asked for, now closed end-to-end. No work owed here beyond reusing the signal.

---

## 4. ADR tensions (surfaced at design time per CLAUDE.md §6.5)
- **ADR-045 / FR-12 (Workstream S — the one real tension).** Splitting ranking from auto-file eligibility **changes FR-12 behavior**. It must be surfaced, not changed silently. **It preserves the invariant ADR-045 actually protects** (a "related to X" match still cannot auto-file) while separating the orthogonal ranking concern — so it reads as a **Path A project-scoped refinement** (documented + reviewer-approved) rather than a relaxation. Confirm the path with the owner at spec time; **gate the numeric change on G4**.
- **ADR-013 (Workstream G5).** The learned scorer is reached via a **new `Services/Ai/PublicContracts/` facade** — compliant; no injection of AI internals into engine/CRUD code. **No amendment** (§3.2).
- **ADR-045 (Workstream G5).** Learned scorer emits **suggest-tier only**; joins noisy-OR like `SemanticMatch`; **never auto-files**. **No amendment** — the only case that would need one (learned auto-file) is deferred (§7).
- **ADR-024 / ADR-045 (extend, never fork).** New rung (`LearnedLinkage`), the party-graph read-model, and the FR-12 split are **additive** to the existing ladder; regarding writes still only via `RegardingFieldMap`. No fork; node engine stays frozen (ADR-039).
- **ADR-010 / ADR-032 (DI minimalism; unconditional registration).** New rung + facade registered unconditionally; feature-gate the scorer via config/kill-switch (ADR-018), not conditional DI.
- **ADR-015 (AI flags, never decides).** Golden-set fixtures de-identified; learned-scorer feature vectors persisted in provenance for auditability.
- **ADR-038 (testing).** G4 report is **observation-only, never a CI gate**; the golden set lives under a KEEP `tests/integration/seam/**` path.

---

## 5. Review decisions + open gates

**Carried as agreed from r2 (the r3 scope the owner directed):**
- Ship **R3-SIGNAL-1** (Workstream S), **G4** (harness), **G5** (party graph + suggest-only scorer), and **host `resolveDisplayName` real-names** (Workstream C). **No ADR amendment required** for G4/G5; the FR-12 split is surfaced per §6.5.
- **Rejected/superseded** (do not build): the 5-tier "matching ladder" (already the 13-rung engine); `IEmailFilterService` (does not exist). [§7]

**Open gates for owner review of this charter:**
1. **Decision D — email-metadata facets: Y/N?** Carries an index-schema migration + full reindex cost. Not part of any prior ask. If yes → Workstream F.
2. **Workstream S — confirm the ADR path** (Path A refinement vs Path B amendment) for the FR-12 ranking/auto-file split.
3. **G5.2 build trigger** — build now (behind kill-switch, once G4.1 has enough labels) vs park until the deterministic-resolution metric plateaus. G5.1 (party graph) is unconditionally in.
4. **Golden-set data policy** — committed de-identified fixture vs tenant-scoped live-data variant kept out of git (ADR-015).

---

## 6. Success criteria (measurable — for the spec)
- **Measured per-rung precision/recall** exists for the Association Engine on a ≥200-case golden set (today: none) — the standing backstop for every future threshold change.
- **"Related to X" ranks in the visible candidate set** for the `PAT-942665`/`PAT-942404` regression case **while remaining auto-file-ineligible** — verified on the golden set (no new misfile introduced).
- **Thread/attachment cards show a real record name + number** (not a type+reason fallback) once `resolveDisplayName` is wired, in both reconciliation hosts.
- **`ParticipantCorrelationRung` resolves to the specific matter** (not all of a counterparty's matters) where the party graph has the edge — measured as a recall gain on the golden set.
- **Learned scorer, when enabled, improves ranking** (top-1 / top-3 recall on the golden set) **with zero change to auto-file counts** (invariant: it never files).

---

## 7. Considered and deferred (looked at, scoped out — with rationale)
- **The 5-tier "matching ladder" as greenfield** — **already built** as the 13-rung Association Engine (`Services/Communication/Engine/`), deterministic-first, noisy-OR reinforced, AI-never-auto-files. Do not rebuild; retaining the greenfield framing risks a future reader re-implementing it. (`email-matching-and-triage-go-forward-plan.md` §"5-tier proposal, mapped".)
- **`IEmailFilterService`** (the old note's "Tier-1 home") — **does not exist**; identifier extraction lives in the rungs. Do not build (§11 component justification).
- **Learned scorer *auto-filing*** — the one option that would need an ADR-045 §6.5 Path-B amendment. **Not recommended**; keep AI/ML suggest-only, deterministic signals own auto-file. If ever wanted, it is a scoped explicit amendment with its own guardrail (a separate `LearnedAutoFileThreshold` kill-switch + mandatory human-audit sample), not a silent relaxation.
- **Email-metadata facets** — parked as **Decision D** pending owner Y/N (reindex cost).
- **Triage category resolution** — **already fixed + UAT-passed in r2** (the Linear-path `$choices` enum injection); not r3 scope.

---

## 8. Dependencies & coordination
- **`Spaarke.Communication.Components`** (shared lib — Workstream C touches it) — historically contended with `email-communication-solution-r5`, now **closed** (code on master), so contention is low. Still `/conflict-check` before every shared-lib PR; dual-consumer rebuild (`CommunicationReconciliation` code page + SpaarkeAi `communications-reconciliation` widget) per the code-page-deploy dual-deploy warning.
- **`Services/Communication/Engine/**` + `Services/Ai/PublicContracts/`** (hot paths, root CLAUDE.md §10) — Workstreams S + G5 add a rung + a facade. Placement Justification in the PR/design; publish-size delta reported per BFF task (ceiling ≤60 MB); reach AI only via `PublicContracts` (ADR-013). `parallel-safe:false` on engine writers; `/conflict-check` before every BFF PR.
- **R-1 affinity confirmation stream** — reused by G4.1 (labels) and G5.2 (retraining); no work owed, only consumed.
- **Architecture reference** — `docs/architecture/communication-intelligence-architecture.md` §3–§7 (13-rung engine, refreshed in r2). Update it when the `LearnedLinkage` rung + party graph land.

---

## 9. Next steps
1. **Owner review of this charter** — resolve the §5 gates (email-metadata-facets Y/N; the FR-12 ADR path; the G5.2 build trigger; the golden-set data policy).
2. `/design-to-spec` — turn the confirmed scope into `spec.md` (FRs/NFRs, closed-set acceptance criteria incl. the auto-file-negative cases, ADR-Tensions section for the FR-12 split).
3. `/project-pipeline` — plan + task POMLs, sequenced **G4.3 → G4.1 → G4.2 → S1** (measurement before the signal change), with **G5.1** and **C1** in parallel, **G5.2** after G4.1.
