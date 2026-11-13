# Spaarke External Access Platform R4 — project operating manual

> Read with `current-task.md` (current state). Repo-wide rules are in root `CLAUDE.md`; this file holds only what is specific to this project. Guidance: `.claude/skills/project-setup/references/claudemd-template.md`.

## 1. Scope and status

R4 replaces the external SPA's R3 record-detail modal with a config-driven **record template** for all six record types (Matter, Project, Work Assignment, Invoice, Service Request, Document). It adds a shared `RecordCommandBar` and icon map, and per-record actions (act where you read). It also closes the R3 and ebilling backlog handed to R4, in four staged phases (P1 template/actions, P2 invoices/documents, P3 grids/information/messages, P4 access/sign-in/carry-overs/Teams), plus closing.

**Out of scope:**
- editing core record fields;
- the invoice submission wizard and ebilling's BFF changes (R4 consumes them);
- auth-plane changes beyond #2269's status code and #2192's first-sign-in path;
- per-user cross-device grid settings;
- #2270/#2278 (→ unified-access-control-r2), #1840/#1737 (deferred), #2534 (→ spaarke-auth-system-of-record-r1);
- any external read of `sprk_signal`.

Tasks: 34 (P1 001–018, P2 020–028, P3 030–038, P4 040–058, closing 080/090). Status: see `tasks/TASK-INDEX.md` and `current-task.md`. Spec: `spec.md` (source of truth for scope). Design: `design.md`. Plan: `plan.md`.

## 2. Binding rules for this project

**UI**
- The modal and every child dialog are SprkModal and its presets; Cancel on the left (ADR-050, FR-02/FR-03).
- Actions a caller may not use are **hidden, not disabled**. **No action the UI offers may answer 403** (FR-06, FR-07, NFR-04).
- Per-record rights come from `/me` `writeAccessPerRecord` and each list's `canCreate`, as `DocumentLibrary` already does. No ViewOnly hard-codes (FR-06).
- `RecordCommandBar` lives in the shared lib: no Xrm, no data fetching, no entity-name literals, exported types, tests (ADR-012, FR-04).
- Record actions use only the icon map constant (FR-05).
- Review detail renders **only when `statuscode` = 1**; unknown values render blank; the client never derives a priority (FR-11).
- `sanitizeEmailHtml` is never given `allowRemoteImages` (FR-22).
- Preview bytes come only from the existing content route through the SPA's `bffApiBlob` (`src/client/external-spa/src/auth/bff-client.ts`; spec FR-14 says `authenticatedFetch`) → a blob URL, revoked on unmount. No Graph, no signed URL, no SPE pointer (FR-14, ADR-007, ADR-028 A1).

**BFF**
- New event routes copy `POST /projects/{id}/events` (`ExternalProjectDataEndpoints.cs` ~`:456`): same filters, DTO, service and `errorCode`s (ADR-008, FR-08).
- Every new or changed route answers ProblemDetails with a stable `errorCode`/`reasonCode` (ADR-019).
- Every new readable column goes into `ReadableColumns` with **UAC's review recorded** in the PR (FR-18).
- An indeterminate access result is a retryable 503, never a 403 (ADR-028, FR-28).
- No new DI registration, package or background work (NFR-05, ADR-010, ADR-052).
- Logs and errors carry no tenant, oid, UPN, email or token (NFR-04, FR-30).
- BFF hygiene per `.claude/constraints/bff-extensions.md`: a Placement Justification in the PR; the publish-size delta against a fresh master build; ≤ 60 MB; no new HIGH CVE; tests with every `Api/`/`Services/` change.

**Tests**
- KEEP paths and banned mocks per ADR-038. Every must-fire test named in an FR has its must-not-fire pair (FR-08, FR-11, FR-28; NFR-07).

**Process**
- Deferred work and newly found issues: `/project-defer-issue-tracking` writes `notes/defer-issues.md` AND a GitHub issue — never one without the other.

**ADR tensions approved for this project** (root CLAUDE.md §6.5; `spec.md` §ADR Tensions):
- **ADR-012, Path C.** `RecordCommandBar` is shared from one consumer, via "Core Spaarke UX pattern". Conditions:
  - it composes `HeaderToolbar`;
  - it ships with tests and has no Xrm or entity literals;
  - task 001 names a candidate second consumer;
  - code-review and adr-check approve explicitly.
  If review rejects it, it falls back to SPA-local and the owner is told.
