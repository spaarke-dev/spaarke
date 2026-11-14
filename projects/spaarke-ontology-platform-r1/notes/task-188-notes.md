# Task 188 notes (untracked; owner brief 2026-11-13)

Rigor FULL, sonnet @ high, directional steps. Branch task/ontology-188-composer-hosts-templates-ai from origin/master 3ae2a63362, worktree C:/wt188.

## Per host
| Host | Instance | Change |
|---|---|---|
| ComposeWorkspace.tsx (memo email) | NEW `createXrmEmailComposeHandlers({authenticatedFetch, bffBaseUrl})` in useMemo | 4 handlers to the memo SendEmailDialog |
| TrackingFieldTrio/index.ts | existing `getEmailHandlers()` | added list, render, AI (upload was already passed) |
| CommunicationPage EmailComposerSlot.tsx | NEW useMemo | 4 handlers to SendEmailPage |
| CommunicationActionsApp.tsx | NEW useMemo; only the 4 feature handlers taken | PCF keeps its inline lookup handlers (factory header says so) |

Shared change: `ISendEmailPageProps` did not declare the four props (SendEmailPage spreads composerProps to EmailComposer, so they forward at runtime). Added them via `Pick<IEmailComposerProps, ...>` (one definition). Both SendEmailPage hosts need it.

## Tests / runners
- Compose.Components (jest): new ComposeWorkspace.memoEmailHandlers.test.tsx; 10 existing ComposeWorkspace suites' `@spaarke/ui-components` mocks gained `createXrmEmailComposeHandlers: () => ({})`.
- CommunicationActions (jest): new composerFeatureHandlers.test.tsx; submitInvoiceButton mock gained the factory.
- UI.Components (jest): wrappers.test.tsx SendEmailPage cases (handlers forward; absent -> hidden).
- NO jest runner: TrackingFieldTrio (package.json has no test script), CommunicationPage (no test script). Covered by tsc + build only; not unit-tested.


## Result
Commit 473bdc695c pushed to origin/task/ontology-188-composer-hosts-templates-ai (no PR). origin/master had not moved (3ae2a63362).
- Fail-before: CA composerFeatureHandlers 4/4 fail on old app; Compose memoEmailHandlers 2/2 fail on old ComposeWorkspace. wrappers.test SendEmailPage cases PASS on the old file too (runtime already forwarded; ts-jest does not type-check) - characterization only. The type gap is caught by CommunicationPage tsc.
- Pass: CommunicationActions 8 suites/42; Compose: 10 edited ComposeWorkspace suites + new (2) pass (full Compose suite NOT run); UI.Components wrappers 17/17.
- tsc: UI.Components clean; CommunicationPage clean; both PCF build:prod Succeeded (CA 1.3.22, TFT 1.0.64); Compose tsc 9 errors, all from unbuilt @spaarke/ai-widgets (TS2307 + implicit-any follow-ons), none in touched lines (environment: ai-widgets dist not built here).
- client-lint ui-components and compose-components: ok.
- Step 9.5: code-review: no F-class. adr-check: ADR-012/021/022/028/038/045 compliant.
- Class grep (composer hosts without feature handlers): DocumentEmailWizard.tsx:815 (SendEmailStep, legacy To/subject/body step, used by DocumentRelationshipViewer App.tsx:830) passes none; DecisionWizard EmailMessageFields.tsx:149 (tasks 182/190 scope); DailyBriefing (D-220 no); Office add-in (task 189).
- Found in passing, K2: createXrmEmailComposeHandlers.ts:365,390 `if (!resp.ok)` is dead after authenticatedFetch (it throws); error still reaches the composer.

rework: none task 188

## Fix round 1
- Solution bundles refreshed from out/ (CommunicationActions 1.3.22, TrackingFieldTrio 1.0.64); styles.css identical.
- Known limit (K): the ComposeWorkspace memo email passes no regarding/associations, so `{!entity.field}` template codes render blank in that host.

rework: fix-round-1 task 188
