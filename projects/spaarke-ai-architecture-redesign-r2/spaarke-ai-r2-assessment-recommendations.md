# Spaarke AI Architecture R2 Assessment and Recommendations

> Purpose: Senior full-stack AI architecture assessment of `Spaarke AI Architecture Redesign R2 — Design Charter`, with implementation-oriented recommendations for Claude Code.
>
> Audience: Spaarke operator, Claude Code, full-stack engineers, AI platform developers, and product design stakeholders.
>
> Source reviewed: `design.md`, Spaarke AI Architecture Redesign R2 Design Charter, draft v0.2 dated 2026-07-07.

---

## 1. Executive Assessment

The R2 design is directionally correct and should proceed to design-to-spec, but it needs several contract-level refinements before implementation begins.

The core architectural judgment is sound: Spaarke R1 already delivered the necessary AI execution chassis. It has bounded tool execution, closed catalogs, a session ledger, confirmation gating, honest refusal behavior, citation/grounding posture, and browser-verified UAT discipline. R2 should not rebuild those foundations. It should refine the layers above them: judgment, completion proof, context, memory, and the document-native Compose experience.

The strongest part of the design is the diagnosis that R1 solved “do not fabricate” but created a new product problem: the assistant became too passive, too confirm-heavy, and insufficiently transparent. That is exactly the right problem to solve next. A legal AI assistant must not invent outcomes, but it also cannot behave like a passive chatbot that refuses, hedges, or asks the user to repeat obvious intent.

The main recommendation is to move from a charter-level architecture to a contract-first implementation architecture. The design names the right subsystems, but Claude Code should not begin implementation until the core contracts are explicitly defined:

- `ContextEnvelope v1`
- `OutcomeCard v1`
- `MemoryItem v1`
- `GateDecision v2`
- `TraceEvent v1`
- `ComposeDisposition v1`
- `JobAwareCompletionState v1`

Without these contracts, there is a high risk that implementation will drift into handler-specific logic, prompt-only steering, duplicated state, or inconsistent UI behavior.

---

## 2. Overall Recommendation

Proceed with R2, but amend the design before `/design-to-spec`.

Recommended disposition:

- Approve the R2 direction.
- Keep the platform-core plus satellites delivery model.
- Keep Compose r2 as a separate parallel project.
- Keep the Resourcefulness Doctrine as the first work item.
- Add a mandatory contract-first phase before feature implementation.
- Expand Completion UX to handle async job lifecycle states.
- Refine Policy v2 risk tiers before allowing auto-execute for legal record mutations.
- Make full document ingestion parity a platform invariant, not only a Compose requirement.
- Strengthen memory governance around retention, deletion, visibility, audit, and poisoning controls.
- Add scenario-based legal work evals in addition to golden utterance tests.

---

## 3. What the Design Gets Right

### 3.1 The platform-core plus satellites split is correct

The design correctly separates the AI platform core from satellites.

The core should own:

- Judgment layer
- Confirmation policy
- Gate engine extensions
- Context Binder
- Memory Service
- Completion Engine
- OutcomeCard contract
- Traceability surface
- AI-internal BFF plumbing
- Dispatch and disposition seams

Satellites should consume these seams but not fork them.

This is the right architecture because Compose, Daily Briefing, and Insights all need the same underlying AI execution guarantees. If each satellite modifies AI internals independently, Spaarke will quickly accumulate inconsistent behavior, duplicate state, and divergent UX patterns.

### 3.2 The Resourcefulness Doctrine is the correct correction after R1

R1 hardened honesty, but the system appears to have generalized caution into passivity. The proposed doctrine is the right design response:

- Read broadly.
- Search before claiming absence.
- Verify before acting.
- Act when explicit and complete.
- Degrade gracefully when blocked.
- Refuse only as the last rung.
- Always provide a concrete next step when refusing.

This is the right legal AI posture. The assistant must be cautious around system-of-record mutations and external communications, but it should be proactive with reads, searches, verification, preparation, extraction, drafting, and navigation.

