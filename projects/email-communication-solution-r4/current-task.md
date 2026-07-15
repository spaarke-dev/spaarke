# Current Task — email-communication-solution-r4

> **Purpose**: Active task state tracker for context recovery. Reset by `task-execute` on each task transition.

---

## Active Task

- **Task**: none active — **W0 Foundation COMPLETE (001–007 all ✅)**
- **Status**: W0 unblocks everything. Next: **W1** (serial spine, starts task 010 — opus tier) ‖ **W2** (task 020, client composer) ‖ **W7** (parallel hardening track).
- **Next Action**: begin W1 task **010** (`ICommunicationEnrichmentService`, opus/xhigh) and/or W2 task **020** (client `<EmailComposer/>`). Run `/conflict-check` before each BFF PR. W5 remains gated on task 050 (Services/Ai r2-core coordination).

## W0 Commits (this session)
- **007** `051a098d2` — retire OOB-`email` (partial: 3 shared-infra files retained; −3.06 MB)
- **003** `89e599293` — `sprk_servicerequest` association target (+ data-model doc)
- **004 + 006** `bbffb4532` — `sprk_event` + org/account domain match; send-path thread-id capture
- (001, 002, 005 committed earlier session)

## Key W0 decisions (owner-confirmed 2026-07-14)
- **Attachment field: NO rename.** `AttachmentDocumentIds` correctly carries Dataverse **Document GUIDs** (email attachments are always tracked Documents; server resolves each to its SPE File). The R3 "rename to DriveItemIds" premise was a misread. DocumentEmailWizard is **correct**, not buggy. Ripples: FR-13 (022) + FR-21 (060) carry the same corrected premise — do NOT re-introduce the rename. See [[email-r4-attachment-id-semantics]].
- **org vs account: distinct, never mixed.** `sprk_organization` = legal entity → `sprk_regardingorganization`; OOB `account` = vendor/payment → `sprk_regardingaccount`. Domain match writes both, each to its own lookup, matched by `sprk_domain` (owner added to both tables). See [[sprk-organization-vs-account]].
- **007 partial retirement accepted.** 3 files in `Services/Email/` are live shared infra (Office worker + inbound pipeline + RAG) — retained by design.

## Owner-created Dataverse fields (this session)
- `sprk_communication`: `sprk_regardingservicerequest` (001), `sprk_regardingaccount`, `sprk_regardingevent`
- `sprk_organization`: `sprk_domain`; `account`: `sprk_domain`

## Follow-ups / debts to carry
- **006 dev smoke-test (REQUIRED before relying on threading):** the post-send Internet-Message-Id auto-capture uses a subject+recency Graph query (best-effort, non-fatal). Needs a dev-mailbox smoke-test to confirm hit-rate (R3 UQ3). Hardening path = correlationId extended property on send.
- **Formal gates batched to W0 PR:** code-review + adr-check + publish-size measurement for 004/006 deferred to the W0 PR gate (changes add no packages → publish delta ~0; ADR self-check: ADR-024 additive, ADR-028 injected client, ADR-010 no new DI, ADR-019 no new error path).
- **007 dead-code follow-ups (deferred, tracked):** orphaned `EmailProcessingOptions` props; `DeadLetterQueueService` lost its only consumer; ADR-045 background text slightly imprecise re Services/Email.
- **Solution-export** of new sprk_communication columns into managed Spaarke solution (ADR-027) — deploy-time.

## Recovery Notes
- Project initialized via `/design-to-spec` → `/project-pipeline` on 2026-07-14.
- W1‖W2‖W7 run after W0. **W5 gated on task 050 (r2-core Services/Ai coordination).**
- Before any BFF PR: run `/conflict-check`.
