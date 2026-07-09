# Project Plan: Spaarke Compose R2

> **Last Updated**: 2026-07-08
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Activate Compose's differentiation layer — five AI actions, inline redline, Word-native interop, memory continuity, and three working entry paths — on the R1 foundation, without reintroducing anything PR #544 retired.

**Scope**:
- 5 Action+Binding AI-action rows dispatched through the shipped seam (zero new endpoints)
- Inline AI toolbar + custom marks + pending redline + undo/replace + serial queue
- Entry paths 1a/1b wired; create-on-save at full ingestion parity
- LLM editing patterns (validator, batch, transaction, semantic appendix)
- Word push/pull + SPE webhook + return-from-Word re-anchoring
- Session memory (anchored annotations, workspace MemoryItems, ledger-query history, provenance/trace)

**Timeline**: Estimated effort **~35–45 developer-days** across 9 phases (spikes ~4.5d). **Sequencing note**: Phases 4 (catalog) and parts of 1/3/5/6 (draft-into-editor, pending-redline, completion cards, memory writes) are **gated on core R2 Phase A0**. The independent tracks (spikes, LLM-pattern BFF services, Word DOCX shuttle, entry-point wiring, create-on-save) are ~20–25 days of work that can proceed immediately.

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-039**: ONE dispatch protocol; Action+Binding as the only routing config; no new dispatch endpoint; no second intent mechanism.
- **ADR-040**: storage-precedes-rendering; edits are ledger `SessionOutput`s (`compose` disposition) before render; undo = supersession; no parallel session store.
- **ADR-013** (Tier-1 NetArchTest): `Services/Compose/` never injects AI internals — reach AI only via the HTTP seam + core contracts.
- **ADR-001**: Minimal API; the SPE webhook renewal is a `BackgroundService`, not a Function.
- **ADR-007**: no `Microsoft.Graph` types above the `SpeFileStore`/Infrastructure facade.
- **ADR-009**: Redis-first for webhook/etag/re-anchor state.
- **ADR-030**: PaneEventBus typed discriminants, no `any` payloads.
- **ADR-021 / ADR-028**: Fluent v9 + dark mode for new UI; `@spaarke/auth` for client fetches.
- **ADR-033**: `compose`-disposition SSE rides `ChatInvocationContext.DocumentStreamWriter`.
- **ADR-029 / §10 BFF hygiene**: publish ≤ 60 MB; per-task size + CVE checks; Placement Justification.

**From Spec**:
- Persistence unit = the Spaarke Document (SPE file + `sprk_document` + profile + indexing); a bare row is never success (R5-E bar).
- Context pane is audit-only (never an input surface).
- Inline redline is the output-staging surface (Word-style); confirmation lives in the Assistant.
- Re-anchor confidence bands: ≥0.85 auto / 0.6–0.85 flag / <0.6 orphan.

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Dispatch via shipped session-dispatch seam | ADR-039; PR #544 killed the parallel endpoint | Zero new dispatch endpoints |
| Create-on-save (mount transient → create on Save) | Owner review #2; renders from upload bytes, no SPE pointer needed | Reframes §2.7(d); upgrades `PromoteIfEphemeralAsync` |
| Regenerate `.docx` from editor on save (original if unedited) | As-built `tipTapToDocxBytes`; matches §1.6 fidelity boundary | FR-06a upload-fidelity branch |
| Container from user's business unit; parent association optional (prompted) | Owner interview | Create-on-save resolves container like new matter/project |
| 5 Action+Binding pairs (not playbooks) | Engine frozen; ADR-039 | Catalog authoring, not playbook authoring |

### Discovered Resources

**Applicable Skills**: `jps-action-create`, `jps-validate` (catalog rows) · `dataverse-create-schema`/`dataverse-deploy` (Action/Binding) · `fluent-v9-component` (UI) · `code-page-deploy` (SpaarkeAi) · `bff-deploy` · `spe-integration` (webhooks) · `ui-test` (flagship gate) · `add-reference-to-index` · `code-review` · `adr-check`.

**Knowledge / Guides**: `SPAARKEAI-WORKSPACE-ARCHITECTURE.md`, `LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md`, `BUILD-A-NEW-WORKSPACE-WIDGET.md`, `.claude/constraints/bff-extensions.md`, `docs/adr/ADR-039/040`.

