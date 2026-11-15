# Task 032 notes: widget Decision wizard over the MCP shims

Branch `worktree-agent-a66d643d5e010a8eb`, reset to the project tip `6b618a409b` before starting. Model: Sonnet 5.5 @ xhigh. Rigor FULL.
No `src/server/**` file touched. No independent adversarial pass was run by this agent (the main session runs it); a self-review and the ADR check below were done.

## What shipped

| File | What |
|---|---|
| `src/client/widgets/spaarke-decisions/src/wizard/wizardFetch.ts` | The wizard's `ResponseFetchFn` over `createMcpFetch`: plan, suppression, commit and lookup routes become `spaarke_get_decision_plan`, `spaarke_complete_decision` and `spaarke_search_decision_lookup`. One plan-tool call per open of an item, shared by the loader, the plan read and the suppression read (`readItemRow`); opening the item again calls again, so the plan-read stamp is fresh. |
| `.../wizard/wizardItems.ts` | The item loader: the item's row is the plan tool's FULL row (the paging tool's rows have no sentence or evidence), the core and the "Also resolve" siblings come from the lane snapshot taken at launch. Fails closed (not in the lane, `row` null, `rowError`, another Signal's row, an unparseable row). |
| `.../wizard/lookupTypeAhead.ts` | The inline type-ahead: trailing 300 ms debounce, 2 character minimum, trimmed and capped at 100 characters, a superseded or cancelled search never calls, an answer that arrives after a newer request is dropped; a failure rejects with the tool's message. |
| `.../wizard/WizardDialog.tsx` | Mounts the shared `DecisionWizardHost`, launches it, and owns the display chain (below), the one-at-a-time rule and the return to inline. |
| `.../src/App.tsx` | Wires lane and wizard: a recorded decision bumps `reloadToken`; the lane is told when a decision is open (`canOpenWizard`). |
| `.../src/lane/DecideLane.tsx` | `reloadToken`, `onData`, `canOpenWizard` and the task 031 review items a to d (below). |
| `.../src/lane/billingLinkOut.ts` | `buildConsoleLinkOut` (the Console web resource; the Console has no per-item link). |
| `.../src/mcp/mcpHost.ts`, `mcpHostContext.ts` | `requestDisplayMode(mode)`: the mode the host chose, or null when it rejects or is not connected. |
| `package.json`, `package-lock.json`, `tsconfig.json` | `lexical` and `@lexical/*` (the wizard's email composer imports the shared RichTextEditor), with the matching `tsconfig` paths. |
| `src/client/shared/Spaarke.UI.Components/src/components/DecisionWizard/*` | Three optional seams, all off by default (below). |
| tests | `wizardFlow.test.tsx` (27), `WizardDialog.chain.test.tsx` (20), `wizardBridge.test.ts` (19), `lookupTypeAhead.test.ts` (19), `DecideLane.reload.test.tsx` (15), `mcpHost.test.tsx` (+3), `billingLinkOut.test.ts` (+11), `widgetSurface.test.ts` (file list), `wizardFixtures.ts`; shared lib `DecisionWizard.hostSeams.test.tsx` (9). |

### Shared-library seams (all optional, default off; the Console is unchanged)

1. `collectBecause` on `DecisionWizardHost` / `DecisionWizard` (and `DecisionItemReady`): an optional "Because" box on Confirm; `review.because` is the trimmed text, set only when non-empty. Typed text counts as an unrecorded choice. Without it no box renders and the review has no `because` member.
2. `signalId` on `DecisionLookupRequest`: the lookup is told which Work Item the wizard is deciding (the widget's tool is scoped by it). The Console's search ignores it.
3. `embedded` on `DecisionWizardHost` / `DecisionWizard` / `DecisionItemWizard`: passes `embedded` to `WizardShell`, so the wizard renders in the page with no dialog (the widget's master-detail).

The existing `DecisionWizard` and `Worklist` tests and `src/solutions/LegalWorkspace/src/sections/worklist.registration.ts` are untouched.

## Display chain (O-5): an interpretation, flagged for the owner

The acceptance line reads "fullscreen refused -> master-detail; unavailable -> `openLink` called". Implemented:

1. The widget asks the host for fullscreen as a wizard opens (an inline frame is about 640 px tall; a dialog needs room), unless the host lists its modes and leaves fullscreen out. **Granted: the shared modal shell as a dialog in the frame** (ADR-050; `requestModal` is not supported in Copilot).
2. **Refused or not offered: master-detail**, the lane stays as the master and the wizard is shown embedded beside it (stacked in a narrow frame), in a fixed 640 px section. Also where a dialog that threw while rendering falls back to.
3. **Frame under 480 px wide, or master-detail threw: `openLink` to the Console**, called at once, with a banner and a manual link kept on screen. The Console has no per-item link, so the link is to the Console page (the same text link the server's own fallback uses).

"Master-detail in fullscreen" in the design is read as the layout (list visible beside the wizard), not as a second use of fullscreen. If the owner meant "ask for fullscreen, then lay out master-detail inside it", the change is one line in `WizardDialog` (the order of the two checks).

## Acceptance criteria

| Criterion | Result | Evidence |
|---|---|---|
| Wizard runs through a `ResponseFetchFn` shim over the three tools, reusing `mcpFetch` | Pass | `wizardBridge.test.ts` (routes, one shared plan call per open, refusal reason code kept, commit body and path id); `wizardFetch.ts` builds on `createMcpFetch` |
| One Work Item of each lookup-bearing type completed with its lookup resolved: revise-budget, reassign, add-todo, assign-work, assign-task, an inquiry to an outside firm | Pass | `wizardFlow.test.tsx` "each lookup-bearing action type ..." (6 cases: the lookup goes through the tool, the review carries the id, the row leaves the lane and the count drops by 1) |
| Every parameter kind renders and submits (Text, TextArea, Choice, Money, Date, Boolean, Person, Lookup) | Pass | "every parameter kind renders and submits" (including an unticked Boolean sent as `false`) |
| Type-ahead debounce >= 300 ms, >= 2 characters | Pass | `lookupTypeAhead.test.ts` (nothing at 299 ms, one call at 300 ms; 0, 1 and blank characters never call; trailing debounce); `wizardFlow.test.tsx` "the type-ahead inside the wizard" (one character: no call after 900 ms; typed text: exactly one call) |
| `because` sent on Confirm | Pass | every flow case asserts `review.because`; a blank one is not sent; shared `DecisionWizard.hostSeams.test.tsx` |
| In-frame shared modal shell; fullscreen request; master-detail; `openLink` last | Pass for the chain as interpreted above | `WizardDialog.chain.test.tsx` (20 chain cases incl. render failures) and `wizardFlow.test.tsx` "the display chain (O-5)" (real wizard) |
| A refusal is shown and the item stays open | Pass | "what the server says is shown" (`not_authorized` 403, a stale plan 422, `decided_in_invoice_review` -> "Decide this in Invoice Review", a host that does not answer -> "may not have been recorded"); the lane and count are unchanged after each |
| After Confirm the row leaves the lane and the count drops by one | Pass | flow cases above; `DecideLane.reload.test.tsx` (a) |
| `spaarke_complete_decision` only on explicit Confirm | Pass | "nothing is written before Confirm": open, step change, back and close call only the page, plan (once) and lookup tools; the commit is called exactly once per Confirm in every flow case; `wizardBridge.test.ts` reads call it zero times |
| No Xrm, `openLookup`, `openRecord` | Pass for Xrm and `openLookup`; `openRecord` is now the widget's own (fix round) | grep over `src/wizard` finds no Xrm; the launch passes no `openLookup`; `openRecord` opens a Console record link through the host's `openLink` (`WizardDialog.chain.test.tsx`, "opening a record from the wizard") |
| Shared-lib changes only as optional props or seams; existing DecisionWizard and Worklist tests and `worklist.registration.ts` unchanged | Pass | three seams above; `git diff` shows no existing test or the registration file changed; shared jest 25 suites, 482 tests pass |
| Fluent v9 semantic tokens only; keyboard operable | Pass | `widgetSurface.test.ts` (no hex / rgb / hsl, no storage, no token words, no `matchMedia`) over the new files; the wizard's own controls are the shared ones; the link banner and Close are Fluent buttons and a link |
| Bundle < 3 MB (aim < 1 MB) | Pass (hard gate); aim not met | 1,607,840 bytes (1.53 MB) |
| ui-tests against a local MCP Apps basic-host | **Not run** | No basic-host in this environment. Every behaviour above is covered by jest through the real wizard; nothing was faked. Needs a run in 042 / the live check |

### Task 031 review items

- (a) a lane reload after Confirm: `reloadToken` reads the lane again in place (no loading state; rows, filter, notice kept; a review under way is pruned to what is still in the lane; absent and 0 are the same token). A failed re-read shows the lane error with Try again and tells the wizard there is no lane.
- (b) a billing link still opening when the service changes no longer puts its failure notice on the new lane (a read epoch).
- (c) the same notice twice is a new alert (keyed), so a repeat is announced again.
- (d) a new service resets the card filter to All.
- (e) a host rejection (`mcpFetch` rethrows) is handled: commit -> "may not have been recorded" with the same review re-sendable; plan, suppression and item reads -> "The decision could not be read. Try again."; a lookup -> no matches and the wizard stays usable.

## Decisions and deviations

- **The paging tool's rows are slim**, so the item's own row comes from the plan tool, and the lane snapshot supplies the core and siblings. A silent lane reload after a Confirm therefore cannot take an item still to be shown out from under the wizard.
- **No empty-query browse in the type-ahead.** The Console's empty-query "show the candidates" is not offered: an empty or one-letter query would ask the server for everything (60 searches a minute are shared with the commit). The field's focus-time search resolves to no matches, so the user types two letters.
- **One decision at a time.** While a decision is open the lane refuses a row click or a review step, says so, and leaves the review where it was (found in self-review: the lane had moved its "n of N" while the wizard refused). The wizard keeps its own copy of the rule.
- **lexical** (about 190 KB minified) comes in with the wizard's email composer (`send-budget-inquiry`), which the POML's inquiry case needs.

## Evidence

- Widget `npx jest`: 14 suites, 305 pass. `npx tsc --noEmit`: clean. `npm run build` (tsc, vite, size gate, built-file storage scan, `widgetSurface`): 1,607,840 bytes (1.53 MB); 0.87 MB before this task (031), 0.73 MB at 030.
- Shared lib `npx jest src/components/DecisionWizard src/components/Worklist` (the shared lib's own jest, run from its folder): 25 suites, 482 tests pass (473 existing + 9 new); no failure to attribute. Shared `tsc --noEmit`: 10 errors, all unresolved `@spaarke/auth` / `@spaarke/sdap-client` imports in files this task did not touch (no sibling links in a fresh worktree); none in `DecisionWizard/`.
- Fail-before proofs (old file copied back, test run, mine restored): `DecideLane.tsx` -> `DecideLane.reload.test.tsx` 9 of 12 failed (the other 3 are controls); `mcpHost.ts` -> 3 of 14 failed; the seven shared files -> `DecisionWizard.hostSeams.test.tsx` 7 of 9 failed (the other 2 are the default-off controls); `DecideLane` + `WizardDialog` + `App` before the one-at-a-time fix -> 7 failed. The new wizard modules have no earlier version to restore.
- Bundle composition (minified, a temporary split config, not kept): MCP SDK 524 KB, Fluent 404 KB, lexical 189 KB, React 142 KB, other packages 112 KB, EmailComposer 56 KB, DecisionWizard 54 KB, widget source 24 KB, Fluent icons 20 KB, Worklist 19 KB, Wizard 14 KB, SprkModal 9 KB, RichTextEditor 9 KB, LookupField 7 KB. The wizard added about 0.73 MB: lexical + EmailComposer + RichTextEditor are about 254 KB of it.
- BFF: no `src/server/**` change, but the widget is embedded in the BFF assembly, so the BFF publish grows by about the widget's growth (+0.73 MB; the 030 notes measured the embed). Not re-measured here (no BFF folder touched); 041/the main session's publish-size check will see it.

## ADR check

- ADR-012: shared components imported by deep path (no barrel); the shared changes are optional props.
- ADR-021: Fluent v9 semantic tokens only (guarded by `widgetSurface`); theme from the host context.
- ADR-050: the wizard in the frame is the shared modal shell (`WizardShell` -> `SprkModal`), not a host modal.
- ADR-038: widget tests are co-located in `src/__tests__` like 030 and 031 and mock only the host boundary (`callServerTool`, `requestDisplayMode`, `openLink`); the one `jest.mock` of the shared host (`WizardDialog.chain.test.tsx`) is the stand-in for the chain tests, and the flow tests use the real wizard.
- Component justification (section 11): new files are named by the POML (wizard shim, loader, type-ahead, dialog). New shared surface is three optional props. Cost of doing nothing: no `because` in the Record, no scoped lookup, no embedded wizard in a 640 px frame.

## Self-review

- Fixed in scope: the one-at-a-time defect above; `undefined` to `0` of `reloadToken` re-read the lane with a loading state (now the same token); a launch the shared host called busy left the place and the fullscreen it had asked for held (now released).
- Known limits (K): a failed lookup looks like "no matches" in the shared field (no error state of its own); the type-ahead waits about 600 ms in all (the field debounces 300 ms and the widget 300 ms); (closed in the fix round: focus now returns to the row after the wizard closes); the Console link has no per-item deep link (no launch parameter exists); a failed re-read after a recorded decision shows the lane error rather than a stale lane (by design); the lane's Previous / Next review bar cannot step while a decision is open (the shared wizard walks its own browse list and advances itself after a record).
- Defects found, not fixed (outside this task): none new beyond the two already carried (DailyBriefing's jest `testMatch` under a `.claude/worktrees` root; the shared `tsc` missing sibling links in a fresh worktree).

## Hand-off

- 042 live acceptance: FR-13 (a licensed dev-tenant user resolves one Work Item of each lookup-bearing type) and FR-15's "appears on the written Decision Record" are live checks; the ui-tests in the POML need a local MCP Apps basic-host (not available here).
- The BFF `DecisionReview.Because` already exists and is stored in the fact snapshot.
- Owner questions: (1) is the display-chain reading above right? (2) the widget is 1.53 MB (aim 1 MB): accept for v1, or lazy-load / drop the email composer's lexical editor for the inquiry action (about 254 KB)? (3) add a launch parameter to the Console web resource so the last fallback can open the Work Item itself?

## Fix round (adversarial review, Fable): 2026-11-14

Model: Sonnet 5.5 (implementer). Based on the project tip `5e9593ff7d`; local commits only, nothing pushed. The deviations and owner questions are in `decisions.md` ("Task 032 fix round"). Items 1 to 12 of the brief:

| # | Item | Result |
|---|---|---|
| 1 | F2 double click on Next recorded an unseen decision (NFR-06) | Fixed in the shared step behind the opt-in `confirmGuardMs` (500 ms, `CONFIRM_GUARD_MS`); on in the widget AND the Console (`ThreePaneShell`). Spy tests: two clicks in a row record 0, a held Enter records 0, the first click after the guard records 1; fail before |
| 2 | F1 superseded lookup wiped newer results | Fixed in `LookupField` (`searchSeq`) and in the widget type-ahead (a superseded search resolves with the newer answer); `lookupTypeAhead.test.ts:169` rewritten to go through `LookupField` |
| 3 | F1 failed or one-letter lookup said "No results found" | Optional seams `searchErrorText` / `minSearchLength` + `shortQueryHint`; the widget supplies "Search could not be completed. Try again." and "Type at least 2 letters"; `wizardFlow.test.tsx` updated |
| 4 | F1 stacked master-detail put the wizard below the lane with no focus | Decision first when stacked (< 790 px), focused, `scrollIntoView`; focus returns to the originating row on close (dialog too) |
| 5 | F1 ITEM_GONE asked for an impossible action and reloaded nothing | The lane is read again in place on every close and when the item is not in the lane; the text asks only to close the decision |
| 6 | F1 "Open in Spaarke" for related records | Evidence lines whose ref is `{table}:{guid}` open the Console record form through `openLink`; no type is invented for the matter (deviation recorded for sign-off) |
| 7 | F4 the wizard never opened under StrictMode | Fixed in `DecisionWizardHost` (microtask-deferred settle); shared and widget StrictMode tests |
| 8 | Escape closes without writing | Optional `closeOnEscape`; on in the widget. Spy tests: 0 commits, the lane is read again, focus returns to the row, the discard check still guards typed choices |
| 9 | "Showing N of M" for the bounded plan row | `evidenceTotal` / `evidenceTruncated` read by the loader; shared optional `evidenceTruncation`; tests |
| 10 | K3 invented `plan_stale` | The two real codes in `wizardFlow.test.tsx` and `wizardBridge.test.ts` |
| 11 | K1 closing always asked for inline | The mode in effect before the wizard asked for fullscreen is restored |
| 12 | jest / build without the shared lib's `node_modules` | Committed in `a5d306f3d1`; verified (below) |

Also corrected from the first round: the acceptance row "No Xrm, `openLookup`, `openRecord`" now reads "no Xrm and no `openLookup`; `openRecord` is the widget's own, through `openLink`", and the known limit "focus does not return to the row after an embedded wizard closes" is closed (item 4).

### Evidence (fix round)

- Widget (`src/client/widgets/spaarke-decisions`): `npx tsc --noEmit` clean; `npx jest` 18 suites, 345 pass, 2 skipped (the two built-file checks that need `dist`); `npm run build` passes: 1,613,642 bytes (1.54 MB; 1,607,840 before the fix round, +5.8 KB), under the 3 MB ceiling, over the 1 MB aim as before; built-file storage scan and `widgetSurface` (40 tests) pass. All three with the shared lib's `node_modules` moved out of the tree (restored after).
- Shared lib (its own jest, run from `src/client/shared/Spaarke.UI.Components` after installing its `node_modules`): `npx jest src/components/DecisionWizard src/components/ConsoleKit src/components/Worklist src/components/LookupField` all pass (see the final counts in the report). No existing shared test was edited. Three suites that import `@spaarke/sdap-client` do not load in a fresh worktree (no sibling links; `WizardFollowOns/AddTodoFollowOnStep`, `Wizard/InAppWizardHost`, `InAppWizardHost.render`); that is the same environment gap as before and unrelated.
- Fail-before (old file copied back, test run, mine restored): widget `WizardDialog.tsx` + `App.tsx` + `wizardItems.ts` at `5e9593ff7d` -> `wizardClose.test.tsx` 12 of 16 failed (the 4 that pass are the two display-mode cases whose answer is inline as before, and the two StrictMode cases, whose fix is in the shared host and has its own fail-before test); `WizardDialog.tsx` + `wizardItems.ts` before the evidence wiring -> `wizardEvidence.test.tsx` and `WizardDialog.chain.test.tsx` 9 of 34 failed (the rest are must-not-fire controls); shared `FoundStep.tsx` before the seams -> `DecisionWizard.evidenceSeams.test.tsx` 7 of 8 failed (the 8th is the default-off control). The confirm guard, lookup seams, StrictMode host and Escape seam were each run fail-before when their tests were written (old file copied back, test run, new file restored).
- Not run: the Console's own tests under `src/solutions/SpaarkeAi` (no `node_modules` in this worktree). The only change there is `ThreePaneShell.tsx` passing `CONFIRM_GUARD_MS` as `confirmGuardMs`; the shared guard is covered by `DecisionWizard.confirmGuard.test.tsx`. The ui-tests against a basic-host were not run (none exists here).
- Test hygiene: `wizardClose.test.tsx` had one flaky case (it read the discard prompt's second button the instant the first appeared); fixed by waiting for the button, and repeated full widget runs passed afterwards.

rework: none task 032
