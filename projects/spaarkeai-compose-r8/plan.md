# Spaarke Compose R8 — Implementation Plan

> **Project**: `spaarkeai-compose-r8` · **Created**: 2026-08-19 · **Status**: INITIALIZED (execution owner-gated)
> **Source**: [`spec.md`](spec.md) ← [`design.md`](design.md)
> **Branch**: `work/spaarkeai-compose-r8` · **Worktree**: `c:/code_files/spaarke-wt-spaarkeai-compose-r8`

---

## 1. Objective

Make Compose work. Two failures, fixed in order: users **cannot reliably save**, and saves that land
**silently destroy Word formatting**. Plus three riders — AI edits located by prose matching, session files
that die at 24h, and five god classes that make the write path unholdable.

---

## 2. Discovered Resources

### Applicable ADRs

| ADR | Why it applies |
|---|---|
| **ADR-049** Compose Shadow Document | **Governing.** R8 files the third Path-B amendment |
| ADR-007 SpeFileStore facade | Save path writes through `SpeFileStore`; engine stays `byte[]`-in/out |
| ADR-009 Redis-first | Version stamps via `IDistributedCache`; Track B's 24h TTL semantics |
| ADR-010 DI minimalism | ≤15 non-framework registrations — binds Track D decomposition |
| ADR-013 AI architecture | `Services/Compose/` reaches no AI internals; `PublicContracts/` discipline |
| ADR-014 / ADR-015 | Tenant isolation on Track B's new store path |
| ADR-021 / ADR-050 | Capability-gate + fork-confirm UI: `SprkModal`/`ConfirmModal`, semantic tokens |
| ADR-028 Spaarke Auth v2 | `authenticatedFetch` stays the transport; only the error branch changes |
| ADR-029 BFF publish hygiene | ≤60 MB, report vs 44.96 MB baseline |
| ADR-032 Null-object kill-switch | If Track B's store is feature-gated → symmetric registration |
| ADR-038 Testing strategy | Seam-first; gate lives in `tests/integration/seam/**` |
| ADR-039 / ADR-040 | AI engine frozen; Track C anchor is envelope-only |
| **ADR-041** Judgment/confirmation | FR-C05's "apply anyway?" must be assessed as a Gate |
| **ADR-043** AI capability execution spine | **Explicitly names "compose edit"** — Track C must confirm no `ActionKind` impact |

### Applicable Skills

`task-execute` (every task) · `code-review` + `adr-check` (Step 9.5) · `bff-deploy` + `code-page-deploy`
(anti-clobber pair) · `conflict-check` (before every BFF PR) · `test-diet` (project close) ·
`context-handoff` (checkpointing) · `docs-architecture` (write-side companion doc)

### Knowledge / Constraints

