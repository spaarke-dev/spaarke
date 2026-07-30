# Task 011 — Client `sprk_analysis` TypeScript type — deviation notes

**Status**: completed · **Rigor**: STANDARD

## File location decision

Created `src/solutions/SpaarkeAi/src/types/sprkAnalysis.ts` — this is the **first `types/` folder
in the SpaarkeAi solution**. Per ADR-012 placement guidance ("if an equivalent record type already
lives in `@spaarke/*`, extend/co-locate; otherwise place at the SpaarkeAi client's conventional type
location"), grep confirmed no existing `@spaarke/*` shared-lib type models `sprk_analysis`. Precedent
for a per-solution `types/` folder (vs. co-locating types inside a component folder) is the sibling
code-page `src/client/code-pages/CommunicationPage/src/types/communication.ts` — same shape
(`ICommunicationRecord`, WebApi-retrieve convention). No barrel/index.ts was added because that
sibling convention also has none — consumers import directly from the file path
(`../../types/sprkAnalysis` or similar), consistent with how `CommunicationPage` consumers import
`communication.ts`.

## Field-shape source

Used `docs/data-model/field-mapping-reference.md` "AI / Analysis Domain > Analysis (`sprk_analysis`)"
(authoritative schema doc, exact Logical Name / Type / Target columns) as the primary source for the
pre-existing "Core" fields the orchestrator named (`sprk_analysisid`, `sprk_name`, `sprk_documentid`,
`sprk_sessionid`, `sprk_analysisstatus`, `sprk_playbook`, `sprk_outputfileid`) — this resolved the
exact Dataverse types (`sprk_playbook` → Lookup → `sprk_analysisplaybook`; `sprk_outputfileid` →
Lookup → `sprk_document`; `sprk_analysisstatus` → Choice 0–6) that weren't in
`notes/schema-prerequisites.md` (which only covers the task-010 NEW columns). Owner-added
columns (`sprk_worktype`, the 3 new regarding lookups, `sprk_description`) came from
`schema-prerequisites.md` "VERIFIED PRESENT" per the orchestrator's ground-truth message.

## Scope boundary (intentionally NOT modeled)

Per the task's SCOPE constraint ("model the owner-created columns exactly as verified in task 010"
plus the named Core fields), the type does **not** include AI-execution-internal columns that also
exist on `sprk_analysis` per `field-mapping-reference.md`: `sprk_actionid`, `sprk_workingdocument`,
`sprk_chathistory` (being retired per project CLAUDE.md), `sprk_finaloutput`, `sprk_errormessage`,
`sprk_inputtokens`/`sprk_outputtokens`, `sprk_startedon`/`sprk_completedon`. These belong to a future
task that wires analysis execution/reopen detail, not this hub/wizard data-spine type. Extend
`ISprkAnalysisRecord` when that need is concrete (CLAUDE.md §11 — no speculative fields).

Also did not model `sprk_regardingrecordurl` (the 5th ADR-024 resolver field used elsewhere, e.g.
`ITodoRecord`) — the orchestrator's ground-truth list for `sprk_analysis` named only 4 resolver
fields (type/id/name/number, no url), and `schema-prerequisites.md` doesn't list it either. Left out
rather than guessed.

## `sprk_worktype` modeling

Modeled as BOTH:
- `SprkAnalysisWorkType` enum (integer-backed: `AgreementAnalysis = 100000000`, etc.) — matches the
  raw Dataverse Choice value on `ISprkAnalysisRecord.sprk_worktype`.
- `AnalysisWorkTypeId` — the closed kebab-id union (`'agreement-analysis' | 'legal-research' |
  'patent-application'`) the acceptance criteria requires, plus bidirectional lookup maps
  (`ANALYSIS_WORK_TYPE_ID_BY_VALUE` / `ANALYSIS_WORK_TYPE_VALUE_BY_ID`).

This satisfies both the literal acceptance criterion (closed string union exists, not `string`) and
the ground-truth instruction to key the enum on the verified integer values.

## Verification

- `npm run typecheck` (SpaarkeAi, `scripts/tsc-surface-gate.mjs`) → 0 surface-owned errors (291
  pre-existing shared-lib errors are unrelated/deferred, per the gate's own Phase-B carve-out).
- `grep -n "any"` on the new file → no matches.
- No `speDriveItemId`/`GraphDriveId`/`GraphItemId` fields present.
