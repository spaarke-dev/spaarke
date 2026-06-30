# Spaarke Compose (R1) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-06-29
> **Source**: [`design.md`](./design.md) + [`Spaarke-AI-Document-Workspace-Solution-Concept.md`](./Spaarke-AI-Document-Workspace-Solution-Concept.md)
> **Project ID**: `spaarkeai-compose-r1`
> **Positioning**: AI-native legal drafting workspace
> **R1 Theme**: Foundation — workspace layout, three-pane wiring, TipTap (OOB) editor, SPE plumbing, ChatSession reuse, consumer-routing smoke test. AI actions and DOCX subset enforcement deferred to R2+.

---

## Executive Summary

Spaarke Compose is the AI-native legal drafting workspace — the center pane of the existing SpaarkeAi three-pane shell, coordinated with the Assistant (left) and Context (right) panes. Compose and Microsoft Word are two complementary surfaces (handoff model, not competition): Compose owns AI-driven legal work; Word remains the advanced word processor reached via SharePoint Embedded. R1 delivers the foundation (layout + wiring + editor shell + plumbing + smoke-test playbook dispatch); AI actions, return-from-Word round-trip, and DOCX subset enforcement layer in R2+.

---

## Scope

### In Scope (R1)

1. **Compose workspace layout** — new `sprk_workspacelayout` system record (template `single-column`, section `compose-editor`), user-facing label "Compose"
2. **Three-pane coordination wiring** — six coordinated flows (Workspace↔Context, Workspace↔Assistant, Context→Workspace, Context→Assistant, Assistant→Workspace, Assistant→Context) with locked data contracts; receivers may be stubs in R1
3. **`compose-editor` section** — TipTap-based React editor shell mounted in the Workspace pane, registered in `SECTION_REGISTRY`
4. **TipTap OOB-only feature set** — features constrained to TipTap StarterKit + standard open-source extensions; no custom integration for advanced features (comments-as-Word-comments, tracked changes, footnotes, fields, equations, SmartArt)
5. **SPE plumbing** — load DOCX from SPE into TipTap; save edits back as new SPE versions; SPE is always file-of-record
6. **Upload-from-Assistant path** — Assistant accepts a file, uploads to SPE, returns drive-item id; Compose mounts file (ephemeral — no `sprk_document` record yet)
7. **Document promotion on first Save** — first Save of an ephemeral document creates a `sprk_document` record (idempotent); indexing follows the normal Document pipeline
8. **ChatSession three-tier reuse** — Compose binds to existing `ChatSession` model (`DocumentId` = SPE drive-item id, or `sprk_documentid` once promoted); no new entity; existing Redis/Cosmos/Dataverse three-tier infrastructure handles persistence
9. **Two new JPS scopes** — `compose-selection`, `compose-document`; registered via `jps-scope-refresh`; no new playbook actions in R1
10. **Consumer-routing foundation + smoke test** — define `ConsumerTypes.ComposeSummarize = "compose-summarize"`; seed `sprk_playbookconsumer` row linking to existing **Document Summary** playbook (id `47686eb1-9916-f111-8343-7c1e520aa4df`); wire end-to-end dispatch (UI → BFF → routing → invoke) and verify
11. **Compose → Word handoffs (Web + Desktop) reusing existing infra** — Compose calls existing `GET /api/documents/{id}/open-links` endpoint; surfaces "Open in Word for Web" + "Open in Word Desktop" toolbar buttons via existing `DesktopUrlBuilder` (`Spaarke.Core`)
12. **Shared library extraction** — extract `useDocumentActions` hook from `SemanticSearch` to new `@spaarke/document-operations` shared library; refactor SemanticSearch to consume from shared; Compose consumes from shared
13. **Per-user single-session SPE check-out lock** — opening a document in Compose acquires SPE check-out; same user opening same document in another tab/session sees "[Go to that session] [Force-close other session and open here]"; orphan locks released via heartbeat (default 15 min idle)
14. **Empty-state UX** — opening Compose with no document shows two options: "Browse / open file" (SPE picker) OR "Search for Document" (Spaarke Document search)
15. **Path A entry (modal)** — command-bar "Open in Compose" from a Document record opens Spaarke AI in modal shell with full-screen toggle (reuses existing modal pattern from `ConversationPane`, `ContextPaneController`, `launch-resolver.ts`)
16. **R1 BFF endpoints** — seven new endpoints under `/api/compose/` (see §Affected Areas + §FRs)
17. **Tests** — unit tests for each new BFF service per CLAUDE.md §10 #6 obligation

