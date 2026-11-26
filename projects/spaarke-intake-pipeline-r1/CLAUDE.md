# Spaarke Intake Pipeline R1 — project operating manual

> Read with `current-task.md` (current state). Repo-wide rules are in root `CLAUDE.md`; this file holds only what is specific to this project. Guidance: `.claude/skills/project-setup/references/claudemd-template.md`.

## 1. Scope and status

This project delivers one request pipeline:

- **Doors:**
  - Front Door
  - Console submit
  - intake mailboxes, plus a suggest-only "Submit as request" proposal at other mailboxes
  - Copilot through MCP tools
  - later, the invoice routes
- **Flow:**
  1. The door creates a request (`sprk_servicerequest`).
  2. A BFF orchestrator records each pass as a run (`sprk_processingjob`).
  3. The run is routed by `requestType` to a harness.
  4. An attorney approves it.
  5. It returns through the door it came in by.
- **Harnesses:**
  - **NDA review (first):** an app-only edit plan, then the accelerator's Python redline engines in a Container Apps Job.
  - **Invoices (Phase 3):** after eBilling UAT.
- **Cleanup:** the design §10.1 defects are fixed along the way.
- **Out of scope:**
  - Phase 4 harnesses (r2);
  - Compose as the NDA front end;
  - new review AI or attorney profiles;
  - auto-send, or sending as the attorney;
  - email joining the ingestor seam (#3162);
  - editing the NDA standard;
  - Durable Task.

Status: see `tasks/TASK-INDEX.md` and `current-task.md`. Spec: `spec.md`. Design: `design.md` (v0.3). Plan: `plan.md`. Reuse verdicts: `notes/reuse-scan.md`.

## 2. Binding rules for this project

**Orchestration and runs**
- The orchestrator is a BFF `IJobHandler` over Service Bus. Each stage is idempotent: a resumed run never re-runs an AI step, and a duplicate delivery never produces duplicate outputs. The terminal state is written last (FR-07, NFR-01).
- Run state lives on `sprk_processingjob` (extended, not replaced); the request is `sprk_servicerequest` (FR-05, FR-06).
- Harnesses are selected by a `requestType` routing key added to `RoutingContext`/`IRoutingContext` (`Services/Ai/PublicContracts/IConsumerRoutingService.cs`). No harness, or classification confidence below the threshold → manual triage, never a guess (FR-08, FR-10).

**NDA harness and engines**
- The engines are a **pure engine** in a Container Apps Job:
  - it runs as its **own** least-privileged managed identity, never the stamp/BFF identity (it parses untrusted .docx): its two queues, the staging container and AcrPull, nothing else. It makes no BFF calls, sends completions only to its own `intake-engine-completions` queue (never a BFF queue: Sender is per queue, not per message type), and the BFF treats each completion as untrusted input. It is an ADR-004 A2 queue consumer (envelope, atomic blob marker, deterministic output paths, `correlationId`, outcome, `MaxDeliveryCount` → DLQ) (FR-01);
  - document content never travels in a message: blob staging plus ids-only queue messages. Staging is stamp-private with a 7-day lifecycle TTL (NFR-03, ADR-004).
- The engine image is built from `src/accelerators/core/engines/` on master (after #3163), on Python 3.11. The engines use stdlib + `lxml` only. The entrypoint layer alone uses the hash-pinned Azure SDKs (`azure-identity`, `azure-servicebus`, `azure-storage-blob`) for lock renewal, explicit dead-lettering and conditional blob writes (owner 2026-11-25: the correct approach, not hand-written REST). **Never copy the engines into this project** (FR-01).
- Existing redlines and comments in a draft are **preserved**: never accepted and never clobbered. An edit that crosses an existing revision becomes a comment plus a "manual" clause-table row (FR-09). The redline and external outputs lose no edit and never auto-accept; the **Clean copy accepts all revisions from all sides** (owner 2026-11-26, Unresolved Q1 resolved).
- **Refused edits (owner 2026-11-26, "A with B as backup"):** on master the engine refuses the whole plan (exit 3) when one edit cannot be placed. (A) the accelerator is asked for a skip-and-report mode; (B) the NDA harness gives each refused edit one AI rewrite, then makes it a comment plus a manual row. Until A ships, the engine job ends a refused plan with the non-retryable outcome `plan_does_not_fit` (refused edit index + reason code only). Engine stderr quotes clause text: never log or forward it (ADR-015).
- **Clause anchoring is deterministic, never probabilistic (owner 2026-11-26; ADR-049 I-7 lesson, nda-r1 "reported, not mis-anchored").** The draft is segmented by the accelerator's `segment_clauses.py` (engine job `segment` operation) BEFORE the AI runs; the AI picks `para` only from that closed `para_key` list and quotes `find`/`old`/`after` verbatim; the BFF pre-checks every anchor by exact match (para in list, text exactly once, word boundaries) and the engine is the final authority. A failing anchor is a refused edit (one AI rewrite, then a manual row), never placed by fuzzy, approximate, "closest" or whole-document text search. The AI decides WHAT to change and WHICH clause; code decides WHERE.
- The NDA standard is consumed at a pinned version, and every output is stamped with that version (NFR-09).

**Approval and return**
- AI output never leaves without attorney approval. Approve and reject go through **one** BFF endpoint, called from both the Console Work Item and the model-driven form. Both write the Decision Record. The endpoint fails closed on authorization (FR-11, NFR-02).
- Email returns are a threaded reply from the **shared legal mailbox**, app-only (`SendMode.SharedMailbox`). Never send as the attorney, and never use `SendMode.User` from a job: it needs an in-request `HttpContext` (FR-20).
- A status email goes to the requester at each status change only when the request's "send email updates" is on (FR-12).

**Intake mailboxes**
- Intake mailboxes accept any sender, subject to a per-sender daily cap; mail over the cap is filed with no run (NFR-04).
- At ordinary mailboxes, only a Job B proposal is made, never an automatic request (FR-17(d), FR-18).

**Governance**
- Each door and each harness has a kill switch (ADR-032) (NFR-08).
- Dev and CI use **synthetic NDAs only**. Real NDAs and the accelerator's `answer-key/` are never readable by an agent or a test (NFR-10).
- BFF tasks state a Placement Justification in the PR (`.claude/constraints/bff-extensions.md`) and verify publish size ≤ 60 MB with no new HIGH CVE (NFR-07, root §10).
- Deferred work and newly found issues: `/project-defer-issue-tracking` writes `notes/defer-issues.md` AND a GitHub issue — never one without the other.

ADR tensions approved for this project (root CLAUDE.md §6.5; full table in `spec.md` "ADR Tensions"):
- **ADR-004 `IJobHandler`** — Path B: Amendment A2 (queue consumers outside the BFF), drafted on this branch. It **merges before or with** the first FR-01 container code.
- **ADR-052** — Path C: the orchestrator in the BFF has a written reason (design §4); the Container Apps Job is owner-approved (design §5).
- **ADR-004 payload** — Path C: claim-check blob staging, ids-only messages.
- **ADR-049 I-5/I-7** — Path C: Compose-scoped. The engine anchors exactly once and refuses otherwise (no fuzzy text search), and the outputs are not authored through Compose. Confirm this in `/adr-check` (spec Unresolved Q2).
- **ADR-028 A5, ADR-039, ADR-045, ADR-002** — Path C: app-only with human approval before send; catalogued Actions only, with the per-sender cap as the Event-path budget; server send through `CommunicationService`; request and run invariants owned by the BFF write path.

## 3. Owner directives and standing decisions

All dated 2026-11-25; rationale in `design.md` §13/§10.1, `spec.md` "Owner Clarifications" and `notes/decisions.md`.

**Design decisions (§13 Q2–Q7)**
- Q2: orchestrate with BFF jobs plus the run record; no Durable Task.
- Q3: Python engines in a Container Apps Job; the C# port and ComposeShadowPatchEngine were rejected.
- Q4: invoices in Phase 3, only after eBilling UAT sign-off.
- Q5: the sending identity is the shared legal mailbox.
- Q6: the NDA standard is the accelerator's, consumed pinned at v1.2. Counsel ratification gates customer rollout.
- Q7: intake mailboxes, plus suggest-only Job B at other mailboxes.

**Scope and workflow (spec interview)**
- r1 = Phases 0–3.
- Approve from the Console AND the model-driven form, through one endpoint.
- Approval goes to the team queue; whoever claims it becomes the assigned attorney.
- No matching harness → manual triage.
- Intake senders = anyone, with a per-sender cap.
- Preserve all prior redlines and comments.
- Status on every step, plus Decision Record entries plus an optional email per status change.

**Cleanup (§10.1)**
- **Phase 0, one PR:** items 1b, 2, 4, 5a, 6, 7, and the docs part of 8.
- **Phase 1:** item 3 (DLQ re-drive), and the `sprk_backgroundjob` description part of 8.
- **Phase 2:** item 1a.
- **Filed:** item 5b → #3162.
- No hygiene-only PRs.

## 4. Coordination

- **Accelerator (`spaarke-legal-ai-accelerator-r1`, Desktop session "acclerator-r1"):**
  - owns the engines and the NDA standard;
  - PR #3163 moves the engines to master and must merge before the FR-01 image build;
  - flag any CLI-contract change by message;
  - do not edit their worktree.
- **`ebilling-r1`:** owns `Services/Finance/**`. **No edits to `Services/Finance/Submission/` before eBilling UAT sign-off** (task 163).
- **`spaarke-ontology-platform-r1`:** owns terminology and the ontology columns on `sprk_servicerequest` (direction, disposition, due date). Coordinate before FR-05 (spec Unresolved Q3).
- **`spaarke-SPA-external-access-platform-r4`:** shares `Api/ExternalAccess/ExternalIntakeEndpoints.cs` and the External SPA (FR-13).
- **`spaarkeai-mcp-bff-api-r1`:** shares the MCP server under `Api/Mcp/` (FR-21). Follow its tool pattern; don't change the host.
- **`customer-provisioning-orchestration-r1`:** owes the Container Apps environment, the Job and the registry per stamp (a provisioning step) and the mailbox RBAC.
- **`specialist-legal-agents-r1`:** the origin project; it owns review AI and attorney profiles (out of scope here).
- **Open PR #1494** (notification delivery) touches `IncomingCommunicationJobHandler.cs`. Rebase over it if it merges first.

## 5. Environment and live actions

- Dev environment only. Deploy the BFF with `/bff-deploy` only (never raw `az webapp deploy --clean`).
- Need the owner's explicit OK:
  - creating Azure resources (Container Apps environment, Job, registry, storage lifecycle rule, Service Bus queues);
  - Exchange/mailbox RBAC for the shared mailbox;
  - Dataverse schema changes on dev;
  - any email send to a real address.
- Key Vault: read `.claude/constraints/provisioning.md` "KV credential lifecycle" before any secret change (root §9).

## 6. Gotchas — do not re-learn

All dated 2026-11-25.
- **Email:**
  - custom Graph `internetMessageHeaders` must start with `x-`;
  - there is no `createReply` anywhere in the codebase yet (FR-03 item 7 adds threading);
  - `CommunicationService` caps total attachments at 35 MB (`MaxTotalAttachmentSizeBytes`).
- **Existing names that mislead:**
  - `sprk_autocreaterecords` gates attachment **filing**, not "this mailbox takes requests", so use a new intake flag;
  - `RoutingContext` carries only `MimeType` and `DocumentType` today;
  - `DeadLetterQueueService` is registered in DI but has no caller (FR-15 wires it).
- **Idempotency:** `IIdempotencyService` as built is check-then-set and fails open. Use an atomic claim (ADR-004 A1 §4).
- **Container Apps:** there is no Container Apps resource anywhere in `infra/` or `infrastructure/`; this project adds the first. Ignore stale claims of existing bot-service/byok Container Apps.

## 7. Key documents

- `spec.md` · `design.md` · `plan.md` · `notes/decisions.md` · `notes/defer-issues.md` · `notes/reuse-scan.md` · `notes/section10-verification-2026-11-25.md`
- Applicable ADRs (concise copies in `.claude/adr/`): 001, 002, 004 (+A1, A2), 007, 008, 010, 013, 015, 017, 019, 028 (A4/A5), 032, 036, 039, 043, 045, 049, 052
- Constraints: `.claude/constraints/jobs.md`, `.claude/constraints/bff-extensions.md`, `.claude/constraints/auth.md`
- Related projects: listed in §4.
