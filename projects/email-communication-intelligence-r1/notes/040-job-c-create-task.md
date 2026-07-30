# 040 — Job C CREATE-TASK-FROM-EMAIL Action + Enrichment Trigger + Deadline-Confirm Gate + Eval Case

> **Task**: 040 (P4, FULL rigor, sonnet/high) · **Depends on**: 020 (association), 022 (triage Action) · **Blocks**: 041

---

## 1. What was wired

### Action (`infra/dataverse/actions/create-task-from-email.action.json`)

New `create-task-from-email` prompted Action (flat-systemPrompt + outputSchema, following
`propose-field-updates.action.json`'s shape — not `triage-email.action.json`'s classic-JPS shape, because
the output has no Dataverse-tunable `$choices` lookup). Job C's OWN targeted "does this email imply
follow-up work" extraction: given the associated record's entity type + the already-produced triage output
(grounding) + the email/attachment text, extracts zero-or-more candidate follow-up tasks, each with
`subject`, `description`, an OPTIONAL `dueDate` (populated ONLY when the source concretely states a
deadline — never guessed), a verbatim `citation`, a `reason`, and a `confidence`.

### Binding row (`infra/dataverse/sprk_playbookconsumer-rows.json`)

Added the `email-create-task` Binding row (mirrors `email-triage`/`email-propose` — a Linear AI Consumer,
not a chat/loop capability): `consumerType: "email-create-task"`, `actionCode: "create-task-from-email"`, all
chat-loop columns null.

### Facade (`Services/Ai/PublicContracts/ICommunicationCreateTaskAi.cs` / `CommunicationCreateTaskAi.cs` /
`NullCommunicationCreateTaskAi.cs`)

Mirrors `ICommunicationTriageAi`/`ICommunicationProposeAi` exactly: resolves + runs the Action via
`IActionResolver`/`IActionRunner` (routed by `ConsumerTypes.EmailCreateTask`), no
`ICommunicationClassificationAi`/`IOpenAiClient` dependency (structurally incapable of a second
classification), best-effort (returns `null` on any failure). Reuses the existing `ProposalCitation` record
from `ICommunicationProposeAi.cs` rather than declaring a new citation shape (three-question test: an
identical shape already exists — extend by reusing the type).

### Enrichment trigger (`Services/Communication/CommunicationEnrichmentService.cs`)

New best-effort **"email-create-task"** step, added after "email-propose" in `EnrichAsync`'s fixed step
order (both consume the SAME hoisted `triageResult` — no second classification call for either). New private
method `RunEmailCreateTaskAsync`:

1. Reads the communication's core regarding lookups (reuses `CoreProposeTargets` +
   `ResolveAssociatedCoreRecords` — the SAME helpers Job B already uses). Targets the FIRST resolved
   association (deterministic `CoreProposeTargets` order) as "the record the email is associated to" (task
   020) — the Action is invoked ONCE, not once per associated record (avoids multiplying LLM cost for the
   same email).
2. Calls `ICommunicationCreateTaskAi.ExtractAsync` with the associated entity type + the hoisted
   `triageResult` (grounding) + the email subject/body/attachment text.
3. For each surviving candidate (after the NFR-06 verify-cited-text gate, reusing `CitationVerifier` from
   `EmailProposalShaping.cs` — same gate Job B applies):
   - Stores a `Proposed` row on `sprk_emailreviewlog` UNCONDITIONALLY (uniform audit trail) —
     see §2 below for the sentinel-field design that makes this reuse-not-fork.
   - **Non-deadline-bearing** (`candidate.DueDate is null`): immediately creates the task via
     `IActionSeam.CreateTaskAsync` (the SAME session-agnostic write core `TaskActionCore` backs — see §3),
     citing the source communication in the created task's description. On success, writes a SECOND
     `Applied` row referencing the created task id.
   - **Deadline-bearing** (`candidate.DueDate.HasValue`): does **NOT** call `IActionSeam.CreateTaskAsync` at
     all — the `Proposed` row is left open (no terminal row) for human confirm (NFR-06 / ADR-015).
4. The entire method is wrapped in try/catch (NFR-04) independent of `RunStepAsync`'s outer guard.

---

## 2. The deadline-bearing → confirm design (how it is gated, where pending entries land)

**Gate mechanism**: a plain code branch on `TaskCandidate.DueDate.HasValue` — NOT the chat-loop
`SideEffectGateAIFunction`/`ConfirmationPolicyEngine` gate (that mechanism is chat-session-scoped; the
Communication enrichment path has no chat session, no LLM turn to suspend). The Action's own systemPrompt
enforces the model-side half of the rule ("dueDate is opt-in, never guessed" — Hard Rule 3), and
`CommunicationEnrichmentService`'s branch is the code-side enforcement (structural, not just prompted).