### Out of Scope (R1 — deferred to R2+)

- AI actions on selection (Explain clause / Replace with standard / Compare-to-playbook / Draft alternative) — R2
- Rich session-memory content beyond `DocumentId` binding (anchored annotations, action log, derived insights) — R2
- Return-from-Word round-trip annotation re-anchoring + conflict UX banner — R2
- DOCX subset *enforcement* (R1 publishes a draft subset spec; enforcement code = R2)
- Office Add-in entry path (Word → Compose) — R3
- PDF / email / transcript artifact types — R4+
- Real-time multi-user co-editing (CRDT) — R5+
- Tracked changes round-trip with Word — never (out of architecture)
- Comments stored as Word `<w:comment>` elements — never; Compose stores comments as ChatSession annotations (R2+)

### Affected Areas

**Frontend (new)**:
- `src/solutions/SpaarkeAi/src/components/workspace/` — register `compose-editor` section type
- `src/solutions/SpaarkeAi/src/components/compose/` — NEW directory: `ComposeWorkspace.tsx`, `ComposeToolbar.tsx`, `ComposeEmptyState.tsx`, `ComposeEditor.tsx`
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/` — register `compose-editor` section
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/context/` — Compose-aware Context pane stubs (wire-only)
- **`src/client/shared/Spaarke.DocumentOperations/`** — NEW shared library

