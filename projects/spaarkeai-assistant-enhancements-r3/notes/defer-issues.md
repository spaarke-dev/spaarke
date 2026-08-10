# Deferred Work & Issues — spaarkeai-assistant-enhancements-r3

> Per CLAUDE.md §11 tracking obligation: every entry names a CONCRETE behavior/contract that fails without the work. Mirror to GitHub Issues via `/defer` at project close.

---

## D-1 — FR-02 visibleToAssistant toggle does not persist through the live-tab path (from task 011 re-point)

- **Discovered**: 2026-08-10 (task 011, owner-approved Option-A re-point).
- **Concrete failure**: A user toggles a layout tab's "visible to assistant" OFF; after reload the tab **re-appears** in the Assistant's workspace-state block. Cause: the awareness block is now fed from the live `StoredSession.Tabs` (`ISessionPersistenceService`), and `StoredWorkspaceTab` has **no `visibleToAssistant` field**, so `MapStoredTabsToWorkspaceTabs` defaults every live tab to `VisibleToAssistant = true`. Task 010 implemented the toggle against `WorkspaceTab.VisibleToAssistant` (correct at the block layer) but the flag is not carried on the live persistence path.
- **Fix required**: additive `visibleToAssistant` field on `StoredWorkspaceTab` + the `PATCH /sessions/{id}/tabs` DTO, and have the SpaarkeAi client send the per-tab flag on tab-persist. Then `MapStoredTabsToWorkspaceTabs` reads it instead of defaulting true. Cross-surface (BFF DTO + SpaarkeAi client) — bounded, additive.
- **Scope note**: FR-01 (Assistant SEES the tabs) IS delivered by the re-point. FR-02 (toggle-off persists hidden) is the partially-deferred half. Owner aware (AskUserQuestion 2026-08-10).
- **Severity**: Medium. Blast radius bounded to `{type,label}` identity (no content leaks post-trim).

## D-2 — `WorkspaceTab.WidgetData` (required) set to null via `widgetData!` in the mapping (from task 011 Step 9.5 finding #4)

- **Concrete failure**: `MapStoredTabsToWorkspaceTabs` assigns `null` to the `required` non-nullable `WorkspaceTab.WidgetData` for kind-less/undeserializable tabs. `TryDeriveVisibleState` tolerates null (tested), so no NRE today — but any future consumer assuming non-null on a `required` property will NRE.
- **Fix required**: make `WorkspaceTab.WidgetData` nullable, or map to a typed empty/None variant instead of `null!`.
- **Severity**: Low (latent).

## D-3 — Active-item handle id staleness on async compose-session backfill (from task 001 Step 9.5 finding)

- **Concrete failure**: when a compose tab is created via `widget_load` without a `composeSessionId` (backfilled asynchronously), `deriveActiveItemHandle` resolves the handle `id` to `tab.id`; the later `composeSessionId` does not update the handle, because a `widgetData` mutation on the already-active tab does not change `tabState.activeTabId` (the effect dep). Still exactly one valid handle — but consumers keying off the compose **session id** specifically see the fallback id.
- **Fix required (task 026 to consider)**: include the compose session-id in the tab-focus effect deps or publish on session-id backfill.
- **Severity**: Low (fidelity nuance; no acceptance-criterion break).
