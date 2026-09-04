# Office Add-ins Integration Architecture

> **Last Updated**: 2026-09-04
> **Last Reviewed**: 2026-09-04
> **Reviewed By**: email-communication-intelligence-r2 (Pillar B add-in realignment — as-built refresh; supersedes the Apr-2026 "Dialog API" version, which predated the NAA auth cutover + the inline Create-To-Do + Word `.docx` save)
> **Status**: Current
> **Purpose**: Architecture of the SDAP Office Add-ins for Outlook and Word — how they are built and function today, as the entry point for any project extending them.
> **Code is the source of truth.** This doc is the map; the code under `src/client/office-addins/**` (+ the module pointer `src/client/office-addins/CLAUDE.md`) is the territory.

---

## Overview

The SDAP Office Add-ins integrate **Outlook** and **Word** with the Spaarke platform: save emails / attachments / documents to SharePoint Embedded, file them against a Dataverse record (Matter / Project / Invoice), create first-class **To Dos** from an email, and (Outlook) surface AI triage + linked to-dos. They are **React 18 + Fluent UI v9** task-pane apps hosted on **Azure Static Web Apps**, calling the BFF's `/api/office/*` surface for every backend operation.

Two patterns make the code portable across hosts:
- **`IHostAdapter`** (`shared/adapters/`) abstracts host differences — Outlook reads `Office.context.mailbox`, Word reads `Office.context.document` — so the UI components are host-agnostic.
- **`@spaarke/auth` `OfficeNaaStrategy`** provides authentication (see below) — the add-in never constructs MSAL directly.

> **⚠️ What changed since the Apr-2026 version of this doc:** authentication moved from a bespoke **Dialog API** service to **`@spaarke/auth` + NAA** (`OfficeNaaStrategy`); the task pane became a **tabbed shell** (Save / Share / Create To Do / Search / Recent); **Create To Do** became a first-class inline `sprk_todo` flow; **Word** now saves a real `.docx`; and the manifests/naming/icons were reworked for the M365 Admin Center. The old doc's "Dialog API (not NAA)" and "SavePanel/FolderPicker/StatusDisplay" descriptions are retired.

---

## Authentication (the load-bearing change) — `@spaarke/auth` + NAA

The add-in authenticates through **`@spaarke/auth`**, not a bespoke MSAL service:

- `shared/services/AuthService.ts` is a **thin singleton wrapper** over `SpaarkeAuthProvider` + **`OfficeNaaStrategy`** (`src/client/shared/Spaarke.Auth/src/strategies/OfficeNaaStrategy.ts`). Per ADR-028, the add-in **MUST NOT** `new PublicClientApplication` / `createNestablePublicClientApplication` itself — all MSAL construction lives inside `OfficeNaaStrategy`.
- **Two acquisition paths, auto-selected by host:**
  - **NAA-capable hosts (Outlook/Word desktop + modern web)** → silent **Nested App Authentication** via the MSAL broker. Redirect URI is **portable**: `brk-multihub://${window.location.hostname}` (derived from the serving host, never hardcoded).
  - **Office-on-the-web without NAA** → standard MSAL `PublicClientApplication` popup, redirecting to **`${origin}/auth-callback.html`** (`public/auth-callback.html`).
- **Token cache** is in-memory only (Office task-pane lifecycle is transient), keyed on the JWT `exp` with a 5-minute freshness buffer. Callers use `authService.getAccessToken()` per request.

### Entra registration (per environment — REQUIRED)

Each environment's SWA host needs **both** redirect URIs registered on the Entra app's **SPA** platform:

| Redirect URI | For |
|---|---|
| `brk-multihub://<swa-host>` | NAA broker (desktop + web) |
| `https://<swa-host>/auth-callback.html` | Office-on-the-web popup fallback |

