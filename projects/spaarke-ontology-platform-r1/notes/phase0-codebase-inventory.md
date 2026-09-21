# Spaarke Ontology Platform — Phase 0 Codebase Inventory & Strategy Assessment

> **Date**: 2026-09-19
> **Input**: `spaarke-ontology-strategy-synopsis.md` v2.0 (Ralph / Claude strategy session, 2026-09-19)
> **Method**: 6 parallel read-only verification agents over `c:\code_files\spaarke` @ master `379c221e0`, plus direct `git grep` checks. Dataverse MCP failed to connect this session — schema evidence is from `docs/data-model/`, entity XML, and the C# schema-contract tests, not the live environment.
> **Status**: Phase 0 complete. Input to `/design-to-spec`. Nothing in this file is a decision; §6 lists the decisions the owner must make.
> **Audience**: Ralph (owner); `design-to-spec` / `project-pipeline` skills.

---

## 0. Bottom line

The **strategic thesis holds and is better-supported by the code than the synopsis knows** — Spaarke has already built or designed most of the primitives it proposes, under different names. But:

1. The **component register (§6.2 / §7.3) is ~80% wrong at the name level**.
2. One of the two structural bets — **"package-not-code" (§6.4)** — **fails the evidence test** as stated; a narrower version is true.
3. The other — **"L2 is the thinnest layer" (§6.2 observation)** — is **rejected**: L2 is scattered, not thin. Sequencing should invert toward L1.
4. The real gaps are **Policy, authority, external binding, and temporal semantics** — not the fact layer.

Nothing is broken today. Both time-critical items (SPE agent SDK, Semantic Kernel) are clear. The one item with a real clock is **D-n** (Agreement Analysis vs. Microsoft Legal Agent for Word, GA early Oct 2026).

---

## 1. Phase 0 items — verdicts

