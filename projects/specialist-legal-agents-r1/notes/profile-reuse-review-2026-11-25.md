# Attorney profile: reuse review of the AI definition and orchestration stack (2026-11-25)

> **Method.** Read-only, Fable-level review of master at `f74f5573a0` (worktree `spaarke-wt-spaarkeai-mcp-bff-api-r2`). Columns were read from `src/dataverse/solutions/SpaarkeMaster/Entities/*/Entity.xml`, not from live metadata.
> **Owner request (2026-11-25):** "do a full Fable level review of existing to ensure we are using what we have before creating new".
> **Spot-checked by the main session.** These exist: `sprk_UserProfile` (with `sprk_systemuser`, `sprk_profileversion` and `sprk_assistantpreferences`), `sprk_aipersona`, `sprk_procedureversion`, `sprk_PolicyVersion`, `sprk_playbookrun` and `StatedProfileReader`. No BFF code writes the `sprk_playbookrun` table; the `.cs` hits are the `appnotification.sprk_playbookrunid` column.
>
> Abbreviations: `BFF` = `src/server/api/Sprk.Bff.Api`; `SM` = `src/dataverse/solutions/SpaarkeMaster`.

**Main finding:** a per-user profile entity already exists, and it already reaches the prompt. Design §6's "Absent / 0 hits" was wrong.

## 1. Reuse map

