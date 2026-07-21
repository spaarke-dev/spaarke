# Task 024 — NewThreadModal + startDirectThread client wrapper (FR-11)

**Status**: completed 2026-07-21 · FULL rigor · sonnet/high · parallel Wave 11 (group C) · Step 9.5 gate RUN — verdict **Fix-first** (3 Majors + 2 minors fixed) · §6.5 Path-A exception ACCEPTED (name/description)

## What shipped
- **New `NewThreadModal`** (`components/NewThreadModal/`): a Fluent v9 Dialog to start (find-or-create) a 1:1 direct thread. Reuses `RecipientField` + `BodyEditor` + `AssociationChips` (NFR-06, no dup). Optional host-supplied `regarding` (read-only chip, ADR-024). Optional body → posted as the first message via the EXISTING send engine.
- **`startDirectThread` wrapper** (additive) in `services/communicationThreadListApi.ts` — POSTs `{ otherParticipantSystemUserId }` to `/api/communications/threads/direct`, parses `{ threadId, callerSystemUserId, otherParticipantSystemUserId }`, throws the module-typed `CommunicationThreadListError` on non-2xx. No client-side dedup / second create path.
- **`ConversationWorkspace` untouched** — the shell already exposes `onCreateThread` (task 012); wiring (open state + `onThreadCreated` selection) is host-controlled via props.

## Backend contract reality (the key finding)
`StartDirectThreadRequest` = `{ otherParticipantSystemUserId: Guid }` (single, required). Response = `{ threadId, callerSystemUserId, otherParticipantSystemUserId }`. The endpoint IS genuinely find-or-create (`IDirectThreadAccessService.FindOrCreateDirectThreadAsync`, ordered-pair dedup) — **no escalation**. It is deliberately NARROW: **no server field for name, description, regarding, a recipient list, or a body**; the caller is resolved server-side.

## How the modal maps to the real contract
- **Participant** (reused `RecipientField`) → the ONE directory-resolved `systemuser` (`entityType==='systemuser'`, `sourceId`=systemuserid, FR-10 enrichment) → `otherParticipantSystemUserId`. Free-text / contact recipients are rejected by validation; **>1 resolved systemuser is blocked** ("Direct conversations are 1:1").
- **Regarding** — OPTIONAL, host-supplied, read-only `AssociationChips` (ADR-024, no second mechanism). Persists via the first message's `associations` (endpoint has no thread-level regarding field) — so when `regarding` is supplied a body is REQUIRED (submit blocked otherwise) to guarantee the link is never silently dropped.
- **Body** (reused `BodyEditor`) — OPTIONAL; when present, after find-or-create it posts the first message via `sendCommunication({ communicationType:'message', threadId, associations })` (ADR-045 — existing send path, ACS branch; not a `/threads/direct` field).

## §6.5 Path-A exception — name/description OMITTED (ACCEPTED)
The shipped `/threads/direct` contract cannot express a thread name or description, and inventing a client sink would be dishonest UX. The gate reviewer confirmed the **rename-after-create pivot (Path C) is UNSAFE**: `StartDirectThreadResponse` carries no create-vs-find discriminator, so a rename would clobber an existing found thread's name — hostile to find-or-create. Direct 1:1 threads are identified by their participants and don't semantically need a user name (Teams DM model). **Accepted as Path A**; if product later wants named direct threads, the clean upgrade is **Path B (backend amendment)** — add `created: bool` to the response (enabling safe rename-on-create-only) or an optional `name` honored only on creation. Tracked as ISS-004 ([#670](https://github.com/spaarke-dev/spaarke/issues/670)).

## Step 9.5 gate outcome — Fix-first, all resolved
- **M1 (Major, FIXED)**: partial success orphaned the thread — if `/threads/direct` succeeded but the first-message send threw, `onThreadCreated` was never called (thread created but unselected; error implied total failure). Now two-phase: create-failure is fatal (stay open, no selection); send-failure-after-create still calls `onThreadCreated(threadId)` (shell selects the thread) + shows a NON-FATAL "created, but first message couldn't be sent — try again in the thread."
- **M2 (Major, FIXED)**: `regarding` shown but silently dropped on empty-body → now requires a body when `regarding` is supplied (submit blocked with a clear message).
- **M3 (Major, FIXED)**: multiple resolved users silently reduced to the first → now blocks submit when >1 resolved systemuser.
- **N1 (FIXED)**: setState-after-unmount → `mountedRef` guard on post-await local setState (host callbacks still fire).
- **N2 (FIXED)**: in-flight not announced → dropped the static button aria-label (visible "Creating…" is the name) + `aria-busy={submitting}`.
- ADR verdict: ADR-028 PASS · ADR-045 PASS · ADR-024 PASS (mechanically; regarding rides the first message) · NFR-06 PASS · ADR-021 PASS. Contract fidelity PASS (no invented fields, no client dedup).

## Verification
- `npm test -- src/components/NewThreadModal` → **10 passed** (incl. the 3 new M1/M2/M3 edge tests).
- Combined with task 023: **63 tests** across 6 suites. `tsc` 2 pre-existing unrelated errors only. `eslint` clean.

## Acceptance criteria
Optional record association (record-less allowed) ✅ · reuse RecipientField/BodyEditor ✅ · find-or-create via POST /threads/direct, existing thread returned (no dup) ✅ · shell selects returned thread + validation + loading/error/in-flight ✅ · keyboard/ARIA/focus ✅ · dark mode ✅ · tests pass ✅. **Deviation (Path A)**: name/description not surfaced (endpoint limitation); participants constrained to a single resolved systemuser (endpoint is 1:1).