- **MODAL-DECISION-CRITERIA, Path C.** Child dialogs mount inside the parent SprkModal. They are the same chrome family, and a focus/Esc test guards it (FR-03).
- **record-modal-selection, Path C.** SprkModal `lg` is the Xrm-free SPA case. FR-34(c) proposes documenting it.

## 3. Owner directives and standing decisions

Full table: `spec.md` §Owner Clarifications; review record: `design.md` §9.0 and §7.2.
- 2026-11-08 — prototype decisions accepted: title = type + number; hide, don't disable; tabs off by default; "Your access" header line.
- 2026-11-08 — all six types use the template; the R3 stacked modal is retired.
- 2026-11-08 — shared `RecordCommandBar`; the icon map is adopted and proposed as a standard.
- 2026-11-08 — internal people's names may show in Information, with UAC review per column.
- 2026-11-08 — Submit invoice on Matter, Project and WA (opens ebilling's flow; gated like `InvoicesWidget`).
- 2026-11-08 — R3 carry-over split: R4 takes #2269, #2275, #2499, #2578 and #2271's delete-409. #2270/#2278 → uac-r2 (confirmed 2026-11-10; owner chose option (a), a customer-BU column on `sprk_dataverseenvironment`). #1840/#1737 deferred. #2534 stays with auth-system-of-record-r1.
- 2026-11-09 — preview: PDF + PNG/JPEG/GIF/WebP, 25 MB cap.
- 2026-11-09 — workforce contact with no grants: the record tabs are hidden (not shown empty).
- 2026-11-09 — #2269 503: SPA auto-retries 3× (1 s/2 s/4 s), then a message with Retry.
- 2026-11-09 — staged delivery: each phase ends with a dev deploy and E2E.
- 2026-11-10 — **no task execution before the owner reviews the final plan and tasks.**
- 2026-11-12 — **E2E feedback (task 018), applied in one rework task after the E2E:**
  - each create action lives in its section (Upload → Documents, New message → Messages, Submit invoice → Invoices); the header toolbar keeps record-level actions;
  - **no To Do (`sprk_todo`) in the SPA** — external users work only with `sprk_event` of type Task (D-6 #2703 → send the type); the to-do BFF routes stay until a filed retirement;
  - Submit invoice opens as a modal over the record modal (same size and position) with the record fixed and no record step (R4 builds, ebilling reviews);
  - on the Invoices grid, Submit invoice sits in the grid toolbar;
  - invoice file types / supporting files / several invoices → ebilling-r1 (#2770).
- 2026-11-13 — **keep the external to-do BFF routes for now** (rollback option) even though the SPA no longer calls them; uac-r2 asked to delete them now. Retirement after the R4 deploy and retest: D-13 #2800.
- 2026-11-13 — **access labels match Manage Access everywhere** ("View Only" / "Collaborate" / "Full Access"; no "Can contribute"). Matter/WA records need the level from the BFF (UAC review) rather than an inferred label.
- 2026-11-11 — **Esc in a child dialog follows the modal standard (§6.5 Path C):** with a form or confirm child open, Esc closes neither the child nor the record and keeps the draft; Cancel/× close the child. Spec FR-03 amended. No local Esc handlers on presets.

## 4. Coordination

**Shared external-access BFF surface** (`Api/ExternalAccess/**`, `Infrastructure/ExternalAccess/**`, `Infrastructure/DI/ExternalAccessModule.cs`)
- Shared with `unified-access-control-r2` (active) and `ebilling-r1` (active). Run `/conflict-check` before every BFF PR.
- **uac-r2** orchestrates every dev BFF deploy (`docs/procedures/dev-bff-deploy-coordination.md`). It also reviews FR-08, FR-18, FR-24, FR-28 and FR-31, and owns #2270/#2278.

**ebilling-r1**
- R4 consumes the 082c-2 deploy (`sprk_reviewdetail` readable) and the `/invoices/{id}/files` routes, which ship first and back to back with FR-13.
- R4 reviews ebilling's master PR (082c-3 `08702087cb`, 114 `c4b8d43e8f`, task-130 batch).
- **Merge order (agreed 2026-11-11):** ebilling's master PR #2689 merges first; R4 then merges master and resolves the five files task 002 shares with it (`ExternalProjectDataEndpoints.cs`, `ExternalDataService.cs`, `ExternalMessageEndpoints.cs`, `WorkforceRoleCeilingGate.cs`, `ExternalProjectDtos.cs`). When resolving `ExternalDataService.cs`, keep `Truncated = … || pastBound || chainDropped` (uac-r2 #2679 narrowing + ebilling's invoice-ancestor walk). Test helpers that call `ExecuteScopedFetchCoreAsync` must pass `readDocumentChains`, as the production route does; without it every row with a parent counts as undecidable. **Done:** #2689 merged (2fc434c912); R4 merged master in `8ec9cff1aa`. A post-merge R4 look at ebilling's `src/client/external-spa/**` changes waits on the owner's go.
- Task-130 (`work/ebilling-r1` `017a965312`, 2026-11-10): a stale grant answers 403, not 404, so `ProjectPage.tsx`'s 404 branch dies (folded in task 043). Lists may set `truncated` for invoice-linked chains; the SPA already shows `TruncatedListNotice`.

**Other projects**
- **spaarke-ontology-platform-r1:** owns `Services/Signals/AssignTaskRecipientGate.cs`, so changing it needs their agreement (task 045). D-199 decides #2445's data source (task 036).
- **Dataset-grid framework owners:** agree the DataGrid persistence and Money option API (032/033).
- **Open PRs touching nearby files (2026-11-10):** #1494 (`GrantExpiryReminderJob.cs`), #1293 (`SprkModal` tests).

## 5. Environment and live actions

- **Dev:**
  - BFF `spaarke-bff-dev`;
  - SWA `swa-spaarke-external-spa-dev`, deployed via `.github/workflows/deploy-external-spa.yml` (workflow_dispatch, environment dev);
  - Dataverse SPAARKE DEV 1.
- **Never deploy the dev BFF directly.** Request it through uac-r2; the owner gives the go per exact SHA.
- **Owner approval and a stated rollback before every live change** (SPA deploy, Teams package, Dataverse data fix, log-level setting).
- **Live reads are read-only, and the commands are shown to the owner first** (tasks 030, 046).
- The SPA's BFF host is baked in at Vite build time. After any BFF host change, rebuild and redeploy the SPA.
- BFF builds run from the net10 tree. Verify `/healthz` after any deploy.

## 6. Gotchas — do not re-learn

- 2026-11-11 — **record create actions (task 004).** Upload / New message / Add to-do / Add event are offered to the toolbar by the lists themselves through a `CreateActionReporter` (`components/createAction.tsx`): inside the template the list renders no create button and keeps rendering its dialog (so the dialog is a React descendant of the record modal); outside it (ProjectPage) nothing changes. Child dialogs open with `useChildDialog` (focus returns to the opener, else to the record dialog). Tests that open a child must wait ~350 ms after the record opens (tabster's aria-hidden pass, D-8 #2704) and install `tests/jsdomLayout.ts`.
- 2026-11-11 — **`RecordDetailModal.tsx` is deleted (task 005).** Records open in `components/record/RecordTemplateModal.tsx`; a section is a file in `record/sections/` registered in `sections/index.ts`, and a toolbar action with its own dialog is a host in `record/actions/`. Older POMLs, the spec and ebilling's task 134 still cite the deleted file: read those references as the matching section.
- 2026-11-12 — **Submit invoice (task 007).** The submission page's preselect param is `?invoiceRecord={kind}:{id}`, NOT `?record=` (that is the app-wide deep link `RecordDeepLink` reads on every route — it would reopen the record over the page and strip the param). The link helpers live in the router-free `pages/invoiceSubmitLink.ts`; the opener gets the router's navigate only through `components/detail/RouterRecordOpener.tsx` (App.tsx and the shell test both mount it; a source pin guards App). Grid-opened records use `GridWidgetBody`'s own modal, so actions needing `onNavigate` must be passed there too.
- 2026-11-11 — **per-record access lives in `components/record/recordAccess.ts` (task 006).** Every create action in the record view follows its own list's `canCreate` (null/failed/loading = hidden and silent); a project caller without `writeAccessPerRecord` also needs its `/me` level. "Your access", the read-only lines (grant wording only when the level is known View Only) and the create rule used by SmartTodo/EventsCalendar (`levelVerdictOf`, `listCreateRightOf`, `ACCESS_LEVEL_LABELS`) all come from that file — add to it, do not re-derive. Creates go through `createRootTodo`/`createRootEvent` (`web-api-client.ts`). The to-do status toggle is hidden in the record view on every root until the BFF answers the PATCH right (D-10 #2717). ProjectPage uses the same rule since the D-9 follow-up (#2716, closed); only its status toggle stays level-based (D-10).
- 2026-11-10 — **`/me` carries access levels for projects only**, and the to-do/event/message lists don't return `canCreate` for matters and WAs. Task 002 adds `canCreate` to those lists (modelled on the documents list), so FR-06 can hide actions correctly.
- 2026-11-10 — **requesting a column that isn't on the allow-list fails the whole read** with 400 `DV_FETCHXML_COLUMN_NOT_PERMITTED`; the column doesn't just come back blank. Keep `sprk_reviewdetail` out of every read until ebilling's 082c-2 is live (tasks 020/028).
- 2026-11-10 — **binder faults become 403 in two places**: the CIAM branch (`CallerPrincipalResolver.cs` ~`:514-524`) and the workforce default (~`:790-794`). Also, `me-client.ts` already turns every 5xx into the default modules, so 503 retries must key on the three binder codes (task 043).
- 2026-11-10 — **#1886 `hasFile` is mostly shipped** (`DocumentLibrary.tsx` ~`:929-956`, `record/actions/DocumentDownloadAction.tsx`, `record/sections/PreviewSection.tsx` ~`:35`). Task 023 only checks it.
- 2026-11-09 — **`hasFile` is already on the external document DTO** (`ExternalProjectDtos.cs:142`), so #1886 is SPA-only. **Submit invoice already exists** (`InvoicesWidget.tsx:66`, `InvoiceSubmitPage.tsx`); R4 only links to it.
- 2026-11-09 — the allow-lists on master are wider than design §5's snapshot (project `sprk_projecttype_ref`, matter/WA `sprk_mattertype`, `modifiedon`). Cite the current `ExternalAccessModule.cs`, not the design.
- 2026-10-11 (R3) — **the external plane maps exactly four DELETE routes**, pinned by `TheExternalPlane_MapsExactlyFourDeleteRoutes`. Adding one means updating that test and the route ledger.
- 2026-10-11 (R3) — don't run the full BFF unit suite while other agents build on this machine: host-based contract tests time out (false failures). Re-run failures in isolation.
- 2026-10-11 (R3) — a POML can contradict the spec. Check each against §1 "Out of scope" and §3 before implementing; take conflicts to the owner.
- 2026-10-10 (R3) — `WizardModal`/`WizardRegistry` no longer exist; the wizard framework is `Wizard/WizardShell.tsx` + `InAppWizardHost.tsx`.
- Bash `ls`/`git` can hang on this machine; prefer PowerShell, Glob and Read. Write commit messages to a file and pass it with `git commit -F` (a PowerShell here-string piped to `-F -` is read as a pathspec).

## 7. Key documents

- `spec.md` · `design.md` · `plan.md` · `notes/decisions.md` · `notes/defer-issues.md` · `notes/prototype-audit-2026-10-20.md`
- `docs/procedures/dev-bff-deploy-coordination.md` · `.claude/constraints/bff-extensions.md` · `.claude/constraints/auth.md`
- Modal: `docs/standards/MODAL-DESIGN-SYSTEM.md`, `docs/standards/MODAL-DECISION-CRITERIA.md`, `.claude/patterns/ui/record-modal-selection.md`
- Applicable ADRs: ADR-001, ADR-007, ADR-008, ADR-010, ADR-012, ADR-019, ADR-021, ADR-028, ADR-038, ADR-050, ADR-052
- Related projects: `spaarke-SPA-external-access-platform-r3` (predecessor; its `CLAUDE.md` §6 gotchas), `unified-access-control-r2`, `ebilling-r1`, `spaarke-ontology-platform-r1`, `spaarke-auth-system-of-record-r1`
