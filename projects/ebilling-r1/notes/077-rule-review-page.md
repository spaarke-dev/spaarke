# Task 077 - Rule Review code page (FR-28)

Rigor: FULL (the POML says STANDARD; raised because the task changes BFF code and ships a page). Model: Sonnet 5.5 @ high. Base: `dcbf7edc1`.

## 1. What shipped

| Piece | Where |
|---|---|
| Decide contract change (076 re-check K3): Accept and Reject name the row version the admin was shown; a mismatch is refused 409 `ruleset.concurrent_edit`, nothing written | `Api/Finance/RuleSetEndpoints.cs`, `Services/Finance/RuleSetService.cs` (commit `ca40c9ba0`) |
| The page | `src/solutions/RuleReview/` (commit `2e0bf4909`); web resource `sprk_rulereviewpage`, build output `dist/sprk_rulereviewpage.html` (single file, 1.3 MB) |

### The decide change (076's surface)

- `GET /api/finance/rulesets/{id}` now carries `rowVersion` on every proposal (`ProposalView.RowVersion`, the Dataverse `versionnumber` of the working version as an opaque decimal string). Write responses (edit, accept, reject) carry none: a re-read right after a write could race with another admin's write, so the page reloads after every write and decides only from a read.
- `POST .../proposals/{policyId}/accept` and `/reject` take body `{ "rowVersion": "<token>" }`. Missing, blank, non-numeric or negative: 400 `ruleset.invalid` with a `rowVersion` field error, no service call. `RuleSetService.DecideAsync(ruleSetId, policyId, accept, long expectedRowVersion, ct)` compares the token with the stored row after the "is a draft" check and before validation (so the admin is not given validation errors about content they were not shown). A mismatch returns 409 `ruleset.concurrent_edit` ("The rule changed after you opened it, so your decision was not saved. Reload the rule, review the change and decide again."). The existing conditional write is kept, so a change between the compare and the write still loses.
- This is a breaking change to the 076 decide route. Its only caller is this page (no other client existed; the external SPA does not call it), so the token is required rather than optional: an optional token would let a client skip the guard.
- Page behaviour on 409: reload the rule set, work out what differs between the rule the admin was shown and the stored rule (`diffProposals`: version, status, type, each parameter, consequence, headline, message, clause quote and location), show the list, and leave the admin to decide again on the new content.

### The page

