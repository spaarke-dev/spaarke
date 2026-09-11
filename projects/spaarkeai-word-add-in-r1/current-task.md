# Current Task State — spaarkeai-word-add-in-r1

> **Last Updated**: 2026-09-10 (task-execute 012 started)
> **Recovery**: Read "Quick Recovery" first. Branch `work/spaarkeai-word-add-in-r1`, PR #960.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **012** — FR-01 server: document-identity resolver extending `/api/documents` (`tasks/012-document-identity-resolver-bff.poml`) |
| **Rigor** | FULL · opus @ high · steps DIRECTIONAL |
| **Step** | 012 committed (`8fec97b2d`), pushed, **deployed to dev** (hash-verified, healthy), and **live-verified**: Spike-1 links 2+3 GREEN (notes/012 §8, spike-1 §22). The live run showed Graph answers 403 for missing items, so 403 → `not_resolvable` (uncommitted; 61/61 tests pass). **Next:** commit + push that change → redeploy → re-run item 3 (expect 200 `not_resolvable`). **Open (operator):** Word-desktop `document.url` capture; optionally a non-Spaarke OneDrive/SharePoint file URL. 015 ✅ and live on the SWA (run 34546485352). PR #960 body rewritten. |
| **Status** | in-progress |
| **In flight elsewhere** | Task **015** (tab shell) running in a background agent in an ISOLATED worktree — it must NOT touch TASK-INDEX/current-task; main session merges its branch when it reports. |
| **Next Action** | `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~SharingUrlToken|FullyQualifiedName~DocumentUrlIdentity"` → fix → full affected test run → publish-size vs fresh master → code-review + adr-check |

### Task 012 — decisions so far
- 2026-09-10 — **§7 does NOT make 012 optional.** Of 16 SPE upload sites in the BFF, task 014 stamps only the Office save path; a document uploaded via `DocumentsEndpoints` (`PUT /api/drives/{driveId}/upload`), email, AI working-doc etc. arrives UNSTAMPED, so the URL path is the only way the pane identifies it on first open. Stamp precedence is already stamp-first in 014 step 5. Corrects spike-1 §7's "consider dropping it from r1".
- 2026-09-10 — /conflict-check: no open PR touches the target files; master has no new commits to them; branch 46 ahead / 0 behind.

### Files modified (task 012) — all uncommitted
- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SharingUrlToken.cs` — NEW: `u!` token + the two encoding candidates
- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/DriveItemOperations.cs` — `ResolveSharedItemAsUserAsync` (Graph `/shares`, OBO)
- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeFileStore.cs` — virtual facade method
- `src/server/api/Sprk.Bff.Api/Models/SpeFileStoreDtos.cs` — `SpeSharedItemOutcome/Attempt/Resolution`
- `src/server/api/Sprk.Bff.Api/Models/FileOperationModels.cs` — request/response DTOs
- `src/server/api/Sprk.Bff.Api/Services/Documents/DocumentUrlIdentityResolution.cs` — NEW: resolver (3 answers, self-heal, drive check)
- `src/server/api/Sprk.Bff.Api/Api/Filters/DocumentUrlIdentityFilter.cs` — NEW: resolution filter → route value `documentId`
- `src/server/api/Sprk.Bff.Api/Api/FileAccessEndpoints.cs` — route `POST /resolve-identity` + handler
- `src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/RecordContainerResolver.cs` — `IsRecordNotFound` private→internal (reuse)
- `tests/unit/.../Infrastructure/Graph/SharingUrlTokenTests.cs`, `.../Services/Documents/DocumentUrlIdentityResolutionTests.cs`, `.../Filters/DocumentUrlIdentityFilterTests.cs` — NEW
- `projects/.../notes/012-identity-resolver-decisions.md` — NEW (contract, reuse-vs-copy, auth design, encoding, placement)

### Key design (details in notes/012-identity-resolver-decisions.md)
- Two filters in order: `DocumentUrlIdentityFilter` (resolve → `RouteValues["documentId"]`) then the UNCHANGED `DocumentAuthorizationFilter("read")`.
- Graph `/shares` runs OBO → unreachable URL answers exactly like non-document (no existence oracle).
- Three answers: resolved / 200 no-identity / **503** — a Dataverse or Graph fault is never "not a Spaarke document" (that would mint duplicates).
- Live SPIKE-1 criteria need this branch's BFF on dev (OBO needs MI credential) → **operator sign-off for a dev BFF deploy**.

### Completed after the handoff — doc accuracy pass (committed with this update)

The background doc-accuracy agent finished and its edits were verified in the main session before commit:
8 files, no workflow YAML touched, the one C# change comment-only. It corrected the admin guide + deployment
checklist (non-existent `manifest-working.xml`, `build:prod`, the `localhost` trap), `uac-access-control.md`
(the stale app-only claim), `.github/WORKFLOWS.md` + the incident runbook (only `Router` is required; three
undocumented workflows added), `src/client/office-addins/CLAUDE.md` and the architecture doc (React 19, build
command, typecheck count), and the `ChatWordExportEndpoints.cs` URL-shape comment.

**It caught two errors in the main session's own brief — keep these:**
- **Outlook's production XML is `/outlook/outlook-manifest.xml`, NOT `/outlook/manifest.xml`** (404 vs 200,
  verified). Only Word's XML output is named `manifest.xml`. Word URL: `/word/manifest.xml` (200, serves 1.0.8.0).
- The Word adapter consolidation (task 010) was already documented correctly; the brief over-claimed that.
- Typecheck: 289 test-file errors at the 2026-09-09 accept decision; 284 on re-measure 2026-09-10; 0 production.

### Operator pending (none can be done by an agent)

1. **Word DESKTOP capture of `Office.context.document.url`** — the 2026-09-10 capture was Word on the WEB.
   Steps: open doc via Spaarke "Open in Desktop" → focus the pane → Ctrl+Shift+I (or right-click → Inspect, or
   pane menu → Attach Debugger) → Console → `Office.context.document.url`. Fallback if DevTools won't open for an
   admin-deployed add-in: user env var `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--auto-open-devtools-for-tabs`,
   restart Word (affects all WebView2 apps — remove after). Note Protected View state before/after Enable Editing.
2. **Re-upload the Word manifest at 1.0.8.0** — operator uploaded from the SWA while it still served 1.0.7.0.
   SWA now serves 1.0.8.0 (verified). Path: M365 Admin Center → Settings → Integrated apps →
   `https://icy-desert-0bfdbb61e.6.azurestaticapps.net/word/manifest.xml`. Then confirm pane footer = 1.0.8 →
   **task 011 can close** (via the XML path).