### 3.3 Completion proof should become a product standard

The proposed Completion Engine and OutcomeCard are essential.

A legal AI system must not merely say “done.” It must prove what happened. Every side effect should produce a durable, user-visible completion artifact that includes:

- Status
- Human-readable summary
- Affected record or document
- Deep link
- Next-step affordances
- Undo or corrective action where available
- Trace reference
- Correlation id
- Job status where async work is involved

This is a core trust mechanism, not just a UX improvement.

### 3.4 Structured memory is the right approach

The design correctly rejects “memory equals embeddings.”

For Spaarke, memory should be structured, scoped, governed, and auditable. Conversation memory should remain the session ledger. User and workspace memory should be explicit governed objects. Semantic retrieval should remain in AI Search and SPE-backed retrieval. Organizational memory should remain an interface until the product is ready for a Work IQ or equivalent provider.

This approach is safer, more explainable, and better aligned with legal confidentiality and data governance requirements.

### 3.5 Compose is correctly treated as the primary legal work surface

The design correctly identifies Compose as the most important product surface.

Legal users do not judge the quality of a legal AI system primarily by how well it chats. They judge it by whether it helps them create, review, revise, and save legal work product in the document flow. Compose should therefore be treated as a document-native workbench, not merely a chat-adjacent editor.

---

## 4. Main Weaknesses and Gaps

### 4.1 The design is not yet contract-first

The design names the right components, but several core objects are still conceptual rather than contractual.

Before implementation, define exact DTOs, lifecycle states, persistence behavior, ownership, versioning, failure modes, and compatibility rules for:

- Context assembly
- Completion rendering
- Memory writes and reads
- Gate decisions
- Trace events
- Compose dispatch
- Async job completion

If these are not specified up front, Claude Code will likely implement plausible local patterns that later conflict.

### 4.2 Completion UX is under-specified for async work

The OutcomeCard concept is strong, but document and AI operations are often asynchronous.

For example, “save this summary as a document” should not only say that a document row was created. It may need to represent:

- SPE upload started
- SPE upload completed
- `sprk_document` created
- document association created
- document profile analysis queued
- document profile analysis running
- document profile analysis completed or failed
- RAG indexing queued
- RAG indexing completed or failed
- final document available in Compose or document grid

The Completion Engine must therefore be job-aware. It should integrate with standard async job status rather than treating all completions as immediate terminal events.

### 4.3 Policy v2 risk tiers are too coarse

The current Tier 2 “create records” concept is too broad for legal operations.

These actions are not equivalent:

- Create a personal follow-up task.
- Create a matter-scoped task.
- Create a new matter.
- Create a document record.
- Save a new document version.
- Update a legal deadline.
- Assign work to another user.
- Associate a document to a confidential matter.
- Prepare an email draft.
- Send an email to external counsel.
- Delete or supersede a document.

Legal AI policy should classify side effects by risk factors, not only by generic CRUD type.

### 4.4 Memory governance needs more implementation detail

The design correctly identifies memory as governed structured objects, but the spec needs more detail on:

- Retention defaults
- Expiration behavior
- User-visible memory review
- Workspace memory review
- Deletion propagation
- Audit events
- Who can see workspace memory
- How memory respects matter security
- Litigation hold implications
- Sensitive content classification
- Memory poisoning prevention
- Untrusted source restrictions
- Tenant and user partitioning

This is especially important because legal AI memory creates both product value and legal risk.

### 4.5 Context Binder needs token budgets and retrieval rules

The Context Binder is the right architectural component, but it must have strict budgets.

Without budgets, the system will gradually add more schema, memory, business context, ledger history, semantic retrieval, and tool output until latency, cost, prompt cache stability, and model quality become unpredictable.

The ContextEnvelope should define:

- Stable prefix slices
- Volatile slices
- Maximum tokens per slice
- Maximum memory items
- Maximum semantic chunks
- Required provenance references
- Cacheability rules
- Fresh-query triggers
- Fallback behavior when context exceeds budget

