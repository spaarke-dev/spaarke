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

## W1 in progress
- **010 ✅** (merged via 3-way onto W0 — worktree had stale master base, reconciled cleanly; build+239 tests green). Landed `ICommunicationEnrichmentService` + `NormalizedMessage` skeleton, wired into both send paths + inbound processor, delivered outbound RAG indexing. Escalation triage:
  - **E2/E3 → task 011's job** (refactor `IncomingAssociationResolver`→engine over the envelope; centralize inbound so full direction-symmetry is atomic). Confirmed staging.
  - **E4/E6 resolved** (E4: RAG needs SPE ids → extra sprk_document retrieve, done; E6: stale-worktree-base, merged correctly).
  - **⛔ E1 (owner decision, non-blocking):** categorization (content-class + urgency) has NO schema home on `sprk_communication`. Options: add columns, OR accept it's subsumed by the FR-15 AI rung (which already outputs category+urgency) → step 2 redundant. Likely the latter.
  - **⛔ E5 (owner/coordination, for gated task 052):** `IEventRulesService.FireAsync` is chat/SSE-shaped — wrong for a fire-and-forget `communication_assessed` emission. 052 must design a non-SSE publish seam under `Services/Ai/PublicContracts/` (coordinate w/ r2-core; don't fork). FR-19 as written doesn't match the current seam.
- **Worktree-base lesson:** isolation:worktree agents branch from `master`, NOT my branch. Disjoint-file tasks (client/add-in/docs) merge clean; BFF-engine tasks that edit W0's files need a 3-way merge or should run in the **main session**. → Run 011+ (engine spine) in main session, serial.

## W2 / W7 parallel results (this session)
- **020 ✅** (`8e2baa85e`) — EmailComposer engine + 6 subcomponents + 18 smoke tests (agent worktree). Client TS, clean disjoint merge. Unblocks 021/041. Scope boundaries: saveDraft needs host `onSaveDraftRequest`; local-file upload deferred.
- **072 ✅** (build-verified) — Outlook add-in onto `@spaarke/auth` `OfficeNaaStrategy`. Agent found the task's named deprecated triad was ALREADY dead; the real ADR-028 violation was `shared/services/AuthService.ts` self-bootstrapping MSAL (NAA hard-disabled) — fixed it (rewrote as thin `SpaarkeAuthProvider`+`OfficeNaaStrategy` wrapper, `IAuthService` preserved so consumers unchanged). Unified manifest onto `outlook/manifest.json` (v1.0.20), deleted `outlook-manifest.xml`+orphan `manifest.prod.json`+dead `shared/api/*`, org-URL→`ORG_URL` env, new script-free `auth-callback.html`. −5,436 LOC. **⛔ FOLLOW-UP TO CLOSE 072: live NAA smoke-test needs real Outlook + Azure AD app-reg + BFF (via `office-addins-deploy`) — not doable in sandbox.** Minor: `initAuth()` public signature lacks a `strategy` override (used `new SpaarkeAuthProvider(config, strategy)` directly) — API-surface polish candidate.

## Follow-ups / debts to carry
- **006 dev smoke-test (REQUIRED before relying on threading):** the post-send Internet-Message-Id auto-capture uses a subject+recency Graph query (best-effort, non-fatal). Needs a dev-mailbox smoke-test to confirm hit-rate (R3 UQ3). Hardening path = correlationId extended property on send.
- **Formal gates batched to W0 PR:** code-review + adr-check + publish-size measurement for 004/006 deferred to the W0 PR gate (changes add no packages → publish delta ~0; ADR self-check: ADR-024 additive, ADR-028 injected client, ADR-010 no new DI, ADR-019 no new error path).
- **007 dead-code follow-ups (deferred, tracked):** orphaned `EmailProcessingOptions` props; `DeadLetterQueueService` lost its only consumer; ADR-045 background text slightly imprecise re Services/Email.
- **Solution-export** of new sprk_communication columns into managed Spaarke solution (ADR-027) — deploy-time.

## Recovery Notes
- Project initialized via `/design-to-spec` → `/project-pipeline` on 2026-07-14.
- W1‖W2‖W7 run after W0. **W5 gated on task 050 (r2-core Services/Ai coordination).**
- Before any BFF PR: run `/conflict-check`.
