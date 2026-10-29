# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-29 ~14:05Z (context-handoff at 85%).
> **Recovery**: Read "Quick Recovery" first. The session narrative is in git log. Live records: `notes/task-002-notes.md` (grid columns), `notes/task-074-a9-probes.md`, `notes/task-070-deploy-record.md`, `notes/contact-identity-dev-rollout-record.md`.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Owner 2026-10-29: "go task by task and close them out" — 002, 043, 059, 070/071, 074, then 072/073/090. |
| **Done today** | PR #2437 merged (`c36907571d`). **043 ✅**, handed to provisioning as #2443 (they accepted). **059 ✅**, C-1 revoked in Dewey Cheatham 13:39Z (dev BFF grants 0; workforce SPA grant kept; isolated az profile deleted). **Dev SPA** redeployed from master `f07ab2ef1f` (run 40882434220, 13:33Z; rollback tag `external-spa-dev-rollback-1fa0cf5cb8`; the live build before was `1fa0cf5cb8` from 2026-10-23, not `3531ed65e`). **074 licensed-user link PASS** (notes/task-074-a9-probes.md §1). |
| **002 (in progress)** | Owner chose "two steps". **Step 1 is live on dev**: all 6 grid configs carry the owner's allowed columns, order, labels and widths (scratchpad `grid-apply.ps1`; rollback `grid-rollback.ps1` with raw JSON in `grid-before\*.json`). **Step 2**: a BFF allow-list for 9 columns — project `sprk_projecttype_ref`; matter `sprk_mattertype`, `modifiedon`; WA `sprk_mattertype`, `modifiedon`; document `sprk_filetype`, `modifiedon`; invoice `sprk_totalamount_base`; service request `modifiedon`. The first 8 are in commit `9cb0668e77`, not pushed. The 9th (service request `modifiedon`) is being added by the implementer agent as a separate commit. UAC: no objection; wants tests against `ServiceRequestsModule(true/false)`, then reviews the PR. Two adversarial reviews (opus) are running on `9cb0668e77`. |
| **Next Action** | 1) Wait for the implementer's 9th-column commit and both review reports; fix any fix-now finding; re-check. 2) Push and open the PR, send it to UAC. 3) Merge after review. Dev BFF deploy needs a fresh owner OK naming the SHA, peer OKs, and UAC's 194b hold status (master ≥ `2cd0f62e2c`). 4) After the BFF is live: add the 9 columns to the grid configs (owner's widths/labels: Project Type 100; Matter Type 100; Last Activity 100; File Type 100; Total Amount (Base) 100; SR Last Activity 100). 5) Record the rollback JSON in notes/task-002-notes.md and close 002. |

### Uncommitted (this session's; commit by pathspec)
- `projects/spaarke-SPA-external-access-platform-r3/notes/task-074-a9-probes.md` (new), `current-task.md`.
- Owner's, never commit or discard: `.claude/agent-memory/researcher/**`.

### Private files (scratchpad, NOT in repo)
`C:\Users\RALPHS~1\AppData\Local\Temp\claude\c--code-files-spaarke-wt-SPA-external-access-platform-r3\15aa240f-634c-4332-99f6-382ebcb06a62\scratchpad\`: `grid-read.ps1`, `grid-apply.ps1` (`-Only`, `-Apply`), `grid-rollback.ps1`, `grid-before\`, `dc-c1.ps1` + `dc-c1-snapshot-*.json` (C-1 rollback data), `link-read.ps1`, `teams-dev-pkg\appPackage.zip`, plus the older `phaseA.ps1`, `readback.ps1`, `token-probe.ps1`, `edov-*.ps1`.

---

## Open owner items
- **070/071 Teams**: the owner installs `appPackage.zip` (id `23610794…`, v1.2.0; still current) in **Dewey Cheatham** via Teams admin center; for the "unlisted" run, install in the Spaarke directory. Instructions were given 2026-10-29.
- **074 remaining probes** (resubmit, intake, existing-record bind, unlisted directory) need accounts. The owner can sign in only as Ralph Dewey today. Ask: can they sign in as Tracy Cheatham, or create a Dewey Cheatham test user with an email (covers resubmit, intake, bind), and an acmecorpdemo or other-directory user (unlisted)?
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
