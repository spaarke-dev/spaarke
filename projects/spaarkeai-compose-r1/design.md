# Spaarke Compose — Design (Working Document)

> **Status**: DRAFT — refinement document. Not yet a committed spec.
> **Codename**: Spaarke Compose
> **Positioning**: AI-native legal drafting workspace
> **Project ID**: `spaarkeai-compose-r1`
> **R1 scope**: Compose workspace layout + three-pane wiring + SPE plumbing + reuse of existing chat-session infrastructure + Compose consumer-routing foundation. AI action depth, DOCX subset enforcement, and rich session-memory content ship in R2+.
> **Owner**: Ralph Schroeder
> **Last updated**: 2026-06-29
> **Companion doc**: [`Spaarke-AI-Document-Workspace-Solution-Concept.md`](./Spaarke-AI-Document-Workspace-Solution-Concept.md) — original concept (preserved)

This document is the working refinement of the concept. It captures the full Compose vision AND narrows R1 scope to what this specific project delivers. When this stabilizes, it informs `spec.md` and the project plan.

---

## 1. Product Statement

**Spaarke Compose is the AI-native legal drafting workspace** — the center pane of the SpaarkeAi three-pane shell, coordinated with the Assistant (left) and Context (right) panes.

### Positioning

Compose and Microsoft Word are **two surfaces, each doing its own job**:

- **Compose**: AI-native drafting + matter intelligence in the browser. Where lawyers do *AI-driven legal work* — explain clauses, compare to playbook, draft alternatives, surface precedent, track derived insights against a matter.
- **Word**: best-in-class word processor for advanced formatting, final polish, print/publish output.

This is a **handoff model**, not competition: the file lives in SPE; Compose is the surface for AI-driven legal work; Word is the surface for advanced document craft. The user moves between them as the task demands.

---

## 2. R1 Project Scope (what this project delivers)

### In scope for R1

| # | Deliverable | Notes |
|---|---|---|
| 1 | **Compose workspace layout** | New `sprk_workspacelayout` system record (template: `single-column`, section: `compose-editor`) |
| 2 | **Three-pane wiring** | Assistant ↔ Compose ↔ Context coordination contract — wire the data flows even where downstream features are stubs |
| 3 | **`compose-editor` section** | TipTap-based editor shell mounted in the Workspace pane. **R1 binding: features constrained to TipTap out-of-the-box capabilities** — no custom integration work for advanced features (comments-as-Word-comments, tracked changes, footnotes, field codes, etc.). Anything outside TipTap OOB → "open in Word." |
| 4 | **SPE plumbing** | Load DOCX from SPE; save edits back as new SPE versions; **always** SPE-as-source |
| 5 | **Upload-from-Assistant path** | "I want to edit this" use case — uploads to SPE, opens in Compose (initially without `sprk_document` record) |
| 6 | **Document record creation on Save** | For ephemeral (unbound) docs, the **first Save** creates a `sprk_document` record; indexing then follows the normal Document pipeline |
| 7 | **Chat session reuse** | Compose uses the existing `ChatSession` + Redis/Cosmos/Dataverse three-tier persistence (see §6) — wired with `DocumentId` binding |
| 8 | **Two new JPS scopes** | `compose-selection`, `compose-document` — defined and registered via `jps-scope-refresh`. No new actions in R1. |
| 9 | **Compose consumer-routing foundation** | Define at least one Compose consumer type in [`ConsumerTypes.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs), seed Dataverse row, prove end-to-end dispatch as smoke test |
| 10 | **Compose → Word handoff** | "Open in Word for Web" (new tab) + "Open in Word Desktop" (via `ms-word:ofe\|u\|` protocol) — both via SPE-provided URLs |

### Out of scope for R1 (deferred)

- AI actions on selection (Explain clause / Replace / Compare-to-playbook) — R2
- Return-from-Word round-trip with annotation re-anchoring — R2
- DOCX subset *enforcement* (R1 publishes a draft subset spec; enforcement = R2)
- Office Add-in entry path (Word → Compose) — R3
- PDF / email / transcript artifact types — R4+
- Co-editing (CRDT) — R5+

R1 deliberately delivers foundation, not features. The hardest architectural work is the wiring; the editor and AI features layer on top of correct wiring.

---

## 3. Supersession Map (read this before touching code)

Compose builds on the **current** SpaarkeAi three-pane shell. This table is binding to prevent code-level commingling:

| Retired / superseded | Current (use this) | Project relationship |
|---|---|---|
| `AnalysisWorkspace` solution (removed) | SpaarkeAi three-pane shell ([`src/solutions/SpaarkeAi/`](../../src/solutions/SpaarkeAi/)) | Compose builds ON the current shell |
| `SprkChat` (standalone, retired) | `ConversationPane` ([`src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx`](../../src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx)) | Compose integrates with `ConversationPane`, NOT `SprkChat` |
| `ai-analysis-workspace-sprkchat-integration` project (historical) | This project (`spaarkeai-compose-r1`) | Not a continuation |
| **`sprk_analysis.sprk_chathistory` field (legacy)** | **`ChatSession` model + `sprk_aichatsummary` + Redis/Cosmos/Dataverse three-tier persistence** | Compose persists chat via the modern pattern, NOT via `sprk_analysis` |

`sprk_analysis` itself remains in use as a legacy audit record for analysis-action+output flows — Compose does NOT extend or reuse it. The two abstractions are different: `sprk_analysis` = one action's record; `ChatSession` = ongoing session.

---

## 4. Shell Placement — Compose is a Workspace Layout

Per [`docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../../docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md), every named user surface ("My Work", "Calendar", etc.) is a `sprk_workspacelayout` record rendered through the unified `WorkspaceLayoutWidget → LegalWorkspaceApp (embedded)` pipeline.