### 4.6 Compose still needs a fidelity boundary

TipTap is reasonable for an integrated drafting surface, especially for AI-generated drafts, structured edits, and matter-aware composition. However, legal documents often require Word fidelity.

The Compose r2 spec should explicitly define which document types and editing operations are safe inside the Compose editor and which should route users to Word for Web or Word Desktop.

Important fidelity pressure points include:

- Tracked changes
- Comments
- Footnotes
- Cross-references
- Numbered clauses
- Defined term consistency
- Headers and footers
- Tables
- Signature blocks
- Styles
- Redlines
- Complex formatting
- Round-trip preservation

Compose should not over-promise fidelity. “Open in Word” is the right pressure valve, but the boundary must be explicit.

---

## 5. Required Design Amendments Before `/design-to-spec`

Add the following amendments to the R2 design before generating implementation specs.

### Amendment 1: Add a contract-first Phase A0

Add a mandatory first phase named:

`Phase A0 — Core Contract Publication`

This phase must produce versioned contracts before feature work begins.

Required outputs:

- `ContextEnvelope v1`
- `OutcomeCard v1`
- `MemoryItem v1`
- `GateDecision v2`
- `TraceEvent v1`
- `ComposeDisposition v1`
- `JobAwareCompletionState v1`
- Contract versioning and tolerant-reader rules
- Example payloads
- Client rendering expectations
- Server persistence expectations
- Failure and partial-completion states

Acceptance condition:

- No implementation work begins on Memory Service, Completion Engine, Context Binder, or Compose seams until these contracts are reviewed and accepted.

### Amendment 2: Make Completion Engine job-aware

Extend D-F2 so the Completion Engine can represent both synchronous and asynchronous side effects.

OutcomeCard must support:

- Immediate success
- Immediate failure
- Queued async work
- Running async work
- Partial completion
- Completed async work
- Failed async work
- Poisoned job
- Cancelled job
- Retry pending
- User action required

The card must be able to show multiple related statuses where one user request creates multiple downstream operations.

Example:

A “save as document” request may include:

- Document created
- Analysis queued
- Indexing queued
- Open document
- View job status
- Retry failed analysis
- Open upload/profile page if processing cannot complete

### Amendment 3: Refine Policy v2 risk taxonomy

Replace broad Tier 2 with sub-tiers.

Recommended taxonomy:

| Tier | Description | Examples | Default behavior |
|---|---|---|---|
| 0 | Read/search/explain | Search matter, summarize known record, inspect metadata | Execute |
| 1 | Draft-only, no system mutation | Draft clause, prepare email draft text, create summary text | Execute |
| 2a | Private/internal reversible create | Create personal task, create draft note | Execute if explicit and complete |
| 2b | Matter-scoped create/update | Create matter task, update internal status, associate record | Execute only if explicit and complete; otherwise confirm |
| 2c | Document creation/versioning | Save generated text as document, create new version, promote draft | Usually confirm or show preview unless safe explicit path |
| 3 | Legal operational risk | Deadline, obligation, assignment to another user, client/matter status | Confirm |
| 4 | External or irreversible risk | Send email, file submission, delete, external communication, legal commitment | Always confirm |

Risk evaluation should also consider:

- Reversibility
- External visibility
- Deadline impact
- Confidentiality impact
- Privilege risk
- Client/matter record-of-truth impact
- User explicitness
- Argument completeness
- Injection suspicion
- Whether the request originated from untrusted document content

### Amendment 4: Make full document ingestion parity a platform invariant

Add a platform invariant:

A Spaarke-created document is not complete unless it has full ingestion parity with the Document Upload wizard.

Minimum required parity:

- SPE file exists.
- `sprk_document` exists.
- Storage pointer is valid.
- Parent association exists where applicable.
- Access context is valid.
- Provenance is stored.
- Document profile analysis is queued or completed.
- RAG indexing is queued or completed where configured.
- Status is visible to user.
- Failures are recoverable or clearly surfaced.

