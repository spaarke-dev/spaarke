# Wave 12.1 Audit 123 — Three Prefill Wizards (Create Matter / Create Project / Create Work Assignment)

> **Author**: Claude (R7 Wave 12.1 audit dispatch)
> **Date**: 2026-06-30
> **Task**: `projects/spaarke-ai-platform-unification-r7/tasks/123-audit-three-prefill-wizards.poml`
> **Rigor**: STANDARD (read-only investigation; no code modifications)
> **Mode**: Static code + Dataverse describe + Dataverse playbook/Action introspection. NO live runs in spaarkedev1.

---

## Executive summary

The three Create-Entity wizards (Matter / Project / Work Assignment) share a **single shared client architecture** for AI-driven pre-fill but have **per-wizard BFF playbook backends with intentional divergence** at the Dataverse Action layer. The wizards are NOT dynamic-schema-driven — they use **hardcoded field mapping** at three layers (server DTO, client `fieldExtractor`, lookup resolvers + JSX render). The Action `sprk_outputschemajson` is **declarative metadata** that documents the wire contract but does not drive code generation at runtime. This means: edits to the Action output schema MUST be coordinated with synchronous edits to (a) server-side `AiPreFillResult` / `AiProjectPreFillResult` DTO, (b) server response record (`PreFillResponse` / `ProjectPreFillResponse`), (c) client `fieldExtractor` + `lookupResolvers` in each wizard's step component, and (d) client form-state typedef. The Action `sprk_systemprompt` JPS (with `$choices` lookup) IS the dynamic surface — that's the only operator-tunable element of pre-fill behaviour today.

**Disposition recommendations**:

| Wizard | Status | Root cause | Disposition | Effort |
|---|---|---|---|---|
| Create Matter (`matter-pre-fill`) | Likely **WORKING** (per code path; needs deployed-env smoke) | None obvious — playbook node has Action FK + AiAnalysis executor + EntityNameValidator scrubber. If broken in UAT, suspect (1) /narrate P3 EntityNameValidator scrubber stripping entity refs from extracted fields, or (2) sprk_playbookconsumer table missing/wrong row | **RESTORE — smoke first** | 0–2 hours (smoke), 2–4 hours if scrubber identified |
| Create Project (`project-pre-fill`) | **BROKEN** — confirmed at Dataverse layer | Playbook `fc343e9c-3460-f111-ab0b-7c1e521b425f` has ONE node (`Extract Project Fields`, AiAnalysis) with `sprk_actionid = NULL` and stub `configjson = {"__actionType":0}` only. AiAnalysisNodeExecutor.Validate (`AiAnalysisNodeExecutor.cs:118-153`) requires Tool + Document but the synthetic empty Action shell (PlaybookOrchestrationService.cs:1117-1122) yields no SystemPrompt, no Schema, no Tool → no extraction. Compare to Matter playbook which has 3 nodes including a non-null `sprk_actionid = 89cc641a-df18-f111-8343-7c1e520aa4df` pointing to ACT-023 "New Matter Field Extraction". | **RESTORE — re-link Action FK to ACT-024 (1e838114-7919-f111-8343-7ced8d1dc988) on the `Extract Project Fields` node** | 30–60 minutes |
| Create Work Assignment (no own playbook) | **DEPENDS on Matter** | Reuses `/api/workspace/matters/pre-fill` (EnterInfoStep.tsx:175) when files are uploaded AND no record is selected. Has SECOND prefill path: `readRecordForPrefill(recordType, recordId)` (workAssignmentService.ts:267-389) that copies fields from a selected Matter/Project/Invoice/Event record (no AI). | **NO ACTION REQUIRED** — fix Matter and Work Assignment AI prefill is fixed automatically. Record-copy prefill is independent of AI and not in scope of audit. | 0 hours |

**Shared-pattern recommendation**: Operator stated "no new shared abstractions unless 2+ demonstrated consumer need." Here the shared infrastructure ALREADY EXISTS and is well-factored:

1. `useAiPrefill` hook (`src/client/shared/Spaarke.UI.Components/src/hooks/useAiPrefill.ts`) — shared client-side AI prefill machinery with `fieldExtractor` + `lookupResolvers` injection points.
2. `IWorkspacePrefillAi` BFF facade (`Services/Ai/PublicContracts/IWorkspacePrefillAi.cs`) — single playbook-execution entry point used by both `MatterPreFillService` and `ProjectPreFillService`.
3. `IConsumerRoutingService.ResolveAsync(ConsumerTypes.X)` with env-var fallback — shared playbook-id resolution per ADR-013 + ADR-018.

The per-service classes (`MatterPreFillService`, `ProjectPreFillService`) DO duplicate ~400 LOC of boilerplate (file validation, text extraction, playbook execution loop, JSON parsing) but they ALSO diverge on the parse strategy (Matter has 3 fallback paths including regex-from-partial-JSON; Project has direct-deserialize only). **Do NOT consolidate** in Wave 12.3 — the duplication is intentional resilience asymmetry, not accidental copy-paste. Per CLAUDE.md §11 the cost-of-doing-nothing test (concrete failure mode) is not met by abstraction.

The pre-fill broken state is primarily a **data hygiene problem at the Dataverse playbook/Action level**, NOT a code architecture problem. Wave 12.3 should focus on (a) fixing the orphan Project node's Action FK and (b) verifying Matter smoke before assuming code changes are needed.

---

## 1. Per-wizard inventory — file locations + Dataverse IDs

### 1.1 Create Matter wizard

