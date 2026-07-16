# Task 023 — EmailComposer + wrapper + communicationApi unit tests (W2)

**Rigor**: TEST-MODIFYING (code-review + adr-check ran UNCONDITIONALLY at Step 9.5).
**Scope**: client-only (`@spaarke/ui-components`); disjoint from the server/BFF track.

## Files created (8)

| File | Tests | What it protects |
|---|---|---|
| `components/EmailComposer/__tests__/EmailComposer.reducer.test.ts` | 26 | `initialState` per mode + reducer action/transition matrix |
| `components/EmailComposer/__tests__/validation.test.ts` | 17 | `validateState` — 7 client-raisable canonical codes + `forSend` gating |
| `components/EmailComposer/__tests__/modes.test.ts` | 15 | `dedupSubjectPrefix`, `derive{Reply,Forward,Draft}State`, `mapStateToDraftUpdate` |
| `components/EmailComposer/__tests__/RecipientField.test.tsx` | 10 | `;`/`,`/mixed parsing, chip removal, a11y labels, `onSearch` boundary |
| `components/EmailComposer/__tests__/AttachmentList.test.tsx` | 8 | 150 / 35 MB / 25 MB caps, source badges, remove, forward checkbox |
| `components/EmailComposer/__tests__/imperative-handle.test.tsx` | 9 | `getState`/`validate`/`send`/`saveDraft` via ref |
| `components/EmailComposer/__tests__/wrappers.test.tsx` | 11 | mount-locking + callback mapping for all 3 wrappers |
| `services/__tests__/communicationApi.test.ts` | ~20 | `SendCommunicationError.fromResponse` (JSON + non-JSON), throw-on-non-2xx, arg guards, attachment pass-through |

**Total: 100 tests across 8 files — all green.** Full-suite run: my 8 files pass; the 26 pre-existing failing suites (WorkspaceShell, XrmDataverseClient, Create*Wizard services, etc.) fail identically WITHOUT my files present (verified by moving files aside) — unrelated to this task.

## Coverage (EmailComposer/ subtree)

`EmailComposer/**` = **80.39% lines** (≥ 80% target met), incl. the 0%-covered `index.ts` barrel that the repo's standard `jest.config.js` excludes via `!src/**/index.ts` (excluding it lifts the number further). Highlights:
- `EmailComposer.reducer.ts` (highest-value pure engine) — **96.25% lines**
- `communicationApi.ts` — **93.75% lines**
- subcomponents 86.63%, wrappers 80.95%, `EmailComposer.tsx` 75%.

Honest gaps (NOT padded): `EmailComposer.tsx` uncovered lines are the mode-transition `useEffect` re-derive path (193-195), the send-error `onError` branch (241-245), and JSX render branches for view-mode chrome (339-363) — exercised end-to-end by the W4 Code-Page integration, not unit-targeted here. `BodyEditor.tsx` (Lexical) internals (76.92%) are out of scope for this task.

## Two POML expectations that no longer match the shipped code (directional-mode adaptations)

1. **communicationApi "one-shot deprecation warn" — DOES NOT EXIST.** The R4 W0 owner decision (2026-07-14) retracted the `attachmentDocumentIds` rename premise (the field correctly carries `sprk_document` GUIDs), so task 022 never added a deprecation-warn path. Writing a test for a non-existent warning would be fiction. Instead the suite asserts the field's correct **pass-through semantics** (GUIDs sent unchanged, no translation, no rename) — the behavior the retraction actually mandates.
2. **AttachmentList "150-cap → Add disabled" — no such behavior.** `AttachmentList.tsx` renders a hard "too many" **alert** at the count cap but never disables the "Add files" button. Tests assert the alert/warning surface that actually exists rather than a disabled-Add affordance.

## Canonical validation-code coverage (all 10 exercised once)

`validateState` raises 7 codes (`TO_REQUIRED`, `TO_INVALID_EMAIL`, `TO_TOO_MANY`, `SUBJECT_REQUIRED`, `BODY_REQUIRED`, `ATTACHMENTS_TOO_MANY`, `ATTACHMENT_TOO_LARGE`) — covered in `validation.test.ts`. The remaining 3 (`ATTACHMENT_BLOCKED_TYPE`, `FROM_REQUIRED`, `FROM_NOT_APPROVED`) are BFF-authoritative per `EmailComposer.types.ts` (no client-side signal to raise them locally) and are exercised as canonical codes via `SendCommunicationError.fromResponse` mapping in `communicationApi.test.ts`.

## Step 9.5 gates

- **code-review**: no Critical/High. Boundary-only mocking (authenticatedFetch / onSearch / onSaveDraftRequest); no banned wiring/DI/ctor-null/mirror/coverage-filler patterns; all tests are MAINTAIN-class behavior contracts.
- **adr-check**: compliant — ADR-038 (KEEP-path behavior contracts), ADR-012 (all platform I/O injected via props; no Xrm/ComponentFramework), ADR-028 (no `@spaarke/auth` import).

## Known noise (not a failure)

`imperative-handle`/`wrappers`/smoketest emit React `act(...)` console warnings originating from the production `BodyEditor` Lexical editor's async post-mount `onChange` (fires outside any `fireEvent`, third-party effect). Pre-existing in the smoketest; benign; not a test failure.
