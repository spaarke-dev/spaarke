# Current Task State — Spaarke AI Architecture Redesign R2 (Core)

> **Last Updated**: 2026-07-10 (late night) — ALL SUBSTANTIVE WORK COMPLETE; awaiting compose wrap-up for one combined deploy + UAT.
> **Recovery**: Read Quick Recovery first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **State** | **All substantive r2 work is DONE.** Spec-vs-built reconciliation complete + every decision operator-signed. Last code = PR #633 (merging on CI green). Remaining = purely mechanical: one COMBINED deploy (with compose) + operator UAT + 090 close. |
| **Deploy plan** | **Option 1 (operator-chosen 2026-07-10): one combined deploy + one UAT.** Core holds its activation (shield + create-matter seed) until compose's wrap-up (DEF-09/08/02/071) lands. Then ONE deploy from master carries both projects; operator runs the consolidated UAT once. |
| **#633** | PromptShield (default-OFF) + create-matter ConsumerType code + spec reconciliation + consolidated UAT checklist. Compose cleared it (zero conflict). Merging on green (watcher `bx1wnh6oa`). |
| **Waiting on** | (1) compose pings when their wrap-up master is final + says who runs the combined deploy; (2) operator runs the UAT after the combined deploy. |
| **Next action (on compose "wrap-up final" ping)** | Land the **seed-time PR**: insert create-matter row into `infra/dataverse/sprk_playbookconsumer-rows.json` + remove `LiveBindingMirror_DoesNotYetContainCreateMatter` tripwire. Then at the deploy window: seed create-matter Action+Binding rows (→ healthz Healthy) + activate PromptShield (App setting `AiSafety:PromptShield:ChatPipelineEnabled=true` + ContentSafety endpoint + MI "Cognitive Services User" role). |
| **Then** | Operator runs `notes/CONSOLIDATED-UAT-CHECKLIST.md` (Parts A/B/C) → clean pass promotes ADR-041 + ADR-042 → 090 close. |

### Key artifacts (this session)
- `notes/spec-vs-built-reconciliation-2026-07-10.md` — all 65 FR/NFR dispositioned; 58 delivered; signed agree-outs (job-aware invariant delivered/card agreed-out, FR-B-03 API-only, #616 security-project, FR-30/#629 governance-project).
- `notes/CONSOLIDATED-UAT-CHECKLIST.md` — one browser run (A1-A10 judgment, B1-B7 memory, C1-C3 shield/create-matter) + signed "NOT in this UAT" list.
- `notes/e2e-completion-audit-2026-07-10.md` — the adversarial audit that started this (no fabricated completions; F-1..F-11 all fixed).
- `notes/629-fr30-triage-2026-07-10.md` — #629 → memory hard-governance project.
- `notes/adr-archtest-handoff-charters.md` — ADR-007/ADR-010-ceiling → CI/hygiene project.

### Remediation wave (post-audit) — ALL MERGED
F-1..F-11 fixed: envelope convergence (interactive+dispatch render), user-memory recall, live budgets, gate safety-perimeter probe, chips activation, TZ pin, CVE hard-stop, AuditLog flake root-fix, all pre-existing test debt (#621 fixture-artifact→closed, #618→closed), CI honesty (TRX + job-level swallow removed, #628/#631). 3 ADR quick wins in r2; 2 handed off. Master CI confirmed honest-green with blocking.

### Deferred / agreed-out (operator-signed, filed at 090)
#616 retrieval ACL (security project), memory hard-governance group-e (governance project), FR-B-03 memory UI (API-only), job-aware chat card (invariant delivered elsewhere), #629 FR-30 (governance project), #592/#591/#612/#594/#617/#619a (issues), Work IQ runtime, close-out groups a/b/c/d/f.

### Coordination state
- Compose joint deploy done (#632). Compose mid-flight on client-only DEF-09/08 (DEF-08 adds one additive `SendWorkspaceArtifactHandler` server-resolved seed — core endorsed, no core change). Session-identity: core confirmed it will NOT unify (two-session model safe). Ack: SpaarkeAi client DOES ack workspace-tab frames (core corrected compose's note); compose's 071 extends to content-render.
- Handoffs exchanged: `REPLY-from-core-option1-combined-deploy` + `HANDOFF-from-core-session-identity-and-seed-ack` in compose notes.

*Resume: on compose "wrap-up final" ping → seed-time PR + combined-deploy activation. Else await operator UAT.*