| Requirement | Existing component | Fit | Gap or risk |
|---|---|---|---|
| L3 anchor, 1:1 with `systemuser` | `sprk_userprofile`: alternate key `sprk_systemuser`, `sprk_primaryrole`, `sprk_focusareas`, `sprk_assistantpreferences`, `sprk_profileversion`, N:N to `sprk_practicearea_ref`. The SystemUser main form already references it. | Reuse as the anchor | User-owned and written directly from the client (`src/solutions/SpaarkeAi/src/components/assistant/userProfileService.ts`). There is no server owner, so it cannot hold ratcheted or approved content |
| Role (partner, associate, paralegal) | `sprk_primaryrole`, global set `sprk_userprofilerole` (14 values) | Reuse as is | Self-declared. If role drives authority, it needs a server-owned source |
| Profile reaches the prompt | `BFF/Services/Ai/Context/StatedProfileReader.cs:84-95`, `StatedProfileRenderer.cs`, `ContextBinder.cs:713` | Extend | Chat path only. Preference-only by design: it must never feed `AgentToolFilterContext` (ADR-039) |
| L1 archetype (role charter) | `sprk_aipersona` (`sprk_personacode`, `sprk_systemprompt`, `sprk_parentpersonaid`, `sprk_scopetype` Global / Tenant / PlaybookAttached); `sprk_analysisplaybook.sprk_playbookpersona` | Partial | `AnalysisPersonaService.cs:150-190` picks the most specific persona rather than merging. The parent lookup is mapped (`:327`) but never walked |
| Function key for `positions[...]` | `sprk_analysisplaybook.sprk_playbookcode`, `sprk_analysisaction.sprk_actioncode` | Reuse as the key space | Design §6 also keys positions by accelerator `topics.yaml` IDs; one must be chosen |
| L2 variant, scoped by practice area | `sprk_practicearea_ref` (already N:N to the profile) | Reuse as the scoping key | No entity holds L2 content |
| L4 matter overlay | `sprk_procedureassignment`: `sprk_matter` / `sprk_project` / `sprk_workassignment` / `sprk_firm` targets, `sprk_mode` (Follow latest / Pinned / Excluded), `sprk_pinnedversion`, `sprk_reason` | Reuse the pattern; reusing the table is an owner question | Billing-shaped service (`BFF/Services/Finance/Procedures/ProcedureAssignmentService.cs`). No user or practice-area target |
| Versioning and one-action rollback | `sprk_procedureversion` (Draft / Active / Superseded / Retired, `sprk_previousversion`, `sprk_versionnumber`); `sprk_policy.sprk_currentversion` with `sprk_policyversion.sprk_basedonversion`. Publish supersedes the predecessor in one transaction (`ProcedureService.cs:752`) | Reuse the pattern | `sprk_procedureversion` has no body column. `sprk_userprofile.sprk_profileversion` is a client-side counter (`useMyAssistant.ts:209`). `sprk_analysisplaybook.sprk_version` has no BFF reference |
| Proposed diff, then approve | `sprk_policyversion.sprk_proposalstatus` (Proposed / Accepted / Rejected), `sprk_rejectionreason`; `RuleSetService.DecideAsync` (`BFF/Services/Finance/RuleSetService.cs:2181`), which takes a row-version token | Reuse the pattern | `sprk_policyversion` is a predicate rule; do not store profiles in it |
| Behaviour diff before approval | `RecordProcedureReadService` dry run ("Now" / "If saved now", plus a disclosure of what the preview cannot know) | Reuse the pattern | It compares rule resolution, not model output |
| Golden set | `tests/integration/contract/Eval/*` (15 suites, JSON case files such as `nda-review-eval-cases.json`), CI only | None at run time | No eval endpoint or service in the BFF |
| Approval and audit trail | `sprk_decisionrecord`; `DecisionRecordWriter.cs:531` (append-only). Precedent for a configuration change: `RecordProcedureDecision` (`BillingDecisionRecorder.cs:82`) | Extend | Decision Records are filed under a core record; a profile has none. The chat gate writes no Decision Record |
| Authority, gates and data access (ratchet inputs) | `sprk_analysistool.sprk_sideeffectclass` / `sprk_permissionscope` / `sprk_requiredcapability`; `sprk_analysisaction.sprk_groundedtoolallowlist` / `sprk_allows*` / `sprk_modeltier`; `sprk_playbookconsumer.sprk_risk` / `sprk_modeltieroverride`; `Chat/Gate/ConfirmationPolicyEngine.cs` | Extend: these are the L0/L1 ceilings | The gate takes no profile input. `sprk_analysisaction` carries two allow-list columns (`sprk_Grounded_Tool_Allow_List` and `sprk_GroundedToolAllowList`) |
| Style merge | `BFF/Services/Ai/PromptSchemaOverrideMerger.cs:23` | Reuse for style only | See §2 |
| Exemplar documents | `sprk_analysisknowledge.sprk_documentid`; `PromptSchema.Examples` (`Services/Ai/Models/PromptSchema.cs:59`) | Reuse as is | No per-user ownership convention |
| Conversational tuning | Memory `Preference` items (`Memory/MemoryFactType.cs:42`); confirmed-only steering through a closed allow-list (`Context/PreferenceDirectiveProducer.cs`) | Partial: capture and confirm exist | Cosmos, unversioned, and by design never grants a capability |
| Correction harvesting | `sprk_precedent` (`sprk_patternstatement`, `sprk_samplesize`, `sprk_effectivenessscore`, status Tentative / Confirmed, `sprk_reviewerby`) | Partial | `[unverified]` whether its producer can be pointed at agent corrections |
| Run records the profile version | `sprk_playbookrun` (`sprk_triggeredby` → systemuser, `sprk_inputcontextjson`, `sprk_outputsjson`) and `sprk_playbooknoderun` | Extend: add a profile-version lookup | No BFF code writes these tables. The session ledger's `SessionContextFingerprint` is erasable |
| Compiled set returned by a tool | `BFF/Api/Mcp/` (`McpToolCatalog.cs`, `McpToolAuthorization.cs`) | Extend: one new tool | None of the existing tools is profile-aware |
| Chaining | `PromptSchema.PriorOutputReference` (`:153`), `sprk_playbookrun.sprk_outputsjson` | Not blocked | No parent-run lookup |
| Profile page UI | `MyAssistantDialog.tsx`; `src/solutions/RuleReview`; `src/solutions/RecordProcedures`; shared `DiffCompareView`, `DecisionRecords`, `Wizard` | Reuse the components | `ScopeConfigEditor` PCF and PlaybookBuilder's `SchemaJsonEditor` are bound to scope entities; only the JSON editor transfers |

## 2. What not to reuse

