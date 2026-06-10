# Email Communication Solution R3 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-06-05
> **Source**: [`design.md`](design.md)
> **Parent project**: [email-communication-solution-r2](../email-communication-solution-r2/) (server-side Communication Service, completed 2026-04)
> **Driver document**: [Communication Architecture Assessment 2026-06-05](../../docs/assessments/communication-architecture-assessment-2026-06-05.md)

---

## Executive Summary

R3 ends the class of client-side fragmentation bugs in Spaarke's email-send pipeline by replacing 6 ad-hoc implementations with one canonical `<EmailComposer />` engine + 3 semantic wrappers (`<SendEmailStep />`, `<SendEmailDialog />`, `<SendEmailPage />`) + 1 typed `sendCommunication()` BFF wrapper + 1 standalone Code Page that replaces the auto-generated Dataverse standard form for `sprk_communication`. A new Communication ADR (ADR-033) codifies the canonical pattern so future surfaces don't re-fragment. R3 is client-side consolidation; R2's server-side Communication Service is unchanged except for two surgical, non-breaking changes (DTO field rename + Internet-Message-Id post-send capture for reply-thread closure).

---

## Scope

### In Scope

- Build `<EmailComposer />` engine + 3 semantic wrappers in `@spaarke/ui-components`
- Build `EmailComposer` Code Page (standalone, replaces auto-generated `sprk_communication` standard form)
- Wire 3 entry surfaces to the Code Page (ribbon "+ New Email", Form Component Control, embeddable launch)
- Refine `sendCommunication()` typed wrapper (canonical `attachmentDriveItemIds` field; `SendCommunicationError` typed class; documented semantics)
- BFF DTO field rename: add `AttachmentDriveItemIds` as canonical, mark `AttachmentDocumentIds` `[Obsolete]` with non-breaking alias
- BFF post-send `Internet-Message-Id` capture for reply-thread closure
- Dataverse schema check / addition for `sprk_inreplyto` (self-lookup) and `sprk_internetmessageid` (text)
- Migrate 5 create-record wizards (Matter, Project, Event, Todo, WorkAssignment) onto `<SendEmailStep />`
- Migrate `SummarizeFilesDialog` (shared lib + LegalWorkspace fork) onto `<SendEmailStep />`
- Migrate `FilePreviewDialog` (LegalWorkspace) onto `<SendEmailDialog />`
- Migrate `DocumentEmailWizard` to canonical `attachmentDriveItemIds` (fixes latent bug)
- Retire `sprk_communication_send.js` webresource (both copies + Dataverse record)
- Retire LegalWorkspace email-touching duplicate component forks (all 8+)
- Write Communication ADR (`.claude/adr/ADR-033-communication-architecture.md`)
- 13 documentation updates per design §11

### Out of Scope

- Outlook add-in changes (its own UI and document-archive flow)
- `/api/v1/emails/*` Power Apps Email-activity subsystem (dead in production usage per assessment §3.3)
- Non-email `CommunicationType` values (TeamsMessage, SMS, Notification) — Email only
- Server-side Communication Service refactor (R2 already did this)
- Inbox / thread browser / list view UI for `sprk_communication` records (separate project)
- Server-Side Sync, Dataverse `email` activities (Spaarke deliberately avoids these)

### Affected Areas

