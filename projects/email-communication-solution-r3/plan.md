# Project Plan: Email Communication Solution R3

> **Last Updated**: 2026-06-05
> **Status**: Ready for Tasks
> **Spec**: [`spec.md`](spec.md)
> **Design**: [`design.md`](design.md)
> **Driver**: [Communication Architecture Assessment 2026-06-05](../../docs/assessments/communication-architecture-assessment-2026-06-05.md)

---

## 1. Executive Summary

**Purpose**: Replace 6 ad-hoc client-side email-send implementations in Spaarke with ONE canonical `<EmailComposer />` engine + 3 wrappers + 1 Code Page + 1 typed `sendCommunication()` wrapper, plus two surgical BFF additions (non-breaking DTO rename + post-send `Internet-Message-Id` capture). Retire `sprk_communication_send.js` (~2.3K LOC) and LegalWorkspace forks. Codify the pattern as ADR-033 so future surfaces cannot re-fragment.

**Scope**:
- Engine + wrappers + sub-components in `@spaarke/ui-components`
- Standalone Code Page replacing the auto-generated `sprk_communication` form (3 entry surfaces)
- Refined client wrapper with typed `SendCommunicationError` + `attachmentDriveItemIds`
- BFF DTO + post-send `Internet-Message-Id` capture
- Dataverse schema check/add for `sprk_inreplyto`, `sprk_internetmessageid`
- 6 caller migrations + retirement of duplicate forks + webresource deletion
- ADR-033 (NEW) + 13 documentation updates

**Timeline**: ~6–8 weeks at the AI-paced cadence (varies by parallelism + review turnaround). **Estimated effort**: ~250–320 hours across ~77 tasks.

---

## 2. Architecture Context

### Design Constraints (must comply)

**From ADRs:**
- **ADR-007 (SPE-FILESTORE)** — Server SPE archival uses existing facade; client never reaches SPE directly
- **ADR-008 (ENDPOINT-FILTERS)** — BFF endpoints retain endpoint-filter auth pattern (no middleware)
- **ADR-010 (DI-MINIMALISM)** — Communication services register via `CommunicationModule` only
- **ADR-019 (PROBLEMDETAILS)** — `SendCommunicationError` parses ProblemDetails error responses
- **ADR-021 (FLUENT-DESIGN-SYSTEM)** — Composer uses Fluent UI v9 only; dark mode required
- **ADR-024 (POLYMORPHIC-RESOLVER-PATTERN)** — Server-side `IncomingAssociationResolver` unchanged
- **ADR-026 (FULL-PAGE-CUSTOM-PAGE-STANDARD)** — Code Page follows this ADR
- **ADR-028 (SPAARKE-AUTH-ARCHITECTURE)** — Code Page uses `@spaarke/auth` v2; shared library components inject `authenticatedFetch` (no direct `@spaarke/auth` import)
- **ADR-033 (NEW, created Wave 0)** — Codifies the canonical client-side Communication architecture

**Explicitly Not Applicable:** ADR-022 (PCF React 16 compat) — composer is React 18 only; no PCF mounts it directly.

**From Spec:**
- React 18 only (NFR-01)
- Shared lib components MUST NOT directly import `@spaarke/auth` — `authenticatedFetch` injected via props (NFR-02)
- Three explicit visual variants per mount: `page` (full-width form), `dialog` (compact), `inline` (wizard step) — shared Fluent v9 tokens (NFR-03)
- Server-enforced attachment caps: 150 max, 35 MB total; client warns at 25 MB (NFR-04)
- Non-breaking server changes; FR-21 alias for migration; FR-22 additive (NFR-05)
- All client send-email paths surface errors as `SendCommunicationError` (NFR-06)
- Form Component Control swap reversible without code changes (NFR-07)
- Performance: composer cold-load < 500 ms; autocomplete < 200 ms; attachment progress UI for files > 5 MB (NFR-08)
- Full Fluent v9 accessibility: keyboard nav, screen-reader live region for errors, focus management on mode transitions (NFR-09)

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Engine + 3 thin wrappers (vs engine-only direct usage) | Wrappers carry semantic prop API per owner clarification; engine internal | Each consumer uses the right wrapper; engine API stays free to refactor |
| Standalone Code Page (vs embedded entity-form control) | Matches ADR-026; reuses `DocumentRelationshipViewer` pattern | Single self-contained HTML web resource deploys via existing pipeline |
| Form Component Control swap (vs custom command-bar redirect) | Native Dataverse pattern; reversible | Standard form retained as admin fallback (NFR-07) |
| Single PR for 5-wizard migration (vs per-wizard) | Owner-clarified: minimizes CI/CD overhead | Wave 5 ships as one large PR; per-wizard sections in PR description |
| Non-breaking DTO alias (vs hard rename) | Concurrent client migration | Both `AttachmentDocumentIds` and `AttachmentDriveItemIds` accepted during R3; R4 removes the alias |
| Best-effort `Internet-Message-Id` retrieval (vs blocking) | Spec FR-22: retrieval failure must not fail send | Retry once with backoff, then proceed; warning log only |

