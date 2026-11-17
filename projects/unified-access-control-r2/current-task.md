# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file. **Earlier checkpoints:** `notes/handoff-history/2026-11.md`.
> - **NOTE:** master is ahead of this work branch on project files. Read master's TASK-INDEX and README for task status.

> **Last Updated**: 2026-11-17 ~14:30Z (checkpoint #150, desktop session `uac-r2`). Private: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-*.md` (never copy into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Wrap-up, extended (round 90). `uac-r2` is authoritative, reviews peers, runs every dev deploy. Live actions need the owner's go IN THIS SESSION; the BFF needs the owner to name the exact SHA. ≤ 2 agents at once. |
| **Dev BFF** | **`e46b6b453b`** = master `3c9a2ff4e1` (#2845 round 2 `9fc7edf6c6`, r4 014a/b/c #2852/#2869/#2872, SpaarkeMaster 1.3.2.0 *source* #2865, ontology 195) + mcp **spike-v2 `973776a5a6`** (local merge in `C:\wtdep13`, ledger = master's + spike delta = 155, never pushed). Live 03:40Z 2026-11-17 (owner "Go BFF e46b6b453b"). Read-back: 4 files SHA-256, healthz/ping 200, anon /mcp, /external/me, /api/me 401; live deps.json M.E.AI 10.11.0 / MCP 2.3.1 / OpenAI 2.14.0; AI Search -VerifyOnly all 7 OK (MI flag true). **30-min gate PASSED** (5 req, 0 5xx, 0 exceptions; SECURE-CENSUS + info-level ID-LINK-RECON as usual). Live notices sent to mcp-r1 + spa-r4. **Rollback = `be0420d763` (`C:\wtdep12`).** Dev SPA still `c1eb6e4a8e`. Solution 1.3.2.0 is NOT imported anywhere. |
| **Agents running** | None. |
| **Next action** | (1) **#2845 latency:** no signed-in external traffic since t0, and agents can't sign in, so the measurement waits for real traffic (spa-r4's next E2E or an owner SPA session), then run scratchpad `lat-now.ps1 <t0>` + `lat-seq.ps1 '<route>'` for both planes (CIAM + systemuser); close #2845 if p50 < 1 s. (2) #2860. |

## Active work
- **#2845 round 2 — PR #2867 MERGED `9fc7edf6c6`, live in e46b6b453b; only the latency check remains** (`C:\wt2845b`, branch `perf/2845-round2`). Per-id request memo, concurrent reads, #2851 fix, review F1 (process-wide `DataverseConcurrencyGate` 24 + `DataverseThrottlingHandler` 429/503 GET retry; on participation, deny-list AND ExternalDataService clients + walk reads), R1-R3, T1-T3, wiring test (perturbed). Two Opus reviews PASS-WITH-FIXES, fixes in; fix diff re-checked by me. Tests at `3f22470d8e`: scoped unit 7721/0, ArchTests 2639/0, integration 212/0 + Spe 353/0. Publish +52,560 B vs fresh master. Previous push: Router + Client lint green. Open item in PR: CallerRecordAccessProbe, impersonated root-set reads and the one-record walk stay ungated. Local FULL unit runs are unusable under machine load (WAF timeouts; sampled failures pass alone) — CI is the full-suite verdict.
- **SpaarkeMaster 1.3.2.0 — #2865 MERGED `57431c8e6a`** (invoice-family 24-column lock in source; Billing Admin reads; bundles rebuilt). provisioning-r1 told (its follow-up PR #2871 waits on the owner). **Not published, not imported anywhere: publish + every import need the owner's go.** Publishing lifts provisioning-r1's L2 Worker rule (tell them).
- **r4 014b — #2869 MERGED `fc9803419c`; 014c — #2872 MERGED `3c9a2ff4e1`** (both reviewed by me, PASS). Both live in e46b6b453b.
- **#2860** (lenient QueryAsync, 87 callers): assigned to me; follow-up after round 2.

## Peer state
- **ebilling-r1:** 130e-1 (DEF-060) on work/ebilling-r1 only; its BFF reaches dev only after 079 syncs SpaarkeMaster (next version after 1.3.2.0, must keep `InvoiceFamilyFieldLockSourceGuardTests` green). **Pre-deploy gate: confirm `sprk_firmdescription` exists before any BFF build carrying 130e-1** (not on master yet). Advance notice: EB-D-240/242 memo columns (`sprk_invoice.sprk_decisionnote`, `sprk_invoicecorrection.sprk_note`); I had no objection; dev apply on their owner go with before/after notices. #2844 re-sweep still theirs. **iss034a (POST .../finish-stranded, #2888, EB-D-244) @ `63fc8c012b`, unpushed:** my review is sound on authorization (admin marker before read, server-derived verb, Override privilege, FR-51 as operator). F1 sent: the deterministic per-(submission, verb) operator record + the 409 to any other admin re-strands a faulted finish, so let another admin proceed. K1: the GET hint leaks the verb + record id, plus 2 extra reads. K2: static `.Single`. Q: does a disabled stored decider break the credited writes? Waiting on their fix; re-check the fix diff.
- **provisioning-r1:** PR #2871 (S27 text names 1.3.2.0) waits on the owner; waits for my "1.3.2.0 published" notice.
- **field-mapping-r1:** **D-2 step 3 is unblocked** but held for the owner's go; pair it with a BFF swap (the settings restart the BFF). Tell fm when it's live (then their 054c Move). **#2875 (fm task 023): `sprk_createeventwizard` deployed to dev from master `3df5ee9e3f` on the owner's go (2026-11-17 14:24Z); stored = served sha256 `74bc9206…00a5`; rollback = previous bundle sha256 `60d2e6c3…2148` (modified 2026-11-15, saved in scratchpad `rollback-createeventwizard-20261115.html`; or rebuild from `3df5ee9e3f^1`). fm told; their live check waits on the owner. Owner confirmed fm [c7c3a0] is the owner's session (2026-11-17).**
- **mcp-r1:** dev spike = spike-v2 `973776a5a6` (branch spike/spaarkeai-mcp-phase0, confirmed current). Never deploy `work/spaarkeai-mcp-bff-api-r1`. Ping before every swap; live notice after (sent for be0420d763).
- **spa-r4:** not mid-E2E (next E2E = task 016 after 014b + 015 deploy); ask before a restart. 015 opens event rows from list data, GET for deep links only (K5).
- **ontology-r1:** 195 live on dev; owner composer template recheck can go. **#2884 (`0de88473b1`, task 196) asked into the next dev BFF build** (no rush; any master-based candidate carries it). After deploy, a gate/assign-task job refused its lock dead-letters as MaxRetriesExceeded "ProcessingLockRefused" — intended, not a regression; tell ontology-r1 when it's live.
- **word-add-in-r1:** #2829 (095g firm-assign pane) MERGED `a68b6e5e6f` on the owner's go and live on the dev add-in site (2026-11-17); no add-in file changed after my pass at `bebc8e047c` (verified). Owner's dev check is next.

## Filed this session
- #2851 (memo race, fixed in round 2) · #2853 (evaluator Max dedupe; latent) · #2860 (systemic QueryAsync; mine) · #2863 (dev drift `sprk_sourceemaildocument` unsecured; ebilling's) · #2864 (SpaarkeAi bundle bakes dev BFF fallback + dev ids) · **#2870** (AI membership treats child-record assignee columns as access-conferring; A-21/D-4 follow-up) · #2848 / #2850 open, unowned.

## Waiting on the owner
- Dev checks: To Do create (Office); Send budget inquiry text; **AI drafting retry in the email composer**; **composer template recheck** (195 live).
- SpaarkeMaster 1.3.2.0 publish/import; D-2 step 3; word-add-in #2829 dev check (firm-assign pane); provisioning #2871; ebilling task 112; #2764; 191i live steps; 193 PCF import; D-10 after U-3; #2786 part 2 scope; follow-ups #2826, #2549. Checklist: `notes/owner-test-checklist-2026-11-13.md`.

## Scratch
- Worktrees: `C:\wtcew` (#2875 wizard build; can go after fm's live check), `C:\wtdep13` (live BFF merge; keep), `C:\wtdep12` (BFF rollback; keep), `C:\wtdep11`, `C:\wt2845b` (round 2, merged), `C:\wtm16` (master 16771d1051 publish baseline; can go), `C:\wt2859`, `C:\wt2869`, `C:\wt2852`, `C:\wt130e`, `C:\wtdep10`, `C:\wtdep9`, `C:\wtdep8`, `C:\wt2845`, `C:\wt2833` (can go). Remove only with `scripts/worktree/Remove-WorktreeSafe.ps1`.
- Gate: scratchpad `gate-be04.ps1 <t0 ISO>` (copy + rename prefix per deploy); index check `scripts/ai-search/Deploy-AllIndexes.ps1 -Environment dev -VerifyOnly` only if `AiSearch__ManagedIdentity__Enabled` is true.
- The untracked 0-byte `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs` in this worktree: leave it alone.