Do not permit bare `sprk_document` creation as a successful document operation.

### Amendment 5: Add memory governance details

Add a Memory Governance subsection with exact requirements.

Memory objects must include:

- Tenant id
- Scope
- Owner
- Subject id
- Source
- Confidence
- Sensitivity
- Expiration
- Deletion policy
- Created timestamp
- Updated timestamp
- Created by user/action
- Provenance reference
- Source trust level
- Retention class

Memory write rules:

- Explicit user instruction may write memory subject to policy.
- Model-inferred memory should require lightweight confirmation or review queue.
- Untrusted document content cannot originate memory writes.
- Tool output cannot write memory unless the write is explicitly authorized.
- Memory writes are side effects and must go through the catalog/gate machinery.
- Memory reads must surface provenance in traceability.
- Users must be able to view and delete user-scope memory.
- Authorized workspace users must be able to review workspace-scope memory according to matter security.

### Amendment 6: Add ContextEnvelope token budgets

Add budget requirements to D-M2.

Recommended initial budgets:

| Slice | Budget guidance |
|---|---|
| Environment facts | Very small stable prefix |
| User identity and preferences | Small stable prefix |
| Business host context | Small to medium, record metadata only |
| Schema cards | Strictly bounded; include only relevant fields/actions |
| Conversation ledger tail | Last relevant outcomes, not arbitrary last N turns |
| Workspace memory | Top relevant governed objects only |
| Semantic retrieval | Top ranked chunks with citations/provenance |
| Tool output references | References and summaries, not full prior output unless needed |

Rules:

- Never include full document text in ContextEnvelope unless the action specifically requires it.
- Use references to ledger entries rather than copying full prior outputs.
- Portfolio or aggregate questions must force fresh retrieval/query rather than extrapolating from prior results.
- Context assembly must be measurable in telemetry by token count per slice, without logging sensitive content.

### Amendment 7: Add legal-work scenario evals

Golden utterance tests are necessary but insufficient.

Add scenario evals that exercise full legal work patterns:

- Matter-aware task creation with no unnecessary confirmation.
- Ambiguous legal record creation that asks exactly one clarification.
- Blocked document creation that extracts useful content and links to the correct upload surface.
- Draft into Compose, revise selection, save back, and show provenance.
- Ask “what happened here” and receive traceable tool/memory/context explanation.
- Attempt memory poisoning from uploaded document content and verify no memory write occurs.
- Ask portfolio-level question after a prior matter-specific answer and verify fresh retrieval.
- Generate document output and verify full ingestion parity status.
- External email send request and verify Tier 4 confirmation.
- Deadline modification request and verify confirmation plus audit trace.

Each scenario should have browser-verifiable acceptance criteria where UI state matters.

### Amendment 8: Define Compose fidelity boundary

Add a Compose r2 boundary table.

Recommended structure:

| Editing need | Compose editor | Word for Web/Desktop |
|---|---|---|
| AI-generated first draft | Yes | Optional |
| Plain text revision | Yes | Optional |
| Clause-level rewrite | Yes | Optional |
| Selection-aware refinement | Yes | Optional |
| Complex legal formatting | Limited | Preferred |
| Tracked changes | No or deferred | Required |
| Comments | Deferred unless implemented | Required |
| Footnotes/cross-references | No or limited | Required |
| Final formatting review | No | Required |
| Redline comparison | Deferred unless implemented | Required |

This protects product trust by preventing Compose from being judged against Word fidelity before it is ready.

---

## 6. Recommended Implementation Order

Use this implementation order unless the operator explicitly changes priority.

### Wave 0: Immediate remediation

Purpose: Fix known hallucination and grounding issues before building new surfaces.

Tasks:

- Repair Daily Briefing hallucination/citation defects.
- Add eval cases for the specific failure modes.
- Verify old playbook/embedding orphans are closed.
- Repair pre-existing failing AI widget and SpaarkeAi test suites where they block core work.

