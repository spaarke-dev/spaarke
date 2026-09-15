# Current Task State — spaarkeai-word-add-in-r1

> **Last Updated**: 2026-09-15 (027/034/035 merged + review fix; 029 and 036 in flight; gates running; local HEAD NOT pushed)
> **Recovery**: Read "Quick Recovery" first. Branch `work/spaarkeai-word-add-in-r1`, PR #960.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **State (2026-09-15, afternoon)** | `origin/work/spaarkeai-word-add-in-r1` = `ed776f915`. **Local HEAD is ahead and NOT pushed** — it adds 046(b) (`eda1e2f75`), 027 (`546a54e5b`), 035 (`41b5223c6`), 034 (`c15bdfafb`), the review fix `6d950bdba` (027's Open buttons now also need `ORG_URL`, and `deploy-office-addins.yml` sets the dev org URL; 034's rows are a real list) and docs. 34 of 47 tasks ✅; every ✅ task's POML now says `completed`. The four execution-time ADR exceptions (ADR-044, ADR-038 §2, ADR-051 Find-only, ADR-024 pane-To-Dos-only) are in `spec.md` ADR Tensions. The "11 failing jest suites" scare was a load flake: a clean full run gave the baseline 10 / 84 with the same ten names. |
| **Gates on the merged tree** | Typecheck 111 / production 0; the five 027/034 suites 63/63. BFF build 0/0; targeted tests (Office/Document/Dedup/Idempot/MatterType/Rag/Visualization/Todo/Email) **2,626 / 0 failed / 25 skipped**; ArchTests 191/191. Add-in build exit 0; **29/29 gated suites, 394 tests**; full jest = the ten known failing suites plus, once, `SaveFlow.matterTypeQuickCreate` under heavy load (3/3 alone) — its waits now allow 5 s (test-only hardening). |
| **In flight (agents, isolated worktrees)** | **029** (opus) — per-version analysis/index key after a version save; BFF-only; worktree `agent-adbd44993f6c323e9` on `0a8730fd2`. **036** (sonnet) — Send Email from the pane; client-only; worktree `agent-aa138408f12a07cd2`; told to reuse 027's `buildOpenRecordUrl` (no record link without `ORG_URL`), put the capability in `adapters/types.ts`, and — if Word cannot open a compose window — ship Outlook, hide it in Word, and write the Word options for the owner instead of stopping. Neither agent edits TASK-INDEX / current-task / CLAUDE.md / `ci-gated-suites.txt`. |
| **Waiting on the owner** | (1) **How Word documents are matched to avoid duplicates** — 014/FR-02 (an invisible id inside the file) vs a user-confirmed match through the 025 clash prompt; decides 014, 025, 045 and 020. (2) Dev BFF deploy sign-off, and an add-in redeploy (027/034 client changes, 046(b)'s name flag, the new `ORG_URL`). (3) Owners for the unowned repo-wide items: #975; the idempotency filter's no-header path is a no-op; the flaky AI-chat Tier-2 test (verified not ours). (4) 031: does Project numbering belong to the separate numbering project? (5) Word manifest 1.0.8.0 re-upload (closes 011). (6) Possibly from 036: whether Send Email is hidden in Word for r1. |
| **Next Action** | **1)** Gates green and pushed (see State). Publish the PR draft (`scratchpad/pr-960-body.md` — already has the 027/034/035 rows, 035's placement statement, an ADR-exceptions table and the new owner decisions; add a Testing row with these gate numbers and mention the `ORG_URL` fix), and watch CI (`gh pr checks 960`; Tier 2 is advisory). If the draft is gone, rebuild from `gh pr view 960 --json body`. **2)** When 029 / 036 report: review the diffs yourself (not just the report), merge with `--no-ff`, run the same gates, add new suites to `ci-gated-suites.txt`, mark ✅ in TASK-INDEX + POML, push. **3)** Held: 020 (014's fate), 025 and 045 (the matching discussion), 031 (numbering answer), 037 (manifest churn). Remaining 🔲 after 029/036: check TASK-INDEX. **4)** Worktree cleanup of 046/027/035/034 was started; confirm with `git worktree list`. NEVER touch `agent-a97210f38cd331cfb` (another session's). |
| **Dispatch rules (learned the hard way)** | Before any Agent dispatch, run a `Set-Location 'C:\code_files\spaarke-wt-spaarkeai-word-add-in-r1'` command and confirm the environment says "is a git worktree" — the lowercase `c:` path once placed an agent worktree INSIDE this worktree. One worktree creation per turn, and never while another git worktree operation is running. Every brief: confirm `git rev-parse --show-toplevel` is its own worktree; rebase onto the LOCAL `work/spaarkeai-word-add-in-r1`; never `git stash`; long jobs in the foreground, never wait on a background job (agents stall); contract tests in NEW files when agents run in parallel. |
### What 013 must honour from 012 (notes/012 §2, "Rules for task 013")
- **503 = could not determine.** Retry or let the user choose; never treat it as a new document.
- **A 403 with `reasonCode` `sdap.access.error.system_failure` is indeterminate too.**
- **Don't call the route for an empty or non-absolute `document.url`** (an unsaved document). Treat it as new locally.
- **`identity_conflict` is not "new"**: do not offer save-as-new.
- Word desktop and web both return the raw-space path form (spike-1 §19, §23). Send it exactly as returned.

### Last session (2026-09-10 → 11) — all committed + pushed to PR #960
- **012:** `8fec97b2d` (resolver + route), then `9750b4968` (Graph 403 → `not_resolvable`, after the live evidence). It was deployed twice to `spaarke-bff-dev` via `/bff-deploy`, hash-verified and healthy. Decisions are in `notes/012-identity-resolver-decisions.md`; the live table is in §8.
- **015:** `aff1ca9e4` (agent, isolated worktree) plus `4ea3cf6de` (corrected stale "waits on 032" claims). The add-in site was redeployed (run 34546485352), and Find is live for Word and Outlook.
- **Gates:** full `Sprk.Bff.Api.Tests` 12,139/0; ArchTests 191; no CVE; publish +0.016 MB against a fresh master build.
- **This worktree** now has root `node_modules`, so the husky/lint-staged pre-commit hook runs. Never `--no-verify`.

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

1. ~~Word DESKTOP capture~~ **DONE 2026-09-11.** The desktop value is byte-identical to the web capture and resolves
   (spike-1 §23).
2. **Re-upload the Word manifest at 1.0.8.0** — the operator uploaded from the SWA while it still served 1.0.7.0.
   The SWA now serves 1.0.8.0 (verified). Path: M365 Admin Center → Settings → Integrated apps →
   `https://icy-desert-0bfdbb61e.6.azurestaticapps.net/word/manifest.xml`. Then confirm the pane footer = 1.0.8 →
   **task 011 can close** (via the XML path). Outlook needs no re-upload (1.0.22.0, unchanged; its XML is
   `/outlook/outlook-manifest.xml`).
3. **Optional (012):** the URL of any OneDrive or SharePoint file that has no Spaarke record, to exercise the
   Dataverse not-found shape live (notes/012 §8). The az CLI cannot list OneDrive (AADSTS65002), and the BFF has no
   route that lists container children.
4. **Share privilege check** — security roles → `sprk_document` → Share. If it is not granted, the record⇔document
   access model has no gap (see D-032-1). OR reconnect Dataverse MCP (`/mcp`) and an agent can check.
5. **Dataverse MCP has been DOWN** (`CONNECTION_CLOSED`). Nothing here was "verified via MCP". Use `/mcp` or a
   restart to reconnect.

### Pending decisions (operator)

- ~~**Spike-1 §7 — stamp-as-primary.**~~ **Superseded 2026-09-14.** 013 is done, and task 014 (the stamp) was escalated: stamping the stored bytes breaks 028's content link, and the owner does NOT want an id added to documents automatically. The owner asked to DISCUSS how Word documents are matched to avoid duplicates — see Quick Recovery, "Waiting on the owner" (1). Design note: `notes/014-xml-part-stamp-decisions.md`.
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

- ~~**F-1**~~ **FIXED 2026-09-11** — `deploy-office-addins.yml` now watches
  `src/client/shared/Spaarke.Communication.Components/src/logic/connections/provenance.ts` (verified: webpack
  `webpack.config.js:101` aliases exactly that file; it has zero imports, so one path entry is complete).
- **W-5** `identity-obj-proxy` mapped in `jest.config.js:48` but not installed. **Verified INERT 2026-09-11**: no
  file in `src/client/office-addins` (source or test) imports a `.css/.less/.scss/.sass`, so the mapping is never
  resolved. Becomes live only when a stylesheet import is added. Fix after 013 lands (it owns office-addins this
  wave): install it as a devDependency, or drop the dead mapping. **Still open 2026-09-14** (013 has landed; not yet
  picked up — small, unowned → ours).
- **`npm run lint` in `src/client/office-addins` is broken** — the script points at a non-existent `src` subfolder
  (found by task 026, 2026-09-14; pre-existing, same class as D-043-2). Agents have been running `eslint` directly on
  touched files instead. Small, unowned → ours; fix alongside W-5.
- 🟠 **`sprk_matternumber` is `sprk_matter`'s PRIMARY NAME column** (task 030 agent, verified live 2026-09-11 —
  the main session has NOT independently re-verified). Consequence: a pane-created Matter shows a **blank name**
  in lookups/views until the separate numbering project ships. Not a regression (the pane already sends no
  number) — it raises that project's urgency. Recorded in `notes/030-numbering-handoff.md`; tell the owner.
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