### Discovered Resources

**Applicable ADRs** (auto-loaded by `task-execute` per task tags):
- `.claude/adr/ADR-007-spe-filestore.md` — SPE facade unchanged
- `.claude/adr/ADR-008-endpoint-filters-authorization.md` — BFF endpoint filters
- `.claude/adr/ADR-010-di-minimalism.md` — `CommunicationModule` registration
- `.claude/adr/ADR-019-problemdetails-error-responses.md` — `SendCommunicationError` parsing
- `.claude/adr/ADR-021-fluent-design-system.md` — Fluent v9 + dark mode
- `.claude/adr/ADR-024-polymorphic-resolver-pattern.md` — Inbound association resolver
- `.claude/adr/ADR-026-full-page-custom-page-standard.md` — Code Page architecture
- `.claude/adr/ADR-028-spaarke-auth-architecture.md` — `@spaarke/auth` v2 bootstrap
- `.claude/adr/ADR-033-communication-architecture.md` — **NEW** (created in Wave 0 task 001)

**Applicable Skills:**
- `fluent-v9-component` — composer + sub-component authoring patterns
- `code-page-deploy` — Wave 2 Code Page deploy
- `dataverse-deploy` — solution + ribbon + web resource deploy
- `dataverse-create-schema` — Wave 0 schema additions (`sprk_inreplyto`, `sprk_internetmessageid`)
- `ribbon-edit` — Wave 2 ribbon button "+ New Email"; Wave 6 entry-point cleanup
- `bff-deploy` — BFF deploy after Wave 0 server changes
- `adr-aware`, `adr-check` — per-task ADR loading + validation
- `code-review` — Wave wrap-ups + 099-project-wrap-up
- `ui-test` — Wave 2 Code Page UI tests (compose/view/reply/forward/draft + dark mode)
- `doc-drift-audit` — Wave 6 documentation cleanup
- `merge-to-master` — branch merge after Wave 6

**Knowledge Articles + Patterns:**
- `.claude/patterns/api/send-email-integration.md` — current send-email integration pattern (will be updated in Wave 6 task 086)
- `.claude/patterns/auth/spaarke-sso-binding.md` — Code Page auth bootstrap pattern
- `.claude/constraints/bff-extensions.md` — BFF governance (will be cross-referenced from new ADR-033)
- `docs/architecture/communication-service-architecture.md` — server-side reference; client-side section added in Wave 6 task 088
- `docs/guides/SHARED-UI-COMPONENTS-GUIDE.md` — shared library guide (EmailComposer section added in Wave 6 task 089)
- `docs/guides/COMMUNICATION-DEPLOYMENT-GUIDE.md` — existing deployment context
- `docs/standards/CHAT-ATTACHMENT-POLICY.md` — analog for new `COMMUNICATION-ATTACHMENT-POLICY.md` (Wave 6 task 084)
- `docs/assessments/communication-architecture-assessment-2026-06-05.md` — driver assessment
- `docs/adr/ADR-{007,008,010,019,021,024,026,028}-*.md` — full ADR text (for context when task POML cites it)