### Wave 1: Contract publication

Purpose: Publish stable seams before satellite or feature implementation.

Tasks:

- Define `ContextEnvelope v1`.
- Define `OutcomeCard v1`.
- Define `JobAwareCompletionState v1`.
- Define `MemoryItem v1`.
- Define `GateDecision v2`.
- Define `TraceEvent v1`.
- Define `ComposeDisposition v1`.
- Add versioning rules and examples.
- Add tolerant-reader behavior for all evolving contracts.
- Add contract tests.

### Wave 2: Resourcefulness Doctrine and refusal affordances

Purpose: Correct R1 passivity without weakening honesty.

Tasks:

- Add the D-F0 strategy block.
- Audit and fold scenario pins into strategy where appropriate.
- Preserve scenario-specific rules as catalog/tool contracts.
- Implement refusal-affordance requirement.
- Add deep links for blocked document workflows.
- Add resourcefulness eval family with fabrication counter-cases.

### Wave 3: Policy v2 and gate pre-validation

Purpose: Reduce friction safely.

Tasks:

- Implement refined risk taxonomy.
- Add deterministic origin classification.
- Add argument completeness classification.
- Add pre-suspend `ValidateChat`.
- Prevent confirm loops structurally.
- Add one-modality confirmation enforcement.
- Add origin/risk eval family.
- Add undo metadata where declared by tool.

### Wave 4: Completion Engine and OutcomeCard

Purpose: Make all side effects visible, durable, and actionable.

Tasks:

- Implement server Completion Engine.
- Implement client OutcomeCard rendering.
- Cover all existing side-effect paths.
- Include links, affected records, next steps, undo where available, trace reference, correlation id.
- Integrate job-aware completion states.
- Add browser UAT for side-effect completion.

### Wave 5: Context Binder v1

Purpose: Make context intentional and reusable.

Tasks:

- Implement ContextEnvelope assembly.
- Add user identity slice.
- Add business host context slice.
- Add environment facts, including date and timezone.
- Add schema card slice.
- Add conversation ledger outcome slice.
- Add workspace memory read slice once Memory Service is available.
- Add token budget telemetry by slice.
- Add fresh-query triggers for aggregate questions.

### Wave 6: Memory Service v1

Purpose: Add governed memory without creating privacy or poisoning risk.

Tasks:

- Add Cosmos memory container.
- Implement `MemoryItem v1`.
- Implement memory read/write tools.
- Enforce memory writes as side effects.
- Block untrusted-content-origin memory writes.
- Add user memory view/delete surface.
- Add workspace memory review surface or API foundation.
- Add retention and deletion behavior.
- Add memory poisoning evals.

### Wave 7: Compose seam enablement

Purpose: Unblock Compose r2 without letting it fork AI internals.

Tasks:

- Add `compose` disposition member.
- Add Compose SSE frame shape.
- Add provenance references.
- Add UI action acknowledgment plumbing.
- Add OutcomeCard support for Compose actions.
- Add Policy v2 classification for Compose save-back and document creation.
- Add handoff documentation to Compose r2.

### Wave 8: Full document ingestion parity

Purpose: Convert “save this as document” from a hard block into a real capability.

Tasks:

- Implement document creation through SPE plus `sprk_document`.
- Create or reuse parent association.
- Queue document profile analysis.
- Queue RAG indexing where configured.
- Persist status transitions.
- Show job-aware OutcomeCard.
- Add retry or recovery affordances.
- Add browser UAT verifying no bare orphan document rows.

### Wave 9: Hardening and verification

Purpose: Ensure the platform remains boring and reliable.

Tasks:

- Verify no satellite modified AI internals.
- Verify all side effects produce OutcomeCards.
- Verify all refusals include affordances.
- Verify all memory writes are governed.
- Verify all UI action claims have client acknowledgments.
- Verify publish-size ceiling.
- Verify eval suite green.
- Verify browser UAT gates.

---

## 7. Claude Code Implementation Instructions

Use the following as direct Claude Code guidance.

