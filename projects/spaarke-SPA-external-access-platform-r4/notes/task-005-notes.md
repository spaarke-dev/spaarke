# Task 005 — Section renderers for all six types; retire the R3 stacked modal (FR-01)

Rigor FULL (sub-agent, opus / high). Hot-path check: no watchlist trigger (main session). No delegation (coupled files).

## Files

**New**
- `src/client/external-spa/src/components/record/sections/` — one renderer per section id, `RECORD_SECTIONS` +
  `renderRecordSection` (`index.ts`):
  - `RecordSectionCard.tsx` — the section card (labelled `<section>`, `HeaderToolbar` title, tokens only).
  - `InformationSection.tsx` — `RecordDetailFields` (unchanged R3 field block).
  - `DocumentsSection.tsx` — `DocumentLibrary` on the entity's documents root; `uploadOverride` moved verbatim.
  - `FilesSection.tsx` — Invoice "Files": the same document list on the invoices route (P1 interim, see Deviations).
  - `InvoicesSection.tsx` — the invoices rollup (`INVOICE_COLUMNS`, `useRelatedRows`), moved verbatim.
  - `MessagesSection.tsx` — `RecordMessages` (`embedded`), busy reported for the send.
  - `TasksEventsSection.tsx` — `EventsCalendar` + `SmartTodo` (still `AccessLevel.ViewOnly`; task 006 removes it).
  - `PreviewSection.tsx` — Document "Preview": `documentFileOf` + a short pointer to the toolbar Download (task 019+
    owns the real preview).
- `src/client/external-spa/src/components/record/actions/` — toolbar actions with their own dialogs or notices
  (`RECORD_ACTION_HOSTS`, keyed by toolbar action id):
  - `DocumentDownloadAction.tsx` — Document "Download" (R3 `DocumentDownload`, now started from the toolbar).
  - `ServiceRequestDeleteAction.tsx` — Service request "Delete request" (R3 `ServiceRequestActions`).
- `src/client/external-spa/tests/RecordTemplateSections.test.tsx` — the port of R3's `RecordDetailModal.test.tsx`
  plus the FR-01 per-type section tests and the negative case.

**Modified**
- `components/record/RecordTemplateModal.tsx` — real sections by default, action hosts, `/me` read once per opening,
  string-keyed busy set (sections + hosts), per-section `refreshSignal`, Refresh hold (below).
- `components/record/recordTemplate.types.ts` — `RecordViewContext`, `RecordSectionContext` (moved here),
  `RecordHostAction`, `RecordActionHostContext`.
- `components/detail/RecordOpener.tsx` — opens `RecordTemplateModal`; a type with no template opens nothing.
- `widgets/GridWidgetBody.tsx` — row-open opens `RecordTemplateModal`; needs a template as well as a read description.
- `components/detail/ServiceRequestActions.tsx` — no own button any more: reports the toolbar Delete through
  `onDeleteActionChange` (hidden, not disabled); confirmation, notices and recall box unchanged (R3 text).
- `components/detail/RecordMessages.tsx` — `embedded` prop (no own `<section>`/h3 inside the card); default unchanged.
- Comments only: `record-targets.ts`, `NotificationBell.tsx`, `recordReads.ts`.
- `tests/Spaarke.ArchTests/InvoiceStatusRetirementGuardTests.cs` — the walk-coverage assert named the retired file;
  re-pointed to `record/sections/InvoicesSection.tsx` (where the invoice status rendering now lives). Path literal, not
  an import.
- Tests: see the inventory.

**Deleted**
- `src/client/external-spa/src/components/detail/RecordDetailModal.tsx`
- `src/client/external-spa/tests/RecordDetailModal.test.tsx` (ported to `RecordTemplateSections.test.tsx`)