**Frontend (modified)**:
- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` — wire JPS scope inputs (`compose-selection`, `compose-document`)
- `src/client/code-pages/SemanticSearch/src/hooks/useDocumentActions.ts` — REMOVE (move to shared lib)
- `src/client/code-pages/SemanticSearch/src/components/SearchCommandBar.tsx` — refactor to import from `@spaarke/document-operations`

**Backend (new)**:
- `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs` — 7 new endpoints
- `src/server/api/Sprk.Bff.Api/Services/Compose/` — NEW directory: `ComposeService.cs`, `ComposeDocumentService.cs`, `ComposeSessionService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs` — add `ComposeSummarize` constant

**Backend (modified)**:
- `src/server/api/Sprk.Bff.Api/Program.cs` — register Compose services + endpoints

**Dataverse**:
- New record in `sprk_workspacelayout` — Compose system layout (hard-coded GUID)
- New record in `sprk_playbookconsumer` — `compose-summarize` → Document Summary playbook

**JPS**:
- Two new scope definitions in JPS catalog (`compose-selection`, `compose-document`)

**Tests (new)**:
- `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/` — unit tests for new services
- `tests/unit/Sprk.Bff.Api.Tests/Api/ComposeEndpointsTests.cs` — endpoint tests
- Frontend tests for `@spaarke/document-operations` consumers

---

## Placement Justification (BFF Hygiene §10 — binding)

All R1 Compose endpoints belong in `Sprk.Bff.Api`. No new microservice; no Dataverse plugin handlers.

**Justification**:

1. **All R1 endpoints touch SPE (Graph API) and/or Dataverse** — both require BFF-resident infrastructure (OBO/app-only auth, Graph client factory, Dataverse SDK).
2. **Plugins lack Graph access** — Compose's upload, load, save, check-out, check-in all hit SPE via Graph.
3. **A separate microservice duplicates auth + deployment without functional benefit** — all endpoints fit the same `RequireAuthorization()` surface as existing BFF endpoints.
4. **AI dispatch uses AI PublicContracts facade per refined ADR-013** — Compose-action endpoint injects `IConsumerRoutingService` + `IInvokePlaybookAi`, NOT direct AI internals (`IOpenAiClient`, `IPlaybookService`).
5. **Publish-size impact minimal** — TipTap + DOCX bridge are client-side. Server delta estimated +1–2 MB compressed. Current baseline ~45.65 MB; ≤60 MB ceiling. Will be measured per task.
6. **No new HIGH-severity CVE expected** — verify at task close per CLAUDE.md §10 #5.
7. **Test obligation** — every new service in `Services/` gets matching tests in `tests/unit/Sprk.Bff.Api.Tests/`.

**Hot-Path Declaration**: BFF=Y · SpaarkeAi=Y · ci-workflows=N · skill-directives=N · root-CLAUDE.md=N.

---

## Requirements

### Functional Requirements

| ID | Requirement | Acceptance |
|---|---|---|
| **FR-01** | A new `sprk_workspacelayout` system record exists with `sprk_layouttemplateid = single-column`, sections=`[compose-editor]`, label="Compose" | Layout appears in workspace picker; selecting it opens Compose |
| **FR-02** | `compose-editor` section type is registered in `SECTION_REGISTRY` and renders a TipTap editor shell | Opening Compose layout mounts editor; basic typing, formatting, headings, lists, tables work |
| **FR-03** | TipTap editor supports OOB feature set only (StarterKit + standard extensions); advanced features (tracked changes, footnotes, equations, fields) NOT supported | Spike #1 produces validated inventory; documented as the locked subset spec |
| **FR-04** | Compose loads a DOCX from SPE given a drive-item id (Path A: from Document record) | Opening a `sprk_document` → "Open in Compose" mounts the file; content readable + editable |
| **FR-05** | Compose loads a DOCX from SPE given a drive-item id without `sprk_document` record (Path B: ephemeral) | Uploading via Assistant + "Open in Compose" mounts file; session can be edited |
| **FR-06** | First Save of an ephemeral document creates a `sprk_document` record (idempotent) | After first Save, `sprk_documentid` exists; subsequent Saves update file only |
| **FR-07** | Compose uses existing `ChatSession` model with `DocumentId` binding (SPE drive-item id, or `sprk_documentid` post-promotion) | `ChatSession.DocumentId` set on Compose open; session persists across browser refresh via existing Redis/Cosmos/Dataverse pipeline |
| **FR-08** | Two JPS scopes (`compose-selection`, `compose-document`) defined and registered via `jps-scope-refresh` | Scopes appear in JPS catalog; `jps-validate` passes |
| **FR-09** | `ConsumerTypes.ComposeSummarize = "compose-summarize"` constant exists in `ConsumerTypes.cs` | Constant present; code compiles |
| **FR-10** | A row exists in `sprk_playbookconsumer` linking `compose-summarize` to playbook id `47686eb1-9916-f111-8343-7c1e520aa4df` (Document Summary) | `IConsumerRoutingService.ResolveAsync("compose-summarize", ...)` returns the playbook id |
| **FR-11** | End-to-end smoke test: Compose UI button → BFF endpoint → `IConsumerRoutingService` → `IInvokePlaybookAi` → Document Summary playbook → result returned | Smoke test passes; full pipeline verified |
| **FR-12** | Compose toolbar has "Open in Word for Web" + "Open in Word Desktop" buttons; both reuse existing `GET /api/documents/{id}/open-links` endpoint + `DesktopUrlBuilder` | Clicking each opens corresponding Word surface on the SPE file |
| **FR-13** | `useDocumentActions` hook moved to new `@spaarke/document-operations` shared library | Hook lives in shared lib; package consumable by both SemanticSearch and Compose |
| **FR-14** | SemanticSearch refactored to consume `useDocumentActions` from `@spaarke/document-operations` (no functional regression) | SemanticSearch existing tests still pass; "Open in Web/Desktop" still works |
| **FR-15** | Opening a document in Compose acquires SPE check-out; closing/idle releases check-out | Verify via Graph API + Word for Web (sees "Checked out to {user}") |
| **FR-16** | Same user attempting to open same document in another Compose tab sees "[Go to that session] [Force-close other session and open here]" UI | Open one tab → open second tab on same doc → conflict UX appears |
| **FR-17** | Orphan locks auto-release after configurable idle threshold (default 15 min) via session heartbeat | Open document → close tab without explicit close → wait 15 min → check-out released |
| **FR-18** | Empty Compose workspace (no document context) shows two options: "Browse / open file" + "Search for Document" | Verify default-open state |
| **FR-19** | Path A entry: command-bar "Open in Compose" button on `sprk_document` record opens Spaarke AI in modal shell with full-screen toggle | Click button → modal opens with Compose mounted; full-screen toggle works |
| **FR-20** | Three-pane coordination data contracts (six flows from design.md §5) defined as TypeScript interfaces + wired endpoint-to-endpoint (receivers stubbed in R1) | All six data contracts compile; smoke test confirms data flows between panes |
| **FR-21** | BFF endpoints exist for: upload-to-SPE, load-document, save-document, promote-document, checkout, checkin, dispatch-action | Each endpoint responds 200 on happy path; auth gates verified |
| **FR-22** | Unit tests exist for each new BFF service per CLAUDE.md §10 #6 | `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/` contains matching test files |

### Non-Functional Requirements

| ID | Requirement |
|---|---|
| **NFR-01** | DOCX load latency: <2s for typical legal doc (50 pages); Redis cache hit <200ms |
| **NFR-02** | DOCX save latency: <1.5s to SPE (new version commit) |
| **NFR-03** | AI action end-to-end latency: <3s for `compose-summarize` smoke test on typical legal doc |
| **NFR-04** | Editor typing latency: <16ms (60fps target) |
| **NFR-05** | SPE check-out/check-in latency: <500ms each |
| **NFR-06** | BFF publish-size impact: ≤+2 MB compressed delta vs baseline (~45.65 MB); strict ceiling ≤60 MB per CLAUDE.md §10 |
| **NFR-07** | Multi-tenant isolation: tenant-scoped at Redis key, Cosmos partition, Dataverse query filter (inherits from existing ChatSession infrastructure) per ADR-015 Tier 3 |
| **NFR-08** | All Compose BFF endpoints require authentication (`RequireAuthorization()`) per Spaarke Auth v2 (ADR-028) |
| **NFR-09** | No new HIGH-severity CVE introduced — verify via `dotnet list package --vulnerable --include-transitive` at task close |
| **NFR-10** | Existing ChatSession compaction (LLM summarize at 15 messages) + archive (at 50 messages) apply to Compose sessions unchanged |
| **NFR-11** | Lock heartbeat interval default 15 min idle (refinable post-spike) |
| **NFR-12** | Caching strategy: Redis hot tier for active Compose sessions; aggressive cache use for repeated DOCX reads |

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance |
|---|---|
| **ADR-001** Minimal API | Compose endpoints follow minimal-API pattern with endpoint groups |
| **ADR-008** Endpoint filters for authorization | Each Compose endpoint `RequireAuthorization()` |
| **ADR-010** Org-owned default | New Dataverse rows (`sprk_workspacelayout`, `sprk_playbookconsumer`) org-owned |
| **ADR-013** AI facade refinement (2026-05-20) | Compose CRUD code injects `IConsumerRoutingService` + `IInvokePlaybookAi` facade — NOT direct AI internals |
| **ADR-015** Multi-tenant isolation Tier 3 | Inherits enforcement from existing ChatSession three-tier infrastructure |
| **ADR-019** Endpoint conventions | Compose endpoints grouped under `/api/compose/` route prefix |
| **ADR-028** Spaarke Auth v2 | Compose UI uses `@spaarke/auth` for SSO; BFF endpoints validate via existing auth pipeline |
| **ADR-032** BFF Null-Object Kill-Switch | Applies if any Compose service ends up feature-gated; R1 default = no feature gates |
| **ADR-038** Testing strategy | Integration-heavy pyramid; 6 KEEP categories; mock-boundary rules; ban `Mock<HttpMessageHandler>`, DI-registration, ctor null-check tests |

### MUST Rules (extracted from ADRs + CLAUDE.md)

- ✅ MUST use minimal API pattern (`MapGroup` + `MapGet`/`MapPost` + endpoint filters)
- ✅ MUST require authorization on every Compose endpoint (`RequireAuthorization()`)
- ✅ MUST inject AI capabilities via `Services/Ai/PublicContracts/` facade — NOT direct AI internal types
- ✅ MUST reuse existing `ChatSession` + Redis/Cosmos/Dataverse three-tier; NOT create new session entity
- ✅ MUST reuse existing `IConsumerRoutingService` + `IInvokePlaybookAi` for AI dispatch
- ✅ MUST reuse existing `GET /api/documents/{id}/open-links` for Word handoff — NOT create new endpoint
- ✅ MUST extract `useDocumentActions` to shared lib before Compose consumes it (avoids duplication)
- ✅ MUST measure publish-size impact on BFF-touching tasks per CLAUDE.md §10 #4
- ✅ MUST add unit tests for new BFF services per CLAUDE.md §10 #6
- ✅ MUST follow ADR-038 testing rules (no banned test patterns)
- ❌ MUST NOT extend `sprk_analysis` for Compose chat/session storage (use `ChatSession` model)
- ❌ MUST NOT inject `IOpenAiClient`, `IPlaybookService`, or other AI internals into Compose CRUD code
- ❌ MUST NOT build custom integrations for advanced DOCX features outside TipTap OOB
- ❌ MUST NOT store comments as Word `<w:comment>` elements in R1 (use ChatSession annotations in R2+)
- ❌ MUST NOT support tracked-changes round-trip (out of architecture)
- ❌ MUST NOT extend `HostContext` in R1 (use existing fields; transient state goes in JPS scope inputs)

### Supersession Map (BINDING — prevent code-level commingling)

| Retired / superseded | Current (use this) |
|---|---|
| `AnalysisWorkspace` solution (removed) | SpaarkeAi three-pane shell (`src/solutions/SpaarkeAi/`) |
| `SprkChat` standalone (retired) | `ConversationPane` (`src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx`) |
| `ai-analysis-workspace-sprkchat-integration` project | This project (not a continuation) |
| `sprk_analysis.sprk_chathistory` field | `ChatSession` model + `sprk_aichatsummary` + Redis/Cosmos/Dataverse three-tier |

### Existing Patterns to Follow

| Pattern | Reference |
|---|---|
| ChatSession three-tier persistence | [`ChatSessionManager.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatSessionManager.cs), [`SessionPersistenceService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionPersistenceService.cs), [`ChatHistoryManager.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatHistoryManager.cs) |
| Consumer-routing dispatch | [`ConsumerRoutingService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerRoutingService.cs), [`InvokePlaybookAi.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/InvokePlaybookAi.cs), [`ConsumerTypes.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs); how-to: [`docs/guides/HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md`](../../docs/guides/HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md) |
| Open-in-Word backend | [`FileAccessEndpoints.cs`](../../src/server/api/Sprk.Bff.Api/Api/FileAccessEndpoints.cs) `GetOpenLinks`, [`DesktopUrlBuilder.cs`](../../src/server/shared/Spaarke.Core/Utilities/DesktopUrlBuilder.cs), [`FileOperationModels.cs`](../../src/server/api/Sprk.Bff.Api/Models/FileOperationModels.cs) `OpenLinksResponse` |
| Open-in-Word client | [`useDocumentActions.ts`](../../src/client/code-pages/SemanticSearch/src/hooks/useDocumentActions.ts) (source — moves to shared lib) |
| SpaarkeAi three-pane shell | [`ThreePaneShell.tsx`](../../src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx); architecture: [`docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../../docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) |
| Section registration | `SECTION_REGISTRY` in workspace layout system |
| Modal launch (Path A) | [`ConversationPane.tsx`](../../src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx), [`launch-resolver.ts`](../../src/solutions/SpaarkeAi/src/utils/launch-resolver.ts) |
| Workspace layout pattern (Calendar precedent) | [`CalendarWorkspaceWidget`](../../src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/) |
| BFF endpoint conventions | Existing `Sprk.Bff.Api/Api/*.cs` files |

