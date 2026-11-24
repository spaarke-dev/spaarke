# iss048 — invoice submission result screen follows the outcome

Found 2026-11-24 in owner checklist Q (task 053): a firm-portal submission ended Failed, yet the screen showed a success icon and
"Invoice received" above the Failed bar, and the bar said "Submit it again" for a cause (reason code
`record_owner_restricted_content_team_unresolved`) that refuses every resubmission.

## What changed (all in `@spaarke/ui-components`)

| File | Change |
|---|---|
| `Wizard/wizardShellTypes.ts` | `IWizardSuccessConfig.title`: `string` -> `React.ReactNode`. No other WizardShell change; `WizardSuccessScreen` already renders it inside `Text as="h2"`. Every existing caller (string titles) compiles unchanged. |
| `InvoiceSubmission/InvoiceSubmissionOutcome.tsx` | New optional prop `onStatusChange(view)`, called after every status read (same poll; absent = no change). `Failed` text: a `record_owner_*` reason code gives "We could not process this invoice because of how the record you chose is set up. Submitting it again will not help until that is fixed. Contact the team and quote tracking number {tracking}." (no tracking clause without a number); every other Failed keeps today's text. The code is never shown. New exports `isRecordSetupRefusal`, `nonBlank` (was module-private; not added to the package barrel). |
| `InvoiceSubmission/InvoiceSubmissionResultHeading.tsx` (new) | `createStatusStore()` (tiny per-submission store: get / set / subscribe), `InvoiceResultTitle`, `InvoiceResultIcon`, and the pure `invoiceResultKind(view)`. |
| `InvoiceSubmission/InvoiceSubmissionWizard.tsx` | `finishOnce` creates one store per accepted submission, passes `statusStore.set` to the outcome as `onStatusChange`, and puts `<InvoiceResultIcon store>` / `<InvoiceResultTitle store>` in the success config. The fixed icon/title and the `successIcon` style are gone. |

## Mechanism and why

The success config is returned once from `finishOnce`, while the status arrives later from the outcome's poll. A callback on the
outcome (`onStatusChange`) feeds a store created next to the config; the title and icon are small components that subscribe to it.
Why this and not the alternatives: (a) lifting the status into wizard state would rebuild the success config (and the body element)
on every poll and ripple through `useCallback` deps and WizardShell; (b) a second `getStatus` poll for the heading would double the
reads and could disagree with the body. The store keeps ONE poll (a test asserts exactly two `getStatus` reads for
Processing then Accepted), changes nothing for hosts that do not pass the prop, and needs no new dependency (subscription is
`useState` + `useEffect`, so no React 18-only API).

## Heading by status

| Status | Title | Icon |
|---|---|---|
| none yet / Received / Processing | Invoice received | DocumentCheckmarkRegular, brand foreground (as before) |
| Accepted | Invoice accepted | DocumentCheckmarkRegular, brand foreground |
| Accepted and held (`warning` present) | Invoice received | DocumentCheckmarkRegular |
| Rejected | Invoice rejected | DocumentDismissRegular, `colorStatusDangerForeground1` |
| Failed | Invoice not processed | DocumentErrorRegular, `colorStatusWarningForeground1` |

`@fluentui/react-icons` 2.0.320 has no "DocumentWarning" glyph, so the Rejected (error-style) and Failed (warning-style) icons differ
by glyph (dismiss vs error) and by token colour. Tokens only; no hard-coded colours.

## Surfaces

Changed (the title/icon and the Failed text):
- `InvoiceSubmissionWizard` itself, and so every host of it: external SPA `InvoiceSubmitPage` (and the record-template "Submit invoice"
  dialog, `RecordTemplateSubmitInvoice`), and the internal `CreateInvoiceWizard` (both of its `InvoiceSubmissionWizard` mounts, new
  invoice and correction). `CreateInvoiceWizard` has no success config of its own; it wraps `InvoiceSubmissionWizard`, so no separate change.

Changed only by the Failed text (they had no fixed "Invoice received" title; they mount `InvoiceSubmissionOutcome` directly):
- external SPA `InvoiceSubmitPage` resume route ("Invoice submission" heading, unchanged).
- `Spaarke.Communication.Components` `SubmitInvoiceFromEmail`.

