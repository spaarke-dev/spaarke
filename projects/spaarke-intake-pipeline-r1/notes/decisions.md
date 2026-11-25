# Decision log — spaarke-intake-pipeline-r1

The full rationale for each decision lives where it was made; this log indexes them. Binding one-line outcomes are in `CLAUDE.md` §3.

| Date | Decision | Where recorded |
|---|---|---|
| 2026-11-25 | Q2: orchestration = BFF `IJobHandler` jobs + run record; ADR-052 written reason; Durable Task not used | design.md §4, §13 |
| 2026-11-25 | Q3: Python engines in a Container Apps Job, Conditions 1–4; C# port and ComposeShadowPatchEngine rejected | design.md §5 |
| 2026-11-25 | Q4: invoice harness in Phase 3 after eBilling UAT; freeze on `Services/Finance/Submission/` | design.md §7 |
| 2026-11-25 | Q5: sending identity = shared legal mailbox, app-only | design.md §3.5 |
| 2026-11-25 | Q6: NDA standard = accelerator v1.2, pinned, version-stamped; counsel ratification gates customer rollout | design.md §5 |
| 2026-11-25 | Q7: intake mailboxes + suggest-only at other mailboxes | design.md §3.1 |
| 2026-11-25 | §10.1 cleanup split across Phase 0 / 1 / 2; item 5b → #3162 | design.md §10.1, notes/section10-verification-2026-11-25.md |
| 2026-11-25 | ADR-004 Amendment A2 (path B): queue consumers outside the BFF | docs/adr/ADR-004-async-job-contract.md |
| 2026-11-25 | Spec interview: r1 = Phases 0–3; approve from Console and MDA (one endpoint); team queue then claim; manual triage; any sender + cap; preserve prior markup; per-status email opt-in; suggestion = Job B proposal kind | spec.md "Owner Clarifications" |
