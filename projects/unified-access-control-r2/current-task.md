# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file. **Earlier checkpoints:** `notes/handoff-history/2026-11.md`.
> - **NOTE:** master is ahead of this work branch on project files. Read master's TASK-INDEX and README for task status.

> **Last Updated**: 2026-11-16 ~07:30Z (checkpoint #145, desktop session `uac-r2`). Private: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-*.md` (never copy into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Wrap-up, extended (round 90). `uac-r2` is authoritative, reviews peers, runs every dev deploy. Live actions need the owner's go IN THIS SESSION; the BFF needs the owner to name the exact SHA. ≤ 2 agents at once. |
| **Dev BFF** | **`5ef0a548df`** = master `ce6c241964` + mcp **spike-v2 `973776a5a6`** (packages only: M.E.AI/.OpenAI 10.11.0, OpenAI 2.14.0). Local merge in `C:\wtdep11`, ledger 154→155, never pushed. Live 05:47:05Z 2026-11-16 (owner "go BFF 5ef0a548df"). Read-back: 4 files SHA-256, healthz 200, anon /mcp 401, /external/me 401, live deps.json is one consistent M.E.AI family; AI Search -VerifyOnly 7/7 OK (MI flag true). **30-min gate PASSED** (15 req, 0 5xx; 5 exceptions = ID-LINK-RECON 15 s DV timeouts, fail-closed, pre-existing). Fixes the dev drafting 503 (TypeLoadException from the old spike); not yet proven by a live draft call. **Rollback = `f451e0874d` (`C:\wtdep10`).** Dev SPA still `c1eb6e4a8e`. |
| **Agents running** | (1) **#2845 round 2 fix-round implementer** (Opus) in `C:\wt2845b`. (2) **#2859 adversarial reviewer** (Opus) on `C:\wt2859`. |
| **Next action** | Wait for both agents. Then: round 2 → re-check the fix diff → rebase onto #2852 once spa-r4 merges it → integration suites → PR → merge → deploy merge (master + spike-v2, ledger 155) → owner SHA go → deploy → measure latency on both planes → close #2845 if < 1 s. #2859 → fix findings → push → PR (code-review + adr-check done by the reviewer) → merge → tell provisioning-r1 the version + commit. |

## Active work
- **#2845 round 2** (`C:\wt2845b`, branch `perf/2845-round2`, 408d045902 + 577f962a5b on ce6c241964; includes the #2851 fix). Two Opus reviews: PASS-WITH-FIXES, no over-grant. The fix round covers:
  - **F1 (blocking):** a process-wide Dataverse concurrency gate (DelegatingHandler + SemaphoreSlim, default 24) with a 429/503 Retry-After retry, on both ExternalAccess typed clients plus the SecureRootInheritance walk. Without it a 429 silently drops records. The live round 1 has the same gap, smaller.
  - R1: partial-batch per-id memo.
  - R2: `For` locks HttpContext then Items.
  - R3: sibling cancel.
  - T1-T3: tests.
- **#2859 SpaarkeMaster 1.3.2.0** (`C:\wt2859`, branch `fix/2859-invoice-lock-in-spaarkemaster` from master e476e9b61b; 7191dcc0ed source + ArchTest, 47832cd10d release, 5beb7c0f05 rebuilt bundles + docs + ontology wording; NOT pushed):
  - the #2799 24-column invoice-family lock in source (IsSecured=1; Readers 4/0/0, Writers 4/4/4);
  - `InvoiceFamilyFieldLockSourceGuardTests`;
  - Billing Admin's two unreleased Reads (084f);
  - the three stale bundles rebuilt from master (no locked-column writes; ratchet baseline now empty);
  - 4 DerivedBaseColumnSecurityExceptions entries, ontology-r1 ACKed (it also confirmed its live SignalPredicateTests fails on dev today for these four, and this fixes it).
  - Owner decision "path A" was made in provisioning-r1's session (task 270). **Importing into any environment needs the owner's go here.** Note for the PR: dev serves wizard bundles built from a different commit (2026-11-15), so a dev import would replace them with master builds.
- **#2852 (spa-r4 014a):** my re-check of a866dad1be PASSED (QueryStrictAsync for the grant reader; cancel; per-call firm cache). spa-r4 merges, then 014b. #2860 (lenient QueryAsync, 87 callers) is assigned to me: follow-up after #2852.

## Peer state
- **ebilling-r1:** 130e-1 (DEF-060) merged into work/ebilling-r1 after my PASS. Its BFF goes to dev only after 079 syncs SpaarkeMaster (days to weeks; 079 takes the version after 1.3.2.0 and must keep `InvoiceFamilyFieldLockSourceGuardTests` green). sprk_rejectionreason + sprk_firmdescription are applied on dev (confirmed by my query; #2858 closed). **My pre-deploy gate: confirm sprk_firmdescription exists before any BFF build that carries 130e-1.** #2844 re-sweep still theirs.
- **provisioning-r1:** H7b S27/S28 (task 270), pinned to the same psd1. It won't deploy L2 procedure v10 before 1.3.2.0 is published. Waits for my version + commit.
- **field-mapping-r1:** 054c Stamp -Apply done on dev (0 rows). **D-2 step 3 is unblocked** but held for the owner's go; pair it with a BFF swap (the settings restart the BFF). Tell fm when it's live (then their 054c Move).
- **mcp-r1:** dev spike = **spike-v2 `973776a5a6`** (branch spike/spaarkeai-mcp-phase0). Never deploy `work/spaarkeai-mcp-bff-api-r1`. Ping before every swap; send a live notice after.
- **word-add-in-r1:** #2829 merge needs the owner's go.

## Filed this session
- #2851 (memo race, in round 2)
- #2853 (evaluator Max dedupe on an out-of-set access level; latent, fails closed)
- #2857 → closed as a dup of ebilling's #2858
- #2860 (systemic QueryAsync; filed by spa-r4, assigned to me)
- #2848 / #2850 open (job dead letters / bg drift), unowned.

## Waiting on the owner
- Owner checks on dev: To Do create (Office); Send budget inquiry text; **AI drafting retry in the email composer** (proves the 5ef0a548df fix).
- D-2 step 3; word-add-in #2829 merge go; ebilling task 112 go; #2764; 191i live steps; 193 PCF import; D-10 after U-3; #2786 part 2 scope; follow-ups #2826, #2549. Checklist: `notes/owner-test-checklist-2026-11-13.md`.

## Scratch
- Worktrees: `C:\wtdep11` (live merge; keep), `C:\wtdep10` (rollback; keep), `C:\wt2845b` (round 2), `C:\wt2859` (#2859), `C:\wt2852` / `C:\wt130e` (review checkouts; can go), `C:\wtdep9`, `C:\wtdep8`, `C:\wt2845`, `C:\wt2833` (can go). Remove only with `scripts/worktree/Remove-WorktreeSafe.ps1`.
- Gate: scratchpad `gate-5ef0.ps1 <t0 ISO>`; index check `scripts/ai-search/Deploy-AllIndexes.ps1 -Environment dev -VerifyOnly` only if `AiSearch__ManagedIdentity__Enabled` is true.
- The untracked 0-byte `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs` in this worktree: leave it alone.