| Path | Change |
|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/` | **NEW** — canonical engine + 3 wrappers + sub-components |
| `src/client/shared/Spaarke.UI.Components/src/services/communicationApi.ts` | Refine: add `attachmentDriveItemIds`, deprecate `attachmentDocumentIds`, add `SendCommunicationError` class |
| `src/client/shared/Spaarke.UI.Components/src/services/EntityCreationService.ts` | Refactor `sendEmail()` to thin adapter over `sendCommunication()` |
| `src/client/shared/Spaarke.UI.Components/src/components/SendEmailDialog/` | Rewrite as canonical wrapper around `<EmailComposer mount='dialog' />` |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/SendEmailStep.tsx` | Replace bespoke step with `<SendEmailStep />` |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/` | Same |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateEventWizard/` | Same |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateTodoWizard/` | Same |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/` | Same |
| `src/client/shared/Spaarke.UI.Components/src/components/SummarizeFilesWizard/SummarizeFilesDialog.tsx` | Replace inline `fetch` with `<SendEmailStep />` |
| `src/client/shared/Spaarke.UI.Components/src/components/DocumentEmailWizard/DocumentEmailWizard.tsx` | Migrate to `attachmentDriveItemIds` (fix latent bug at line 494) |
| `src/client/code-pages/EmailComposer/` | **NEW** — standalone Code Page (deploys as `sprk_emailcomposer` web resource) |
| `src/solutions/LegalWorkspace/src/components/FilePreview/FilePreviewDialog.tsx` | Replace inline `fetch` with `<SendEmailDialog />` (canonical wrapper) |
| `src/solutions/LegalWorkspace/src/components/SummarizeFiles/` | DELETE fork — re-export from shared lib |
| `src/solutions/LegalWorkspace/src/components/CreateMatter/` | DELETE fork — re-export |
| `src/solutions/LegalWorkspace/src/components/CreateProject/` | DELETE fork — re-export |
| `src/solutions/LegalWorkspace/src/components/CreateEvent/` | DELETE fork — re-export |
| `src/solutions/LegalWorkspace/src/components/CreateTodo/` | DELETE fork — re-export |
| `src/solutions/LegalWorkspace/src/components/CreateWorkAssignment/` | DELETE fork — re-export; fix cross-package source-path import at `WorkAssignmentWizardDialog.tsx:31` |
| `src/server/api/Sprk.Bff.Api/Services/Communication/Models/SendCommunicationRequest.cs` | Add `AttachmentDriveItemIds`; mark `AttachmentDocumentIds` `[Obsolete]` with mapping |
| `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs` | Add post-send `Internet-Message-Id` capture |
| `src/client/webresources/js/sprk_communication_send.js` | **DELETE** (~600 LOC) |
| `infrastructure/dataverse/ribbon/EmailRibbons/WebResources/sprk_communication_send.js` | **DELETE** (duplicate) |
| `.claude/adr/ADR-033-communication-architecture.md` | **NEW** |
| `docs/guides/EMAIL-COMPOSER-COMPONENT-GUIDE.md` | **NEW** |
| `docs/standards/COMMUNICATION-ATTACHMENT-POLICY.md` | **NEW** |
| `docs/data-model/sprk_communication-form.md` | **NEW** |
| `.claude/patterns/api/send-email-integration.md` | UPDATE — reference EmailComposer wrappers |
| `.claude/FAILURE-MODES.md` | UPDATE AP-4 with `attachmentDriveItemIds` rename note |
| `.claude/constraints/bff-extensions.md` | UPDATE — Communication as sensitive surface |
| `docs/architecture/communication-service-architecture.md` | UPDATE — add client-side section |
| `docs/guides/SHARED-UI-COMPONENTS-GUIDE.md` | UPDATE — add EmailComposer section |
| `docs/architecture/email-to-document-architecture.md` | Mark **RETIRED** banner |
| `docs/architecture/email-to-document-automation.md` | Mark **RETIRED** banner |
| `docs/architecture/sdap-overview.md` | Fix `sprk_email*` field references on `sprk_document` |

---

## Requirements

### Functional Requirements

#### Engine + Wrappers

**FR-01: EmailComposer engine**
Build the canonical `<EmailComposer />` engine in `src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/EmailComposer.tsx` supporting 5 modes (`compose`, `view`, `reply`, `forward`, `draft`) × 3 mounts (`inline`, `dialog`, `page`). Internal state machine via `useReducer`. Imperative handle via `useImperativeHandle` exposing `validate()`, `send()`, `saveDraft()`, `getState()`.
**Acceptance**: Engine supports all 15 mode × mount combinations; unit tests cover state machine, mode switching, validation, normalization, attachment cap enforcement; imperative handle methods callable from wizard hosts.

**FR-02: `<SendEmailStep />` wrapper (inline mount)**
Thin wrapper (~30–60 LOC) in `EmailComposer/wrappers/SendEmailStep.tsx` that locks `mount='inline'`. Exposes semantic prop API for wizard step usage: `composerRef`, `initialTo`, `initialSubject`, `initialBody`, `associations`, `attachmentSources`, `wizardContext`, `onStateChange`. Wizard owns the Send action via `composerRef`.
**Acceptance**: Wrapper sets `mount='inline'`; passes props through; wizard's Finish action calls `composerRef.current.send()` successfully; unit test confirms prop pass-through and mount value.

**FR-03: `<SendEmailDialog />` wrapper (dialog mount)**
Thin wrapper in `EmailComposer/wrappers/SendEmailDialog.tsx` that locks `mount='dialog'`. Exposes semantic prop API for popup usage: `open`, `onClose`, `initialTo`, `initialSubject`, `associations`, `attachmentSources`, `onSent`, `onError`. Rewrites the existing single-purpose `SendEmailDialog` component into this canonical wrapper.
**Acceptance**: Wrapper sets `mount='dialog'`; `onSent` and `onClose` both close the dialog; `LegalWorkspace/.../FilePreviewDialog.tsx` consumes this wrapper without error.

**FR-04: `<SendEmailPage />` wrapper (page mount)**
Thin wrapper in `EmailComposer/wrappers/SendEmailPage.tsx` that locks `mount='page'`. Exposes semantic prop API for Code Page usage: `mode` (driven by URL), `communicationId`, `initialTo`, `initialSubject`, `initialBody`, `associations`, `onSent`, `onClose`.
**Acceptance**: Wrapper sets `mount='page'`; supports all 5 modes; Code Page entry point renders correctly with `mode=compose|view|reply|forward|draft`.

**FR-05: `<RecipientField />` sub-component**
Single canonical recipient field for To/Cc/Bcc lines. Accepts string-with-`;,`-separator input (paste-from-Outlook compatibility), resolves via `searchUsersAndContacts()` directory autocomplete, renders Fluent Tags for resolved entries, validates email-format on commit.
**Acceptance**: Replaces 5 different recipient-normalization implementations identified in the assessment; handles `;` and `,` separators; rejects malformed emails on commit; unit tests cover separator parsing, autocomplete resolution, Tag rendering.

