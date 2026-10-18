# Task 077 - Rule Review code page (FR-28)

Rigor: FULL (the POML says STANDARD; raised because the task changes BFF code and ships a page). Model: Sonnet 5.5 @ high. Base: `dcbf7edc1`.

## 1. What shipped

| Piece | Where |
|---|---|
| Decide contract change (076 re-check K3): Accept and Reject name the row version the admin was shown; a mismatch is refused 409 `ruleset.concurrent_edit`, nothing written | `Api/Finance/RuleSetEndpoints.cs`, `Services/Finance/RuleSetService.cs` (commit `0bcf0bf3a`) |
| The page | `src/solutions/RuleReview/` (commit `ebf4b5225`); web resource `sprk_rulereviewpage`, build output `dist/sprk_rulereviewpage.html` (single file, 1.3 MB) |

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
3. **Reject has no reason.** 076's reject route takes no reason and there is no column to hold one, so a reason field would be thrown away. Reject is a `ConfirmModal`. Owner question below.
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

1. **Reject reason.** FR-28 and the task text say "reject (reason)". The 076 route takes no reason and there is nowhere to store one. Recommendation: leave reject as a confirmation for now; if a reason is wanted, add an optional `reason` to the reject body and a column on the version in a follow-up that 076's owner schedules (a schema change).
2. **Entry point.** 079 needs to decide how admins open the page (sitemap subarea, or a command on the rule set form).

## 7. Known limits

- The ui-tests and component rendering are unrun (section 4).
- The not-converted list is shown as returned; when 075 stored a non-JSON string the page shows the raw text.
- Polling after "Draft rules" stops after five minutes and says so; the run continues server-side and the page can be reopened.