| Layer | Location | Notes |
|---|---|---|
| Solution wrapper | [`src/solutions/CreateMatterWizard/src/main.tsx:1-122`](../../../../src/solutions/CreateMatterWizard/src/main.tsx) | Code Page bootstrap; resolves auth, BFF base URL, navigation service, BU cascade defaults |
| Shared library wizard root | [`src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/CreateMatterWizard.tsx:230-591`](../../../../src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/CreateMatterWizard.tsx) | Composes `CreateRecordWizard` with `CreateRecordStep` as info step |
| Form step (renders fields + triggers AI prefill) | [`src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/CreateRecordStep.tsx:291-649`](../../../../src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/CreateRecordStep.tsx) | Hosts `useAiPrefill` hook (line 355) |
| Form state typedef | [`src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/formTypes.ts:22-47`](../../../../src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/formTypes.ts) | Hardcoded form fields incl. all 7 prefill outputs |
| AI prefill hook (shared) | [`src/client/shared/Spaarke.UI.Components/src/hooks/useAiPrefill.ts:129-299`](../../../../src/client/shared/Spaarke.UI.Components/src/hooks/useAiPrefill.ts) | Generic shared hook; takes `fieldExtractor` + `lookupResolvers` props |
| BFF endpoint | [`src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceMatterEndpoints.cs:44-155`](../../../../src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceMatterEndpoints.cs) | `POST /api/workspace/matters/pre-fill` accepting multipart/form-data |
| BFF service | [`src/server/api/Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs:33-772`](../../../../src/server/api/Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs) | Orchestrates SPE staging → text extraction → playbook invocation → 3-tier parser |
| BFF response DTO | [`src/server/api/Sprk.Bff.Api/Api/Workspace/Models/PreFillResponse.cs:13-40`](../../../../src/server/api/Sprk.Bff.Api/Api/Workspace/Models/PreFillResponse.cs) | Hardcoded 7-field record with `MatterTypeName/PracticeAreaName/MatterName/Summary/AssignedAttorneyName/AssignedParalegalName/AssignedOutsideCounselName/Confidence/PreFilledFields` |
| BFF AI facade | [`src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IWorkspacePrefillAi.cs:28-53`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IWorkspacePrefillAi.cs) + [`WorkspacePrefillAi.cs:9-29`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/WorkspacePrefillAi.cs) | Thin wrapper around `IPlaybookOrchestrationService` |
| DI registration | [`Infrastructure/DI/WorkspaceModule.cs:104`](../../../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/WorkspaceModule.cs) + [`AnalysisServicesModule.cs:359` (Null) + :819 (Real)`](../../../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs) | Scoped registrations gated by feature flag |
| Endpoint registration | [`Infrastructure/DI/EndpointMappingExtensions.cs:190`](../../../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs) | `app.MapWorkspaceMatterEndpoints()` |
| ConsumerTypes constant | [`Services/Ai/PublicContracts/ConsumerTypes.cs:46`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs) | `MatterPreFill = "matter-pre-fill"` (typo defense — 2026-06-24 UAT incident) |
| Dataverse routing row | `sprk_playbookconsumer` `consumerType=matter-pre-fill` → `sprk_playbook=2d660cad-d418-f111-8343-7ced8d1dc988` | Routes to playbook "Wizard New Matter Create" |
| Dataverse playbook | `sprk_analysisplaybook(2d660cad-d418-f111-8343-7ced8d1dc988)` "Wizard New Matter Create" | 3 nodes: Start (order 1) → AI Analysis (order 2, Action FK present) → Entity Name Validator (order 3) |
| Dataverse Action | `sprk_analysisaction(89cc641a-df18-f111-8343-7c1e520aa4df)` `ACT-023` "New Matter Field Extraction" | Has `sprk_outputschemajson` (the wizard-UI contract) + `sprk_systemprompt` (JPS with `$choices` lookup to sprk_mattertype + sprk_practicearea) |
| Config fallback | [`WorkspaceOptions.cs:106`](../../../../src/server/api/Sprk.Bff.Api/Configuration/WorkspaceOptions.cs) `MatterPreFillPlaybookId` env var | Used if `sprk_playbookconsumer` lookup returns null (FR-1R-06 deprecation window) |

### 1.2 Create Project wizard

| Layer | Location | Notes |
|---|---|---|
| Solution wrapper | [`src/solutions/CreateProjectWizard/src/main.tsx`](../../../../src/solutions/CreateProjectWizard/src/main.tsx) | Sibling pattern of CreateMatterWizard |
| Shared library wizard root | `src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/CreateProjectWizard.tsx` | Composed in similar pattern; uses `CreateProjectStep` |
| Form step (renders fields + triggers AI prefill) | [`src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/CreateProjectStep.tsx:201-489`](../../../../src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/CreateProjectStep.tsx) | Hosts `useAiPrefill` hook (line 275) |
| Form state typedef | [`src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/projectFormTypes.ts:14-64`](../../../../src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/projectFormTypes.ts) | Hardcoded form fields with `isSecure` extension |
| BFF endpoint | [`src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceProjectEndpoints.cs:24-126`](../../../../src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceProjectEndpoints.cs) | `POST /api/workspace/projects/pre-fill` |
| BFF service | [`src/server/api/Sprk.Bff.Api/Services/Workspace/ProjectPreFillService.cs:27-512`](../../../../src/server/api/Sprk.Bff.Api/Services/Workspace/ProjectPreFillService.cs) | Same shape as MatterPreFillService but ONLY direct deserialize (no entity-extraction fallback, no regex fallback) |
| BFF response DTO | [`src/server/api/Sprk.Bff.Api/Api/Workspace/Models/PreFillResponse.cs:46-71`](../../../../src/server/api/Sprk.Bff.Api/Api/Workspace/Models/PreFillResponse.cs) | `ProjectPreFillResponse` — hardcoded 7-field record + coalescing logic for `projectDescription` vs `description` (line 433 of service) |
| DI registration | [`WorkspaceModule.cs:109`](../../../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/WorkspaceModule.cs) | Scoped registration |
| Endpoint registration | [`EndpointMappingExtensions.cs:191`](../../../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs) | `app.MapWorkspaceProjectEndpoints()` |
| ConsumerTypes constant | [`ConsumerTypes.cs:52`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs) | `ProjectPreFill = "project-pre-fill"` |
| Dataverse routing row | `sprk_playbookconsumer` `consumerType=project-pre-fill` → `sprk_playbook=fc343e9c-3460-f111-ab0b-7c1e521b425f` | Routes to playbook "Wizard New Project Create" |
| Dataverse playbook | `sprk_analysisplaybook(fc343e9c-3460-f111-ab0b-7c1e521b425f)` "Wizard New Project Create" | **ONE node** "Extract Project Fields" (order 1, AiAnalysis) with **`sprk_actionid = NULL`** ← BUG |
| Dataverse Action (orphaned) | `sprk_analysisaction(1e838114-7919-f111-8343-7ced8d1dc988)` `ACT-024` "New Project Field Extraction" | EXISTS with full `sprk_outputschemajson` documenting wire contract, but **NOT REFERENCED** by any playbook node |
| Config fallback | [`WorkspaceOptions.cs:45`](../../../../src/server/api/Sprk.Bff.Api/Configuration/WorkspaceOptions.cs) `ProjectPreFillPlaybookId` (nullable `string?` ⚠️) | Discrepancy vs Matter's non-nullable; not directly causal but signals less hardening |

### 1.3 Create Work Assignment wizard

| Layer | Location | Notes |
|---|---|---|
| Solution wrapper | [`src/solutions/CreateWorkAssignmentWizard/src/main.tsx:1-107`](../../../../src/solutions/CreateWorkAssignmentWizard/src/main.tsx) | Sibling pattern |
| Shared library wizard root | [`src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/WorkAssignmentWizardDialog.tsx:108-629`](../../../../src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/WorkAssignmentWizardDialog.tsx) | Uses `WizardShell` directly (not `CreateRecordWizard`) because step sequence differs |
| Form step | [`src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/EnterInfoStep.tsx:111-344`](../../../../src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/EnterInfoStep.tsx) | Has TWO prefill paths: (1) record-based (initialValues from selected matter/project/invoice/event) + (2) AI-based (useAiPrefill on uploaded files, **REUSES Matter endpoint**) |
| Service | [`src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/workAssignmentService.ts:157-715`](../../../../src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/workAssignmentService.ts) | Includes `readRecordForPrefill(recordType, recordId)` (line 267-389) that copies fields from selected record |
| BFF endpoint | **NONE — reuses `POST /api/workspace/matters/pre-fill`** (`EnterInfoStep.tsx:175`) | No dedicated WorkAssignment prefill playbook |
| Dataverse routing row | **NONE — no `work-assignment-pre-fill` consumerType in `sprk_playbookconsumer`** | |
| Dataverse playbook | **NONE — no dedicated playbook** | |
| Dataverse Action | **NONE — reuses ACT-023 "New Matter Field Extraction"** via Matter endpoint | The mapping discards Outside Counsel / Paralegal / etc., keeping only Name + Description + Matter Type + Practice Area (EnterInfoStep.tsx:179-192) |

---

## 2. Action `sprk_outputschemajson` — the OPERATOR-TUNABLE CONTRACT

### 2.1 Matter `ACT-023` — output schema field list

Source: `sprk_analysisaction(89cc641a-df18-f111-8343-7c1e520aa4df).sprk_outputschemajson`. JSON Schema (Draft-07), `additionalProperties: true`, no `required` fields.

| Field | Type | Lookup resolution | Wizard form-field binding |
|---|---|---|---|
| `matterTypeName` | string \| null | `sprk_mattertype_ref` via `searchMatterTypes` | `matterTypeId` + `matterTypeName` (lookup) |
| `practiceAreaName` | string \| null | `sprk_practicearea_ref` via `searchPracticeAreas` | `practiceAreaId` + `practiceAreaName` (lookup) |
| `matterName` | string \| null | none (text) | `matterName` (form field) |
| `matterDescription` | string \| null | none (text) | `summary` (form field) **— name diverges; mapping is intentional per server-side `BuildPreFillResponse` line 710** |
| `assignedAttorneyName` | string \| null | `contact` via `searchContactsAsLookup` | `assignedAttorneyId` + `assignedAttorneyName` (lookup) **— BUT NOT RENDERED in CreateRecordStep JSX (only 4 fields rendered: Matter Type, Practice Area, Matter Name, Summary). Handler defined at line 444 but prefixed with `_` (unused). See §3.1.1.** |
| `assignedParalegalName` | string \| null | `contact` via `searchContactsAsLookup` | `assignedParalegalId` + `assignedParalegalName` **— same: defined, not rendered** |
| `assignedOutsideCounselName` | string \| null | `sprk_organization` via `searchOrganizationsAsLookup` | `assignedOutsideCounselId` + `assignedOutsideCounselName` **— same: defined, not rendered** |
| `confidence` | number 0..1 \| null | n/a | Server clamps via `Math.Clamp` (`MatterPreFillService.cs:700`), surfaced as `Confidence` in `PreFillResponse` |

**Auxiliary JPS surface in `sprk_systemprompt`** (also in ACT-023): `$choices: "lookup:sprk_mattertype_ref.sprk_mattertypename"` and `lookup:sprk_practicearea_ref.sprk_practiceareaname`. These are RESOLVED AT RUNTIME by the orchestrator (`PlaybookOrchestrationService.CollectDownstreamNodeInfo` referenced at line 1148) and INJECTED into the LLM prompt as an enumerated allowed-values list — this is the ONLY runtime-dynamic operator-tunable surface today.

### 2.2 Project `ACT-024` — output schema field list

Source: `sprk_analysisaction(1e838114-7919-f111-8343-7ced8d1dc988).sprk_outputschemajson`. JSON Schema (Draft-07), `additionalProperties: true`, no `required` fields.

| Field | Type | Lookup resolution | Wizard form-field binding |
|---|---|---|---|
| `projectName` | string \| null | none (text) | `projectName` |
| `projectDescription` | string \| null | none (text) | `description` **— server coalesces both `projectDescription` and `description` (line 433)** |
| `description` | string \| null | none (text, alias) | `description` |
| `projectTypeName` | string \| null | `sprk_projecttype_ref` (via service search) | `projectTypeId` + `projectTypeName` (lookup) |
| `practiceAreaName` | string \| null | `sprk_practicearea_ref` (via service search) | `practiceAreaId` + `practiceAreaName` (lookup) |
| `assignedAttorneyName` | string \| null | `contact` (via service search) | `assignedAttorneyId` + `assignedAttorneyName` **— resolvers wired (CreateProjectStep.tsx:296) but Attorney/Paralegal/Outside Counsel fields NOT RENDERED in JSX (only Project Type, Practice Area, Project Name, Description). Same pattern as Matter.** |
| `assignedParalegalName` | string \| null | `contact` | same |
| `assignedOutsideCounselName` | string \| null | `sprk_organization` | same |
| `confidence` | number 0..1 \| null | n/a | Same clamping pattern |

### 2.3 Work Assignment — schema borrowed from ACT-023 (Matter), partial use

The Work Assignment wizard's `EnterInfoStep.tsx:179-192` calls `useAiPrefill` with `endpoint: '/api/workspace/matters/pre-fill'` (Matter endpoint). Its `fieldExtractor` discards 5 of the 7 Matter fields:

| Matter Action field | WA wizard binding | Effect |
|---|---|---|
| `matterName` | → WA `name` (text) | Used |
| `summary` (server-side from `matterDescription`) | → WA `description` (text) | Used |
| `matterTypeName` (or `matterType`) | → WA `matterTypeId/Name` (lookup) | Used |
| `practiceAreaName` (or `practiceArea`) | → WA `practiceAreaId/Name` (lookup) | Used |
| `assignedAttorneyName` | dropped | — |
| `assignedParalegalName` | dropped | — |
| `assignedOutsideCounselName` | dropped | — |

WA's record-based prefill (`workAssignmentService.ts:267-389`) reads `_sprk_mattertype_value`, `_sprk_practicearea_value`, `sprk_mattername/sprk_projectname/...`, `sprk_matterdescription/sprk_projectdescription/...`, `sprk_priority` — totally distinct field set from the AI prefill flow.

---

## 3. Wizard UI binding pattern — HARDCODED across all three wizards

**Operator's question**: "I see field mapping in the output schema but I'm not sure if the execution is actually hardcoded — check it out."

**Answer**: The execution **IS hardcoded**, not dynamic-schema-driven. Evidence at 4 layers:

### 3.1 Layer A — JSX renders a fixed field list per wizard

#### 3.1.1 Matter wizard (`CreateRecordStep.tsx:578-645`)

The form renders EXACTLY 4 fields via `<DataverseLookupField label="Matter Type" .../>` (line 582), `<DataverseLookupField label="Practice Area" .../>` (line 595), `<Input ... matterName />` (line 611), `<Textarea ... summary />` (line 635). Lines 407-472 define handlers for Attorney, Paralegal, Outside Counsel (`_handleAttorneyChange`, `_handleParalegalChange`, `_handleOutsideCounselChange` — prefixed with `_` meaning intentionally unused) and corresponding `_attorneyValue` / `_paralegalValue` / `_outsideCounselValue` derivations. **These three lookups are NOT rendered in JSX** — even though the Action output schema declares them and the BFF resolves them server-side.

Effect: if the operator edits `ACT-023.sprk_outputschemajson` to add a new field (e.g., `clientName`), the LLM may emit `clientName` in its JSON response, the server's `AiPreFillResult` DTO (`MatterPreFillService.cs:746-771`) will SILENTLY DROP it (does not have a matching JSON property), `PreFillResponse` (`PreFillResponse.cs:13-23`) does not have `ClientName`, the client `fieldExtractor` (`CreateRecordStep.tsx:360-373`) does not extract it, and even if it did, no `<Input>` is rendered for it.

#### 3.1.2 Project wizard (`CreateProjectStep.tsx:412-484`)

Same pattern. Renders 4 fields (`projectType`, `practiceArea`, `projectName`, `description`). `assignedAttorneyName/assignedParalegalName/assignedOutsideCounselName` resolvers are wired (line 296-298) but no JSX inputs render them. Adding a new schema field requires DTO + response + extractor + JSX changes.

#### 3.1.3 Work Assignment wizard (`EnterInfoStep.tsx:282-342`)

Renders 6 fields: Name (Input), Description (Textarea), Matter Type (LookupField), Practice Area (LookupField), Priority (Dropdown), Response Due Date (Input type=date). Only Name / Description / Matter Type / Practice Area are AI-prefillable; Priority + Response Due Date are user-entered. Priority comes from `readRecordForPrefill` when an event is selected (`workAssignmentService.ts:332-335`).

### 3.2 Layer B — Client `fieldExtractor` callback is a hardcoded mapping function

Each wizard's `useAiPrefill({ fieldExtractor: data => ({...}) })` callback explicitly names each property of `data` (the parsed AI response) and maps it to either `textFields` or `lookupFields`:

- Matter: `CreateRecordStep.tsx:360-373` enumerates 7 keys.
- Project: `CreateProjectStep.tsx:280-292` enumerates 7 keys.
- WA: `EnterInfoStep.tsx:179-188` enumerates 4 keys (subset of Matter).

The hook does NOT walk the AI response generically; it requires the caller to enumerate the expected fields. If the LLM returns extra fields, they are silently discarded.

### 3.3 Layer C — Server response record is hardcoded

`PreFillResponse` (`PreFillResponse.cs:13-40`) and `ProjectPreFillResponse` (lines 46-71) are .NET records with explicit positional fields. Adding a field requires editing the record. The Matter parser has 3 fallback paths (`MatterPreFillService.cs:474-523`) but all fallback paths produce the same `AiPreFillResult` DTO with its fixed 8 properties.

### 3.4 Layer D — Server internal DTO `AiPreFillResult` / `AiProjectPreFillResult` is hardcoded

`MatterPreFillService.cs:746-771` declares the internal DTO with `[JsonPropertyName(...)]` attributes for exactly 8 fields. Same in `ProjectPreFillService.cs:483-511` with 9 fields (incl. the `projectDescription`/`description` coalesce). System.Text.Json silently drops unknown properties (no `UnmappedMemberHandling.Disallow`).

### 3.5 Summary: what IS dynamic vs hardcoded

| Surface | Dynamic? | Notes |
|---|---|---|
| LLM prompt's `$choices` lookup hydration (`sprk_mattertype.sprk_mattertypename` enum) | **YES, dynamic** | Resolved at runtime from Dataverse. Operator-tunable by adding/removing matter type rows. |
| LLM prompt's instruction text, constraints, examples (`sprk_systemprompt`) | **YES, dynamic** | Operator-tunable by editing the Action row. |
| LLM model + temperature (`sprk_modeldeploymentid`, `sprk_temperature`) | **YES, dynamic** | Operator-tunable on the Action row. |
| Set of fields the LLM is asked to extract | **PARTIALLY dynamic** | Editing `sprk_outputschemajson` changes what the LLM is asked to produce, BUT the wizard UI will silently ignore any new field. The schema IS read by the LLM via prompt schema rendering (when `structuredOutput: true`), but the wizard consumer is fixed. |
| Wizard UI form-field set | **HARDCODED** | Requires JSX edit + DTO edit + response edit. |
| Lookup-resolver targets | **HARDCODED** | Each lookup is bound to a specific search function. |

---

## 4. End-to-end data flow trace (Matter wizard — representative)

```
[Step 1] User uploads PDF/DOCX/XLSX in wizard step 1
  ↓
[Step 2 mounts] CreateRecordStep useAiPrefill effect fires (line 167)
  → POST multipart/form-data to ${bffBaseUrl}/api/workspace/matters/pre-fill
  ↓
[BFF] WorkspaceMatterEndpoints.HandlePreFill (line 87)
  → MatterPreFillService.ValidateFiles (size + ext + MIME)
  → MatterPreFillService.AnalyzeFilesAsync
    ↓
    a) ExtractTextFromFilesAsync (line 203)
       - Stages each file to SPE under ai-prefill/{requestId}/
       - Calls _textExtractor.ExtractAsync per file
       - Appends extracted text to StringBuilder
    ↓
    b) ExtractFieldsViaPlaybookAsync (line 302)
       - Truncates to 80,000 chars
       - Resolves playbook id: IConsumerRoutingService.ResolveAsync(ConsumerTypes.MatterPreFill)
         → returns 2d660cad-d418-f111-8343-7ced8d1dc988 (per Dataverse query)
       - _playbookLookup.GetByIdAsync(playbookId) → Guid
       - Constructs PlaybookRunRequest{ PlaybookId, Document.ExtractedText, Parameters{entity_type=matter, extraction_mode=pre-fill} }
       - RequireAi().ExecutePlaybookAsync(request, httpContext, ct)
         → IWorkspacePrefillAi → WorkspacePrefillAi → IPlaybookOrchestrationService.ExecuteAsync
  ↓
