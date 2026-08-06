# PLAN — Email Communication Intelligence R1 (Phase 1)

> **Generated**: 2026-07-28 (planning artifacts) · **Status**: Planning (task POMLs not yet generated)
> **Source**: [`spec.md`](spec.md) (17 FRs, 8 NFRs) · [`design.md`](design.md) §0 (authoritative)
> **Branch**: `work/email-communication-intelligence-r1`
> **Builds on (shipped + merged)**: `email-communication-solution-r4` (Association Engine + enrichment) · `spaarke-notification-spine-r1` (RI action / notification delivery) · `spaarke-ai-architecture-redesign-r2` (`Services/Ai/PublicContracts/` — complete, merged) · `email-communication-solution-r5` (review surfaces — **complete; owns all UI**).

---

## 1. Objective

Build the **intelligence + record-write spine** over the shipped communication engine. All work is **code-directed (Action + Binding, ADR-039)** reached via `Services/Ai/PublicContracts/` — the node-graph playbook engine is **frozen and MUST NOT** receive new capability. r1 builds **no UI**; it feeds r5's shipped surfaces via a feed + apply contract.

**Graduation** = all 9 spec Success Criteria met (see [`README.md`](README.md)).

**Phasing guardrail (design §0.11, build order)**: (1) **lean spine** — identifier rung + `TRIAGE-EMAIL` + RAG + RI-confidence + triage fields + `sprk_emailreviewlog` + notification path; (2) **Job B** propose→apply on r5 surfaces; (3) **regarding-vs-related intent**. Each stage validated before the next. FR-15 (shared/group mailbox) is a **spike-first track fully parallel to the spine**.

---

## 2. Architecture Context — Discovered Resources

### Applicable ADRs

| ADR | Full-doc path | Relevance to r1 |
|---|---|---|
| **ADR-045** | `docs/adr/ADR-045-communication-architecture.md` | Engine over normalized envelope; direction-symmetric enrichment; channel seams. **Extend, never fork.** |
| **ADR-024** | `.claude/adr/ADR-024-polymorphic-resolver-pattern.md` | Polymorphic regarding family; write only via the resolver; one regarding mechanism. |
| **ADR-039** | `docs/adr/ADR-039-grounded-execution-closed-catalogs.md` | Grounded execution / closed catalogs / Action + Binding. New capability = catalog data + `coded`/`prompted`, never the frozen node engine. |
| **ADR-013** | `docs/adr/ADR-013-ai-architecture.md` | BFF AI facade; `PublicContracts` seams only — no `IOpenAiClient`/`IPlaybookService` in Communication code. |
| **ADR-040** | `docs/adr/ADR-040-session-ledger.md` | Session ledger; store-before-render; owns the word "disposition" (r1 uses "review outcome"). |
| **ADR-041 / ADR-043 / ADR-047** | `docs/adr/ADR-041-judgment-confirmation-completion-policy.md` · `ADR-043-ai-capability-execution-spine.md` · `ADR-047-notification-action-spine.md` | Judgment gate / execution spine / notification spine — **Proposed, in-flight.** Pin to current shape; `/conflict-check` before BFF PRs; check charters. |
| **ADR-015** | `docs/adr/ADR-015-ai-data-governance.md` | Privilege flagged, never decided. |
| **ADR-018 / ADR-016 / ADR-014** | (jobs / kill-switch / caching / budgets) | Kill-switch; AI caching; per-tenant budgets. |
| **ADR-028** | `.claude/adr/ADR-028-spaarke-auth-architecture.md` | Auth — app-only background triage; OBO Job B writes. |
| **ADR-004 / ADR-036** | (job contract) | Job contract for any async work. |
| **ADR-038** | `docs/adr/ADR-038-testing-strategy.md` | Integration-heavy pyramid; vertical-slice seam tests are the DoD for dispatch-spine changes. |

### Existing patterns / canonical files (extend, don't rebuild)

All under `src/server/api/Sprk.Bff.Api/` unless noted.

