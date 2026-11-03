# Task 111 - Invoice decision wizard and Context / Related work / History panes

Code page `src/solutions/InvoiceReview/` (web resource `sprk_invoicereviewpage`). Rigor: FULL (the task writes `.tsx`; the POML says STANDARD, raised). Model: Sonnet 5.5, effort high.

Branch `worktree-agent-a54513667b4988dd6`, started from `3e7ad3295d`. The first commit is the coordinator's 110 fix round 2 (`7aba48dda0`, recorded in `notes/110-invoice-review-page.md`); the 111 commit follows it.

## What was built

110's page is not restructured. `components/InvoiceReviewWithDecisions.tsx` fills its four slots and `main.tsx` renders it.

| Slot | Filled with | Source |
|---|---|---|
| `headerExtras` | `AwaitingApprovers`: "Awaiting: names" (at most four, then "and n more"); a warning line when nobody may approve (FR-14), when eligibility could not be resolved, or when the invoice is held; "Approved / Rejected / Returned to firm by name" once decided | 103 GET approval |
| `invoiceActions` | `DecideInvoiceAction`: "Decide invoice..." (eligible approver, Pending, not held, In Review, Write) or "View decision" (decided); nothing otherwise | `decisionAccess` over the server signals `callerEligible`, `held`, `status` and the review's `flags.canDecide` |
| `banners` | `BlockedBanner`: why the invoice is held (blocking findings with policy, clause and reason), **Override...** (only with `canOverride`), **Return to firm...** (only with `canReturn`), each a `FormModal` with one mandatory reason | 105 GET blocked, POST override, POST return |
| `paneTabs` | **Context**, **Related work**, **History** | 106, 107, 100 + 101 |

`ApprovalProvider` holds the approval state for the three slots that need it. It re-reads only when a signature of what approval depends on in the review changes (status, hold, line-status counts, approved total, invoice adjustments, history length), so a Processing poll does not hit the route every few seconds; concurrent syncs coalesce into one request and a sync during a read marks it dirty for one more.

### The wizard (`components/decision/InvoiceDecisionWizard.tsx`, `steps.tsx`)

`WizardShell`, modal mode (ADR-050 wizard preset, `showSkippedSteps`). Steps are added and removed from the Decision step with the shell's own handle, in a canonical order:

- Approve: **Decision** -> **Invoice adjustment** (skippable) -> **Confirm** -> `POST approve` (no body: the decider is the caller).
- Reject / Return: **Decision** -> **Reason** (mandatory, trimmed, max 2000 as the server) -> *[Notify, only if a slot is supplied]* -> **Confirm** -> `POST reject` / `POST return`.

