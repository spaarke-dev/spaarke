# Spaarke Compose R2 — Task Index

> **Created**: 2026-07-08 · **Source**: [plan.md](../plan.md) · **Spec**: [spec.md](../spec.md)
> **Legend**: Status 🔲 not-started · 🔄 in-progress · ✅ complete · ⛔ blocked-on-core-A0
> **Gating**: 🟢 startable now (no core dep) · 🔴 blocked on core Phase A0 · 🟡 splittable (independent half startable)

## Core-A0 dependency (read first)

Tasks marked ⛔ are **blocked until core R2 publishes Phase A0 contracts** (see [notes/HANDOFF-core-r2-A0-contract-requirements.md](../notes/HANDOFF-core-r2-A0-contract-requirements.md)). Do NOT author their implementation against guessed contract shapes. The 🟢 tasks (spikes, LLM services, entry paths, create-on-save pipeline, DOCX shuttle, independent memory) are ~20–25 days of work startable immediately.

## Task Roster

| ID | Title | Phase | Gate | Deps | Status | Rigor | Model | Effort |
|----|-------|-------|------|------|--------|-------|-------|--------|
| 000 | Spike 0 — validate session-dispatch path (throwaway compose Binding) | 0 Spikes | 🟢 | none | ✅ | STANDARD | opus | high |
| 001 | Spike 1 — Action row + structured OutputSchemaJson reliability | 0 Spikes | 🟢 | 000 | 🔲 | STANDARD | sonnet | high |
| 002 | Spike 2 — edit validator match_mode + ambiguity errors | 0 Spikes | 🟢 | none | ✅ | STANDARD | sonnet | high |
| 003 | Spike 3 — atomic edit batch + rollback | 0 Spikes | 🟢 | none | ✅ | STANDARD | sonnet | high |
| 004 | Spike 4 — semantic appendix hallucination delta | 0 Spikes | 🟢 | none | ✅ | STANDARD | sonnet | high |
| 005 | Spike 5 — Open XML write w:ins+w:comment → Word for Web | 0 Spikes | 🟢 | none | ✅ | STANDARD | sonnet | high |
| 006 | Spike 6 — reverse round-trip (Word edit → webhook → read); tune re-anchor bands | 0 Spikes | 🟢 | 005 | ✅ | STANDARD | sonnet | high |
| 007 | Spike 7 — SPE checkout vs Word-for-Web open collision UX | 0 Spikes | 🟢 | none | ✅ | STANDARD | sonnet | high |
| 008 | Spike 8 — docx-benchmark harness baseline | 0 Spikes | 🟢 | 005 | 🔲 | STANDARD | sonnet | high |
| 010 | FR-01 wire 1a "Browse / open file" → transient mount | 1 Entry | 🟢 | 000 | ✅ | FULL | sonnet | high |
| 011 | FR-02 wire 1a "Search for Document" → reuse 1c load path | 1 Entry | 🟢 | none | ✅ | FULL | sonnet | high |
| 012 | FR-03 1b upload → transient mount (flip send_workspace_artifact + feed bytes) | 1 Entry | 🟢 | none | ✅ | FULL | opus | high |
| 013 | FR-05 create-on-save pipeline — extend PromoteIfEphemeralAsync (container/record/index; profile→core) | 1 Entry | 🟢 | none | ✅ | FULL | opus | xhigh |
| 014 | FR-05 optional parent-association prompt (Tier 2c dialog integration) | 1 Entry | 🟡 | 013 | 🔲 | FULL | sonnet | high |
| 015 | FR-06a upload fidelity branch (original-if-unedited) | 1 Entry | 🟢 | 013 | 🔲 | FULL | sonnet | high |
| 016 | FR-04 draft-into-editor via compose disposition | 1 Entry | 🟢 | 000 | 🔲 | FULL | opus | high |
| 017 | Deploy — BFF + SpaarkeAi (entry paths) + verify 1a/1b/1c mount | 1 Entry | 🟢 | 010,011,012,013,015 | 🔲 | STANDARD | sonnet | high |
| 020 | FR-19 IComposeEditValidator + POST /edit-batch/validate | 2 LLM | 🟢 | 002 | ✅ | FULL | sonnet | xhigh |
| 021 | FR-20 ComposeEditBatch (4-phase pipeline) | 2 LLM | 🟢 | 003 | ✅ | FULL | sonnet | high |
| 022 | FR-21 ComposeEditTransaction (snapshot/rollback) | 2 LLM | 🟢 | 021 | ✅ | FULL | sonnet | high |
| 023 | FR-22 SemanticAppendixGenerator + CriticMarkup read direction | 2 LLM | 🟢 | 004 | ✅ | FULL | sonnet | high |
| 024 | FR-23 enriched compose-selection/compose-document scope descriptions | 2 LLM | 🟢 | none | ✅ | STANDARD | sonnet | high |
| 025 | Unit tests for Services/Compose LLM services (NFR-05/08 + NetArchTest) | 2 LLM | 🟢 | 020,021,022,023 | ✅ | STANDARD | sonnet | high |
| 030 | FR-14 TipTap BubbleMenu inline AI toolbar (Explain/Compare/Draft/More) | 3 Inline | 🟢 | none | ✅ | FULL | sonnet | high |
| 031 | FR-15 custom ProseMirror marks (insertion/deletion/commentAnchor) | 3 Inline | 🟢 | none | ✅ | FULL | sonnet | high |
| 032 | FR-18 serial action queueing in ConversationPane | 3 Inline | 🟢 | none | 🔲 | FULL | sonnet | high |
| 033 | FR-16 pending track-change materialization from ledger (compose disposition) | 3 Inline | 🟢 | 031,016 | ✅ | FULL | opus | high |
| 034 | FR-17 undo/replace via ledger supersession | 3 Inline | 🟢 | 033 | 🔲 | FULL | opus | high |
| 040 | FR-07 compose-explain-clause Action + Binding | 4 Catalog | 🟢 | 001 | ✅ | FULL | sonnet | high |
| 041 | FR-08 compose-compare-to-playbook Action + Binding | 4 Catalog | 🟢 | 001 | ✅ | FULL | sonnet | high |
| 042 | FR-09 compose-draft-alternative Action + Binding (compose disposition) | 4 Catalog | 🟢 | 001,016 | ✅ | FULL | opus | high |
| 043 | FR-10 compose-summarize-word-changes Action + Binding | 4 Catalog | 🟢 | 001,006 | ✅ | FULL | sonnet | high |
| 044 | FR-11 compose-defined-terms Action + Binding (overflow trigger → Context) | 4 Catalog | 🟢 | 001 | ✅ | FULL | sonnet | high |
| 045 | FR-12 eval cases per row (golden + dispatch ≥5) + schema validation | 4 Catalog | 🟢 | 040,041,042,043,044 | ✅ | FULL | sonnet | high |
| 046 | FR-13 dispatch wiring (compose_selection_offer choreography + direct dispatchConsumer) | 4 Catalog | 🟢 | 016,030 | 🔲 | FULL | opus | high |
| 047 | Deploy catalog rows to Dataverse (mirror-first) | 4 Catalog | 🟢 | 045 | 🔲 | STANDARD | sonnet | high |
| 050 | FR-24 DocxAnnotationWriter (comments + track changes, edge cases) + push endpoint | 5 Word | 🟢 | 005 | ✅ | FULL | opus | xhigh |
| 051 | FR-25 DocxAnnotationReader (parse w:comment/w:ins/w:del) + pull endpoint | 5 Word | 🟢 | 006 | ✅ | FULL | sonnet | high |
| 052 | FR-26 SPE webhook subscription + BackgroundService renewal + delta query | 5 Word | 🟢 | none | ✅ | FULL | opus | high |
| 053 | FR-26 webhooks/spe-doc-changed + check-changes endpoints | 5 Word | 🟢 | 052 | 🔲 | FULL | sonnet | high |
| 054 | FR-27 return-from-Word re-anchoring (bands ≥0.85/0.6–0.85/<0.6) + conflict banner | 5 Word | 🟢 | 051,006 | 🔲 | FULL | opus | high |
| 055 | FR-28 push/save deterministic path (gate dialog + OutcomeCard = splittable) | 5 Word | 🟡 | 050 | 🔲 | FULL | sonnet | high |
| 056 | Deploy + Word for Web round-trip verification (Spikes 5/6 as gate) | 5 Word | 🟢 | 050,051,053,054 | 🔲 | STANDARD | sonnet | high |
| 060 | FR-29 anchored annotations in Compose session payload (doc-adjacent) | 6 Memory | 🟢 | none | 🔲 | FULL | sonnet | high |
| 061 | FR-31 action history via ledger queries (no duplicate structure) | 6 Memory | 🟢 | none | ✅ | FULL | sonnet | high |
| 062 | FR-33 compaction over ledger + cross-version persistence (DocumentId+MatterId) | 6 Memory | 🟢 | 061 | 🔲 | STANDARD | sonnet | high |
| 063 | FR-30 workspace-scope MemoryItems via gated memory.write | 6 Memory | 🔴 | none | ⛔ | FULL | sonnet | high |
| 064 | FR-32 Context-pane provenance + D-F4 trace hosting | 6 Memory | 🔴 | none | ⛔ | FULL | sonnet | high |
| 070 | FR-34 activate six coordinated flows (PaneEventBus choreography) | 7 Coord | 🟡 | 030,031 | 🔲 | FULL | opus | high |
| 071 | FR-34 D-F3 UI ack-on-frame-id | 7 Coord | 🔴 | 070 | ⛔ | FULL | sonnet | high |
| 072 | FR-35 Document Q&A over session-mounted document (stretch) | 7 Coord | 🟢 | none | 🔲 | STANDARD | sonnet | high |
| 080 | R1 spec.md amendment (two Word-native non-goals → "shipped in R2") | 8 Wrap | 🟢 | none | ✅ | MINIMAL | sonnet | high |
| 081 | Publish-size ≤60 MB + CVE scan + NetArchTest facade verification (NFR-01/02/05) | 8 Wrap | 🟢 | 025,050,052 | 🔲 | STANDARD | sonnet | high |
| 082 | Flagship gate G-R2-C — browser-verified full chain on spaarkedev1 | 8 Wrap | 🔴 | 016,033,042,046,047,014,055 | ⛔ | FULL | opus | high |
| 083 | AnchoredAnnotation Path-A deviation code-review sign-off | 8 Wrap | 🟢 | 060 | 🔲 | MINIMAL | sonnet | high |
| 090 | Project wrap-up (code-review, adr-check, repo-cleanup, /test-diet, lessons) | 8 Wrap | 🔴 | all | ⛔ | FULL | opus | high |

