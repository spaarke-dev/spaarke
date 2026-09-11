# Add-in Context Handoff — from `email-communication-intelligence-r2`

> **Created**: 2026-09-04 · by `email-communication-intelligence-r2` (Pillar B did the most recent, substantial round of Outlook/Word add-in work).
> **For**: `spaarkeai-word-native-r1`. Complements — does **not** replace — this project's `design.md` §4 ("Prior art in our codebase").
> **One-line**: r2 reworked the add-ins' auth (NAA, incl. web), added a first-class inline **Create To Do**, gave **Word** a real `.docx` save, and reworked manifests/naming/icons — then **refreshed the canonical add-in docs to match**. Read those two docs first; this note tells you what changed and reconciles it against your `design.md`.

---

## 0. Read these first (the canonical map — new since your design.md)

Your `design.md` (dated 2026-09-01) predates two documents r2 created on **2026-09-04**. They are the authoritative, as-built map now — start here rather than re-deriving from code:

| Doc | What it gives you |
|---|---|
| **`docs/architecture/office-outlook-teams-integration-architecture.md`** | Full as-built architecture: NAA auth model, the tabbed task-pane shell, the complete `/api/office/*` route table, manifest rules, deploy, constraints + known pitfalls. **Rewritten from scratch** — the prior version described the retired Dialog-API auth and is gone. |
| **`src/client/office-addins/CLAUDE.md`** | The module pointer an agent hits the moment it opens add-in code: entry-point map + **six load-bearing facts** + build/typecheck/deploy. Did not exist before. |

