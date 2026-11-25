# NDA in Spaarke today: how to review one, how to write one, and the submit-and-return flow

> **Date:** 2026-11-24. **Why:** owner request: "we have already built an extensive NDA review process including our playbooks", so review what exists and what the accelerator already tested before this program plans any more NDA work.
> **Sources:** `ai-advanced-capabilities-nda-r1` (closed, deployed to dev), `ai-advanced-capabilities-agreements-r1` (deployed to dev; e2e and wrap-up still open), External Access r3 intake, the BFF on `origin/master` (`e70ff38894`), and the accelerator worktree (`work/spaarke-legal-ai-accelerator-r1`, PR #2528, not merged).
> **Method:** three read-only reviews. I re-checked the main claims at source: `nda-review` SKILL.md, the `component-protocol.md` 2026-11-02 decision, `SendEmailNodeExecutor` (OBO, no attachments), the `EventRulesService` launch binding (classify only), `NdaAssessmentWizard.tsx`, and the task-033 Draft Alternative contract.
> **Not opened:** `answer-key/`, real client NDAs, or the `.docx`/`.xlsx` evidence files.

## 1. The short answer

Spaarke already has **three working NDA review routes**. Each has its own strength:

| Route | Where the attorney works | What comes out | Status |
|---|---|---|---|
| **A. Spaarke platform review** (`agreement-classify` → `agreement-review`) | Spaarke Console: Assistant + Compose | A cited risk summary and one advisory comment per flagged clause. Per clause, a **Draft Alternative** that lands as a pending tracked change the attorney accepts or rejects. Then a Review Summary Memo (docx/email) and a Word export with comments and tracked changes | **Deployed to dev**, owner UAT passed (nda-r1). agreements-r1 generalised it to any agreement type |
| **B. Microsoft Word legal plugin + our Word playbook** | Word for Windows desktop | Native tracked changes and comments in the open document ("Apply edits with comments") | **Tested** by the accelerator (Acts 1–2, R1–R7). Our decision is to ship no competing Word skill (`component-protocol.md` L83) |
| **C. Accelerator `nda-review` skill** | Copilot Cowork | A packaged set checked by script: an internal redline with comments plus a page-1 summary, an external redline without comments, a clean copy, an HTML dashboard and an Excel audit pack. Plus `nda-library-review` for batches | **Tested** (T-06/T-07, Act 3, Act 6c). Owner decision 2026-11-02: in Cowork, NDAs route to `nda-review` |

**Writing an NDA has no NDA-specific capability anywhere.**
- In the platform, `NDA-DRAFT` was designed and explicitly deferred out of nda-r1 (`nda-r1/spec.md:28`).
- In Copilot, drafting goes to Microsoft's `draft-new-contract`; our `nda-review` description sends drafting there.
- The accelerator's NDA-drafting test (R-2) was planned but not run.

**The submit → AI review → marked-up NDA → send back flow** has every building block, but nothing connects them. No step runs the review without an attorney in the Console, and no step turns the result into an outgoing document and sends it back. See §5.

**So: no new NDA review agent, plugin or skill is needed.** The real work is:
1. **One NDA standard.** Two differ today (§4).
2. **Connect the submit-and-return flow on the platform** (§5).
3. **Drafting,** if wanted (§3).

## 2. How to review an NDA in Spaarke (today)

### Route A: Spaarke Console (the platform route)

Sources: nda-r1 README "What this delivers"; agreements-r1 README; nda-r1 notes 023, 033 and 041.

1. Upload or open the NDA in the Spaarke Console Assistant, then ask "review this document" (or use the classification card).
2. `agreement-classify` (Reasoning tier) confirms that it is an agreement and identifies the type. If confidence is below 0.85, or the document is composite, it asks you to confirm. For `nda`, it binds knowledge pack **KNW-011, the Spaarke Baseline NDA Standard** (`infra/dataverse/sprk_agreementtype-rows.json`).
3. Pick Quick or Thorough. `agreement-review` (Reasoning tier, advisory under ADR-039) then runs **once**. It returns `{overallRisk, flaggedSections[{sectionRef, quotedText, riskLevel, explanation, standardRef}]}`, with risk rated Low/Medium/High/Critical from the represented party's side.
4. The one run fans out on the client, with no second model call:
   - a cited summary panel in the Assistant and in Compose;
   - an advisory comment in the Compose gutter for each flagged clause. Each cites the standard clause, and `/api/ai/nda-standard` shows its text.
5. In Compose, use **Draft Alternative** on any flagged clause. It writes **one pending tracked change**, which you accept, reject or undo. It is never a document-wide auto-edit (task 033).
6. Generate the **Review Summary Memo**. It is persisted to `sprk_analysisoutput`; download it as docx, or email it with the EmailComposer (agreements-r1 tasks 050/051).
7. Save to SPE (versioned) and export to Word. The export carries:
   - comments that mirror the gutter, with a configurable author (task 052; done and deployed, but I have not verified it in code);
   - your accepted or pending tracked changes;
   - an appended Summary Page (task 041).
8. Reopening the document recalls the review with no model call (task 030).

**Limits:**
- The standard is org-wide v0.1, **not ratified by counsel**, with no per-firm or per-attorney positions.
- Proposed wording is generated per clause on request. The review itself returns no replacement text, so no full redline comes out in one step.
- Not available in Word or Copilot. The MCP server has no NDA/agreement tool, and the Word add-in has no review feature.

### Route B: Word desktop with Microsoft's legal plugin

Sources: accelerator `notes/poc-e2e/results.md`, `synopsis-word-and-cowork.md`.

1. Attach our four-column Word playbook to each review. Don't use Word's "saved playbook": it freezes a copy with no version check (R6, gap #32).
2. Run *Review contract*, then *Apply edits with comments* to get native tracked changes and comments in the open document.

**Measured:**
- 20/24 (R3) and 19/24 (R7) playbook adherence, with run-to-run variation on 3 topics (gap #29).
- Import drops compromise positions and topic IDs (gaps #30, L24).
- It sometimes deletes whole sections without renumbering (R3).
- It accepts only one attached file (gap #37).
- Word for Windows desktop only. Word web has no legal plugin, and Word never runs our skills (S17, L21, L25).

### Route C: Copilot Cowork with the accelerator skills

Source: `src/accelerators/practices/nda/skills/nda-review/SKILL.md`.

1. Say "Review this NDA."
   - It uses the attorney's playbook from `Documents/Cowork/Playbooks/nda/`, or the Spaarke baseline positions if there is none, plus `legal-profile.md` from `accelerator-setup`.
   - Scripts:
     - segment the clauses;
     - rate every topic RED/YELLOW/GREEN/N/A;
     - write an edit plan;
     - show ONE approval card;
     - then build and check the redline set.
2. **Measured:**
   - 12 of 13 key topics (Act 3), at 118–166 credits per review.
   - Library review: 375/384 cells across 16 NDAs, 185 credits (Act 6c).
   - A playbook version change is picked up (Act 5).
   - Injection and counterparty-markup cases pass (T-10).
3. **Guardrails:** the agreement is data, not instructions; the original is never edited; the counterparty is never emailed.

## 3. How to write an NDA (today)

| Option | What it does | Status |
|---|---|---|
| Microsoft `draft-new-contract` (Cowork) / `@Draft-new-contract` (Word) | Drafts from a precedent you supply. Planned precedent: the Acme model form (`playbook-library-and-grounding.md` L123) | Available with the Copilot seat. **Our NDA-from-model-form test (R-2) not run** |
| Spaarke `compose-draft-document` | A general "draft a full document" Action in Compose, grounded in session documents | Exists. Not tuned for NDAs, and its NDA quality is untested |
| External Front Door **NDA Assessment** wizard, "issue" path | An internal requester asks Legal for a new NDA (counterparty, purpose, terms, timing) | Built (External Access r3, task 052). **No document generation**, by owner decision 2026-10-12 |
| Spaarke `NDA-DRAFT` (draft from parameters, from the baseline's required terms) | — | **Design only; deferred** out of nda-r1 |
| Templates (`sprk_template` + `{{token}}` merge, Compose r8) | Would give "our NDA form, filled in" | Design draft; not built |

**Gap:** we have no firm NDA form or template in the repo. The playbook's model language exists for only 5 of 24 topics (accelerator `mvp-nda/results.md` L24).

## 4. Two NDA standards, which must become one

| | Platform (route A) | Accelerator (route C, and the Word playbook for route B) |
|---|---|---|
| Name | Spaarke Baseline NDA Standard **v0.1** (KNW-011) | "Spaarke NDA – Mutual" **v1.2** (`practices/nda/positions/baseline-mutual.md`) |
| Shape | Clause rubric B1… (Required / Acceptable / Red flag), Low–Critical risk | 24 topics keyed by `topics.yaml` IDs (`nda.*`), RED/YELLOW/GREEN, four-column export |
| Status | Not ratified by counsel | `status: mvp-test`, not attorney-approved |

The ontology's term for this is a **Playbook** (D-209: one concept, several formats).
- The platform's KNW-011 is the JPS-side grounding.
- The accelerator's file is the legal-playbook master.

**They are different content.** A Spaarke-reviewed NDA and a Cowork-reviewed NDA can reach different conclusions. Reconciling them into one master, rendered to each format, is the first real piece of NDA work this program has. It is the "positions master" in design §0 / Q2.

## 5. Submit → AI review → marked-up NDA → send back

| Stage | Exists? | Evidence | Gap |
|---|---|---|---|
| **1. Submit** | **Yes, three ways** | (a) External Front Door NDA Assessment wizard, "review" path; the requester then adds the counterparty draft under Documents (`NdaAssessmentWizard.tsx`, `ExternalIntakeEndpoints.cs:110`). (b) Inbound email to a monitored mailbox; attachments filed as `sprk_document` (`IncomingCommunicationProcessor.cs`). (c) Upload in the Console | — |
| **2. Recognise it is an NDA** | **Partial** | `agreement-classify` (deployed) and `DocumentClassifierHandler` know "NDA"; the email path runs profile analysis (`AppOnlyDocumentAnalysisJobHandler`) | Classification only drives chat cards. Neither intake path acts on "this is an NDA" |
| **3. Run the review without an attorney present** | **No** | The event-rules launch binding is `document_uploaded → [chat-classify]` only (`EventRulesService.cs`). Background jobs cover profile and indexing only. agreements-r1 lists "autonomous/email intake (future)" | A background, app-only `agreement-review` run triggered by an NDA intake. This is new background work (ADR-052 → an ADR-004 `IJobHandler`) |
| **4. Produce the marked-up NDA** | **Partial** | The engine writes native tracked changes and comments (`ComposeShadowPatchEngine.cs`, `ComposeDocumentRenderer.cs`). The comment export mirror is task 052. Draft Alternative makes one tracked change per clause, interactively | A server-side step that turns `flaggedSections` into a document: a comment per flag, plus proposed wording as tracked changes for the clauses that need it. The review returns no replacement text, so this needs a "propose wording" pass (reuse Draft Alternative's prompt and the standard's model language) |
| **5. Attorney approves before anything goes out** | **Partial** | Compose accept/reject; Signals, Work Item, Decision Record write paths exist (`DecisionRecordWriter.cs`) | A Work Item, "NDA review ready for approval", that opens the marked-up draft in Compose. A send that happens only on approval. This is required: the review is advisory (ADR-039), and the accelerator's rule is "never email the counterparty" |
| **6. Send back to the requester** | **Partial** | `CommunicationService` sends Graph mail with attachments (35 MB) and files it against the record. The memo EmailComposer is interactive. `SendEmailNodeExecutor` has **no attachments** and is **OBO-only** | An approval-driven send of the marked-up docx plus the memo, as a reply on the intake thread (email) or back to the requester's Front Door request (External SPA: today it notifies, but has no document return path) |
| **7. Record it** | **Yes (parts)** | Ledger/`sprk_analysisoutput`, the communication filed on the record, Decision Record | Tie the run, the approval and the sent version to the request |

**Shape of the work (a sketch, not a design):** intake (any of the three) → classify as NDA → background `agreement-review` against the one standard → server builds the marked-up docx (comments + proposed tracked changes + summary page) → Work Item to the attorney → attorney edits and approves in Compose → send to the requester with the memo → Decision Record.

**Almost all of it is platform (BFF + Console) work.** None of it needs a Copilot agent. Copilot (routes B/C) stays the attorney's own desk tool.

**Correction (2026-11-24, owner question).** The sketch above calls the platform's existing `agreement-review` directly. That skips the specialist. In this program's design the flow **is the specialist's autonomous run** (design §3.6): the same NDA specialist (practice content + the NDA function + the owning attorney's compiled profile L0–L4 + run record) that the Chat area agent uses, started by an intake event instead of by an attorney. It runs at autonomy level **prepare**: it builds the marked-up draft and memo and stops at the approval gate; it never sends. It is a fixed procedure, not an open-ended planner (ADR-039 closed catalog). The background run is app-only and attributed to the attorney (T-1, path C); the send happens on the attorney's own approval click. Design §4 puts autonomous intake in Phase 3; doing the "prepare" level in Phase 1 needs T-1 only, not T-2 (Agent ID). Auto-send stays Phase 3 with counsel sign-off.

## 6. Already tested: do not redo

The accelerator ran these between 1 and 11 Nov:
- **Cowork MVP:** T-01 to T-15.
- **POC Acts 1–7:**
  - playbook creation;
  - Word import and review R1–R7, including the saved-playbook freeze;
  - Brightwater routing T-P2/T-P10/T-P13;
  - `summarize-redlines` / `draft-legal-memo` / `explain-legal-concepts` / PDF review;
  - playbook versioning;
  - library review 6a–6c;
  - legal hold W1a/W1b/W3/W5.
- **DA-1 agents.**
- **Tier round 1** (W-1, G-1, G-2).
- **Test 1–3.** Their comparison is not yet recorded, pending the owner confirming whether Test 3 started from a fresh copy.

**Not yet run** (pick up rather than re-plan):
- T-P1, T-P2's five-prompt matrix (now our G6), T-P7, T-P8, T-14;
- WP-0 to WP-4 (our G4 waits on WP-0–WP-2);
- round 2: U-1, **R-2 (NDA drafting from the model form)**, R-3, G-5;
- C-1 to C-3, A-1 to A-3;
- team and IT scripts.

**Consequences for this program's Phase 0:**
- **G1** (profile adherence) is already partly answered for NDA: Word 19–20/24 with variation, Cowork 12/13 and 99% on the library. Rerun only what is new: per-attorney profiles and the ratchet.
- **G6** = the accelerator's T-P2 (already agreed).
- **G4** waits on WP-0–WP-2.

## 7. Questions for the owner

1. **One standard:** which is the master, the platform's KNW-011 rubric or the accelerator's `topics.yaml` playbook? Recommendation: the accelerator's topic-keyed playbook. Word and Cowork already read it, and the platform pack would be rendered from it.
2. **The submit-and-return flow:** make it the NDA deliverable of this program (Phase 1), in place of a Copilot NDA agent? It is platform work in the BFF and the Console.
3. **Drafting:** build NDA drafting (a firm NDA form + template merge, or `NDA-DRAFT`), or rely on Microsoft's `draft-new-contract` with our model form? Run R-2 first either way.

## 8. Proposed shape after the owner discussion (2026-11-24, a proposal, not decided)

Owner points: separate how a request starts (email, Teams or Front Door form, Copilot agent) from the analysis; output is a **Word file plus a clause table**, not Compose; treat it as a **shared intake pipeline** that routes content to the right AI harness, reusing what invoice, email and document intake already do.

**What exists to reuse (origin/master, 2026-11-24):**
- **Invoice intake is the model.**
  - `sprk_invoicesubmission`: a channel (SPA, Internal; Mailbox, Outlook and API reserved) and status Received → Processing → Accepted / Rejected / Failed.
  - One ADR-004 job per submission (`InvoiceSubmissionProcessingJobHandler`): lock, re-read and resume rules.
  - The pipeline is a processor (reads and runs AI, writes nothing) → a persister (one transaction) → after-steps (Signals, Decision Record, indexing, notification, terminal status last).
  - The email side classifies attachments (`AttachmentClassificationJobHandler`, `invoice-classify`) and `SubmitInvoiceFromEmailOperation` creates the submission.
- **Email intake:** `IncomingCommunicationJobHandler` (Graph webhook → `sprk_communication`, attachments as `sprk_document`, `.eml`), behind the channel seam `ICommunicationChannelIngestor` (email, ACS messaging; Teams reserved).
- **Document intake:** upload → `AppOnlyDocumentAnalysis` job → playbook chosen by consumer routing (`IConsumerRoutingService`, the Binding table).
- **Front Door:** `POST /api/v1/external/servicerequests` creates `sprk_servicerequest` with `sprk_requesttype` (NDA Assessment, Invention Submission, Policy Question, Trademark Search) and the intake answers. No processing follows today.
- **Routing:** `agreement-classify` with the `sprk_agreementtype` registry; consumer types → Actions/playbooks.

**Proposed pipeline:**
- **Doors** only create a request, attach the content and enqueue one job:
  - email;
  - Front Door;
  - the Copilot agent (a gated MCP submit tool);
  - Teams (through the Copilot agent in Teams, G8-P2; no Teams bot today);
  - Console upload.
- **One request record:** extend `sprk_servicerequest` (§11: existing), adding a channel, a status and links to the input and output documents.
- **One processing job** modelled on the invoice job:
  1. classify;
  2. route by request type plus agreement type to the harness (NDA → the NDA specialist with the owner's profile);
  3. build outputs;
  4. persist;
  5. after-steps (approval Work Item, Decision Record, notification).
- **Return** goes through the door the request came in by, and only after attorney approval.

**Same AI components for every door.** The door changes only intake and return. The attorney's own desk tools (Word legal plugin, Cowork `nda-review`) run in Microsoft's runtime and share the **content** (the one standard, the profile export), not the server code. That is why the one standard and the shared golden set matter.

**Output:** the redlined `.docx` (comments + tracked changes) and a clause table (the review's per-topic result). The OOXML engine under Compose is server-side; whether it runs without a Compose session is **not yet checked**. The attorney opens the `.docx` in Word to edit and approves from the Work Item.