[`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) (§A pre-merge checklist,
§F asymmetric registration, §G hot-path) · [`.claude/patterns/testing/god-class-ratchet.md`](../../.claude/patterns/testing/god-class-ratchet.md)
· [`docs/architecture/COMPOSE-READ-REFERENCE-FIDELITY.md`](../../docs/architecture/COMPOSE-READ-REFERENCE-FIDELITY.md)
· [`docs/standards/TEST-ARCHITECTURE.md`](../../docs/standards/TEST-ARCHITECTURE.md) ·
[`docs/adr/ADR-038-testing-strategy.md`](../../docs/adr/ADR-038-testing-strategy.md)

### Canonical implementations to reuse (NOT rebuild)

| Asset | Use |
|---|---|
| `ComposeBaselineParaIdStamper` | Track A — stamp the baseline before comparison (promote off the op-log path) |
| `ComposeFormatChange.PreviousPropertiesXml` | The proven opaque-XML carry + SDK parse gate — generalize |
| `ComposeBlockAtom` + `opaqueAtomNode.ts` | Shipped ProseMirror `atom:true` placeholder semantics — add payload |
| `ResolveSaveBaselineAsync` | The baseline the merge projects from |
| `SpeAdminGraphService` chunked upload | FR-S08 — route Compose to it, do not write a new one |
| `UploadSessionManager` `If-Match` overload | FR-A12 — exists, currently bypassed with `ifMatch: null` |
| `ComposeFidelityGateHarnessTests` + `ComposeCorpusFixtureLocator` | Extend; keep dynamic fixture enumeration |
| R7 `SAVE_DEGRADATION_COPY` + banner stack + `ApiError` | FR-S01/S09 rebuild on these |
| `SessionRestoreService` + R7 re-attach layer | Track B — do not rebuild |
| `AnnotationReanchorService` | KEEP — the ADR-sanctioned return-from-Word fuzzy case |
| `ComposeOrigin` (Authored/Imported) | FR-A08 two document classes |
| Provisioned blob account + MI RBAC | Track B — no new Azure resource |

### External prerequisites

- **PR #690** (Git-LFS corpus fixtures in CI) — **hard dependency**; land before the gate (owner decision 2026-08-19).
- **PR #266** (`DocumentFormat.OpenXml` 3.4.1 → 3.5.1) — sequence deliberately; Track A builds on this library.
- Owner-supplied worst-offender corpus documents.
- Owner review: capability-gate trigger list + residual loss list.

---

## 3. Phase Breakdown

### Phase 0 — Coordination & prerequisites (001–003)
Land the CI-fixture dependency, take the publish-size baseline, and confirm no in-flight collision.
**Gate**: corpus fixtures load in CI; baseline recorded.

### Phase 1 — Track S: Save reliability (010–022) · **P0, SHIPS ALONE**
Close all ten verified failure modes, add the save-outcome contract and telemetry.
**No architecture dependency.** Its own PR and its own dev deploy, ahead of everything else.
**Gate**: a user can save — every failure honest, recoverable, measured.

### Phase 2 — The oracle & corpus (030–035)
Build the preservation + outcome-honesty oracle **first**, so it measures today's loss as the control.
Extend the corpus with the three constructs that broke R4 plus near-tier documents.
**Gate**: oracle runs green on current `master`, reporting today's real loss numbers.

### Phase 3 — Model proof (040–043) · **THE GATE**
Prototype the three-way merge end-to-end against the corpus. Answer the spec's research questions.
**Exit**: 100% near-tier / ≥95% overall preservation + zero hard-fails → proceed. Miss → re-open with owner.
**No Phase-4 task starts until this passes.**

### Phase 4 — Track A: Faithful save (050–060)
Baseline stamping · re-projection oracle · block copy-through · property inheritance · atom payload ·
table/atom identity · capability gate + "Edit a copy" · two document classes · `If-Match` · residual list.
Plus the ADR-049 third amendment (merges with or before this code).
**Gate**: gate green in CI at both comparison levels.

### Phase 5 — Track C: AI edit placement (065–072)
Assess ADR-043/ADR-041 first, then thread the anchor, wire `CitationResolver`, retire the text-search path.
**Gate**: "wording differs slightly" unreachable; no mis-placement regression.

### Phase 6 — Track B: Durable session files (075–080)
Durable bytes · lazy re-index · retention bound to session TTL · authoritative availability · erasure.
**Gate**: a day-60 session recalls from its files.

### Phase 7 — Track D: God-class removal (082–087)
All five Compose files below 2,000 lines; all five waivers deleted. Interleaved with Phases 4–6 where
decomposition unblocks work rather than competing with it.
**Gate**: ArchTests green with the waiver entries removed.

### Phase 8 — Wrap-up (090)
Anti-clobber deploy, `/test-diet`, docs (write-side fidelity companion), lessons-learned, INDEX update.

---

## 4. Critical Path

```
001 (PR #690) → 030 (oracle) → 031 (corpus) → 040 (merge prototype) → 043 (GATE)
              → 050 (stamping) → 052 (copy-through) → 058 (gate green) → 090
```

Track S (010–022) runs **off the critical path entirely** — it ships first and independently.
Tracks B (075–080) and C (065–072) are parallel to Track A after the gate.

---

## 5. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Phase-3 gate fails → merge model wrong | Project re-plans | Gate is cheap and early; oracle built first measures the control. Escalate to owner, do not improvise |
| Capability-gate false positives block real documents | Users blocked — opposite of the mandate | Trigger list owner-reviewed; "Edit a copy" means never a hard wall |
| `paraId` unreliability worse than expected on real corpus | Copy-through degrades to thin render | Stamper + consume-in-order + dup→fallback; measured in Phase 2 as the control |
| BFF merge contention (13+ active worktrees) | Conflicts | `/conflict-check` before EVERY BFF PR; small sequential PRs; `parallel-safe:false` on the Compose spine |
| Track D competes with Tracks S/A for the same files | Rework | Interleave, don't parallelize; decompose as each area is touched |
| PR #690 stalls | Gate untestable in CI | Owner decision: land #690 first |

---

## 6. Coordination

**`parallel-safe: false` on the entire Compose spine** — `Services/Compose/**`, `Api/ComposeEndpoints.cs`,
`ComposeWorkspace.tsx`, `ComposeEditor.tsx`, `ComposeAiToolbar.tsx`, `usePendingRedline.ts`, `docxBridge.ts`.

Active worktrees sharing surface (per [`projects/INDEX.md`](../INDEX.md)): `spaarkeai-compose-r6`
(initialize-only), `spaarkeai-assistant-enhancements-r3/r4` (SpaarkeAi client spine),
`chatendpoints-decomposition-r1` + `speadmin-decomposition-r1` (god-class program — Track D coordinates,
does not overlap files), `code-quality-and-assurance-r3` (owns the ratchet).

**Deploy BFF + `sprk_spaarkeai` together** (NFR-05). Never build from a net8 tree. **NEVER delete `docxBridge.ts`.**

---

## 7. References

[`spec.md`](spec.md) · [`design.md`](design.md) · [`notes/fidelity-architecture-investigation.md`](notes/fidelity-architecture-investigation.md)
· [`notes/durable-session-files.md`](notes/durable-session-files.md) · [`../spaarkeai-compose-r7/notes/uat-issues.md`](../spaarkeai-compose-r7/notes/uat-issues.md)
· [`ADR-049`](../../.claude/adr/ADR-049-compose-shadow-document.md) · [`projects/INDEX.md`](../INDEX.md)
