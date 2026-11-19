# Task 197: composer opens in rich text; attach button beside Attachments

Owner requests from the 185 recheck (2026-11-18); scope "every composer" (owner, same day).
Branch `task/ontology-197-composer` (worktree `C:\wt197`), base origin/master `2f224a78f4`.
Implementer: Sonnet, high effort, FULL rigor.

## Host inventory

Changed to rich text. Each converts its plain pre-fill with the existing `plainTextToHtml` and passes `initialBodyFormat="HTML"`:

1. Console `ConversationPane` (`SendEmailDialog`): Quick Start blank, and the "Send as email" seed from the drafted correspondence.
2. Decision wizard message step (`DecisionWizard/steps/EmailMessageFields.tsx`).
3. Document Upload wizard `DocumentEmailStep`.
4. LegalWorkspace `FilePreviewDialog` email.
5. Daily Briefing item email (`DailyBriefingApp.tsx`). The briefing email itself was already HTML.

Already rich text (task 182), left alone: `SendEmailFollowOnStep`, which hosts CreateRecord, WorkAssignment, SummarizeFiles, DocumentEmailWizard and CreateReportCard; Compose; the Outlook add-in `EmailView`; `CommunicationTimeline`. Their `bodyFormat: 'PlainText'` is the wizard's stored value, not what the composer shows. `keepPlainBody` still keeps an unchanged plain body as plain text on send.

Not changed: `CommunicationPage`'s `EmailComposerSlot` passes `initialBody` (a URL pre-fill or `record.sprk_body`) with no format, so it gets the engine default (HTML). See "Findings outside scope".

## Decisions

- **Decision wizard send path.** The commit route sends plain text: `BodyFormat.PlainText` is hard-coded in `DecisionActionExecutors.cs` (~1107) and `BudgetInquiryExecutor.cs` (~154); the inquiry executor also appends plain link lines. So the step opens in rich text and commits plain text, never raw HTML:
  - an untouched body keeps the step's own value (blank lines intact);
  - an edited body is converted back, with a blank line between paragraphs.
- **Known limit (K-class).** Bold, lists and a link's target URL are not kept in that one email. The text itself is preserved, so the escalation trigger (content loss) did not fire.
- **Helper reuse (CLAUDE.md §11).** No new converter. `plainTextToHtml` is now exported from the `EmailComposer` barrel. The "unchanged plain body" test moved out of `SendEmailFollowOnStep` into `composeOnlyText.ts` as `isUnchangedPlainBody`, so the follow-on step and the Decision wizard share it.
  - Existing: the private `keepPlainBody` logic in `SendEmailFollowOnStep`.
  - Extension: moved, not duplicated.
  - Cost of doing nothing: the Decision wizard would either rewrite an untouched body (losing blank lines) or need a second copy.
- **Attach trigger.** The same menu items and handlers as the old paperclip, now a `linkTile` button passed to `AttachmentList` as a new `headerAction` prop. It renders in both modes and is absent when the host can neither add local files nor link documents. The toolbar divider now shows only when record search and template/AI tools are both present.

## PCF versions

Bumped (each bundle contains the composer; `npm run build:prod` succeeded; the paperclip tooltip is gone from the bundle):

- `CommunicationActions` 1.3.22 → 1.3.23 (`SendEmailPage`)
- `TrackingFieldTrio` 1.0.64 → 1.0.65 (`SendEmailDialog`)
- `SemanticSearchControl` 1.1.88 → 1.1.89 (`DocumentEmailWizard`)

Not packed or copied into `Solution/`. The committed `bundle.js` files for CommunicationActions and TrackingFieldTrio stay stale until the deploy step. Other PCFs import the `@spaarke/ui-components` barrel but no composer symbol, and the library is `sideEffects: false`, so their bundles were judged unchanged. That is not verified by a build.

## Verification (implementer)