**Reusable Code:**
- `src/client/code-pages/DocumentRelationshipViewer/` — Code Page exemplar (22 files; auth bootstrap + URL parsing + `@spaarke/auth` integration)
- `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs` — existing BFF endpoint pattern
- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs` — server pipeline to extend with `Internet-Message-Id` capture
- `src/client/shared/Spaarke.UI.Components/src/services/communicationApi.ts` — typed wrapper to refine
- `src/client/shared/Spaarke.UI.Components/src/components/SendEmailDialog/` — existing dialog to rewrite as canonical wrapper
- `src/client/shared/Spaarke.UI.Components/src/components/CreateRecordWizard/steps/SendEmailStep.tsx` — current shared step (consumed by Project/Event/Todo/WorkAssignment); becomes a thin re-export of canonical wrapper

**Empirical Findings (delta from spec assumptions — verified at pipeline pre-flight):**
- LegalWorkspace has **ONE** email-step fork (`CreateMatter/SendEmailStep.tsx`), NOT 5 as spec implied. Project/Event/Todo/WorkAssignment dirs exist in LegalWorkspace but contain no email step. Wave 5 scope is correspondingly smaller.
- `WorkAssignmentWizardDialog.tsx:31` has a cross-package source-path import: `import { SendEmailStep } from '../CreateRecordWizard/steps/SendEmailStep';` — flagged as the canonical resolution target.
- `sprk_communication_send.js` is ~1,150 LOC per copy (not ~600 LOC as spec stated); retirement is ~2.3K LOC removal.
- `CommunicationEndpoints.cs` lives at `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs` (NOT under `Services/Communication/Api/`).
- `communicationApi.ts` already has `sendCommunication()` but lacks `SendCommunicationError` export and `attachmentDriveItemIds` field.
- `SendCommunicationRequest.cs` has `AttachmentDocumentIds` only; missing `AttachmentDriveItemIds`.
- `CommunicationService.cs` does NOT yet capture `Internet-Message-Id` post-send.
- DocumentEmailWizard latent bug at line 494 confirmed.
- SummarizeFilesDialog inline `fetch` confirmed at line 436.

---

## 3. Implementation Approach

### Phase Structure (Waves)

```
Wave 0 (Foundations + non-breaking BFF):       tasks 001–008 — ~8 tasks
Wave 1 (Engine + wrappers + sub-components):   tasks 010–029 — ~20 tasks
Wave 2 (Code Page + 3 entry surfaces):         tasks 030–045 — ~16 tasks
Wave 3 (SendEmailDialog + FilePreview):        tasks 050–055 — ~6 tasks
Wave 4 (SummarizeFiles migration):             tasks 060–065 — ~6 tasks
Wave 5 (5-wizard migration + Doc fix + forks): tasks 070–077 — ~8 tasks
Wave 6 (Retirement + 13 doc updates):          tasks 080–093 — ~14 tasks
Wrap-up:                                       task  099    — 1 task
                                               Total ~77 tasks