**Compose ships as a new workspace layout**:
- `sprk_layouttemplateid`: `single-column` (same as Calendar)
- Section: new section type `compose-editor`, registered in the section registry
- Layout record: hard-coded system layout
- **User-facing label**: "Compose"
- **Multi-tab**: each open document is its own Compose tab (one matter / many documents → many tabs)

The Assistant (left) and Context (right) panes wrap Compose for free.

---

## 5. The Three-Pane Coordination Contract

### Role statements (binding)

- **Assistant** (left pane): the AI agent that *acts* on or around the document. **All AI actions go through the JPS playbook system + consumer-routing dispatch** (see §7). Multi-step actions, orchestrated workflow, conversation memory.
- **Workspace** (center pane): the artifact being *worked on* — Compose drafting surface in R1, extensible to other artifact types in later releases (§15).
- **Context** (right pane): the legal intelligence *around* the artifact — matter, clauses, precedent, history, derived insights; the persistent memory surface.

### Six coordinated flows

| # | Flow | Example | R1? |
|---|---|---|---|
| 1 | **Workspace → Context** | Select a clause → Context surfaces matching precedent, playbook entries, prior negotiation history | Wire only |
| 2 | **Workspace → Assistant** | Select text → Assistant offers "Explain / Replace with standard / Compare / Draft alternative" (playbook actions) | Wire only |
| 3 | **Context → Workspace** | Drag a precedent clause from Context → drops into editor at cursor | Wire only |
| 4 | **Context → Assistant** | "Use this precedent" → Assistant takes it as a tool input | Wire only |
| 5 | **Assistant → Workspace** | Assistant drafts text → inserts with provenance trail | Wire only |
| 6 | **Assistant → Context** | Assistant produces a derived insight → persists to matter knowledge graph | Wire only |

**R1 binding rule**: all six flow data contracts must be defined and wired. Receivers may be stubs. R2 features cannot retrofit data contracts after R1 ships.

---

## 6. Session Persistence — Reuse the Existing Three-Tier Pattern

The Spaarke chat persistence pattern is production-grade and already does everything Compose needs. Compose REUSES it; we do NOT build a new entity.

### The existing three tiers

| Tier | Storage | Purpose | TTL |
|---|---|---|---|
| **Hot** | Redis | Active session — sub-ms reads, pane coordination, AI tool inputs | 24h sliding |
| **Warm** | Cosmos DB | Survives browser refresh + multi-day gaps | 90 days |
| **Cold** | Dataverse `sprk_aichatsummary` + `sprk_aichatmessage` | Long-term audit, GDPR-erasable | Indefinite |