**Reusable Code** (verified in-repo):
- `ComposeService.PromoteIfEphemeralAsync` — create-on-save backbone (extend to full parity)
- `ComposeEditor` `docxBytes` seam + `docxBridge` (`docxToTipTapHtml` / `tipTapToDocxBytes`) — mount + save
- `ComposeEndpoints.cs` — endpoint group (add annotation/webhook/validate routes)
- `SendWorkspaceArtifactHandler` — chat→Compose pre-seed bridge (flip upload refusal)
- `StaleCheckoutSweeperHostedService` — `BackgroundService` pattern for the webhook-renewal cron
- `dispatchConsumer` → `POST /api/ai/chat/sessions/{id}/dispatch` — the dispatch seam

---

## 3. Implementation Approach

### Phase Structure

```
Phase 0: Spikes & de-risking          (no core dep — START NOW)
Phase 1: Entry paths & lifecycle       (mostly no core dep; draft-into-editor GATED)
Phase 2: LLM editing patterns (BFF)    (no core dep — START NOW)
Phase 3: Inline editing UX             (partial — pending-redline/undo GATED)
Phase 4: AI catalog (5 pairs)          (GATED on core Phase A0)
Phase 5: Word interop (DOCX shuttle)   (no core dep; completion cards GATED)
Phase 6: Memory & provenance           (partial — memory.write/trace GATED)
Phase 7: Coordination + Doc Q&A        (partial)
Phase 8: Integration, flagship gate, wrap-up
```

### Critical Path

- **Core Phase A0 publication** is the single biggest external gate. It blocks: Phase 4 (all), Phase 1 draft-into-editor, Phase 3 pending-redline + undo/replace, Phase 5 completion cards, Phase 6 memory writes + trace hosting.
- **Independent critical path** (no core dep): Phase 0 spikes → Phase 2 LLM-pattern services + Phase 1 entry-paths/create-on-save → Phase 5 DOCX shuttle. These fill the wait.
- The **flagship gate (Phase 8)** requires the full assistant-driven chain, so it cannot pass until the core-gated legs land.

