# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file. **Earlier checkpoints:** `notes/handoff-history/2026-11.md`.
> - **NOTE:** master is ahead of this work branch on project files. Read master's TASK-INDEX and README for task status.

> **Last Updated**: 2026-11-16 01:20Z (checkpoint #144, desktop session `uac-r2`). Private: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-*.md` (never copy into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Wrap-up, extended (round 90). `uac-r2` is authoritative, reviews peers, runs every dev deploy. Live actions need the owner's go IN THIS SESSION; the BFF needs the owner to name the exact SHA. ≤ 2 agents at once. |
| **Dev BFF** | **`f451e0874d`** = master `ce6c241964` + spike `d7369fecf1` (local merge in `C:\wtdep10`, ledger 154→155, never pushed). Live ~00:45Z 2026-11-16 on the owner's "go BFF ce6c241964". Carries #2845 (PR #2847) and ebilling #2846. Read-back OK (4 files SHA-256, healthz 200, anon /mcp, spike, /external/me 401). **30-min gate PASSED** (13 req, 0 5xx), BUT **0 external requests in the window → #2845 latency NOT yet measured on dev.** Rollback = `aa7d6345f3` (`C:\wtdep9`) alone; still pairs with the dev SPA (`c1eb6e4a8e`). |
| **#2845 (owner go 2026-11-15)** | MERGED as PR **#2847** `ce6c241964`: `RequestAccessMemo` (per-request, in HttpContext.Items, faults never kept, cleared on grant invalidation + removed-marker writes), three per-type compositions concurrent, 5 malformed-2xx fail-opens fixed (NoAccessListReader.ParseRows, org memberships, ReadIdColumnAsync, org-member page, colleague page). Two Opus adversarial passes, all F fixed; known limits in the PR. Issue #2845 stays OPEN until the live latency is measured (baseline 3–4 s, 50–60 sequential DV calls; target < 1 s). |
| **Next action** | Measure #2845 on dev: get a token for `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/.default` with `AZURE_CONFIG_DIR=C:/tmp/az-uac-child` (testuser1, dev test identity, read-only), call `GET /api/v1/external/me` a few times, then query App Insights for server duration + DV calls per request (`scratchpad/gate-f451.ps1` has `latency`/`dvPerReq` queries; t0 = call time). If a systemuser can't reach /external, ask spa-r4 to have its owner open a record in the external SPA. Post numbers on #2845, close it if < 1 s. |

## Open items handed to peers (watch)
- **#2844 (ebilling's):** sprk_document fault fixed and live. The re-index sweep (12 jobs, `ebilling-iss022-sweep-20261116T005512Z`) failed at the next step: the live dev `spaarke-invoices-index` lacks 14 fields from task 152 (`coreRecordType`, …), giving AI Search 400. ebilling's fix is an additive PUT via `scripts/ai-search/Deploy-AllIndexes.ps1`, then -VerifyOnly, then a re-sweep. Needs the owner's go in ebilling's session. #2844 stays open until a clean re-sweep.
- **Gate addition (mine, agreed with ebilling):** from the next deploy, the post-deploy gate runs `Deploy-AllIndexes.ps1 -Environment dev -VerifyOnly`. It writes nothing; exit 0 = OK, 6 = drift, other = setup/auth failure. **First read `AiSearch__ManagedIdentity__Enabled` on spaarke-bff-dev and run the verifier ONLY if it is true**, because the false path reads the search admin key (a secret).
- **#2848:** 4085 dead letters on dev sdap-jobs (+71 sdap-communication, +14 office-upload-finalization). Owner TBD (job platform). Filed, not triaged.
- **spa-r4 task 014 (event forms):** my UAC review is recorded verbatim in its `notes/task-014-notes.md` (e3d116ff1f). It is building **014a** (new service in Infrastructure/ExternalAccess: "contacts with effective external access to root R") as its own PR for MY GATE REVIEW. Check it against Q2 1–7 + the Q6 pins. With its owner: Q5 (creator name column) and the Q2.9 cross-firm names note.

## Peer state
- **ebilling-r1:** see #2844 above. Task 112 needs the owner's go in ebilling.
- **word-add-in-r1:** #2829 (095g Outlook pane) PASSED my review at bebc8e047c; 095g is on dev; merge needs the owner's go.
- **spa-r4:** owner E2E retest round finished (its task 018 closed). Building 014a.
- **mcp-r1:** spike still `d7369fecf1` (never deploy `work/spaarkeai-mcp-bff-api-r1`). Live notice sent for f451e0874d. Ping before every swap.
- **ontology-r1 / field-mapping-r1:** nothing pending with me.

## Waiting on the owner
- Owner checks on dev: To Do create (Office); Send budget inquiry text.
- word-add-in #2829 merge go; ebilling task 112 go; ebilling index PUT go (in their sessions).
- D-2 step 3 (Direct setup + `DirectMessage:*` settings); #2764; 191i live steps; 193 PCF import; D-10 after U-3; #2786 part 2 scope.
- Follow-ups open: #2826 (access mode in G5 checks), #2549 (deploy-window exceptions).
- Checklist: `notes/owner-test-checklist-2026-11-13.md`.

## Scratch
- Worktrees: `C:\wtdep10` (current dev BFF merge; keep), `C:\wtdep9` (rollback source; keep until the next deploy), `C:\wtdep8` (can go), `C:\wt2845` (merged; can go), `C:\wt2833` (merged, detached; can go), `C:\wtdep4`, `C:\wt197a`, `C:\wt197b`, `C:\wt198s`.
- Gate script: scratchpad `gate-f451.ps1 <t0 ISO>` (App Insights `spe-insights-dev-67e2xz`, rg `spe-infrastructure-westus2`).
- The untracked 0-byte `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs` in this worktree: leave it alone.