---

## ADR Tensions (per CLAUDE.md §6.5)

*Anticipated conflicts between Compose's R1 design and existing ADR rules. Surfaced at design time so they can be resolved as Path A (project-scoped exception) / Path B (ADR amendment) / Path C (pivot to comply) rather than discovered as silent violations at code-review.*

**Scan result at design time**: No ADR tensions surfaced.

**Tensions surfaced during implementation** (post-design):

| # | Rule challenged | Surfaced by | Conflict | Path | Rationale | Impact |
|---|---|---|---|---|---|---|
| T-1 | **design.md §14 row 4** (project decision row — "SPE-native check-out lock") | Spike #3 (2026-06-29; locked artifact: `notes/spikes/spike-3-spe-checkout-promotion.md`) | Spike found existing `DocumentCheckoutService` (~1170 LOC in production) already implements check-out / check-in / discard / cross-user 409 conflict / `sprk_fileversions` row creation / Office Online edit-URL exchange. SPE-native wrapper would duplicate ~85% of this for one capability gain (auto-lock-banner in Word for Web + Word Desktop). | **Path A — project-scoped exception** | Reuse existing service (CLAUDE.md §11 default-to-reuse). Add heartbeat field + sweeper + 2 net-new endpoints (~150 LOC) instead of building parallel SPE-native lock infrastructure (~600 LOC). Operator approved 2026-06-29 at post-Wave-0 review gate. | (1) design.md §14 row 4 amended to "Dataverse-side via existing `DocumentCheckoutService`". (2) Phase 5 task LOC estimate ~600 → ~150. (3) Phase 1 must add Dataverse field `sprk_documents.sprk_lastheartbeatutc` + verify Alternate Key on `sprk_graphitemid`. (4) **Known R1 cross-surface trade-off**: Dataverse-side lock NOT visible to Word for Web or Word Desktop — concurrent edits across surfaces resolve via **last-writer-wins**. Risk small (user must deliberately bypass Compose to open same doc in Word); R2+ escape hatch pre-documented in Spike #3 §3 (SPE Graph API surface). |

