# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file. **Earlier checkpoints:** `notes/handoff-history/2026-11.md`.
> - **NOTE:** master is ahead of this work branch on project files. Read master's TASK-INDEX and README for task status.

> **Last Updated**: 2026-11-16 ~23:10Z (checkpoint #148, desktop session `uac-r2`). Private: `~/.claude/projects/c--code-files-spaarke-wt-unified-access-control-r2/private-*.md` (never copy into repo text).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Wrap-up, extended (round 90). `uac-r2` is authoritative, reviews peers, runs every dev deploy. Live actions need the owner's go IN THIS SESSION; the BFF needs the owner to name the exact SHA. ≤ 2 agents at once. |
| **Dev BFF** | **`be0420d763`** = master `16771d1051` + mcp **spike-v2 `973776a5a6`** (local merge in `C:\wtdep12`, ledger 154→155, never pushed). Live ~22:21Z 2026-11-16 (owner "Go BFF be0420d763"). Read-back: 4 files SHA-256, healthz 200, anon /mcp, /external/me, /api/me 401; live deps.json M.E.AI 10.11.0 / MCP 2.3.1 / OpenAI 2.14.0; AI Search -VerifyOnly all OK (MI flag true). **30-min gate PASSED** (12 req, 0 5xx, 0 exceptions). Carries ontology 195 (`04f5c943f9`, template render; AFTER notice sent) and r4 014a (#2852). Does NOT carry r4 014b, #2845 round 2, or SpaarkeMaster 1.3.2.0 source. **Rollback = `5ef0a548df` (`C:\wtdep11`).** Dev SPA still `c1eb6e4a8e`. |
| **Agents running** | None. |
| **Next action** | **PR #2867** (#2845 round 2, ready, head `3f22470d8e` = round 2 + master `fc9803419c` incl. 014b): merge on Router + Client lint green (`--match-head-commit` full sha). Then: next dev candidate = master (round 2 + 014b + 1.3.2.0 source) + spike-v2 in a fresh `C:\wtdep13` (ledger: copy `C:\wtdep12`'s resolved Ledger.cs if master's ledger is unchanged since 16771d1051) → build/tests/CVE/deps.json → ping mcp-r1 + ask spa-r4 → owner SHA go → deploy → read-back → gate → measure external latency both planes → close #2845 if < 1 s. |

## Active work
- **#2845 round 2 — PR #2867** (`C:\wt2845b`, branch `perf/2845-round2`). Per-id request memo, concurrent reads, #2851 fix, review F1 (process-wide `DataverseConcurrencyGate` 24 + `DataverseThrottlingHandler` 429/503 GET retry; on participation, deny-list AND ExternalDataService clients + walk reads), R1-R3, T1-T3, wiring test (perturbed). Two Opus reviews PASS-WITH-FIXES, fixes in; fix diff re-checked by me. Tests at `3f22470d8e`: scoped unit 7721/0, ArchTests 2639/0, integration 212/0 + Spe 353/0. Publish +52,560 B vs fresh master. Previous push: Router + Client lint green. Open item in PR: CallerRecordAccessProbe, impersonated root-set reads and the one-record walk stay ungated. Local FULL unit runs are unusable under machine load (WAF timeouts; sampled failures pass alone) — CI is the full-suite verdict.
- **SpaarkeMaster 1.3.2.0 — #2865 MERGED `57431c8e6a`** (invoice-family 24-column lock in source; Billing Admin reads; bundles rebuilt). provisioning-r1 told (its follow-up PR #2871 waits on the owner). **Not published, not imported anywhere: publish + every import need the owner's go.** Publishing lifts provisioning-r1's L2 Worker rule (tell them).
- **r4 014b — #2869 MERGED `fc9803419c`** after my review (PASS-WITH-FIXES → fix re-check PASS at `67b99e2126`). **014c (PATCH assignee, owner option (a)) = PR #2872 @ `32c32cb890`: my review PASS (349/349 tests); spa-r4 merges on green and sends the SHA** → fold into the next dev candidate.
- **#2860** (lenient QueryAsync, 87 callers): assigned to me; follow-up after round 2.

## Peer state
- **ebilling-r1:** 130e-1 (DEF-060) on work/ebilling-r1 only; its BFF reaches dev only after 079 syncs SpaarkeMaster (next version after 1.3.2.0, must keep `InvoiceFamilyFieldLockSourceGuardTests` green). **Pre-deploy gate: confirm `sprk_firmdescription` exists before any BFF build carrying 130e-1** (not on master yet). Advance notice: EB-D-240/242 memo columns (`sprk_invoice.sprk_decisionnote`, `sprk_invoicecorrection.sprk_note`); I had no objection; dev apply on their owner go with before/after notices. #2844 re-sweep still theirs.
- **provisioning-r1:** PR #2871 (S27 text names 1.3.2.0) waits on the owner; waits for my "1.3.2.0 published" notice.
- **field-mapping-r1:** **D-2 step 3 is unblocked** but held for the owner's go; pair it with a BFF swap (the settings restart the BFF). Tell fm when it's live (then their 054c Move).
- **mcp-r1:** dev spike = spike-v2 `973776a5a6` (branch spike/spaarkeai-mcp-phase0, confirmed current). Never deploy `work/spaarkeai-mcp-bff-api-r1`. Ping before every swap; live notice after (sent for be0420d763).
- **spa-r4:** not mid-E2E (next E2E = task 016 after 014b + 015 deploy); ask before a restart. 015 opens event rows from list data, GET for deep links only (K5).
- **ontology-r1:** 195 live on dev; owner composer template recheck can go.
- **word-add-in-r1:** #2829 merge needs the owner's go.

## Filed this session
- #2851 (memo race, fixed in round 2) · #2853 (evaluator Max dedupe; latent) · #2860 (systemic QueryAsync; mine) · #2863 (dev drift `sprk_sourceemaildocument` unsecured; ebilling's) · #2864 (SpaarkeAi bundle bakes dev BFF fallback + dev ids) · **#2870** (AI membership treats child-record assignee columns as access-conferring; A-21/D-4 follow-up) · #2848 / #2850 open, unowned.

## Waiting on the owner
- Dev checks: To Do create (Office); Send budget inquiry text; **AI drafting retry in the email composer**; **composer template recheck** (195 live).
- SpaarkeMaster 1.3.2.0 publish/import; D-2 step 3; word-add-in #2829; provisioning #2871; ebilling task 112; #2764; 191i live steps; 193 PCF import; D-10 after U-3; #2786 part 2 scope; follow-ups #2826, #2549. Checklist: `notes/owner-test-checklist-2026-11-13.md`.

## Scratch
- Worktrees: `C:\wtdep12` (live merge; keep), `C:\wtdep11` (rollback; keep), `C:\wt2845b` (round 2), `C:\wtm16` (master 16771d1051 publish baseline; can go), `C:\wt2859`, `C:\wt2869`, `C:\wt2852`, `C:\wt130e`, `C:\wtdep10`, `C:\wtdep9`, `C:\wtdep8`, `C:\wt2845`, `C:\wt2833` (can go). Remove only with `scripts/worktree/Remove-WorktreeSafe.ps1`.
- Gate: scratchpad `gate-be04.ps1 <t0 ISO>` (copy + rename prefix per deploy); index check `scripts/ai-search/Deploy-AllIndexes.ps1 -Environment dev -VerifyOnly` only if `AiSearch__ManagedIdentity__Enabled` is true.
- The untracked 0-byte `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs` in this worktree: leave it alone.
