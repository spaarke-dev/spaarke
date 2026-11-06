# SpaarkeAI Tuning and Feedback R1 — project seed

> Status: seed for `/design-to-spec` · Created 2026-10-14 from the eBilling R1 design discussion · Owner decisions: eBilling D-61, D-64.

## 1. Owner direction

- "We aren't using Microsoft Foundry (which provides a front end) — so how would we tune and improve our AI capabilities?"
- The tuning capability is a **separate platform project**; other projects may feed it but must not depend on it.
- It must be "an **embedded tool** that improves the AI functionality as an integrated part of day-to-day work activities" — not a separate training activity.
- It should be part of the AI platform generally, not eBilling-specific.

## 2. Problem

Spaarke's AI capabilities (Actions) are edited and published, but nobody can answer: *How accurate is this capability? Did the new prompt make it better or worse? Is it learning from the corrections our users make every day?* Users correct AI output constantly (codes, extracted values, record matches, classifications); those corrections are lost or sit in audit history, and the next run makes the same mistake.

## 3. Current platform state (verified 2026-10-14)

| Piece | What exists | Gap |
|---|---|---|
| Capability definition | Action (`sprk_analysisaction`: JPS prompt, output schema, model tier) + Binding (`sprk_playbookconsumer`), run by `Services/Ai/LinearConsumers/ActionRunner.cs` behind `PublicContracts` facades (ADR-039, ADR-043 Proposed) | No example input; no version comparison |
| Admin UI | Playbook Builder code page — catalog editor `src/client/code-pages/PlaybookBuilder/src/components/catalog/ActionEditorForm.tsx`; `ScopeConfigEditor` PCF | Edits prompts / schemas; no measurement, no evaluation |
| Model choice | Tier per Action, override per Binding (`ModelTierDeploymentResolver.cs`) | No A/B or champion / challenger |
| Feedback | `Services/Ai/Feedback/` — per-response feedback in Cosmos (`feedback` container, partition `/tenantId`), aggregated per playbook | Recorded, never fed back into prompts |
| Learning from confirmations | Email association: `sprk_affinity` frequencies from user confirmations, read by `AffinityRung` (deterministic, no ML) — `docs/architecture/communication-intelligence-architecture.md` | Email-specific; not available to Actions |
| Input channels an Action could use for examples | `BoundInputs` (`Context/BoundInputs.cs`): `ContextEnvelope`, `ResolvedOperand` (`## Input`), record-memory fragment; knowledge nodes (`KnowledgeRetrievalHandler`, `sprk_analysisknowledge`) | None wired for retrieved examples |
| Evaluation | None found (no evaluation runner, no test sets per Action) | — |

## 4. Proposed R1 scope (revised after `notes/research/market-ai-tuning-and-feedback.md`)

Market pattern (Microsoft Copilot Studio, Salesforce, ServiceNow, Intercom): per-capability quality dashboard, test sets with expected answers, draft-vs-live comparison, review thresholds, versioning with rollback; corrections become test cases and curated examples — **no per-customer model retraining**. Invoice-capture vendors (Rossum, Hyperscience) retrain only on human-reviewed fields. Legal products (Brightflag, Ironclad, Harvey) give customer admins no accuracy or tuning screen — a differentiation opportunity.

1. **Correction capture** — one record format for every correction or confirmation: AI value, final value, confidence, outcome type (Corrected, Confirmed individually, Accepted in bulk, Unreviewed), Action code + version, user and role, tenant. Only Corrected and Confirmed-individually are labels. First producers: eBilling line review, email → matter confirmation.
2. **Quality dashboard** — per Action and field: corrected / confirmed / unreviewed share, by version and confidence band, with sample sizes.
3. **Example library** — correction → candidate → admin approves and redacts → pinned into the Action's prompt via the existing JPS `PromptSchema.Examples` slot (no new prompt mechanism). Automatic per-request similar-example retrieval is **R2**.
4. **Test sets and runs** — published version vs draft, exact per-field scoring, LLM judge only for free text; candidate library `Microsoft.Extensions.AI.Evaluation` (MIT, headless; BFF pins 10.3.0 vs current 10.11.0 — align).
5. **Versioning gate** — publishing a new Action version requires a passing test run; one-click rollback.
6. **Review thresholds** — per Action and field, set from observed accuracy; consumed by projects such as eBilling (D-47).

Minimum admin UI (in Playbook Builder): quality overview · corrections queue · examples · test and compare · thresholds.

Out of R1: fine-tuning (also ruled out through 2027 by Spaarke's AI architecture doc — cite in design); automatic similar-example retrieval; automatic prompt rewriting; online A/B traffic splitting.

## 5. Principles and constraints

- **Tenant isolation**: examples never cross customers (dedicated AI Search per customer; tenant filter as well).
- **Privileged content**: examples may contain privileged narratives — ADR-015 governance, minimum text, retention rules.
- **Label authority**: who may confirm / correct per capability; conflicting labels resolved, not averaged; bad examples removable.
- **No feedback loop reinforcing errors**: unconfirmed AI output is never an example; examples carry the Action version they were judged against.
- **Consumers stay independent**: a consuming capability works identically when the platform is absent (null-object, ADR-032).
- **BFF hygiene**: CLAUDE.md §10 / `.claude/constraints/bff-extensions.md`; §11 justification for each new component; likely an ADR (or an ADR-043 amendment) for the example channel in `ActionRunner`.

## 6. First consumers and their data

| Consumer | Data already planned | Notes |
|---|---|---|
| eBilling R1 UTBMS coding (`INVOICE-LINE-CODE@v1`) | Structured confirmation / correction records per line (eBilling FR-31, D-64) | Highest-value first consumer; codes vary by code set → example filter |
| eBilling extraction (Content Understanding analyzer) | Field corrections with confidence | Analyzer labeled-sample training is a different mechanism (CU GA feature) — decide whether the platform manages it |
| Email association | `sprk_affinity` confirmations | Possible migration onto the common capture contract |
| Document profile, classification | Existing feedback rows | — |

## 7. Open questions for design

0. **Example access policy (owner sign-off; security-sensitive)**: an example drawn from Matter A shown to a user who cannot read Matter A breaks the rule that AI never bypasses record security. Research recommends R1 uses only admin-curated, redacted examples.
0a. Storage split: high-volume correction events (Cosmos?) vs curated examples and test sets (Dataverse?); retention — the existing feedback store keeps 90 days only.
0b. Who counts as a qualified labeller per capability.
1. Example store for R2 retrieval: AI Search index per customer (vector + filters) vs Dataverse table + embeddings.
2. Does the example channel go into `## Input` (operand) or the `ContextEnvelope`, and how does ADR-043 change?
3. Should Content Understanding labeled-sample training be orchestrated from the same console?
4. Evaluation tooling: in-house runner vs `Microsoft.Extensions.AI.Evaluation` / other libraries (see research).
5. Who are the admins (tenant admin vs Spaarke operations), and what can each change?
6. Relationship with the existing Cosmos feedback store — extend or replace?

## 8. Next step

Owner reviews this seed and `notes/research/market-ai-tuning-and-feedback.md`, then `/design-to-spec projects/spaarkeai-tuning-and-feedback-r1` in its own worktree (`spaarke-wt-spaarkeai-tuning-and-feedback-r1`, branch `work/spaarkeai-tuning-and-feedback-r1`).