Escalation 2 (repo-wide grep for `RecordDetailModal` before deleting): no code outside `src/client/external-spa`
imports it. Outside the SPA the name appears only in `InvoiceStatusRetirementGuardTests.cs` (path literal, re-pointed)
and in `ModalUnmountedWhileOpenGuardTests.cs:88,102` (inline sample strings for the detector; left as is). After the
change no file under `src/client/external-spa/src` imports it; the remaining mentions are provenance comments ("R3's
record-detail modal").

## Decisions

- **Action hosts (FR-05 / FR-07).** A toolbar action with its own dialogs or notices (Download, Delete request) is an
  "action host" in `record/actions/`, keyed by toolbar action id (`RECORD_ACTION_HOSTS`). The modal mounts the host for
  every action id the record's config lists, inside the content region, and the host reports `{visible, onClick,
  tooltip?}` through a stable `onActionChange`. The modal still never branches on the entity name; an action no host
  offers is hidden (not disabled), as are the create actions until tasks 004/006.
- **Accessible names kept from R3.** The Download host overrides the toolbar tooltip (= accessible name) to
  `Download {name}`; Delete's default tooltip is "Delete request". Both contain the visible label (WCAG 2.5.3), so the
  R3 tests that find these buttons by name port unchanged. The SR delete confirmation keeps R3's label
  (`recordDetailTitle`: the request's name), not the new modal title.
- **Refresh and the hold (handoff R3 / task 003 review K1): Refresh respects `holdNavigation`.**
  - Refresh is hidden while any section or action host reports an operation in flight (a message send, a recall post),
    and offered again when it settles.
  - A refresh already in flight when an operation starts keeps the shown record (and so the busy section) on screen if
    its answer is not a record (error, denied, not found); the answer replaces the sections once the hold releases.
    A successful answer replaces the record at once (sections stay mounted: same record key).
  - Tests: `RecordTemplateModal.test.tsx` "Refresh and the hold (task 005)" (hidden while busy; failed re-read held
    until release; must-not-fire pair: with nothing busy a failed refresh shows the error at once).
- **Refresh reaches every section.** `refreshSignal = attempt + sectionSignals[section]`. Documents / Files, Invoices
  and Tasks & events remount on it (they have no re-read prop); Messages re-reads through its existing `refreshSignal`.
  `refreshSection('messages')` replaces R3's private thread signal for the recall.
- **`/me` once per opening**, as R3: `useCallerContext(open)` in the modal; `caller` memoised; handed to sections and
  hosts. Paging does not read it again (R3 test kept).
- **Title** is "{type} {number}" (owner 2026-11-08, FR-02; task 003). Ported title assertions changed to the new
  format from the same read values (obsolete-by-spec, not weakened; no escalation needed).

## Deviations

- New folder `record/actions/` (not named in the POML outputs): Download and Delete request are toolbar actions with
  their own notices and dialogs; a host per action id keeps the modal entity-agnostic. 
- `RecordSectionCard` (section chrome via the shared `HeaderToolbar`) — one card for all sections.
- `RecordMessages` gained `embedded` (default false, R3 rendering unchanged) so the card owns the one "Messages"
  region and heading.
- `ServiceRequestActions` no longer draws its button; it reports the toolbar action. Everything after the click is R3's.
- Document **Preview** (P1 interim): a short text pointing to the toolbar Download, or "There is no file to show for
  this document." Task 022 owns the real preview (FR-14). `record-detail-download` marks the text only when a download
  is offered, so R3's "absent when no parent / `hasFile: false`" assertions keep their meaning.
- Invoice **Files** (P1 interim): the same document list on the invoices route as R3, keeping test id
  `record-detail-documents`. Task 021 owns the real Files section (FR-13).
- ArchTest path literal re-pointed (see Files).

## Assertion inventory (11 test files)

Rule followed: port before delete; no assertion weakened. "Port" = same assertion against the template; "keep" = file
unchanged in substance (import / mock target / comment only); "obsolete" = replaced by the spec's new behaviour.

| File | Tests | Outcome |
|---|---|---|
| `RecordDetailModal.test.tsx` (deleted) → `RecordTemplateSections.test.tsx` | 47 R3 cases | **Port, all 47.** Same harness (widget bodies, fake grid, `bffApiCall` seam). Every assertion kept: read-only (one textbox, in Messages; none for a document), external-plane-only reads, the one id-filtered fetch, denied shows none of the record, 1-of-N paging + read order, denied while browsing, Close, the three send-hold tests, every section's routes and empty / truncated / failed states, Messages per type, option-set labels, the five negative cases. **Obsolete-by-spec (FR-02, owner 2026-11-08):** the expected modal *title* strings — R3 titled by name ("Matter Read-Name"), the template titles "{type} {number}" ("Matter MAT-READ-1"). Each is replaced by the new title from the same read; the browsing fixtures gained a number column (`M-201`…) so paging stays visible in the title, and the R3 "previous record's data is gone" check still looks for the old record's *name*. |
| `RecordTemplateSections.test.tsx` (new tests) | +8 | FR-01 left/right sections in order for each of the six types (6); negative: a type with no template opens nothing through `RecordOpener`, no BFF call, no throw, `onTerminal` once (1); its must-not-fire pair (1). The grid negative ("no detail view keeps row-open a no-op") is the ported R3 case. |
| `DetailDocumentsDelete.test.tsx` | unchanged count | **Port.** Three direct renders now `RecordTemplateModal config={RECORD_TEMPLATES.sprk_matter / sprk_servicerequest}`; every assertion unchanged (incl. `/me` once across paging, SR "was deleted" on paging back, `onRecordDeleted` once on 204 and not on a refusal, recall, 409s, Delete found by name "Delete request"). |
| `DetailEventsTodosRoots.test.tsx` | unchanged | **Port.** `renderModal` renders the template; assertions unchanged. |
| `DocumentDownload.test.tsx` | unchanged | **Keep.** Opens via the widget; "Download Contract.pdf" is now the toolbar button (same accessible name); error text and test id unchanged. Comments only. |
| `DocumentDetail.hasFile.test.tsx` | unchanged | **Keep.** Download absent for `hasFile: false` (button and `record-detail-download`), present otherwise. Comments only. |
| `RecordDeepLink.test.tsx` | unchanged | **Keep.** Mock target moved to `record/RecordTemplateModal` (`data-entity` from `props.config.entity`); assertions unchanged; the denied case runs the real template. |
| `NotificationBell.test.tsx` | unchanged | **Keep.** Mock target only. |
| `NotificationBell.noInteraction.test.tsx` | unchanged | **Keep.** Mock target only. |
| `App.notificationBell.test.tsx` | unchanged | **Keep.** Mock target only. |
| `App.deepLinkSignIn.test.tsx` | unchanged | **Keep.** Comment only (runs the real template). |
| `RecordTemplateModal.test.tsx` (task 003) | +3 | **Keep** all task 003 assertions. Harness default `renderSection` is now an explicit stand-in (labelled empty card) because the modal's default is the real sections; `/me` is mocked (`getExternalUserContext`). New: the three Refresh-hold tests. |

Intake success link: opens through `useOpenRecord` (the same `RecordOpener` path the bell and the deep link use);
covered by the opener tests above.

## Test scope

Contract: FR-01 sections per type (6 tests), the negative case + its pair (2), the Refresh hold (2 + 1 pair). Everything
else is the R3 suite ported. No test beyond those.

Fail-before proofs (old file copied back, run, restored):
- `RecordTemplateModal.tsx` at HEAD (task 003): "Refresh is hidden while busy" and "a refresh in flight … keeps that
  section on screen" fail; restored → pass.
- `GridWidgetBody.tsx` + `RecordOpener.tsx` at HEAD (R3 modal): all 6 FR-01 section tests and the negative
  `RecordOpener` test fail; restored → pass.

## Review (Step 9.5: code-review + adr-check, one full pass; inline)

| # | Finding | Class | Outcome |
|---|---|---|---|
| R1 | Refresh could replace the sections while a section or host was busy (task 003 K1 handoff) | F1 (data/result loss on a real path now that Messages and the recall report busy) | Fixed: Refresh hidden while held; a failed in-flight refresh is held until release. Tests + fail-before proof. |
| R2 | A type with a read description but no template would have opened nothing silently through `RecordOpener`, leaving the deep link / bell waiting for `onTerminal` | F2 (caller hangs) | Fixed in this task: no template → `onTerminal()` at once, nothing opened. Test + pair. |
| R3 | Section titles render as `<h2>` (shared `HeaderToolbar`), the same level as the dialog title; R3 used `<h3>` for its sections | K3 (shared component's fixed level; not a WCAG failure) | Note; screen-reader pass in task 018 |
| R4 | Download progress / failure and the SR notices show in the action-host strip at the top of the content, not beside the toolbar button | K3 (layout choice; R3 texts and test ids kept) | Note; check in task 018 |
| R5 | Document Preview and Invoice Files are P1 interims | K2 (owned by tasks 022 / 021) | Note |
| R6 | Test files are outside `npm run typecheck` (`tsconfig.json` includes `src` only); an ad-hoc check of this task's test files found one pre-existing unused parameter (`DetailDocumentsDelete.test.tsx:412`, TS6133), none from this task | K4 (pre-existing tooling gap; not a product defect) | D-5, Issue PENDING |
| R7 | `GridWidgetBody` replaces `config` in place while open; the modal's tab state is not reset on an entity change | K1 (tabs are off for every type) | Note |

adr-check:
- ADR-050: SprkModal `lg`, footer Close only; child dialogs unchanged (task 004). ✓
- ADR-021: new styles use tokens via `makeStyles` only; no hex, no px stroke. ✓
- ADR-012: no shared-lib change; consumes `HeaderToolbar`, `RecordCommandBar`, `RECORD_ACTION_ICONS`. ✓
- FR-05: icons only from `RECORD_ACTION_ICONS`. ✓
- FR-06/07 hide-don't-disable: Download and Delete hidden when not offered; Delete no longer has a disabled state.
  `AccessLevel.ViewOnly` props left for task 006 (brief). ✓
- ADR-038: KEEP path `tests/`; only the network seam, the grid and browser-only helpers are substituted. ✓
- `ModalUnmountedWhileOpenGuardTests` pattern: openers still keep the modal mounted with `open={false}`. ✓
- No new server read, no request-shape change (brief): every call is an R3 call. ✓

Re-checks: none needed after the pass (R1 and R2 were fixed before the pass and are covered by its tests).

## Results

- `npm run typecheck`: 0 errors. `npm run lint`: clean.
- `npm test`: 48 files, 1343 tests (baseline 1332 in 48: −47 R3 modal tests, +55 template-sections, +3 hold).
  Full run on a loaded machine (24 node processes): 1317 passed, 26 failed — all 60 s timeouts in auth/bootstrap
  suites plus one cascade from a timed-out render. The 14 affected files re-run: 14/14, 463/463 passed. Every file
  this task touched passed in the full run.
- ArchTest `dotnet test tests/Spaarke.ArchTests --filter InvoiceStatusRetirement`: 14/14 passed (re-pointed walk path).

## UI tests left for deploy task 018

- All six types from a grid row and from a `?record=` deep link: FR-01 sections per column, Document one column,
  toolbar actions as the FR-01 table minus the create actions (tasks 004/006); no console errors.
- R3 behaviours: send a message and try to page / close / Refresh (held, Refresh hidden); Download a document, open one
  with no file (no Download, "There is no file to show"); as the requester delete a service request with related
  records (409 → "Send recall request" → posted, Messages re-reads, Delete withdrawn).
- Dark theme, high contrast and the Teams host: section cards on tokens, no white panels.
- Screen reader: section headings (R3) and the toolbar's "Download {name}" / "Delete request" names (R4).