- **Association rung pattern** — `Services/Communication/Engine/Rungs/ExplicitReferenceRung.cs` + `ThreadContinuityRung.cs` + `IAssociationRung.cs` (mirror rung 0 for the 7-entity identifier rung).
- **Regarding write map** — `Services/Communication/Engine/RegardingFieldMap.cs` (add `sprk_reportcard`→`sprk_regardingreportcard`).
- **Auto-file policy (C-1)** — `Services/Communication/Engine/AssociationStatusMapper.cs` + `AutoFileGate.cs` (narrow to rung 0+1).
- **Enrichment / RI emit** — `Services/Communication/CommunicationEnrichmentService.cs` (`RunAssessmentEmissionAsync` ~L238).
- **RI action path** — `Services/Communication/ICommunicationAssessedProducer.cs` + `CommunicationRuleGate.cs` + `CommunicationRiActionService.cs`.
- **Classification substrate** — `Services/Communication/Engine/Rungs/AiClassificationRung.cs` + `Models/Ai/Communication/CommunicationClassificationResult.cs` (reuse; no 2nd full LLM pass).
- **Write cores** — `Services/Ai/PublicContracts/IActionSeam.cs` + `Services/Ai/Nodes/ActionCore/UpdateRecordActionCore.cs` + `TaskActionCore.cs`.
- **AI facade** — `Services/Ai/PublicContracts/ICommunicationClassificationAi.cs`.
- **Endpoint group** — `Api/CommunicationEndpoints.cs` (extend `suggest-associations` family; add apply + queue-feed).
- **Capture (FR-15)** — `Services/Communication/GraphSubscriptionManager.cs`.
- **Binding reference** — `infra/dataverse/sprk_playbookconsumer-rows.json` + `infra/dataverse/inputschemas/create-task-v1.input.schema.json`.

### Applicable skills

`jps-action-create` (TRIAGE-EMAIL Action) · `jps-validate` (eval/render check) · `dataverse-create-schema` (triage fields, `sprk_emailreviewlog`) · `dataverse-deploy` (catalog rows, config seed) · `bff-deploy` (BFF) · `conflict-check` (**mandatory before every BFF PR** — shared `Services/Communication/`) · `code-review` + `adr-check` (Step 9.5 gates) · `ui-test` (operator UAT) · `task-execute` (all tasks).

### Scripts

`scripts/dataverse/Seed-PlaybookConsumers.ps1` (Binding rows) · `scripts/Refresh-ScopeModelIndex.ps1` (scope catalog after Action authoring) · `scripts/Deploy-BffApi.ps1` (BFF deploy) · `scripts/Test-SdapBffApi.ps1` (BFF verify).

### Knowledge / constraints

- `docs/architecture/communication-intelligence-architecture.md` · `communication-service-architecture.md` · `SPAARKE-LINEAR-AI-CONSUMER-ARCHITECTURE.md`.
- `docs/guides/ai-guide-consumer-wiring.md` · `BUILD-A-NEW-NARRATIVE-OUTPUT-CONSUMER.md` (Action + Binding authoring).
- `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md` (`Xrm.WebApi` vs BFF).
- `.claude/constraints/bff-extensions.md` — **binding** BFF-addition checklist (load before every BFF task).
- [`notes/email-intelligence-r1-coordination.md`](notes/email-intelligence-r1-coordination.md) — r5 feed/apply contract.

---

## 3. Placement Justification (root §10) + Hot-Path Declaration

### Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff>Y</bff>             <!-- Services/Communication rung + scorer; Services/Ai via PublicContracts; new endpoints -->
  <spaarkeai>N</spaarkeai> <!-- surfaces are r5's; r1 feeds them, builds no widget -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

### Placement (BFF domains touched)

All server work lands in **`Sprk.Bff.Api`** inside the **existing Communication + Ai domains** — no new top-level service.