A missing / mismatched `brk-multihub://<host>` → **AADSTS7000471** ("reply address scheme is reserved for brokered application requests"). Because the code derives the host from `window.location`, **no code change is needed per environment** — only the Entra registration. Full operator steps: [`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §7.3 (H3)](../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md).

> **Documented exception to [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md):** the add-ins consume `SpaarkeAuthProvider` + `OfficeNaaStrategy` **directly** rather than the `useAuth()` hook + `localStorage` used by PCFs / Code Pages — the Office host has no cross-tab `localStorage` analog and a transient pane lifecycle. This is now the `@spaarke/auth`-blessed Office strategy (not a bespoke service). Full `useAuth()` migration remains deferred.

---

## Component structure (as-built)

```
src/client/office-addins/
├── outlook/                         # Outlook host entry
│   ├── OutlookHostAdapter.ts        # mailbox item/attachment access
│   ├── outlook-manifest.xml         # XML manifest (M365 Admin Center)
│   ├── manifest.json                # Unified manifest (icons.color/outline)
│   ├── taskpane/index.tsx           # mounts <App> with the Outlook adapter
│   └── commands/                    # ribbon command surface (e.g. ?action=createTodo)
├── word/
│   ├── WordHostAdapter.ts           # document access + getFileAsync(Compressed) → .docx
│   ├── word-manifest.xml
│   └── taskpane/index.tsx           # mounts <App>, save-only (no nav)
├── shared/
│   ├── adapters/                    # IHostAdapter contract + HostAdapterFactory + Outlook/Word impls
│   ├── services/                    # AuthService (NAA wrapper), ApiClient, authenticatedJsonFetch
│   └── taskpane/
│       ├── App.tsx                  # the shell composition (tabs, auth gate, save-context wiring)
│       ├── components/              # TaskPaneShell/Header/Toolbar/Navigation/Footer,
│       │   │                        #   RelatedToPicker, SaveFlow, AttachmentSelector, EntityPicker,
│       │   │                        #   LinkedTodosBanner, ErrorBoundary, LoadingSkeleton, SpaarkeLogo
│       │   └── views/               # SaveView, CreateTodoView, ShareView, StatusView, SignInView
│       ├── hooks/                   # useSaveFlow, useCreateTodoFromEmail, useEntitySearch,
│       │                            #   useLinkedTodosForCommunication, useOfficeTheme/useTheme, useAnnounce
│       └── services/                # communicationSuggestionsService, communicationLookupService,
│                                    #   createTodoLauncher, quickSaveHelpers, SseClient, todoChoices
├── public/auth-callback.html        # web popup redirect target
└── staticwebapp.config.json         # SWA routing/headers
```

### Task-pane tabs (`App.tsx`)

Navigation renders **only for Outlook** (`showNavigation={hostType === 'outlook'}`); **Word is Save-only**.

| Tab | View | Status |
|---|---|---|
| **Save** | `SaveView` → `useSaveFlow` → `POST /api/office/save` | ✅ Live (real save + `RelatedToPicker` filing) |
| **Create To Do** | `CreateTodoView` → `POST /api/office/todo` | ✅ Live (r2 — first-class `sprk_todo`, see below) |
| **Share** | `ShareView` | ⚠️ **Placeholder** — `onSearch`/`onGenerateLink`/`onInsertLink` are stubs in `App.tsx` |
| **Search** | `StatusView` | ⚠️ **Placeholder** — `onFetchJobs` returns `[]` |
| **Recent** | `StatusView` | ⚠️ **Placeholder** — `onFetchJobs` returns `[]` |

> A new project extending the add-ins should know Share / Search / Recent are **scaffolded but not wired** — do not assume they call the BFF.

---

## Headline features shipped by email-communication-intelligence-r2

### Create To Do (inline, first-class `sprk_todo`)
- The **Create To Do** tab creates a **first-class `sprk_todo`** — **not** an `sprk_event` of type "to do", and **not** the SmartTodo popup wizard. (Spaarke no longer uses the `sprk_event` "to do" type.)
- `POST /api/office/todo` with `{ name, description, assignedToContactId, dueDate, priorityScore, effortScore, regardingEntityType, regardingRecordId, regardingRecordName }` → `OfficeService.CreateTodoAsync` (app-only create via `IGenericEntityService`, `ownerid` = caller; regarding wired via `sprk_regardingmatter/project/invoice`).
- **Regarding = the record the email was filed to.** `SaveView.onSaved(selectedEntity)` seeds `App.savedContext`, which the Create To Do form reads — so the To Do is created "related to" the filed record.
- Assignee is a **Contact** typeahead via `GET /api/office/search/entities?type=Contact`. Priority/Effort choice→score mapping is mirrored add-in-side in `todoChoices.ts` (sanctioned duplicate of the wizard's score tables).

### Word real `.docx` save
- `WordHostAdapter.getFileAsync(Office.FileType.Compressed)` returns the actual OOXML bytes; the save uses a real `.docx` extension (previously a text approximation).

### Save flow + "Related to" filing
- `SaveFlow` / `RelatedToPicker` present auto-matched Matter/Project/Invoice candidates (with confidence) + inline "create new record" (`POST /api/office/quickcreate/{type}`) + green-check select; the chosen record becomes the `sprk_document` regarding.

### Outlook linked-to-dos banner
- `LinkedTodosBanner` (Outlook only) queries `GET /api/office/communications/{commId}/linked-todos` and pins a "N linked to-dos" indicator when the email's saved communication has them.

---

## BFF surface (`/api/office/*`)

Endpoints live in `src/server/api/Sprk.Bff.Api/Api/Office/*.cs`; logic in `Services/Office/OfficeService.cs`. As-built routes:

| Method + route | Purpose |
|---|---|
| `POST /api/office/save` (+ `/save-debug`) | Save the current email/document → queues async processing, returns a job id |
| `GET /api/office/{jobId}` · `GET /api/office/{jobId}/stream` | Job status (poll + **SSE** progress) |
| `GET /api/office/search/entities` | Entity search (Matter/Project/Invoice/Contact) — powers RelatedToPicker + Contact assignee |
| `GET /api/office/documents` · `GET /api/office/recent` | Document/recent lookups |
| `POST /api/office/quickcreate/{entityType}` | Inline "create new record" for filing |
| `POST /api/office/todo` | **Create first-class `sprk_todo`** (r2) |
| `POST /api/office/links` · `POST /api/office/attach` | Sharing links / attach flows |
| `GET /api/office/communications/by-message-id/{id}` (+ `/suggestions`) | Resolve the saved communication + AI association suggestions |
| `GET /api/office/communications/{commId}/linked-todos` | Linked `sprk_todo` records for the banner |
| `GET /api/office/health` | Add-in-facing health probe |

Auth: the caller's bearer token → BFF **OBO** → Graph/SPE + Dataverse (ADR-028; the BFF is secret-free). The async save pipeline (upload finalization → AI profiling → RAG indexing) is **shared with the communication-intelligence capture pipeline** — see [`communication-intelligence-architecture.md`](communication-intelligence-architecture.md) and the job handlers under `Services/**` (canonical source; do not re-document queue/worker names here — read the code).

---

## Manifests, naming, icons

| Item | As-built |
|---|---|
| **XML manifests** | `outlook/outlook-manifest.xml`, `word/word-manifest.xml` — for M365 Admin Center upload |
| **Unified manifest** | `outlook/manifest.json` — carries `icons.color` (128px) + `icons.outline` (32px) |
| **Names** | "Spaarke Outlook" / "Spaarke Word" |
| **Icons** | white-on-black brand marks generated into `shared/assets` (`icon-color.png` 128, `icon-outline.png` 32, plus `icon-16/32/64/80/128`) via `generate-icons.mjs` from `spaarke-logo.svg` (`sharp` is a manual dev dep, not in `package.json`) |

### Manifest rules (validated against M365 Admin Center — still binding)

| Rule | Reason |
|---|---|
| 4-part version `X.X.X.X` (not `X.X.X`) | Admin Center rejects 3-part |
| **No** `<FunctionFile>` in the Outlook manifest | Causes validation failure |
| Single `VersionOverridesV1_0` (do not nest V1.1) | Validation failure |
| `RuleCollection Mode="Or"` + `DisableEntityHighlighting` present | Required for Outlook read surface |
| All icon URLs return HTTP 200 | Manifest validation fails otherwise |
| Bump the manifest version on any change | M365 requires re-register at the new version |

---

## Build & deploy

```bash
cd src/client/office-addins
npm install --legacy-peer-deps --no-audit --no-fund
npm run build:dev          # (or build:prod)
npm run typecheck          # NOTE: ~397 PRE-EXISTING exactOptional errors — filter to changed files
```

- **Hosting**: Azure **Static Web App**. **Deploy runs in CI** — GitHub Actions **`deploy-office-addins.yml`** (holds the SWA secrets); it is **not** an agent-run script. Push the branch → confirm the run is green (`gh run list --workflow=deploy-office-addins.yml`).
- **Version bumps**: `outlook/manifest.json` + `outlook/taskpane/index.tsx` (and the Word equivalents), then **M365 re-register** at the new version.
- **Config (env-driven, not hardcoded)**: `BFF_API_BASE_URL` (defaults to `spaarke-bff-dev`), `ORG_URL` (Quick-Create Dataverse deep-link; unset → Quick Create is a safe no-op). Never pin the add-in to the dev org.

---

## Constraints (MUST / MUST NOT)

- **MUST** authenticate via `@spaarke/auth` `OfficeNaaStrategy`; **MUST NOT** construct MSAL directly in the add-in (ADR-028).
- **MUST** register **both** `brk-multihub://<host>` and `https://<host>/auth-callback.html` as **SPA** redirect URIs per environment.
- **MUST** create To Dos as first-class **`sprk_todo`** (never `sprk_event` type "to do").
- **MUST** keep host/org values **config-driven** (`BFF_API_BASE_URL`, `ORG_URL`) — no hardcoded org URLs.
- **MUST** use Fluent UI v9 + Office theme dark-mode (ADR-021); keep UI host-agnostic behind `IHostAdapter`.
- **MUST** use XML manifests for M365 Admin Center + the 4-part version + the Outlook manifest rules above.
- **MUST** treat AI profiling / search indexing as non-fatal enhancements (core save must still succeed).

---

## Known pitfalls

| Pitfall | Symptom | Resolution |
|---|---|---|
| `brk-multihub://<host>` not registered (SPA) | `AADSTS7000471` on web sign-in | Register `brk-multihub://<swa-host>` under the Entra app's **SPA** platform for that host |
| `auth-callback.html` redirect not registered | Web popup never completes | Register `https://<swa-host>/auth-callback.html` (SPA) |
| Assumed Share/Search/Recent tabs work | Empty results / no-op | They are **placeholders** in `App.tsx` — wire them to the BFF before relying on them |
| Manifest version not bumped | Old add-in served after deploy | Bump the 4-part version + M365 re-register |
| `FunctionFile` / nested VersionOverrides / 3-part version | M365 Admin Center rejects manifest | See the manifest-rules table |
| Icon URL 404 | Manifest validation fails | Verify all icon sizes return HTTP 200; regenerate via `generate-icons.mjs` (+ `npm i --no-save sharp`) |
| 401 from BFF | Add-in token rejected | Ensure the add-in client id is an authorized client on the BFF API app registration |
| Quick Create opens a broken window | `ORG_URL` unset | By design it no-ops when unset — set `ORG_URL` to enable the Dataverse deep-link |

---

## Related

- **Module pointer**: [`src/client/office-addins/CLAUDE.md`](../../src/client/office-addins/CLAUDE.md) — the per-file "where to start reading" map
- [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — auth architecture (+ the Office NAA exception)
- [ADR-021](../../.claude/adr/ADR-021-fluent-design-system.md) — Fluent UI v9 / dark mode
- [`communication-intelligence-architecture.md`](communication-intelligence-architecture.md) — the email capture + association + triage substrate the Save flow feeds
- [`content-identity-and-deduplication-architecture.md`](content-identity-and-deduplication-architecture.md) — the dedup layers the Save / save-back path rides (item identity, content hash, message id; graduate-on-divergence for editable docs)
- [`spaarke-todo-architecture.md`](spaarke-todo-architecture.md) — the `sprk_todo` entity the Create To Do flow writes
- [`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) §7.3 — per-env Entra redirect-URI registration
- [`docs/guides/office-addins-admin-guide.md`](../guides/office-addins-admin-guide.md) · [`docs/guides/office-addins-deployment-checklist.md`](../guides/office-addins-deployment-checklist.md) — operator guides (verify against this doc; may lag)

---

*Last Updated: 2026-09-04 — as-built after email-communication-intelligence-r2 Pillar B.*
