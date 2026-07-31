# Task 030 — G8 External-change refresh + remount banner — Deviations & Notes

> **Status**: ✅ COMPLETE · 2026-07-30 · FULL rigor · sonnet/high (run on Opus 4.8 session)

## What the investigation found (before implementing)
- **Server delivery/subscription leg was ALREADY wired in code.** `HandleSpeDocChangedWebhookAsync`
  (ComposeEndpoints.cs:451) is fully implemented (handshake → clientState → per-notification
  driveId→container resolve → `EnumerateChangesAsync` → 202); `SpeSyncOrchestrator.EnsureSubscriptionAsync`
  is called from the Compose load endpoint (:1142); renewal cron + DI registered (ComposeModule.cs:50-51).
  The only true "E2E-pending" is the Graph→BFF network delivery, gated on the
  `Compose:Webhook:{SigningKey,ClientState,NotificationUrl}` Key Vault secrets (**owner task 056 / DEF-03**) —
  **operator config, not code**, with a fail-safe poll fallback (already contract-tested).
- **Client gap was real**: external-change detection is poll-on-focus (`runReturnFromWordCheck`
  ComposeWorkspace.tsx:893) but it did NOT remount the projection or show a "document updated" banner.
- **No dirty-guard existed**: the only remount primitive is `requestLoad` (resets to INITIAL_STATE → a
  blind remount discards unsaved edits). Dirty state (`editorRef.current.isDirty()`) was used only to
  ENABLE Save, never to guard a destructive action.

## What shipped
**Client** (the real code gap):
- `ComposeExternalChangeBanner.tsx` (NEW) — standalone Fluent v9 `MessageBar` banner (mirrors
  `ComposeReanchorBanner`), fixed FR-07 wording **"Document updated from document management system version"**.
  Two shapes: CLEAN → informational (parent already remounted); DIRTY → `warning` + an explicit **Reload**
  action + an "unsaved edits" notice (NFR-08 — the user chooses discard-and-remount; never silent).
- Reducer (`ComposeWorkspace.types.ts`): new `externalChangePending` state; `requestLoad` gained an
  optional `externalChange` flag (carried through the clean-editor auto-remount so the banner still shows
  after `loadSucceeded`); new `externalChangeDetected` / `externalChangeDismissed` actions.
- `runReturnFromWordCheck` (`ComposeWorkspace.tsx`): on `checkChanges.changed`, branch on the authoritative
  `editorRef.current.isDirty()` — **CLEAN → dispatch `requestLoad {externalChange:true}`** (transparent
  remount from server-authoritative bytes + banner); **DIRTY → dispatch `externalChangeDetected`** (banner
  with Reload, NO remount — unsaved edits preserved). Detection resolves by document/version identity
  (checkChanges), never content (NFR-02/I-7).
- Banner mounted in `ComposeWorkspace` next to `ComposeReanchorBanner`; Reload dispatches
  `requestLoad {externalChange:true}`, Dismiss dispatches `externalChangeDismissed`.
- The existing 423/409 lock message (`ComposeBannerStack`) is untouched — still renders; after a lock
  releases a focus poll can remount.

**Server**: NO new production code (the delivery leg was already wired). Added the missing
**webhook-receiver seam test** — the delivery-leg proof criterion 4 asks for.

**Tests**:
- `tests/integration/seam/Compose/ComposeSpeDocChangedWebhookSeamTests.cs` (NEW, 4 through-the-wire slices):
  validation-handshake echoes token (200); signed notification + valid clientState → **202** (delivery
  reaches the handler); signed + WRONG clientState → 401; WRONG signing key → 401 (HMAC filter). Own
  `ComposeWebhookReceiverFixture` boots the REAL `SpeSyncOrchestrator` with the two `Compose:Webhook:*`
  TEST keys the poll fixture omits; HMAC computed exactly as production validates it. **4/4 green.**
- `ComposeExternalChangeBanner.test.tsx` (NEW, 7 tests): render-only-when-pending, fixed wording,
  clean-vs-dirty shapes (Reload only when dirty), dismiss, dark-mode. **7/7 green.**

## Escalation trigger — did NOT fire (and why that's correct)
The trigger: "If completing the remount would discard unsaved local edits, STOP and escalate." The
NFR-08-safe design (which the task's own ui-test criterion describes: *"if it would [drop edits], the flow
escalates rather than remounts"*) is a **dirty-guard**, not an operator stop: a CLEAN doc remounts
transparently; a DIRTY doc is **deferred to the user** via the non-blocking banner's explicit Reload
action. Unsaved edits are never silently dropped — the remount only auto-applies when safe. This is the
guarded path the trigger requires, so no operator escalation was warranted.

## Verification
- New webhook seam **4/4**; new banner **7/7**; toolbar **39/39** (unchanged); full Compose C# suite
  **814/814** (810 prior + 4 webhook — R4.5 non-regression intact); byte-diff corpus **24/24**.
- **Publish size unchanged: 48.13 MB** — task 030 added ZERO BFF production code (webhook already wired;
  the new .cs is a TEST in the test project, not published). No new runtime package. ≤60 ceiling.
- **ArchTests unchanged** (3 pre-existing failures) — no BFF production code changed.
- Client typecheck: only the known pre-existing `@spaarke/*`-unlinked cascades remain; zero new errors.

## Step 9.5 quality gates (applied)
- **code-review**: banner is pure presentational (mirrors ComposeReanchorBanner); the guarded remount
  reads the authoritative `isDirty()` (same source the save path uses); reducer is pure; no security issue;
  no AI code smells.
- **adr-check**: ADR-049 (remount re-projects server bytes, client stays view+controller, no byte
  authoring; I-7 detection by identity), ADR-007 (no new Graph code — the webhook Graph hop was already in
  the endpoint layer), ADR-038 (webhook receiver seam slice, no banned shapes), ADR-021 (banner theme
  tokens + dark-mode test), ADR-013 (no AI type), §10 (no new server production code / endpoint / package) —
  all clean.

## PR obligations
- **Placement Justification (§10)**: no new server code — the receiver + subscription were already wired;
  this task added a receiver seam test + client remount/banner. New client component justified in the
  banner's header (§11): distinct payload (external-change, dirty-gated reload) not modeled by
  ComposeReanchorBanner / ComposeBannerStack.
- `/conflict-check` before the shared-client PR (soft-warn: analysis-hub-r1 #694 shares
  Spaarke.Compose.Components — NFR-09 reopen-restore parity covered by the 814 suite; ComposeEditor.tsx
  NOT modified here, so no overlap with task 031's editor region).
- Webhook DELIVERY (Graph→BFF network) remains E2E-pending on owner task 056 secrets — documented, not a
  code gap; the receiver + poll fallback + subscription-origin call are all wired + tested.
