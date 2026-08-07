# Project Plan: SpaarkeAI Assistant Enhancements R2

> **Last Updated**: 2026-08-05
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Make the SpaarkeAI Assistant surface-aware, proactive, and truly resumable — reliability/wiring work over existing machinery (R1 dispatch spine, PaneEventBus, Cosmos `StoredSession`, grounded catalog).

**Scope**:
- A — Active-tab awareness (focus-stamp)
- B — Proactive follow-on chips (one grounded turn per tab, cached)
- C — Email Assistant-visibility (`getAgentVisibleState` + `Email` variant + `email` context type)
- D — History robustness & true resume (rich-path restore, reliability, titles, retention, Reanalyze)
- E — Remove the Notifications banner (preserve the spine)

**Timeline**: ~2–3 weeks | **Estimated Effort**: ~20 implementation tasks + deploy + wrap-up across 5 workstream phases.

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-039** — grounded execution / closed catalogs. B (proactive chips) + D (title-gen) stay within the one grounded turn; **MUST NOT** add a classifier/reranker/keyword map.
- **ADR-015** — deterministic-metadata-only agent context. **Tension → Path A exception**: the *active* tab is content-visible (compact-ambient); background tabs stay metadata-only.
- **ADR-040** — Cosmos session ledger / store-of-record. D reliability + retention stay *within* the existing 3-tier cascade; no new store, no move back to Dataverse.
- **ADR-024** — `regarding` field-set (D "Set related record" polymorphic association).
- **ADR-047** — notification/action spine. E removes a *surface*, keeps the *spine*.
- **ADR-030** — PaneEventBus. A/B use `active_widget_changed` (already broadcast).
- **ADR-007** — SpeFileStore (C `eml-render`, output-document save).
- **ADR-049** — Compose shadow document (D redline/comments reload, render-follows-store).
- **ADR-042** — memory (orthogonal to resume; do not conflate).

**From Spec (MUST rules)**:
- Inject active-tab context via the existing `onDecorateOutboundBody` seam — **MUST NOT** fork `SprkChat`.
- Reuse Cosmos `StoredSession` — no new persistence store.
- Reuse the existing dispatch seam + catalog — no new BFF dispatch endpoint (compose invariant).
- Render B's chips via the **reactive** chip surface (`useConsumerChips` / `sprk_chiptransitions`) — **MUST NOT** resurrect the removed spine-driven surface (`useSuggestionCards`).
- Keep background tabs metadata-only; fire the proactive turn **at most once per tab** (cached), never on switch-back.

### Hot-Path Declaration

```xml
<hot-path-declaration>
  <bff>Y</bff>
  <spaarkeai>Y</spaarkeai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

**Placement justification (BFF)**: all BFF work reuses existing seams in `Services/Ai/Chat` (`SprkChatAgentFactory`, `SessionPersistenceService`/`ChatSessionManager`, `ChatEndpoints`) and `Models/Workspace` — no new services, no new dispatch endpoint, no new packages. Publish-size ≤60 MB checked per BFF-touching task (baseline ~49.63 MB incl. PDBs).

### Discovered Resources

**Applicable ADRs**: 039, 015, 040, 024, 047, 030, 007, 049, 042 (see §2 above).

**Reusable code (canonical impls to copy)**:
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/pillar9-visibility.ts` — visible-state derivations (copy for the Email variant, C)
- `src/solutions/SpaarkeAi/src/components/conversation/useConsumerChips.tsx` — reactive chip surface B **MUST** reuse
- `src/solutions/SpaarkeAi/src/hooks/useSessionRestore.ts` + `components/shell/ThreePaneShell.tsx` (`SessionRestoreManager`) — rich restore path D routes through
- `src/server/api/Sprk.Bff.Api/Api/FileAccessEndpoints.cs:901` (`eml-render`) — on-demand email body (C, FR-C4)
- `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` — `active_widget_changed` payload (A)

**Applicable skills**: task-execute, code-review, adr-check, bff-deploy, code-page-deploy, conflict-check, test-diet.

