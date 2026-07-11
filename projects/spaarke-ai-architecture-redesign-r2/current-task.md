# Current Task State — Spaarke AI Architecture Redesign R2 (Core)

> **Last Updated**: 2026-07-10 (late evening) — REMEDIATION WAVE in flight (by context-handoff)
> **Recovery**: Read "Quick Recovery" first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Phase** | **Post-audit remediation wave** (operator-directed). E2E completion audit DONE: `notes/e2e-completion-audit-2026-07-10.md` (committed `dd81632c3`). Operator ruled: F-1..F-4 + F-7..F-11 are r2 DELIVERABLES (no deferral); F-5 fixed immediately. |
| **Done this wave** | **F-5 CI TRX-overwrite fix COMMITTED** (`fe1d1cfab`): LogFilePrefix per-project TRX + multi-TRX pass-2 verdict in `.github/workflows/sdap-ci.yml`. Consequence: CI will honestly FAIL until F-6 pre-existing failures are fixed (agents on it). |
| **In flight (6 background agents, do not duplicate)** | (1) fix-client: F-4 onNextStep wiring in ConversationPane + F-9 TZ-pinned EntityInfoWidget test. (2) fix-harness: F-10 CVE hard-stop in Measure-BffPublishSize.ps1. (3) fix-test-hygiene: F-11 AuditLog flake root-fix (#618) + SprkChatAgentFactory:662 stale comment + F-6 stale guards (Canvas ×2 retarget-or-delete, StableId Consumer06/07 update, AiModule dead comment). (4) fix-jobaware: F-3 wire ComposeJobAware onto live path (honest-stop clause if no real consumer). (5) fix-ambiguity: F-8 thread real content-safety/uncertainty into gate. (6) fix-analysis-tests: F-6 ExecuteAnalysis ×2 root-cause (UAC one may be a SECURITY bug — filter not applied) + #621 GET-after-DELETE 500 root-cause. |
| **WAVE COMPLETE + MERGED (2026-07-10 ~23:00Z)** | ALL findings fixed. **PR #628 MERGED** (master `ef5d098b4`, full remediation wave) + **PR #631 MERGED** (F-5 completion: build-test job-level swallow REMOVED, 2 Scheduling flakes registered). **Master CI CONFIRMED honest-green with blocking active.** #618 + #621 CLOSED (root-fixed / fixture-artifact); #619 = (a)-only. ADR ArchTests 5→2 (2 handoff charters in `notes/adr-archtest-handoff-charters.md`). Compose-r2 pinged (their notes: PING-from-core-parity-merge-landed + HANDOFF-from-core-remediation-wave) — they reconcile 034 onto the bind-move and take the JOINT DEPLOY from master. |
| **Shield (F-8 follow-up, operator-approved in-wave)** | PromptShieldChatMiddleware DONE + committed LOCALLY (`51a490a60` on this worktree branch, default-OFF config gate `AiSafety:PromptShield:ChatPipelineEnabled`). AFTER compose's joint deploy: rebase branch on master → PR → merge → small BFF deploy + spaarkedev1 activation (setting=true + ContentSafety endpoint + MI "Cognitive Services User" role). Env checklist in the commit message + agent report. |
| **Blocked on operator** | 049 + 069 browser UAT (AFTER joint deploy + shield deploy); #629 FR-30 memory-governance handoff triage (from compose, separate); then 090 close (defer groups a–f still unfiled; memory hard-governance risk re-confirm). |

### Key facts
- Audit verdict: NO fabricated completions; gaps = built-but-not-load-bearing (F-1 envelope telemetry-only, F-2 user-memory no recall, F-3 job-aware dormant, F-4 chips inert) + systemic F-5 CI swallow + F-6 pre-existing failures (all R1-era: Canvas/StableId guards, ExecuteAnalysis ×2) + minor tail F-7..F-11.
- Live env verified: healthz Healthy; memory.write row `2172b721` ACTIVE; 3 retired rows Inactive (send-artifact Active); `memory-items`+`audit-partitioned` containers live.
- Master CI run 29108634079 = "success" while logs show 9 test failures + 5 ADR ArchTest failures → F-5 proof.
- Deferral inventory given to operator (DEF-001..004, PE-D1..D8/#612–#619, unfiled close groups a–f, parked items). PE-D7/#618 being fixed now; PE-D8(b) scope question asked.