Rule set list (name, applies to, status, visible to firms, effective date, rule count; "New rule set"), rule set review (proposals grid; detail pane with the proposed rule beside its quoted clause and location; typed parameter inputs per rule type; consequence choice; accept; reject; "Draft rules" that calls 075's trigger and polls the read until the run has left its marker; not-converted clauses; publish with an effective date; assignment; "visible to firms"). Every call goes through `authenticatedFetch` to the BFF; the only `Xrm.WebApi` use is the read-only firm-name lookup. The Navigator side pane is registered on mount (`ensureNavigatorSidePane()`). Fluent v9 semantic tokens only, a wrapper div carries the surface colours, modals through `FormModal` and `ConfirmModal` (ADR-050).

Error handling: ProblemDetails title/detail plus the stable `errorCode` are shown; 400 `errors[]` are shown per rule and field; 403 turns the page read-only (write controls hidden, a banner; if the list itself is refused, the notice alone); 503 `ebilling.disabled` shows the disabled notice; a 409 other than `concurrent_edit` (rule in force, already drafted, publish in progress) is shown with its message.

## 2. Deviations and decisions

1. **Proposals grid is a plain Fluent v9 `DataGrid`, not the Spaarke DataGrid framework.** The framework is driven by a `sprk_gridconfiguration` record and reads rows through `IDataverseClient`; these rows come only from the BFF (rule bodies and clause text must not be read with the caller's own Dataverse reads; the page must not write them: ADR-002, D-76). The shared pieces that fit are used: `PanelSplitter` and `useTwoPanelLayout` for the two panels, `thinScrollbarStyle`, `LookupField` with `searchOrganizationsAsLookup` for the firm choice.
2. **Typed parameter editor is page-local** (`services/ruleTypes.ts` catalogue plus `components/ParamEditor.tsx`), composed from ordinary Fluent inputs. No shared component is added. The catalogue mirrors the bounds of the 11 `billing-*.schema.json` files only to give quick feedback; the server validates again at edit, accept and publish and its answer is the one shown. A rule type the catalogue does not know is shown read-only as JSON (its consequence, wording and clause can still be edited).
3. **Reject has no reason.** *(Superseded by D-113: see section 8. Reject now takes a required reason.)* 076's reject route takes no reason and there is no column to hold one, so a reason field would be thrown away. Reject is a `ConfirmModal`. Owner question below.
4. **npm dependencies (NFR-12).** No package new to the repository, but `package.json` lists more than the copied skeleton: `dompurify`, `marked`, `diff`, `d3-force`, `react-window`, `scheduler`, `lexical` and `@lexical/*`, `@hello-pangea/dnd`, `mammoth`, `pdfjs-dist`. They are the transitive imports of the `@spaarke/ui-components` barrel (the skeleton obtains them through `@spaarke/communication-components`, which this page does not use; `@spaarke/ui-components` is aliased to source, so Rollup resolves its bare imports from this project's `node_modules`, as `Reporting` does). `@spaarke/sdap-client` is aliased to source in `vite.config.ts` as in `CreateEventWizard`. Removed from the skeleton: `@spaarke/communication-components`. Test-only dev dependencies added: `jest`, `ts-jest`, `@types/jest`.
5. **Web resource name and entry point.** `sprk_rulereviewpage`. How admins open it (sitemap subarea or a command on the rule set form) is a solution change that task 079 owns; no entry exists yet.
6. **Write responses carry no row version** (see section 1).

## 3. Section 11 - component justification (modified decide surface and new page files)

| Surface | Existing overlap | Why not extend | Cost of doing nothing |
|---|---|---|---|
| `rowVersion` on the decide body and on the read | The existing conditional write in `DecideAsync` (it compares the row version it just read, not the one the admin saw) | The existing guard only protects the milliseconds between the service's own read and write; it cannot know what the admin was shown | An admin accepts a rule another admin changed minutes ago: content nobody reviewed goes into a published rule set (076 re-check K3) |
| `RuleReview` page | `CommunicationReconciliation` skeleton (copied); no other page reviews rule sets | A new page: a different domain, routes and permission | FR-28 has no UI: rules can only be reviewed by calling the API |
| Page-local parameter editor, grid | See deviations 1 and 2 | Shared components are for Dataverse-sourced grids and do not know the 11 closed schemas | Free-text JSON editing of rule bodies, with the server as the only check |

## 4. Verification

- Page: `npm run typecheck` (tsc-surface-gate) 0 surface-owned errors (99 pre-existing shared-library errors ignored by the gate); `npm test` **49 pass / 0 fail** (3 suites: API client and failure mapping 14, decide / edit / draft / publish workflow 23, rule-type catalogue 12); `npm run build` succeeds, single file `dist/sprk_rulereviewpage.html`.
- Fail-before (page): with `decideProposal` sending a constant token and not reloading on 409, `reviewActions.test.ts` has **4 of 21 failing** (measured before the review round's added tests); restored, all pass.
- BFF: `dotnet build src/server/api/Sprk.Bff.Api/` 0 warnings, 0 errors. `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~RuleSet"` **256 pass / 0 fail**. ArchTests (`tests/Spaarke.ArchTests`) **1175 pass / 0 fail**; the route ledger stays at 137 (no new route, existing routes changed).
- Fail-before (decide): with the comparison in `DecideAsync` disabled, **6 of 256 fail** (every case that expects the 409); restored, 256 of 256 pass. New service tests (`RuleSetServiceTests`): the read carries a token per rule and a write response none; admin A loads, admin B edits, admin A decides gives 409 and nothing is decided (accept and reject); an unchanged draft decides with the token shown (accept and reject); any change to the row, even a headline-only edit, is refused; a second decision after another admin decided is refused; a stale token is refused before validation; a token of another row or one that matches nothing is refused. Endpoint tests (`RuleSetEndpointsTests`): the 400 for a missing, blank, non-numeric or negative token (no service call) and the 409 mapping. Existing accept / reject tests now pass a token.
- Publish size: Release, `-r linux-x64 --self-contained false`, `Compress-Archive` Optimal, short-path copies from `git archive`, 192 files each: master `a82578cdc` **39,190,734 B**; base `dcbf7edc1` **39,595,796 B**; head **39,596,259 B**. This task's delta (head - base) **+463 B**; branch vs master +405,525 B (+0.39 MB, the ebilling-r1 work already on the branch, not this task). Far under the +5 MB and 55 MB thresholds. No package or project reference changed.
- Not run: the ui-tests of the POML (browser, dark mode and high-contrast against a deployed page) and component rendering tests. They need the web resource deployed (079) or a browser harness this session does not have. The code uses Fluent semantic tokens only (grep for hex, `rgb(`, hard-coded colours: none), the only inline styles are the dynamic panel widths and the `height: 100%` on `FluentProvider` that the skeleton also has.

## 4.5 Review round (one adversarial pass; no F-class finding left open)

1. A stale Accept/Reject on a rule another admin published meanwhile got 409 `ruleset.proposal_in_force` (the in-force check runs before the token check), which the page showed as an error without reloading, leaving a stale badge. Fixed: `decideProposal` reloads on any 409 and reports the change (test added).
2. One extra poll request after leaving the page mid-draft. Fixed: cancellation is checked before each read (two tests).
3. A text field emptied by hand was invisible to "unsaved changes" (Accept stayed on, Discard off). Fixed: it counts as an unsaved change and Save explains why nothing was sent.
4. Accept/Reject stayed enabled while a reload was in flight. Fixed: the detail pane is busy while `loading`.
5. Nits, not changed: two wordings of the "not visible to firms" sentence (page vs server publish response; neither contains the exact visible sentence, so D-66 holds); the firm compare in the assignment panel is case sensitive (harmless).

The reviewer could not construct a path that saves a stale decision (token read, sent, compared against the stored row, then written with a conditional write on the same token).

## 5. Class-grep (pre-existing and new)

- Hard-coded colours, `rgb(`, `dangerouslySetInnerHTML`, `console.log`, `localStorage`, direct `Xrm.*` outside `services/firmLookup.ts`: none.
- Any other caller of `DecideAsync` or the accept/reject routes that does not send a token: the only callers are the page and the tests (grep over `src/` and `tests/`).

## 6. Owner questions

1. **Reject reason.** *(Answered 2026-10-18, D-113: "Add a reason now"; built in section 8.)* FR-28 and the task text say "reject (reason)". The 076 route takes no reason and there is nowhere to store one. Recommendation: leave reject as a confirmation for now; if a reason is wanted, add an optional `reason` to the reject body and a column on the version in a follow-up that 076's owner schedules (a schema change).
2. **Entry point.** 079 needs to decide how admins open the page (sitemap subarea, or a command on the rule set form).

## 7. Known limits

- The ui-tests and component rendering are unrun (section 4).
- The not-converted list is shown as returned; when 075 stored a non-JSON string the page shows the raw text.
- Polling after "Draft rules" stops after five minutes and says so; the run continues server-side and the page can be reopened.

## 8. D-113 reject reason (owner decision 2026-10-18: "Add a reason now")

Supersedes section 2 item 3 ("Reject has no reason") and owner question 1. Base `eec0f3512`.

Commits:
- `bd0ee4b77`: the column's schema script, the data-model doc, the scripts README entry.
- `340640b4a`: the BFF (reject route and service) and its tests.
- `9f0359eac`: the page and its tests.

### What changed

| Piece | Behaviour |
|---|---|
| Column | `sprk_policyversion.sprk_rejectionreason`, multiline text, max 2,000, not required. `sprk_policyversion` already holds the draft's decision (`sprk_proposalstatus`: Proposed / Accepted / Rejected), so the reason sits beside it |
| Script | `scripts/ebilling/Deploy-EbillingRuleRejectionReasonSchema.ps1` (see "Deviation 1"). Idempotent, check-then-create, `-WhatIf`, solution-scoped to `SpaarkeMaster`, entity-scoped publish only when it wrote. **Not run against dev.** `-WhatIf` was run: it reads metadata, wrote nothing, and reported `WOULD CREATE sprk_rejectionreason (Memo)`, which also confirms the column does not exist on dev |
| Reject route | `POST .../reject` body `{ rowVersion, reason }`. `reason` is **required**: trimmed, 1 to 2,000 characters, else 400 `ruleset.invalid` with a `reason` field error, before the service is called. `RuleSetService.DecideAsync` applies the same rule again (`TryNormalizeRejectionReason`), so no other caller can store a blank or over-long reason; it refuses before any read |
| Reject write | The reason and `sprk_proposalstatus = Rejected` are one conditional write (on the row version the admin was shown). A stale `rowVersion` is still 409 `ruleset.concurrent_edit` and stores nothing |
| Accept | Ignores any `reason` in the body. It clears an earlier reason in the accepting write (`DBNull`), only when the row has one, so an accept of a never-rejected draft writes what it always did |
| Edit | An edit that returns the draft to Proposed (D-109: type, body, template, consequence, clause text, section reference or location) clears the reason in the same write. A headline-only edit keeps the decision and its reason. An edit that creates a new version has no reason (a new row) |
| Read | `ProposalView.rejectionReason`: set only while the state is Rejected (a stale value stored on another state is not returned) |
| Page | Reject opens `RejectModal` (a `FormModal`, see "Deviation 2") with a required Reason field; Reject is disabled until the trimmed reason is 1 to 2,000 characters (or while a decision is in flight). A rejected draft's detail shows "Rejected: reason" and the text (plain text, `pre-wrap`; no HTML). A reload that finds a changed reason lists it in the "what changed" notice |

**Required or optional: required (recommended and built).** "Add a reason" is read as "a rejection carries a reason". An optional reason would leave blank rejections the page cannot tell from old ones, and the reason is the only record of why a rule was thrown away. If the owner wants it optional, the change is: drop the minimum in `TryNormalizeRejectionReason`, store `DBNull` for a blank, and relax `canConfirmReject`.

### Admin-only

The reason is never firm-visible:
- `GetFirmClauseAsync` selects `sprk_policy`, `sprk_clausereference`, `sprk_clauselocation`, `sprk_inforceto` by name and returns `FirmClauseText(Quote, SectionReference)`; neither carries it. The other readers of `sprk_policyversion` in `src/server` select named columns (checked: `OntologyAdminRulesService`, `BillingRuleCatalogue`, `PolicyScopeResolver`, `BillingFinding`).
- Test `TheFirmProjection_NeverCarriesTheRejectionReason`: a published version in force that (wrongly) holds a reason yields only the quote and section; the serialised result holds neither the reason nor the word "reason"; `FirmClauseText` has exactly two instance properties; the firm read's column set is neither "all columns" nor contains `sprk_rejectionreason`. It fires: adding the column to the firm read's column set makes it fail.
- The route is admin-only as before (`BillingAdminAuthorization.DecideRule()`, and the read needs Write on `sprk_ruleset`).

### Section 11 - the new column

- **Existing**: no column of `sprk_policyversion` is usable. Checked in `docs/data-model/sprk_ruleset.md`, the SpaarkeMaster source `Entities/sprk_PolicyVersion/Entity.xml` (its custom columns: `sprk_authoredby`, `sprk_clauselocation`, `sprk_clausereference`, `sprk_confidencethreshold`, `sprk_consequence`, `sprk_decisionplan`, `sprk_inforcefrom`, `sprk_inforceto`, `sprk_messagetemplate`, `sprk_name`, `sprk_policy`, `sprk_proposalstatus`, `sprk_proposedaction`, `sprk_rulebody`, `sprk_ruletype`, `sprk_shortheadline`, `sprk_versionnumber`) and live dev (the `-WhatIf` run found no `sprk_rejectionreason`).
- **Extension**: nothing can be extended. `sprk_proposedaction` is a required 400-character text that means what the ontology rules propose a human do, not a rationale. `sprk_messagetemplate`, `sprk_shortheadline` and `sprk_clausereference` are firm-facing text (D-66), so a reason stored there would leak. `sprk_decisionplan` is the ontology's decision plan JSON. `sprk_name` is the version's label.
- **Cost of doing nothing**: D-113 cannot be met: a rejection reason has nowhere to be stored, so the page cannot show it.

### Deploy order and the other tasks

- **Task 079 runs the script on dev; task 053 syncs SpaarkeMaster** (export after the run; the script does not edit the solution source, and this task did not).
- **Run the script before the BFF build is deployed.** `RuleSetService` selects `sprk_rejectionreason` on every version read (the rule set read, edit, accept, reject, publish), so a BFF without the column faults on them. The same applies to anything that runs the rule set service against a live environment (for example `tests/integration/seam/Finance/BillingRuleDraftSeamTests.cs`) until the column exists.
- No new route, DI registration or package. The route ledger stays at 137.

### Deviations

1. **Script location.** The brief named `projects/ebilling-r1/scripts/`; every ebilling schema script, and the shared `EbillingSchema.psm1` the script imports, is in `scripts/ebilling/`, which `scripts/README.md` documents. The script is there, following Deploy-EbillingSignalLineSubjectSchema.ps1 (the `sprk_firmreason` memo).
2. **`FormModal`, not the existing `ConfirmModal`.** `ConfirmModal` takes a message and has no field and no way to disable its confirm button, so it cannot hold a required reason. `RejectModal` is a `FormModal` (ADR-050 form family) with the same title, message and Reject label. `ConfirmModal` is no longer used by the page.
3. **`DecideAsync` gains an optional trailing `string? rejectionReason = null` after the `CancellationToken`.** Optional only so the accept call sites stay as they were; a reject without a reason is refused.
4. **The "reject is disabled until a reason is entered" test is a logic test.** The page's suites run in a node environment with no renderer (section 4), so the test covers `canConfirmReject` / `checkRejectionReason`, which the dialog binds to the Reject button (`submitDisabled={!canConfirmReject(text, false)}`), and `decideProposal`, which refuses a reject without a valid reason before any request. The button wiring itself is unrendered (same known limit as section 7).

### Verification

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~RuleSet"`: **272 pass / 0 fail** (256 before; +16 test rows: 14 in `RuleSetServiceTests`, 2 in `RuleSetEndpointsTests`, theory rows counted). ArchTests (`tests/Spaarke.ArchTests`): **1175 pass / 0 fail**.
- Page: `npx jest` **64 pass / 0 fail** (49 before; +15); `npm run typecheck` 0 surface-owned errors (99 pre-existing shared-library errors, as before); `npm run build` succeeds (`dist/sprk_rulereviewpage.html`, 1.31 MB).
- **Fail-before.** The new tests reference members that did not exist, so against the previous service and endpoint files the test project does not compile (66 errors: no `rejectionReason` parameter, no `ProposalView.RejectionReason`, no `RejectionReasonColumn`). For behaviour, each rule was broken in turn in the finished code and the filtered suite run:

  | Mutation | Failed |
  |---|---|
  | reject does not store the reason | 7 (store, replace, 2,000-character, accept-after-reject, both edit rows, headline-edit keeps) |
  | the reason rule always passes | 5 (3 service no-reason rows, the 2,000 / 2,001 boundary, the endpoint 400 test) |
  | accept does not clear the reason | 1 (`Accept_AfterAReject_ClearsTheReason...`) |
  | edit does not clear the reason | 2 (`Edit_ThatReturnsARejectedDraftToProposed_ClearsTheReason`, body and quote) |
  | the firm read selects the reason column | 1 (`TheFirmProjection_NeverCarriesTheRejectionReason`) |
  | the read returns a reason on any state | 1 (`Read_ShowsAReasonOnlyForARejectedDraft...`) |
  | the row version comparison removed | 7 (6 existing 409 tests and `Reject_WithAStaleRowVersion_StillIs409_AndStoresNoReason`) |

  Page: with the previous `reviewActions.ts` and `ruleSetApi.ts` restored, **16 of 64 fail**; restored, 64 of 64 pass.
- New BFF tests: reject stores the trimmed reason in the same conditional write; no reason (null, empty, white space, over 2,000) is 400 on `reason` with nothing read or written; 2,000 characters after trimming is stored and 2,001 is refused; a stale row version is still 409 and stores nothing; a second reject replaces the reason; accept after reject clears it; accept ignores a reason; an edit that returns a draft to Proposed clears it (body, clause); a headline edit keeps it; the read returns it only for Rejected; the firm projection never carries it; endpoint: 400 for missing / empty / blank / over-long reasons without a service call, and the trimmed reason reaches the service. Existing reject calls pass a reason; the domain-isolation reject case still proves 404 rather than the new 400.
- Publish size (Release, `-r linux-x64 --self-contained false`, `Compress-Archive` Optimal, `git archive` copies, 192 files each): base `eec0f3512` **39,596,455 B**, head `340640b4a` **39,596,817 B**: delta **+362 B**. No package or project reference changed.

### Known limits

- Reject stays disabled on a draft that is already Rejected, so a reason cannot be reworded in place: edit the rule (which returns it to Proposed), then reject again.
- The reason is plain text; it is not translated or moderated.
- The dialog's disabled button is unrendered in tests (deviation 4).