- `Spaarke.UI.Components` jest, composer plus every host folder: 123 suites, 1251 tests pass.
- Console jest `send-email-rich-text` + `send-email-dialog-handlers`: 6 pass.
- LegalWorkspace `FilePreviewDialog` (3) and `DocumentEmailStep` (1) pass, run under throwaway jest configs. Neither solution has a jest runner wired, so CI will not run them.
- Outlook add-in `EmailView`: 16 pass, run with the UI.Components `node_modules` renamed (project gotcha 2026-10-23).
- Daily Briefing jest: 24 of 26 suites pass; 2 fail to compile on missing LegalWorkspace modules (local `node_modules` problem).
- `tsc` clean for UI.Components and Daily Briefing; Console typecheck gate 0 surface-owned errors; Console `npm run build` exit 0.
- `client-lint check`: ui-components, daily-briefing, spaarkeai and legal-workspace-app pass. Five other surfaces fail on files this task did not touch, probably missing local installs; not confirmed against master.
- Tests that fail on the old code: `attachButton` 8 of 10, Decision wizard 8, Console seed 3 of 3, `FilePreviewDialog` seed, `DocumentEmailStep`. `richTextSeed` passes on the old code by design: it pins the composer's own behaviour with a converted seed.

## Not verified

- No browser or live check of the Console dialog or the PCFs.
- No host-level test for the Daily Briefing item email; it is covered by the shared `SendEmailDialog` contract test and typecheck only.

## Findings outside scope (D-106: not fixed here)

1. `CommunicationPage` `EmailComposerSlot` passes `initialBody` with no format; a plain body opened as HTML would lose its line breaks.
2. `toPlainText` drops a link's target URL and flattens paragraphs to single newlines; fixing the helper would change a pinned test.
3. `DocumentUploadWizard` `npm run typecheck` already fails with about 37 errors (e.g. `Cannot find namespace 'JSX'`).

## Review

An independent adversarial review (Opus, one pass) returned FAIL on 2026-11-18. It found no F1 (content loss on a main path). It confirmed that the host inventory is complete, that `keepPlainBody` behaves exactly as on master, that the attach button logic is right, and that the PCF bumps are consistent.

Findings, all fixed in fix round 1 (Sonnet). Each fix has a test that fails on the pre-fix code.

- **F2: the Decision wizard dropped link addresses and list markers.**
  - The fix adds `richTextToPlainText` beside `toPlainText` in `composeOnlyText.ts`. A link becomes `text (href)`, for http(s) and mailto only. List items keep `• ` or `1. `, and every block boundary gets a blank line.
  - The step now shows an info MessageBar: "This email is sent as plain text, so formatting such as bold is not kept."
  - `isSafeHref` moved from `EmailComposer.tsx` into `composeOnlyText.ts` so both paths share it.
  - It sits beside `toPlainText` rather than replacing it because `toPlainText`'s single-newline output is pinned by its callers' tests.
- **F2: the Daily Briefing item email's record URL was no longer a link.** `buildItemEmailDraft` now also returns `html`, with a real anchor and every field escaped by the existing `esc` helper. The plain `body` is unchanged.
- **F3: host tests that never ran in CI.**
  - `DocumentUploadWizard/jest.config.js` now matches `.test.tsx` and maps `@spaarke/ui-components` to its source; the test renders with `react-dom/server`, so jsdom is not needed.
  - The FilePreviewDialog test moved with `git mv` into SpaarkeAi jest (`components/shell/__tests__/`), because LegalWorkspace has no jest runner.
  - A Daily Briefing item-email test was added.
  - Runner: `.github/workflows/client-tests.yml` job `test`. It runs nightly and on demand, and does not gate PRs.
- **F4: block breaks were lost at paragraph↔list boundaries.** Covered by `richTextToPlainText`.
- **F4: the Attachments header layout changed on CommunicationTimeline** (no `headerAction`). Master's layout is restored when there is no attach button.
- **Exposed defect (on master, more reachable now): switching plain → rich garbled the text.** `BodyEditor` now runs `plainTextToHtml` before switching.

The main session re-checked the fix diff and its direct callers. The fix did not change the approach, so there was no second full review.

**Client-lint.** ai-widgets, compose-components, reporting and semantic-search fail with output identical to origin/master `2f224a78f4`, in files this task did not touch. That is pre-existing, not from this change.

Left as is: `BodyEditor` keeps its own `htmlToPlainText` for the rich → plain toggle. It is a duplicate of the conversion logic, but it predates this task and behaves correctly.

## PR

[spaarke-dev/spaarke#2910](https://github.com/spaarke-dev/spaarke/pull/2910) to master, with auto-merge (squash) under D-115. Deploy needs the owner's go, given on 2026-11-18 ("Deploy"):
- the Console auto-deploys from master;
- the three PCFs are deployed per `/pcf-deploy` (owner, 2026-11-18) from a fresh master worktree with `Import-SolutionScoped.ps1`.