| # | Item | Verdict | Evidence |
|---|---|---|---|
| 1 | Component register | **Substantially wrong.** 8 of 25 named entities absent; 6 renamed or are columns. `EmailToDocumentJobHandler`, `EmailPollingBackupService`, `EmailFilterService` do not exist. `spaarke-knowledge-index(-v2)` is **retired** and structurally rejected by the control plane. `IAiToolHandler` is a two-member tool contract — three-path dispatch is real but lives in the ADR-039 Binding catalog (Event → `EventRulesService`, Click → `SessionDispatchOrchestrator`, Text → `ToolHandlerToAIFunctionAdapter`). L6 provisioning control plane is **~300 shipped files** (`src/server/services/Sprk.Provisioning.ControlPlane.*`), not "[Designed]". | `docs/data-model/entity-relationship-model.md`, `docs/architecture/AI-SEARCH-INDEX-CATALOG.md` |
| 2 | L2 thinness | **Rejected — L2 is scattered, not thin.** Shipped deterministic services: 12-rung association ladder with explicit deterministic/AI partition (`Services/Communication/Engine/RungKind.cs`), `sprk_communicationrule` declarative rules table, `RiConfidenceScorer` (pure function), `SignalEvaluationService` (threshold engine), `PriorityScoringService` / `EffortScoringService` (table-driven), `ScorecardCalculatorService`, `CoreAncestorResolver`, `DailyBriefingCollector`, and **`Services/Insights/LiveFacts/*LiveFactResolver`** returning deterministic `FactArtifact` per predicate — this is the L2 fact-supply seam already. **Missing**: SLA clock, OCG rule engine, conflict/hold checks, deterministic date parser (dates are LLM-extracted), general condition→action rules engine (`EventRulesService` is a binding router). Email→matter association is *not* LLM-first: `AiClassificationRung` is structurally barred from emitting a record GUID or auto-filing. | `Services/Communication/Engine/Rungs/`, `Services/Finance/SignalEvaluationService.cs`, `Services/Insights/LiveFacts/` |
| 3 | Package-not-code | **False as stated.** NDA/Agreement ≈ 50/50 by artifact (60–80 JPS/schema/row files vs 70–100 module-specific C#/TS — `NdaStandardEndpoints.cs`, `ReviewMemo*`, 12 review-specific Compose widgets; generalizing NDA→any type itself required code). Email Intelligence ≈ 95% compiled (165 C# in `Services/Communication/`). Front Door pre-spec; its own roadmap names requester portal, SLA engine, conversational intake as net-new code. **Data-only today**: prompted `sprk_analysisaction` + `sprk_playbookconsumer` Binding reusing an existing surface/disposition/tool set (e.g. a Lease Review sibling varying grounding only). **Always code**: new tool handler, node executor (engine **FROZEN** anyway), disposition leg in `OutputRouter` ("cannot be data"), surface (`surfaceLaunchRegistry` — in code *by design*), `ConsumerType`, coded workflow, widget, export format. The "six output-contract archetypes" are **not a repo concept**; mapped honestly: deviation ✅, structured extraction ✅, narrative synthesis ✅ (Layer 1/2 pattern = "zero new C# per consumer"), cross-corpus tabular ⚠️ (no fan-out node; scoped out by NDA-r1), change-impact ⚠️ (Compose-bound), adversarial ❌. | `docs/guides/ai-guide-consumer-wiring.md`, `docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md` §3, `projects/ai-advanced-capabilities-development/ADVANCED-AI-USE-CASE-PATTERNS.md` |
| 4 | `sprk_document` overlay | **Partial.** Pointer (`sprk_graphitemid/graphdriveid/filepath/containerid/currentversionid/canonicalhash`) / projected (`filename/filesize/filetype/…`) / derived (`sprk_extract*`, summary, index lifecycle) split is clean. `sprk_sourcetype` exists but is an **ingest-path** discriminator (UserUpload=659490000 … SystemGenerated=659490006), not an external-system key — no `sourcesystem/sourceid/sourceetag`. Email is a **known two-path defect**: webhook capture → `sprk_communication`; Outlook "Save to Spaarke" → `sprk_document` + 14 `sprk_email*` columns, bypassing the association engine. R2 Pillar C4 reconciliation on `sprk_internetmessageid` is designed, not confirmed shipped. | `src/server/shared/Spaarke.Dataverse/Models.cs:264`, `tests/unit/Sprk.Bff.Api.Tests/Integration/DataverseEntitySchemaTests.cs`, `projects/email-communication-intelligence-r2/design.md` §1.2 |
| 5 | Binding seams | **Designed once already — `spaarke-connect-integration-module-r1`** (design-only): "Reference, don't replicate"; `sprk_externalconnection / externalref / externalfieldmapping / externalevent`; `sprk_document` alt key `(sprk_externalsystem, sprk_externalkey)` + `sprk_storagemode` Internal/External; iManage Work 10 prebuilt connector + BYO gateway over one canonical contract; declarative JSON mapping DSL; e-billing invoice→matter matching; `Sprk.Connect.Mcp` deferred to Phase 3. Retrieval seam = `RagService` + `ReferenceRetrievalService`. **Enumeration / change-detection / writeback seams do not exist.** | `projects/spaarke-connect-integration-module-r1/design.md` |
| 6 | Temporal | **Greenfield.** `IsAuditEnabled` only as raw entity XML (24 attrs on / 39 off), no audit policy doc, **zero** `RetrieveRecordChangeHistory`, no Dataverse change-tracking, no bitemporal / as-of query, **no playbook or policy version field** (mutated in place). Real audit = ADR-015 immutable Cosmos Tier-2 (SHA-256 hashes, 7-yr retention) + ADR-040 session ledger. Nearest as-of work: `unified-access-control-r2` tasks 088/089 (evaluator replay, unexecuted). | `docs/adr/ADR-015-ai-data-governance.md` |
| 7 | Action ledger | **Gate ledger, not authority ledger.** ADR-040 entries: `SessionOutput`, `SessionToolChain` (identifiers/counts only), `SessionWidgetEvent`, `SessionGate` (`Kind`, `Status`, `SideEffectClass`, `BindingId`, `Turn`), `SessionContextFingerprint` (dark — no writer). **No** authority / policy-version / evidence / confirmer fields. Richest design of those = on-hold `ai-spaarke-action-engine-r1` (`IGateResolver` 5 gate types, `sprk_gate_approval`, `sprk_actionrun`). Persistence: Redis → Cosmos → Dataverse cold. | `Models/Ai/Chat/SessionLedgerEntries.cs`, `projects/ai-spaarke-action-engine-r1/README.md` |
| 8 🚩 | SPE agent SDK | **No exposure.** Zero `ChatEmbedded` / `sharepointembedded-copilotchat` references in `src/`. | `git grep` |
| 9 | Foundry IQ delta | **Inventory complete; decision open (D-i).** ~15k LOC hand-built RAG. Seven push-indexed indexes (`spaarke-files-index` 512-tok, `spaarke-discovery-index` 1024-tok, `spaarke-records-index`, `spaarke-rag-references`, `spaarke-insights-index`, `spaarke-session-files`, `spaarke-invoices-index`); 3072-d `text-embedding-3-large`, HNSW cosine; BFF downloads from SPE via Graph OBO, chunks, embeds, uploads — **no AI Search indexer or SPE data source**. Query: hybrid keyword + vector + Azure semantic reranker; **no query rewriting or multi-query planning** (no-op `IQueryPreprocessor` hooks only). Trimming: `tenantId` always + `PrivilegeFilterBuilder` over `privilege_group_ids` (Entra groups from JWT / `memberOf` OBO), fail-closed to public-only; session-files path relies on `sessionId` clause. Bound to **Azure OpenAI only** (`OpenAiClient` wraps `AzureOpenAIClient`; `ModelSelector` = deployment selection, not provider abstraction). Zero references to Foundry IQ / agentic retrieval / knowledge agents anywhere. | `Services/Ai/RagService.cs` (1,492 LOC), `RagIndexingPipeline.cs`, `Services/Ai/Security/PrivilegeFilterBuilder.cs`, `infrastructure/ai-search/*.json` |
| 10 | Work IQ auth | **Delegated-only boundary cuts through L3.** App-only (MI): `PlaybookService`, `ScopeResolverService`, `DataverseServiceClientImpl`, `DataverseWebApiService`, `SpeAdminGraphService`, **every Service Bus job handler**. OBO: `GraphClientFactory.CreateOnBehalfOfClientAsync`, `SpeFileStore` user paths, `DataverseAccessDataSource` (both, split). BFF identity is secret-free (MI-FIC, ADR-028 A4). Consequence: RAG inside a playbook run degrades to public-only trim unless caller groups are threaded in. | `Services/Ai/PlaybookService.cs:16-35`, `Infrastructure/Graph/GraphClientFactory.cs` |
| 11 🚩 | Legal Agent for Word | **Direct overlap on the review UX; the survivable part exists but is thin.** Shipped: `agreement-review` Action (Reasoning tier, `outputDeterminism: advisory`), `sprk_agreementtype` registry (10 rows, **only 2 knowledge packs** — NDA `KNW-011` + general fallback `KNW-012`), classifier + ≥0.85 confirmation gate, B1–B16 NDA clause standard served deterministically (`NdaStandardClauseProvider`), single-clause `compose-compare-to-playbook`, risk rating, cited findings, gutter comments, draft-alternative rewrites, **native OOXML `w:ins/w:del/w:comment` generated server-side in Compose** (`ComposeShadowPatchEngine`), Review Memo persisted to `sprk_analysisoutput` under `sprk_analysis`. Output schema already enforces a **per-finding fact/judgment split**: `flaggedClause` (fact) · `assessment` (judgment) · `quotedText` (audit citation) · `standardRef` · `riskLevel` · `sectionRef`. Executes on the **linear Action path** (`Services/Ai/LinearConsumers/ActionRunner.cs` via `SessionDispatchOrchestrator`), not the frozen playbook graph. Suggestion→redline loop is **ledger-mediated** (`compose`-disposition `SessionOutput` → `ledgerRef` → pending redline in `ComposeWorkspace.tsx` → native OOXML on save via `SpeSyncOrchestrator`) — functionally Word's accept/reject, but only inside Spaarke's Compose surface. **Absent**: any in-Word review (Word add-in is save/share/to-do only), obligation extraction, policy-version / approval / audit linkage per finding. **Lifecycle gaps**: exported comments are flat legacy `w:comment` (no reply chains) and `resolved` is UI-only — never written to the file, so a reopened `.docx` starts unresolved; **no server-side per-finding disposition** exists (`ReviewMemoAssembler.cs` takes accept/reject from the client at generation time — "rejected" and "never acted on" are indistinguishable). The per-finding disposition *is* the missing authority record. | `infra/dataverse/actions/agreement-review.action.json`, `infra/dataverse/outputschemas/agreement-review.schema.json`, `infra/dataverse/sprk_agreementtype-rows.json`, `Services/Compose/ComposeShadowPatchEngine.cs`, `src/client/office-addins/word/` |
| 12 | Semantic Kernel | **None.** BFF is on `Microsoft.Agents.AI 1.0.0-rc1` (`SprkChatAgent`); only a bump to 1.0 GA. `agent-framework-fit-assessment-r1` (complete 2026-06-03: 1 ADOPT / 5 PARTIAL / 4 DON'T) is the settled baseline. | `Sprk.Bff.Api.csproj:62`, `docs/assessments/agent-framework-fit-assessment-2026-06-03.md` |
| 13 | MCP server | **Does not exist** — no server, no client, no `ModelContextProtocol` package anywhere under `src/`. Skills `mcp-tool-handler` / `declarative-agent` / `widget-design` describe one as if shipped. Real agent surface = **Copilot declarative agent** (`src/solutions/CopilotAgent/`: `declarativeAgent.json` v1.2 + OpenAPI plugin v2.2, ~28 functions, `OAuthPluginVault`), manual deploy via `scripts/Deploy-CopilotAgent.ps1`, no CI workflow. Every prior MCP decision deferred/declined (Copilot→R2, Connect→Phase 3, `mcp-dataverse-implementation` explicit non-goal). Adapter seam: `ToolHandlerRegistry` → `ToolHandlerToAIFunctionAdapter` (`AIFunction`). | `src/solutions/CopilotAgent/`, `Services/Ai/Chat/ToolHandlerToAIFunctionAdapter.cs` |

---

## 2. What the synopsis is right about — and under-claims

1. **The epistemic tier (§4.2 Tier 2) is shipped.** Insights Engine r1/r2 (complete, deployed) delivered **Fact** (deterministic Dataverse query, confidence 1.0) · **Observation** (LLM extraction, verbatim-quote-grounded, confidence-gated, mandatory human review) · **Precedent** (SME-authored `sprk_precedent`) · **Inference** (on demand, never stored, must decline on insufficient evidence). It isn't named "epistemic class" and isn't framed as an ontology tier. **Promote, don't build.**
2. **The gate/judgment layer is further along than "[Designed]"**: ADR-039 closed catalogs + `side_effect_class`; ADR-040 ledger; ADR-041 deterministic confirmation policy (risk tier × origin × completeness; **its "D-F0" ≠ the synopsis's "D-F0" — name collision to resolve**); ADR-043 execution spine (explicitly *reserves* the multi-step Action Engine seam); ADR-047 notification→grounded-action spine.
3. **Waves 2–3 have prior art**: Finance Intelligence = a working L2 instance (AI classify → human gate → AI extract → deterministic snapshots/signals/rollups; LEDES explicitly out of scope); `x-matter-performance-KPI-r1` shipped a Matter Report Card MVP (Feb 2026; `sprk_kpiassessment`, 6 grade fields, recalc endpoint; 100+ KPI catalog incl. OCG compliance; R2–R5 planned, never run).
4. **The Policy-object claim is confirmed concretely**: policy-shaped config lives in ≥5 places with 5 shapes — `sprk_communicationrule` (thresholds, privilege flag), `SignalEvaluationService` threshold rules, `AutoFileGate` per-tenant kill-switch, `ConfirmationPolicyEngine` risk tiers, `sprk_emailupdatefield` allow-lists. That *is* the "highest-value ADR" case.
5. **Identity resolution (D-e) already has its shape**: the rung ladder over `sprk_recordtype_ref` (7 identifier types) + `sprk_affinity` per-tenant frequency table *is* `[PROPOSED: MatterResolutionService]` in all but name. CLAUDE.md §11: extend it.
6. **Agreement Analysis's output contract already has the right shape for repositioning** — D-n is a four-lookup extension (`standardRef` → Policy version, `flaggedClause` → Obligation candidate, memo → approval gate, Matter inherited from document), not a redesign.

---

## 3. Corrections required before any spec is written

| Issue | Correction |
|---|---|
| §6.4 "no module-specific code" | Redefine the boundary: **capability** (Action + Binding + policy + knowledge pack, authored in `infra/dataverse/actions/`) is packageable today; **surface** and **ingestion** are platform, compiled by design. Node engine is frozen → "generic execution paths" must be prompted Actions / coded workflows / disposition legs, not node types. |
| §6.2 "L2 thinnest → drives sequencing" | Invert. L2 needs **consolidation and naming** (one `IFactSupply` contract over `LiveFactResolver` + `SignalEvaluation` + rungs). Engineering goes to L1: Policy, Obligation, Engagement, authority ledger, binding registry. |
| §7.3 ERD | Regenerate from `docs/data-model/entity-relationship-model.md` (~70 real `sprk_*` entities). `sprk_organization` = Party; `sprk_billingevent` = invoice line; `sprk_servicerequest` exists and is wired into the Association Engine while Front Door r1 proposes a new `sprk_legalrequest` — **duplicate Request object to settle**. |
| §6.2 / §4.2 vocabulary | Adopt the code's vocabulary: **three execution models** (node playbook / direct Action / legacy sequential-tool — `DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md`) and **eight Binding dispositions** (`DispositionRoutability.cs`). Drop "six output-contract archetypes". |
| D-a (Communication object) | Decided by ADR-045; **not yet true in data** (two-path defect). Reframe as "finish R2 Pillar C4 reconciliation". |
| §9.2 "ADR-024/025 Bridge" | Numbers taken (ADR-024 polymorphic resolver, ADR-025 icon library). ADR-035 unused; next clean number **ADR-052**. Hygiene: ADR-024/025 are missing from `docs/adr/` and from the `.claude/adr/INDEX.md` table. |
| §2.1 positioning contradiction | The cited materials (`spaarke-for-your-it-team`, feature/functional specs, `EMAIL-TO-DOCUMENT-ARCHITECTURE.md`) are **not in this repo**. Unverifiable here — owner to point to their location. |
| Existing strategy doc | `docs/enhancements/SPAARKE-AI-STRATEGY-AND-ROADMAP.md` (v1.0, 2026-02-21) still says "node-graph playbooks are the product"; frozen 2026-07-05. Stale duplicate at `docs/guides/SPAARKE-AI-STRATEGY-AND-ROADMAP.md`. This program should formally supersede it (and delete the duplicate). |
| MCP-first (§2.6) | Reverses three prior deferrals. State it as a reversal with the §2.2–2.3 reason. |
| §6.2 index names | `spaarke-knowledge-index-v2` retired; `insights-index` → `spaarke-insights-index`. |

---

## 4. Alignment with in-flight work

- **19 active worktrees contend on `Sprk.Bff.Api`**; `Services/Ai/` internals are solely owned by `spaarke-ai-architecture-redesign-r2` (live UAT); `unified-access-control-r2` is executing, security-critical, wide (22 confirmed enforcement findings); `spaarkeai-compose-r8` is `parallel-safe:false` on the entire Compose spine. → Ontology work must be **schema + ADR first, off the BFF hot path**. MCP egress as a **separate `Sprk.Mcp` project consuming `Services/Ai/PublicContracts/`** stays off the hot path entirely.
- **Wave 1 already has homes**: Legal Front Door is module #2 of `spaarke-SPA-external-access-platform-r2` (40 tasks, initialized, owner-gated); Email Triage is 37/38 in `email-communication-intelligence-r2` (deployed to dev, UAT). Realign both to *originate ontology objects* rather than start new modules.
- **Designs to harvest**: `spaarke-connect-integration-module-r1` (Connector Manifest), `ai-spaarke-action-engine-r1` (gates/authority), `agent-framework-fit-assessment-r1` (L3 substrate), `x-matter-performance-KPI-r1` (Wave 3), `ai-m365-copilot-integration` (declarative agent, 0% executed, overtaken).
- Standing constraint to add to §9.4: **Work IQ is a user-turn-only substrate** (chat / Context Binder); never on job or playbook paths. Matches the auth split already in code.

---

## 5. Recommended program shape

One program, four projects, in dependency order; each via `/design-to-spec` → `/project-pipeline`.

| # | Project | Scope | Hot path |
|---|---|---|---|
| 1 | `ontology-foundations-r1` | ADRs only: Legal Ops Ontology spec (objects/tiers/links/field classes on the real ERD + Insights epistemic tiers) · **Policy object** (versioned, immutable versions — 80% of temporal defensibility without bitemporal Dataverse) · authority-ledger extension to ADR-040 (authority, policy version, evidence, confirmer) · Binding registry / Connector Manifest **on the Spaarke Connect design** · settle `sprk_servicerequest` vs `sprk_legalrequest` · resolve D-F0 name collision | Skill-directives=Y (ADRs); BFF=N |
| 2 | `agreement-analysis-repositioning-r1` (D-n, ~3 weeks) | Make the Review Memo an ontology object: findings → Obligation, `standardRef` → Policy version, memo → approval gate, linked to Matter. Apply tool-neutrality to own module: **ingest Legal Agent for Word's output** as one more tool whose findings Spaarke records. Do **not** fill the eight empty knowledge packs — that lane is Microsoft's. | BFF=Y narrow (`Services/Ai/ReviewMemo/`), coordinate redesign-r2 |
| 3 | `spaarke-mcp-server-r1` (egress) | New `Sprk.Mcp` project over `PublicContracts/`: object read, link traversal, fact query (over `LiveFactResolver`s), gated action invocation (over Bindings + `ConfirmationPolicyEngine`). Reference shape: Harvey's five tools. Register with Agent 365 / Entra Agent ID. Streamable HTTP + Entra OAuth. | BFF=N (new service); consumes facade only |
| 4 | `l4-retrieval-decision-r1` (D-i) | Decision memo + spike, not a build: Foundry IQ for retrieval, Spaarke for epistemic classification above it. Foundry IQ adds what's missing (query planning, native SPE source, managed trimming); Spaarke keeps what it can't (Fact/Observation/Inference, `privilege_group_ids` semantics, non-Azure-OpenAI routing). | BFF=N until decided |

---

## 6. Open decisions for the owner

| ID | Decision | Phase 0 finding |
|---|---|---|
| D-a | Communication object | Decided (ADR-045); finish C4 reconciliation |
| D-b | `sprk_sourcebinding` entity vs field-set | Both, per Spaarke Connect: `sprk_externalconnection` (registry) + `(sprk_externalsystem, sprk_externalkey)` alt key (field-set) |
| D-c | Edge placement | Dataverse today for all governance edges; `IInsightGraph` Cosmos impl exists (r2) for similarity — matches the synopsis's recommendation |
| D-d | Ledger granularity | Extend ADR-040 (universal, typed) — do not add per-type entities |
| D-e | Identity resolution | Extend the rung ladder; no new service |
| D-f | Binding-mode promotion | Unaddressed in code; needs the manifest first |
| D-g | Benchmarking consent | Unaddressed; not before Wave 4 |
| D-h | Pricing | Commercial; not a codebase question |
| D-i | Foundry IQ vs hand-built RAG | Inventory complete (item 9); needs external research (§7) |
| D-j | Work IQ | Boundary rule proposed (§4); needs external research (§7) |
| D-k | Entra Agent ID for Dataverse | Fits the authority-ledger design; needs external research (§7) |
| D-l | Model routing incl. MAI | `ModelSelector` is deployment selection only; provider abstraction is new work |
| D-m | Wave 1.5 registry | Not evaluated here; note Agent 365 overlap risk stands |
| D-n | Agreement Analysis repositioning | Feasible in ~3 weeks (§2.6, §5 project 2) |
| **New** | `sprk_servicerequest` vs `sprk_legalrequest` | Must settle before Front Door ships |
| **New** | Supersede `SPAARKE-AI-STRATEGY-AND-ROADMAP.md` | Yes; delete `docs/guides/` duplicate |

---

## 7. External research required (targeted)

The synopsis's market research (19 Sep 2026) post-dates the assistant's training and should **not** be re-derived. These items gate build decisions and need verification against Microsoft primary sources via the `researcher` subagent (CLAUDE.md §15):

| Topic | Decision |
|---|---|
| Legal Agent for Word — GA date, playbook ingestion sources, any DMS/MCP connector support | D-n |
| Foundry IQ knowledge bases — SPE as source, Entra-group trimming semantics, query-planning model restriction, per-query pricing | D-i |
| Work IQ ISV access — delegated-only confirmed, Copilot Credits metering, tenant prerequisites | D-j |
| Entra Agent ID for Dataverse (preview 2026-08-06) — agent user + least-privilege role shape; Agent 365 registration requirements | D-k |
| MCP C# SDK (`ModelContextProtocol`) current version; Streamable HTTP + OAuth 2.1 / Entra auth; Copilot declarative-agent MCP GA state | Project 3 |
| Microsoft Agent Framework 1.0 GA — rc1 → GA breaking changes | Housekeeping |
| Dataverse audit / change-tracking capabilities and retention limits | Temporal ADR |
| LEDES 98B/2000 + UTBMS current spec | Wave 2 |

---

## 8. Evidence index (agent reports)

Six read-only verification agents ran 2026-09-19; findings are consolidated above. Raw transcripts are session-local and not retained in the repo. Key file anchors are cited inline per row.