```

### Critical Path

**Blocking dependencies:**
- Wave 1 (engine) BLOCKED BY Wave 0 (foundations: ADR-033 + BFF DTO + schema)
- Wave 2 (Code Page) BLOCKED BY Wave 1 (uses the canonical wrappers)
- Waves 3, 4 PARALLEL (independent surfaces) BLOCKED BY Wave 1
- Wave 5 BLOCKED BY Waves 1+2 (uses canonical components in shipped form)
- Wave 6 retirement BLOCKED BY Waves 2–5 (all consumers migrated first)
- Wave 6 documentation INDEPENDENT of Wave 6 retirement (can run in parallel within wave)
- Wrap-up BLOCKED BY all prior waves

**High-risk items:**
- Form Component Control swap breaking existing customizations (Wave 2 task 035 audits UQ1)
- Internet-Message-Id retrieval strategy uncertainty (Wave 0 task 003 spike resolves UQ3)
- Wave 5 single-PR diff size (Owner-decided; PR description structure mitigates)

### Parallel Execution Opportunities

Documented in `tasks/TASK-INDEX.md` Parallel Execution Groups section. Highlights:
- **Wave 0**: Tasks 002 (BFF DTO), 003 (Internet-Message-Id), 004 (schema check), 005 (client wrapper), 006 (backlog file) can run in parallel after 001 (ADR-033) completes
- **Wave 1**: Sub-components (012–019) can be parallelized after engine skeleton (010, 011) lands
- **Wave 2**: UI test definitions (041–045) parallel after Code Page mounts; ribbon button (037) and Form Component Control (038) parallel after Code Page deploys
- **Wave 6**: 13 documentation updates split into independent file targets — 5–6-way parallelism possible

`.claude/`-write boundary: ADR-033 creation (001), `.claude/FAILURE-MODES.md` update (087), `.claude/constraints/bff-extensions.md` update (087), `.claude/patterns/api/send-email-integration.md` update (086) — all marked `parallel-safe: false` (main-session-only per CLAUDE.md "Sub-Agent Write Boundary").

---

## 4. Phase Breakdown

### Wave 0: Foundations + Non-Breaking BFF (tasks 001–008)

**Objectives:**
1. Codify the canonical pattern in ADR-033 BEFORE any code change
2. Add non-breaking BFF DTO field `AttachmentDriveItemIds` (alias for migration)
3. Implement post-send `Internet-Message-Id` capture (UQ3 spike → choose strategy → implement)
4. Verify (or add) Dataverse columns `sprk_inreplyto` + `sprk_internetmessageid`
5. Refine `sendCommunication()` client wrapper (add `attachmentDriveItemIds` + `SendCommunicationError`)
6. File backlog referrals (#12 retire `/api/v1/emails/*`, #13 inbox UI, #14 Outlook add-in, #15 future composers)

**Deliverables:**
- [ ] `.claude/adr/ADR-033-communication-architecture.md` (new file)
- [ ] `src/server/api/Sprk.Bff.Api/Services/Communication/Models/SendCommunicationRequest.cs` updated (canonical field + alias + mapping)
- [ ] `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs` extended with `Internet-Message-Id` post-send retrieval
- [ ] Dataverse columns verified / added: `sprk_inreplyto` (Lookup self-ref), `sprk_internetmessageid` (Text 255 indexed)
- [ ] `src/client/shared/Spaarke.UI.Components/src/services/communicationApi.ts` updated (canonical field + `SendCommunicationError` class)
- [ ] `projects/_backlog/needs-a-project.md` updated with 4 backlog items
- [ ] BFF deployed to dev environment; integration tests pass

**Critical Tasks:**
- 001 (ADR-033) MUST BE FIRST — codifies pattern before code changes
- 003 (Internet-Message-Id) includes 1-hour UQ3 spike

**Inputs:** `.claude/adr/ADR-028-spaarke-auth-architecture.md`, `.claude/adr/ADR-024-polymorphic-resolver-pattern.md`, `.claude/constraints/bff-extensions.md`, existing `CommunicationService.cs`, existing `communicationApi.ts`

**Outputs:** ADR-033 + 3 modified C# files + 1 modified TS file + 4 Dataverse schema rows + 4 backlog rows + dev BFF deploy

---

### Wave 1: EmailComposer Engine + Wrappers + Sub-Components (tasks 010–029)

**Objectives:**
1. Build canonical `<EmailComposer />` engine — 5 modes × 3 mounts × imperative handle
2. Build 3 thin wrappers (`SendEmailStep`, `SendEmailDialog`, `SendEmailPage`)
3. Build sub-components (`RecipientField`, `BodyEditor`, `AttachmentList` + 4 source pickers, `ComposerActionBar`)
4. Implement validation contract with canonical error codes
5. Wire all 5 modes (compose / view / reply / forward / draft)
6. Unit tests for engine state machine, sub-components, mode transitions

**Deliverables:**
- [ ] `src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/` directory with engine + 3 wrappers + 6 sub-components + tests
- [ ] State machine via `useReducer`; imperative handle exposing `validate()`, `send()`, `saveDraft()`, `getState()`
- [ ] Validation contract: `IValidationResult` with canonical codes (TO_REQUIRED, TO_INVALID_EMAIL, SUBJECT_REQUIRED, BODY_REQUIRED, ATTACHMENT_TOO_LARGE, etc.)
- [ ] All 5 modes functional; mode transitions preserve content where possible
- [ ] `npm run build` + `npm run lint` + unit tests pass in `@spaarke/ui-components`
- [ ] Shared lib published to local dev consumers

**Critical Tasks:**
- 010 (scaffold directory) + 011 (engine skeleton) MUST be first; sub-components depend on engine
- 028 (engine tests) — full coverage of state machine, validation, normalization

**Inputs:** Wave 0 outputs (ADR-033 + canonical wrapper), `.claude/patterns/api/send-email-integration.md`, `docs/guides/SHARED-UI-COMPONENTS-GUIDE.md`

**Outputs:** EmailComposer module + wrappers + tests; shared lib builds clean

---

### Wave 2: EmailComposer Code Page + 3 Entry Surfaces (tasks 030–045)

**Objectives:**
1. Build standalone Code Page at `src/client/code-pages/EmailComposer/` per `code-page-deploy` skill
2. URL parameter parsing (mode, id, to, cc, subject, body, associatedTo)
3. `@spaarke/auth` v2 bootstrap (no popup in steady state)
4. Deploy to dev environment
5. Wire 3 entry surfaces: ribbon "+ New Email", Form Component Control swap, embeddable launch
6. UQ1: audit existing standard form customizations and document migration path
7. UI tests: compose / view / reply / forward / draft modes + dark mode (ADR-021)

**Deliverables:**
- [ ] `src/client/code-pages/EmailComposer/` directory with HTML artifact + build script
- [ ] Code Page deploys as Dataverse web resource `sprk_emailcomposer`
- [ ] Ribbon button "+ New Email" present on `sprk_communication` views (Active, My, related-record subgrids)
- [ ] Form Component Control replaces default `sprk_communication` form; standard form retained as admin fallback
- [ ] All 5 mode URLs functional (`mode=compose|view|reply|forward|draft`)
- [ ] UI tests pass for all 5 modes + dark mode (ADR-021)
- [ ] UQ1 audit documents: existing form custom JS, business rules, migration path

**Critical Tasks:**
- 030 (scaffold) → 031 (auth) → 032 (URL parse) → 033 (mount) → 034 (build pipeline) sequential
- 035 (UQ1 audit) gates 038 (Form Component Control)
- 036 (deploy) gates 037, 038, 040, 041–045

**Inputs:** Wave 1 outputs (`<SendEmailPage />` wrapper), `src/client/code-pages/DocumentRelationshipViewer/` (exemplar), `.claude/patterns/auth/spaarke-sso-binding.md`, `docs/guides/PCF-DEPLOYMENT-GUIDE.md` (for deployment patterns analogous to Code Pages)

**Outputs:** Code Page + 3 entry surfaces + UI tests passing in dev

---

### Wave 3: SendEmailDialog Rewrite + FilePreviewDialog Migration (tasks 050–055)

**Objectives:**
1. Rewrite existing `SendEmailDialog` as canonical `<SendEmailDialog />` wrapper around engine
2. Migrate `LegalWorkspace/FilePreview/FilePreviewDialog.tsx` to use the wrapper (remove inline `fetch`)
3. Update `ISendEmailDialogProps` interface to canonical shape (`open`, `onClose`, `onSent`, `onError`)
4. Audit + migrate any other `SendEmailDialog` consumers

**Deliverables:**
- [ ] `src/client/shared/Spaarke.UI.Components/src/components/SendEmailDialog/SendEmailDialog.tsx` is now a thin wrapper around `<EmailComposer mount='dialog' />`
- [ ] `src/solutions/LegalWorkspace/src/components/FilePreview/FilePreviewDialog.tsx` has zero inline `fetch` to `/api/communications/send`
- [ ] LegalWorkspace solution builds + deploys
- [ ] Manual smoke test: FilePreview file-email flow works end-to-end in dev

**Critical Tasks:**
- 050 (SendEmailDialog rewrite) before 051 (FilePreviewDialog migration)

**Inputs:** Wave 1 outputs (engine + wrappers), existing `SendEmailDialog.tsx`, `FilePreviewDialog.tsx`

**Outputs:** Canonical `SendEmailDialog` wrapper + LegalWorkspace FilePreview migration + dev smoke test pass

---

### Wave 4: SummarizeFilesDialog Migration (tasks 060–065)

**Objectives:**
1. Migrate `Spaarke.UI.Components/.../SummarizeFilesWizard/SummarizeFilesDialog.tsx` to use `<SendEmailStep />` wrapper
2. Delete LegalWorkspace fork `src/solutions/LegalWorkspace/src/components/SummarizeFiles/SummarizeFilesDialog.tsx`
3. Re-export from `@spaarke/ui-components`
4. Build + deploy both solutions
5. Smoke test Summarize email step

**Deliverables:**
- [ ] Shared `SummarizeFilesDialog.tsx` has zero inline `fetch`
- [ ] LegalWorkspace `SummarizeFiles/` fork deleted; re-exports from shared lib
- [ ] SummarizeFilesWizard solution + LegalWorkspace solution build + deploy
- [ ] Manual smoke test: Summarize wizard's email step works in dev

**Inputs:** Wave 1 outputs, current `SummarizeFilesDialog.tsx` (line 436 has inline fetch)

**Outputs:** Migration + fork delete + smoke test pass

---

### Wave 5: 5-Wizard Migration + DocumentEmailWizard Fix + LegalWorkspace Consolidation (tasks 070–077)

**Objectives:**
1. Refactor `EntityCreationService.sendEmail()` to thin adapter (≤30 LOC) over `sendCommunication()`
2. Migrate shared `CreateRecordWizard/steps/SendEmailStep.tsx` to canonical `<SendEmailStep />` wrapper
3. Migrate `CreateMatterWizard/SendEmailStep.tsx` (separate file) to canonical wrapper
4. Resolve `WorkAssignmentWizardDialog.tsx:31` cross-package source-path import
5. Delete LegalWorkspace `CreateMatter/SendEmailStep.tsx` fork (only true fork — others don't exist)
6. Migrate `DocumentEmailWizard.tsx` to canonical `attachmentDriveItemIds` (fixes latent bug at line 494)
7. Single PR for all wizard migrations (Owner-clarified)

**Deliverables:**
- [ ] `EntityCreationService.sendEmail()` is ≤30 LOC, calls `sendCommunication()` directly
- [ ] Shared `CreateRecordWizard/steps/SendEmailStep.tsx` wraps canonical `<SendEmailStep />`
- [ ] `CreateMatterWizard/SendEmailStep.tsx` wraps canonical wrapper
- [ ] `WorkAssignmentWizardDialog.tsx:31` import resolved to `@spaarke/ui-components`
- [ ] LegalWorkspace `CreateMatter/SendEmailStep.tsx` deleted
- [ ] `DocumentEmailWizard.tsx` line 494 uses `driveItem` IDs (sourced from `wizardContext.uploadedFiles`)
- [ ] All 5 wizards + LegalWorkspace solutions build + deploy
- [ ] Smoke tests pass for: CreateMatter, CreateProject, CreateEvent, CreateTodo, CreateWorkAssignment, DocumentEmailWizard with attachments

**Critical Tasks:**
- 070 (EntityCreationService refactor) BLOCKS all wizard migrations
- 071 (shared step) BLOCKS 072 (CreateMatter step) only if same imports; otherwise parallel
- 075 (DocumentEmailWizard) independent of wizard migrations
- 076 (deploy) BLOCKED BY 070–075

**Inputs:** Waves 1+2 outputs (canonical wrappers + Code Page deployed), `EntityCreationService.ts`, all 5 wizard source files, `DocumentEmailWizard.tsx`

**Outputs:** 5 wizards on canonical wrapper + LegalWorkspace CreateMatter fork gone + cross-package import resolved + DocumentEmailWizard bug fixed + single PR

---

### Wave 6: Retirement + 13 Documentation Updates (tasks 080–093)

**Objectives:**
1. Audit Dataverse for `sprk_communication_send.js` ribbon-button / command-bar / workflow references
2. Replace each entry point with Code Page (already deployed in Wave 2)
3. Delete both `sprk_communication_send.js` source files (~2.3K LOC) and the Dataverse web resource record
4. Final LegalWorkspace email-touching duplicate audit (per spec FR-25, confirms zero forks remain)
5. Land 13 documentation updates per spec FR-27

**Deliverables:**
- [ ] Both `sprk_communication_send.js` files deleted (`src/client/webresources/js/` + `infrastructure/dataverse/ribbon/EmailRibbons/WebResources/`)
- [ ] Dataverse web resource record deleted
- [ ] All ribbon entry points functional via Code Page
- [ ] Final LegalWorkspace audit: 0 email-touching forks remain
- [ ] **NEW** `docs/guides/EMAIL-COMPOSER-COMPONENT-GUIDE.md`
- [ ] **NEW** `docs/standards/COMMUNICATION-ATTACHMENT-POLICY.md`
- [ ] **NEW** `docs/data-model/sprk_communication-form.md`
- [ ] **UPDATE** `.claude/patterns/api/send-email-integration.md` → reference canonical wrappers
- [ ] **UPDATE** `.claude/FAILURE-MODES.md` AP-4 → note `attachmentDriveItemIds` rename closes latent bug class
- [ ] **UPDATE** `.claude/constraints/bff-extensions.md` → Communication as sensitive surface, cite ADR-033
- [ ] **UPDATE** `docs/architecture/communication-service-architecture.md` → add client-side section
- [ ] **UPDATE** `docs/guides/SHARED-UI-COMPONENTS-GUIDE.md` → add EmailComposer section
- [ ] **RETIRE** `docs/architecture/email-to-document-architecture.md` → mark banner
- [ ] **RETIRE** `docs/architecture/email-to-document-automation.md` → mark banner
- [ ] **UPDATE** `docs/architecture/sdap-overview.md` → fix `sprk_email*` field refs on `sprk_document`
- [ ] **UPDATE** root `MEMORY.md` → add feedback memory for code-review enforcement
- [ ] `/doc-drift-audit` passes (no orphaned references)

**Critical Tasks:**
- 080 (audit ribbon entry points) BLOCKS 081, 082, 083 (deletes) — must replace consumers first
- Documentation tasks 083–092 can run in 5–6-way parallel (independent file targets), EXCEPT those touching `.claude/` are main-session-only
- 093 (`/doc-drift-audit`) AFTER all doc updates

**Inputs:** Waves 0–5 outputs (everything migrated), assessment doc, existing FAILURE-MODES.md, existing bff-extensions.md, existing patterns

**Outputs:** ~2.3K LOC removed + 3 new docs + 8 doc updates + 2 retirement banners + drift-audit pass

---

### Wrap-up: Project Completion (task 099)

**Objectives:**
1. `/code-review` on all project code (cross-cutting)
2. `/adr-check` on all project code
3. Fix any critical findings
4. `/repo-cleanup projects/email-communication-solution-r3`
5. Update `README.md` Status → Complete, all graduation criteria checked
6. Update `plan.md` Status → Complete
7. Write `notes/lessons-learned.md` (per R2 precedent)
8. Final verification: all `TASK-INDEX.md` rows ✅

**Deliverables:**
- [ ] `/code-review` report — no Critical findings remain
- [ ] `/adr-check` report — no violations
- [ ] Repo-cleanup report reviewed; ephemeral notes archived
- [ ] README + plan statuses → Complete
- [ ] `notes/lessons-learned.md` written

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| R2 server-side Communication Service | ✅ Production | Low | Already operational |
| `@spaarke/auth` v2.0.0+ | ✅ Available | Low | — |
| BFF `/api/communications/send` endpoint | ✅ Operational dev | Low | — |
| Dataverse customization access (Form Component Control, schema, web resource) | ✅ Available | Low | — |
| Microsoft Graph `SendMail` API behavior | ✅ Stable | Med | UQ3 spike de-risks `Internet-Message-Id` retrieval strategy |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| `code-page-deploy` skill | `.claude/skills/code-page-deploy/` | Available |
| `fluent-v9-component` skill | `.claude/skills/fluent-v9-component/` | Available |
| `dataverse-create-schema` skill | `.claude/skills/dataverse-create-schema/` | Available |
| `ribbon-edit` skill | `.claude/skills/ribbon-edit/` | Available |
| `ui-test` skill (Chrome integration) | `.claude/skills/ui-test/` | Available |
| `DocumentRelationshipViewer` Code Page (exemplar) | `src/client/code-pages/DocumentRelationshipViewer/` | Production |
| Existing `CommunicationService.cs` pipeline | `src/server/api/Sprk.Bff.Api/Services/Communication/` | Production |

---

## 6. Testing Strategy

**Unit Tests** (Wave 1 and as part of each migration wave):
- Engine state machine (5 modes × transitions)
- Validation contract (every canonical error code)
- Recipient field separator parsing (`;`, `,`, paste-from-Outlook)
- Body editor mode toggle (HTML ↔ PlainText content preservation, best-effort)
- Attachment cap enforcement (150 / 35 MB; warn at 25 MB)
- `SendCommunicationError` parsing from ProblemDetails

**Integration Tests** (BFF, Wave 0):
- `AttachmentDriveItemIds` accepted, mapped, sent to Graph
- `AttachmentDocumentIds` alias accepted, deprecation warning logged
- `Internet-Message-Id` retrieval succeeds → stamped on `sprk_communication`
- Retrieval failure → send proceeds, warning logged

**E2E Tests / UI Tests** (Wave 2, via `ui-test` skill):
- Code Page renders in compose mode
- Code Page opens record in view mode
- Reply mode pre-fills + stamps `sprk_inreplyto`
- Forward mode carries attachments + stamps `sprk_inreplyto`
- Draft mode resumes saved draft + transitions on Send
- Dark mode compliance (ADR-021) for all controls
- Console error check (zero errors) for all 5 modes

**Smoke Tests** (per migration wave):
- Wave 3: FilePreview file-email
- Wave 4: SummarizeFilesWizard email step
- Wave 5: All 5 wizards send-email + DocumentEmailWizard with attachments (regression for latent bug fix)
- Wave 6: Code Page accessible via all 3 entry surfaces; ribbon "+ New Email" launches correctly

**Reply-thread closure regression** (post Wave 0+6):
- Send outbound email from Spaarke → recipient replies → inbound reply auto-associates to original outbound `sprk_communication` via `In-Reply-To` matching (existing `IncomingAssociationResolver.cs` consumes the newly-stamped `Internet-Message-Id`)

---

## 7. Acceptance Criteria

### Technical Acceptance

**Wave 0:**
- [ ] ADR-033 file created and cross-referenced from `CLAUDE.md` §16 + `.claude/constraints/bff-extensions.md`
- [ ] BFF accepts requests with `AttachmentDriveItemIds` OR `AttachmentDocumentIds` (non-breaking)
- [ ] `Internet-Message-Id` stamped on outbound `sprk_communication` records (best-effort)
- [ ] Dataverse columns `sprk_inreplyto` (Lookup) + `sprk_internetmessageid` (Text 255 indexed) present
- [ ] `SendCommunicationError` class exported from `communicationApi.ts`

**Wave 1:**
- [ ] Engine supports all 15 mode × mount combinations (5 modes × 3 mounts)
- [ ] All 3 wrappers implement semantic prop API as specified in FR-02/03/04
- [ ] All canonical validation codes implemented (FR-08)
- [ ] Unit tests cover state machine, validation, separator parsing, attachment cap
- [ ] `npm run build` + `npm run lint` clean in `@spaarke/ui-components`

**Wave 2:**
- [ ] Code Page deploys to dev as `sprk_emailcomposer` web resource
- [ ] All 5 modes functional via URL
- [ ] Ribbon "+ New Email" present on `sprk_communication` views
- [ ] Form Component Control swap functional; standard form retained as admin fallback
- [ ] UI tests pass (5 modes + dark mode + zero console errors)

**Waves 3–5:**
- [ ] Every migrated surface has zero inline `fetch` to `/api/communications/send`
- [ ] LegalWorkspace fork deletes succeed; affected solutions build + deploy
- [ ] All migration smoke tests pass

**Wave 6:**
- [ ] Both `sprk_communication_send.js` files + web resource record deleted
- [ ] All 13 documentation items landed
- [ ] `/doc-drift-audit` passes
- [ ] `/adr-check` against spec returns no violations

### Business Acceptance

- [ ] One client function (`sendCommunication()`) handles every email send across the platform
- [ ] One canonical React component family renders every email-send UX
- [ ] Reply-thread closure functional (round-trip verification)
- [ ] CI passes on master after each wave merge

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|-------------|--------|------------|
| R1 | Form Component Control swap breaks existing customizations | Med | High | UQ1 audit in Wave 2 task 035; NFR-07 fallback form |
| R2 | `Internet-Message-Id` retrieval strategy unreliable | Med | Med | UQ3 spike in Wave 0 task 003; best-effort failure mode |
| R3 | Wave 5 single-PR unreviewable | Med | Med | Owner-decided; structured PR description by wizard |
| R4 | ADR-033 number conflict (parallel project claims 033) | Low | Low | Confirmed free at pre-flight; task 001 re-checks |
| R5 | Ribbon-button audit misses a `sprk_communication_send.js` consumer | Low | Med | Wave 6 task 080 audits all definitions; PR lists every entry point |
| R6 | PCFs bundling `@spaarke/ui-components` need rebuild | High | Low | Track in PR descriptions; wrap-up confirms no stale references |
| R7 | LegalWorkspace fork retirement breaks consumer | Low | High | Empirical scan confirmed only CreateMatter is a fork; task 074 documents delta |
| R8 | `attachmentDriveItemIds` rename breaks `attachmentDocumentIds` callers | Low | High | Non-breaking alias (FR-21); deprecation warning; R4 removes the alias |
| R9 | Task generator misses an FR | Low | Med | TASK-INDEX includes FR→task back-reference; `/adr-check` validates |
| R10 | Active PR #360 (audit-r1-docs-update) collides with R3's Wave 6 doc updates | Med | Med | Coordinate at Wave 6 start; rebase if needed |

---

## 9. Next Steps

1. **Review this PLAN.md** for completeness
2. **`task-create`** decomposes this into ~77 task POMLs (immediately follows in pipeline)
3. **Begin Wave 0 Task 001** (create ADR-033) via `/task-execute`

---

**Status**: Ready for Tasks
**Next Action**: Pipeline Step 3 — task POML generation; then `git commit` + `git push`; then handoff to `task-execute` for Wave 0

---

*For Claude Code: This plan is the canonical wave decomposition. Task POMLs reference these phase descriptions. Update statuses as waves complete.*
