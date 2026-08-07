# CLAUDE.md — Spaarke Compose R6 (Render-on-Save) — project context

> Loads when working in this project. Extends root `CLAUDE.md`; does not override it.

## What this project is

Re-architects Compose around **render-on-save**: every save **renders a fresh docx from a canonical
document model into a new immutable SPE version** — never a surgical byte-patch of inherited XML. This
eliminates the anchor-reconciliation **422 bug class by construction** (nothing to anchor against on save).
Also adds **PDF intake** (first-class), **Word template part-merge**, a **Documents version-history open
UX** (read-only), and a **round-trip fidelity CI harness** seeded with the NDA.

Source of truth: [`spec.md`](spec.md). Rationale + evidence: [`design.md`](design.md). Execution:
[`plan.md`](plan.md) + [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md).

## 🚨 MANDATORY: Task Execution Protocol

When working a task here, **invoke the `task-execute` skill** — do NOT read POML files and implement
manually. It loads ADRs/constraints/patterns, tracks `current-task.md`, checkpoints every 3 steps, and runs
the Step 9.5 quality gates. See root `CLAUDE.md` §4. Trigger phrases: "continue" / "next task" → read
`tasks/TASK-INDEX.md`, find first 🔲, invoke `task-execute`.

## The core invariant (every task inherits)

**Save renders from the model — never patches inherited bytes.** No surgical anchoring on the save path; no
text-search on the save path (satisfied trivially by rendering). Untouched-subtree byte-identity (ADR-049
I-4) is **intentionally superseded for the save path** by the ADR-049 Path-B amendment (task 001) — version
history is the fidelity safety net. The surgical `ComposeShadowPatchEngine` is retained ONLY for any
transitional clean-apply path the amendment permits. **NEVER delete `docxBridge.ts`.**

## Locked decisions (owner clarifications) — do not re-litigate

- **Scope** = full 7 phases, but **PDF intake only** (export deferred to a fast-follow) and
  **version-history open/view only** (restore & branch-from deferred).
- **Template-merge** = **part-merge built in `Services/Compose`** reusing the `template` entity +
  `ITemplateEngine` for storage/variables. Native Dataverse `documenttemplate` **rejected** (zero code;
  placeholder-merge cannot inject a full authored body). The §3-Q3 investigation is dropped.
- **Corpus** = proceed on NDA + synthetic exemplars; owner worst-offender slots stay open (no gate).
- **PDF-export licensing sign-offs** (AGPL-as-service; Syncfusion "free") = deferred; bind only when the
  export fast-follow is scheduled.

## BFF Hygiene (root §10) — this project is BFF=Y

Every BFF-touching task MUST:
- State the **Placement Justification** in the PR (cite `.claude/constraints/bff-extensions.md`). New work
  stays inside `Services/Compose/` (part-merge) or extends existing `Services/Ai` services (PDF intake).
- Keep `Services/Compose/` pure: no `IOpenAiClient`/executor/routing type (ADR-013 Tier-1 NetArchTest);
  no `Microsoft.Graph` above `SpeFileStore` (ADR-007); `byte[]`-in / projection-out.
- **Verify publish size** ≤60 MB compressed; report absolute + delta vs baseline (~49.63 MB incl. PDBs;
  re-measured in task 003). Azure DI = managed service (no binary weight). **No PDF-export/LibreOffice
  sidecar may be linked into the BFF** (deferred anyway; separate-process only if/when it lands).
- Add/update **seam slices** + fidelity-harness for every read/projection/save change (ADR-038; NO
  `Mock<HttpMessageHandler>`, DI-registration, or ctor-null tests).
- Run **`/conflict-check` before EVERY BFF PR**.

## Coordination (⚠️ most-contested surface in the repo)

`Services/Compose/` is actively touched by **`spaarkeai-compose-r5`** (active on `ComposeService.cs` /
`ComposeWorkspace.tsx`), **`spaarkeai-compose-fidelity-r4.5`**, **`spaarkeai-compose-r1/r2/r3`**,
**`spaarke-ai-architecture-redesign-r2`** (sole owner of `Services/Ai/` — consume `PublicContracts/` seams,
**NO fork**), and **`ai-advanced-capabilities-agreements-r1`** / **`analysis-hub-r1`** (touch
`Services/Compose` + `ComposeWorkspace`).
- **`parallel-safe:false` on ALL Compose tasks.** Serialize Compose-file edits.
- **Engine frozen (ADR-039)** — no new AI dispatch endpoint.
- Deploy **BFF + `sprk_spaarkeai` together**; anti-clobber verify (live artifact is a strict superset).
- R4 (Shadow Document) + R4.5 (read/reference fidelity) already merged to master — this branch is off a
  base that includes them.

## Applicable ADRs (quick reference)

| ADR | Rule |
|---|---|
| **ADR-049** | Compose Shadow Document — **Path-B amendment (task 001)** supersedes I-4 + "no re-derive on save" for the save path |
| ADR-007 | no `Microsoft.Graph` above `SpeFileStore`; `byte[]`-in / projection-out |
| ADR-013 | AI facade; no AI internals in `Services/Compose/`; consume `PublicContracts/` seams, NO fork |
| ADR-039 | engine frozen; adds no AI dispatch |
| ADR-040 | session ledger / persist canonical model + projection payload |
| ADR-029/010 | BFF publish hygiene (≤60 MB) / DI minimalism |
| ADR-038 | seam + fidelity-harness DoD; banned mock/DI/ctor tests |

## Entry points

| Surface | Start here |
|---|---|
| Render-from-model (the pivot) | `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocumentRenderer.cs:102` (`SynthesizeDocument`) |
| Save path | `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs:642` (`SaveAsync`; Authored/Imported branch `:714`) |
| Count-gate (422 root — retire) | `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeBaselineParaIdStamper.cs:113` |
| Canonical model / numbering | `.../Services/Compose/ComposeDocxProjectionBuilder.cs` (`NumberingComputationEngine:1357`) |
| PDF intake | `.../Services/Ai/DocumentIntelligenceService.cs` + `DocumentParserRouter.cs` |
| Template storage | `.../Services/Ai/Delivery/WordTemplateService.cs` + `ITemplateEngine` + Dataverse `template` entity |
| Version-history primitive | `.../Api/ContainerItemEndpoints.cs:48` (admin) + `DriveItemOperations.DownloadFileVersionAsUserAsync:842` (OBO) |
| Documents surface | `src/solutions/AllDocuments/src/App.tsx` |
| Compose client | `src/client/shared/Spaarke.Compose.Components/**` (NEVER delete `docxBridge.ts`) |
| Corpus + harness | `tests/fixtures/compose-corpus/` · `tests/integration/seam/Compose/**` |

## Licensing (deferred PDF export) — HARD when it lands

MIT/permissive only for anything linked into the BFF. No commercial (Aspose/GemBox/Syncfusion — incl. the
free "Community License") or AGPL paginators. LibreOffice (MPL-2.0/LGPL) permitted **only** as a separate
process. Two R4.5 sign-offs (AGPL-as-service; Syncfusion) are human-only (root §9) — bind at the export
fast-follow, not in R6.