Both are on `master` (PR #942, merged 2026-09-04).

---

## 1. What r2 actually changed in the add-ins

All merged to master. Two PRs carry the code:

- **PR #934** (`f5fee2141`) — *"Outlook/Word add-in — Create To Do (sprk_todo), Word .docx save, web auth, icons"* — the headline add-in work.
- **PR #942** (`4485013a8`) — the doc refresh above.
- (PR #936 fixed email **triage** category resolution — server-side, not add-in — but it's what makes the Outlook triage/association cards populate.)

### Auth — NAA, now including Office-on-the-web (relevant to your §4.2)
Your `design.md` §4.2 says "auth — already solved / no auth work needed." **Mostly true, with one important correction r2 made:** the NAA broker redirect URI was **hardcoded** `brk-multihub://localhost`, which only exists in the dev app's *native* client list — so **desktop worked but Office-on-the-web failed** (AADSTS7000471). r2 made it **portable**: `brk-multihub://${window.location.hostname}`, derived from the serving host (`OfficeNaaStrategy.ts`). Consequence for you:
- Auth is solved for **desktop and web**, via `@spaarke/auth` `OfficeNaaStrategy` — no code per environment.
- **But each environment MUST register two SPA redirect URIs** on the Entra app: `brk-multihub://<swa-host>` **and** `https://<swa-host>/auth-callback.html`. This is now documented in `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md §7.3` and the refreshed arch doc. Fold this into your Phase-0/1 provisioning.

### Create To Do — a working example of a BFF-backed record operation from the pane
The Outlook pane now creates a **first-class `sprk_todo`** inline (not `sprk_event`, not the SmartTodo popup): `POST /api/office/todo` → `OfficeService.CreateTodoAsync` (app-only create, `ownerid`=caller, regarding wired to the filed record). This is a concrete, shipped reference for your **D-15 thin capability pane** — same shape you'll want (pane form → `/api/office/*` or `/api/ai/*` → Dataverse under the user). See `App.tsx` `handleCreateTodo` + `components/views/CreateTodoView.tsx`.

### Word real `.docx` save (touches your D-11/D-14 area)
`WordHostAdapter.getFileAsync(Office.FileType.Compressed)` now returns real OOXML bytes + a `.docx` extension (was a text approximation). **Note for your D-11**: r2 added +81 lines to `word/WordHostAdapter.ts` — which is exactly the *untested adapter instantiated directly at `word/taskpane/index.tsx`, bypassing `HostAdapterFactory`/the tested `shared/adapters/WordAdapter.ts`* that your §4.1 flags. So **the "two redundant adapters, the untested one is used" problem is still live, and r2 grew the untested one.** Your D-11 (consolidate onto `WordAdapter` via `HostAdapterFactory`) remains correct and now has a little more surface to reconcile.

### BFF Office surface grew (relevant to your D-14)
`OfficeService.cs` (+222) / `OfficeEndpoints.cs` (+115) gained `CreateTodoAsync`, `QuickCreateAsync`, entity search, suggestions, linked-todos. When you extend `/api/office/save` with document identity (D-14), read `OfficeService.cs` **as it is now** — it's materially bigger than when your design snapshot was taken. Contract tests are in `tests/integration/contract/Api/Office/OfficeEndpointsContractTests.cs`.

### Manifests / naming / icons
- **Current versions on master** (your `design.md` §4.1 cites Word `1.0.4.0` / Outlook `1.0.20` — now stale): **Word XML `1.0.6.0`**, **Outlook XML `1.0.22.0`**, **Outlook unified `manifest.json` `1.0.22`**.
- **Outlook already has a unified `manifest.json`** (`outlook/manifest.json`, with `icons.color`/`icons.outline`) alongside its XML manifest — a working precedent for your **D-12** (Word → unified JSON). Word currently has **only** `word-manifest.xml`.
- Names: "Spaarke Outlook" / "Spaarke Word". White-on-black brand icons generated via `generate-icons.mjs` from `spaarke-logo.svg` (`sharp` is a manual `--no-save` dev dep).

---

## 2. Reconciliation with your `design.md` §4 (trust-but-verify)

| Your design.md claim | Status on master (2026-09-04) |
|---|---|
| §4.1 Word manifest `v1.0.4.0` vs Outlook JSON `v1.0.20` | **Updated** → Word XML `1.0.6.0`, Outlook JSON `1.0.22` (r2 bumped both) |
| §4.1 `quickSave`/`shareDocument` are stubs (`word/commands/index.ts`) | **Still true** — r2 did not wire the Word ribbon commands |
| §4.1 Two Word adapters; the untested `WordHostAdapter` is the one used | **Still true, and grown** — r2 added `.docx` save to `WordHostAdapter.ts` (the bypassed one). D-11 consolidation still needed. |
| §4.1 `getItemId()` is a synthetic hash → no document identity (G2) | **Still true** — r2 did email/save *filing*, not Word *document identity*. Your keystone gap G2/D-13 is untouched — still first-task material. |
| §4.2 Auth solved via `OfficeNaaStrategy` (NAA) | **True + hardened** — r2 fixed the web path (portable `brk-multihub://<host>`); requires per-env SPA redirect registration (see §1). |
| §3.2 `/api/office/save` ships but "doesn't know which document" | **Still true** — save/filing works; document identity is still the gap. r2 grew the surrounding Office surface but not identity. |
| §4.4 `office` is a declared `sprk_surfaces` token with zero live rows | Unchanged by r2 (r2 used `/api/office/*`, not `surface=office` Bindings). Your D-15 extension point is still clean. |

**Net**: your §4 archaeology holds. The two things worth patching in your own doc are the **manifest versions** and the **auth "no work needed" line** (it's solved, but per-env SPA redirect registration is a real provisioning step).

---

## 3. Reusable patterns from the (more mature) Outlook add-in

The Outlook add-in is the richer sibling; several of its shipped patterns are directly reusable for your Word-native pane:

- **NAA auth** (`@spaarke/auth` `OfficeNaaStrategy`) — **use as-is** for the Word pane; your §4.2 already assumes this. No MSAL construction in the add-in (ADR-028 arch-test-enforced).
- **`IHostAdapter` + `HostAdapterFactory`** — the host-agnostic seam. Your D-11 wants Word consolidated onto this; the Outlook side already routes through it cleanly.
- **BFF-backed record op from the pane** — Create To Do (`POST /api/office/todo`) and RelatedToPicker filing (`GET /api/office/search/entities`, `POST /api/office/quickcreate/{type}`) are working templates for D-15 dispatch-from-pane.
- **Save-context wiring** — `SaveView.onSaved` → `App.savedContext` shows how a filed record threads into a follow-on action; analogous to how your Word pane will carry document/matter identity (D-13) into capability dispatch.
- **SSE job progress** — `GET /api/office/{jobId}/stream` + `services/SseClient.ts` is the shipped pattern for long-running save/AI feedback in the pane.

⚠️ **Don't reuse**: Share / Search / Recent tabs in `App.tsx` are **placeholders** (stub handlers). And the **Xrm-bound wizard components** (`CreateTodoWizard` etc.) won't run in an add-in (no Xrm) — the Outlook add-in *recreated* those layouts rather than importing them; do the same.

---

## 4. Pointers (everything in one place)

| Topic | Location |
|---|---|
| **As-built architecture** | `docs/architecture/office-outlook-teams-integration-architecture.md` |
| **Module entry-point map** | `src/client/office-addins/CLAUDE.md` |
| Shell composition (tabs, auth gate, save→todo wiring) | `src/client/office-addins/shared/taskpane/App.tsx` |
| Auth strategy | `src/client/shared/Spaarke.Auth/src/strategies/OfficeNaaStrategy.ts` (wrapped by `shared/services/AuthService.ts`) |
| Word host adapter (the used one) / tested one | `word/WordHostAdapter.ts` · `shared/adapters/WordAdapter.ts` (+ `HostAdapterFactory.ts`) |
| BFF Office surface | `src/server/api/Sprk.Bff.Api/Api/Office/OfficeEndpoints.cs` + `Services/Office/OfficeService.cs` |
| Office contract tests | `tests/integration/contract/Api/Office/OfficeEndpointsContractTests.cs` |
| Deploy (CI, not agent-run) | `.github/workflows/deploy-office-addins.yml` |
| Per-env Entra SPA redirects | `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §7.3 (H3) |
| To Do entity the Create-To-Do flow writes | `docs/architecture/spaarke-todo-architecture.md` |
| Email capture/association/triage substrate (Save feeds it) | `docs/architecture/communication-intelligence-architecture.md` |
| r2's own project record | `projects/email-communication-intelligence-r2/` (current-task.md, notes/) |

---

## 5. One caution carried from r2

`npm run typecheck` in `src/client/office-addins` reports **~397 pre-existing `exactOptionalPropertyTypes` errors** unrelated to any single change — filter to the files you touched. The build (`npm run build:dev`) is clean; the typecheck noise is a known, separate cleanup. Don't let it read as "my change broke the build."
