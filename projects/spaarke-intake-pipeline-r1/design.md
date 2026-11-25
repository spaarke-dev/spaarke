# Spaarke Intake Pipeline R1 — Design (initial draft for discussion)

> **Status:** DRAFT v0.1, 2026-11-25. For owner discussion. Project created 2026-11-25. Next: resolve §13, then `/design-to-spec`.
> **Project:** `spaarke-intake-pipeline-r1`. Worktree `C:/code_files/spaarke-wt-spaarke-intake-pipeline-r1`, branch `work/spaarke-intake-pipeline-r1`.
> **Origin:** split out of `specialist-legal-agents-r1` (owner, 2026-11-25). The NDA submit-and-return flow turned out to be a shared platform pipeline, not a specialist-agent feature.
> **Evidence:**
> - `notes/code-review-evidence-2026-11-24.md` (three Fable code reviews of origin/master `b005279c4d`, key claims re-checked);
> - `projects/specialist-legal-agents-r1/notes/nda-current-state-2026-11-24.md` (on `work/specialist-legal-agents-r1`);
> - the accelerator engines on `work/spaarke-legal-ai-accelerator-r1` (`src/accelerators/core/engines/`).

---

## 1. Problem

People send content to Legal and expect an outcome back:
- a business user sends an NDA for review and wants a marked-up NDA;
- a law firm submits an invoice;
- a requester files an invention, trademark or policy question through the Front Door.

**Today each kind of intake is built separately, and the most important one is missing.**
- **Separate builds:** six content-intake paths (email, ACS messaging, Console chat upload, Office add-in save, External SPA upload, Front Door), plus a separate invoice pipeline.
- **Five queues** between them.
- **One run record**, used by Office only.
- **No shared routing by content:** only chat picks its AI work from what the content is.
- **Front Door requests trigger nothing.**
- **No "approve, then send back" anywhere.**

We want **one process model** for requests:
- the same records, stages, routing, approval and return for every kind of request;
- different paths only where the use case technically needs them, not because something is already built.

## 2. Goals and non-goals

**Goals**
1. **One request pipeline:**
   - doors (email, Front Door, Copilot agent, Teams via the agent, Console upload) feed one orchestrator;
   - it routes each request to a **harness** for its kind of work;
   - an attorney approves where required;
   - the result goes back through the door it came in by.
2. **NDA review as the first harness.** It produces a marked-up Word file plus a clause table, reusing the accelerator's tested redline engines.
3. **Invoice intake on the same model,** unless a technical reason says otherwise (§7).
4. **The other Front Door types** (invention, trademark, policy) become harnesses later with no new plumbing.
5. **Clean up** what the code review found along the way (§10).

**Non-goals**
- Forcing every content path through the request pipeline (§3.1).
- Compose as the NDA front end.
- New review AI. The review content and the specialist (attorney profile) come from `specialist-legal-agents-r1` and the accelerator.
- Auto-send. An attorney always approves before anything leaves.

## 3. Architecture

### 3.1 Two levels, not one funnel

