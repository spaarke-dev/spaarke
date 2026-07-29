# Task 034 — Reading-pane header + attachments view (FR-11/FR-12)

**Status**: Complete. `tsc --noEmit -p tsconfig.test.json` clean for all new files (zero errors attributable to this task). Jest: 10/10 new tests green.

## What was built

New component folder (NOT the `reading-pane/` path in the original POML — see Deviation 1):

- `src/client/shared/Spaarke.Communication.Components/src/components/EmailReadingHeader/EmailReadingHeader.types.ts` — prop contracts: `IEmailReadingHeaderProps`/`IEmailHeaderDataService` (header, FR-11) and `IEmailReadingAttachmentsProps`/`IEmailAttachmentsDataService`/`IEmailAttachmentsNavigationService` (attachments, FR-12). Narrow structural interfaces (mirrors the codebase's existing `IAttachmentsWebApi`/`IActionsWebApi`/`IPolymorphicWebApi` pattern) so a host can inject either an Xrm-backed or BFF-backed adapter (ADR-012 portability) without this view depending on `@spaarke/ui-components`' full `IDataService`/`INavigationService` shape.
- `.../EmailReadingHeader/EmailReadingHeader.tsx` — fetches the selected `sprk_communication` record's envelope fields via `dataService.retrieveRecord(...)` and renders subject as title + From/To/Cc/Bcc/Sent/Received meta rows. Loading (Skeleton), error (MessageBar), and loaded states.
- `.../EmailReadingHeader/EmailReadingAttachments.tsx` — fetches attachments via the promoted (task 021) `CommunicationAttachmentsService.getFileAttachments()` (inline `cid:` images already excluded INSIDE that service — not re-filtered here), renders the promoted `<AttachmentList/>`, and wires row-click to the shared `<RichFilePreviewDialog/>` (`@spaarke/ui-components`), mirroring the production `CommunicationAttachmentsApp.tsx` PCF wiring exactly (preview-url/open-links via `AttachmentApiService`, `onOpenRecord` via the injected navigation adapter, `onEmailDocument` intentional no-op — same scope decision as the PCF).
- `.../EmailReadingHeader/index.ts` — folder barrel (component + types). **NOT wired into `../index.ts`** — orchestrator adds the Wave-5 barrel line post-wave (see Barrel Export below).
- `.../EmailReadingHeader/__tests__/EmailReadingHeader.test.tsx` — 5 tests: renders from/to/cc/bcc/subject/sent/received from the selected record, re-fetches on `selectedId` change, loading skeleton, error banner (no crash), dark-mode render with no console errors.
- `.../EmailReadingHeader/__tests__/EmailReadingAttachments.test.tsx` — 5 tests: true attachments render + inline `cid:` images excluded (FR-12, exercised via the REAL shared service, not a mock of the filter), click opens `RichFilePreviewDialog` (mocked — pulls iframe/Graph machinery unsuitable for jsdom, same rationale as the PCF's own test mock) + `onOpenRecord` wiring, **negative: zero attachments → empty state, no crash, no phantom `listitem`s**, error banner (no crash), dark-mode render with no console errors.

## The slot wiring (for task 040)

These are NOT drop-in slot renderers themselves — they're prop-driven components. The `EmailWorkspace` composition root (task 040) fills the shell's slots with a closure, e.g.:

```tsx
renderHeader={(id) => <EmailReadingHeader selectedId={id} dataService={dataService} />}
renderAttachments={(id) => (
  <EmailReadingAttachments
    selectedId={id}
    dataService={dataService}
    navigation={navigation}
    apiBaseUrl={apiBaseUrl}
  />
)}
```

`dataService`/`navigation`/`apiBaseUrl` are host-resolved once (task 040's "host-agnostic props" — an `IDataService`/`INavigationService`-shaped adapter + `authenticatedFetch`-based BFF base URL, per task 040's own POML). Both `IDataService` and `INavigationService` from `@spaarke/ui-components` are structurally compatible with the narrower `IEmailHeaderDataService`/`IEmailAttachmentsDataService`/`IEmailAttachmentsNavigationService` this task declares — no adapter needed at that call site beyond what task 040 already builds.

## Deviations from the POML (documented per directional-steps latitude)

1. **File paths.** The POML's `<relevant-files>` pointed at `src/client/shared/Spaarke.Communication.Components/src/reading-pane/EmailHeaderAndAttachments.tsx` + modifying `reading-pane/ReadingPaneShell.tsx`. Neither path exists — task 032 actually landed the shell at `src/components/EmailReadingPaneShell/**` (see `notes/task-032-reading-pane-shell.md`), a `render*`-slot composition root that explicitly does **not** get edited by 033/034/035/036. Built in the real location per the task-032 slot contract instead, per the parent orchestrator's explicit guardrails for this run.

2. **`CommunicationHeader` reuse (the constraint says "reuse-only, do not fork").** `CommunicationHeader` (`src/client/code-pages/CommunicationPage/src/components/CommunicationHeader.tsx`) lives in the **private** `communication-page` npm workspace package (`"private": true`, not exported via any `exports` map). `@spaarke/communication-components` is a dependency **of** `communication-page` (see its `package.json`), so importing `CommunicationHeader` from this shared package would invert that dependency direction — exactly what ADR-012's shared-component boundary forbids (a leaf app's local component cannot become a shared-lib import). Literal reuse was therefore not mechanically possible.
   - **Resolution taken (Path C-adjacent, not a formal ADR violation — no ADR mandates this specific reuse, the constraint is task-scoped)**: built `EmailReadingHeader` as a fresh implementation matching `CommunicationHeader`'s exact visual language (token-based header band, title + meta rows), and — since FR-11 asks for the fuller field set — **extended** it to render Cc/Bcc and independent Sent + Received rows (the source component renders only From/To and a single direction-branched date). This is documented in the component's file-header doc comment.
   - **Follow-up flagged, not actioned (out of this task's scope)**: promote `CommunicationHeader` into `@spaarke/communication-components` (mirroring how `AttachmentList` was promoted in task 021) so `communication-page` and the reading pane converge on one component. Left as a backlog item — see `notes/defer-issues.md` candidate.

3. **Output test-file name.** POML listed a single `EmailHeaderAndAttachments.test.tsx`. Split into `EmailReadingHeader.test.tsx` + `EmailReadingAttachments.test.tsx` (one per component file, matching this package's existing one-test-file-per-component convention, e.g. `EmailReadingPaneShell.test.tsx`).

## Quality gates (Step 9.5 — explicitly requested for this STANDARD-rigor task)

- **code-review**: 0 Critical, 0 Warning (blocking). Suggestions: (a) the `CommunicationHeader` divergence above is flagged for reviewer sign-off — not a code defect, a scope/architecture note; (b) `EmailReadingAttachments` carries several responsibilities (fetch, list render, preview-dialog wiring) matching the production `CommunicationAttachmentsApp.tsx` reference 1:1 — accepted as inherent to the reuse mandate, not a smell to refactor away from the reference. No secrets, no XSS surface (no `dangerouslySetInnerHTML`), no blocking calls, proper cleanup via `cancelled` flags in every effect.
- **adr-check**: 0 Violations. ADR-012 (context-agnostic, no PCF/`Xrm` deps in props) — compliant. ADR-021 (Fluent v9 only, semantic tokens, dark-mode verified in both test files) — compliant. ADR-022/NFR-05 (no `as React.ComponentType` cast; `React.FC` + standard hooks only) — compliant (grep-verified, only appears in doc comments).
- Build: `tsc --noEmit -p tsconfig.test.json` — **zero errors in any new file**. The package-wide `npm run build` (real `tsc` emit) currently fails, but **only** on `src/components/EmailBody/EmailBodyView.tsx` (task 033's file, concurrently mid-edit in this same worktree — untracked, timestamps within the last few minutes of this run) missing a `MessageBar`/`MessageBarBody`/`MessageBarTitle` import. That file is out of this task's scope (guardrail: do not touch other components) and unrelated to these changes.

## Barrel export line for the orchestrator

Add to `src/client/shared/Spaarke.Communication.Components/src/components/index.ts`:

```ts
export * from './EmailReadingHeader';
```
