# Compose JPS Input Scopes — Locked Catalog

> **Status**: LOCKED (Phase 1 task 012 deliverable)
> **Project**: spaarkeai-compose-r1
> **Date**: 2026-06-29
> **Source**: Spike #4 (`notes/spikes/spike-4-consumer-routing-jps.md` §4)
> **POML**: [`tasks/012-jps-register-compose-scopes.poml`](../../tasks/012-jps-register-compose-scopes.poml)

---

## Purpose

This directory is the **canonical permanent home** for the two Compose JPS input-scope schemas locked by Spike #4. Downstream tasks (Phase 2 BFF endpoint, Phase 4 TypeScript scope-builder, Phase 5 dispatch wiring) consume from here, NOT from `notes/spikes/spike-4-prototype/scopes/` (which is throwaway prototype per Spike #4 §13).

## Artifact inventory

| File | Schema | Scope code | Total fields | Required | Char cap | User-content? |
|---|---|---|---|---|---|---|
| [`compose-document.scope.json`](./compose-document.scope.json) | `jps-scope/v1` | `compose-document` | 8 | 3 (`documentSpeId`, `sessionId`, `tenantId`) | 1,500 | false (identifiers only) |
| [`compose-selection.scope.json`](./compose-selection.scope.json) | `jps-scope/v1` | `compose-selection` | 9 | 6 (`selectionText`, `selectionAnchorStart`, `selectionAnchorEnd`, `documentSpeId`, `sessionId`, `tenantId`) | 16,500 | TRUE (`selectionText` → `doNotLog` per ADR-015) |

## Schema version

Both files use `$schema: https://spaarke.com/schemas/jps-scope/v1` — this is a NEW schema family introduced by this project. No prior `jps-scope/v*` schema exists in the repo or in Dataverse. The schema URL is currently aspirational (no JSON-Schema endpoint published yet); the contract is enforced by the BFF endpoint code (Phase 2 task 020/021) + TypeScript types (Phase 4) that consume it.

## Why these scopes are NOT in `.claude/catalogs/scope-model-index.json`

The existing `scope-model-index.json` indexes Dataverse `sprk_analysisaction/skill/knowledge/tool` rows. **Input scopes are a different artifact class** — they describe the SHAPE of data flowing INTO a playbook from the BFF dispatch endpoint, not the AI catalog that playbooks reference. Per Spike #4 §4.3:

> **`jps-validate` targets action JPS files (prompt schemas).** Scope files are a different artifact class — they're consumed by the JPS catalog/registration tooling (`jps-scope-refresh` skill — invoked by task 012 in Phase 1). Phase 1 task 012 is the live-validation surface for scopes; this spike locks the SHAPE that task 012 will register.

Task 012 EXECUTION DISCOVERY (2026-06-29): `jps-scope-refresh` queries Dataverse `sprk_analysis*` entities and regenerates the action/skill/knowledge/tool catalog. It does NOT consume `$schema: jps-scope/v1` files. Therefore, the "registration" surface for input scopes is **code consumption**, not a Dataverse row + catalog refresh:

1. **Phase 2 task 020** authors `ConsumerTypes.ComposeSummarize` constant + the `ComposeActionRequest` DTO that materializes a `compose-document` payload.
2. **Phase 4 task 040+** authors the TypeScript builder that the SpaarkeAi/Compose UI uses to construct payloads matching these scopes.
3. **Phase 5 task 025** wires `ComposeEndpoints.MapPost("/action/{consumerType}")` which takes the scope payload as the request body and forwards via `IConsumerRoutingService` + `IInvokePlaybookAi`.

The locked JSON files in THIS directory are the **single source of truth** consumed by tasks 020, 040+, and 025. Code changes to those tasks MUST match the field names + types + required/optional flags here. Any drift = file an issue against this directory + the consuming code.

## Validation status (Phase 1 task 012)

Both scope JSONs PASS the scope-specific checks from Spike #4 §4.3 (adapted from `jps-validate` Step 5 "Scope Reference Validation" + ADR-015 dataGovernance check):

| Check | compose-document | compose-selection |
|---|---|---|
| Valid JSON | ✅ | ✅ |
| `$schema = jps-scope/v1` | ✅ | ✅ |
| `$version = 1` | ✅ | ✅ |
| `scopeCode` matches design name | ✅ | ✅ |
| `description` non-empty | ✅ | ✅ |
| `metadata.tags[]` present | ✅ `[compose, document, input-scope, r1]` | ✅ `[compose, selection, input-scope, r1]` |
| `inputs.fields[]` complete (name + type + description per field) | ✅ 8 fields | ✅ 9 fields |
| Required vs optional clearly demarcated | ✅ 3 req / 5 opt | ✅ 6 req / 3 opt |
| ADR-015 `dataGovernance` block declares user-content + `doNotLog` | ✅ `containsUserContent: false`; `doNotLog: []` | ✅ `containsUserContent: true`; `doNotLog: [selectionText]` |
| `totalCharCap` under chat-text caps | ✅ 1,500 | ✅ 16,500 (per `selectionText` maxLength=16K + ~500 identifiers) |
| ADR-013 PublicContracts boundary preserved (no AI internals in scope) | ✅ identifiers + selectionText only | ✅ identifiers + selectionText only |

**Summary**: 22 PASS · 0 WARN · 0 FAIL. Both schemas ready for downstream consumption.

## Consumer registry (forward links)

| Consumer task | Phase | Scope consumed | What it builds |
|---|---|---|---|
| `020-bff-add-consumer-type-constant` | Phase 2 | `compose-document` (R1) | `ConsumerTypes.ComposeSummarize` constant + `ComposeActionRequest` C# DTO |
| `025-bff-add-compose-action-endpoint` | Phase 5 | `compose-document` | `ComposeEndpoints.MapPost("/action/{consumerType}")` |
| `040+` (frontend tasks) | Phase 4 | `compose-document` (R1), `compose-selection` (R2-prep) | TypeScript scope-builder + Compose UI dispatch |
| R2 actions (not in R1) | Future | `compose-selection` | Explain clause, Replace with standard, Compare-to-playbook, Draft alternative |

## ADR alignment

- **ADR-013 (refined 2026-05-20)** — Scopes flow through `Services/Ai/PublicContracts/` facade only. The `ComposeActionRequest` DTO that maps to `compose-document` MUST be a public-contract type (no `IOpenAiClient`/`IPlaybookService`/`IPlaybookOrchestrationService` references).
- **ADR-015 Tier 3 multi-tenant isolation** — `selectionText` flagged `doNotLog`; identifiers (`documentSpeId`, `sessionId`, `tenantId`, etc.) loggable for audit/correlation.

## Reference path mapping

Spike #4 §13 lists prototype files at `notes/spikes/spike-4-prototype/scopes/`. Those copies remain for spike traceability but are **NOT** the canonical source. The canonical source is THIS directory (`notes/jps-scopes/`). Phase 2/4/5 task POMLs reference `projects/spaarkeai-compose-r1/notes/jps-scopes/{compose-document,compose-selection}.scope.json`.

---

*Locked 2026-06-29 by task 012. Reopen only if Phase 2 BFF endpoint contract surfaces a field gap not anticipated here.*