3. **Share privilege check** — security roles → `sprk_document` → Share. If not granted, the record⇔document
   access model has no gap (see D-032-1). OR reconnect Dataverse MCP (`/mcp`) and an agent can check.
4. **Dataverse MCP is DOWN this whole session** (`CONNECT_TIMEOUT` then `CONNECTION_CLOSED`). Nothing "verified
   via MCP" came from this session. `/mcp` or restart to reconnect.

### Pending decisions (operator)

- **Spike-1 §7 — stamp-as-primary.** Recommended: FR-02 custom-XML stamp as PRIMARY identity, `document.url` as
  fast path (URL identity breaks on rename/move; the stamp does not). Operator said legacy data doesn't matter
  (dev only), which removes the forward-only objection — but has not explicitly accepted §7. If accepted, 014
  runs before 012/013.
- **Shadow-window latch mechanism** (ISS-002 residual) — `-Since` was advanced (Option 1, applied); the latch
  itself (`$falseGreens` unfiltered by `$countingFrom`) still exists and the NEXT false green will latch the same
  way. Needs a cutover-owner decision: hard stop or reset.

---

## Critical Context (the 5 things a fresh session must not re-learn the hard way)

1. **Docs and agent reports in this area have repeatedly been WRONG vs code.** Verify against code/live before
   relaying. Examples this session: ArchTests "broken" (false — concurrent-build contention), UAC doc "app-only"
   (stale 1 day; code is fail-closed caller-scoped), office-addins gate "PR-blocking" (false — only `Router` is a
   required check), `manifest-working.xml` (doesn't exist), `ChatWordExport` URL-shape comment (wrong).
2. **The access model is ENFORCED in code** (D-032-1 WITHDRAWN, final): record access ⇔ document access.
   Internal: `AuthorizationService` fails closed, queries Dataverse AS THE USER (`:54`, `:79`, `:224-225`).
   External: grants at ROOT record only; SPE broker-only (BFF app-only download, `ExternalProjectDataEndpoints`
   `DownloadDocumentContent`); `GrantMembershipAsync` has no callers. No code shares an individual row. Access is
   BU-assigned, never org-wide (operator). Cascade (Referential) was the WRONG question — it governs
   share/assign/delete propagation, which the model never uses.
3. **Link 2 cannot be tested outside the BFF** — only the owning app or a container-type-REGISTERED app reads SPE
   files. `az` CLI / Graph Explorer would 403 regardless of URL validity (false negative).
4. **Space encoding is the live link-2 question**: `document.url` returns RAW spaces; the BFF's own open-links
   returns `%20`. Same URL after decoding (shape check MATCH, spike-1 §21). Task 012 must test both forms.
5. **Many agents in ONE worktree** caused lost writes to shared files (TASK-INDEX, current-task) and phantom
   findings from build contention. Prefer separate worktrees for waves of build-heavy agents.

---

## Session summary — 2026-09-08 → 2026-09-10 (all committed + pushed to PR #960)

**Phase 0 COMPLETE.** Tasks ✅: 001, 002, 003, 004, 005, 006, 007, 008, 009, 010, 017, 018, 019, 028, 032, 043, 044.
011 🔄 (awaiting 1.0.8 re-upload).

| Area | Outcome | Key commits |
|---|---|---|
| History repair | Squashed Copilot commit split into 3 honest commits; out-of-scope collision fix REVERTED (violated FR-12) | `df1a3805b`, `0b68943d1` |
| FR-12 / collision | Premise FALSE (add-in uses a different upload path). Path C→B: build FR-11 first, then amend FR-12 | `c72455e5a`, `e7c79b698`, `436507b32` |
| F-h / 028 | Editable Office saves link/graduate, never immutable-suppress; host-neutral on `SaveContentType`; mutation-tested | `dd286200f` |
| F-b / 032 | Per-row authorization on Find; closed a `countOnly` count side-channel; negative test | `f892c8ada` |
| FR-18 | Production typecheck 88 → **0** (A1+B1); 289→284 test-file errors CONSCIOUSLY ACCEPTED (inert: no CI/build/test gate) | `cc318390f` |
| Jest harness | jest-dom + user-event were NEVER installed; RTL 14→16 (+ `@testing-library/dom` peer) | `cc318390f`, `abe10e431` |
| 018 | `useAnnounce` (NFR-11) out-of-tree DOM → React-owned; 19/19 | `fe9684ab0` |
| 010 | Single Word adapter via `HostAdapterFactory`; `getCompressedFile` byte-identical (36-case differential incl. multi-slice); Option B = pass Stage-1 host | `eb92c604d`, `36bb6dc3d` |
| 019 | Custom-XML parts premise CONFIRMED; `CustomXmlParts 1.1` declared, WordApi NOT bumped (would drop Office 2019/2021 LTSC). 014 GO with 4 conditions (explicit `xmlns` on stamp root!) | `1f47fde3e` |
| 011 | Word unified manifest + WordApi 1.1→1.3 fix. XML is the M365 Admin Center artifact (binding rule; JSON is `devPreview`) | `336ea3809`, `12c1c3b74` |
| Spike-1 | Link 1 GREEN (web), link 4 GREEN, shape check MATCH; links 2-3 moved into 012 | `223a3a148`, `8babf3f21`, `d7a71baaf`, `7d7beb9ae` |
| 043 | office-addins jest CI check (REPORTS, not blocking — only `Router` is required); vacuous-green count assertion; CODEOWNERS on allow-list | `c2f924ef8`, `02e17260e`, `9c7a29a09` |
| 044 / ISS-002 | Shadow false green = real ROUTER DEFECT, already fixed by PR #944; `-Since` advanced → window clean (0 false greens) | `db6dbe6ab`, `c525f3276` |
| Deploys | Office add-ins redeployed from this branch — SWA now serves **1.0.8.0** | runs `34414699298`, `34532444044` |

### Open findings surfaced, NOT yet actioned

- **F-1** `deploy-office-addins.yml` path filter misses `Spaarke.Communication.Components/.../provenance.ts`, which
  webpack bundles into the add-ins — editing it ships nothing. One-line fix.
- **W-5** `identity-obj-proxy` mapped in `jest.config.js` but not installed — now affects a PR check.
- **W-2 (043)** manifest-line deletion from `ci-gated-suites.txt` — mitigated by CODEOWNERS, not mechanically.
- **W-7** CLAUDE.md §12 `npm ci` ban may not hold for office-addins (`npm ci --dry-run` exits 0) — §6.5 question.
- 10 failing jest suites (84 tests) — genuine mock/assertion defects, unowned by a task.
- F-6 `unified-access-control-r2` has a branch diff on the frozen `ci-tier1-blocking.yml` — operator's call.

---

## Decisions made (operator, this session)

- 2026-09-08 — FR-12 path **C then B**; **F-h owned by r1**, fix host-neutral.
- 2026-09-09 — Deferral policy: defer only with a good technical reason OR an ACTIVE hand-off (a note the other
  project is instructed to read — a GitHub issue alone is NOT a hand-off). D-032-2 folded into task 033.
- 2026-09-09 — FR-18 = production types only (A1+B1); 289 test-file errors consciously accepted.
- 2026-09-09 — React 19 stays; RTL aligns to it. ci-cd-unit-test-remediation-r1 is CLOSED → r1 owns CI work.
- 2026-09-09 — r1 owns ISS-002 (044) with authorization to touch frozen tier files (not exercised).
- 2026-09-10 — Legacy/existing documents don't matter (dev only). Shadow window Option 1 applied.
- 2026-09-10 — Access model confirmed as enforced; D-032-1 withdrawn (final).
- 2026-09-10 — Operator authorized agent-triggered deploys of `deploy-office-addins.yml`.
