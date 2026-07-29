# CLAUDE.md — Spaarke Compose Legal Fidelity R4.5 (project context)

> Loads when working in this project. Extends root `CLAUDE.md`; does not override it.

## What this project is

Finishes R4's **read + reference** promise for legal-grade fidelity. Extends the R4 server projection (`ComposeDocxProjectionBuilder` + `ComposeDocxProjection`) and rewires the client mount so **one reader** serves every entry path. Read/reference only — **no byte-authoring changes** (the R4 two-author split stands).

Source of truth: [`spec.md`](spec.md). Rationale + evidence: [`design.md`](design.md). Execution: [`plan.md`](plan.md) + [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md).

## 🚨 MANDATORY: Task Execution Protocol

When working a task here, **invoke the `task-execute` skill** — do NOT read POML files and implement manually. It loads ADRs/constraints/patterns, tracks `current-task.md`, checkpoints every 3 steps, and runs the Step 9.5 quality gates. See root `CLAUDE.md` §4.

Trigger phrases: "continue" / "next task" → read `tasks/TASK-INDEX.md`, find first 🔲, invoke `task-execute`. "work on task NNN" → invoke `task-execute` with that POML.

## The 5 invariants (F-1…F-5) — every task inherits

1. **F-1 Text exactness** — run text emitted verbatim, character-for-character; the only transform is lossless HTML encoding (`&`/`<`/`>`). Any unrepresentable construct is **surfaced as a warning**, never silently dropped.
2. **F-2 One reader** — exactly one docx→editor reader (the server projection); every entry path renders through it; the client `mammoth` fallback is deleted.
3. **F-3 Deterministic numbering** — displayed clause/section/heading/list numbers are computed server-side from the OOXML numbering model, identical to Word; never the browser `<ol>` auto-count for a legal number.
4. **F-4 Stable reference** — every paragraph carries `paraId` **and** its computed legal number + level, persisted so citations survive edits.
5. **F-5 Honest layout numbering** — page/line numbers are rendering artifacts; delivered only via an explicit pagination engine where in scope, never fabricated from OOXML.

## Locked decisions (owner clarifications) — do not re-litigate

- **WS-3 render** = explicit **non-editable number-atom** (not browser `<ol>` auto-count). The number is a fixed computed artifact of the source and must **not** silently re-flow. Edit-triggered renumber (delete a clause → renumber, reflected in redline) is **R5 G3** — R4.5 guarantees **read-time correctness only**.
- **WS-4 store** = persist the `paraId → number` map **both** in the projection payload **and** the session ledger.
- **WS-5** = **spike + decision only**; pagination implementation is a possible fast-follow, not committed in R4.5.
- **Citation depth** = single labels + **sub-item depth ("4.2(b)(iii)")** + **contiguous ranges ("Sections 4–7")**.

## BFF Hygiene (root §10) — this project is BFF=Y

Every BFF-touching task MUST:
- State the **Placement Justification** in the PR (cite `.claude/constraints/bff-extensions.md`). All work stays inside `Services/Compose/`.
- Keep `Services/Compose/` pure — no `IOpenAiClient`/executor/routing type (ADR-013 Tier-1 NetArchTest); no `Microsoft.Graph` above `SpeFileStore` (ADR-007); `byte[]`-in/projection-out.
- **Verify publish size** ≤60 MB compressed; report absolute + delta vs ~49.63 MB baseline (incl. PDBs). WS-1..WS-4 expect **~0 MB delta** (pure OOXML on existing `DocumentFormat.OpenXml`). **WS-5's pagination engine is a separate-process sidecar — MUST NOT be added to the BFF publish.**
- Add/update **seam slices** for every read/projection change (ADR-038; NO `Mock<HttpMessageHandler>`, DI-registration, or ctor-null tests).
- Run **`/conflict-check` before every BFF PR** — `Services/Compose/` overlaps `spaarkeai-compose-r1/r2/r3/r4` + `spaarke-ai-architecture-redesign-r2`.

## Coordination

- **Consume `Services/Ai/PublicContracts/` seams only — NO fork of `Services/Ai/`** (`spaarke-ai-architecture-redesign-r2` is sole owner). No new AI dispatch endpoint; **engine frozen (ADR-039)**.
- R4 (Shadow Document) merged to master 2026-07-24 (`a58c0b5cc`) — already in this branch. Sibling worktrees `spaarkeai-compose-r1/r2/r3/r4` may still iterate on `Services/Compose/` — coordinate the cutover.
- Watch open PRs **#690** (LFS corpus fixtures — coordinate WS-5 corpus adds) and **#266** (`DocumentFormat.OpenXml` 3.4.1→3.5.1).

## ADR Tensions (root §6.5) — declared at design time

| # | ADR / rule | Path | Resolution |
|---|---|---|---|
| **T-1** | NFR-03 (permissive-only; no commercial/AGPL) | **A** (exception + spike) | LibreOffice (MPL-2.0) as a separate process, or Word-rendering service; licensing analysis in WS-5. No commercial lib linked into BFF. |
| **T-2** | ADR-040 / R4 I-2 (client never authors bytes; browse client-only) | **A** (documented) | Read-only stateless `project` round-trip (bytes → projection, no persist) — server only *reads*; client authors nothing. |
| **T-3** | R4 two-author decision | **C** (comply) | R4.5 is read/reference only; the create/edit split stands. |

## Applicable ADRs (quick reference)

| ADR | Rule |
|---|---|
| ADR-040 | session ledger / browse client-only — WS-1 browse via read-only `project` endpoint |
| ADR-013 | AI facade; no AI internals in `Services/Compose/` — WS-4 exposes via projection contract |
| ADR-007 | no `Microsoft.Graph` above `SpeFileStore`; `byte[]`-in/projection-out |
| ADR-038 | `docs/adr/ADR-038-testing-strategy.md` — seam/fidelity-harness DoD; banned mock/DI/ctor tests |
| ADR-039 | engine frozen; R4.5 adds no AI dispatch |

## Entry points

| Surface | Start here |
|---|---|
| Projection builder (WS-2/3) | `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjectionBuilder.cs` |
| Projection payload (WS-4) | `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjection.cs` |
| Write-side numbering mirror | `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocumentRenderer.cs` |
| Compose endpoints (WS-1) | `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs` |
| Re-anchor layer (WS-4) | `Services/Compose/{ParaIdPreParser,AnnotationReanchorService,DocxAnnotationReader}.cs` |
| Compose client mount (WS-1) | `src/client/shared/Spaarke.Compose.Components/src/widgets/{ComposeEditor,ComposeWorkspace}.tsx` |
| Corpus + tests | `tests/fixtures/compose-corpus/` · `ComposeDocxProjectionBuilderTests.cs` · `tests/integration/seam/` |

## Licensing (NFR-03) — HARD

MIT/permissive only. No commercial (Aspose, GemBox, Syncfusion) or AGPL paginators linked into the BFF. LibreOffice (MPL-2.0/LGPL) permitted only as a **separate process/service** (WS-5).
