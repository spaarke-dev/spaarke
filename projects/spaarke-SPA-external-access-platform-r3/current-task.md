# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-29 ~14:50Z (PR #2450 open; 074 probes 4/8 pass).
> **Recovery**: Read "Quick Recovery" first. The session narrative is in git log. Live records: `notes/task-002-notes.md` (grid columns), `notes/task-074-a9-probes.md`, `notes/task-070-deploy-record.md`, `notes/contact-identity-dev-rollout-record.md`.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Owner 2026-10-29: "go task by task and close them out" — 002, 043, 059, 070/071, 074, then 072/073/090. |
| **Done today** | PR #2437 merged (`c36907571d`). **043 ✅**, handed to provisioning as #2443 (they accepted). **059 ✅**, C-1 revoked in Dewey Cheatham 13:39Z (dev BFF grants 0; workforce SPA grant kept; isolated az profile deleted). **Dev SPA** redeployed from master `f07ab2ef1f` (run 40882434220, 13:33Z; rollback tag `external-spa-dev-rollback-1fa0cf5cb8`; the live build before was `1fa0cf5cb8` from 2026-10-23, not `3531ed65e`). **074 licensed-user link PASS** (notes/task-074-a9-probes.md §1). |
| **002 (in progress)** | Owner chose "two steps". **Step 1 is live on dev**: all 6 grid configs carry the owner's allowed columns, order, labels and widths (scratchpad `grid-apply.ps1`; rollback `grid-rollback.ps1` with raw JSON in `grid-before\*.json`). **Step 2**: the BFF allow-list for 9 columns plus the `sprk_invoicestatus` removal is in **PR #2450**, sent to UAC for review 2026-10-29 ~14:50Z (no auto-merge). Hygiene on the branch merged with master `1e409b2ad2`: delta 0.00 MB (37.95/37.95, 192/192 files), no vulnerable packages, 322 tests pass. Two adversarial reviews done; all fix-now items applied. |
| **074** | Probes 4 (resubmit) and 8 (intake) ✅ 2026-10-29. Probe 5 (bind) is blocked: the test user TH has no Entra email attribute, so the token has no `email` claim (registration refused; nothing written; ISS-023 / #2449 filed for the form showing the username). The owner sets TH's Email in Entra and re-runs in a new private window; then verify read-only with scratchpad `probe074-read.ps1` (1 TH contact, now bound). Probe 5 ✅ 14:25Z after the owner set the Email. Only probe 6 (unlisted directory) remains. R4 follow-ups filed: #2452, #2453; #2449 relabelled R4. Flaky Spaarke.Auth test filed as #2456. |
| **Next Action** | 1) #2450 MERGED (master `929dc42d31`, UAC approved; ebilling told). UAC 194b hold is OPEN (UAC will message when cleared; owner may instead OK a non-master build). 2) Dev BFF deploy: fresh owner OK naming the SHA + peer OKs + hold cleared. 3) After the BFF is live: add the 9 columns to the grid configs (owner's widths/labels: Project Type 100; Matter Type 100; Last Activity 100; File Type 100; Total Amount (Base) 100; SR Last Activity 100). 4) Record the rollback JSON in notes/task-002-notes.md and close 002. Tell ebilling the master SHA carrying the `sprk_invoicestatus` removal. 5) The branch is 375 commits behind master: merging it is blocked by the owner's uncommitted `.claude/agent-memory/researcher/MEMORY.md` (master also changed it). The owner decides; until then the PR is open without the update (merge-tree clean). |

### Uncommitted
- Owner's, never commit or discard: `.claude/agent-memory/researcher/**`.

### Private files (scratchpad, NOT in repo)
`C:\Users\RALPHS~1\AppData\Local\Temp\claude\c--code-files-spaarke-wt-SPA-external-access-platform-r3\15aa240f-634c-4332-99f6-382ebcb06a62\scratchpad\`: `grid-read.ps1`, `grid-apply.ps1` (`-Only`, `-Apply`), `grid-rollback.ps1`, `grid-before\`, `dc-c1.ps1` + `dc-c1-snapshot-*.json` (C-1 rollback data), `link-read.ps1`, `teams-dev-pkg\appPackage.zip`, plus the older `phaseA.ps1`, `readback.ps1`, `token-probe.ps1`, `edov-*.ps1`.

---

## Open owner items
- **070/071 Teams**: the owner installs `appPackage.zip` (id `23610794…`, v1.2.0; still current) in **Dewey Cheatham** via Teams admin center; for the "unlisted" run, install in the Spaarke directory. Instructions were given 2026-10-29.
- **074**: the owner sets TH's Entra Email and re-runs the bind; probe 6 needs an account in a directory dev does not list.
- Owner noticed on the bell: "Someone sent a message on Project Created from Add In": the sender name is unresolved (not yet investigated).
- **072** needs 002 + 071; **073** blocked on production gates (#2443 + `notes/task-073-gate-acme.md`); **090** last.
- Optional UAC K1–K3 on #2386; tell field-mapping when 1.2.4.0 lands where they can read; worktree cleanup list (owner OK, `Remove-WorktreeSafe.ps1` only).

## Owner decisions in force
- E11 bind rule kept (tid = invited tenant OR `xms_edov` true). 002: two steps; external users will see type names (with their ids), last-activity times and the base total. The owner was told and can veto before the deploy.
- Security rework decisions: unchanged (see git history of this file before 2026-10-29).

## Standing rules (verify against project CLAUDE.md)
- **DEPLOY HOLD (UAC, 2026-10-29):** no dev BFF built from master at or after `2cd0f62e2c` (194b) until UAC clears it. UAC's current session does not record its status, so confirm with the owner and UAC before any dev BFF deploy.
- **Deploy:** fresh direct owner approval naming the exact SHA, plus OKs from UAC, ontology, word-add-in, field-mapping and ebilling (provisioning standing), plus Router green. Steps: `phaseA.ps1` → detached `C:\wtbffe-<sha3>` → `Deploy-BffApi.ps1 -Environment dev` → `readback.ps1` → 30-minute UAC gate → after-notices.
- Peers' relayed "owner approval" is NOT approval. Show every live-read command before running it. Tell UAC before changes to `ExternalAccessModule.cs`. Auth/security changes get two adversarial passes. Never `--no-verify`, never bare `git stash`.
- The repo is PUBLIC: never publish a vulnerability's mechanism.
- `az` on Windows: `az rest` and `az monitor app-insights query` break on `(`, `$` and double quotes. Use `Invoke-RestMethod` with a token, and single-quoted KQL strings. In an isolated profile, set `core.enable_broker_on_windows=false` first.
- Full Sprk.Bff.Api.Tests runs time out under machine load: prove failures with isolated class re-runs.