**Knowledge / patterns**: `docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md`, `docs/standards/ASSISTANT-UI-ELEMENT-CRITERIA.md`, `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`, `.claude/constraints/bff-extensions.md`, `.claude/patterns/ui/*`.

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: E — Remove Notifications banner
Phase 2: A — Active-tab awareness (focus-stamp)
Phase 3: B — Proactive follow-ons
Phase 4: D — History robustness & true resume
Phase 5: C — Email Assistant-visibility
+ Deploy tasks (code-page + bff) after deployable waves
+ Wrap-up (test-diet, lessons-learned, INDEX update, archive)
```

Phasing E→A→B→D→C is owner-accepted (spec Owner Clarifications).

### Critical Path

**The `ConversationPane.tsx` sequential spine**: workstreams E, A, B, and D all edit `ConversationPane.tsx`, so those client tasks are `parallel-safe:false` **among themselves** and run sequentially. Tasks in *other* files parallelize alongside that spine:
- BFF concerns (A-server, D-server tasks) — different files, parallelizable
- `HistoryOverlay.tsx` (D-037) — separate file, parallel with the ConversationPane spine
- Shared-lib types (`contextType` B-020, `SerializedWidgetState` Email C-040) — parallel
- Catalog data (B-021, Reanalyze binding) — Dataverse, no code deploy, parallel

**High-Risk Items**:
- FR-D1 tab-restore overwrite hazard → clear/remount workspace first; regression test (task 035, opus/xhigh)
- FR-D10 Cosmos retention → spike per-doc TTL feasibility first; idempotent cleanup (task 033, opus/xhigh)
- FR-A4 ADR-015 privacy boundary → bounded compact shape; documented (task 012, opus/xhigh)
- FR-C1 email visible-state must reach data held in `useEmailWorkspaceRecord`, not the widget wrapper (task 042, opus/xhigh)

---

## 4. Phase Breakdown

### Phase 1: E — Remove Notifications banner

**Objectives:** Remove the spine-driven proactive-suggestion surface (banner + cards) from the Assistant; preserve the notification spine + shared `NotificationsClient` + Daily Briefing.

**Deliverables:**
- [ ] Delete `useSuggestionCards.tsx` + `SuggestionCard.tsx` (+ tests)
- [ ] Remove hook block (`ConversationPane.tsx:941-987`) + render site (`:2649`)
- [ ] Preserve `notificationsBootstrap.ts` / `getNotificationsClient`, `sprk_notificationoutbox`, `/api/notifications/*`, Daily Briefing
- [ ] Regression check: Communications badge/toast + Daily Briefing render + spine unaffected

**Inputs**: `src/solutions/SpaarkeAi/src/components/conversation/` (ConversationPane, useSuggestionCards, SuggestionCard).
**Outputs**: banner/cards removed; deploy code page.

### Phase 2: A — Active-tab awareness (focus-stamp)

**Objectives:** Feed the real focused tab into each chat turn; replace the server `UpdatedAt` "active" heuristic with the explicit focus-stamp.

**Deliverables:**
- [ ] FR-A1 — `ConversationPane` subscribes to `workspace.active_widget_changed`, holds focus ref (+ seam test)
- [ ] FR-A2 — `handleDecorateOutboundBody` adds `activeContext` to outbound body (no `SprkChat` fork)
- [ ] FR-A3/A4 — server threads `activeContext` through `ChatSendMessageRequest` → `CreateAgentAsync` → `BuildWorkspaceStateBlock`; prefers focus-stamp; active = compact-content, background = metadata-only

**Inputs**: ConversationPane, PaneEventTypes, `SprkChatAgentFactory.cs`, `ChatEndpoints.cs` (DTO).
**Outputs**: focus-stamped chat turns; deploy code page + bff.

### Phase 3: B — Proactive follow-ons

**Objectives:** On a tab's first open, run one grounded suggestion turn filtered by the tab's context type; cache per `tabId`; render ≤3 dismissible chips via the reactive chip surface.

**Deliverables:**
- [ ] FR-B1 — closed `contextType` set { email, document, compose-doc, matter-grid, dashboard, calendar } on widget registry metadata
- [ ] FR-B2 — context-type relevance tags on catalog Bindings + "Reanalyze" Binding (Dataverse, analyst-editable, no deploy)
- [ ] FR-B3/B5 — first-open grounded turn, cached per `tabId`, no re-fire on switch-back; ≤3 dismissible chips via `useConsumerChips`
- [ ] FR-B4 — manual "refresh suggestions" affordance
- [ ] FR-B6 — dev-visible proactive-selection trace

**Inputs**: widget registry + types, catalog (Dataverse), ConversationPane, useConsumerChips.
**Outputs**: proactive chips; deploy code page + catalog data.

### Phase 4: D — History robustness & true resume

**Objectives:** Route History through the rich restore path; fix transcript-write reliability, 404 contract, titles, retention; rebuild the history UX.

**Deliverables (server):**
- [ ] FR-D2 — awaited/confirmed Cosmos write for `messages[0]`; remaining turns async; latency budget
- [ ] FR-D3 — `GET .../history` returns 404 on genuinely-missing session
- [ ] FR-D4 — stored writable `title` + `PATCH /api/ai/chat/sessions/{id}` rename + cheap grounded title-gen
- [ ] FR-D10 — retention: per-doc TTL extension on filing (preferred), else remove container TTL + `expiresAt` + idempotent cleanup job
- [ ] FR-D9 — rename Promote → "Set related record"; prompt existing-vs-create-document; reuse promote endpoint

**Deliverables (client):**
- [ ] FR-D1 — route `handleSelectHistorySession` through rich `/restore`+`/tabs`, clearing/remounting workspace first; regression test the overwrite hazard
- [ ] FR-D5 — rehydrate attachment chip from server `UploadedFiles` manifest
- [ ] FR-D6/D7/D8 — `HistoryOverlay` rebuild: overflow ⋮ (Open/Rename/Set related record/Delete), wire DELETE, remove up-arrow, preview + tab summary, Today/Yesterday/This-week grouping + search
- [ ] FR-D11 — "Reanalyze" chip on `document` context re-runs the playbook

**Inputs**: `ChatSessionManager.cs`, `SessionPersistenceService.cs`, `ChatEndpoints.cs`, `AnalysisEndpoints.cs`, ConversationPane, HistoryOverlay, WorkspacePane/TabManager, Cosmos infra.
**Outputs**: true resume + robust history; deploy code page + bff.

### Phase 5: C — Email Assistant-visibility

**Objectives:** Let the Assistant see an open email's compact shape; full body on-demand via `eml-render`.

**Deliverables:**
- [ ] FR-C2 (client) — `Email` variant in `SerializedWidgetState` union + discriminator guard
- [ ] FR-C2 (server) — `Email` variant in `WorkspaceTabVisibleState` + `TryDeriveVisibleState`/`FormatVisibleStateFields`
- [ ] FR-C1 — `getAgentVisibleState()` on `EmailWorkspaceWidget` (compact shape from `useEmailWorkspaceRecord`)
- [ ] FR-C3 — declare `email` context type
- [ ] FR-C4 — on-demand full body via existing `eml-render` (verify)
- [ ] FR-C5 — DEFERRED to email-r5 (no task)

**Inputs**: `SerializedWidgetState.ts`, `WorkspaceTabVisibleState.cs`, `SprkChatAgentFactory.cs`, `EmailWorkspaceWidget.tsx`, `useEmailWorkspaceRecord.ts`.
**Outputs**: email-visible Assistant; deploy code page + shared lib + bff.

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Cosmos DB (StoredSession) | GA | Low | Reuse existing; retention change is spike-gated |
| Redis (hot tier) | GA | Low | Existing 24h sliding TTL |
| SharePoint Embedded (eml-render) | GA | Low | Existing endpoint |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| email-communication-solution-r5 widgets | `src/client/shared/Spaarke.Communication.Components/` | ✅ Merged |
| R1 dispatch spine + catalog | `Services/Ai/PublicContracts/`, `sprk_playbookconsumer` | Production |
| Rich restore path | `useSessionRestore.ts` / `ThreePaneShell.tsx` | Production |
| Reactive chip surface | `useConsumerChips.tsx` / `sprk_chiptransitions` | Production |

---

## 6. Testing Strategy

**Unit / seam tests**:
- Focus-stamp subscriber updates ref on `active_widget_changed` (FR-A1)
- Suggestion cache keyed by `tabId`; no re-fire on switch-back (FR-B3)
- Awaited `messages[0]` Cosmos write present before request completes (FR-D2)
- Email visible-state deserializes structurally (no fallback to Dashboard) (FR-C2)

**Integration tests**:
- Rich-path restore: reopen restores chat + tabs + document + redline; tab set not corrupted by prior session (FR-D1 overwrite regression)
- 404-on-missing-session → stale-session recovery fires (FR-D3)
- "Set related record" association + TTL-extension resumable after >90 days (FR-D9/D10)

**UI tests**:
- History rows: descriptive title + preview + tab summary; rename/delete; up-arrow gone (FR-D6/7/8)
- ≤3 relevant chips on email tab open; dismissible (FR-B5)

**Regression**:
- Notifications banner gone; spine + Daily Briefing + Communications badge still work (FR-E1)

---

## 7. Acceptance Criteria

Per README graduation criteria + spec §Success Criteria (1–9). Each BFF-touching task additionally verifies `dotnet publish` ≤60 MB and reports the delta in task notes.

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | FR-D1 tab-restore overwrite corrupts stored tabs | Med | High | Clear/remount first; regression test; opus/xhigh |
| R2 | FR-D10 retention change causes data loss | Low | High | Spike per-doc TTL; idempotent past-due-only cleanup; opus/xhigh |
| R3 | FR-A4 leaks background-tab content (ADR-015) | Low | High | Bounded compact shape; background metadata-only; documented bound |
| R4 | FR-C1 can't reach email data from widget wrapper | Med | Med | Derive compact shape from `useEmailWorkspaceRecord` |
| R5 | ~~Merge collision with notification-spine-r1~~ (RETIRED) | — | — | Verified 2026-08-05: notification-spine-r1 + analysis-hub-r1 both fully merged to master (stale INDEX.md rows). R2 builds on their landed state; no live overlap. Normal `/conflict-check` hygiene only |
| R6 | BFF publish size creeps >60 MB | Low | Med | No new packages anticipated; measure per BFF task |

---

## 9. Next Steps

1. **Review this plan.md** + TASK-INDEX.md
2. **Begin** Phase 1 (task 001) via `task-execute` — no cross-worktree blocker (notification-spine-r1 + analysis-hub-r1 both merged to master); `/conflict-check` before BFF/ConversationPane PRs as normal hygiene

---

**Status**: Ready for Tasks
**Next Action**: Generate task files (task-create) → execute Phase 1

---

*For Claude Code: load relevant sections when executing tasks.*