| Level | What it is | Examples | Shape |
|---|---|---|---|
| **Content filing** (exists; tidy, don't rebuild) | Content becomes a Spaarke record and is enriched in the background. Nobody is waiting for an answer | An email and its attachments filed to a matter; an Office save; an External SPA upload | Store → profile → index (the shared `AppOnlyDocumentAnalysis` + RAG tail). Fire-and-forget |
| **Request processing** (new; the single model) | Someone asks Legal for an outcome and expects it back | An NDA for review; an invoice; a Front Door request | Request → run → classify → route → harness → approve → return → record |

Every request also files its content, so it reuses the filing level. Most filing is not a request, so it never creates a run.
- **Console chat upload stays interactive.** It is a live conversation, not a pipeline.
- **Email is a door into both levels.** It always files. It creates a request when the mail is a request: sent to an intake mailbox, or classified as one, for example an NDA arriving at the legal mailbox.

### 3.2 Records

| Role | Entity | Notes |
|---|---|---|
| **Request** | `sprk_servicerequest` (owner decision 2026-11-24) for legal requests. **Invoices:** `sprk_invoicesubmission` keeps the request role (§7) | **Add to `sprk_servicerequest`:** channel, stage (submitted → in review → awaiting approval → returned → closed), assigned attorney, current run. **Coordinate:** the ontology project is adding direction, disposition and response-due date to it |
| **Run** | `sprk_processingjob` (owner agreed 2026-11-25), extended | **Add:** request lookups (`sprk_servicerequest`; `sprk_invoicesubmission` if invoice moves), output-document lookup, job-type values for request runs, the stage set. One request, several runs (each revised draft is a new run) |
| **Content** | `sprk_document` | The submitted files and every generated output, filed against the request. The Front Door already shows a requester the documents on their request |
| **Results** | `sprk_analysis` + `sprk_analysisoutput` | Structured AI results (classification, review findings, clause-table data), linked to the run |

`sprk_processingjob` is used today only by the Office add-in. A note on `sprk_backgroundjob` says it is "Office-scoped — do NOT overload". The owner chose to extend it, so the job type separates Office runs from request runs, and the note gets updated.

### 3.3 Stages

```
 Doors           email · Front Door · Copilot agent (Teams via the agent) · Console upload · invoice routes
   │  find or create the Request, file content (filing level), create a Run, enqueue
   ▼
 Orchestrator    1 Receive → 2 Store → 3 Classify → 4 Route
                                                      │ harness chosen by (request type + document type)
                 5 Analyse → 6 Build outputs          ◄── the harness owns 5–6 (and its own persist rules)
                 7 Persist (one transaction)
                 8 Approve   (optional per harness) → run waits in AwaitingApproval; approval resumes it
                 9 Return    (through the originating door)
                10 Record    (Decision Record, terminal status last, requester told once)
```

**Rules copied from the invoice pipeline, not its code:**
- step 0: payload → lock → re-read → terminal-first → flag → resume from stored state;
- "Processing" is written before any AI call;
- a split into analysis that writes nothing → one transaction → idempotent after-steps with the terminal status written last;
- deterministic ids;
- AI is never re-run on a resume;
- the final delivery is mapped to an outcome;
- the requester is notified exactly once;
- a marker is written before any send;
- intake order is stage → row → enqueue;
- admin-attention text is written for anything held.

### 3.4 Routing and harnesses

- **Routing reuses the Binding table** (`IConsumerRoutingService`). `RoutingContext` today matches only `mimeType` and `documentType`. **Add a `requestType` match key.** Classification supplies `documentType`.
- **A harness** is a registered handler that owns the analyse and build steps, plus any domain transaction, for one kind of work. Its contract is new: inputs (the request, the run, the content), outputs (documents, results, whether approval is needed, the return payload) and its persist hook. Adding a request type means adding a harness and a Binding row.

### 3.5 Doors and returns

| Door | Intake | Return |
|---|---|---|
| Email (intake mailbox or classified request) | Existing Graph intake; new step that creates a request | Reply from the shared legal mailbox with the outputs attached (`CommunicationService` shared-mailbox send is app-only and takes `AttachmentDocumentIds`, 35 MB). **Check:** In-Reply-To threading (§10) |
| Front Door (External SPA) | Existing `POST /servicerequests` + document upload; new: enqueue a run when the draft arrives | Output documents filed on the request (already visible to the requester), plus a message regarding the request so the bell fires (the bell fires on messages, not documents) |
| Copilot agent (and Teams via the agent) | New gated MCP tools on the Spaarke MCP server (mcp-r1): submit for review, request status | Status tool / widget with the output links |
| Console upload | Existing upload; new "submit as a request" action | The run's outputs on the request |
| Invoice routes | Existing SPA / internal / from-document / from-email routes | Existing firm notices and SPA status (§7) |

**Sending identity:** the shared legal mailbox, app-only, for every door (recommended). The alternative, the attorney's own mailbox, needs their sign-in at approval.

### 3.6 Approval

There is no generic "approve, then resume" today. New:
- A Work Item for the assigned attorney, regarding the request (existing task seam: `IActionSeam.CreateTaskAsync`, `sprk_event` tasks).
- The attorney opens the output `.docx` in Word, edits it if needed, and approves or rejects.
- **Approve:**
  - `DecisionRecordWriter` records the decision;
  - the run moves to Approved;
  - the return stage is enqueued;
  - the version returned is the approved one.
- **Reject or rework:** the run closes; the attorney can start a new run or reply manually.

## 4. Orchestration host (ADR-052 decision, 🔔)

ADR-052 permits **Durable Task**, in its own host, never inside the BFF, for "multi-step, long-running, human-gated or fan-out orchestration". It says a "hand-rolled Service Bus + state machine needs a written reason". A request run is multi-step and human-gated, so this needs an explicit choice.

| Option | For | Against |
|---|---|---|
| **A. BFF: ADR-004 jobs + the run record as the state machine** (recommended) | **B2:** routing, AI dispatch (ActionRunner), the Dataverse write path and ownership all live in the BFF. **B3:** low volume. The approval wait is a status on the run record, not a held orchestration (approval enqueues the next stage). It follows the invoice pattern | Hand-rolled: needs the written reason in the Placement Justification |
| B. Durable Task Scheduler (Durable Functions, .NET isolated, own host) | Native waits for external events, retries, fan-out | Activities may not reference `Sprk.Bff.Api` or call BFF endpoints, so routing and AI code would need extracting to `src/server/shared`, or a queue round-trip to the BFF for every step. Also: first-Function setup, a deployable per stamp, owner approval |

## 5. NDA harness: reuse the accelerator's redline engines

The accelerator already has the redline pipeline the owner remembers (`src/accelerators/core/engines/`, tested, on the accelerator branch, **not on master**):
- **`ooxml_redline.py`** (732 lines). It applies an **edit plan** (`edit-plan.schema.json`) as surgical tracked changes and outputs three files:
  - **Redline:** tracked changes, a margin comment per edit, bookmarks;
  - **External:** the same tracked changes without our comments; the counterparty's comments stay;
  - **Clean:** all changes accepted.
- **`summary_table.py`** (285 lines). It adds the page-1 "Review summary" table, with one row per playbook topic and a coloured rating.
- **`check_outputs.py`** (429 lines). It verifies the outputs against the plan.
- **`segment_clauses.py`** splits the agreement into clauses.

**The flow:** the model writes the edit plan → the engines apply it, render the clause table and verify. That is exactly "a revised Word file plus a table".

**Anchoring.** The edit plan anchors by paragraph and exact text:
- the text must occur exactly once, on word boundaries;
- a span must not cross an existing revision;
- the engine refuses anything else.

The platform's C# engine (`ComposeShadowPatchEngine`) anchors by paragraph id, and the platform's `agreement-review` output has neither replacement wording nor anchors. So the accelerator path is the better fit.

**Where the engines run (decision, 🔔):**

| Option | For | Against |
|---|---|---|
| **A. Run the same Python engines in a Container Apps Job** (ADR-052: "heavy or long-running work needing a custom runtime") | One engine on every surface: Cowork and the server produce identical output, and the accelerator's tests and the cross-surface golden-set gate apply unchanged | A new deployable per stamp; Python runtime and supply chain; owner approval; ADR-052 guardrails |
| B. Port to C# in the BFF (the specialist program's original T2 plan) | No new runtime | Two implementations to keep identical; port effort (about 1,600 lines of engines plus tests) |
| C. Adapt `ComposeShadowPatchEngine` | Exists in C# | Anchoring model differs; still needs the plan → anchor step and the table and checks |

Lean: **A**, decided after a spike (§9 phase 0).

**Review content.** Review against the **one NDA standard** (the accelerator's topic-keyed playbook is recommended as master, still an open owner decision). Today's model step is the `agreement-review` Action, which must become callable app-only: `agreement-classify` and `agreement-review` run only inside a chat session (`SessionDispatchOrchestrator`) today. The step must write an edit plan rather than flagged sections alone. Later, `specialist-legal-agents-r1` supplies the attorney profile layers.

**ADR-049 check.** ADR-049 forbids text search in the **Compose** write path. The edit plan's exact-text anchoring is not Compose, but confirm the ADR's scope in `/adr-check`.

## 6. Classification

Email and Console intake need "is this a request, and what kind":
- reuse `agreement-classify` (with the `sprk_agreementtype` registry) app-only for agreements;
- reuse the request-type answer from the Front Door;
- invoices reuse `invoice-classify`, whose job handler (`AttachmentClassificationJobHandler`) exists but has no producer today. The email door's classify step is where it would plug in.

## 7. Invoice intake: should it move onto the shared model?

**What is legitimately invoice-specific** (stays domain code in any design):
- the Content Understanding read;
- line coding and code-set matching;
- rule evaluation and holds;
- the invoice/lines/approval transaction and its commit gate (undo);
- tracking numbers and firm-visible resubmission numbering (`INV-002.1`);
- reason codes (an SPA contract);
- firm notices;
- the invoice approval lifecycle.

That would be an **invoice harness**.

**What is orchestration** (would move to the shared model): the intake order, the step-0 shell, the run status and dead-letter handling, admin attention, notify-once, and terminal-last.

**Why moving is worth considering:**
- one way to operate and monitor every run;
- one place for the retry and resume rules;
- the shared email door gives invoices the **Mailbox** channel they have reserved but never wired.

**What it costs:**
- re-proving behaviour that went through many adversarial fix rounds;
- "linked submission is never Failed" and resume-on-link must map onto the run record;
- `sprk_invoicesubmission` keeps the request role. It is firm-facing, with field-secured findings, so it is not replaced.

**Timing:** eBilling is **not live yet**. Its dev deployment (tasks 079, 112, 136, 153), end-to-end test (160) and UAT (163) are still open. Moving before any customer rollout is far cheaper than after.

**Recommendation** (owner to decide):
1. Build the shared orchestrator from the invoice pipeline's rules.
2. NDA is the first harness.
3. Invoice moves onto it as the second harness in this project, with eBilling's existing tests and end-to-end run as the regression proof.
4. Do this after eBilling's UAT on the current code (a known-good baseline) and **before** any customer rollout.
5. Coordinate with the eBilling owner.

The alternative, migrating before eBilling's UAT, saves one UAT cycle but tests two changes at once.

## 8. Component justification (CLAUDE.md §11), first pass

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| Columns on `sprk_servicerequest` | Front Door request | Extending it | Requests have no channel, stage, attorney or run |
| Columns and job types on `sprk_processingjob` | Office run record | Extending it | No record per run; can't route, resume or show status |
| Request orchestrator job | `IJobHandler`, invoice pipeline | New handler on the existing framework; invoice code is type-bound (§7) | Front Door and email requests are never processed |
| `requestType` routing key | `RoutingContext` | Extending it | Can't route by request type |
| Harness contract | None (invoice is closed) | — | Each request type would need its own pipeline |
| Approve-then-resume | Task seam, Decision Record | New action | Nothing can wait for an attorney and continue |
| MCP submit and status tools | mcp-r1 server | New tools | The Copilot/Teams door doesn't exist |
| Engine host (if §5 A) | None | — | Server-side marked-up NDA impossible, or a port |

## 9. Phasing (first cut)

| Phase | Scope | Gate |
|---|---|---|
| **0 Spikes and cleanup** | (a) Python engines in a Container Apps Job vs a C# port. (b) App-only `agreement-review` writing an edit plan on 3 synthetic NDAs. (c) §10 cleanup items 1–8 | Engine host chosen; edit plan valid on all 3; cleanup PRs merged or filed |
| **1 Core + NDA via Front Door and Console** | Records, orchestrator, routing key, NDA harness, approval, Front Door return, Decision Record | An NDA submitted on the Front Door comes back marked up after approval, on dev |
| **2 Email and Copilot doors** | Intake mailbox, classify-to-request, reply with attachments (threading checked), MCP submit/status tools | Same NDA end-to-end through email and through the agent |
| **3 Invoice harness** | Move invoice orchestration onto the model (§7) | eBilling tests and end-to-end run unchanged |
| **4 Other Front Door types** | Invention, trademark, policy harnesses as the owner prioritises | — |

## 10. Cleanup found in the code review (in scope; owner: this project unless noted)

Each item: confirm, then fix or file. These are master `b005279c4d` facts. None of them was caused by this work.

| # | Finding | Evidence | Proposed action |
|---|---|---|---|
| 1 | Job handlers with no producer in `src`: `AttachmentClassification`, `ProfileSummary`, `EmailAnalysis`, `DocumentProcessing` (self-described sample) | grep: only the constant and the DI registration | Check `infra/`, Functions, Power Automate and Dataverse for outside producers. `AttachmentClassification` → wire it in the email door (§6) or confirm with eBilling. The rest: remove, or record why kept |
| 2 | `StartupDiagnostics.cs:84` expects `ProcessEmailToDocument` and `BatchProcessEmails`, which don't exist | read | Fix the list to match the registered handlers |
| 3 | `DeadLetterQueueService` (list, re-drive) is registered but nothing calls it | grep | Decide: wire it into an admin view (useful for runs) or remove |
| 4 | `DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md` `:129/:177/:265` describe the app-only profile as the node playbook; code runs a direct Action (`AppOnlyAnalysisService.cs:639`) | read | Update the doc |
| 5 | Email bypasses `ICommunicationChannelIngestor`; profile idempotency keys differ (`DocAnalysis:{id}` vs `analysis-{id}-documentprofile`); retry ceilings differ (2 vs 3) | read | Align the keys (a duplicate profile is possible across paths); decide whether email joins the seam |
| 6 | `agreement-classify` and `nda-review` Binding rows have no `ConsumerTypes.All` entry; the health check may report Unhealthy | read; health effect not checked | Add constants as part of making them app-only |
| 7 | Outbound "reply" email may not set In-Reply-To (no `CreateReply`; only a marker header) | inferred | Confirm with a test send; fix if so (affects every reply Spaarke sends) |
| 8 | `sprk_backgroundjobrun` deployed but unused; `sprk_backgroundjob` description says `sprk_processingjob` is Office-scoped | data-model doc, entity XML | Update the description when `sprk_processingjob` is extended; record the unused table's intent |

## 11. ADR tensions (to resolve before the spec)

| ADR | Tension | Proposed path |
|---|---|---|
| ADR-052 | Hand-rolled orchestration of a human-gated flow; a possible Container Apps Job | **C (comply)** with the written reason (§4 A). The engine host needs owner approval if §5 A |
| ADR-028 A5 / ADR-052 identity | The background run acts with no user present | **C:** app-only, attributed to the request and the assigned attorney. The send is from the shared mailbox after a human approval |
| ADR-049 | Exact-text anchoring in the NDA edit plan | **C** if ADR-049's scope is Compose only; confirm |
| ADR-039 | Harness AI steps | **C:** closed catalogue; harnesses call cataloged Actions only |
| ADR-004 / ADR-017 | Job idempotency, explicit terminal status | **C:** the invoice rules (§3.3) |

## 12. Dependencies and coordination

- **specialist-legal-agents-r1:** the NDA specialist and attorney profiles plug into the NDA harness later. That program's design §3.6 "Autonomous runs" is delivered here.
- **Accelerator r1:** the engines, edit-plan schema and NDA standard (not on master yet; merge or vendor them). It also decides which NDA standard is the master.
- **Ontology r1:** columns on `sprk_servicerequest`; the request vs Inquiry distinction (owner: different concepts).
- **eBilling r1:** §7 timing and the regression proof.
- **External Access (Front Door):** the request record, document upload and the return message.
- **mcp-r1:** the MCP submit and status tools.
- **BFF hygiene (§10 of root CLAUDE.md):** Placement Justification, publish-size delta and a hot-path declaration (BFF = Y; External SPA = Y; SpaarkeAi = Y only if the Console gets a submit action).

## 13. Open questions for the owner

1. ~~Project slug and name~~ **Decided 2026-11-25:** `spaarke-intake-pipeline-r1`.
2. Orchestration host: BFF jobs + run record (recommended) or Durable Task?
3. NDA engines: Python in a Container Apps Job (lean) or a C# port? A spike decides.
4. Invoice: move as phase 3, after eBilling UAT and before customer rollout (recommended), or earlier?
5. Sending identity: the shared legal mailbox (recommended)?
6. NDA standard master: the accelerator's playbook (recommended)? Carried from the NDA note.
7. Email door: an intake mailbox only, or also classify mail arriving at existing mailboxes?