- **`PromptSchemaOverrideMerger` as the layer compiler.** It concatenates arrays and replaces scalars, and `__replace` lets an override discard base constraints. So a lower layer can loosen anything.
- **`sprk_userprofile` columns for the profile body.** The client writes the row directly, so no ratchet or approval can be enforced on it.
- **`sprk_userpreferences`.** It is a UI key-value store.
- **`sprk_policy` / `sprk_policyversion` as storage.** These are predicates; D-209 says a Playbook is not a Policy.
- **Scope "Extend" / "Save As" as inheritance.** It copies; the parent is kept in memory only (`AnalysisActionService.cs:481,514`).
- **`NdaStandardClauseProvider`.** Its positions are a hard-coded dictionary, not data.
- **`ReviewMemoAssembler`.** It is a pure assembler, with no positions.
- **The session ledger as the run record.** It is erasable per session (ADR-040).
- **`MemoryCompositionService`.** It is dark (`ContextBinder.cs:579`).

## 3. Recommended data shape

Hang the profile off what exists:
- `sprk_userprofile` stays the L3 anchor and the page's entry point from the user record.
- `sprk_aipersona` (or the playbook's persona link) identifies the L1 archetype.
- `sprk_practicearea_ref` scopes L2; `sprk_matter` scopes L4.
- Position keys reuse `sprk_playbookcode` / `sprk_actioncode`.
- Tool, model-tier and risk ceilings are read from the existing scope columns, not copied.

New surface: one entity pair, modelled on `sprk_policy` / `sprk_policyversion` and `sprk_procedureversion`.
- **`sprk_agentprofile`** (identity; one row per layer instance): layer, parent profile, archetype, practice area, user profile, matter, current version.
- **`sprk_agentprofileversion`** (immutable): version number, status (Draft / Proposed / Active / Superseded / Rejected), previous version, body JSON, diff source, golden-set result.

All writes go through the BFF, field-secured. Rollback repoints the current version. Add a profile-version lookup and a parent-run lookup on `sprk_playbookrun`.

CLAUDE.md §11:
1. **Existing.** It overlaps `sprk_userprofile` (unversioned, client-written), the Procedure tables (versioning and matter pinning, but no body and no user scope) and `sprk_policyversion` (the proposal lifecycle, but a predicate).
2. **Extension.** Not `sprk_userprofile`, because it is client-written. Extending the Procedure tables would need a body column, user and practice-area scope and a layer parent. It also carries billing invariants, and the ontology defines a Procedure as "provenance only: never evaluated".
3. **Cost of doing nothing.** No row records which profile version a run used, rollback has no target, and an attorney's direct write could raise their own authority.

## 4. Corrections to the program design

- **§6 Agent Instance "Absent" and Role Profile "no per-user overlay, 0 hits":** wrong. See §1.
- **§3.14 placement choice:** the 1:1 entity exists. The decision is what to add beside it.
- **§6 Role Profile path:** the type is `Services/Ai/Models/PromptSchema.cs`, not `Services/Ai/Schemas/PromptSchema`.
- **§6 Position Library:** `sprk_aiplaybook` does not exist; the entity is `sprk_analysisplaybook`.
- **§6 Authority and Gate:** `GateDecisionRecordJob` records the communications filing gate, not the chat confirmation gate.
- **§6 Position Library, "stored as an Internal Standard Procedure":** ontology-area Procedures are document-only in R1 (`ontology-component-model.md:118`, D-175(3)). The content would be a stored file, not structured positions.
- **§6 Run Ledger:** it omits `sprk_playbookrun` / `sprk_playbooknoderun`, which exist and which the BFF never writes.
- **Doc drift:** `docs/architecture/scope-architecture.md:25-26` cites `Services/Scopes/ScopeInheritanceService.cs` and `ScopeCopyService.cs`. Neither exists, and inheritance is not persisted.

## 5. Open questions for the owner

1. A new `sprk_agentprofile` pair, or extend the Procedure tables?
2. Is the archetype a `sprk_aipersona` row, a template `sprk_analysisplaybook`, or a layer-1 profile row pointing at both?
3. Does role drive authority? If so, `sprk_primaryrole` cannot stay self-declared.
4. Should `sprk_assistantpreferences` and `sprk_focusareas` migrate into the L3 style section, or stay a separate chat-only input?
5. Where do profile-diff approvals live: a Decision Record with no core record, or the version row's own status and approver columns?
6. Is the golden-set run a BFF job over the existing JSON case files, or does it stay in CI?
7. Should the BFF start writing `sprk_playbookrun` as the durable run record?
8. Position key: playbook/action code, or accelerator `topics.yaml` IDs?