Key services: [`ChatSessionManager.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatSessionManager.cs) (hot tier), [`SessionPersistenceService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionPersistenceService.cs) (warm), [`ChatHistoryManager.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatHistoryManager.cs) (compaction).

### What the existing model already gives us

- **`ChatSession.DocumentId`** — the document↔session pointer (use SPE drive-item id; falls back to `sprk_documentid` once a Document record exists)
- **`HostContext`** — entity-aware metadata (EntityType, EntityId, WorkspaceType); Compose may extend this with `ComposeContext` if needed for editor-specific metadata
- **`PlaybookId`, `UploadedFiles`, `AdditionalDocumentIds`** — already there for free
- **Compaction at 15 messages** — LLM-based summarization, stored in `sprk_summary` field (Claude Code-style retention)
- **Archive at 50 messages** — trim oldest messages, retain summary
- **TenantId isolation** — at Redis key, Cosmos partition, and Dataverse query level (ADR-015 Tier 3)
- **Rehydration**: `ChatSessionManager.GetSessionAsync(tenantId, sessionId, ct)` already cascades hot → warm → cold

### Document state vs Session memory (the differentiator)

> **Document state is versioned. Session memory is continuous.**

- **Document state** = the DOCX bytes (SPE blob) + the TipTap projection (browser memory). Versioned.
- **Session memory** = `ChatSession` instance, bound to `DocumentId` (and optionally `MatterId` via `HostContext`). Persists across document version churn, Compose↔Word handoffs, and sessions.

When the user returns to Compose after editing in Word (a new SPE version exists):
1. Load latest DOCX from SPE into TipTap
2. Existing `ChatSessionManager` loads prior `ChatSession` for that `DocumentId` — chat history, action log, annotations all intact
3. Re-anchor span-bound annotations to closest matching text (R2 work)
4. Banner: "Document updated in Word — N annotations re-anchored, M need your review" (R2 work)

**R1 scope for this section**: wiring + `DocumentId` binding only. Compaction, anchoring, banners = R2.

### Session continuity (the "session history" UX)

Like Claude Code surfacing prior session history when you open a worktree, Compose surfaces prior `ChatSession` instances for a document:

1. User opens Document X in Compose
2. New `ChatSession` starts (existing pattern: current session warned/replaced if any)
3. Compose UI shows "Prior sessions for this document" — list of past `ChatSession` records (loaded from warm/cold tier by `DocumentId`)
4. User can opt to "Bring forward" — appends prior session's *summary* (the 15-msg compacted form) into the new session as context

This is **largely free from existing infrastructure**. R1 deliverable: the UI affordance + the "bring forward" wiring. No new persistence.

---

## 7. Playbook Integration + Consumer-Routing Dispatch

**All Compose AI actions are JPS playbook actions, dispatched via consumer routing.** No bespoke AI endpoints. This is binding.

### Why

- Consistency, governance, observability — all AI actions go through one pathway
- Extensibility: makers add Compose workflows by adding `sprk_playbookconsumer` rows — **no code deploy**
- Reuse: existing actions (summarize, compare-to-playbook, etc.) work in Compose with minimal adaptation

### The dispatch pattern (existing infrastructure)

Per [`docs/guides/HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md`](../../docs/guides/HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md):

1. **`ConsumerTypes.cs`** — add stable consumer type constants (e.g., `compose-summarize`, `compose-explain-clause`, `compose-compare-to-playbook`)
2. **Dataverse row** in `sprk_playbookconsumer` linking consumer type to playbook ID (optionally + consumer code for variant routing)
3. **BFF service** injects `IConsumerRoutingService` + `IInvokePlaybookAi`, calls `ResolveAsync` → `InvokePlaybookAsync`
4. Adding new Compose workflows later = new Dataverse row, zero code

Active consumer types as of 2026-06-29: 7 (matter-pre-fill, project-pre-fill, ai-summary, summarize-file, chat-summarize, email-analysis, daily-briefing-narrate). Compose adds to this set.

### Compose's R1 playbook contributions

| Contribution | Description | R1? |
|---|---|---|
| **JPS scope: `compose-selection`** | "Selected text in a Compose-hosted document" — inputs: selection text, span anchors, document SPE id, matter id, session memory pointer | Yes (scope only) |
| **JPS scope: `compose-document`** | "Whole document open in Compose" — for full-doc actions like summarize | Yes (scope only) |
| **`ConsumerTypes` constants** | At least one Compose consumer type defined (e.g., `compose-summarize`) | Yes |
| **Seeded `sprk_playbookconsumer` row** | Links that consumer type to a working playbook (existing or new) | Yes — smoke test |
| **End-to-end dispatch** | One Compose action invokable via `IConsumerRoutingService` + `IInvokePlaybookAi` | Yes — smoke test |
| **Additional Compose actions** | Explain clause, Replace with standard, Compare-to-playbook, Draft alternative, etc. | R2 |

The R1 smoke test is the load-bearing one: it proves the foundation works. R2 multiplies actions through the foundation.

---

## 8. Document Source — Always SPE

**Binding rule**: every document Compose opens MUST be in SPE. There is no "local file" mode. This applies even when the user starts from a desktop Word file.

### Entry paths

| Path | How user gets there | Storage flow | `sprk_document` record? |
|---|---|---|---|
| **A — From Spaarke (existing Document)** | Open a `sprk_document` record → command-bar "Open in Compose" | Already in SPE; load + start session bound to record | Yes (existing) |
| **B — From Assistant** | Search SPE (existing `sprk_document` OR uploaded file) → "Open in Compose" | If existing Document: load it. If upload: PUT to SPE → load (no Document record yet) | Optional initially; **required at first Save** |
| **C — Office Add-in (R3+)** | In Word desktop/web, "Open in Spaarke Compose" | Add-in ensures file is in SPE → opens Compose | Optional; same as B |

### Entry path UX details

**Path A — from a Document command bar**:
- "Open in Compose" button opens Spaarke AI in a new browser tab (or modal — TBD per UX) with the file mounted in Compose
- Tab/modal decision: lean toward **new tab** for first-class workspace experience; modal for quick-edit use cases — open in §14

**Path B — from Assistant search or upload**:
- Assistant has a search affordance for SPE/Documents
- For uploads: Assistant accepts a file, BFF uploads to SPE, returns drive-item id; "Open in Compose" then opens the workspace mounted on that drive-item

### Ephemeral docs and the Save-promotion rule

Path B can start without a `sprk_document` record. R1 binding rule:

1. **Open + edit without a record is allowed.** Session memory binds to SPE drive-item id; matter binding is optional.
2. **First Save creates the `sprk_document` record.** This is non-negotiable in R1 — it is the gate that triggers normal Document lifecycle (matter binding, AI search indexing, permissions, audit).
3. **Pre-Save state** is held in browser memory (TipTap) + warm-tier `ChatSession` (with `DocumentId` = SPE drive-item id, no `sprk_documentid` yet).
4. **AI Search indexing** is the standard Document pipeline — happens automatically once the Document record exists. No Compose-specific indexing logic needed (resolves Q-IDX).

### Open Document → command-bar entry (Path A specifics)

When opening from an existing `sprk_document`:
- Command bar button "Open in Compose"
- Opens Spaarke AI in a new browser tab navigated to the Compose workspace
- `?documentId={spe-drive-item-id}&recordId={sprk_documentid}` deep-link parameters
- Compose mounts the file + starts (or resumes) a `ChatSession` bound to that `DocumentId`

### R1 deliverable for §8

- BFF endpoint: upload to SPE (used by Assistant + Compose)
- BFF endpoint: create `sprk_document` record on Save (idempotent — no-op if record already exists)
- Path A command-bar wiring (button + deep-link)
- Path B Assistant upload + "Open in Compose" wiring

---

## 9. Word ↔ Compose Surface Handoffs

### 9.1 Compose → Word (R1)

Two buttons in the Compose toolbar:

| Button | Behavior | Mechanism |
|---|---|---|
| **"Open in Word for Web"** | New browser tab; Word for Web edits the SPE file | SPE WOPI launcher URL (Microsoft-hosted) |
| **"Open in Word Desktop"** | Word desktop opens the SPE file | `ms-word:ofe\|u\|{wopiSrc}` protocol handler URL (provided by SPE driveItem) |

Before either: Compose saves current TipTap state → DOCX → SPE (new version).

**Spaarke does no WOPI work** — SPE's Microsoft-managed WOPI handles the bridge.

### 9.2 Word → Compose (R3 — Office Add-in)

Deferred to R3. Document the deep-link contract in R1 (what URL parameters, what session-init flow) but do not implement.

### 9.3 Return from Word (R2)

When user returns to Compose after Word editing produced a new SPE version:
1. Detect via SPE etag / version (R1 wires the detection only)
2. Reload doc from SPE (R1 plumbing)
3. Restore session memory — already there from `ChatSession` reload (R1 — works for free via existing infrastructure)
4. Re-anchor annotations (R2)
5. Conflict banner if Compose had unsaved local edits (R2)

R1 gets (1)–(3) for free. (4) and (5) = R2.

---

## 10. Q&A Resolutions

| Q | Resolution |
|---|---|
| **Q1 — Layout vs widget?** | **Workspace layout.** `sprk_workspacelayout` system record. Confirmed. |
| **Q2 — Editor strategy** | **TipTap (ProseMirror).** Confirmed. R1 builds a TipTap editor shell; spike validates DOCX bridge (likely TipTap DOCX extension or open-source equivalent). |
| **Q3 — DOCX strategy** | **(a) Constrained subset, defined by TipTap OOB capabilities.** We leverage what TipTap provides out of the box (StarterKit + standard open-source extensions); zero custom integration work for advanced features. Anything TipTap doesn't render OOB → drop on import / "open in Word" for the user. The subset is determined by the editor's natural capabilities, not invented separately. On export, emit only what TipTap can roundtrip. Pattern (b) server-side canonical remains the R2+ fallback if users push back on subset gaps. R1 deliverable: a *validated inventory* (output of Spike #1) of what survives OOB roundtrip, published as the subset spec. |
| **Q4 — Collaboration model** | **Single-editor with SPE check-out lock, R1.** BFF wraps SPE check-out on Compose session open + check-in on close/save. Word for Web users automatically see "Checked out to X" via SPE's built-in indicator — no custom UI. CRDT / true co-editing deferred to R5+. |
| **Q5 — Canonical bytes location** | **Three layers with distinct roles** (§6): browser TipTap (active editing + AI input), three-tier `ChatSession` (Redis/Cosmos/Dataverse — session memory), SPE blob (file of record + Word interop + indexing). Not a choice — a layered architecture. |
| **Q-IDX — SPE indexing** | **Resolved.** Indexing happens via the normal Document pipeline once a `sprk_document` record exists. Ephemeral (no-record) docs are not indexed until first Save promotes them to a Document. No Compose-specific indexing logic needed. |
| **Q-Session entity** | **Use existing `ChatSession` + `sprk_aichatsummary`** (not a new `sprk_composesession`, not `sprk_analysis`). `DocumentId` binding gives doc↔session pointer; `HostContext` carries entity awareness; Compose extends `HostContext` with `ComposeContext` if/when editor-specific metadata is needed (open item, see §14). |

---

## 11. Component Reuse Map

Per CLAUDE.md §11 (Component Justification):

| Need | Reuse from | Net-new in R1 |
|---|---|---|
| Three-pane shell | `SpaarkeAi/src/components/shell/ThreePaneShell.tsx` | — |
| Assistant pane | `ConversationPane.tsx` | New JPS scope wiring (`compose-selection`, `compose-document`); "prior sessions for this document" affordance |
| Workspace pane host | `WorkspaceLayoutWidget` + `LegalWorkspaceApp` (embedded) | — |
| Context pane | Existing `@spaarke/legal-workspace` panes | Wiring only (rich playbook output rendering = R2) |
| Auth | `@spaarke/auth` (Spaarke Auth v2 — ADR-028) | — |
| BFF | `Sprk.Bff.Api` | New endpoints (§12) — placement justification per CLAUDE.md §10 + [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) |
| SPE access | Existing Graph client + SPE patterns | SPE check-out/check-in wrapper; upload-to-SPE for ephemeral path |
| Playbook execution | Existing JPS infrastructure | New JPS scopes (`compose-selection`, `compose-document`) |
| Playbook dispatch | `IConsumerRoutingService` + `IInvokePlaybookAi` ([§7](#7-playbook-integration--consumer-routing-dispatch)) | New `ConsumerTypes` constants; seeded Dataverse row(s) |
| Session persistence | `ChatSession` + Redis/Cosmos/Dataverse three-tier ([§6](#6-session-persistence--reuse-the-existing-three-tier-pattern)) | DocumentId binding; possibly `ComposeContext` extension on `HostContext` |
| Editor | TipTap (NET-NEW client-side dependency) | The Compose editor shell |
| DOCX bridge | TipTap DOCX extension OR open-source equivalent | Determined in spike |
| Layout / section registry | Existing `SECTION_REGISTRY` + `useWorkspaceLayouts` | New `compose-editor` section registration; hard-coded system layout entry |

---

## 12. BFF Surface (R1)

R1 endpoints (each must satisfy §10 placement justification per CLAUDE.md):

| Endpoint | Purpose | Reuse vs new |
|---|---|---|
| `POST /api/compose/document/upload` | Upload a file from Assistant to SPE; return drive-item id | NEW |
| `GET /api/compose/document/{spe-id}` | Load DOCX bytes from SPE; return as bytes or pre-converted (spike decides) | NEW |
| `PUT /api/compose/document/{spe-id}` | Save edits as new SPE version | NEW |
| `POST /api/compose/document/{spe-id}/promote` | Create `sprk_document` record on first Save (idempotent) | NEW |
| `POST /api/compose/document/{spe-id}/checkout` | SPE check-out (lock) | NEW (thin SPE wrapper) |
| `POST /api/compose/document/{spe-id}/checkin` | SPE check-in (unlock + save) | NEW (thin SPE wrapper) |
| `POST /api/compose/action/{consumerType}` | Invoke playbook via consumer routing — wraps `IConsumerRoutingService` + `IInvokePlaybookAi` | NEW (one consumer wired E2E in R1; thin dispatch wrapper) |
| **Chat session** | All chat persistence/load/save reuses existing endpoints | **REUSE** — no new chat endpoints |
| **Word handoff URLs** | Compose UI consumes SPE-provided WOPI launcher + `ms-word:` protocol URLs | REUSE (no BFF work) |

**Hot-path declaration (per CLAUDE.md §10)**: BFF=Y, SpaarkeAi=Y, ci-workflows=N, skill-directives=N, root-CLAUDE.md=N.

**Publish-size budget**: per CLAUDE.md §10, ≤60 MB compressed. Current baseline ~45.65 MB. TipTap is client-side. Server-side DOCX libraries (only if Q3 fallback (b) kicks in later) would add cost — flag in spike if relevant. R1 should remain well within budget.

---

## 13. R1 Spike Plan

Before writing `spec.md`, four focused spikes (~5 days total):

| # | Spike | Days | Decision unlocked |
|---|---|---|---|
| 1 | **TipTap OOB + DOCX round-trip prototype** | 2 | OOB-feature inventory (what survives roundtrip); cheapest DOCX bridge identified (e.g., open-source `prosemirror-docx` or equivalent — no custom integration); validated on 3 real legal DOCXs (one letter, one long agreement, one multi-level-numbered contract). Output = the locked subset spec. |
| 2 | **Three-pane coordination wiring** | 1 | Flows 1, 2, 5 work without shell changes; data contracts locked |
| 3 | **SPE check-out/check-in + Document-record promotion-on-Save** | 1 | Q4 mechanism confirmed; Path B ephemeral → Save → Document creation path works |
| 4 | **Consumer-routing E2E smoke test + JPS scope registration** | 1 | One Compose consumer type wired through `IConsumerRoutingService` + `IInvokePlaybookAi`; scopes validated via `jps-validate` |

Output: 4-page feasibility memo + 4 working prototypes. Then `spec.md`.

---

## 14. Resolved Decisions (locked 2026-06-29)

All six prior open items are resolved:

| # | Topic | Resolution |
|---|---|---|
| 1 | **DOCX subset** | **Leverage TipTap OOB only.** The subset is *defined by* TipTap's out-of-the-box capabilities (StarterKit + standard open-source extensions). No custom integration work for advanced features (comments-as-Word-comments, tracked changes, footnotes/endnotes, field codes, TOC auto-gen, equations, SmartArt, per-section headers/footers). For anything outside TipTap OOB → "open in Word." Spike #1 validates the OOB inventory on real legal DOCXs and publishes the locked subset spec. |
| 2 | **`HostContext` extension** | **Do NOT extend in R1.** Existing `HostContext` (EntityType, EntityId, WorkspaceType) is sufficient at the session level. Transient editor state (selection span, focused clause, artifact type) lives in browser memory and is passed to playbook actions as **JPS scope inputs** (`compose-selection`), NOT persisted on the session. Revisit only if Spike #2 surfaces a concrete persistent-metadata need. |
| 3 | **Path A entry UX** | **Modal with full-screen toggle.** Command-bar "Open in Compose" from a Document opens Spaarke AI in a modal shell consistent with other SpaarkeAi launches; user can expand to full-screen for clean Compose experience. Reuses existing SpaarkeAi modal pattern (found in `ConversationPane`, `ContextPaneController`, `launch-resolver.ts`). |
| 4 | **Multi-tab / multi-session** | **Per-user single-session lock.** When a user opens a document in Compose, SPE check-out locks it. If the same user attempts to open the same document in another tab (or another browser session), Compose detects the existing check-out and shows: **"This file is open in another Compose session. [Go to that session] [Force-close other session and open here]."** Orphan locks released via session heartbeat (auto-release after 15 min idle — refine in spike). Avoids the "user collaborating with themselves" failure mode. |
| 5 | **R1 default-open behavior** | **Empty state with two options**: "Browse / open file" (SPE picker) OR "Search for Document" (Spaarke Document search). |
| 6 | **First Compose consumer type (R1 smoke test)** | **`compose-summarize`** — whole-document summarize. Simplest E2E proof; reuses existing summarize playbook patterns; predictable success criteria. R1 deliverable: `ConsumerTypes.ComposeSummarize` constant + seeded `sprk_playbookconsumer` row + BFF endpoint wired through `IConsumerRoutingService` + `IInvokePlaybookAi` + Compose UI button to fire it. |

### Pending — spike outputs (not decisions, just outputs)

The following are not unresolved decisions; they're outputs the spikes will produce:

- **DOCX subset spec (locked, published)** — output of Spike #1
- **TipTap DOCX bridge choice** (open-source library name + version) — output of Spike #1
- **Lock heartbeat interval** — output of Spike #3 (proposed default: 15 min idle → auto-release)
- **JPS scope schema for `compose-selection` and `compose-document`** — output of Spike #4

Once spikes complete, these become locked artifacts in `spec.md`.

---

## 15. Vision Roadmap (post-R1)

| Release | Theme | Headline deliverables |
|---|---|---|
| **R1 (this project)** | Foundation | Workspace layout + three-pane wiring + TipTap editor + SPE plumbing + Document promotion-on-Save + JPS scopes + first consumer-routing type wired E2E |
| **R2** | AI actions + round-trip | Playbook-defined actions (Explain / Replace / Compare / Draft); rich session-memory content; return-from-Word annotation re-anchoring; DOCX subset enforcement |
| **R3** | Word add-in | "Open in Spaarke Compose" add-in for Word desktop + Word for Web; deep-link flow |
| **R4** | Multi-artifact | PDF artifact (viewer + extracted-text editor); email artifact; transcript artifact |
| **R5+** | Co-editing | Real-time multi-user editing (CRDT); comparison/redline artifact |

The **Artifact Surface** abstraction is baked into R1 — Compose is a `DocxArtifact`; later releases register additional artifact types into the same host:

```
ComposeWorkspace
  └── ArtifactHost
        └── ArtifactRenderer (by type)
              ├── DocxArtifact (R1 — TipTap editor)
              ├── PdfArtifact (R4)
              ├── EmailArtifact (R4)
              └── ...
```

Each Artifact declares: its renderer, its supported playbook actions, its Context-pane contributions, its import/export contract.

---

## Footer

This is a working document. Edit in place as we refine. When stable, it informs `spec.md` (the committed spec) and the task plan.

**Companion docs**:
- [`Spaarke-AI-Document-Workspace-Solution-Concept.md`](./Spaarke-AI-Document-Workspace-Solution-Concept.md) — original concept (preserved unchanged)
- `spec.md` — TBD, written after spikes
- `plan.md` — TBD, written after spec