**Totals**: 56 tasks — **33 🟢 startable** · **16 🔴 core-A0-blocked** · **7 🟡 splittable** (splittable are counted among startable; their gated half joins the blocked set). Task 045 rigor bumped STANDARD→FULL per CLAUDE.md §8 TEST-MODIFYING override (it modifies `tests/**`).

## Parallel Execution Plan (startable 🟢 tracks)

> Waves list only startable tasks. 🔴 tasks join the plan when core A0 publishes. `.claude/` boundary: none of these touch `.claude/` (all main-session-or-subagent-safe under `projects/` + `src/`), but file-overlap within `src/` is enforced below.

| Wave | Tasks | Prereq | File-overlap notes | goal-eligible |
|------|-------|--------|--------------------|---------------|
| **W0 Spikes** | 000, 002, 003, 004, 005, 007 | none | separate spike notes; 000 opus | NO (exploratory, no crisp end-state) |
| **W0b Spikes** | 001, 006, 008 | 000/005 | — | NO |
| **W1 BFF services** | 020, 021, 023, 024, 052 | W0 | separate `Services/Compose/*` files | YES (build+tests verifiable, ≥3 tasks, well-specified) |
| **W1b UI foundations** | 030, 031, 032 | none | separate `ComposeEditor`/`ConversationPane` regions — **serialize 030/031 (both touch ComposeEditor)** → 030 then 031; 032 parallel | NO (frontend, no headless verify) |
| **W2 Entry + engine** | 010, 011, 012, 013, 022 | W1 | 010/011/012 touch `ComposeWorkspace`+`ComposeEmptyState` — **serialize** or split by handler; 013 BFF-only | partial |
| **W2b** | 015, 060, 061, 072 | 013 / none | separate | YES |
| **W3 Word** | 050, 051, 053, 054, 055 | W1 (052), spikes | 050/051 separate writer/reader; 053 endpoints; 054 anchoring | YES |
| **W4 Integration** | 014, 017, 056, 062, 080, 081, 083 | phase deps | deploy + verify + docs | partial |