- **`Services/Communication/Engine/`** — one new identifier rung (extends the ladder) + `RegardingFieldMap` entry + `AssociationStatusMapper`/`AutoFileGate` C-1 narrowing. **No second engine, no second regarding mechanism** (ADR-045 / ADR-024).
- **`Services/Communication/`** — RI-confidence scorer replaces the hardcoded-0 `CommunicationAssessedSignal.Confidence`; triage-output persistence on the enrichment path.
- **`Services/Ai/PublicContracts/`** — consume `IActionSeam` (Job B apply) + `ICommunicationClassificationAi`. **No new AI internals**; no `IOpenAiClient`/`IPlaybookService` injected into Communication code (NFR-03 / ADR-013).
- **`Api/CommunicationEndpoints.cs`** — apply + queue-feed endpoints extend the existing `suggest-associations` family (thin minimal-APIs).
- **Dataverse catalog data** — `sprk_analysisaction` (TRIAGE-EMAIL) + `sprk_playbookconsumer` (Binding) rows + `infra/dataverse/inputschemas/` + a golden-utterance eval case.
- **Dataverse schema** — triage fields on `sprk_communication`; `sprk_emailreviewlog`; taxonomy/priority seed. (`sprk_emailupdatefield`, `sprk_regardingreportcard`, RPTC row: **operator-created**, verified in 001.)

**Per-BFF-task gates (every BFF-touching task, root §10):** publish-size (baseline ~49.63 MB incl. PDBs; ceiling ≤60 MB; report absolute + delta) · no new HIGH CVE (`dotnet list package --vulnerable`) · `/conflict-check` before PR · test update obligation in `tests/unit/Sprk.Bff.Api.Tests/` + seam tests per ADR-038. Triage **adds no new heavy dependency** (reuses the AI stack) → expected delta ≈0.

### §11 Component Justification

Concrete cost-of-doing-nothing for each new surface is captured in **spec § "New Components (§11 three-question gate)"** — 7-entity rung (only matter numbers match without it), `sprk_emailreviewlog` (no defensible per-email review record), RI-confidence scorer (notification path stays dark), `TRIAGE-EMAIL` Action (no category/summary/obligations), `sprk_emailupdatefield` (Job B could write arbitrary columns), apply + queue-feed endpoints (r5 has no apply/queue path). Triage fields **extend** `sprk_communication` (no new entity, per D-01).

### ADR Tensions (spec §6.5 — on the record)