**FR-06: `<BodyEditor />` sub-component**
Rich text editor (HTML mode) + plain text editor (PlainText mode) with `bodyFormat` toggle. HTML mode shows a lightweight rich editor; PlainText mode shows a monospace `<Textarea />`. Editor honors `bodyFormat` prop and emits content matching that format.
**Acceptance**: HTML mode produces clean HTML; PlainText mode produces text without HTML tags; toggle switches between modes; content preserved across toggle (best-effort — switching may lose markup).

**FR-07: Attachment system (4 sources)**
`<AttachmentList />` sub-component supporting 4 attachment sources rendered in order from `attachmentSources` prop:
- `local` — `<input type='file' />` picker
- `spe` — SPE document browser (picks from a container's contents)
- `related` — related-record document list (e.g., on a Matter, pick from that matter's `sprk_document` records)
- `wizard` — files uploaded earlier in the parent wizard (via `wizardContext.uploadedFiles`)

Each source has a picker component; selected attachments display in a unified list with name/size/source-badge/remove button.
**Acceptance**: All 4 source pickers functional; combined cap of 150 attachments / 35 MB total enforced client-side with warning at 25 MB; attachment list renders source badge.

**FR-08: Validation contract**
Composer exposes `validate(): IValidationResult` returning typed errors. Error structure: `{ field, code, message }` where `field ∈ {'to', 'subject', 'body', 'attachments', 'from'}` and `code` uses canonical strings: `TO_REQUIRED`, `TO_INVALID_EMAIL`, `TO_TOO_MANY`, `SUBJECT_REQUIRED`, `BODY_REQUIRED`, `ATTACHMENT_TOO_LARGE`, `ATTACHMENTS_TOO_MANY`, `ATTACHMENT_BLOCKED_TYPE`, `FROM_REQUIRED`, `FROM_NOT_APPROVED`.
**Acceptance**: All canonical codes implemented; no string-only errors; unit tests verify each code path.

**FR-09: Error contract (`SendCommunicationError`)**
Composer's `send()` throws `SendCommunicationError` on non-2xx response. Error class exposes `status`, `code`, `detail`, `correlationId` parsed from BFF ProblemDetails response. Replaces 3 existing error contracts (throw `Error`, return `{success, warning}`, push to `warnings[]`).
**Acceptance**: `SendCommunicationError` defined in `communicationApi.ts`; all 4 fields populated; thrown on non-2xx; callers can `instanceof SendCommunicationError` check.

#### Modes

**FR-10: `view` mode**
Read-only display of an existing `sprk_communication` record. Loads via `Xrm.WebApi.retrieveRecord` (Code Page) or programmatic prop (`communicationId`). Renders From / To / Cc / Subject / Body / Attachments / Associations as Fluent labels and tags. No editable controls.
**Acceptance**: Opens a `sprk_communication` record via URL `mode=view&id={guid}`; all fields display; no validation; "Edit" / "Reply" / "Forward" / "Close" action buttons present in `<ComposerActionBar />`.

**FR-11: `reply` mode**
Pre-fills new compose from an existing `sprk_communication` record. `to` = original `from`; `cc` blank by default; `subject` = `Re: ${original.subject}` (prefix not duplicated if already present); `body` wraps the original body with a divider; `attachments` not carried; `associations` carried from original; `sprk_inreplyto` lookup stamped on new record pointing to original.
**Acceptance**: Reply from a `view`-mode record creates a new draft; Send creates a new `sprk_communication` with `sprk_inreplyto` set to the original; original record's `sprk_threadid` (if present) carried to new record.

**FR-12: `forward` mode**
Pre-fills new compose from an existing `sprk_communication` record. `to` / `cc` blank; `subject` = `Fwd: ${original.subject}`; `body` wraps original; attachments carried by default with user able to deselect; `associations` carried; `sprk_inreplyto` stamped.
**Acceptance**: Forward from a `view`-mode record creates a new draft with original attachments pre-checked; deselected attachments removed before send; Send creates new record with `sprk_inreplyto`.

**FR-13: `draft` mode**
Edit / resume a saved draft. Loads from `sprk_communication` where `statuscode = Draft`. Both Send and Save Draft actions functional. Save Draft updates the existing record; Send transitions `statuscode` to `Sent` and triggers Graph SendMail via the server pipeline.
**Acceptance**: Draft loads with previously-entered values; Save Draft persists to the same record; Send transitions statuscode and posts to `/api/communications/send`.

#### Migration

**FR-14: SendEmailDialog migration (FilePreviewDialog)**
`LegalWorkspace/.../FilePreview/FilePreviewDialog.tsx` uses canonical `<SendEmailDialog />` wrapper. Inline `handleSendEmail` and inline `fetch` removed. `ISendEmailDialogProps` updated to expose `open` / `onClose` / `onSent` / `onError` (removes legacy `onSend` callback). Audit for any other `SendEmailDialog` consumers and migrate inline.
**Acceptance**: `FilePreviewDialog.tsx` no longer contains `fetch` calls to `/api/communications/send`; LegalWorkspace solution builds and deploys; FilePreview file-email flow works end-to-end in dev.

**FR-15: SummarizeFilesDialog migration (both copies)**
Both `Spaarke.UI.Components/.../SummarizeFilesWizard/SummarizeFilesDialog.tsx` AND `src/solutions/LegalWorkspace/src/components/SummarizeFiles/SummarizeFilesDialog.tsx` use `<SendEmailStep />`. Inline `fetch` removed from both. LegalWorkspace fork deleted (re-exports from `@spaarke/ui-components`).
**Acceptance**: Neither file contains `fetch` to `/api/communications/send`; LegalWorkspace fork deleted; SummarizeFilesWizard solution and LegalWorkspace solution both build and deploy; Summarize wizard's email step works in dev.

**FR-16: Create-record wizard migration (5 wizards, single PR)**
All 5 create-record wizards (CreateMatter, CreateProject, CreateEvent, CreateTodo, CreateWorkAssignment) migrate to canonical `<SendEmailStep />`. `EntityCreationService.sendEmail()` refactored to thin adapter over `sendCommunication()`. LegalWorkspace duplicate forks of all 5 wizards' email steps and services deleted. Single PR for all 5.
**Acceptance**: All 5 wizards' email steps use `<SendEmailStep />`; `EntityCreationService.sendEmail()` is a thin adapter (≤30 LOC); LegalWorkspace forks deleted; cross-package source-path import at `WorkAssignmentWizardDialog.tsx:31` resolved; all 5 wizard solutions + LegalWorkspace solution build and deploy; manual smoke test on each wizard's send flow passes.

**FR-17: DocumentEmailWizard migration**
`DocumentEmailWizard.tsx` migrates to canonical `attachmentDriveItemIds` field (fixes latent bug at line 494 where `sprk_document` GUIDs were sent instead of driveItem IDs). Uses `wizardContext.uploadedFiles` convention to supply driveItem IDs.
**Acceptance**: `attachmentDocumentIds` removed from this caller; driveItem IDs sourced from `uploadedFiles`; emails sent from DocumentEmailWizard successfully attach files end-to-end in dev (verified via SPE container check).

#### Code Page

**FR-18: EmailComposer Code Page**
Build standalone Code Page at `src/client/code-pages/EmailComposer/` per `code-page-deploy` skill conventions. Single self-contained HTML artifact deploys as Dataverse web resource `sprk_emailcomposer`. Mounts `<SendEmailPage />`. Reads URL `data=` query string parameters: `mode` (required, 1 of 5), `id` (required for view/reply/forward/draft), `to`, `cc`, `subject`, `body`, `associatedTo`. Auth bootstrap via `@spaarke/auth` v2 per ADR-028.
**Acceptance**: Code Page builds with `npm run build` + `build-webresource.ps1`; deploys to dev; opens with `mode=compose` URL; all 5 modes functional; auth bootstrap succeeds (no popup in steady state per `@spaarke/auth` v2).

**FR-19: Code Page entry surfaces (3 surfaces)**
Three entry surfaces all functional:
1. Ribbon button "+ New Email" on `sprk_communication` views (Active Communications, My Communications, related-record subgrids). Launches Code Page with `mode=compose` and `associatedTo` pre-filled when from a related-record subgrid.
2. Form Component Control replacing default `sprk_communication` form. Navigating to a record opens Code Page with `mode=view&id={recordGuid}`. Standard form retained as hidden / admin-only form.
3. Embeddable launch from other Code Pages / components via `Xrm.Navigation.navigateTo` (no central work — opportunistic adoption).
**Acceptance**: Ribbon button present on all `sprk_communication` views; Form Component Control wired; standard form retained as fallback; manual smoke test: open existing record (Surface 2), click "+ New Email" from a subgrid (Surface 1).

#### `sendCommunication()` + BFF DTO

**FR-20: `sendCommunication()` wrapper refinements**
Update `src/client/shared/Spaarke.UI.Components/src/services/communicationApi.ts`:
- Add `attachmentDriveItemIds?: string[]` as canonical field
- Mark `attachmentDocumentIds?: string[]` as `@deprecated` (logs `console.warn` when used)
- Add `SendCommunicationError` class exposing `status`, `code`, `detail`, `correlationId` (parsed from ProblemDetails)
- Wrapper throws `SendCommunicationError` on non-2xx
- Update docstrings to clarify driveItem semantics
**Acceptance**: Both `attachmentDriveItemIds` and `attachmentDocumentIds` accepted; deprecation warning logged; `SendCommunicationError` thrown on non-2xx; no breaking change for existing callers during migration.

**FR-21: BFF DTO field rename**
Update `src/server/api/Sprk.Bff.Api/Services/Communication/Models/SendCommunicationRequest.cs`:
- Add `public string[]? AttachmentDriveItemIds { get; init; }` as canonical
- Mark `public string[]? AttachmentDocumentIds { get; init; }` with `[Obsolete("Use AttachmentDriveItemIds")]`
- Server-side mapping: if `AttachmentDocumentIds` set and `AttachmentDriveItemIds` null, copy across; if both set, prefer canonical and log warning
- Update XML doc on both fields to clarify driveItem semantics
**Acceptance**: BFF accepts requests with either field name; non-breaking for existing clients; R4 removes the deprecated alias.

**FR-22: Internet-Message-Id post-send capture**
Update `CommunicationService.SendAsync()` to retrieve the real `Internet-Message-Id` after Graph `SendMail` succeeds and stamp it onto `sprk_communication.sprk_internetmessageid`. Strategy: query `graphClient.Users[sender].Messages` filtered by a deterministic property (correlationId in extended properties OR receivedDateTime window + subject match — selected during implementation per UQ3). On retrieval failure: retry once with backoff, then proceed without enrichment (best-effort, does not block send).
**Acceptance**: Outbound `sprk_communication` records have `sprk_internetmessageid` populated for successful sends; retrieval failure logs a warning but does not fail the send; reply matching by `IncomingAssociationResolver.cs:171` (existing inbound logic) successfully matches inbound replies to outbound records.

**FR-23: Dataverse schema check / additions**
Verify the following columns exist on `sprk_communication`:
- `sprk_inreplyto` — Lookup, self-referencing to `sprk_communication` (parent)
- `sprk_internetmessageid` — Single Line of Text (max 255), indexed for inbound `In-Reply-To` matching

Add either if missing. Document the columns in `docs/data-model/sprk_communication.md`.
**Acceptance**: Both columns present in dev environment; Dataverse customization deployed; column metadata documented.

#### Retirement

**FR-24: `sprk_communication_send.js` webresource retirement**
- Audit Dataverse for ribbon buttons / command bars / workflows referencing `sprk_communication_send.js`. List entry points in PR description.
- Replace each entry point with a ribbon button that launches the EmailComposer Code Page (Wave 2 provides the Code Page).
- Delete `src/client/webresources/js/sprk_communication_send.js` (~600 LOC).
- Delete `infrastructure/dataverse/ribbon/EmailRibbons/WebResources/sprk_communication_send.js` (duplicate).
- Delete the web resource record in Dataverse.
**Acceptance**: Both source files deleted; web resource record deleted in dev; all ribbon entry points functional via Code Page; no remaining self-bootstrapped MSAL in any client surface.

**FR-25: LegalWorkspace duplicate fork retirement**
After Waves 3–6, audit `src/solutions/LegalWorkspace/src/components/` for any remaining email-touching duplicates of `@spaarke/ui-components`. Delete duplicates; ensure LegalWorkspace re-exports from `@spaarke/ui-components`. Specifically resolve the cross-package source-path import at `WorkAssignmentWizardDialog.tsx:31`. Confirm zero email-touching forks remain.
**Acceptance**: `src/solutions/LegalWorkspace/src/components/{CreateMatter,CreateProject,CreateEvent,CreateTodo,CreateWorkAssignment,SummarizeFiles,FilePreview}/` contain only LegalWorkspace-specific code (not shared-lib forks); LegalWorkspace solution builds and deploys.

#### Documentation

**FR-26: Communication ADR (ADR-033)**
Create `.claude/adr/ADR-033-communication-architecture.md` codifying the canonical pattern. Sections: Status, Context (link to assessment), Decision (client surfaces + server pipeline + solution boundaries + out-of-scope), Consequences, Related (ADR-028, ADR-024, ADR-007, ADR-026). Cross-reference from `CLAUDE.md` §16 pointer table and `.claude/constraints/bff-extensions.md`.
**Acceptance**: ADR file created; cross-references updated; passes `/adr-check` against the spec.

**FR-27: 13 documentation updates**
Land all 13 documentation changes per design §11:
1. New: `.claude/adr/ADR-033-communication-architecture.md` (FR-26)
2. New: `docs/guides/EMAIL-COMPOSER-COMPONENT-GUIDE.md` — developer guide for `<EmailComposer />` + wrappers
3. New: `docs/standards/COMMUNICATION-ATTACHMENT-POLICY.md` — analog to `CHAT-ATTACHMENT-POLICY.md`
4. New: `docs/data-model/sprk_communication-form.md` — documents Code Page as canonical form
5. Update: `.claude/patterns/api/send-email-integration.md` — reference `<EmailComposer />` + wrappers + typed wrapper
6. Update: `.claude/FAILURE-MODES.md` AP-4 — note that `attachmentDriveItemIds` rename closes the latent bug class
7. Update: `.claude/constraints/bff-extensions.md` — Communication as sensitive surface; cite ADR-033
8. Update: `docs/architecture/communication-service-architecture.md` — add client-side section
9. Update: `docs/guides/SHARED-UI-COMPONENTS-GUIDE.md` — add EmailComposer section
10. Mark RETIRED: `docs/architecture/email-to-document-architecture.md` (R1 legacy)
11. Mark RETIRED: `docs/architecture/email-to-document-automation.md` (R1 legacy)
12. Update: `docs/architecture/sdap-overview.md` — fix `sprk_email*` field references on `sprk_document`
13. Update: `MEMORY.md` — add feedback memory for code review enforcement
**Acceptance**: Each item complete; doc-drift-audit skill confirms no orphaned references.

### Non-Functional Requirements

**NFR-01: React version**
`<EmailComposer />` is React 18 only. No PCF React 16 compatibility required. ADR-022 (PCF platform libraries) does not apply to this component.

**NFR-02: Auth decoupling**
Shared library components MUST NOT directly import `@spaarke/auth`. `authenticatedFetch` is injected via props (per existing decoupling rule in `communicationApi.ts:21-23`). Code Page is the only surface that imports `@spaarke/auth` directly.

**NFR-03: Styling — variants per mount**
Three explicit visual variants:
- `mount='page'`: primary entity form visual language (full-width, generous spacing, section dividers, prominent bottom-right action bar, page-level header showing entity context)
- `mount='dialog'`: compact dialog visual (bounded width ~600px, compressed spacing, dialog header + footer action bar)
- `mount='inline'`: wizard step visual (fills wizard step container; no internal header or action bar; wizard frame owns navigation)

Shared Fluent v9 design tokens across all mounts. Variants differ in layout density and chrome, not color/typography palette.

**NFR-04: Attachment limits**
Server-enforced caps (existing in `CommunicationService.cs`): 150 attachments max, 35 MB total. Client warns at 25 MB total. Client blocks attempts exceeding 150 / 35 MB before send. Blocked MIME types per `COMMUNICATION-ATTACHMENT-POLICY.md` (new, FR-27 item 3).

**NFR-05: Non-breaking server changes**
Both server-side changes (FR-21 DTO rename, FR-22 Internet-Message-Id capture) must not break existing callers during the migration. FR-21 uses a non-breaking alias. FR-22 is additive (populates a previously-null field).

**NFR-06: Uniform error contract**
All client send-email paths surface errors as `SendCommunicationError` (FR-09). Replaces 3 existing inconsistent error contracts identified in the assessment.

**NFR-07: Form replacement reversibility**
The Form Component Control swap (FR-19 Surface 2) must be reversible without code changes. Standard Dataverse form for `sprk_communication` must remain available as an admin/debug fallback form.

**NFR-08: Performance**
- Composer initial render: < 500 ms (page mount, cold load)
- Recipient autocomplete: < 200 ms per keystroke (after warm cache)
- Attachment upload progress UI: shown for files > 5 MB

**NFR-09: Accessibility**
- Fluent v9 accessibility patterns (per ADR-021)
- Full keyboard navigation across all composer controls
- Screen reader announcements for validation errors (live region)
- Focus management on mode transitions (e.g., Reply pre-focuses subject)

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance |
|---|---|
| **ADR-007** SpeFileStore facade | Server SPE archival uses the existing facade (unchanged) |
| **ADR-008** Endpoint filters for auth | BFF endpoints retain existing filter pattern |
| **ADR-010** DI minimalism | Communication services remain registered via `CommunicationModule` |
| **ADR-019** ProblemDetails for errors | `SendCommunicationError` parses ProblemDetails responses |
| **ADR-021** Fluent UI v9 | Composer styling uses Fluent v9 only |
| **ADR-024** Polymorphic resolver | Server-side `IncomingAssociationResolver` unchanged |
| **ADR-026** Full-page custom page standard | Code Page architecture follows this ADR |
| **ADR-028** Spaarke Auth v2 | Code Page bootstraps via `@spaarke/auth`; client surfaces inject `authenticatedFetch` |
| **NEW ADR-033** Communication architecture | Created in Wave 0 of this project; codifies the canonical pattern |

### Explicitly Not Applicable

- **ADR-022 PCF platform libraries / React 16** — explicitly NOT applicable. `<EmailComposer />` is React 18 only. No PCF mounts the composer (PCFs launch wizard Code Pages for email).

### MUST Rules

- ✅ MUST use `<EmailComposer />` engine (via `<SendEmailStep />`, `<SendEmailDialog />`, `<SendEmailPage />` wrappers) for any email-send UX in Spaarke
- ✅ MUST use `sendCommunication()` typed wrapper for any programmatic email send (no UI)
- ✅ MUST inject `authenticatedFetch` via props in shared library components (per existing decoupling rule)
- ✅ MUST register Communication-touching services via `CommunicationModule` only (ADR-010)
- ✅ MUST use ProblemDetails error responses on the BFF; `SendCommunicationError` parses them on the client (ADR-019)
- ✅ MUST use Fluent UI v9 for all composer styling (ADR-021)
- ✅ MUST use `@spaarke/auth` v2 bootstrap in the EmailComposer Code Page (ADR-028)
- ✅ MUST use the `code-page-deploy` skill conventions for the Code Page (build, bundle, deploy)

### MUST NOT Rules

- ❌ MUST NOT fork email-touching components in `src/solutions/LegalWorkspace/` — re-export from `@spaarke/ui-components`
- ❌ MUST NOT call `fetch` to `/api/communications/send` directly — use `sendCommunication()` or a wrapper
- ❌ MUST NOT self-bootstrap MSAL — use `@spaarke/auth`
- ❌ MUST NOT touch `/api/v1/emails/*` path — out of scope (dead code in production usage)
- ❌ MUST NOT use OOB Dataverse `email` activities (Spaarke deliberately avoids Server-Side Sync)
- ❌ MUST NOT directly import `@spaarke/auth` in shared library components — inject `authenticatedFetch`
- ❌ MUST NOT add a sixth client-side send-email implementation; if a new mount context emerges, add a new wrapper next to the existing three

### Existing Patterns to Follow

| Pattern | Path |
|---|---|
| Code Page structure | `src/client/code-pages/DocumentRelationshipViewer/` (exemplar per code-page-deploy skill) |
| Code Page auth bootstrap | `.claude/patterns/auth/spaarke-sso-binding.md` |
| Send-email integration | `.claude/patterns/api/send-email-integration.md` (will be updated in FR-27) |
| BFF endpoint pattern | `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs:50` |
| Communication architecture (server) | `docs/architecture/communication-service-architecture.md` |
| Polymorphic resolver | `.claude/adr/ADR-024-polymorphic-resolver-pattern.md` |
| Failure modes (AP-4) | `.claude/FAILURE-MODES.md` (will be updated in FR-27) |

---

## Success Criteria

From design §2.3 + execution criteria:

1. [ ] Exactly **one** client function (`sendCommunication`) handles every email send in the platform — Verify by: grep audit `fetch.*communications/send` returns zero results in `src/` (excluding compiled bundles)
2. [ ] Exactly **one** React component family (`<EmailComposer />` engine + 3 wrappers) renders the email-send UX — Verify by: grep audit `import.*SendEmailStep|SendEmailDialog|SendEmailPage|EmailComposer` covers all surfaces sending email
3. [ ] Zero callers send inline `fetch` to `/api/communications/send` — Verify by: assessment grep audit re-run
4. [ ] LegalWorkspace solution contains zero forked copies of email-touching shared-lib components — Verify by: per-component audit of `src/solutions/LegalWorkspace/src/components/`
5. [ ] `sprk_communication` Code Page is the default form opened from views in Dataverse — Verify by: open a `sprk_communication` record from a view; confirm Code Page loads (not standard form)
6. [ ] Communication ADR (ADR-033) exists and is referenced from `bff-extensions.md` and `CLAUDE.md` §16 — Verify by: file existence + cross-reference grep
7. [ ] All 8 waves merged to master — Verify by: PR list in project tracking
8. [ ] Smoke tests pass for each migrated surface — Verify by: manual test plan (5 wizards send-email; SummarizeFiles email step; FilePreview email dialog; DocumentEmailWizard send with attachments; Code Page compose / view / reply / forward / draft)
9. [ ] Reply-thread closure functional — Verify by: send email from Spaarke; recipient replies; inbound reply auto-associates to original outbound `sprk_communication` via `In-Reply-To` header matching
10. [ ] `attachmentDriveItemIds` field rename does not break existing callers — Verify by: regression test on DocumentEmailWizard send with attachments
11. [ ] No new lint errors / TypeScript errors in `@spaarke/ui-components` after each wave — Verify by: `npm run lint` + `npm run build`
12. [ ] CI passes on master after each wave merge — Verify by: GitHub Actions run status
13. [ ] `/adr-check` against this spec returns no violations — Verify by: skill invocation post-spec

---

## Dependencies

### Prerequisites

- **R2 (email-communication-solution-r2)** — server-side Communication Service. Status: ✅ Complete (April 2026). Master branch.
- **`@spaarke/auth` library** — v2.0.0+. Status: ✅ Available.
- **BFF `/api/communications/send` endpoint** — operational. Status: ✅ Operational in dev.
- **Communication Architecture Assessment (2026-06-05)** — drives the project. Status: ✅ Complete; in `docs/assessments/`.
- **Communication Architecture Consolidation #12 (backlog)** — this project. Status: ✅ Now scoped as R3.

### External Dependencies

- **Dataverse customization access** — required for Form Component Control change (FR-19 Surface 2), schema check/addition (FR-23), web resource deletion (FR-24)
- **Dataverse schema columns** — `sprk_inreplyto`, `sprk_internetmessageid` (verify existence; add if missing in Wave 0)
- **Code Page deployment pipeline** — per `code-page-deploy` skill (Wave 2)
- **Wizard solution build pipelines** — 5 wizards + SummarizeFiles + LegalWorkspace (multiple waves)
- **PCF rebuild required** — PCFs bundling `@spaarke/ui-components` (e.g., SemanticSearchControl, SpeDocumentViewer) require rebuild + redeploy after Waves 3–5 to pick up canonical components

---

## Owner Clarifications

Captured during 2026-06-05 design review session:

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Reply-thread closure | In scope or deferred? | **In scope**. Client modes (reply/forward) + server post-send Internet-Message-Id capture. | FR-11, FR-12, FR-22, FR-23 all in Wave 0/1 scope; reply chain visually closes in inbound matching (existing `IncomingAssociationResolver`). |
| Entry surfaces | Ribbon-only vs Form Component Control vs both? | **All three**: ribbon "+ New Email", Form Component Control replacing default form, embeddable from other Code Pages. | FR-19 scoped to 3 surfaces; Form Component Control retains standard form as admin fallback. |
| React version | React 16 compat for PCFs? | **React 18 only**. No PCF hosts the composer directly. | NFR-01; composer is free to use React 18 hooks (`useId`, etc.). |
| Wave 5 PR shape | One PR for 5 wizards or per-wizard? | **Single PR for all 5**. Minimizes CI/CD overhead. | FR-16 single PR; risk register notes review burden trade-off. |
| Webresource | Retire vs redirect? | **Retire** (option 6a). | FR-24 deletes both copies + Dataverse record. |
| Wrapper layer | Keep wrappers vs drop? | **Keep 3 thin wrappers**: `<SendEmailStep />`, `<SendEmailDialog />`, `<SendEmailPage />`. | FR-02, FR-03, FR-04 build the wrappers; FR-15–FR-17 callers use wrappers, not engine directly. |
| Component dev rig | Storybook / visual rig? | **Not required**. Lightweight only if anything. | Wave 1 ships with unit tests + visual validation via Wave 2 Code Page deploy; no dedicated rig. |
| Composer styling | Same look across mounts or variants? | **Restyle for form-replacement; explicit variants per mount**. | NFR-03; page mount = primary entity form look; dialog = compact; inline = wizard step. |

---

## Assumptions

Proceeding with these assumptions (validated during implementation):

- **A1**: The existing `sendCommunication()` wrapper in `communicationApi.ts` is the right typed function to evolve. No need to introduce a separate canonical function. The wrapper's prop API extends to support `attachmentDriveItemIds` and `SendCommunicationError` without breaking existing callers.
- **A2**: Form Component Control (FR-19 Surface 2) can be configured in Dataverse without breaking existing customizations on the standard `sprk_communication` form (custom JS, business rules, field visibility). Validation required during Wave 2.
- **A3**: The reply-thread `Internet-Message-Id` post-send capture (FR-22) can be implemented without breaking existing `CommunicationService.SendAsync()` side effects (daily send count, SPE archival, etc.). Best-effort failure mode ensures the send path remains critical.
- **A4**: Server-side mapping between `AttachmentDocumentIds` (deprecated) and `AttachmentDriveItemIds` (canonical) is safe under concurrent caller migration. Both fields can be populated during the transition; canonical takes precedence.
- **A5**: Existing PCFs that bundle `@spaarke/ui-components` (e.g., SemanticSearchControl, SpeDocumentViewer) do NOT mount `<EmailComposer />` directly. Per design review Q3, they launch wizard Code Pages. If a PCF is later found to mount the composer directly, NFR-01 (React 18) requires re-evaluation.

---

## Unresolved Questions

Implementation-time questions (not blocking spec; resolved during the relevant wave):

- [ ] **UQ1** — Standard form customizations migration. When Form Component Control replaces the default `sprk_communication` form (FR-19 Surface 2), what happens to existing custom JavaScript / business rules / field visibility on the standard form? **Resolution**: During Wave 2, audit existing form customizations; document migration path or carry to admin fallback form. **Blocks**: Wave 2 deploy decision.

- [ ] **UQ2** — View mode editability for non-draft records. Is there an "Edit" mode for in-place text corrections on a sent `sprk_communication`, or strictly read-only with Reply / Forward as the only mutation paths? **Resolution**: Per design intent, view mode is read-only; sent records are immutable. Confirm during Wave 1 by checking with owner. **Blocks**: nothing critical; spec can ship with read-only assumption.

- [ ] **UQ3** — Internet-Message-Id post-send retrieval strategy. Design suggests "correlationId in extended properties OR receivedDateTime window + subject match." Which Graph property reliably maps to our correlationId? **Resolution**: Wave 0 server task starts with a 1-hour spike to validate retrieval strategy; document choice in the PR. **Blocks**: FR-22 implementation detail; does not block spec.

- [ ] **UQ4** — ADR number confirmation. Spec proposes **ADR-033**. Existing ADRs go through ADR-032. **Resolution**: Verify no other parallel project is claiming 033 before Wave 0; if conflict, claim next available. **Blocks**: ADR file creation in Wave 0.

- [ ] **UQ5** — DocumentEmailWizard driveItem ID source. Does the `wizardContext.uploadedFiles` convention cover the translation from `sprk_document` GUIDs to driveItem IDs, or does the wizard need a separate translation layer? **Resolution**: Validated during Wave 1 — the `wizardContext.uploadedFiles` shape includes both `documentId` AND `driveItemId`, so the composer always uses `driveItemId`. **Blocks**: FR-17 implementation detail; not a spec blocker.

---

## Backlog Referrals (file in Wave 0)

Add to `projects/_backlog/needs-a-project.md`:

- **#12 Investigate retirement of `/api/v1/emails/*`** (Email-to-Document Automation subsystem). Built Jan 2026, operates on Power Apps Email activities which Spaarke does not use. Functionally dead in production usage.
- **#13 Build `sprk_communication` browse / thread / inbox UI**. R3 builds the form (single record); server already creates records; no list view exists.
- **#14 Outlook add-in alignment with canonical composer**. When the add-in is next touched, evaluate whether it can mount `<EmailComposer />`.
- **#15 Future `TeamsMessage` / `SMS` / `Notification` composers**. Sibling components in `@spaarke/ui-components` when use cases land.

---

*AI-optimized specification. Original design: [`design.md`](design.md). Driver assessment: [Communication Architecture Assessment 2026-06-05](../../docs/assessments/communication-architecture-assessment-2026-06-05.md). Next step: review, then `/project-pipeline projects/email-communication-solution-r3`.*