Not touched: the Office add-in `InvoiceView` (its own seam, not this component); other `WizardShell` callers (title stays a string).

## Tests

New (React Testing Library in a FluentProvider via `renderWithProviders`):
- `InvoiceSubmission/__tests__/InvoiceSubmissionWizard.resultScreen.test.tsx` (7): Failed + `record_owner_*` (title, setup text, tracking, no "Submit it again", no code, icon kind); Failed other code and Failed no code (old text under "Invoice not processed"); Accepted; Accepted held; Rejected; Processing then Accepted (one poll).
- `InvoiceSubmission/__tests__/InvoiceSubmissionOutcome.failed.test.tsx` (4): the exact setup sentence with and without a tracking number; other Failed keeps today's text; `onStatusChange` called per read with one poll.

Existing assertions updated (only where the new behaviour requires it): three assertions that resolve the submit inside `act`, after which
the status (Accepted) has already landed, read `'Invoice accepted'` instead of `'Invoice received'`:
`InvoiceSubmissionWizard.correction.test.tsx` (double click), `CreateInvoiceWizard.correction.test.tsx` (double click),
`external-spa/tests/RecordTemplateSubmitInvoice.test.tsx` (x while in flight, then accepted). The other `'Invoice received'` assertions
(about 30) still pass: they look up the title before the first poll lands.

### Fail-before / pass-after

| Suite | Before (original source; new tests present) | After |
|---|---|---|
| New tests (2 files, 11) | 10 failed, 1 passed (the "other Failed reason keeps today's text" control) | 11 passed |
| Package: InvoiceSubmission + CreateInvoiceWizard + Wizard (+ WizardFollowOns matched by the `Wizard` path filter), 29 suites | 2 suites failed (the new ones), 10 tests failed, 431 passed | 29 suites passed, 441 tests passed (run twice, same) |
| external-spa `InvoiceSubmitPage`, `RecordTemplateSubmitInvoice`, `ReviewDetail` | 155 passed (original tests, original source) | 155 passed (one assertion updated as above) |
| `npm run build` (package) | n/a | exit 0 |
| external-spa `npm run typecheck` | n/a | clean |

### Environment note

The worktree had no `node_modules`. The package's `file:` dependencies `@spaarke/auth` and `@spaarke/sdap-client` (sibling packages)
have no `dist` until built, so a fresh `npm run build` / four Wizard suites (`InAppWizardHost*`, `AddTodoFollowOnStep`,
`CreateEventAndAddTodoFollowOn.sidePane`) fail with "Cannot find module" until `Spaarke.Auth` and `Spaarke.SdapClient` are installed
and built (`npm install --legacy-peer-deps --no-audit --no-fund && npm run build` in each). I did that; nothing from it is committed
(`node_modules`, `dist` are ignored). Without it, the first baseline showed 391 passed with those 4 suites unable to run.

## Review (Sonnet adversarial-reviewer, 2026-11-24)

No F-class finding. The reviewer ran 243/0 in 15 suites on 1ed3fd8422. Known limits, recorded and not fixed:
- K1: the `record_owner_` prefix also covers two codes the resolver documents as retryable (`record_owner_direct_team_unavailable`, `record_owner_unsettled`). No invoice path emits them as Failed today; if one ever does, the firm is told a resubmission will not help.
- K2: an internal blocked submission (status Rejected, held for Override or Return) now reads "Invoice rejected". The body bar already said Rejected.
- K3: several tests whose status answer is Accepted assert the pre-poll "Invoice received". They pass because the assertion samples before the mocked poll lands.
- K4: no table tests for null, unknown or Received statuses, a failed status read, or a different-case prefix. These were traced by hand and behave correctly.
- K5: the heading keeps its last status when the body shows a load error. Not reachable today.
- K6: a submission superseded by a correction reads "Invoice accepted".
- K7 (pre-existing, EB-D-210): the Rejected bar shows the reason code.
- K8: the Failed sentence starts with a leading space after the bar title.

Merged into work at f4f5b06f52. The portal picks it up at the next external-SPA build from master.
