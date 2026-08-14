# Phase C (Email Assistant-visibility) — architecture gap surfaced by task 041

**Date**: 2026-08-06 · **Status**: 🔔 ESCALATED to owner — Phase C blocked on a re-scope decision
**Discovered by**: task 041 (server Email visible-state) escalation + main-session verification

---

## TL;DR
Tasks 040/041 correctly added the **Email visible-state OUTPUT shape** (client `SerializedEmailState`, server `WorkspaceTabVisibleState.Email` + `FormatVisibleStateFields`). But **FR-C1 does not function end-to-end** with the current 040/041/042 decomposition, because the *input* side is missing and an ADR-015 invariant blocks the shortcut task 042 assumes.

## The ADR-015 invariant (locked in Phase A, task 012)
The server derives **all** agent-visible tab content from **persisted `WorkspaceTab.widgetData`** via `SprkChatAgentFactory.TryDeriveVisibleState(tab.WidgetData)` (SprkChatAgentFactory.cs:1602). The client-supplied `ChatActiveContext.CompactState` is **deliberately NOT injected** into the prompt — documented emphatically in 3 places (ChatEndpoints.cs:3412-3476): *"the server does NOT trust the client-supplied CompactState as prompt content."* `ActiveContext.TabId` is the only load-bearing field (flips the "(active)" label + `contentVisible`). This is the ADR-015 Path-A privacy/integrity boundary.

## Why email has no channel
- The persisted `WorkspaceTabWidgetData` closed union has **only 4 subtypes** (Summary/DocumentViewer/Dashboard/Table) on BOTH sides — client `WorkspaceTab.ts:216` + server `WorkspaceTabWidgetData.cs:27-31`. **No `EmailTabWidgetData`.**
- `EmailWorkspaceWidget` is a **self-driving direct widget**: it ignores `WorkspaceWidgetProps` (`data`/`tabId`/`isActiveTab` "intentionally unused") and reads the email record itself via `useEmailWorkspaceRecord` from Xrm host context. So the email content **never enters persisted `widgetData`**.
- The registry contract is `RegistryGetAgentVisibleState = (widgetData) => SerializedWidgetState` — the client derivation ALSO reads from `widgetData`, mirroring the server. An email tab whose `widgetData` lacks email fields can derive nothing either way.
- Net: 041 correctly could not add a `TryDeriveVisibleState` Email case (nothing to derive from) and escalated instead of fabricating. Task 042 as written (client `getAgentVisibleState()` reaching `useEmailWorkspaceRecord`) feeds the CompactState channel the server ignores → **email content still never reaches the prompt.**

## What still works (keep)
- **040** (client `SerializedEmailState` + `WorkspaceTabWidgetType` alignment + `emailWidgetVisibility`) — correct, reused under any path. ✅
- **041** shape half (`WorkspaceTabVisibleState.Email` record + `FormatVisibleStateFields` Email case + regression trip-wire test) — correct, reused under any path. The `TryDeriveVisibleState` producer is the only missing piece. 🔄

## Resolution options (owner decision)

### Path 1 — Persisted `EmailTabWidgetData` carrier (ADR-015-clean; follows the 4-widget pattern) — RECOMMENDED
Add `EmailTabWidgetData {subject,from,date,threadId,snippet}` to the persisted union (client `WorkspaceTab.ts` + server `WorkspaceTabWidgetData.cs` `[JsonDerivedType(...,"Email")]`); populate the email tab's `widgetData` from `useEmailWorkspaceRecord` at open/update (client writes it so it persists); add `TryDeriveVisibleState` Email case (041's missing piece) + the client `getVisibleState(widgetData)`. Email then behaves exactly like the other 4 widgets — content persisted, both derivations work, ADR-015-authoritative, survives resume/warm-restore. **Bigger than the original 042** (adds a persisted carrier + client population), but correct + robust. Fits standing "robustness over build-ease."
- *Variant 1b*: persist only the email **id** in `widgetData` and have the server fetch compact metadata during derivation (most ADR-015-clean, but adds a per-turn server fetch on the prompt-build hot path — latency risk).

### Path 2 — ADR-015 amendment: trust `CompactState` for the ACTIVE tab only (§6.5 Path B)
Amend ADR-015 Path A so the active tab's client-supplied `CompactState` IS injected (active-tab-as-consent). Then 042's `getAgentVisibleState()` output rides `CompactState` → server emits it. **Smaller code change** but reverses a deliberate Phase-A decision and weakens the server-authoritative invariant (client bytes become prompt content). Higher governance cost + weaker security posture.

## Recommendation
**Path 1** (persist `EmailTabWidgetData`, populate from the hook at tab open, add both derivations). Re-scopes Phase C: 040 ✅ / 041 shape ✅ + add carrier + population + `TryDeriveVisibleState` case; 042 becomes "populate widgetData from `useEmailWorkspaceRecord` + client `getVisibleState`" rather than the CompactState route. Deploy 043 unchanged.