All listed ADRs (above) apply to Compose without exception:
- Compose follows ADR-001 (minimal API), ADR-008 (endpoint filters), ADR-019 (route conventions) for all new BFF endpoints
- Compose follows refined ADR-013 by consuming AI capabilities ONLY through `Services/Ai/PublicContracts/` facades (`IConsumerRoutingService`, `IInvokePlaybookAi`); no direct injection of AI internals into Compose CRUD code
- Compose inherits ADR-015 Tier 3 multi-tenant isolation through the reused `ChatSession` infrastructure (no Compose-side enforcement gap)
- Compose follows ADR-028 (Spaarke Auth v2) via `@spaarke/auth` on the client and existing auth pipeline on the BFF
- Compose follows ADR-038 (testing strategy) — integration-heavy pyramid, 6 KEEP categories, banned test patterns avoided
- Compose follows ADR-010 (org-owned default) for new Dataverse rows (`sprk_workspacelayout`, `sprk_playbookconsumer`)
- Compose intentionally REUSES existing patterns (per CLAUDE.md §11 Component Justification) — no parallel sessions infrastructure, no parallel Word-handoff plumbing, no parallel consumer-routing facade

**This section MUST be updated as further tensions emerge.** Per CLAUDE.md §6.5, silent compliance with an ADR rule that produces a sub-optimal outcome is itself a failure mode. If an implementer encounters a legitimate need to deviate from any ADR rule above, they MUST surface the conflict via Path A/B (exception/amendment) rather than silently choosing a worse approach.