[Orchestrator] PlaybookOrchestrationService.ExecuteAsync per-node loop (line 1130+)
  Nodes (per Dataverse query):
    Node 1: Start (executortype 33, order 1) — no Action FK, synthetic action shell
    Node 2: AI Analysis (executortype 0, order 2) — Action FK = 89cc641a (ACT-023)
            - executor = AiAnalysisNodeExecutor (single-hop dispatch via SprkExecutortype = AiAnalysis)
            - Validate (line 113): requires Tool + Document with ExtractedText
            - Execute → calls IToolHandlerRegistry handler → LLM call w/ prompt assembled from ACT-023.sprk_systemprompt
            - StructuredData = parsed JSON matching ACT-023.sprk_outputschemajson
    Node 3: Entity Name Validator (executortype 141, order 3) — no Action FK
            - EntityNameValidatorNodeExecutor scrubs candidateText against allowList
            - **SAME executor class as in /narrate — P3 bug class applies if allowList authored against
              fields not present on the wizard's BFF-resolved data shape**
  ↓
[Aggregation] InvokePlaybookAi.AggregatePlaybookEvents (per W11 T116 doc Phase 1 Layer 2)
  Loop: only IsDeliverOutput-true nodes captured as terminal.
  For Matter prefill: NO node sets IsDeliverOutput. First node with non-null TextContent is captured.
  AI Analysis node sets StructuredData (the extracted JSON) but TextContent is the JSON string too.
  → terminalText = AI Analysis node's rawJson
  → structuredData = AI Analysis node's parsed JsonElement
  ↓