### 7.1 Primary instruction

Implement R2 as a contract-first refinement of the existing Spaarke AI architecture. Do not rebuild the agent loop, dispatch protocol, session ledger, catalog model, or gate system. Extend the existing architecture through explicit contracts, deterministic policies, and user-visible completion evidence.

### 7.2 Do not do these things

Do not:

- Create a second AI dispatch protocol.
- Create a parallel session cache.
- Move AI orchestration out of `Sprk.Bff.Api`.
- Add Azure Functions or Durable Functions.
- Let satellites modify AI-internal services directly.
- Use prompt text as the only enforcement mechanism for side effects.
- Treat memory as embeddings.
- Allow untrusted document content to write memory.
- Create bare `sprk_document` rows as successful document creation.
- Let UI tools claim success without client acknowledgment.
- Show a refusal or hard block without a concrete next-step affordance.
- Treat all “create record” actions as the same risk tier.
- Copy full sensitive content into logs, status records, traces, or telemetry.
- Add bespoke UI components when the shared Fluent UI component library should be used.

### 7.3 Follow these architectural constraints

Follow existing Spaarke ADR posture:

- Use Minimal API and BackgroundService workers.
- Use the BFF as the AI runtime.
- Use endpoint filters for authorization.
- Use `SpeFileStore` for SPE/Graph operations.
- Use Redis for distributed caching where caching is approved.
- Use Job Contract for async work.
- Use ProblemDetails for API failures and terminal SSE errors.
- Use Fluent UI v9 and shared components for UI.
- Use PCF for model-driven app custom UI where applicable.
- Use explicit feature flags and kill switches.
- Use structured governance for AI data and memory.

### 7.4 Preferred implementation style

Implement small, explicit contracts first. Then wire features to those contracts.

Prefer:

- Versioned DTOs.
- Tolerant readers.
- Contract tests.
- Server-composed links.
- Durable persisted outcome records.
- Deterministic gate decisions.
- Small focused services.
- Explicit state machines.
- Browser-verifiable UAT scripts.
- Eval cases tied to known failure modes.

Avoid:

- Prompt-only fixes.
- Handler-specific branching.
- Hidden caches.
- Implicit UI state.
- Magic string contracts.
- Duplicated schema descriptions.
- Client-side claims that are not backed by server or client events.

---

## 8. Proposed Contract Sketches

These are not final schemas, but they should guide `/design-to-spec`.

### 8.1 ContextEnvelope v1

```json
{
  "version": 1,
  "tenantId": "guid",
  "sessionId": "guid",
  "turnId": "guid",
  "user": {
    "userId": "guid",
    "contactId": "guid-or-null",
    "displayName": "string",
    "timezone": "America/New_York",
    "preferencesRefIds": ["memory-id"]
  },
  "environment": {
    "currentDate": "2026-07-08",
    "currentDateTime": "2026-07-08T09:30:00-04:00"
  },
  "business": {
    "hostEntityName": "sprk_matter",
    "hostRecordId": "guid",
    "hostRecordName": "Matter name",
    "schemaCardRef": "schema-card-id"
  },
  "workspace": {
    "workspaceId": "guid-or-null",
    "openDocumentIds": ["guid"],
    "activeDocumentId": "guid-or-null"
  },
  "memory": {
    "conversationRefs": ["ledger-entry-id"],
    "userMemoryRefs": ["memory-id"],
    "workspaceMemoryRefs": ["memory-id"]
  },
  "semantic": {
    "retrievalRefs": ["retrieval-result-id"]
  },
  "budgets": {
    "maxTokens": 8000,
    "usedTokensEstimate": 3200,
    "sliceTokenEstimates": {
      "user": 200,
      "business": 500,
      "memory": 800,
      "semantic": 1200
    }
  }
}
```

### 8.2 OutcomeCard v1

