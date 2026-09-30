# Task 024 — FR-11 client: save mode, error handling, deviations, findings

> Companion to `024-dedup-mode-decision.md` (why the override is link/graduate) and `024-publish-size.md`.
> Base `7bc11ddf3`. Every identifier below is named by symbol.

## 1. What shipped

- **Default = version.** When task 013 resolves the open document, Save sends `document.existingDocumentId`
  (canonical bare-lowercase via the local `cleanGuid` — ADR-044, the project's ADR-012 Path A continuation) and
  `document.isNewVersion: true` together (task 023's contract: `existingDocumentId` without it → `OFFICE_018`).
  No `targetEntity` and no `versionComment` are sent on a version save.
- **Explicit override.** An inline `RadioGroup` in the footer area ("A new version of “…”" / "A new document").
  The override sends a plain create — which the server routes through the editable **link/graduate** mode (task
  028; `024-dedup-mode-decision.md`). Nothing on the add-in path can reach the immutable suppress branch.
- **Every identity outcome has a defined, non-dead-end pane state** (§2), and **every version-save refusal has a
  way forward** (§3).

Files: `shared/taskpane/hooks/useSaveFlow.ts`, `components/SaveFlow.tsx`, `components/SaveModeSection.tsx` (new),
`components/views/SaveView.tsx`, `App.tsx`, `services/documentIdentityService.ts` (type alias + shared constant),
`utils/errorMessages.ts` (OFFICE_016–019 + `describeVersionSaveFailure`). Tests: three new jest suites, the
`OfficeEndpointsContractTests.cs` world + a new wire-contract class, and a new data-mutation file.

## 2. Identity outcome → pane

`resolveSaveMode(identity, choice)` (pure, `SaveModeSection.tsx`) is the single source of what Save sends.

| Identity (task 013) | Save default | What the pane shows / offers | Request |
|---|---|---|---|
| not applicable (Outlook; `undefined`) | create | nothing new | unchanged create |
| `checking` (seeded before resolution starts) | **disabled** | "Checking whether this document is already in Spaarke…" | none |
| `resolved` | **new version** | radio: new version of “name” / a new document; hint text; Related-to + Document Details hidden in version mode | version (`existingDocumentId` + `isNewVersion`) — or create on the override |
| `new` | create | no version affordance | create, no `existingDocumentId` |
| `conflict` | **disabled** | warning + "Check again"; **no save-as-new offered, whatever the choice state** | none |
| `indeterminate` (503 / system-failure 403) | **disabled** | warning + "Try again" + explicit checkbox "Save it as a new document anyway" | create only after the explicit choice |
| `error` (network / 5xx / unexpected) | **disabled** | same as indeterminate (different copy) | create only after the explicit choice |
| `denied` | **disabled** | info + explicit checkbox "Save my copy as a new document" | create only after the explicit choice |

Mode changes and identity decisions are announced through `useAnnounce` (NFR-11). A new identity (first
resolution or a retry) resets the explicit choice to that identity's default.

## 3. Server response → pane (version saves)

`describeVersionSaveFailure` (`errorMessages.ts`) — applied only when the refused request was a version save.

| Response | Pane | Retry | "Save as new document" |
|---|---|---|---|
| 404 `OFFICE_016` (target gone) | catalog message / server detail | no | **yes** |
| 409 `OFFICE_017` (target has no file) | catalog / detail | no | **yes** |
| 400 `OFFICE_018` (intent mismatch — the client never sends it) | catalog / detail | no | **yes** |
| 423 `OFFICE_019` (locked) | catalog / detail | **yes** (new idempotency key — §4) | **yes** |
| 403 `OFFICE_009` (SPE refused the caller's write) | catalog / detail | no | **yes** |
| 403 **no `errorCode`** (the version-save filter: not authorized OR unknown id — anti-enumeration, owner-accepted) | "You can’t add a version to this document: you don’t have permission to change it, or it is no longer available in Spaarke." — never claims which | no | **yes** |
| 403 `reasonCode = sdap.access.error.system_failure` | "Spaarke could not check your access…" | **yes** | no |
| anything else | existing generic mapping | per catalog | no |

"Save as new document" switches the mode to create and clears the error; it never saves on its own. A refused
**create** save never offers it.

**Completion.** A version save completes on the EXISTING document id when the polled job reports `Completed`
without an artifact (see finding F3) — the resulting document is, by task 023's contract, the one it named.

## 4. Idempotency key

- **Non-version saves:** the canonical string is byte-for-byte the pre-024 format (pinned by a test). Header only.
- **Version saves:** `buildIdempotencyCanonical` adds `version: { existingDocumentId, fileName, contentSha256,
  failedAttempts? }` — mirroring what task 023's server key discriminates on for a version save
  (`…|FileName|ExistingDocumentId|version-content:{sha256}`). A version save and a non-version save of the same
  file name differ (AC6, tested); two revisions differ; an identical re-send matches.
- **The key is also sent in the BODY (`idempotencyKey`) for version saves.** Reason (verified in code — finding F1):
  `OfficeEndpoints.SaveAsync` reads `X-Idempotency-Key` into a local it never uses; the header reaches only the
  Redis `IdempotencyFilter`. The server's PERSISTENT job dedupe uses `SaveRequest.IdempotencyKey` (the body)
  or its own key. Without the body key, a retry after a 423 recomputes the same server key and is answered with
  the FAILED job forever (finding F2).
- **`failedAttempts`** — incremented on every failed version attempt in the pane session (ProblemDetails refusal,
  thrown error, failed job), never decremented, so a retry never reproduces a failed attempt's key. At worst a
  retry after a lost response writes one more identical SPE version of the same document — never a second row.
- The `contentSha256` is used ONLY inside the idempotency key. The add-in performs **no** content-hash
  comparison and makes no call to `ContentDedupDetector` (AC7 — grep evidence in the task report).

## 5. Deviations from the POML

| # | Deviation | Why |
|---|---|---|
| 1 | **No production server change.** `OfficeDocumentPersistence.cs` is listed as "modify"; it is untouched. | Task 028 already routes `SaveContentType.Document` creates through link/graduate. Directional mode; the obligation left was proof — the data-mutation test, shown to FAIL when `Document` is temporarily routed to suppress (2 of 3 tests failed; reverted, `git diff -- src/server` empty). |
| 2 | **New file `components/SaveModeSection.tsx`** (§11 justification below). | Keeps `SaveFlow.tsx` from absorbing a seventh state machine; makes every identity outcome unit-testable without rendering the whole pane. |
| 3 | **`App.tsx` modified** (not in `<relevant-files>`). | The pane needs the identity STATE (conflict/indeterminate/denied/error were only `console.warn`ed) and a retry. Resolution is refactored into one callback, seeded `'checking'`, and stale attempts are ignored. |
| 4 | **`'error'` identity is no longer silently "new".** Task 013 left a thrown resolution as a warn-only no-op (the pane then saved as create). | Same data-integrity reasoning as `indeterminate`: it says nothing about whether Spaarke tracks the file. The pane stays usable (retry, or an explicit choice) — task 013's "save flow not blocked" holds in the sense that no outcome dead-ends. |
| 5 | **Version mode hides Related-to + Document Details and sends no `targetEntity`.** | The version path never re-associates or renames (task 023 D-4/D-5), so the inputs would be inert — and a stale remembered selection would add an unrelated `EntityAccessFilter` check that could refuse a legitimate version save. They reappear on "A new document". |
| 6 | **Client tests in NEW files** instead of the existing `hooks/__tests__/useSaveFlow.test.ts`. | That suite is red (19/26, ungated). Tests added there would not be protected by the PR gate. The three new suites are green and gate-eligible. |
| 7 | **The version key is sent in the body too** (§4). | Finding F1/F2 — without it the "retryable" 423 is a dead end. |
| 8 | **Contract-test world extended** (023's fixture): `RetrieveMultiple` answers the detector's canonical-hash query; `ApplyGenericUpdate` records an `EntityReference` link. | Needed to observe the link through the real route. Every other query still answers empty; all 260 Office-area tests stay green. |
| 9 | **`errorMessages.ts` edited** (it is in task 025's file list). | Additive only: four catalog entries, `offerSaveAsNew`, `reasonCode`, one helper, one exported constant. |
| 10 | **No UI for a version comment.** | SPE keeps no readable version comment (verified by 023 §6); the POML only asks to "carry a versionComment where the UI collects one" — it collects none. |

### §11 justification — `SaveModeSection.tsx`

1. **Existing** — `grep -rn "version" src/client/office-addins/shared/taskpane/components` found no save-mode or
   version affordance. The shipped two-option dialog lives in `DocumentUploadWizard` / `@spaarke/ui-components`,
   which this package may not import (ADR-012 Path A).
2. **Extension** — could be inlined into `SaveFlow.tsx` (881 lines before this task). Not done: the affordance has
   its own reason to change (the identity contract), and inlining it would make its eight outcomes testable only
   through a full-pane render.
3. **Cost of doing nothing** — without it the pane cannot express FR-11's default/override, and `conflict` /
   `indeterminate` / `denied` / `error` would fall through to a silent create — the duplicate-row failure mode.

## 6. Findings for the main session (NOT fixed here — outside this task's files)

- **F1 — the save handler discards `X-Idempotency-Key`.** `OfficeEndpoints.SaveAsync` assigns it to a local that
  is never read. 023 §10's "a client-supplied key replaces the server's content-aware key" is true of the BODY
  `idempotencyKey` only.
- **F2 — a failed job de-duplicates its retries.** `OfficeDocumentPersistence.CheckForExistingJobAsync` returns any
  job with the key, including `Failed` (status 3), so `SaveAsync` answers a same-key retry `Duplicate=true` with the
  failed job. Affects every server-keyed retry after `OFFICE_012` / `OFFICE_019` / `OFFICE_014`. The pane now avoids
  it for version saves (§4); the server-side fix (ignore Failed/Cancelled jobs, with a query that prefers the
  newest non-failed row) belongs with 023's owner. `GetProcessingJobByIdempotencyKeyAsync` is in shared
  `Spaarke.Dataverse`.
- **F3 — the synchronous save completes the in-memory job with no `Result.Artifact`, on BOTH paths.** The pane's
  first poll sees `Completed` without an id, `pollJobStatus` calls `cleanup()` (which also closes SSE), and the pane
  never reaches its success state for a CREATE save (it stays on the job card). The version path is handled
  client-side (§3 Completion); the create path needs `OfficeService.SaveAsync` to set `Result.Artifact` (it knows
  `documentId`). Pre-existing; not introduced by 024.
- **F4 — the Word CREATE key is content-free.** Same document + same "Related to" → same `X-Idempotency-Key` →
  `IdempotencyFilter` replays the first 202 for 24 h, so a second create save of EDITED content to the same record
  is silently not written. Left unchanged here because the constraint binds the non-version canonical to the
  server's create canonical (which is also content-free); flagged for a decision.
- **F5 — D1 residual for task 025.** The override is a path-keyed create. If the typed name and the container
  derived from the chosen record equal the original's, the upload lands on the ORIGINAL's drive item (a new SPE
  version of it) and the create then collides on `sprk_graphitemid_uk`. The default name is the document's title
  property (`WordAdapter.getSubject`), not the SPE item name, so the exact-name case needs a matching title.
  This is spike-4's D1, owned by task 025; task 024 introduces no new collision logic and relaxes nothing (NFR-07).
- **F6 — jsdom has no `ResizeObserver`.** The pane's error `MessageBar` (auto layout) throws in jsdom — a likely
  contributor to the red `SaveFlow.test.tsx`. Shimmed only in the new suite.
- **F7 — the persistent idempotency lookup is not user-scoped** (pre-existing; now reachable with client-chosen
  body keys for version saves — keys are SHA-256 of document id + content, and job status reads are
  owner-filtered by `JobOwnershipFilter`).

## 7. Gate-list suggestion (`ci-gated-suites.txt` — not edited, per boundary)

Add, all green from creation:
- `shared/taskpane/hooks/__tests__/useSaveFlow.versionSave.test.ts`
- `shared/taskpane/components/__tests__/SaveModeSection.test.tsx`
- `shared/taskpane/components/__tests__/SaveFlow.versionMode.test.tsx`

## 8. Unverified

- All five POML `<ui-tests>` (live Word host, dark-mode toggle, keyboard-only mode change) — no Office host here.
- `origin/master` publish re-measure (see `024-publish-size.md`).
- Whether a live Word session holding the file open produces 423 on the OBO item-keyed write (023's unverified
  writer-identity item applies unchanged).
