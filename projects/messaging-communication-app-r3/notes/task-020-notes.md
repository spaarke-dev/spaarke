# Task 020 — Extend SendEmailDialog/EmailComposer: thread id + regarding + record link (FR-07/FR-19)

**Status**: completed 2026-07-21 · FULL rigor · **opus**/high · Step 9.5 gate RUN (1 Major fixed, 4 Minor fixed) · `/conflict-check` HARD-WARN (see bottom)

## What shipped (additive only)
An email opened from a conversation now (a) carries the active `threadId` into the send payload so the backend pins it (FR-19), (b) auto-associates to the regarding record via the EXISTING association mechanism (ADR-024), and (c) can render an optional record link.

- **Files**:
  - `EmailComposer/EmailComposer.types.ts` — new `IComposerRecordLink`; optional `threadId?` + `recordLink?` on `IEmailComposerProps`.
  - `EmailComposer/EmailComposer.tsx` — `mapStateToSendRequest(state, threadId?)` carries `threadId`; `send()` passes `props.threadId` (added to deps); renders `recordLink` (with `isSafeHref` scheme guard).
  - `EmailComposer/wrappers/SendEmailDialog.tsx` — new optional `threadId?`, `regarding?` (`ISendEmailDialogRegarding`), `recordLink?`; folds `regarding` into the existing `associations` (case-insensitive GUID dedup); passes through.
  - `services/communicationApi.ts` — **doc-accuracy fix** (M1): the `threadId` field doc no longer says "Ignored for Email sends" — it now documents FR-19 (email honors explicit threadId via `AssignExplicitThreadAsync`, same path) + the bare-GUID contract.
  - `Sprk.Bff.Api/.../SendCommunicationRequest.cs` — **comment-only** doc-accuracy fix (same stale "Ignored for Email sends" → now documents FR-19). De-minimis BFF touch: no endpoint/service/DI/package/size/CVE change (§10 checklist items not triggered).
  - `__tests__/SendEmailDialog.threadRecord.test.tsx` — new (jest).

## Key design decisions (blast-radius discipline)
- **No 6th send path (ADR-045)**: `threadId` is a static prop merged into the existing `mapStateToSendRequest → sendCommunication` payload. Backend already honors it on the email branch (task 005/FR-19, `AssignExplicitThreadAsync`) — confirmed, no escalation needed.
- **No second association mechanism (ADR-024)**: `regarding` is wrapper-level sugar converted to ONE `ICommunicationAssociation` merged into the existing `associations` prop → flows through `AssociationChips` + the send payload unchanged. The engine gained NO association logic.
- **`threadId`/`recordLink` are engine props; `regarding` is wrapper-only** (it's association sugar; the engine stays association-centric).
- **No-regression (NFR-08)**: all new props optional; for callers passing none, `mergedAssociations` yields `undefined` → `initialState (props.associations ?? [])` is identical to before. Regression guard = the task-010 characterization test (`SendEmailDialog.characterize.test.tsx`) — still green.
- **EmailComposer.tsx edited though not in POML `<outputs>`**: directional mode + the acceptance criteria require the send payload to carry `ThreadId`, which is built in the engine. Additive optional-prop change; noted here.

## Step 9.5 gate outcome (adversarial review) — Fix-first, all resolved
- **Major (FIXED)**: the shared `threadId` field doc (client `communicationApi.ts` + server `SendCommunicationRequest.cs`) said "Ignored for Email sends" — the exact opposite of FR-19 (a latent regression trigger). Both corrected.
- **Minor (FIXED)**: `threadId` is `string` client / `Guid?` server — documented the bare-GUID contract; tests now use GUID-shaped ids.
- **Minor (FIXED)**: `recordLink.url` rendered into `<a href>` with no scheme guard → added `isSafeHref` (blocks `javascript:`/`data:`/`vbscript:`, degrades to a non-clickable label); added a test.
- **Minor (FIXED)**: regarding dedup was case-sensitive on the GUID → now case-insensitive; test proves it with differing-case ids.
- **Minor (FIXED, docs)**: `regarding` "captured at open" note; `recordLink` JSDoc "header area" → "below the linked-record chips".
- ADR verdict: ADR-045 PASS · ADR-024 PASS · ADR-028 PASS · ADR-021 PASS · no-regression PASS.

## Verification
- `npm test -- src/components/EmailComposer` → **117 passed** (10 suites, incl. characterization regression guard + new threadRecord test).
- `tsc --noEmit` → 2 pre-existing unrelated sibling-dist errors only. `eslint` clean.

## Acceptance criteria — all met
Optional props / no regression ✅ · auto-associate via existing mechanism ✅ · send payload carries ThreadId via existing path ✅ · record link renders ✅ · dark mode ✅ · tests pass incl. no-regression ✅.

## ⚠️ /conflict-check — HARD-WARN (coordination required before PR merge)
Branch is **54 commits behind origin/master**; master contains email-r4 work touching the SAME shared-engine files, notably `423e9c7b8 feat(pcf): include source attachments on Reply/Forward/ReplyAll (email-r4 W10)` → `EmailComposer.tsx`. No competing OPEN PR overlaps, but master itself has diverged on `EmailComposer.tsx`/`.types.ts`. My changes are additive, so reconcilable, but **origin/master must be merged (resolving EmailComposer.tsx) before this branch's PR merges** — and ideally before Phase 3 tasks 021/022 (which also edit this engine) pile more edits on a stale base. Recommend merging master next.