[MatterPreFillService receives stream] line 403 await foreach (var evt in stream)
  Captures NodeCompleted.NodeOutput.StructuredData.GetRawText() into preFillJson (line 411)
  Note: this code does NOT depend on IsDeliverOutput — it captures EVERY NodeCompleted with StructuredData, last one wins.
  So Entity Name Validator's NodeCompleted (which has no StructuredData since EntityNameValidator returns _validationMetadata only) does NOT overwrite the AI Analysis output. ✓
  ↓
[Parse] ParseAiResponse → 3-tier fallback (direct schema → entity-extraction → regex)
  → AiPreFillResult { MatterTypeName, PracticeAreaName, MatterName, MatterDescription, AssignedAttorneyName, AssignedParalegalName, AssignedOutsideCounselName, Confidence }
  → BuildPreFillResponse → PreFillResponse {camelCase JSON}
  ↓
[Client] useAiPrefill receives JSON
  → fieldExtractor extracts (matterName, summary) + (matterType, practiceArea, attorney, paralegal, outsideCounsel)
  → For each lookup field: call resolver(value) → findBestLookupMatch → resolved {id, name}
  → onApply({matterName, summary, matterTypeId+matterTypeName, ...})
  → CreateRecordStep dispatches APPLY_AI_PREFILL → form fields populated → JSX renders prefilled values