**Where pending entries land**: reused task 030's Decision-1 pattern — `sprk_emailreviewlog` with
`sprk_action = Proposed`. Task 030 keyed its rows by a REAL field logical name (`sprk_targetfield`); Job C
has no field to key by (it is creating a NEW record, not updating one), so it uses a **sentinel**
`sprk_targetfield` value: `"__create_task__:" + SHA256(subject)[..8]` (see `BuildCreateTaskSentinelField`).
This is a compact, honest reuse:

- `LoadOpenProposalFieldsAsync` (Job B's idempotency helper) is REUSED UNMODIFIED — it is generic over any
  `sprk_targetfield` string value; the sentinel just rides the same "open Proposed row with no later terminal
  row, per (communication, entity, field)" walk Job B already established. No new idempotency query.
- The sentinel can never collide with a real allow-listed field name (Job B's allow-list only ever contains
  real Dataverse field logical names), so the two Jobs' rows coexist safely in the SAME table without
  cross-contamination of each other's open/closed state.
- Two distinct implied tasks on the same email get two distinct sentinel keys (hashed on subject), so each
  has its own open/closed lifecycle rather than collapsing into one.

**Confirmation surface**: r1 builds no UI (project constraint C-3). The open `Proposed` row (with
`kind: "create-task"` in its `sprk_aisuggestion` JSON) is the same shape r5's Job B confirm surface already
reads for field-update proposals — a follow-on task (not 040) would extend that surface (or a dedicated
apply endpoint) to recognize `kind: "create-task"` rows and call `IActionSeam.CreateTaskAsync` on confirm.
This is explicitly OUT OF SCOPE for 040 (the acceptance criterion is "does NOT auto-finalize", not "ships an
apply endpoint") — flagged here for whoever picks that up next.

---

## 3. Reuse, not fork (FR-14 / ADR-039) — the two "create-task" mechanisms in this codebase, and why there is still only ONE create mechanism

Investigation found **two visibly different code paths** that both end in a Dataverse task/event create,
and it was important to identify which one task 040 could actually reuse from a non-chat, background
context:

1. **Chat-loop `create-task` Binding** (`consumerType: "create-task"`, `CREATE-TASK@v1` Action) — drafts a
   task from a LIVE conversation (elicits `due_date`/`assign_to` from the user turn-by-turn), then the model
   calls the generic `dataverse.create_record` tool (`DataverseCreateRecordHandler`), which is
   **chat-context-only** (`SupportedInvocationContexts => InvocationContextKind.Chat`) and **gated** by
   `SideEffectGateAIFunction` (the confirmation-dialog suspend/resume mechanism) — it creates an
   `sprk_event` row under the confirming user's own OBO token. This entire mechanism REQUIRES a live chat
   session; `CommunicationEnrichmentService` has none.
2. **Session-agnostic `IActionSeam.CreateTaskAsync` → `TaskActionCore`** — the ADR-013 PublicContracts facade
   task 031 (Job B apply) already uses, and the SAME core the (deprecated but still-registered)
   `CreateTaskNodeExecutor` used before it. Creates a `task` (CRM activity) entity, registered UNCONDITIONALLY
   (not AI-model-gated), reachable with no chat session and no OBO token — exactly the shape a background
   enrichment step needs.

**Decision**: task 040 reuses mechanism (2) — `IActionSeam.CreateTaskAsync`. This is NOT a fork of mechanism
(1); it is the SAME session-agnostic write core Job B's apply step already reaches through the SAME facade.
The POML's framing ("CREATE-TASK@v1 → gated dataverse.create_record / IActionSeam.CreateTaskAsync") captures
both names because they are alternate ENTRY POINTS to functionally the same "create a follow-up task" intent
in this codebase, not because task 040 had to choose between forking either — the session-agnostic entry
point was always the correct (and only reachable) one for a background trigger. The eval family's
`reuse-not-fork` case (`CREATETASK-004`) proves this structurally: `CommunicationEnrichmentService` calls
`_actionSeam.CreateTaskAsync`, never constructs a `task`/`sprk_event` `Entity` directly, and never references
`CreateTaskNodeExecutor`.

**Note on entity type**: mechanism (2) creates a CRM `task` activity (not `sprk_event`, unlike mechanism (1)'s
chat-loop tool description). This is the SHIPPED behavior of `TaskActionCore` (unchanged by this task) — task
040 does not introduce this distinction, it inherits it from the existing facade. If a future task wants Job
C's created records to be `sprk_event` rows instead (matching the To Do architecture), that is a change to
`TaskActionCore`/`IActionSeam` itself, out of scope here (would need its own §11 justification).

**Not the 031 OBO problem**: unlike task 031 (blocked pending OBO plumbing for customer-record field
mutation), a system-generated task create from an email is legitimately app-only — `IActionSeam` has no OBO
requirement (it is registered unconditionally, Singleton, no per-user token dependency) — so this task does
not carry that blocker.

---

## 4. Citation approach

Every created/proposed item carries a citation to the source communication:

- **Created (non-deadline) tasks**: `BuildCreatedTaskDescription` appends a provenance line to the task's
  `description` — `Provenance: source communication {communicationId}; {locator}. "{quotedText}"` — mirroring
  the chat-loop `create-task` Binding's own provenance-line convention (`"Provenance: source document ...;
  source analysis ..."`), adapted to cite a communication instead of a document.
- **Pending (deadline-bearing) proposals**: the full citation (`source`/`locator`/`quotedText`) lives in the
  `Proposed` row's `sprk_aisuggestion` JSON, same shape as Job B's proposal JSON.
- **NFR-06 verify-cited-text**: every candidate's `quotedText` is re-located in the source
  (subject+body+attachmentText) via the SAME `CitationVerifier.IsCitedTextPresent` Job B uses — a candidate
  whose citation cannot be verified is dropped before either branch runs.

---

## 5. Golden-utterance eval location (NFR-07)

New net-new family (same pattern as `email-propose`/`email-triage`):

- **Seed**: `tests/integration/contract/Eval/create-task-from-email-eval-cases.json`
  (`create-task-from-email-eval@v1`, 6 cases across `structured-output`, `verify-cited-text`,
  `deadline-confirm`, `reuse-not-fork`, `binding-resolution`, `no-second-pass`)
- **Harness**: `tests/integration/contract/Eval/CreateTaskFromEmailEvalTests.cs`
- **Forcing function**: `tests/integration/contract/Eval/golden-utterances.json` GU-140 (satisfies
  `P2LoopInjectionEvalSuiteTests.FullCatalog_EveryClosedCatalogConsumerType_HasAnEvalFamily`'s
  `ConsumerTypes.All` scan, same pattern as GU-138/GU-139).

---

## 6. Files created / modified

**Created**
- `infra/dataverse/actions/create-task-from-email.action.json`
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ICommunicationCreateTaskAi.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/CommunicationCreateTaskAi.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/NullCommunicationCreateTaskAi.cs`
- `tests/integration/seam/Communication/EmailCreateTaskSeamTests.cs`
- `tests/integration/contract/Eval/create-task-from-email-eval-cases.json`
- `tests/integration/contract/Eval/CreateTaskFromEmailEvalTests.cs`

**Modified**
- `infra/dataverse/sprk_playbookconsumer-rows.json` — `email-create-task` Binding row.
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs` — `EmailCreateTask =
  "email-create-task"` constant + `All`.
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` — Null + real facade
  registration (mirrors `ICommunicationProposeAi`'s registration exactly).
- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationEnrichmentService.cs` — new
  `ICommunicationCreateTaskAi` + `IActionSeam` ctor deps, new "email-create-task" step + private helper
  methods + the sentinel-field constant.
- `tests/integration/contract/Eval/golden-utterances.json` — GU-140 (`ConsumerTypes.All` forcing function).
- 4 existing seam test ctor call sites updated for the two new dependencies
  (`TriagePersistenceSeamTests.cs`, `EmailTriageSeamTests.cs`, `CommsAssessedProducerSeamTests.cs`,
  `EmailProposeSeamTests.cs`) — each passes `new NullCommunicationCreateTaskAi()` +
  `new Mock<IActionSeam>(MockBehavior.Loose).Object` (unrelated to what those tests assert; Job C never fires
  in them since their `ICommunicationCreateTaskAi` double returns no candidates via the Null-Object).

---

## 7. Hand-off to task 041 (attachment-grounded action extraction)

041 grounds its create path on the SAME mechanism this task wires:

- **Facade to extend/parallel**: `ICommunicationCreateTaskAi`/`CommunicationCreateTaskRequest` currently
  takes `AttachmentText` as an optional string already (bounded, same shape as Job B's
  `CommunicationProposeRequest.AttachmentText`) — 041 can either extend THIS Action's grounding to weight
  attachment text more heavily, or (if its extraction shape genuinely differs) author its OWN Action +
  facade following this exact template, reusing the SAME create leg.
- **Create leg to reuse**: `IActionSeam.CreateTaskAsync` (session-agnostic, unconditionally registered,
  app-only) is the create path — do not fork a third mechanism. 041 must apply the SAME deadline-bearing →
  confirm branch (`TaskCandidate.DueDate.HasValue` today; if 041 introduces its own request/candidate shape,
  it must preserve an equivalent nullable-due-date field and the SAME branch semantics).
- **Sentinel-field idempotency pattern**: if 041 also writes to `sprk_emailreviewlog`, reuse
  `LoadOpenProposalFieldsAsync` (unmodified) with its own sentinel-field naming convention (distinct prefix
  from `"__create_task__:"` to avoid semantic collision, even though the two Jobs' rows cannot collide
  functionally since `LoadOpenProposalFieldsAsync` is scoped per `(communicationId, entityLogicalName)` and
  the field-name SET only grows).
- **Uniform audit trail convention**: always write a `Proposed` row first (whether or not the candidate
  auto-finalizes), and a SECOND `Applied` row only on a successful immediate create — never mutate the
  `Proposed` row (append-only).