Foundational projects like R1 should typically have few or no tensions because they reuse existing patterns. Later releases (R2 AI actions, R3 Office add-in, R4 multi-artifact) are more likely to surface tensions as they introduce novel surface area — those releases will populate this section accordingly.

---

## Success Criteria

| # | Criterion | Verify by |
|---|---|---|
| 1 | New "Compose" entry appears in SpaarkeAi workspace picker | Open SpaarkeAi → workspace dropdown shows "Compose" |
| 2 | Selecting "Compose" mounts the TipTap editor in the Workspace pane | Visual verification + DOM inspection |
| 3 | TipTap OOB feature inventory documented as the locked subset spec | Output of Spike #1 published in design.md |
| 4 | Path A: opening a `sprk_document` via "Open in Compose" command bar loads the file into editor (in modal) | Manual test against a real `sprk_document` |
| 5 | Path B: Assistant upload + "Open in Compose" loads ephemeral file into editor | Manual test |
| 6 | First Save of ephemeral document creates `sprk_document` record | Inspect Dataverse before/after Save |
| 7 | ChatSession with correct `DocumentId` exists in Redis after open; persists across browser refresh | Inspect Redis + Cosmos |
| 8 | `compose-selection` and `compose-document` JPS scopes pass `jps-validate` and appear in scope catalog | `jps-validate` exit code 0; scope catalog refresh |
| 9 | `compose-summarize` smoke test: click button in Compose → Document Summary playbook executes → result returned | Manual test + integration test |
| 10 | "Open in Word for Web" + "Open in Word Desktop" buttons in Compose toolbar work for any opened document | Manual test |
| 11 | `@spaarke/document-operations` shared library exists with `useDocumentActions` exported | Package builds; type-checks |
| 12 | SemanticSearch refactored to consume from shared lib; all existing SemanticSearch tests still pass | CI test suite green |
| 13 | SPE check-out acquired on document open; visible as "Checked out to {user}" in Word for Web | Manual verification via Word for Web |
| 14 | Same-user multi-tab open of same document shows conflict UX | Manual test (two tabs, same browser) |
| 15 | Orphan lock auto-released after 15 min idle | Slow test or manual + clock |
| 16 | Empty Compose state shows "Browse / open file" + "Search for Document" options | Manual test |
| 17 | All six three-pane data contracts compile as TypeScript interfaces | `tsc --noEmit` passes |
| 18 | All seven new BFF endpoints respond per FR-21 with correct auth gating | Integration tests for each endpoint |
| 19 | Unit tests exist + pass for every new BFF service | `dotnet test tests/unit/Sprk.Bff.Api.Tests/Services/Compose/` green |
| 20 | BFF publish-size delta ≤+2 MB compressed | Run `dotnet publish -c Release` per-task per CLAUDE.md §10 #4 |
| 21 | No new HIGH-severity CVE introduced | `dotnet list package --vulnerable --include-transitive` clean at task close |
| 22 | Spike phase complete (4 spikes, ~5 days) before main R1 implementation begins | Spike outputs documented in design.md §13 |