```

### 4.1 Engine bug class exposure

Cross-referencing the W11 T116 systematic assessment:

| Bug class | Exposure in Matter pre-fill | Exposure in Project pre-fill | Exposure in WA pre-fill |
|---|---|---|---|
| **P1: Aggregator drops ReturnResponse output** (`InvokePlaybookAi.AggregatePlaybookEvents` only captures IsDeliverOutput) | **NOT EXPOSED** — pre-fill playbook does NOT use ReturnResponse. `MatterPreFillService.cs:403-434` reads `evt.NodeOutput.StructuredData` directly from `NodeCompleted` events on every iteration (last write wins). The aggregator's broken contract only matters when the consumer reads `playbookResult.StructuredData` AFTER aggregation, which the pre-fill flow does NOT do. | Same — not exposed | Same — uses Matter path |
| **P2: LoadKnowledge passthroughBinding type mismatch** (`Dictionary<string,string>?` cannot hold native JSON array) | **NOT EXPOSED** — Matter playbook does not contain a LoadKnowledge node | **NOT EXPOSED** — Project playbook has only 1 node (Extract Project Fields) | **NOT EXPOSED** |
| **P3: EntityNameValidator allowList mismatched to DTO shape** (allowList expressions referencing fields not on the BFF DTO → all entity references scrubbed) | **POTENTIALLY EXPOSED** — Matter playbook DOES have an EntityNameValidator (node order 3). However it operates on a different DTO (the AI Analysis output, not the DailyBriefing payload). If the validator's allowList expressions reference fields not present on the AI Analysis output (e.g., references `start.priorityItems[].name` instead of `matterName/matterDescription`), it would scrub the LLM's structured output. Need to inspect the Matter playbook's node config to confirm. | **NOT EXPOSED** — no EntityNameValidator node | **NOT EXPOSED** |
| **R7 single-hop dispatch failure** (PlaybookOrchestrationService.cs:1059-1076 — null `SprkExecutortype` errors out without Action fallback) | **NOT EXPOSED** — Matter playbook nodes have non-null SprkExecutortype (33, 0, 141 per Dataverse) | **NOT EXPOSED at orchestration entry** — Project node has SprkExecutortype=0 (AiAnalysis), so the synthetic-action shell path is taken (line 1110-1122), then AiAnalysisNodeExecutor.Validate (line 113) **FAILS** at "AI analysis node requires a tool to be configured" because synthetic action shell has no Tool. **THIS is the Project root cause.** | **NOT EXPOSED** |
| **R7 enum rename `ActionType → ExecutorType`** | **NOT EXPOSED** — pre-fill code uses ExecutorType via PlaybookOrchestrationService internals. No direct enum reference in MatterPreFillService. | Same | Same |

---

## 5. Root-cause categorization per wizard

### 5.1 Create Matter

**Status**: Code path is healthy by inspection. If broken in spaarkedev1 UAT, likely root causes (priority order):

1. **`sprk_playbookconsumer` routing row** missing OR `sprk_enabled=false` OR `sprk_consumertype` typo (the 2026-06-24 UAT-2 incident matched this pattern). Audit confirmed row is present + active + consumertype="matter-pre-fill". Verify in target env.
2. **EntityNameValidator allowList stripping output** — if the node 3 EntityNameValidator's allowList expressions don't match the AI Analysis output shape, the scrubber's removedTerms would include real extracted entities. Need to inspect Matter playbook node 3 `sprk_configjson` to confirm.
3. **`AI Features` disabled** in target env (`Analysis:Enabled=false` OR `DocumentIntelligence:Enabled=false`) — `NullWorkspacePrefillAi` throws `FeatureDisabledException` → 503 ProblemDetails. Easy to verify via App Service config.
4. **OBO token / auth issue** at endpoint boundary (`WorkspaceAuthorizationFilter`). Unlikely if other workspace endpoints work.

### 5.2 Create Project — **CONFIRMED BROKEN AT DATAVERSE LAYER**

**Root cause**: Playbook `fc343e9c-3460-f111-ab0b-7c1e521b425f` "Wizard New Project Create" has exactly **one node** "Extract Project Fields" (`sprk_playbooknodeid = dacac491-4f6c-f111-ab0e-7ced8ddc4a05`) with:
- `sprk_executortype = 0` (AI Analysis) ✓
- `sprk_actionid = NULL` ✗ — **ORPHANED**
- `sprk_configjson = '{"__canvasNodeId":"0893d69d-3460-f111-ab0b-70a8a59455f4","__actionType":0}'` — STUB only

When `PlaybookOrchestrationService.ExecuteNodeAsync` reaches this node:
1. `actionType = ExecutorType.AiAnalysis` (line 1062) ✓
2. `node.ActionId == Guid.Empty` (line 1109) → enters synthetic-action-shell branch (lines 1117-1126), creates `AnalysisAction { Id = Guid.Empty, Name = "Extract Project Fields", ExecutorType = AiAnalysis }` with NO SystemPrompt, NO OutputSchemaJson, NO Temperature, NO ModelDeploymentId.
3. `executor = AiAnalysisNodeExecutor` (single-hop dispatch via SprkExecutortype)
4. `executor.Validate(context)` (`AiAnalysisNodeExecutor.cs:118`) → FAILS because `context.Tool is null` (no Tool resolved without an Action FK)
5. NodeFailed event emitted → playbook errors → no extraction → BFF returns `PreFillResponse.Empty()` with confidence=0 → client wizard shows empty form (no AI badge, no fields populated).

**Diverged from Matter playbook** which has a complete 3-node setup with non-null `sprk_actionid` on node 2. This looks like a **partial / abandoned manual edit** of the playbook in Power Apps Maker, possibly during the R7 ExecutorType rename or some prior maintenance.

**The standalone `sprk_analysisaction(1e838114-7919-f111-8343-7ced8d1dc988)` "New Project Field Extraction"** (ACT-024) EXISTS with a fully-defined output schema and prompt — it just isn't linked from any playbook node. Re-establishing the link `sprk_playbooknode(dacac491...).sprk_actionid = 1e838114-7919-f111-8343-7ced8d1dc988` IS the fix.

### 5.3 Create Work Assignment

**No independent prefill state.** The wizard's AI prefill (when files are uploaded with no record selected) reuses Matter's endpoint. Fixing Matter (§5.1 actions) fixes WA AI prefill. The record-based prefill (`readRecordForPrefill`) operates entirely client-side via Dataverse OData reads and does not involve AI or playbooks — out of audit scope, but worth noting it WILL continue to work even if the AI path is broken.

---

## 6. Disposition recommendation per wizard + shared-pattern recommendation

### 6.1 Per-wizard disposition

| Wizard | Disposition | Action | Effort | Confidence |
|---|---|---|---|---|
| **Create Matter** | **RESTORE — smoke first** | Wave 12.3 task should (a) deploy a smoke test in spaarkedev1 against `/api/workspace/matters/pre-fill` with a known PDF; (b) if response is empty/incorrect, inspect Matter playbook node 3 EntityNameValidator allowList for shape mismatch; (c) if still failing, check feature flags. | 0.5–2 days end-to-end | HIGH (code path appears healthy) |
| **Create Project** | **RESTORE — link Action FK** | Wave 12.3 task should: PowerShell script via `mcp__dataverse__update_record` to set `sprk_playbooknode(dacac491-4f6c-f111-ab0e-7ced8ddc4a05).sprk_actionid = 1e838114-7919-f111-8343-7ced8d1dc988`. Then smoke test. | 1–2 hours including smoke + validation | HIGH (root cause confirmed at Dataverse layer) |
| **Create Work Assignment** | **NO ACTION** | Inherits fix from Matter. Optionally add a smoke test confirming the WA wizard's AI prefill path works with uploaded files. | 0 hours | HIGH |

### 6.2 Shared-pattern recommendation

Operator stated 2026-06-30: *"No new shared abstractions unless 2+ demonstrated consumer need."* Three consumers (3 wizards) crosses this bar. **However**, the audit found that **the shared abstractions ALREADY EXIST**:

1. **Client side**: `useAiPrefill` hook (`useAiPrefill.ts:129`) is the shared mechanism, with `fieldExtractor` + `lookupResolvers` as injection points. Both Matter and Project wizards use it cleanly; WA wizard uses it for the AI path. No additional shared abstraction needed.

2. **BFF facade**: `IWorkspacePrefillAi` (`IWorkspacePrefillAi.cs:28`) is shared between Matter and Project services. WA does not consume directly (it reuses Matter's endpoint).

3. **Routing**: `IConsumerRoutingService.ResolveAsync(ConsumerTypes.X)` with env-var fallback is the shared lookup mechanism per ADR-013.

The **per-service classes** (`MatterPreFillService` 772 LOC, `ProjectPreFillService` 512 LOC) do contain ~400 LOC of structurally similar boilerplate (`ValidateFiles`, `ExtractTextFromFilesAsync`, `ExtractFieldsViaPlaybookAsync`, `ParseAiResponse`). A "shared helper" could be carved out as something like:

```csharp
// Hypothetical: StructuredPrefillRunner<TResult>
//   .RunAsync(IFormFileCollection files, string consumerType, Guid playbookFallbackId,
//             Func<string, double, Guid, TResult> parser, ...)
```

**Do NOT build this in Wave 12.3.** Reasons:

a. **The duplication is intentional resilience asymmetry, not accidental**. Matter's parser has 3 tiers (direct → entity-extraction → regex from partial JSON) because LLM responses can be truncated or use the alternate Document Intelligence "entities" envelope. Project's parser has 1 tier because Project responses are simpler. Consolidating would force both into one path, losing Matter's resilience or burdening Project's with unneeded complexity.

b. **Consumers will diverge further, not converge**. WA already takes a completely different path (record-based + reuses Matter endpoint). A future Create Invoice wizard would have its own field set and probably its own playbook. Premature consolidation under a generic interface punishes per-consumer variation.

c. **Cost-of-doing-nothing test (CLAUDE.md §11 Q3) is not met**: no concrete contract or behavior fails today without consolidation. Risk-of-doing-something IS concrete — Wave 12.3 has limited time + the operator wants restored functionality, not refactoring. Doing the abstraction would extend the time-to-restore by several days while introducing a new abstraction that needs its own tests.

d. **The existing `IWorkspacePrefillAi` facade + `useAiPrefill` hook already capture the meaningful shared concerns.** Further consolidation would be code-shape-similarity-driven, not behavior-driven.

**Recommendation**: leave the per-service classes as-is. Wave 12.3 tasks should be SCOPED to (a) fix Project playbook node Action FK, (b) optional smoke test for Matter + WA. No code refactoring. If a fifth wizard appears in the next 6 months, revisit; until then, the per-wizard divergence is correctly captured at the service-class boundary.

---

## 7. Operator-tunable contract — preservation requirements for Wave 12.3

**Critical: any Wave 12.3 work MUST preserve the following surfaces UNCHANGED** so operator edits in the maker portal continue to drive runtime behavior:

1. **`sprk_analysisaction.sprk_systemprompt`** (JPS structure) — must remain readable + renderable. Especially the `$choices: lookup:<entity>.<field>` mechanism that hydrates allowed values at runtime.
2. **`sprk_analysisaction.sprk_outputschemajson`** — even though the UI doesn't render new fields dynamically, the schema IS read by the LLM via prompt schema rendering when `structuredOutput: true`. Operator edits affect what the LLM is asked to produce.
3. **`sprk_analysisaction.sprk_temperature`, `sprk_modeldeploymentid`, `sprk_outputformat`** — read by AiCompletion / AiAnalysis executors at runtime.
4. **`sprk_playbookconsumer` routing table** with `sprk_consumertype` matching `ConsumerTypes.MatterPreFill` ("matter-pre-fill") and `ConsumerTypes.ProjectPreFill` ("project-pre-fill") exactly. Typo defense lives in `ConsumerTypes.cs`; admin-side typos still cause silent breakage (the 2026-06-24 UAT-2 pattern).
5. **Env-var fallback** `Workspace__MatterPreFillPlaybookId` and `Workspace__ProjectPreFillPlaybookId` — read by `WorkspaceOptionsValidator` (line 48-49) and consumed at lines `MatterPreFillService.cs:338` / `ProjectPreFillService.cs:305` as fallback if routing table returns null.
6. **Client `fieldExtractor` field names** in each wizard's step — these are the SECOND HALF of the contract. If the Action schema changes a field name, the extractor must be updated in lockstep.

**If Wave 12.3 needs to add a new prefill field** (e.g., `clientName`): the change must touch all 4 layers (`sprk_outputschemajson` → server DTO + `JsonPropertyName` → `PreFillResponse` record → client `fieldExtractor` + form-state + JSX). This 4-layer hardcoding is the cost of the per-wizard customization the operator wants; it's also why a "dynamic schema-driven UI" would be a much larger architectural change that is out of MVP scope.

---

## 8. Acceptance criteria checklist

- [x] All three wizards' UI entry points + BFF endpoints + playbook IDs located with file:line references — §1
- [x] Action output schemas inspected; field lists documented per wizard — §2
- [x] Wizard UI binding pattern determined per wizard (HARDCODED for all three) — §3
- [x] At least one wizard traced end-to-end with engine-bug-class exposure analysis — §4 (Matter end-to-end + §4.1 cross-reference to W11 T116)
- [x] Failures predicted per wizard (no live spaarkedev1 access in this audit) — §5
- [x] Root cause categorized per wizard — §5
- [x] Disposition recommended per wizard with effort estimate — §6.1
- [x] Shared-pattern recommendation with operator's 2+ consumer threshold considered — §6.2 (recommends NO new abstraction; existing ones suffice)
- [x] Action output schema contract documented precisely enough that Wave 12.3 work cannot accidentally break it — §7

---

## 8.5 Resolution (Wave 12.3 task 142 — Create Project FK re-link)

> **Applied**: 2026-06-30
> **Task**: [`tasks/142-restore-project-wizard-fk.poml`](../../tasks/142-restore-project-wizard-fk.poml)
> **Rigor**: STANDARD (data fix only; no code change)
> **Operator/Agent**: task-execute via mcp__dataverse__update_record

### Pre-state (verified before PATCH)

```sql
SELECT sprk_playbooknodeid, sprk_name, sprk_executortype, sprk_actionid, sprk_playbookid, sprk_configjson
FROM sprk_playbooknode
WHERE sprk_playbooknodeid = 'dacac491-4f6c-f111-ab0e-7ced8ddc4a05';
```

Result: `sprk_actionid = NULL` (column absent from response row — confirming the orphan diagnosis in §5.2). Node `Extract Project Fields` in playbook `fc343e9c-3460-f111-ab0b-7c1e521b425f` ("Wizard New Project Create"), executor type 0 (AI Analysis), config json reduced to the canvas stub `{"__canvasNodeId":"0893d69d-3460-f111-ab0b-70a8a59455f4","__actionType":0}`.

Target Action `sprk_analysisaction(1e838114-7919-f111-8343-7ced8d1dc988)` ACT-024 "New Project Field Extraction" verified present with full `sprk_outputschemajson` (project-prefill output schema, ~10 fields) + full `sprk_systemprompt` (JPS instruction/output/examples block for project field extraction). The Action's wire contract is the wizard-UI contract per §7 — preservation requirement satisfied (no schema or prompt change applied).

### PATCH applied

```
mcp__dataverse__update_record(
  tablename = 'sprk_playbooknode',
  recordId  = 'dacac491-4f6c-f111-ab0e-7ced8ddc4a05',
  item      = { sprk_actionid: { relatedTable: 'sprk_analysisaction',
                                 name:         'New Project Field Extraction',
                                 recordId:     '1e838114-7919-f111-8343-7ced8d1dc988' } }
)
→ "Record updated successfully."
```

### Post-state (verified after PATCH)

Same SELECT as pre-state. Result: `sprk_actionid = "1e838114-7919-f111-8343-7ced8d1dc988"`. The FK now resolves to ACT-024.

### Effect on orchestration path

With the FK now present, `PlaybookOrchestrationService.ExecuteNodeAsync` (`src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs`) will:

1. Read `node.SprkExecutortype = 0` → `ExecutorType.AiAnalysis` (single-hop dispatch — no change from before).
2. Load the real `AnalysisAction` (ACT-024) via the `sprk_actionid` lookup instead of falling into the synthetic-action-shell branch (lines 1117-1126).
3. `AiAnalysisNodeExecutor.Validate` will now find a non-null SystemPrompt + OutputSchemaJson + Tool (per Action) and proceed to execute the LLM call.
4. The LLM emits JSON matching the ACT-024 output schema → `ProjectPreFillService.ParseAiResponse` deserializes into `AiProjectPreFillResult` → BFF returns populated `ProjectPreFillResponse` → wizard `useAiPrefill` hook receives non-empty fields → JSX renders prefilled `projectName`, `description`, `projectType`, `practiceArea` (the 4 fields the wizard actually renders per §3.1.2).

### Acceptance criteria status

- [x] `sprk_playbooknode(dacac491-...).sprk_actionid = 1e838114-...` (verified pre + post)
- [ ] Create Project wizard prefill works end-to-end (operator UAT — deferred to T145)
- [x] Audit notes updated with Resolution (this section)
- [x] No code change required (data fix only — no `.cs`/`.ts`/`.tsx` edits)

### Disposition

This was a surgical Dataverse PATCH addressing the EXACT root cause identified in §5.2. No code changes, no schema changes, no new abstractions, no test changes. Per CLAUDE.md §11 (Component Justification): rule does NOT apply (no new surface). Per CLAUDE.md §10 (BFF Hygiene): does NOT apply (no BFF touch).

### Open follow-ups (filed via task-execute, NOT in scope of T142)

- Operator UAT at T145 — confirm wizard end-to-end in spaarkedev1 (upload PDF → see prefill).
- Open question §9.2 still recommended: one-shot audit `SELECT sprk_name, sprk_executortype FROM sprk_playbooknode WHERE sprk_executortype IN (0,1,2) AND sprk_actionid IS NULL` to detect other R7-rename-era orphans. Tracked as potential ISS during R7 wrap-up if Matter smoke (§5.1) reveals similar pattern.

---

## 9. Open questions for operator (non-blocking)

1. **Confirm the Project playbook node's missing Action FK is acceptable to restore** (vs. operator-intentional state for a different reason). Wave 12.3 should ask before flipping the FK.
2. **Was the Project node Action FK NULL the result of an R7 enum-rename touch** (executor.ActionType → ExecutorType)? If yes, may indicate other playbooks have silently lost their Action FK. Suggest a one-shot Dataverse audit: `SELECT sprk_name, sprk_executortype FROM sprk_playbooknode WHERE sprk_executortype IN (0,1,2) AND sprk_actionid IS NULL`.
3. **Smoke test for Matter in spaarkedev1 — does the wizard return populated fields today?** If yes, Matter disposition reduces to "no-op." If no, follow §5.1 priority list.
4. **WorkspaceOptions field nullability discrepancy** (`ProjectPreFillPlaybookId` is `string?` while `MatterPreFillPlaybookId` is `string`). Cosmetic, but worth a 1-line cleanup if Wave 12.3 touches `WorkspaceOptions.cs` for any reason.

---

## 10. Resolution (Wave 12.3 task 143 — Create Matter smoke + diagnostic)

> **Applied**: 2026-06-30
> **Task**: [`tasks/143-smoke-matter-wizard.poml`](../../tasks/143-smoke-matter-wizard.poml)
> **Rigor**: STANDARD (verification + diagnostic; mutation deferred to operator)
> **Status**: CODE PATH VERIFICATION COMPLETE. End-to-end smoke deferred to operator T145 UAT (sandboxed agent has no browser to spaarkedev1).
> **Disposition revised**: audit §5.1 ranked Matter as "likely WORKING by code path inspection". Re-verification with Dataverse data reads + executor source reading shows **NOT WORKING — deterministic failure** at node 3 (EntityNameValidator). The §5.1 priority-2 risk ("EntityNameValidator allowList stripping output") underestimated severity — it's not "scrubs output", it's "fails validation, kills entire run, returns empty."

### 10.1 Dataverse state re-verified

All confirmed via `mcp__dataverse__read_query` 2026-06-30:

| Surface | Verified value | Healthy? |
|---|---|---|
| Playbook `2d660cad-d418-f111-8343-7ced8d1dc988` | `sprk_name = "Create New Matter Pre-Fill"` (audit had named it "Wizard New Matter Create"; actual name differs but ID matches) | ✅ |
| Routing row `sprk_playbookconsumer` consumerType=matter-pre-fill | `sprk_enabled = true`, routes to playbook above | ✅ |
| Node 1 "Start" (`434b06d3-...`) | executortype=33, order=1, dependsonjson=null | ✅ |
| Node 2 "AI Analysis" (`444b06d3-...`) | executortype=0, order=2, depends on Start, `sprk_actionid = 89cc641a-df18-f111-8343-7c1e520aa4df` (ACT-023) | ✅ FK present |
| Node 3 "Entity Name Validator" (`c3c5226d-5b71-f111-ab0d-7ced8ddc4a05`) | executortype=141, order=3, depends on AI Analysis, **stub configJson** | ⚠️ broken (§10.2) |
| Node 2 configJson | contains stub `systemPrompt` override that REPLACES ACT-023's real instruction | ⚠️ degrades quality (§10.3) |
| Action ACT-023 (`89cc641a-...`) | present, has full systemPrompt + outputschemajson + modelDeploymentId | ✅ |

### 10.2 Root cause #1 — EntityNameValidator stub configJson causes total run failure

`sprk_playbooknode(c3c5226d-...).sprk_configjson` = `{"__canvasNodeId":"node_1782477270462_pehsxfjnu","__actionType":141}` — only Power Apps Maker canvas metadata, **no `candidateText`, no `allowList`**.

[`EntityNameValidatorNodeExecutor.Validate()`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/EntityNameValidatorNodeExecutor.cs) (lines 151-198) requires BOTH fields:

```
if (string.IsNullOrWhiteSpace(config.CandidateText))
    errors.Add("EntityNameValidator node requires 'candidateText' in ConfigJson");