**Concurrency cap**: 6 agents/wave. **Build gate between waves**: `dotnet build src/server/api/Sprk.Bff.Api/` (any `.cs`) + `npm run build` in affected package (any `.ts/.tsx`).

## Blocked-on-core-A0 (⛔) — authored/executed post-A0

016 UNBLOCKED (A0 ComposeDisposition landed 2026-07-08). Core task **020 triple-twin hoist published to master** 2026-07-09 (`78073ae03`; SEAM-STATUS row 020 = ✅ published) unblocked the catalog. **040/041/043/044 DONE 2026-07-09** (read Actions + Bindings authored mirror-first under `infra/dataverse/actions|inputschemas|outputschemas` + `sprk_playbookconsumer-rows.json`; deploy = task 047; eval cases = task 045). Remaining startable catalog: **045** (eval cases, dep 040-044), **046** (dispatch wiring, deps 016/030/031 done), **047** (deploy, dep 045). **Compose-disposition ROUTING PROMOTION applied by compose-r2 2026-07-09** (`BindingDisposition.Compose` + `ToLedgerValue` + `OutputRouter` pass-through case — core task 010 published only the CONTRACT and left this unscheduled; compose-r2 applied it, 31 router/compose tests green). This **UNBLOCKS 042** (draft-alternative), **033** (pending-redline materialization), **034** (undo/replace). Core 037 (UI-ack ✅) + 032 (gate engine ✅) landed 2026-07-09 → **071** unblocked (dep 070). Still core-gated: **063** (core 057 memory.write), **064** (core 038 D-F4 view); **082/090** terminal. See SEAM-STATUS.md.

These get full POML authoring finalized once core A0 contract shapes are confirmed (the handoff doc is the input). 082 (flagship gate) + 090 (wrap-up) are terminal — they require the full chain.

## How to execute a wave

1. Confirm all prereqs are ✅ and (for 🔴) core A0 published.
2. `task-execute` each task; run parallel waves as one message with multiple invocations (respect file-overlap serialization above).
3. Build-gate between waves; update this index's Status column.