- Approve is disabled, with the server's open counts ("Not yet: 2 lines to decide, 1 still processing", I-2), when `canApprove` is false; Reject and Return stay available to an eligible approver.
- The adjustment step adds through 102 (`method`, `value`, `reason`, optional `ruleId` from GET adjustment-rules, a client `adjustmentId`), removes through 102 after a `FormModal` confirm, and then **re-reads the review**: the totals shown (Billed, Line adjustments, Invoice adjustments, Approved total) are always the server's. No amount is computed on the page; the only client check is the shape of the number (positive; percent at most 100). A 503 `ebilling.adjustment.incomplete` turns the button into **Repeat** with the same `adjustmentId`. Unsaved adjustment text blocks Next (add it or Clear); Skip leaves it behind.
- Confirm shows the decision, the totals, the reason, and (D-72) "N clean lines have no decision: approving accepts them as billed (accepted in bulk, not counted as reviewer labels)". The finish button reads "Approve {server total} & record", "Reject & record" or "Return to firm & record".
- A refused decision (403, 409, 503) stays on the Confirm step as the shell's error bar with the server's `detail` and the stable code in brackets, and **nothing is re-read or changed** (a 503 that saved part of the change re-reads the page and approval first, and says "It was saved in part: repeat it to finish."; Finish again sends the same request). `alreadyApproved` / `alreadyDecided` is shown as "Already ..." with "no second Decision Record was written".
- After success the page and the approval are re-read; the button becomes "View decision".
- **Read-only reuse**: the same shell reopens on a decided invoice with a `StatusBar` (`state="Decided"` + task 109's `statusOverride` for Approved / Rejected / Returned to firm, the Decision Record number, who, when, the reason), locked choices, the adjustments, the reason, and a Close / Back / Next footer. Nothing in it can write.
- **Notify slot**: `notifyStep` on `InvoiceReviewWithDecisions` (`{ label, renderContent, canAdvance }`). When supplied it appears, skippable, on the reject / return path only. It is not supplied by `main.tsx`: nothing is built in it (131 / 132 fill it).

### The panes

- **Context** (106): five independent sections (matter or project, budget, prior invoices, disallow history, rate history). `available` renders; `notTracked` and `unavailable` say so in their own place with the server's message and code, without hiding the others. Read when the tab opens; Retry on a failed read.
- **Related work** (107): asks for a line first; searches **only on the button** (107's latency, its K1); answers for the selected line only (re-keyed per line); an empty answer shows the reason in words (`not-a-fee-line`, `no-timekeeper`, `no-linked-contact`, `no-core-record`, `no-line-date`, `no-narrative`, `none-in-window` with the window, `none-indexed`); `unavailable` shows the message and code; a failed call shows the detail and code.
- **History** (100 + 101): the Decision Records and the corrections as `RecordRow`s, newest first, append-only (no edit or delete). The badge carries the eBilling words through `statusOverride`. Corrections are read when the tab opens (110's page keeps its own copy for the "Corrected from" notes; its slot context does not expose it); a failed corrections read says so and the decisions still list.

## Server answers (step 0 and the escalation triggers)

None fired. The defaults the task named match the server: eligibility is the caller's own contact (EB-D-171, F-2), D-72 bulk-accepts undecided Clean lines on approve, Override needs the privilege and a reason (D-76) and Return needs a reason but no privilege (D-73, F-7). WizardShell hosts the read-only reuse (`statusBar`, `footer` override) and a skippable step (`isSkippable`, `showSkippedSteps`) with no shared-library change. The blocked view needs no navigation entry or form command of its own: see the deviation below.

## Deviations from the task text and the prototype

| What | Why |
|---|---|
| The blocked view is not launched with a submission id. A held invoice opens by invoice id, and the banner reads `header.held` + `review.submission.submissionId` (110's `submission` is typed `unknown`; only the id is read). | One launch parameter set (`invoiceId`); no navigation entry or form command needed. If the Submission form should open this page directly, that is 112 / 118's (not a SpaarkeMaster change here). |
| Related work runs on a button, not on selecting a line. | 107's search latency (its K1); the task says "on demand". |
| No "budget after this invoice" figure. | It would be computed on the client (I-1); the Budget section shows the server's figures as returned. |
| The prototype's "Accept all clean first" offer on approve is not built. | D-72: approve accepts undecided Clean lines in bulk; the page says so instead. |
| Modal wizard, not an embedded panel. | ADR-050 wizard preset; the review page stays visible behind it; the shell supports the read-only reuse as a modal. |
| History fetches corrections itself. | The page's slot context does not carry them; no change to 110 was needed. |

## Tests

153 jest tests in 6 suites (65 from 110 and its fix round 2, 88 new): `decisionApi.test.ts` (routes, bodies, shape refusal, failure text, incomplete codes), `decisionModel.test.ts` (access from signals, approve blocker, awaiting line, signature, recorded decision, history merge, related-work reasons, adjustment shape), `InvoiceDecisions.test.tsx` (the page with the slots: awaiting line and the zero-approver flag; who sees Decide; approve with and without an adjustment, remove, refusals with codes, 503 repeat, D-72 note; reject and return with mandatory reasons; notify slot absent / present / skippable; read-only reuse; the blocked view with and without `canOverride` / `canReturn`, empty reason blocked, a 403 shown in the form; the three tabs and their per-section / empty / failure states; dark theme; source guards). The colour-literal guard of 110 walks the new folders (asserted, so it is not vacuous).

Fail-before proof: four deliberate mutations (Decide shown to a non-eligible caller; Override shown without `canOverride`; reason step without a reason; failure text without the code) made 14 tests fail across the three new suites; the real source passes all.

## Gates

- `npx jest` in `src/solutions/InvoiceReview`: 6 suites, 153 tests, all passing.
- `npm run typecheck` (tsc-surface-gate): Surface-owned errors 0 (99 pre-existing shared-library errors, deferred to Phase B, unchanged).
- `npm run build` (check-html-css-reset, tsc-surface-gate, vite build): green, `dist/sprk_invoicereviewpage.html` 1.93 MB single file.
- No new npm package, no prototype import, nothing under `src/dataverse/solutions/SpaarkeMaster/` changed, no `.claude/` edit.

**Browser ui-tests are not run here.** They need the deploy in task 112 (web resource upload and the navigation entry). Run the POML's six ui-tests there, including dark mode.

## K findings (known limits, none on a confirmed-defect path)

- K1: Nested modals. "Remove adjustment" is a `FormModal` over the wizard's modal. In jsdom the outer dialog is marked `aria-hidden` by the focus trap a moment after a step change, so the tests query with `hidden: true`; check focus return and Esc/Tab behaviour in a real browser (112).
- K2: The approval state is re-read when the review's approval signature changes, not on every poll. A change made by another approver while the page is open and Processing-free is seen at the next write or reload; the server refuses a stale action (409), which the wizard shows.
- K3: Related work lists document names and filing dates only. There is no route in this page to open a document, so there is no link (a later task may add "open in SharePoint Embedded").
- K4: Context is read once per tab open (the tab remounts when it is reopened); it does not refresh while it stays open.
- K5: A correction has no reviewer name in 101's DTO, so the History row shows none.
- K6: `INCOMPLETE_CODES` lists the four 503 codes the services document; a new incomplete code would show as an ordinary refusal until added there.
- K7: Carried from 110 and still true: the PDF worker is loaded from a CDN with no SRI and its fallback constant is behind the installed pdfjs (check the CSP in 112); one notice at a time; en-US number parsing in the correction form.

## What the deploy (112) needs

- Upload `dist/sprk_invoicereviewpage.html` (no new web resource: same name as 110).
- No new BFF route and no new solution component from 111. The BFF routes it calls exist on master: 102 (adjustments, adjustment-rules), 103 (approval, approve), 104 (reject, return), 105 (blocked, override, return), 106 (context), 107 (related-work), 100 and 101 (review, corrections).
- Run the six ui-tests of this task, including: nested-modal behaviour (K1), dark and high-contrast themes for the wizard, the three tabs and the blocked form, a held internal submission opened by invoice id (banner, Override hidden without the privilege, Return), and a Work Assignment invoice whose Budget section says "not tracked".
- The 107 latency (its K1) and the 110 CSP / PDF worker item (K7).