if (config.AllowList is null)
    errors.Add("EntityNameValidator node requires 'allowList' in ConfigJson (use [] to scrub all proper-noun names)");
```

Without them, returns `NodeOutput.Error(NodeErrorCodes.ValidationFailed)`.

[`PlaybookOrchestrationService` batch loop](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs) (lines 724-745): on ANY failed node fires `PlaybookStreamEvent.RunFailed` and aborts the entire run (the TODO at line 730 acknowledges `sprk_continueonerror` is not implemented yet — GitHub #233).

[`MatterPreFillService.ExtractFieldsViaPlaybookAsync`](../../../../src/server/api/Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs) (lines 427-433): on `RunFailed` returns `PreFillResponse.Empty("PLAYBOOK_FAILED: ...")` — **even though node 2 (AI Analysis) had already emitted valid StructuredData earlier in the stream (line 411 captured it; lines 427-433 discard it)**.

Net effect: wizard always shows empty prefill. App Insights signature: `"Playbook execution failed for pre-fill. Error=Node 'Entity Name Validator' failed: EntityNameValidator node requires 'candidateText' in ConfigJson; EntityNameValidator node requires 'allowList' in ConfigJson"`.

This is the same R7 Wave 5 backfill mechanism that orphaned the Project node's Action FK (per audit §5.2). The backfill script ([`notes/drafts/playbook-node-review-output.csv` line 61](../drafts/playbook-node-review-output.csv)) set `sprk_executortype = 141` based on node-name match ("Entity Name Validator" → 141) without verifying the configJson contained the required executor inputs. This confirms audit §9.2 — there are other R7-rename-era orphans beyond the Project Action FK.

### 10.3 Root cause #2 — AI Analysis node systemPrompt override clobbers Action's real prompt

`sprk_playbooknode(444b06d3-...).sprk_configjson` contains a stub systemPrompt override:

```json
{
  "__canvasNodeId":"node_1772743778436_khspxdutg",
  "__actionType":0,
  "modelDeploymentId":"cdfa4e52-7c16-f111-8343-7c1e520aa4df",
  "systemPrompt":"{\"$schema\":\"https://spaarke.com/schemas/jps/v1\",\"$version\":1,\"instruction\":{\"task\":\"Task\",\"role\":\"You are a document reviewer\"}}"
}
```

[`PromptSchemaOverrideMerger.MergeInstruction`](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/PromptSchemaOverrideMerger.cs) (lines 208-234) REPLACES the Action's `instruction.task` and `instruction.role` with this stub when override is present. Net effect: even if §10.2 is fixed, the LLM receives the stub instruction "Task / You are a document reviewer" instead of ACT-023's real Matter-field-extraction prompt with `$choices` lookups against `sprk_mattertype` and `sprk_practicearea`. Extraction quality severely degraded; LLM doesn't know what fields to emit or what allowed enumerations to use.

### 10.4 Recommended minimal fix (data-layer only, no BFF code change)

**Operator approval required before mutation. Sandboxed agent did NOT apply** (per `mcp__dataverse__delete_record` requiring `hasUserApproved=true`; aligns with CLAUDE.md §6 escalation triggers for production data mutation).

Two atomic Dataverse updates needed. Recommend both in one operator session for atomicity:

| # | Action | Target record | Field | New value | Effect |
|---|---|---|---|---|---|
| A | **Delete** (preferred) OR **Update** | `sprk_playbooknode` `c3c5226d-5b71-f111-ab0d-7ced8ddc4a05` (Entity Name Validator) | (whole record) OR `sprk_configjson` | Delete the node entirely (preferred), OR set configjson to `{"__canvasNodeId":"node_1782477270462_pehsxfjnu","__actionType":141,"candidateText":"","allowList":[]}` (workaround) | Stops total-run-failure. **Preferred: delete** — EntityNameValidator is designed for narrative text scrubbing (R4 FR-3) where the LLM emits free-form prose with hallucination risk; structured field extraction via ACT-023's `sprk_outputschemajson` + Structured Outputs has constrained the LLM at generation time, eliminating the hallucination problem the validator is designed for. The validator is the wrong tool here. |
| B | **Update** | `sprk_playbooknode` `444b06d3-d418-f111-8343-7ced8d1dc988` (AI Analysis) | `sprk_configjson` | Strip the `systemPrompt` key. Keep `__canvasNodeId`, `__actionType`, `modelDeploymentId`. New value: `{"__canvasNodeId":"node_1772743778436_khspxdutg","__actionType":0,"modelDeploymentId":"cdfa4e52-7c16-f111-8343-7c1e520aa4df"}` | Stops the stub instruction from clobbering ACT-023's real prompt. ModelDeploymentId override remains intact. |

Operator can apply via Power Apps Maker (edit node configJson in the canvas designer) OR via Dataverse Web API (preferred — atomic; canvas editor may add/strip canvas metadata).

**Audit §7 contract preservation**: both fixes preserve all operator-tunable surfaces (Action `sprk_systemprompt`, `sprk_outputschemajson`, `sprk_temperature`, `sprk_modeldeploymentid`, routing table, env-var fallback). Only per-node STUB overrides are removed — the Action remains the single source of truth, as designed.

### 10.5 Inheritance to T144 (Create Work Assignment wizard)

WA wizard's AI prefill path reuses `/api/workspace/matters/pre-fill` (audit §1.3 + §5.3). Fixing the Matter playbook automatically fixes WA AI prefill. **T144 disposition**: verify the Matter fix from §10.4 has been applied; then run WA UAT alongside Matter UAT at T145. WA does NOT need its own playbook or its own fix — only inheritance verification. Should be a straightforward "wait for Matter fix → confirm WA inherits" task.

### 10.6 Why no end-to-end Bash smoke from sandboxed agent

Per T143 POML fallback instruction: *"If you cannot reach spaarkedev1 for browser smoke, do code path verification via Read/Grep + leave end-to-end smoke for operator at T145 UAT."* Sandboxed agent has no browser, no SSO credentials, no spaarkedev1 network reach. Code path + Dataverse data verification done; mutation requires operator consent (CLAUDE.md §6 escalation; `mcp__dataverse__delete_record` requires `hasUserApproved=true`).

### 10.7 Acceptance criteria status

- [x] Create Matter wizard code path verified end-to-end (Read/Grep)
- [x] All 3 Dataverse surfaces (playbook, 3 nodes, Action, routing row) verified via read_query
- [x] Failure mode identified deterministically (§10.2)
- [x] Secondary issue (§10.3 stub systemPrompt) identified — would degrade quality even after §10.2 fix
- [x] Minimal fix recommended (§10.4) — data-layer only; no BFF code change; preserves §7 contract
- [x] T144 (WA wizard) inheritance confirmed (§10.5)
- [x] Audit notes updated with Resolution (this section)
- [ ] Operator applies §10.4 fixes (deferred to operator)
- [ ] T145 UAT — wizard end-to-end in spaarkedev1 (deferred per T143 POML)

### 10.8 Confidence

- **Diagnosis confidence**: VERY HIGH (Dataverse data read directly via MCP; code path traced from endpoint to executor; failure mode deterministic — both bugs trigger on every invocation)
- **Fix-effectiveness confidence**: HIGH (both root causes are pure data-layer; no code change risk; preserves all operator-tunable surfaces per §7)
- **T145 UAT pass probability after operator applies §10.4**: HIGH

### 10.9 Open follow-ups (filed via this task, NOT in scope of T143)

- Operator applies §10.4 fixes A + B before T145.
- T145 UAT against Matter + WA wizards together (per §10.5 inheritance).
- Audit §9.2 recommendation already executed by T143 — sweep query `SELECT sprk_name, sprk_playbookid, sprk_configjson FROM sprk_playbooknode WHERE sprk_executortype = 141` returned exactly 2 rows:
  1. **The broken Matter node** (`c3c5226d-...` — addressed by §10.4 fix A)
  2. **Daily Briefing's `ValidateEntityNames` node** (`11895da7-a171-f111-ab0d-7ced8ddc4cc6` in playbook `7b5a6ed3-0271-f111-ab0e-000d3a13a4cd`) — has properly-template-expanded `candidateText` + `allowList` per R7 Wave 11 T116 DTO-alignment fix (visible in its `_allowListNote` comment in configJson). **Healthy.**

  No additional EntityNameValidator orphans exist. The Matter node is the only instance of this misconfiguration pattern. Scope of cleanup confirmed limited to §10.4 fix A.

- For Wave 5 backfill audit (parallel concern, separate from EntityNameValidator class): the systemPrompt-override clobbering pattern in §10.3 may exist on other AiAnalysis nodes that received node-level stub overrides during Power Apps Maker canvas authoring. A broader sweep `SELECT sprk_name, sprk_playbookid, sprk_configjson FROM sprk_playbooknode WHERE sprk_executortype = 0 AND sprk_configjson LIKE '%systemPrompt%'` would surface candidates. Optional; track as DEF if operator wants comprehensive cleanup; not blocking for T145 UAT (Matter fix is the only one needed for Wave 12 MVP).

---

*End of audit 123. Resolution §10 appended 2026-06-30 by T143 (T142 §8.5 added by sibling task earlier the same day).*