**High-Risk Items:**
- Core R2 Phase A0 timing unknown (core has no worktree yet). Mitigation: sequence all core-gated tasks behind an explicit `blocked-on: core-A0` marker; do not start them speculatively.
- `@spaarke/legal-workspace` package extraction (dataset-grid-framework-r2, PR #537) — merge-order coordination for Context-pane sections.
- Publish-size brushes the ≥55 MB architecture-review trigger. Mitigation: PDB-exclusion lever; measure per BFF task.

---

## 4. Phase Breakdown

### Phase 0 — Spikes & De-risking  *(no core dependency — start immediately)*

**Objectives:** validate the shipped dispatch path for a Compose Binding; prove the LLM-pattern + DOCX-shuttle approaches before building on them.

**Deliverables:**
- [ ] Spike 0: validate session-dispatch path with a throwaway `compose-explain-clause` Binding (PaneEventBus → ConversationPane → `dispatchConsumer` → `/dispatch` → SSE + ledger write)
- [ ] Spike 1: one Action row + adeu-style prompt + structured `OutputSchemaJson` returns schema-valid JSON reliably
- [ ] Spike 2: `IComposeEditValidator` `match_mode` + structured ambiguity errors (5 sample edits)
- [ ] Spike 3: `ComposeEditBatch` 4-phase pipeline + rollback atomicity
- [ ] Spike 4: `SemanticAppendixGenerator` hallucination-delta measurement
- [ ] Spike 5: Open XML SDK writes `<w:ins>`+`<w:comment>` → SPE → Word for Web renders natively
- [ ] Spike 6: reverse round-trip (Word edit → webhook → SDK read w/ author/date); informs re-anchor bands
- [ ] Spike 7: SPE checkout vs Word-for-Web open collision UX
- [ ] Spike 8: MCP stub + `docx-benchmark` harness baseline

**Outputs**: spike notes in `notes/spikes/`; validated approach for Phases 1–5.

### Phase 1 — Entry Paths & Document Lifecycle  *(mostly no core dep; draft-into-editor GATED)*

**Objectives:** make all three entry paths mount a file; implement create-on-save at full ingestion parity.

**Deliverables:**
- [ ] FR-01: wire 1a "Browse / open file" → file picker → transient mount (`docxBytes` seam)
- [ ] FR-02: wire 1a "Search for Document" → Document lookup → reuse 1c load path
- [ ] FR-03: 1b upload transient mount — flip `send_workspace_artifact` refusal; feed retained bytes to editor; activate `POST /api/compose/upload`
- [ ] FR-05: create-on-save at full parity — extend `PromoteIfEphemeralAsync` (container from business unit; `sprk_document`; profile analysis; indexing; optional parent-association prompt); per-step OutcomeCard *(OutcomeCard rendering GATED on core `JobAwareCompletionState`)*
- [ ] FR-06a: upload fidelity branch (original if unedited; regenerate if edited)
- [ ] **FR-04 (GATED — core `compose` disposition)**: draft-into-editor from chat
- [ ] **FR-06 (partial GATED)**: save-back + open-in-Compose chain

**Dependencies:** create-on-save reuses existing SPE/Dataverse plumbing; **confirm the business-unit→container API** (spec Unresolved Q#2) during this phase.

### Phase 2 — LLM Editing Patterns (BFF)  *(no core dependency — start immediately)*

**Objectives:** build the deterministic edit engine adeu's patterns inform.

**Deliverables:**
- [ ] FR-19: `IComposeEditValidator` (`match_mode` + structured ambiguity errors) + `POST /api/compose/edit-batch/validate`
- [ ] FR-20: `ComposeEditBatch` (resolve → sort-desc → skip-overlap → apply-bottom-up)
- [ ] FR-21: `ComposeEditTransaction` (snapshot/rollback)
- [ ] FR-22: `SemanticAppendixGenerator` + CriticMarkup read-direction rendering
- [ ] FR-23: enriched `compose-selection`/`compose-document` scope descriptions (prompt = description; double as tooltips)
- [ ] NFR-05/08: unit tests + NetArchTest facade compliance for each service

### Phase 3 — Inline Editing UX  *(partial — pending-redline/undo GATED)*

**Deliverables:**
- [ ] FR-14: TipTap `BubbleMenu` inline AI toolbar (Explain/Compare/Draft/More; extensible overflow)
- [ ] FR-15: custom ProseMirror marks (`insertion`, `deletion`, `commentAnchor`)
- [ ] FR-18: serial action queueing in `ConversationPane`
- [ ] **FR-16 (GATED)**: pending track-change materialized from the ledger with `{bindingId}@t{n}` provenance
- [ ] **FR-17 (GATED)**: undo/replace via ledger supersession

### Phase 4 — AI Catalog (5 Action+Binding pairs)  *(GATED on core Phase A0)*

**Objectives:** author the five capabilities as catalog rows dispatched through the seam.

**Deliverables:**
- [ ] FR-07: `compose-explain-clause` Action + Binding
- [ ] FR-08: `compose-compare-to-playbook` Action + Binding (reads `sprk_analysisplaybook` as reference data)
- [ ] FR-09: `compose-draft-alternative` Action + Binding (declares `compose` disposition; structured edit payload)
- [ ] FR-10: `compose-summarize-word-changes` Action + Binding
- [ ] FR-11: `compose-defined-terms` Action + Binding (overflow-menu trigger → Context-pane read-only output)
- [ ] FR-12: eval cases per row (golden + dispatch, ≥5 each) + `OpenAiFunctionSchemaValidator` compliance (no property-level boolean `required`)
- [ ] FR-13: dispatch wiring (PaneEventBus `compose_action_request` → `ConversationPane` → `dispatchConsumer`)

**Dependencies:** core triple-twin hoist (author rows *through* it); core `invoke`/dispatch seam; eval-gate.

### Phase 5 — Word Interop (DOCX Shuttle)  *(no core dep; completion cards GATED)*

**Deliverables:**
- [ ] FR-24: `DocxAnnotationWriter` (comments + track changes; ordering + paragraph-boundary handling) + `POST /push-annotations`
- [ ] FR-25: `DocxAnnotationReader` + `POST /pull-annotations`
- [ ] FR-26: SPE webhook subscription (`BackgroundService` renewal) + delta query + `POST /webhooks/spe-doc-changed` + `POST /check-changes`
- [ ] FR-27: return-from-Word re-anchoring (confidence bands ≥0.85/0.6–0.85/<0.6) + conflict banner
- [ ] **FR-28 (partial GATED)**: confirm/completion/trace three surfaces (gate Tier 2c dialog / OutcomeCard / Context audit)

### Phase 6 — Memory & Provenance  *(partial — memory.write/trace GATED)*

**Deliverables:**
- [ ] FR-29: anchored annotations in the Compose session payload (document-adjacent; tenant/matter scoped) — no core dep
- [ ] FR-31: action history via ledger queries (no duplicate structure) — no core dep
- [ ] FR-33: compaction over ledger + cross-version persistence (`DocumentId + MatterId`)
- [ ] **FR-30 (GATED)**: workspace-scope MemoryItems via gated `memory.write`
- [ ] **FR-32 (GATED)**: Context-pane always-visible provenance + D-F4 trace hosting

### Phase 7 — Three-Pane Coordination + Doc Q&A  *(partial)*

**Deliverables:**
- [ ] **FR-34 (partial GATED — D-F3 ack)**: activate the six coordinated flows + UI-ack-on-frame-id
- [ ] FR-35 (stretch): Document Q&A over the session-mounted document (agent loop + citations)

### Phase 8 — Integration, Flagship Gate & Wrap-up

**Deliverables:**
- [ ] Flagship gate (G-R2-C) browser-verified on spaarkedev1 in one conversation
- [ ] Publish-size ≤ 60 MB measure + CVE scan + NetArchTest green (NFR-01/02/05)
- [ ] File R1 spec.md amendment (two Word-native non-goals → "shipped in R2")
- [ ] `AnchoredAnnotation` Path-A deviation code-review sign-off
- [ ] `/test-diet` reconciliation + wrap-up (090-project-wrap-up)

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Core R2 Phase A0 contracts | **Not started** (no worktree) | **High** | Mark all core-gated tasks `blocked-on: core-A0`; run independent tracks first; confirm timing with core project |
| Open XML SDK 3.x + OpenXmlPowerTools (MIT) | GA | Low | Standard NuGet; publish-size measured per task |
| SPE Webhook subscriptions (Graph) | GA | Low | Renewal `BackgroundService`; delta fallback poll (`/check-changes`) |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| R1 + redesign-r1 as-built (Compose service, layout, bridge, ledger) | `src/**` (master) | Merged |
| `@spaarke/legal-workspace` package extraction | dataset-grid-framework-r2 (PR #537) | In-flight — merge-order coordinate |
| `dispatchConsumer` + session-dispatch seam | `Services/Ai/**` | Shipped |

---

## 6. Testing Strategy

**Unit Tests** (per NFR-08): every new `Services/Compose/` service (validator, batch, transaction, appendix, DOCX writer/reader, sync orchestrator) → matching tests in `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/`. Mock at module boundaries (ADR-038); no `Mock<HttpMessageHandler>`.

**Integration Tests**: create-on-save full-parity chain (container → record → profile → indexing); Word push/pull round-trip; return-from-Word re-anchoring.

**Eval Tests** (NFR-06): golden + dispatch families, ≥5 each, per catalog row; gate to merge.

**E2E / UI Tests**: the flagship gate chain (browser, spaarkedev1); Fluent v9 dark-mode checks (ADR-021) for BubbleMenu toolbar, empty-state buttons, OutcomeCard, Context sections, conflict banner.

---

## 7. Acceptance Criteria

Mirror of README graduation criteria + spec Success Criteria §. Verified per-phase; the flagship gate (§8) is the release gate. Each acceptance item cites its FR.

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Core Phase A0 slips → catalog + draft-into-editor stall | High | High | Sequence independent tracks first; explicit `blocked-on: core-A0`; confirm timing |
| R2 | Publish-size crosses 55 MB review trigger | Medium | Medium | PDB-exclusion lever; per-task measurement |
| R3 | `@spaarke/legal-workspace` extraction conflicts | Medium | Medium | Merge-order coordinate with dataset-grid-framework-r2 |
| R4 | Uploaded-file save loses formatting (regenerate path) | Medium | Low | FR-06a preserves original if unedited; §1.6 sets expectation |
| R5 | Business-unit→container API unknown | Medium | Medium | Resolve in Phase 1 (spec Unresolved Q#2) |
| R6 | SPE webhook reliability | Medium | Low | `/check-changes` poll fallback |

---

## 9. Next Steps

1. **Review this plan.md** — confirm phase sequencing + core-gated markers.
2. **Run** `/task-create projects/spaarkeai-compose-r2` to decompose into POML task files with `blocked-on: core-A0` dependency markers.
3. **Begin Phase 0 spikes + Phase 2 LLM-pattern services** (no core dependency) once tasks exist and the branch is synced.

---

**Status**: Ready for Tasks
**Next Action**: `/task-create projects/spaarkeai-compose-r2`

---

*For Claude Code: This plan sequences work around the core R2 Phase A0 dependency. Independent tracks (Phase 0/2/5, entry paths, create-on-save) proceed immediately; core-gated tracks (Phase 4 + parts of 1/3/5/6/7) wait for published contracts.*
