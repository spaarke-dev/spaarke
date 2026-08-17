# Task 011 — Close the id-less mount dedup vector (FR-07c)

> Phase 1 (Save-Identity Fix / UC-8) · sonnet@xhigh · FULL rigor · 2026-08-15

## Root cause + fix

The single id-less mount door was `toAssistantInsertPayload` (ComposeWorkspace.tsx): the **legacy (no-`ledgerRef`) Flow-5 assistant-insert** fell back to `documentRef: event.documentRef ?? { speDriveItemId: '' }` — an empty dedup identity that historically skipped dedup. (The `ledgerRef` path already mounts via `materializeComposeDraftFromLedger` → `mountDraftHtml`, which task 010 made carry a `composeLogicalId`; and every transient/draft mint site was covered by task 010.)

**Fix**: at the `onAssistantInsert` call site, compute a dedup-identity fallback and pass it into `toAssistantInsertPayload`:
```ts
const fallbackRef = state.documentRef        // mounted doc — already carries task-010 composeLogicalId
  ?? { speDriveItemId: '', composeLogicalId: startNewComposeLogicalId() };  // nothing mounted → mint+persist
```
`toAssistantInsertPayload` gains an optional `fallbackDocumentRef` param used before the empty sentinel. So the legacy assistant-insert now **always** carries a dedup identity: the currently-mounted document's (the common case) or a freshly-minted-and-persisted logical id (from-scratch case). No second identity derivation — reuses task 010's `startNewComposeLogicalId` + `getComposeLogicalIdentity` accessor verbatim.

## Verification

- `composeIdentity.test.ts` +1 test (FR-07c): a `pendingAssistantInsert` payload with `{ speDriveItemId: '', composeLogicalId }` retains a non-empty dedup identity through the reducer (`getComposeLogicalIdentity` → the logical id, not `''`). Combined with the existing accessor `''`-guard test, the mechanism is proven. **15/15 green.**
- Typecheck: no new errors in the touched code (the standing `@spaarke/*` standalone-resolution noise is pre-existing/unrelated).
- **Standalone-env limitation (honest)**: the full-component id-less-door e2e (POML `<ui-tests>`: trigger the door → save → re-mount → save → assert ONE identity) can't run in this standalone jest env because `ComposeWorkspace.tsx` imports `@spaarke/auth` (unlinked here — the same gap that blocks 15 `ComposeWorkspace.*.test.tsx` suites; they run in CI with workspaces linked). The reducer/accessor-level proof + typecheck cover the mechanism; the full-flow assertion is the deferred UAT ui-test.
- Client-only; no server/BFF change; `docxBridge.ts` untouched.

## Relationship to 013 + 071

This is the **client-side** vector. The **server-side** atomic upsert (the concurrent/retry vector no client change can close) is task 013. Task 071 (Restore-from-Source) shares this mount-lifecycle root cause.
