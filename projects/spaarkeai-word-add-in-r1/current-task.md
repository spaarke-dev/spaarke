# Current Task State — spaarkeai-word-add-in-r1

> **Last Updated**: 2026-09-11 (mid-session checkpoint — 021, 023 and the 030 rework in flight in isolated worktrees)
> **Recovery**: Read "Quick Recovery" first. Branch `work/spaarkeai-word-add-in-r1`, PR #960.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **2026-09-14 — PUSHED: 038 ✅ (plus the 014 design note and the 025 analysis note).** 014 ⛔ escalated: stamping the stored bytes conflicts with 028's content link; the owner rejected auto-adding an id and asked to DISCUSS how Word documents are matched to avoid duplicates (options put to the owner: location match only / content hash / an invisible id inside the file / a user-confirmed match; recommended clash prompt: [Save as a new version of it] [Use a different name] [Cancel]). **The 025 re-scope waits on that discussion.** Owner decided: generated `.eml` names get a unique suffix. New tasks **045** (stale bytes — production-blocking, after 025) and **046** (Email/Attachment never overwrite or delete another document's file — live on master; next to dispatch). **026 running** (related-record card). The CI Tier-2 failure on `9373c5abb` is an order-dependent AI-chat test (`ChatEndpointsTests.GetHistory_ReturnsMessages_WhenAuthenticated`: expected 2 messages, found 4), not this project's. — **Earlier: 038 MERGED locally at `49147eee9`**: required Matter Type via the owner-accepted `GET /api/office/search/matter-types`, cached ~24h, with a user-initiated Retry (`5350e129f` + `92fe94ddf`); 3 new suites added to `ci-gated-suites.txt` (21 listed). Merged-tree gates running (office-addins + BFF); then mark 038 ✅, commit, push, publish the PR draft (038 row + placement already drafted). **Next client task: 026 (related-record card), NOT 020** — 020's server fix edits `OfficeDocumentPersistence.CreateDocumentWithSpePointersAsync` (name→`sprk_documentname`, not Description) and appends to `OfficeEndpointsContractTests.cs`, the same method 014 (running) may restructure for its stamp-ordering crux and the same test file → **020 waits for 014**. 026 (deps 013 ✅) does not touch the save persistence path; give it a NEW contract-test file. Also: 020's "verified as-built" line numbers predate 021/024/038, which reshaped `SaveFlow.tsx`/`useSaveFlow.ts` — its agent must re-verify every premise. Dispatch 026 once the merged-tree gates pass and the 038 worktree removal finishes. **Earlier: 039 ✅ PUSHED at `9373c5abb`** (post-merge gates: build 0/0, Office/Docs/dedup/idempotency 260/0/9, ArchTests 191); PR #960 description updated + verified live (039 row; "025 must land before production use"). **In flight (isolated worktrees): 014 (server custom-XML stamp, opus — told that any change to content-dedup or idempotency behaviour from stamped bytes is an ESCALATION), 038 (Retry + client cache), 025 residual-collision analysis (read-only).** CI on `9373c5abb` being watched. — Earlier: **039 ✅ MERGED locally at `bdb98c230`.** 039 turns the silent lost-edit into a visible D1 collision → **025 now gates production use of the save path**. **038 NOT merged**: it added a new route `GET /api/office/search/matter-types` past its escalation trigger → owner asked; owner then asked whether a hard-coded list could replace the call (analysis: GUIDs ARE preserved by `scripts/Migrate-DataverseData.ps1`, so possible, but new/customer types would need a redeploy) → **owner 2026-09-13: KEEP the route + cache the list client-side (~24h)**. 038's agent is adding the Retry for a failed load plus the cache. **025 residual-collision analysis dispatched** (read-only; drafts the FR-12 amendment + re-scoped 025 for owner approval — per the 2026-09-08 path C → B plan). 014 next, after the post-039 gates pass. — Earlier: wave 3 = 039 (save-spine defects incl. silent lost edits, opus/xhigh — ALONE on the save spine) and 038 (required Matter Type in the pane, sonnet — client + maybe the SEARCH group; contract tests in a NEW file), isolated worktrees, started one at a time. Wave 2 **PUSHED** at `b5c08f26f` (022 + 024 + jest-dom types fix + 039/025 docs); PR #960 description updated and verified live (45.40 MB = +0.04 MB vs master; gated jest 18/18 / 277; test-file typecheck 111). CI on `b5c08f26f`: **32 pass / 0 fail / 2 skip (terminal)**. After 039: 014, 029, 025 (all save-spine/finalization — one at a time); client chain 020 → 026 (both `SaveFlow.tsx`, after 038). — Wave 2 detail: **022 ✅ `9f9fabb36`, 024 ✅ `7771aa71c`.** Uncommitted: `tsconfig.json` jest-dom types fix (owner-approved 2026-09-12; test-file typecheck 290 → ~111, production 0) + 3 new gated suites from 024. Gates running on the fully merged tree (office-addins + BFF Office/Docs/dedup/data-mutation + ArchTests). Then: commit, push, PR body. New task **039** (4 save-spine server defects from 024, incl. silent lost edits — run before any prod deploy of the save path); 024's 5th finding → task 025. **Next wave candidates:** 039 (alone on the save spine; priority), then 014; client serial chain 020 → 026 → 038 (all `SaveFlow.tsx`); 029. — Earlier this wave: **wave 2 dispatched: 024 (client version save + override, opus — critical path) and 022 (Generate Profile trigger, sonnet — contract tests in a NEW file)**, each in its own isolated worktree, started one at a time. **022 reported done (`a16de3bea`) but is NOT merged — main-session review found it returns 202 even when nothing was dispatched** (AI facade unregistered, or no bearer) → the pane would show "Pending" for a job that never runs (§10 / §F.1 / ADR-032). Agent sent back: 202 only when dispatched, 503 `OFFICE_PROFILE_002` when the facade is unavailable, client shows an error not "Pending". The detached-scope fire-and-forget itself was reviewed and is sound (mirrors the shipped Compose `refresh-profile`). CI on `7bc11ddf3`: 32 pass / 0 fail / 2 skip (terminal). 014 held (shares `OfficeService.cs`/`OfficeEndpoints.cs` with 022, and the save path with 024). Previous wave **PUSHED** at `7bc11ddf3`; PR #960 body updated (merged-tree publish 45.39 MB = **+0.04 MB** vs master `e0a6f87c4` 45.35 MB, re-checked unmoved; targeted rerun after 021's fix 76/0/9); CI on `7bc11ddf3` being watched to terminal. — Previous wave detail: **merged at `6d59d39cf`: 023 ✅, 030 ✅, 021 ✅** (incl. the Picklist fix `7bf213cf0` and the owner-ordered Notes removal `ccd876a2e`). All agent worktrees removed. Gates on the merged tree: BFF build 0/0; **full `Sprk.Bff.Api.Tests` 12,190 passed / 0 failed / 56 skipped** (compiled before 021's fix — a targeted rerun of the fix's tests + Documents/Office contract + version-save data-mutation is running, then a merged-tree publish-size measurement); ArchTests 191/191; office-addins typecheck/build/jest running. **To finish:** fill `scratchpad/pr-960-body.md` placeholders (TARGETED, MERGED_*), commit, push, `gh pr edit 960 --body-file`. **Next tasks:** 024 (client version save, critical path) or 014 (not together — both touch the save path); 022 (edits 021's files), then 020, 026, 038, 029 one at a time. Owner answers 2026-09-12: SPE has no readable version comment → job record; unknown id → 403; 029 owned here; Notes removed. |
| **Status** | **013 ✅ and 016 ✅ merged** (`8b5d58a2b`, `c475ed764`); gates on the merged tree: office-addins prod typecheck 0, build green (needs the 4 env vars — CI supplies them; locally use placeholders), jest 10 failing suites = pre-existing baseline, 013's 2 new suites added to `ci-gated-suites.txt`. **030 🔄 rework** (agent resumed with context): owner moved matter numbering to a SEPARATE project (server-side, on record create, NO plugin) and made Matter Type required-but-never-rejected — see project CLAUDE.md Decisions 2026-09-11. The pane half is **task 038** (created; `SaveFlow.tsx` posts `{name}` only today). 030 rework status 2026-09-11: code applied (numbering removed; publish +0.03 MB vs fresh master 45.35 MB), agent told to rerun its lost Step 9.5 review + full suite, and to treat an unknown `matterTypeId` like a missing one (create without type + warning). All three agents were interrupted once by a network drop (ENOTFOUND) and resumed with context. |
| **State at dispatch** | Main tree clean, 0/0 with origin, PR #960 CI green. Agents were told: do NOT edit current-task.md / TASK-INDEX.md / project CLAUDE.md (main session owns them); do NOT push, PR, deploy or trigger workflows; commit on their worktree branch only; no `--no-verify`. File ownership: 013 = `src/client/office-addins/**`; 016 = `tests/integration/**` + owns any shared contract-fixture change; 030 = `Services/Office/*` QuickCreate path + `OfficeEndpoints.cs` + a NEW `OfficeQuickCreateContractTests.cs` (no shared-fixture edits). 030 may do READ-ONLY Dataverse GETs via an az token; never writes. |
| **Next Action** | As each agent reports: review its diff on its `worktree-agent-*` branch, `git merge --no-ff` into `work/spaarkeai-word-add-in-r1` (expect small hunk conflicts in `OfficeEndpoints.cs` — three branches touch different routes), then rerun gates on the merged tree (`dotnet build` BFF, ArchTests, Office contract/unit subsets; office-addins typecheck/build-with-placeholder-env/jest). Then: update TASK-INDEX + this file; paste the Placement Justifications (030, 023, and 021's if it touched the BFF) into the PR #960 body; `/conflict-check`; push. **After 023:** 024 (critical path) and 014 (both touch the save path — not together). **After 021:** 022 (edits 021's new files), then 020, 026, 038 one at a time (all edit `SaveFlow.tsx`). Worktree note: `isolation: worktree` now bases on `origin/master` — every agent prompt must say "rebase onto the LOCAL `work/spaarkeai-word-add-in-r1`" and never dispatch two at the same instant (one creation failed on a race). |

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

- **Spike-1 §7 — stamp-as-primary.** Still recommended on its merits (identity by content survives a rename or a
  move), but it is no longer needed for viability: Spike-1 is GREEN. It cannot replace the URL path either, because
  014 stamps only the Office save path (notes/012 §1). The only thing left to decide is ORDER: 014 before or after
  013. Both are startable now.
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
  wave): install it as a devDependency, or drop the dead mapping.
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