---

## Dependencies

### Prerequisites (must exist / be true)

- SpaarkeAi three-pane shell stable on master (✅ exists)
- `ConversationPane` is the canonical Assistant pane (✅ exists)
- `ChatSession` + Redis/Cosmos/Dataverse three-tier persistence operational (✅ exists)
- Consumer-routing infrastructure operational (✅ exists — 7 active consumer types)
- Document Summary playbook id `47686eb1-9916-f111-8343-7c1e520aa4df` exists and is callable (✅ user-confirmed)
- SPE file storage operational (✅ exists)
- `GET /api/documents/{id}/open-links` endpoint operational (✅ exists)
- Spaarke Auth v2 (per ADR-028) operational (✅ exists)
- `@spaarke/auth` and `@spaarke/ui-components` shared libraries available (✅ exist)

### External Dependencies

- **TipTap** — new client-side dependency (StarterKit + standard open-source extensions; specific version chosen in Spike #1)
- **DOCX bridge library** — chosen in Spike #1 (candidates: `prosemirror-docx`, similar open-source converters; **constraint: no custom integration work**)

### Coordination

- Hot-path declaration registered in [`projects/INDEX.md`](../../projects/INDEX.md) (per CLAUDE.md §10) — BFF=Y, SpaarkeAi=Y, others=N

---

## Owner Clarifications

Answers captured during this design-to-spec session:

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Editor | TipTap vs Lexical vs Slate? | **TipTap (ProseMirror)** | Locked editor choice |
| Editor scope | Custom integration scope for advanced features? | **OOB only — what TipTap provides out of the box** | Drops tracked changes, footnotes, fields, equations, SmartArt from R1; "open in Word" for those |
| Session entity | Use `sprk_analysis` for chat history? | **No — `sprk_analysis.sprk_chathistory` is superseded; use modern `ChatSession` + Redis/Cosmos/Dataverse three-tier with `DocumentId` binding** | `sprk_analysis` is NOT extended; existing pattern reused |
| HostContext | Extend `HostContext` for editor metadata? | **No — existing fields sufficient; transient state goes in JPS scope inputs** | No model change in R1 |
| Multi-tab | What if same user opens same doc in two tabs? | **Per-user single-session lock via SPE check-out + "[Go to that session] [Force-close other session]" UX** | Avoids "user collaborating with themselves" |
| Modal vs new tab (Path A) | "Open in Compose" command bar UX? | **Modal with full-screen toggle** | Reuses existing SpaarkeAi modal pattern |
| Empty state UX | What does blank Compose show? | **Two options: "Browse / open file" + "Search for Document"** | UX requirement locked |
| First consumer type | Which Compose consumer for R1 smoke test? | **`compose-summarize`** → existing **Document Summary** playbook (id `47686eb1-9916-f111-8343-7c1e520aa4df`) | No new playbook authoring needed |
| Open-in-Desktop | Required for R1? | **Yes — and reuse existing components, extracting to shared lib (`@spaarke/document-operations`)** | Added shared-lib extraction to R1 scope |
| BFF placement | Where do Compose endpoints live? | **All in `Sprk.Bff.Api` — full placement justification** | Captured in dedicated section; no microservice |
| Performance | NFR targets? | **Aggressive Redis caching; quicker the better** | Specific latency NFRs captured (NFR-01 through NFR-05) |
| DOCX strategy | How to handle DOCX↔TipTap impedance? | **(a) Constrained subset, defined by TipTap OOB** | Spike #1 produces validated inventory |
| Collaboration | Multi-user co-editing in R1? | **No — single-editor with SPE check-out lock; CRDT deferred to R5+** | Per-user lock + heartbeat |
| Document promotion | When does ephemeral file get a `sprk_document` record? | **On first Save (idempotent)** | Indexing follows normal pipeline (Q-IDX resolved) |

---

## Assumptions

Proceeding with these assumptions (owner did not specify, but reasonable defaults):

- **Compose layout default**: NOT a per-user default workspace; users explicitly open it. Confirm if different.
- **Modal vs full-screen default**: Modal opens in default (non-fullscreen) state; user clicks expand to go full-screen. Confirm if reversed (default full-screen).
- **First Save trigger**: Save button click triggers promotion; explicit user action required (no auto-save promotion). Auto-save during edits goes to SPE blob only.
- **Heartbeat interval**: 15 min idle default (configurable). Refinable per Spike #3 findings.
- **DOCX bridge license**: Open-source library preferred over TipTap Pro (paid) unless OOB feature gaps force the Pro choice. Spike #1 decides.
- **Bringing forward prior session memory**: Per design.md §6 "Session continuity" — user opts in via "Bring forward" affordance; not auto-merged.
- **Compose toolbar styling**: Inherits Spaarke Fluent v9 theme; matches existing SpaarkeAi visual language.

---

## Unresolved Questions (Spike Outputs)

These are NOT undecided owner questions — they're outputs the spike phase will produce, becoming locked artifacts in subsequent task documents:

- [ ] **DOCX subset spec (locked, published)** — output of Spike #1 (validated on 3 real legal DOCXs)
- [ ] **TipTap DOCX bridge library choice** (specific name + version) — output of Spike #1
- [ ] **Client-vs-server DOCX conversion decision** — output of Spike #1
- [ ] **Lock heartbeat interval** (final value; default 15 min) — output of Spike #3
- [ ] **JPS scope schemas** for `compose-selection` and `compose-document` — output of Spike #4 (validated via `jps-validate`)
- [ ] **Final consumer-routing endpoint shape** (`POST /api/compose/action/{consumerType}` request/response contract) — output of Spike #4

---

## Spike Plan Reference

Spike phase precedes main R1 implementation. Details in [`design.md` §13](./design.md#13-r1-spike-plan):

1. TipTap OOB + DOCX round-trip prototype (2 days)
2. Three-pane coordination wiring (1 day)
3. SPE check-out/check-in + Document promotion-on-Save (1 day)
4. Consumer-routing E2E smoke test + JPS scope registration (1 day)

Total: ~5 days. Output: 4-page feasibility memo + 4 working prototypes + locked artifacts above.

---

## R1 Scope Outside This Spec

Tracked in design.md §15 Vision Roadmap (R2 through R5+). Not part of R1 acceptance.

---

*AI-optimized specification. Original design: [`design.md`](./design.md). Source concept: [`Spaarke-AI-Document-Workspace-Solution-Concept.md`](./Spaarke-AI-Document-Workspace-Solution-Concept.md).*
