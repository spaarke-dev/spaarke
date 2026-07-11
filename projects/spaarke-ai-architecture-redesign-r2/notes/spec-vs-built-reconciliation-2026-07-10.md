# Spec-vs-Built Reconciliation — spaarke-ai-architecture-redesign-r2

> **Date**: 2026-07-10 (night) · **Purpose**: operator's "complete OR explicitly agreed-out" bar — every FR/NFR reconciled against merged code + live env, so UAT tests exactly what's built.
> **Basis**: this worktree = delivered state (PRs #628/#631 on master); post-remediation-wave (R-F1..R-F11). Evidence from 4 parallel adversarial recon agents + `notes/e2e-completion-audit-2026-07-10.md`.
> **Operator decisions applied (2026-07-10)**: F-3 job-aware = **PULL PRODUCER INTO R2**; #616 retrieval ACL = **AGREE-OUT to security project**.

## Headline
- **65 of 65 requirements accounted for.** No fabricated completions (adversarial audit).
- **DELIVERED: 58** (FR-P0 ×4, FR-A0 ×8, FR-A ×2, FR-A1 ×11 of 14, FR-B ×11 of 16, FR-D ×7, NFR ×11 of 14).
- **PARTIAL — must close for end-to-end (operator: pull in): 3** → FR-A1-07/NFR-09/NFR-12 (job-aware live producer), NFR-03 (shield activation), FR-A1-13 (create-matter live seed).
- **PARTIAL — NEW decision needed: 1** → FR-B-03 memory review/delete has NO client UI (spec says "user-VISIBLE"; gate rule says curl ≠ gate).
- **DEFERRED-BY-SPEC / agreed-out: 6 groups** (see bottom).
- **Gate-pending (code-complete, awaiting operator browser run): ADR-041 (FR-A1-14) @ 049, ADR-042 (FR-B-16) @ 069.**

---

## Load-bearing gaps to close for complete end-to-end UAT (operator confirmed in-scope)

| Item | FRs closed | Status | Work |
|---|---|---|---|
| **J1. Job-aware live producer** | FR-A1-07, NFR-09 (async half), NFR-12 | Router leg live (R-F3) but no capability emits job states through it | Wire the real chat doc-upload/create→indexing path's OutcomeCard through `ComposeForRoutedOutput`→`ComposeJobAware` so "indexing…" shows honestly. **Operator: YES, pull in.** |
| **J2. PromptShield activation** | NFR-03 (live content-safety), FR-A1-03 overlay input | Middleware built default-OFF (`51a490a60`); unfed probe | Merge shield PR + BFF deploy + spaarkedev1 activation (setting + ContentSafety endpoint + MI "Cognitive Services User" role). Already planned post-joint-deploy. |
| **J3. Create-matter live seed** | FR-A1-13 | Authored, inert (contract tripwire asserts NOT-yet-live) | DEF-003 7-step seed (Binding/Action rows + `ConsumerTypes.CreateMatter` + GU-065/066/067 flip). Planned at gate 049. |

## FR-B-03 memory review/delete UI — RESOLVED: AGREE-OUT (operator 2026-07-10)

FR-B-03 text: "user-**visible** review/delete surface." Delivered = 4 BFF endpoints only; zero SpaarkeAi client consumer. **Operator decision: AGREE-OUT — API-only satisfies r2; a client UI is a follow-on.** 069 exercises review/delete via API (documented exception to the "curl ≠ gate" rule, operator-signed). Recorded as an explicit agreed-out shortfall: the primary user control over automatic memory ships without an in-product surface in r2.

## Minor / measurement
- **NFR-14 latency** not re-measured in r2 (rides r1 posture; R-F1/R-F7 added per-turn envelope bind+render+token counting). → measure once on spaarkedev1 during UAT, or agree-out.

---

## Full FR/NFR disposition

### FR-P0 (discovery/measurement) — 4/4 DELIVERED
001 r1-P4 reconciliation · 002 prompt baseline · 003 schema-card determinism · 004 anchor verification. All contract/note-verified, not UAT-observable.

### FR-A0 (contracts) + FR-A (infra) — 10/10 DELIVERED
010 ComposeDisposition · 011 OutcomeCard · 012 GateDecision v2 · 013 TraceEvent · 014 JobAwareCompletionState · 015 ContextEnvelope · 016 MemoryItem · 017 seam ordering · 020 triple-twin hoist · 021 test-repair (+incidental EntityInfoWidget prod fix). All contract-tested + production-consumed (audit-confirmed no published-but-unused seam).

### FR-A1 (judgment/confirmation/completion) — 11 DELIVERED, 2 PARTIAL, 1 gate-pending
- DELIVERED: A1-01 resourcefulness doctrine · A1-02 resourcefulness evals · A1-03 gate engine live (residual: overlay input unfed → J2) · A1-04 origin evals · A1-05 pre-suspend validation · A1-06 completion+OutcomeCard+chips (R-F4 activated) · A1-08 UI-ack (residual #594) · A1-09 trace view · A1-10 progressive render · A1-11 refusal affordance (residual #591 markdown-not-card) · A1-12 capability endpoint (residual #592 no menu).
- **PARTIAL**: A1-07 job-aware → **J1**. A1-13 create-matter → **J3**.
- Gate-pending: A1-14 ADR-041 authored, Accepted flip @ 049.

### FR-B (memory + context) — 11 DELIVERED, 1 PARTIAL(UI), 2 interface/deferred, 2 spec-deferred
- DELIVERED: B-01 MemoryItemStore · B-02 envelope contracts · B-04 Context Binder consumed interactive+dispatch (R-F1/PE-D8b; residual #619a, #617) · B-05 budgets (R-F7 live) · B-06 caller-contact · B-07 fresh-retrieval bias · B-08 memory.write silent+live (row `2172b721` Active) · B-13 next-step chips (R-F4) · B-14 ACL spike (escalated→#616) · B-15 size-cap · B-16 ADR-042 (Accepted @ 069).
- **PARTIAL**: B-03 governance review/delete = **API-only, no UI** → NEW decision above.
- Interface-only (deferred wiring): B-11 organizational provider (runtime deferred-by-spec) · B-12 semantic provider (Binder wiring #617).
- **DEFERRED-BY-SPEC**: B-09 semantic-trust boundary · B-10 poisoning evals → governance project.

### FR-D (hardening) — 7/7 DELIVERED
D-01 publish harness (R-F10 CVE hard-stop) · D-02 eval gate · D-03 seam-fork · D-04 hygiene · D-05 audit re-key (live) · D-06 workspace-tool retirement (rows Inactive live) · D-07 orphans (index 404 live).

### NFR — 11 DELIVERED, 3 PARTIAL
- DELIVERED: 01 publish≤60 · 02 eval gate · 04 prompt stability (pins hold post-convergence) · 05 budgets · 06 governance minimal (API; see FR-B-03) · 07 counts-only telemetry · 08 UI-ack · 10 refusal affordance · 11 contract-first · 13 grep-zero retirement.
- **PARTIAL**: 03 content-safety live → **J2** · 09 async OutcomeCard + 12 ingestion-parity → **J1** (structural+seam only, no live producer) · 14 latency not re-measured (minor).

---

## Explicitly deferred / agreed-out (operator-confirmed)

1. **#616 retrieval ACL (HIGH security)** — matter-wall not enforced at retrieval; entity-TYPE not row-level memory read. **AGREE-OUT to separate security project** (operator 2026-07-10). Interim controls documented.
2. **Memory hard-governance (group e / FR-B-09/10)** — untrusted-origin ban enforcement, trust boundary, litigation-hold, poisoning evals → governance project; accepted residual risk (cross-session document-injection poisoning), operator-confirmed at spec + re-affirmed.
3. **Work IQ / Foundry IQ runtime + researcher spike** (FR-B-11) — interface ships, runtime deferred-by-spec.
4. **Semantic-slice Binder wiring** (#617/FR-B-12) — interface ships; conditional-trigger design is follow-on.
5. **Schema-card prose consolidation** (#619a/FR-B-04) — single-sourced via task-020 JSON; assembly into Business slice is follow-on.
6. **UX-polish deferrals** (system functions without): #592 soft-slash launcher menu, #591 structured-card refusal, #612 clickable upload link, #594 ack session-ownership.
7. **Close-out named groups (a–d, f)**: workspace-intelligence goal-tracking, admin observability dashboards, Spaarke-as-MCP-server, Insights-Engine→memory wiring.
8. **Repo-level handoffs** (not r2): ADR-007 Graph isolation + ADR-010 interface ceiling (charters ready); client ESLint v9 config gap.

---

## Path to complete end-to-end UAT
1. Build **J1** (job-aware producer) + **FR-B-03 UI** (pending decision) on branch; bundle with **J2** shield.
2. Compose joint deploy (theirs) → core BFF deploy (shield + J1 + memory UI) → **J3** create-matter seed → env verification.
3. One consolidated UAT script (merges 049+069): every scenario ↔ FR; explicit "not in this UAT" list = the agreed-out items above.
4. Operator UAT → ADR-041/042 Accepted → findings intake → 090 close with this table as the signed record.
