# Market and technical research: AI tuning, feedback and evaluation for customer admins

> **Date**: 2026-10-14 · **Author**: researcher subagent · **Project**: spaarkeai-tuning-and-feedback-r1
> **Status**: research input for `design.md`. Not a decision record.
> **Evidence labels**: **[Doc]** = vendor product documentation or Microsoft Learn (primary). **[Mkt]** = vendor marketing, blog or press release (vendor's claim, not verified). **[3P]** = third-party blog or aggregator (weak). **[Unverified]** = could not confirm from a primary source. **[Paper]** = peer-reviewed or arXiv research.

---

## 0. Summary

1. **The market pattern for customer admins is consistent.** Admins get five things: (a) a quality dashboard per AI capability, (b) test sets with expected outputs and graders, (c) side-by-side runs of draft vs published versions, (d) per-field or per-skill confidence thresholds that route work to human review, and (e) versioning with publish and rollback. Copilot Studio, Agentforce, ServiceNow, Intercom Fin and Rossum/Hyperscience all ship some version of this set. None of them asks a customer admin to fine-tune a model.
2. **Corrections improve results in one of two ways.** Document-AI vendors (Rossum, Hyperscience) retrain a per-customer model on human-validated fields, and only on human-touched data ("we train on human input (corrections), not previous AI predictions"). LLM-platform vendors (Salesforce, ServiceNow, Copilot Studio, Intercom) do not retrain per tenant. Corrections become curated knowledge, guidance and test cases that admins act on, and the products report accuracy against them.
3. **For Spaarke, the right "learn from corrections" mechanism without fine-tuning is a governed Example Library.** Examples are promoted from corrections, admin-approved, and injected through the existing JPS `examples` slot. A later step retrieves the top-k most similar examples per call from the tenant-partitioned index. Every promotion is gated by an offline evaluation run against a golden test set.
4. **Spaarke already has most of the building blocks:**
   - JPS already supports static few-shot examples (`PromptSchema.Examples`, rendered in `PromptSchemaRenderer.cs` step 8).
   - The BFF already references `Microsoft.Extensions.AI` 10.3.0. `Microsoft.Extensions.AI.Evaluation` is MIT-licensed (10.11.0, published 2026-10-14) and runs headless in .NET.
   - The eBilling design already sets field-level accuracy targets and per-field confidence (`sprk_extractionconfidence`, `sprk_codingconfidence`).
5. **The biggest new risk is privileged content in examples.** A few-shot example drawn from Matter A and injected into a prompt for a user who cannot read Matter A breaks the architecture rule that the AI is not an authorization side-channel (`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` §1.2). Example promotion therefore needs curation, redaction and an access policy before examples are retrievable.
6. **Fine-tuning is not justified for R1.** It is also an explicit non-goal "through 2027" in the same architecture doc. Azure SFT and DPO are available for the gpt-4.1 family, but that family retires on 2027-04-14 (see the researcher memory entry on CU vs DocIntel). GPT-5 fine-tuning is RFT-only and by invitation.

---

## 1. How enterprise products expose AI tuning and quality to customer admins

### 1.1 Microsoft Copilot Studio — Agent Evaluation (GA 2026-03-31)

- **Status**: Agent Evaluation reached GA on 2026-03-31 **[Doc/Mkt]** (Tech Community announcement). Multi-turn evaluation and "evaluate AI action outcome in workflows" were announced for GA on 2026-06-30 **[3P reposts of Message Center; not re-verified]**.
- **Test sets** **[Doc]** (Learn, ms.date 2026-10-05):
  - Up to 100 test cases per single-response test set; each question can be up to 1,000 characters.
  - Created by: AI-generated "quick" (10 questions) or "full" set from knowledge or topics; from the maker's test chat; CSV import (Question, Expected response); manual entry; or **from production "themes"**, which cluster real user questions from analytics (themes are in preview).
- **Test methods (graders)** **[Doc]**:
  - General quality (0–100%, no expected answer needed)
  - Content safety
  - Compare meaning (semantic, with a pass score)
  - Tool use (expected capabilities called)
  - Keyword match
  - Text similarity
  - Exact match
  - Custom (natural-language evaluation instructions plus labels)
  - Several graders can be combined; graders that produce a score take a **pass score threshold**.
- **Results** **[Doc]**: rerun a test set to compare results "over time and iterations". Results are kept 89 days, and CSV export is the retention path. API access to retrieve test sets, trigger runs and track results is offered for CI/CD **[Mkt]**.
- **Governance note** **[Doc]**: tests run as the signed-in maker. Generated test cases can contain sensitive data the maker can see, and any maker on the agent can view the test sets. This is the same privileged-content issue Spaarke will face.
- **How corrections feed improvement**: indirectly. Production themes become test sets, and the maker edits instructions or knowledge. There is no per-tenant model training.

### 1.2 Salesforce — Prompt Builder, Agentforce Testing Center, Observability, Einstein audit and feedback

- **Audit and feedback** **[Doc]** (Salesforce developer blog, 2024-07): Einstein audit and feedback data is stored in Data Cloud as a data model. User actions (**accept as-is, edit, regenerate**) are captured as **implicit feedback**, alongside explicit thumbs. The Trust Layer masks PII before the LLM call and scores responses for toxicity. This is the closest market analogue to Spaarke's "correction = label" idea: the edit itself is the signal.
- **Testing Center** **[Doc/3P]**:
  - Each test case is an utterance plus expected topic, expected actions and expected response/outcome.
  - Test cases can be AI-generated in bulk, and runs are parallel.
  - A 2026 update **[Mkt]** added persona-based multi-turn simulation and **custom evaluations written in plain language with a pass/fail threshold**. Each saved metric appears as a column in the test grid. Test cases can now be edited inline instead of through CSV round-trips.
  - LLM-judge scoring of 0–5 with a pass at ≥3 is reported by **[3P]** only.
  - Limits differ between sources (500 vs 1,000 cases per job) **[Unverified]**.
- **Observability (2026)** **[Mkt/3P]**:
  - Session tracing writes turns, LLM calls, actions and guardrail checks into Data Cloud.
  - "Agent Optimization" applies **LLM-as-judge quality scores per session and per intent** (high/medium/low/very low, with an explanation).
  - Agent Health Monitoring alerts on thresholds.
  - Salesforce says Observability is unmetered across Agentforce SKUs **[Mkt]**.
  - GA dates conflict between sources **[Unverified]**.
- **Prompt Builder**: prompt templates grounded in CRM or Data Cloud records, with an interactive preview against a chosen record **[Doc, Spring '24]**. Template versioning and activation exist in-product **[Unverified detail for 2026]**.
- **How corrections feed improvement**: telemetry and test sets. There is no customer-visible per-tenant retraining of the LLM.

### 1.3 ServiceNow — Now Assist Skill Kit and AI Control Tower

- **Skill Kit "Evaluate a prompt"** **[Doc]** (Yokohama/Zurich/Australia docs):
  - The Prompt performance tab has an Evaluation runs sub-tab.
  - A dataset is built from a table or a Now Assist Data Kit collection.
  - An evaluation run selects prompts × dataset × metrics.
  - Human Feedback (thumbs plus comments) is the default metric.
  - Community posts describe additional **correctness**, **faithfulness** and latency metrics, plus **automated prompt optimization** **[3P/Unverified]**.
  - Evaluations are UI-run and not schedulable out of the box **[3P]**.
- **AI Control Tower** **[Doc]**:
  - AI Skills Approvals: a review and approval workflow for skills, models and datasets.
  - Health tab: guardrail monitoring for offensive content and prompt injection.
  - Evaluation tab for virtual-agent interaction quality.
  - May 2026 (Knowledge 2026): Control Tower expanded to "discover, observe, govern, secure, measure", with runtime observability via the Traceloop acquisition **[Mkt]**.
- **Pattern worth copying**: **approval workflow before a skill or prompt version goes live**, which is governance separated from authoring.

### 1.4 Intercom Fin and Zendesk (customer-service AI: the most mature "admin tunes AI" UX)

- **Fin** **[Doc]** (Intercom help centre):
  - **Resolution rate** (resolved without handoff) and **CX Score**. CX Score is an AI-assessed score on every conversation, with a per-conversation explanation; Intercom claims "5x more coverage than CSAT" **[Mkt]**.
  - Outcome taxonomy: resolved, procedure handoff, escalated, user-requested vs rules-driven handoff.
  - Three test tools:
    - **Batch tests**: up to 50 questions; the admin rates each answer Good/Acceptable/Poor; used for content coverage and pre-launch QA.
    - **Simulations**: an AI-simulated customer runs an end-to-end Procedure and is judged against admin-written success criteria; monthly run quota.
    - **Previews**: manual testing.
  - Admins tune through **Guidance** (natural-language instructions), **Procedures** and content, not models.
  - Intercom documents one false-failure cause: overly rigid success criteria. Its advice is to judge the final outcome, not intermediate steps **[Doc]**.
- **Zendesk** **[3P]**:
  - "Automated resolution" is counted only after an LLM verifies the issue was resolved.
  - Quality Assurance (Quality Score) extends to AI agents.
  - Product packaging changed in May 2026 (Essential/Advanced tiers merged) **[3P/Unverified]**.
- **Lesson**: **define one outcome metric per capability that the business understands** (resolution rate ≈ for Spaarke, "field accepted without correction"), and give admins a cheap batch test with human rating before automated graders.

### 1.5 Workday, SAP Joule

Not researched in depth. They are lower relevance because neither exposes customer-admin AI tuning comparable to the products above, as far as the sources show **[Unverified]**. If needed, revisit as a follow-up.

### 1.6 Document-AI and AP products (Rossum, Hyperscience, AppZen): these really do learn from corrections

- **Rossum** **[Doc]** (knowledge base, automation guide):
  - Training uses human input: "We train on human input (corrections), not previous AI predictions." Only **manually checked documents** are used for training.
  - Rossum discourages the "Always" automation level with dedicated or Aurora engines because it leaves "very few documents for the AI to learn from".
  - **Per-field confidence thresholds** are set in the schema editor, with a default of 97.5% for "Confident" automation.
  - Per-queue automation levels: Confident / Always / Never.
  - **Automation blockers** are shown to admins: low score, failed built-in checks, errors, missing required values.
  - Validation sources are visible per field: score, data-integrity check, **vendor history** (match against previously exported values), or user-confirmed.
  - Aurora "instant learning" / "10x fewer documents" claims are **[Mkt/3P, unverified]**.
- **Hyperscience** **[Doc]** (help centre):
  - Keyers do human transcription and validation in **Flexible Extraction** tasks.
  - QA data feeds **periodic retraining cycles**; it does **not** change model behaviour immediately.
  - Admins set an **accuracy target** (for example 99% = 1 error per 100 fields). The system automates only when it is confident it can meet the target, so raising the target lowers automation.
  - Training data is managed in **Keyer Data Management**, with **labeling anomaly detection** on ground truth **[Mkt release note]**.
- **AppZen** **[Mkt]**:
  - An **Auditor Feedback Report** is used to tune configuration.
  - Auditors can mark false positives.
  - In October 2026 AppZen announced ZenLM Plus finance models, benchmarked by the vendor itself. Whether they learn from per-customer auditor corrections is **[Unverified]**.
- **Lesson for Spaarke**: the **accuracy-target → automation-rate trade-off** (Hyperscience) and **per-field threshold with visible blockers** (Rossum) are the right admin abstractions for eBilling extraction and UTBMS coding. Both vendors **exclude unreviewed AI output from training data**.

### 1.7 Legal products (Brightflag, Onit, LawVu, Harvey, Ironclad)

- **Brightflag** **[Mkt]**: the AI classifies each line into task and activity. Brightflag's **internal** lawyers review low-confidence cases and give corrective feedback, so it is a vendor-side human-in-the-loop, not customer-admin tuning. Reviewers approve, adjust or reject invoices **[Doc]**. Whether reviewer adjustments retrain the model is **[Unverified]**.
- **Onit** **[Mkt, 2021–22]**: InvoiceAI "continuously learns from invoices"; it has since been folded into the Unity platform (per a 2025 listing) **[3P]**. Learning from reviewer decisions is not documented.
- **LawVu**: no public material found on AI tuning or feedback **[gap]**.
- **Ironclad** **[Doc]**:
  - Admins author **AI Playbooks**: preferred and fallback positions, triggers scoped by paper type or geography, presence rules with an extra approver.
  - Role-based permissions control who can view, create and modify playbooks.
  - **No documented accuracy-testing or evaluation surface was found.**
- **Harvey** **[Mkt]**: Workflow Builder and Vault. A blog post titled "A Framework for Domain-Specific Evaluations" (2025-10-27) exists, but its content was not retrieved. No customer-admin evaluation surface is documented.
- **Lesson**: the legal market **has no customer-admin accuracy or tuning surface**. Vendors either tune internally (Brightflag) or give admins policy and playbook authoring (Ironclad). An admin-visible "AI accuracy by capability" page with labelled data from in-workflow corrections would be differentiating for Spaarke in legal ops **[analysis]**.

### 1.8 Cross-vendor capability matrix

| Capability | Copilot Studio | Agentforce | ServiceNow | Intercom Fin | Rossum / Hyperscience | Legal vendors |
|---|---|---|---|---|---|---|
| Quality dashboard per capability | Analytics + eval results | Observability, quality scores | Control Tower, Prompt performance | Resolution rate, CX Score | Automation rate, blockers, accuracy target | Rare |
| Test sets with expected outputs | Yes (≤100/set, CSV, generated, from production themes) | Yes (utterance → topic/action/response) | Yes (dataset × prompt × metrics) | Batch tests (≤50), Simulations | Ground-truth / QA data | No |
| Graders | Exact, keyword, similarity, meaning, tool use, custom LLM, safety | LLM judge, custom plain-language metrics | Human feedback, correctness, faithfulness | Human rating; LLM-judged simulation | Field-level exact match | — |
| Version comparison | Rerun and compare | Yes | Per prompt in an eval run | Preview vs live | Model versions | — |
| Threshold / review routing | Pass score per grader | Health alerts | Approvals | Handoff rules | **Per-field confidence threshold; accuracy target** | Confidence flags |
| Corrections → improvement | Production → test set; maker edits | Implicit feedback (edit/accept/regenerate) logged | Human-feedback metric | Admin edits Guidance/content | **Retrain per customer on human-validated data only** | Vendor-internal |
| Fine-tuning by customer | No | No | No | No | Managed by vendor (dedicated engine) | No |

---

## 2. Technical approaches to "learn from corrections" without per-customer fine-tuning

### 2.1 Signal capture: not all feedback is a label

| Signal | Label strength | Use |
|---|---|---|
| **Field corrected** (AI value → user value) | Strong negative for the AI value; positive for the user value if the actor is qualified | Example candidate, test case, accuracy metric |
| **Explicit confirm of a suggestion** (e.g. "Suggested · confirm to link" in the Communication engine) | Medium; exposed to automation bias | Accuracy metric; example candidate only after sampling |
| **Accepted unchanged inside a bulk action** (eBilling "bulk accept of clean lines") | **Weak; treat as unlabelled** | Do not train or promote; use only in "not corrected" rate with a caveat |
| Thumbs up/down (existing Cosmos `feedback`) | Weak, sparse, no target value | Dashboard signal; triage queue |
| Downstream outcome (firm disputes adjustment; reviewer later re-codes) | Strong but delayed | Re-label; conflict detection |

Rossum's rule "train on corrections, not AI predictions" and Salesforce's implicit-feedback taxonomy (accept/edit/regenerate) both support this distinction **[Doc]**.

**Minimum event shape** ("AI outcome event"):

- tenant
- action id + **action version**
- binding id
- model deployment
- source record reference (not a content copy)
- field path
- AI value and AI confidence
- final value
- outcome (`corrected` | `confirmed` | `accepted-unreviewed` | `rejected`)
- actor id and actor role
- surface
- timestamp

Without action version and actor role, neither evaluation nor label-quality control is possible.

### 2.2 Dynamic few-shot retrieval

- **Evidence**:
  - kNN selection of in-context examples by embedding similarity beats random selection **[Paper: Liu et al. 2021, "What Makes Good In-Context Examples for GPT-3?", arXiv 2101.06804]**.
  - Microsoft Research's **Medprompt** uses kNN dynamic few-shot as one of its three components **[Paper: Nori et al. 2023, arXiv 2311.16452]**.
  - Example **order** can swing accuracy substantially **[Paper: Lu et al. 2021, "Fantastically Ordered Prompts", arXiv 2104.08786]**.
  - Recent practitioner write-ups report large gains over static few-shot on classification **[3P, figures unverified]**.
- **Fit for Spaarke**:
  - UTBMS task/activity coding (closed enum, short narrative input) and email→matter classification are near-ideal targets: short inputs, categorical outputs, high example reuse.
  - Extraction of invoice header fields benefits less. Layout dominates there, and Content Understanding / DocIntel handle it.
- **Design notes**:
  - Retrieve top-k (k ≈ 3–8) from a **tenant-partitioned** index (existing Azure AI Search tenant partitioning under ADR-014). Filter by action id and approval status.
  - Diversify the examples, for example with MMR, so all k are not the same code.
  - Render through the existing JPS `examples` slot.
  - Log which example ids were injected per call so evaluation can attribute wins and losses.
- **Order of rollout**: static pinned examples (zero new infrastructure, JPS already supports them) → dynamic retrieval behind a per-Action toggle once the library has dozens of approved examples per Action.

### 2.3 Example curation and aging

- States: `candidate` → `approved` → `retired`. Only `approved` examples are injected. Promotion is an admin action, or a policy rule such as "N agreeing corrections by qualified reviewers".
- **Aging**: retire examples whose code table entry changed, whose source Action schema version changed, or whose own evaluation contribution is negative (leave-one-out on the test set). Cap the number of examples per class to avoid majority-class drift.
- **Leakage guard**: an example and a test case must never come from the same source record. Otherwise evaluation scores are inflated.

### 2.4 Label quality controls

- **Who may label**: a role-based "qualified labeller" per Action. For example, an eBilling reviewer role for UTBMS codes and an admin for the Example Library. Corrections by other users count for metrics but are not promotable.
- **Conflicting labels**: when the same or near-duplicate input has different final values, flag a conflict in the admin queue. Do not promote either value until it is resolved.
- **Anomaly checks**: Hyperscience ships labelling anomaly detection on ground truth **[Mkt]**. A cheap version: flag a correction when the user's value is far from all approved examples of that class, or when one user's correction rate is an outlier.
- **Audit**: every promotion and retirement is attributable and reversible, because the Example Library is configuration and changes prompt behaviour.

### 2.5 Confidence calibration from corrections

- **Self-reported LLM confidence is poorly calibrated.** Calibrate empirically: bucket the AI-reported confidence and plot the observed correction rate per bucket (a reliability table). Then set the per-field threshold from the observed curve. This is the admin view Hyperscience and Rossum expose as an accuracy target or score threshold.
- **Logprobs** can give better confidence for single-token enum outputs. Reasoning models generally do not return logprobs **[Unverified for the current Azure deployments; verify per model]**.
- Later, fit a monotone mapping (isotonic or Platt) per Action and field from the outcome events.

### 2.6 Champion/challenger and offline evaluation

- **R1 offline**: replay a frozen golden test set through the published version and the draft version. Score per field and show diffs. Gate publish on "no regression beyond tolerance on any tracked field".
- **Later online**: run the challenger in **shadow mode** (executes, is not shown, is logged). Compare against the subsequent human outcome. A percentage traffic split is less suitable for legal workflows because users would see inconsistent behaviour.
- **Scorers for structured JSON** (most Spaarke Actions): use deterministic checks first. Exact match for enums and IDs; numeric tolerance for amounts and hours; date equality; set precision/recall for multi-valued fields. Use an LLM judge only for narrative fields, such as describer text or summaries.
- **Statistics**: report n and a confidence interval (Wilson) per metric. Suppress "accuracy" for n < ~30 to avoid admins tuning to noise.

### 2.7 When fine-tuning becomes worth it

**Azure status (Learn, ms.date 2026-10-06)** **[Doc]**:

- **SFT and DPO**: gpt-4o, gpt-4o-mini (SFT only), gpt-4.1, gpt-4.1-mini, gpt-4.1-nano.
- **RFT**: o4-mini; **gpt-5 RFT requires an invitation**.
- Open models: gpt-oss, Qwen3, Llama-3.3.
- **Distillation**: "teacher-generated SFT data".
- **Training types**: Standard (regional residency), Data zone (US only), Global (cheaper, no residency), Developer (preemptible, no residency).
- **Deployment**: Developer deployments avoid the hourly hosting fee but last only 24 hours, for evaluation only. Standard deployments carry hosting charges.

**Retirement risk**: the gpt-4.1 family retires on 2027-04-14 (researcher memory, CU vs DocIntel entry, 2026-10-14). A fine-tuned adapter "isn't interchangeable across similarly named base models" **[Doc]**, so every base retirement forces a retrain and re-evaluation.

**Fine-tuning is worth considering only when all of these hold**:

1. The task is stable. Code tables and output schema are fixed.
2. There are thousands of high-quality labelled examples across classes.
3. Prompt plus dynamic few-shot has plateaued below target on the offline harness.
4. There is concrete cost or latency pressure. For example, distilling a large-tier Action to a nano model for high-volume line coding.
5. A base model with a long support horizon exists.

**Per-tenant fine-tuning** multiplies hosting, retraining and evaluation by the number of tenants. It also conflicts with the architecture non-goal ("No fine-tuning pipeline is in scope through 2027") and would need an ADR amendment (§6.5 path B). A **cross-tenant** fine-tune on customer data conflicts with the tenant boundary rule ("no cross-tenant model fine-tuning").

### 2.8 Pitfalls

| Pitfall | Mechanism | Mitigation |
|---|---|---|
| **Self-reinforcing feedback loop** | Unreviewed AI output (bulk-accepted, auto-applied) becomes an example, then the model repeats its own error with higher confidence | Only human-touched outcomes by qualified labellers are promotable (Rossum rule); never promote `accepted-unreviewed` |
| **Automation bias** | Reviewers confirm suggestions without checking, which inflates "accuracy" | Report correction rate and confirmation rate separately; periodic blind audit sample; show acceptance latency |
| **Label noise and conflicts** | Different reviewers code the same work differently (UTBMS is notoriously inconsistent) | Conflict queue; per-Action qualified-labeller role; admin resolution |
| **Privileged and confidential content in examples** | An example from Matter A appears in a prompt run for a user or matter without access, which is an authorization side-channel and possibly a waiver concern | Examples are **admin-curated with a redaction step**, or retrieval is filtered to sources the calling user can read. Default to curated-only for R1 |
| **Cross-tenant leakage** | Shared index or shared library | Tenant partition key on events, library and index (ADR-014); no global examples except Spaarke-authored seed examples |
| **Evaluation leakage** | Example and test case from the same record | Source-record disjointness check |
| **Retention mismatch** | Existing `feedback` container is Tier 3 operational data with 90-day retention (`FeedbackEntry.cs`); labelled data needs longer, governed retention plus deletion on record delete | Separate store and retention class for outcome events and curated examples; decide in design |
| **Goodhart on LLM judges** | Tuning to the judge rather than the task | Deterministic scorers for structured fields; human-rated batch test for narrative |
| **Small-n dashboards** | Admins react to noise | Confidence intervals; minimum n |
| **Model retirement** | Examples or thresholds calibrated on one model drift on the successor | Re-run the harness on model change; thresholds stored per model deployment |

---

## 3. Evaluation tooling usable from .NET/Azure without the Foundry portal

| Option | Status (2026-10-14) | Licence | Headless from .NET? | Fit |
|---|---|---|---|---|
| **Microsoft.Extensions.AI.Evaluation** (+ `.Quality`, `.NLP`, `.Safety`, `.Reporting`, `.Reporting.Azure`, `.Console`) | GA line; **10.11.0 published 2026-10-14** (10.10.0 on 2026-09-09, 10.9.0 on 2026-08-11) per the NuGet API | **MIT** (dotnet/extensions) | **Yes**, natively | **Primary choice.** Builds on the `IChatClient` abstraction the BFF already uses (MEAI 10.3.0, so version alignment is needed). Quality evaluators (Relevance, Completeness, Equivalence, Groundedness, Task Adherence, ...) are LLM judges on your own Azure OpenAI deployment. NLP evaluators (BLEU/GLEU/F1) need no LLM. Custom `IEvaluator` for exact-match per JSON field. Response caching and reporting to Azure Storage; `dotnet aieval` report tool |
| MEAI `.Safety` evaluators | GA package | MIT | Yes, **but calls the Foundry Evaluation service** (needs a Foundry project) | Optional; not needed for R1 |
| **Foundry cloud evaluation** (Evals API via `Azure.AI.Projects` → `ProjectOpenAIClient.GetEvaluationClient()`) | Learn page ms.date 2026-09-25. **The C# package is `--prerelease`**, and the OpenAI .NET evals client needs `#pragma warning disable OPENAI001` (experimental) | MIT SDKs; paid service | **Yes, headless** (no portal needed). Needs a Foundry project, the Foundry User role and a judge deployment | Later option for large-scale runs, built-in evaluators and App Insights trace evaluation. Adds a Foundry project dependency per environment. Not needed for R1 |
| Azure OpenAI **Stored Completions** (`store: true`) | Documented, dating from 2024-10 preview API versions. Current GA status **[Unverified]** | Service | REST | Captures production traffic for eval or distillation. Overlaps with Spaarke's own outcome events; stores customer content in the AOAI resource, which is a privacy review item |
| **promptfoo** | Active. **Acquired by OpenAI (announced 2026-03-09)**; repo states it remains open source | **MIT** (GitHub API confirmed) | CLI/Node only; callable from CI | Good for developer-side prompt regression in CI. Not embeddable in the admin product. Long-term governance under OpenAI is **[Unverified]** |
| **DeepEval** (Confident AI) | Active (pushed 2026-10-12) | **Apache-2.0** (GitHub API confirmed) | Python only | Rich metrics (G-Eval and others). Requires a Python sidecar, so it does not fit a .NET-only BFF |
| **Ragas** | Repo moved to `vibrantlabsai/ragas`; last push 2026-02-24 | **Apache-2.0** (GitHub API confirmed) | Python only | RAG-specific metrics; lower activity in 2026. Not recommended as a dependency |

**Recommendation**:

- Build the harness on `Microsoft.Extensions.AI.Evaluation` (core + Quality + Reporting), with **custom deterministic evaluators** for JSON field accuracy.
- Run it as a BFF background job (ADR-004 `IJobHandler`) that reads test sets from Dataverse and writes run results back.
- Keep promptfoo optional for developer CI.
- Defer Foundry cloud evaluation until scale or safety evaluators require it.

BFF hygiene (§10) applies: measure the publish-size delta of the Evaluation packages, and consider placing the harness outside the BFF if ADR-052 workload placement prefers a worker.

---

## 4. Recommended capability model

### 4.1 R1 (MVP): "measure, curate, test, promote"

| # | Capability | Builds on | Why it is in R1 |
|---|---|---|---|
| R1-1 | **AI outcome event capture**: one contract (§2.1 shape) emitted by consuming surfaces. Starting surfaces: eBilling line coding and extraction correction, Communication engine confirm/reject of suggested associations. Includes action **version** and actor role | Existing correction UIs (eBilling Invoice Review, r5 "confirm to link"); `FeedbackEntry` pattern (tenant partition) | Without it there is no labelled data; everything else depends on it |
| R1-2 | **Action quality dashboard**: per Action and per field: volume, corrected %, confirmed %, unreviewed %, trend by version, by confidence bucket (reliability table), with n and CI | Outcome events; existing feedback aggregates | The owner's "measure" ask; mirrors the market baseline (Copilot Studio results, Fin resolution rate, Rossum blockers) |
| R1-3 | **Example Library** per Action: corrections promotable to `candidate` → admin approves (with redaction) → `approved` examples are **pinned** into JPS `examples`; retire and age | JPS `ExampleEntry` already rendered | The non-fine-tuning "learn from corrections" lever, at zero model cost. Curated-only avoids the privilege side-channel |
| R1-4 | **Golden test sets + offline evaluation run**: test cases created from corrections or CSV; run published vs draft Action version; deterministic per-field scorers; diff view | MEAI.Evaluation + custom `IEvaluator`; ADR-004 job | Prevents tuning from regressing. This is the gate every vendor has |
| R1-5 | **Action versioning with publish gate and rollback**: publishing a draft requires a passing evaluation run (configurable tolerance); one-click rollback | Playbook Builder Action records | Admins edit prompts today with no safety net |
| R1-6 | **Per-Action/field confidence threshold → review routing**, informed by the calibration table | eBilling reliability threshold, `sprk_*confidence` fields | Converts accuracy into the business lever (automation vs review), as in Hyperscience/Rossum |

### 4.2 R2 and later

- **Dynamic few-shot retrieval** (top-k by embedding from a tenant-partitioned index; security-filtered or curated-only; per-Action toggle; injected-example logging).
- **Shadow-mode champion/challenger** on live traffic, compared against subsequent human outcomes.
- **Automatic calibration** (isotonic per field), **conflict detection** and **labelling anomaly detection**.
- **LLM-judge evaluators** for narrative Actions; human batch rating (Fin-style Good/Acceptable/Poor) for narrative test sets.
- **Failure clustering → suggested prompt edits** (ServiceNow-style "prompt optimization"; admin approves).
- **Approval workflow for Action versions** (ServiceNow AI Control Tower pattern) for regulated customers.
- **Fine-tuning or distillation**: only per §2.7 criteria, Spaarke-operated (not per tenant), and only after an ADR amendment of the "no fine-tuning through 2027" non-goal.

### 4.3 Minimum viable admin UI (one code page or a Playbook Builder tab set)

1. **Quality** (landing): table of Actions with volume, corrected %, trend arrow and last evaluation result; drill into per-field metrics, reliability table and a corrections list.
2. **Corrections queue**: each row shows input snippet, AI value → final value, actor, conflict flag. Actions: *Promote to example*, *Add to test set*, *Dismiss (reason)*.
3. **Examples** (per Action): list with status (candidate/approved/retired), source, redaction editor, pin/unpin.
4. **Test & compare**: pick a test set; run published vs draft; summary scores per field plus a case-level diff; *Publish draft* (enabled only on pass) and *Roll back*.
5. **Thresholds** (per Action/field): slider showing "at threshold X: Y% auto-accepted, est. Z errors per 100" from the calibration data.

Explicitly out of the MVP UI: model training, custom judge authoring, online A/B.

---

## 5. Open questions for design

1. **Storage split**: Cosmos (high-volume outcome events, tenant partition, new retention class) vs Dataverse (curated examples, test sets, evaluation runs, under the Dataverse security model). The recommended split is events in Cosmos and curated artefacts in Dataverse, but this needs the §11 component justification and a retention decision (the current `feedback` container is 90-day).
2. **Example access policy**: curated-and-redacted only, vs retrieval filtered by the caller's record access. This interacts with ADR-028 and the "AI is not an authorization side-channel" constraint, so it needs owner sign-off.
3. **Who are qualified labellers** per Action, and does the eBilling reviewer role suffice for UTBMS?
4. **Harness placement**: BFF `IJobHandler` vs a separate worker (ADR-052; publish-size impact of `Microsoft.Extensions.AI.Evaluation.*`). The packages must also align with the BFF's MEAI 10.3.0 pin (Evaluation is at 10.11.0).
5. **Spaarke-authored seed test sets** for shipped Actions (UTBMS coding, email→matter) are needed so tenant admins do not start from zero. These must be synthetic or licensed data, never another tenant's.

---

## Sources (accessed 2026-10-14)

**Microsoft (most authoritative for §3 and §2.7)**
- Microsoft.Extensions.AI.Evaluation libraries — https://learn.microsoft.com/en-us/dotnet/ai/evaluation/libraries (ms.date 2026-04-09, updated 2026-09-10) [Doc]
- NuGet API: `microsoft.extensions.ai.evaluation` versions and publish dates (10.11.0 on 2026-10-14, MIT) — https://api.nuget.org/v3-flatcontainer/microsoft.extensions.ai.evaluation/index.json [Doc]
- Foundry cloud evaluation (SDK, C# prerelease) — https://learn.microsoft.com/en-us/azure/foundry/observability/how-to/cloud-evaluation (ms.date 2026-09-25) [Doc]
- Fine-tuning overview: supported models, training types, limitations — https://learn.microsoft.com/en-us/azure/foundry/fine-tuning/overview (ms.date 2026-10-06) [Doc]
- SFT how-to — https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/fine-tuning (ms.date 2026-10-05) [Doc]
- What's new in Foundry fine-tuning, April 2026 — https://devblogs.microsoft.com/foundry/whats-new-in-foundry-finetune-april-2026/ [Mkt]
- Stored completions — https://learn.microsoft.com/en-us/azure/ai-services/openai/how-to/stored-completions [Doc, status unverified]
- Copilot Studio: create a test set — https://learn.microsoft.com/en-us/microsoft-copilot-studio/analytics-agent-evaluation-create (ms.date 2026-10-05) [Doc]
- Copilot Studio: Agent Evaluation GA — https://techcommunity.microsoft.com/blog/copilot-studio-blog/agent-evaluation-in-microsoft-copilot-studio-is-now-generally-available/4507392 [Mkt]

**Salesforce**
- Einstein audit and feedback data model — https://developer.salesforce.com/blogs/2024/07/the-einstein-audit-and-feedback-data-model-in-data-cloud [Doc]
- Agentforce Testing Center blog — https://www.salesforce.com/blog/agentforce-testing-center/ [Mkt]
- Trailhead: set up testing criteria — https://trailhead.salesforce.com/content/learn/modules/agentforce-agent-testing/set-up-testing-criteria [Doc]
- Agentforce Observability — https://www.salesforce.com/blog/agentforce-observability/ [Mkt]; https://salesforcebreak.com/2026/05/28/setting-up-agentforce-observability/ [3P]

**ServiceNow**
- Evaluate a prompt (Skill Kit) — https://www.servicenow.com/docs/r/intelligent-experiences/evaluate-prompt.html [Doc]
- AI Control Tower release notes — https://www.servicenow.com/docs/r/yokohama/release-notes/ai-governance-rn.html [Doc]
- Knowledge 2026 AI Control Tower press release — https://newsroom.servicenow.com/press-releases/details/2026/ServiceNow-expands-AI-Control-Tower-to-discover-observe-govern-secure-and-measure-AI-deployed-across-any-system-in-the-enterprise/ [Mkt]

**Intercom / Zendesk**
- Fin: simulations vs batch tests vs previews — https://www.intercom.com/help/en/articles/14077180-simulations-vs-batch-tests-vs-previews [Doc]
- Fin performance — https://www.intercom.com/help/en/articles/11390083-check-on-how-fin-is-performing [Doc]
- Zendesk AI features (2026 packaging) — https://pluno.ai/blog/zendesk-ai-features [3P]

**Document-AI / AP / legal**
- Rossum automation guide — https://knowledge-base.rossum.ai/docs/guide-to-automation-process-in-rossum [Doc]
- Hyperscience transcription models — https://help.hyperscience.ai/v42/docs/transcription-models-overview.md [Doc]; flexible extraction — https://help.hyperscience.ai/v43/docs/flexible-extraction [Doc]; labelling anomaly detection — https://www.hyperscience.ai/blog/release-highlight-faster-model-training-with-labeling-anomaly-detection/ [Mkt]
- AppZen auditor feedback report — https://www.appzen.com/resources/webinars/reporting-on-auditor-efficiency [Mkt]; ZenLM Plus — https://runtimewire.com/article/appzen-zenlm-plus-finance-models [3P]
- Brightflag: how AI invoice review works — https://brightflag.com/insights/ai-invoice-review-how-it-works [Mkt]; invoice lifecycle — https://help.brightflag.com/en/articles/753069-invoice-lifecycle-in-brightflag [Doc]
- Onit InvoiceAI — https://www.onit.com/news/onit-debuts-invoiceai-ai-for-legal-invoice-review [Mkt, 2021]
- Ironclad AI Playbooks — https://support.ironcladapp.com/hc/en-us/articles/24948981301143-Create-Ironclad-AI-Playbooks-in-Workflow-Designer [Doc]
- Harvey blog index — https://www.harvey.ai/blog/tag/technical [Mkt]

**Open source (licences confirmed via the GitHub API on 2026-10-14)**
- promptfoo (MIT) — https://github.com/promptfoo/promptfoo; acquisition coverage — https://qaskills.sh/blog/openai-promptfoo-acquisition-explained-2026 [3P]
- DeepEval (Apache-2.0) — https://github.com/confident-ai/deepeval
- Ragas (Apache-2.0, now vibrantlabsai/ragas) — https://github.com/explodinggradients/ragas

**Research papers** (from background knowledge; well-known, not re-fetched)
- Liu et al. 2021, "What Makes Good In-Context Examples for GPT-3?" — arXiv 2101.06804
- Lu et al. 2021, "Fantastically Ordered Prompts and Where to Find Them" — arXiv 2104.08786
- Nori et al. 2023, "Can Generalist Foundation Models Outcompete Special-Purpose Tuning? (Medprompt)" — arXiv 2311.16452

**Spaarke repo (code wins)**
- `src/server/api/Sprk.Bff.Api/Services/Ai/Models/PromptSchema.cs` (`Examples`, `ExampleEntry`)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PromptSchemaRenderer.cs` (step 8, few-shot rendering)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Feedback/FeedbackEntry.cs` (Cosmos `feedback`, `/tenantId`, 90-day retention)
- `src/server/api/Sprk.Bff.Api/Api/Ai/FeedbackEndpoints.cs` (aggregates by playbook and capability)
- `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` (Microsoft.Extensions.AI 10.3.0)
- `src/server/api/Sprk.Bff.Api/Services/Communication/Engine/AssociationStatusMapper.cs` ("Suggested · confirm to link")
- `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` §1.2–1.3 (tenant boundary; no fine-tuning through 2027)
- `projects/ebilling-r1/design.md` (§7.5 reliability threshold; §12 accuracy targets; WP7 Evaluation)
