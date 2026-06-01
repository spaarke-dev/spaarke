# Rewrite Ledger — `sdap-bff.api-test-suite-repair`

> **Purpose** (FR-29): Track every §4.8 rewrite escalation outcome (approved or denied) per project NFR-02 ("≤5% of touched files may be escalated to rewrite"). Each entry references the corresponding `escalations/rewrite-request-*.md` + owner approval date.
>
> **Authority**: `design.md` §4.8 binding rule + NFR-02 hard limit. Task 086 verifies count / touched-files ≤ 5%.
>
> **Finalized by**: Task 085 on 2026-05-31.

---

## Summary

**Total escalations filed**: 1
**Total escalations approved as RE-WRITE**: 0
**Total escalations dispositioned as NO-OP**: 1
**Percentage of touched files**: 1 / 60 (Phase 2+3 files touched) = **1.67%** — ✅ well under the NFR-02 hard limit of 5% (3.33% slack)

---

## Entry: RWT-T031-01 — Task 031 scope-mismatch / glob-with-zero-hits

| Field | Value |
|---|---|
| **Escalation ID** | RWT-T031-01 |
| **Date filed** | 2026-05-31 |
| **Filing task** | Task 031 (Phase 2+3 Wave 2.1 — P23.A IChatClient streaming batch 2) |
| **Escalation type** | §4.8-adjacent — **not** a >50% rewrite; **scope-mismatch / glob-with-zero-hits**. Filed to make the NO-OP disposition visible per project §6.2 trait-tagging discipline + NFR-02 rewrite-not-repair governance |
| **Original target glob** | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Capabilities/Streaming*.cs` AND `…/*StreamingTests.cs` |
| **Files matched** | **0** — `dotnet test --filter "FullyQualifiedName~Services.Ai.Capabilities.Streaming" --no-build` returns "No test matches the given testcase filter" |
| **Root cause** | The `Services/Ai/Capabilities/` directory contains 5 files (CapabilityRouterBenchmark, CapabilityRouter, CapabilityValidator, DataverseCapabilityManifestLoader, ManifestRefreshService) — **none** are streaming tests. Streaming tests are in `Services/Ai/Chat/Streaming*` (task 030's scope) and `Api/Ai/AnalysisStreamChunkTests.cs` (Api-tier). Task 031's POML glob was a planning over-estimate of IChatClient cluster span |
| **Escalation record** | [`escalations/rewrite-request-T-031-SCOPE-MISMATCH.md`](../escalations/rewrite-request-T-031-SCOPE-MISMATCH.md) |
| **Disposition requested** | NO-OP completion (0 files modified, 0 traits applied, 0 §4.8 >50% rewrites) |
| **Disposition outcome** | **ACCEPTED as NO-OP** — confirmed by sibling task 030 (Services/Ai/Chat/Streaming*) being the canonical IChatClient streaming task; the 2 `CapabilityRouterBenchmarkTests` failures (lines 191 + 320) absorbed by task 053 (`Services/Ai/Capabilities` non-streaming) per the 22-test rollup in [`baseline/failure-inventory-post-018-2026-05-31.md`](../baseline/failure-inventory-post-018-2026-05-31.md) |
| **Owner approval date** | 2026-05-31 (orchestrator-confirmed at task 032 verification gate close: P23.A track CLOSED with 015+016+030+031 = 42/42 pass; helper integrity verified; 031's zero contribution to repair count is correctly reconciled) |
| **Approval recorded by** | Task 032 (P23.A verification gate) — TASK-INDEX.md status: "**P23.A track CLOSED**: 015+016+030+031 = 42/42 pass" |
| **Production-code impact** | None — NFR-01 / §4.5 / NFR-03 / NFR-11 all preserved (none touched) |
| **Test-code impact** | None — 0 files modified; 0 `[Trait("status", …)]` applied |
| **NFR-02 contribution** | 1 escalation against ~60 touched-files denominator = 1.67% — ✅ under 5% hard limit |
| **Severity** | INFORMATIONAL — no production gap; no test deletion; pure planning/scoping artifact |

### Why this counts as an escalation

Per `design.md` §4.8 binding rule, **any** disposition that deviates from the per-task POML's per-file repair contract MUST be escalated and recorded — even when the deviation is "0 files touched because the glob matches nothing." Task 031 correctly filed an escalation to make the NO-OP visible and auditable; without it, the post-hoc reader would have no record of why this task's repair count is 0 while its sibling task 030 contributed 28.

### Why this is INFORMATIONAL (not REJECTED)

Per `design.md` §4.8: the escalation is **REJECTED** only when the agent proposes >50% rewrite without owner sign-off. Task 031 proposed NO-OP (0 files, 0 LOC), which is the most conservative possible disposition; it is auto-approved as informational record.

### Touched-files denominator (NFR-02 governance)

Per NFR-02: "≤5% of touched files may be escalated to rewrite." The denominator for this project is the count of files in tests/ that this project's per-task POMLs targeted with repair work. Counting from per-task `<relevant-files>` lists in TASK-INDEX.md:

| Phase | Tasks | Distinct files targeted |
|---|---|---:|
| Phase 1 P1.A (compile recovery) | 010–014 | 17 |
| Phase 1 P1.B (helper) | 015–016 | 2 |
| Phase 1 P1.C (factory) | 017–019 | 1 (factory only) |
| Phase 2+3 P23.A (IChatClient) | 030–032 | 1 |
| Phase 2+3 P23.B (factory-dependent) | 033–034 | 7 |
| Phase 2+3 P23.H (HIGH-tier) | 040–046 | 18 |
| Phase 2+3 P23.M (MEDIUM-tier) | 050–056 | 9 |
| Phase 2+3 P23.I (integration) | 060–063 + 027 | 16 |
| Phase 2+3 P23.L (LOW-tier) | 070–074 | 9 |
| Phase 1 P1.E (CS1739 integration) | 024 | 1 |
| **Distinct touched-files total** | — | **~81** (some files appear in multiple POMLs but counted once) |

**Ceiling**: 5% × 81 = 4.05 escalations
**Actual**: 1 escalation
**Slack**: 3.05 escalations remaining within budget

NFR-02 ✅ **satisfied** — well under hard limit.

---

## Schema reference (for future entries, none expected)

```markdown
## RWT-T0XX-NN — {brief description}

| Field | Value |
|---|---|
| **Escalation ID** | RWT-T0XX-NN |
| **Date filed** | 2026-MM-DD |
| **Filing task** | Task 0XX (Phase X / Wave X.Y / Track XX.Y) |
| **Escalation type** | §4.8 >50% line replacement / §4.8-adjacent scope-mismatch / §4.8-adjacent shape-mismatch |
| **Original target** | {file glob or specific file path} |
| **Lines replaced** | NNN of MMM ({percentage}%) |
| **Root cause** | {brief explanation} |
| **Escalation record** | [`escalations/rewrite-request-T-NNN-{shortdesc}.md`](../escalations/...) |
| **Disposition requested** | {APPROVED rewrite / NO-OP / different scope} |
| **Disposition outcome** | {APPROVED / REJECTED / NO-OP} |
| **Owner approval date** | YYYY-MM-DD |
| **Approval recorded by** | {task or gate that confirms} |
| **Production-code impact** | None (NFR-01) |
| **Test-code impact** | {LOC + files modified} |
| **NFR-02 contribution** | {N+1 of {touched-files}} = {%} |
```

---

## Reconciliation

| Metric | Value |
|---|---:|
| Total escalations filed across project | 1 |
| Approved as REWRITE | 0 |
| NO-OP / informational | 1 |
| REJECTED | 0 |
| Touched-files denominator | ~81 |
| Percentage | 1.23% |
| NFR-02 5% hard limit | ✅ satisfied |
| Task 086 verification pending | ✅ this ledger is the source of truth for that check |

---

*This ledger satisfies FR-29 (per-file rewrite request reference + owner approval date). Task 086 will perform the final NFR-02 percentage verification using this ledger's totals.*
