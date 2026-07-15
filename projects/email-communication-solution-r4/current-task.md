# Current Task — email-communication-solution-r4

> **Purpose**: Active task state tracker for context recovery. Reset by `task-execute` on each task transition.

---

## Active Task

- **Task**: none active — **parallel wave complete: 012 ✅ (`6a1a513cb`), 021 ✅ (`9d5cffa17`), 022 ✅ (`4ded53c0a`), 070 ✅ (`747698ef0`), 071 ✅ (`f7067d6e2`).** **18 of 45 tasks done.**
- **Status**: W1 rungs 0–1 (012) done → **013 (rung 2) unblocked, next in main session; then 014 (rung 3); then 015 (confidence→status + auto-file).** W2 client (020/021/022) done → unblocks 040/041/060/061. W7: only **075** remains (index-config, Services/Ai hot path — needs main-session /conflict-check with r2-core; NOT dispatched as agent).
- **Next Action**: **013** (rung 2 — participant correlation: replace the kept `ParticipantCorrelationRung` adapter with real recipient-side + org-by-domain content, all 8 targets, direction-symmetric). Main session (shared engine edit point). Combined 011+012+071 Communication suite = 254 green.
- **Rigor**: FULL · 013 = sonnet @ high · directional.
- **Master**: `origin/master` at fb4012cb3 (through 076). Branch now well ahead (011/012/021/022/070/071 + tracking) — merge to master again before the NEXT agent wave to keep agents on a current base.
- **Merged to master ✅ (2026-07-15)**: `origin/master` fast-forwarded `240d0e5c5..fb4012cb3` — all 19 branch commits now on remote master. Future agent worktrees branch from `origin/master` (confirmed pattern: 073 branched from origin/master's value, not local master) → **stale-base 3-way tax eliminated.** (Local master ref in `C:/code_files/spaarke` left at `bcc15973a` — FF blocked by pre-existing untracked files there; cosmetic, does not affect agent branching.)
- **Next Action**: next wave — **012→013→014 serial in main session** (all edit the engine's shared `_rungs` composition point; not cleanly parallel regardless of base) ‖ **021/022 (W2) + 070/071/075 (W7) as clean worktree agents** (now branch from current master).
- **Rigor Level**: FULL · **Model**: opus @ xhigh · **Step mode**: directional (R-7 order binding).
- **011 scoping decision (directional)**: engine refactor is **inbound-only** in 011. Running the engine over OUTBOUND via `EnrichAsync` would change outbound behavior (client-supplied associations) and needs direction-aware rung content (012/013/015) — deferred there. 011 delivers: finalized envelope + rung abstraction + Graph→envelope normalizer + refactored inbound engine, behavior preserved. Enrichment `RunAssociationAsync` stays a documented seam (doc updated).

## 013 results (rung 2 — participant correlation) — gate clean, committing
- **NEW**: `Engine/Rungs/ParticipantCorrelationRung.cs` (from/to/cc; sender→contact[0.70]+memberships[0.80]+domain org/account[0.65]; recipients→memberships[0.70]; dedup by field+targetId keep-highest; recipient cap 25). **NEW Dataverse query**: `QueryContactMembershipsAsync` (sprk_userentityassociation junction, personidtype=2 Contact) in all 3 Spaarke.Dataverse files. **Deleted** 011 adapter `Engine/ParticipantCorrelationRung.cs`. Tests: `ParticipantCorrelationRungTests` (7, incl. FR-04 org-target guard + symmetry).
- **261 Communication tests green** (011 baseline preserved). Build clean; publish 45.26 MB (~0 delta); CVE unchanged. Gate: **0 Critical, ADR-024/010/032/045 PASS, FR-04 org-target=sprk_organization CORRECT, no 011 regression**. S1 (recipient cap) applied.
- **FR-04/DEC-3**: org→`sprk_organization`, account→`account`, separate lookups; a test proves no path writes `account` into the org lookup.
- **Note (S4)**: membership matches are dormant until R3 membership Phase-2 populates `sprk_userentityassociation`; query is correct + graceful-when-empty (degrades to person+domain).

## 🔑 CARRY-FORWARDS FOR TASK 015 (confidence→status + auto-file) — BINDING
1. **Engine apply-loop conflict consumption (gate W1, from 013)**: `IncomingAssociationResolver` currently applies matches via `fields[field] = target` (last-wins), which SILENTLY COLLAPSES conflicting same-field matches that rung 2 correctly surfaces (two participants → two different matters). 015 MUST change the apply-loop to collect `List<RungMatch>` per field and drive **Ambiguous** status on conflict (per spec Ambiguous logic) — otherwise the ambiguity signal is thrown away.
2. **Confidence-based arbitration, not rung-order (from 012 W1)**: 015 must pick the winning association by CONFIDENCE across rungs, not first-rung-wins. Confidences already assigned: caller-supplied 1.0 > thread 1.0 > subject-token 0.9 > rung-2 (0.60–0.85) > (rung 3 0.70–0.95). This makes thread outrank a subject-regex token (resolves the 012 owner heads-up) and sets the ≥0.85 auto-file gate. AI rungs (4–5) NEVER auto-file (always Suggested/Ambiguous).

## 012 results (rungs 0–1) — gate clean, committing
- **NEW**: `Engine/Rungs/ExplicitReferenceRung.cs` (rung 0), `Engine/Rungs/ThreadContinuityRung.cs` (rung 1), `Engine/RegardingFieldMap.cs`. **Modified**: `AssociationContext` (+`CallerSuppliedRegarding`), `IncomingAssociationResolver` (DI-injected `IEnumerable<IAssociationRung>`; now consumes `RegardingFieldMap.All` — W3 dedup), `CommunicationModule` (3 rung regs). **Deleted**: 011 adapters `Engine/{ThreadContinuityRung,SubjectReferenceRung}.cs` (kept `ParticipantCorrelationRung` → 013). Tests: `ExplicitReferenceRungTests`, `ThreadContinuityRungTests` (+direction-symmetry); 3 engine-ctor sites updated.
- **244 Communication tests green** (011 characterization baseline preserved). Build clean; publish 45.25 MB (0 delta); CVE unchanged. Step 9.5 gate: **0 Critical, ADR-check PASS, no 011 regression**.
- **Gate warnings actioned**: W3 (resolver now consumes shared `RegardingFieldMap`), S1 (subject-token confidence 1.0→0.9, heuristic vs caller-supplied), S2/S3 (doc fixes).
- ⚠️ **W1 (OWNER HEADS-UP — filing behavior change)**: subject explicit-ref (rung 0) now precedes thread continuity (rung 1). A reply inside a thread already filed to Matter A whose subject contains `MAT-999` now files to **Matter 999**, overriding thread inheritance. This follows FR-10's rung-0 taxonomy (explicit reference = highest determinism) and the POML (subject-regex is part of rung 0); the reliability trade-off (thread vs subject regex) is definitively resolved by **task 015**'s confidence→status ladder (subject-token already lowered to 0.9). Flag if you want thread to outrank subject-regex instead — that's a one-line reorder.
- **W2 (intentional)**: thread rung now copies ALL 11 regarding fields from the parent (was 3: matter/org/person) — per FR-10 "inherit the thread's regarding across all targets."
- **Deferred (per 011 note)**: engine outbound production invocation stays `CommunicationService.MapAssociationFields`; rung 0's caller-supplied branch is implemented + tested but production-dormant until 015/017 wires it. Gate agreed this is correctly deferred.

## 011 implementation results (pre-commit — awaiting Step 9.5 gate)
- **New**: `Engine/{RungKind,RungMatch,AssociationContext,IAssociationRung,GraphMessageNormalizer,ThreadContinuityRung,ParticipantCorrelationRung,SubjectReferenceRung}.cs`. **Refactored**: `IncomingAssociationResolver.cs` (engine over envelope; ADR-024 write path preserved verbatim; dropped `IGraphClientFactory`). **Updated**: `IncomingCommunicationProcessor.cs` (normalize at boundary, Select `internetMessageHeaders`+`conversationId`, reuse envelope for enrichment, removed `BuildInboundEnvelope`), `CommunicationEnrichmentService.cs` (association-seam doc), `CommunicationModule.cs` (+`GraphMessageNormalizer` singleton), `Models/NormalizedMessage.cs` (finalized). **Tests**: migrated `IncomingAssociationResolverTests` to envelope (assertions verbatim), new `GraphMessageNormalizerTests`, ctor fixes in `InboundPipelineTests` + `CommunicationIntegrationTests`.
- **R-7**: baseline 10/10 green pre-refactor → **234 Communication tests green post-refactor** (identical write-contract assertions). Behavior contract: `notes/011-resolver-behavior-contract.md`.
- **§10**: build clean (0 err); publish **45.25 MB compressed incl PDBs** (~0 delta vs ~49.63 baseline; ≤60 ceiling); CVE = 1 **pre-existing** High (`Microsoft.Kiota.Abstractions 1.21.2`, transitive via Graph, inherited from master — NOT introduced; 0 packages added). Placement Justification: refactor-in-place of existing `Services/Communication`, no new endpoints/packages, +1 unconditional DI reg (normalizer).

## 073 (W7) — parallel agent DONE, patch staged for merge
- Agent committed `d7411989a` (off stale master 240d0e5c5). Wired ALL mapped Office endpoints (9 baseline + 2 job-ownership + 1 entity-access; 0 `TODO: Task 033` left). 155 Office tests green; 0 Critical/0 ADR violations. Patch at `/c/tmp/073.patch` (3 files: OfficeEndpoints.cs + 2 tests) — apply via 3-way after 011 commit.

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