```json
{
  "version": 1,
  "outcomeId": "guid",
  "sessionId": "guid",
  "turnId": "guid",
  "actionId": "create-task",
  "status": "completed",
  "summary": "Created follow-up task due Friday and assigned it to you.",
  "affectedRecords": [
    {
      "entityName": "sprk_event",
      "recordId": "guid",
      "displayName": "Follow-up task",
      "link": "https://..."
    }
  ],
  "jobs": [
    {
      "jobId": "guid",
      "jobType": "AppOnlyDocumentAnalysis",
      "status": "running",
      "statusUrl": "/api/jobs/guid/status"
    }
  ],
  "nextSteps": [
    {
      "id": "open-record",
      "label": "Open task",
      "action": "navigate"
    }
  ],
  "undo": {
    "available": true,
    "actionId": "delete-created-task",
    "expiresAt": "2026-07-08T10:00:00-04:00"
  },
  "traceRef": "trace-id",
  "correlationId": "correlation-id"
}
```

### 8.3 MemoryItem v1

```json
{
  "version": 1,
  "memoryId": "guid",
  "tenantId": "guid",
  "scope": "user",
  "ownerId": "guid",
  "subjectType": "preference",
  "subjectId": "guid-or-null",
  "content": {
    "kind": "drafting_preference",
    "value": "User prefers concise executive summaries before detailed analysis."
  },
  "source": {
    "sourceType": "explicit_user_instruction",
    "sessionId": "guid",
    "turnId": "guid"
  },
  "confidence": 1.0,
  "sensitivity": "standard",
  "sourceTrustLevel": "trusted_user",
  "expiration": null,
  "deletionPolicy": "user_deletable",
  "retentionClass": "standard_user_memory",
  "createdAt": "2026-07-08T09:30:00-04:00",
  "updatedAt": "2026-07-08T09:30:00-04:00"
}
```

### 8.4 GateDecision v2

```json
{
  "version": 2,
  "gateDecisionId": "guid",
  "sessionId": "guid",
  "turnId": "guid",
  "actionId": "create-task",
  "origin": "user_explicit",
  "argumentCompleteness": "complete",
  "riskTier": "2a",
  "riskFactors": {
    "reversible": true,
    "externalVisibility": false,
    "deadlineImpact": false,
    "confidentialityImpact": false,
    "recordOfTruthImpact": true,
    "injectionSuspect": false
  },
  "decision": "execute_without_confirmation",
  "requiresDialog": false,
  "validatedBeforeSuspend": true,
  "reason": "Explicit complete request for reversible internal task creation."
}
```

### 8.5 JobAwareCompletionState v1

```json
{
  "version": 1,
  "subjectId": "guid",
  "subjectType": "document",
  "overallStatus": "partial",
  "steps": [
    {
      "step": "spe_upload",
      "status": "completed"
    },
    {
      "step": "document_record",
      "status": "completed"
    },
    {
      "step": "profile_analysis",
      "status": "running",
      "jobId": "guid"
    },
    {
      "step": "rag_indexing",
      "status": "queued",
      "jobId": "guid"
    }
  ]
}
```

---

## 9. Browser UAT Acceptance Backbone

Each gate should have browser-verifiable acceptance criteria.

### G-R2-A Judgment and Friction

Browser UAT should verify:

- Explicit complete task creation executes without confirmation.
- The assistant does not ask again in chat after confirmation.
- The created record appears as a clickable card/chip.
- The transcript shows a durable completion state.
- Ambiguous requests ask one clarification, not repeated confirmation.
- Blocked actions provide extracted value and a working next-step link.
- UI action claims are backed by actual client events.
- Decision trace opens and explains context, tools, gate path, and outcome.

### G-R2-B Memory

Browser UAT should verify:

- User preference stated once can be remembered.
- Memory appears in a user-visible review/delete surface.
- Deleted memory is not reused.
- Workspace memory is scoped to the matter/workspace.
- Untrusted document text cannot write memory.
- Memory reads show provenance in traceability.
- Aggregate questions trigger fresh retrieval rather than stale extrapolation.

### Compose r2 Gate

Browser UAT should verify:

- Open an existing document into Compose.
- Pre-seed from a real `sprk_document`.
- Draft content into editor.
- Perform selection-aware AI revision.
- Save back with provenance.
- Show document creation or save-back OutcomeCard.
- Show analysis/indexing job status where applicable.
- Open in Word when fidelity requires it.

### G-R2-D Hardening

Browser UAT and automated checks should verify:

- No satellite modifies AI internals.
- No side-effect path lacks OutcomeCard.
- No refusal lacks affordance.
- No UI action succeeds without acknowledgment.
- No document creation creates a bare orphan row.
- Eval suite is green.
- Publish-size limit is respected.
- Telemetry contains identifiers, sizes, timings, and codes only, not sensitive content.

---

## 10. Specific Recommendations for the Current Design Text

Apply these edits directly to the R2 design charter.

### Add after Section 3

Add:

```markdown
### 3.4 Contract-first rule

Before implementing any R2 feature, the core must publish versioned contracts for ContextEnvelope, OutcomeCard, MemoryItem, GateDecision, TraceEvent, ComposeDisposition, and JobAwareCompletionState. Satellites may only consume these contracts. They may not invent local variants.
```

### Add to Section 4

Add:

```markdown
Completion UX must represent both immediate and async outcomes. Any action that enqueues background work must render a durable status state and link to job status. The user must be able to distinguish record creation from downstream analysis/indexing completion.
```

### Modify D-F1

Replace broad Tier 2 with sub-tier taxonomy:

```markdown
Tier 2 is split into 2a internal reversible create, 2b matter-scoped system-of-record mutation, and 2c document creation/versioning. Auto-execution applies only when the request is explicit, arguments are complete, the action is classified as safe for its sub-tier, and no injection suspicion exists.
```

### Modify D-F2

Add:

```markdown
OutcomeCard is job-aware. It can represent queued, running, completed, failed, poisoned, cancelled, partial, and user-action-required states. For multi-step document operations, it renders per-step completion.
```

### Modify D-M1 and D-M3

Add memory governance fields and deletion rules.

```markdown
Every memory object carries tenantId, scope, owner, subject, source, confidence, sensitivity, sourceTrustLevel, expiration, deletionPolicy, retentionClass, timestamps, and provenance. User-scope memory must be viewable and deletable by the user. Workspace-scope memory must obey matter/workspace authorization.
```

### Modify D-M2

Add:

```markdown
ContextEnvelope assembly is token-budgeted by slice. Full sensitive document content is not copied into the envelope unless specifically required by the invoked capability. Aggregate questions trigger fresh retrieval or query execution rather than extrapolating from prior ledger output.
```

### Modify Section 8

Add:

```markdown
Compose r2 must define a fidelity boundary. TipTap is the integrated AI drafting surface, but tracked changes, comments, complex formatting, footnotes, cross-references, and final legal formatting may require Word for Web or Word Desktop.
```

### Modify Section 10

Add backlog item:

```markdown
Job-aware Completion Engine: integrate side-effect OutcomeCards with async job status so document creation, analysis, indexing, and save-back flows can show durable multi-step progress and failure recovery.
```

---

## 11. Key Product Principle to Preserve

R2 should optimize for this principle:

A Spaarke AI action is not complete when the model says it is complete. It is complete when the system has persisted the outcome, linked the affected business object, exposed the status to the user, and made the decision traceable.

This principle should guide all implementation decisions.

---

## 12. Bottom Line

The R2 design is strong and market-aligned. It correctly focuses on the layers that will differentiate Spaarke from a generic AI chatbot:

- Resourceful judgment
- Reduced confirmation friction
- Durable completion proof
- Governed memory
- Matter-aware context
- Document-native Compose workflows
- Traceability and trust

The required refinement is to make the design more concrete before implementation. A contract-first, job-aware, risk-tiered R2 will be much safer for Claude Code to implement and much more likely to produce a coherent legal AI platform rather than a collection of plausible but inconsistent feature additions.