| ADR | Rule | Path | Resolution |
|---|---|---|---|
| ADR-040 | "disposition" is the AI-output-delivery term | **A (exception)** | Use "review outcome" for the triage decision; leave "disposition" to ADR-040 (naming disambiguation). |
| ADR-024 | regarding family expresses "regarding" only | **A (exception)** | Represent "related-to" distinctly for FR-12 without a second regarding mechanism; document the relationship model. |
| ADR-045 | ships auto-file for deterministic ≥0.85 (rungs 0–3) | **A (exception)** | C-1 narrows auto-file to rungs 0+1 (misfiling = #1 trust-killer); a tightening, kill-switch-governed. |
| ADR-041/043/047 | Proposed, in-flight | coordinate | Pin to current shape; `/conflict-check` before BFF PRs; check charters. |

---

## 4. External Prerequisites (operator-created — NOT r1 tasks; verified in Phase 0 task 001)

| Prerequisite | Used by |
|---|---|
| `sprk_emailupdatefield` table (Job B allow-list; FR-11 schema) | Phase 3 (Job B propose/apply) |
| `sprk_regardingreportcard` lookup on `sprk_communication` | 010 (RegardingFieldMap entry) |
| `sprk_recordtype_ref` **RPTC** row + `sprk_reportcardnumber` field | 010 / 020 (identifier rung roster) |
| `sprk_recordtype_ref` data-hygiene: `sprk_regardingfield` typos + `contact`-row anomaly | 020 (rung reads defensively, or clean first) |

Task 001 is **read-only verification** of the above; it does not create schema. Job B (Phase 3) is **gated** on `sprk_emailupdatefield` existing.

---

## 5. Phase Breakdown (WBS)

Baseline BFF publish: **~49.63 MB** incl. PDBs. Ceiling ≤60 MB. Report absolute + delta on every BFF task.

### Phase 0 — Prerequisites & verification
- **001** Verify operator schema inputs live in `spaarkedev1` (`sprk_regardingreportcard`; `sprk_recordtype_ref` RPTC row + `sprk_reportcardnumber`; `sprk_emailupdatefield` table) + data-hygiene check of `sprk_recordtype_ref` `sprk_regardingfield` typos. *(read-only; MINIMAL)*

### Phase 1 — Schema & catalog foundation (parallel; deps 001)
- **010** Add `sprk_reportcard`→`sprk_regardingreportcard` to `RegardingFieldMap.cs` (FR-02).
- **011** Triage fields on `sprk_communication`: category, priority, summary, obligations (lean JSON), riconfidence, reviewoutcome (FR-07).
- **012** `sprk_emailreviewlog` append-only audit entity (FR-08).
- **013** Category taxonomy + priority-weight config seed (FR-16).

### Phase 2 — Lean intelligence spine (the core)
- **020** 7-entity identifier rung — catalog-driven (`sprk_recordtype_ref`), value-based reverse lookup, reinforcement-gated, auto-file per C-1 (FR-01). *(opus/xhigh; FULL — high blast radius on association correctness.)*
- **021** Auto-file policy narrowing C-1 in `AssociationStatusMapper`/`AutoFileGate` — rung 0+1 auto-file, 2/3 → `Suggested` (FR-03).
- **022** `TRIAGE-EMAIL` Action authoring via `jps-action-create` — `{category, summary, obligations[], priority, reviewOutcome}` reusing `AiClassificationRung` signal, no 2nd full LLM pass (FR-05).
- **023** `TRIAGE-EMAIL` Binding + input/output schema (mirror-first) + golden-utterance eval case + RAG grounding + enrichment/event trigger via `PublicContracts` facade (FR-05/06, NFR-07).
- **024** RI-confidence scorer — compute (urgency × deterministic-rung agreement) + wire into `CommunicationAssessedSignal` (`RunAssessmentEmissionAsync`); lights up `CommunicationRiActionService` (FR-04). *(deps 022; reads 020 rung agreement.)*
- **025** Persist triage output to `sprk_communication` triage fields on enrichment path (FR-07 wiring). *(deps 011, 022.)*

### Phase 3 — Job B record currency (deps spine + `sprk_emailupdatefield`)
- **030** Job B propose — Action proposes allow-listed field updates (reads `sprk_emailupdatefield`), old→new, cited, confidence, stored as pending (FR-09). *(opus/high; FULL.)*
- **031** Job B apply endpoint → `IActionSeam.UpdateRecordAsync` under OBO + `sprk_emailreviewlog` audit row (FR-10). *(opus/high; FULL.)*
- **032** Job B queue-feed endpoint — ranked exceptions feed for r5 (FR-17).

### Phase 4 — Job C + intent (deps spine)
- **040** Job C email-triggered tasks/events via create-task pattern (`CREATE-TASK@v1`), cited (FR-14).
- **041** Attachment-grounded action extraction — ground Action on extracted attachment text, gated to action-triggers (FR-13). *(opus/high.)*
- **042** Regarding-vs-related intent — classify file/update/new-related; demote identifier on "new filing based on X"; propose create-record linked as related (FR-12). *(opus/xhigh; FULL.)*

### Phase 5 — FR-15 capture (PARALLEL track, spike-first)
- **050** SPIKE: shared vs M365-group mailbox Graph subscription + Exchange `ApplicationAccessPolicy` model; `GraphSubscriptionManager` delta (FR-15 sizing). *(opus/high; has escalation trigger.)*
- **051** Implement shared/group mailbox capture coverage (gated on 050 finding) (FR-15).

### Phase 6 — Integration & wrap
- **060** Deploy BFF + Dataverse to `spaarkedev1`.
- **061** Operator browser UAT — success criteria 1–9.
- **090** Project wrap-up — status Complete, `/test-diet`, lessons-learned.

---

## 6. Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **P0** | 001 | — | Read-only prerequisite verification (gates Phase 1) |
| **P1** | 010, 011, 012, 013 | 001 | Distinct surfaces (010 = 1 BFF file; 011/012/013 = schema/config) — parallel |
| **P2-assoc** | 020 → 021 | 001 | Shared `Engine/` path; serial (021 narrows the rung's mapper) |
| **P2-triage** | 022 → 023 → {024, 025} | 001 (022); 011 (025) | 022 catalog; 023/024/025 enrichment `.cs` serial; runs parallel to P2-assoc (different files) |
| **P3** | 030 → {031, 032} | 020, 022, 001 (`sprk_emailupdatefield`) | Job B; endpoints shared → serial after 030 |
| **P4** | 040 → 041; 042 | 020, 022 | 041 grounds on 040's create-task path; 042 parallel |
| **P5** | 050 → 051 | — | **Fully parallel to P1–P4** (independent capture track); 051 gated on 050 finding |
| **P6** | 060 → 061 → 090 | P1–P5 complete | Deploy → UAT → wrap |

**Max concurrency**: 6 agents/wave. **BFF writers to shared `Services/Communication/` are `parallel-safe: false`** among each other — never run two concurrently; `/conflict-check` before each PR. `.claude/`-touching + wrap-up (090) run **main-session**.

**Model tiers (per CLAUDE.md §8.5)**: default **sonnet @ high**. **opus** on 020, 030, 031, 041, 042, 050. **effort xhigh** on 020, 042. Schema/config tasks **sonnet @ medium**; 001/061/090 low/MINIMAL.

---

## 7. Critical Path

```
001 → 020 → 021                         (association + C-1 auto-file)
001 → 022 → 023 → 024 → 025             (triage spine + RI-confidence + persist)   [parallel to assoc]
{020,022} → 030 → 031 → 060 → 061 → 090 (Job B FULL — the deep serial track)
{020,022} → 040 → 041 ; 042             (Job C + intent — parallel to Job B)
[parallel throughout] 050 → 051         (FR-15 capture — independent)
```

The genuine serial spine: **001 → 020 → 030 → 031 → 060 → 061 → 090** (Job B is the deepest track). The triage track (022→023→024→025), the association-mapper (021), Job C + intent (040/041/042), and the FR-15 capture track (050→051) all run in parallel after their prerequisites. FR-15 is fully independent of Phases 1–4.

---

## 8. High-Risk / Watch Items

- **020 (7-entity identifier rung)** — high blast radius on association correctness; bare-numeric identifiers never auto-file alone (need sender/participant reinforcement); multi-entity → `Ambiguous`; AI-tier never auto-files. Reads `sprk_recordtype_ref` **defensively** (typos). Report per-message query count (NFR-08).
- **030/031 (Job B)** — record-mutating; MUST human-confirm, cite, audit, allow-list; apply under confirming user OBO (NFR-05); verify cited text exists (NFR-06); nothing deadline-bearing/privilege-adjacent auto-finalizes (ADR-015).
- **041 (attachment-grounded extraction)** — highest-difficulty AI; heaviest eval-case obligation (NFR-07); deterministically gated to action-triggers for cost.
- **050 (FR-15 spike)** — carries an escalation trigger; implementation (051) is **gated** on the finding (shared vs group mailbox permission model). Legitimate stop per root §6.
- **Shared `Services/Communication/` + `Services/Ai/PublicContracts/`** — `/conflict-check` before every BFF PR; ADR-041/043/047 in-flight (pin to current shape). NFR-04: triage/RI/proposal MUST NOT fail capture or send path.
- Every BFF-touching task: `/conflict-check` + publish-size + CVE + tests obligation (root §10).

## 9. FR Coverage

FR-01→020 · FR-02→010 · FR-03→021 · FR-04→024 · FR-05→022,023 · FR-06→023 · FR-07→011,025 · FR-08→012,031 · FR-09→030 · FR-10→031 · FR-11→001 (verify),030 (reads) · FR-12→042 · FR-13→041 · FR-14→040 · FR-15→050,051 · FR-16→013 · FR-17→032. NFRs distributed (NFR-01/02 every BFF task; NFR-03 §10 gate; NFR-04 enrichment tasks; NFR-05→031; NFR-06→030/031; NFR-07→023/041; NFR-08→020).

## 10. References

- [`spec.md`](spec.md) · [`design.md`](design.md) §0 · [`notes/email-intelligence-r1-coordination.md`](notes/email-intelligence-r1-coordination.md)
- Root `CLAUDE.md` §10 (BFF Hygiene) + §11 (Component Justification) + §6.5 (ADR Conflict Resolution)
- `.claude/constraints/bff-extensions.md` · `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`
- `projects/INDEX.md` (hot-path registry) · siblings `email-communication-solution-r4/r5` + `spaarke-notification-spine-r1`
