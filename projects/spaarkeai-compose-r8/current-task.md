# Current Task State — `spaarkeai-compose-r8`

> **Last Updated**: 2026-08-21 (task-execute — **Phase 2 COMPLETE**)
> **Recovery**: Read "Quick Recovery" first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **030** — Merge prototype + measurement: stamp → re-project → per-block compare → clone unchanged |
| **Phase** | 3 — Model proof · **THE GATE** |
| **Rigor / Tier** | FULL · `opus` @ **`max`** · `parallel-safe: false` |
| **Status** | not-started — **startable now** (dep 023 ✅) |
| **Next Action** | Read `tasks/030-merge-prototype-measurement.poml`. It answers spec §5.3 and includes FR-G06 (heavy restructure) + FR-G07 (N-cycle Word round trip, no compounding drift). |
| **Then** | **031 — GATE DECISION.** A miss is an **owner escalation**, not an improvisation (root §6/§6.5). |

### The control 030 must beat

| | Master today | Gate requires |
|---|---:|---|
| Near tier (lenient) | **6.67%** | **100%**, on every single document |
| Overall (lenient) | **18.08%** | **≥95%** corpus-wide |
| Overall (strict) | **12.18%** | no-regression ratchet — **not** a gate |

18 documents · 271 comparable blocks · 210 near-tier-relevant. Full analysis, per-loss classification and the
**MISS condition** (defined in advance): [`notes/control-measurement.md`](notes/control-measurement.md).

**100% near-tier is reachable by construction** — a verbatim block clone is identical to its original by
definition. If the merge model is right the number is 100%; if it is below 100%, a block that should have
been cloned was not. The bar is a binary correctness check on the mechanism, not a quality percentage.

### Critical context

- **Phase 1 (Track S) CLOSED** — owner UAT GO. **Phase 2 COMPLETE** — oracle, corpus, control all published.
- **The loss is INSIDE blocks, not structural.** Block counts are stable on every document (109→109, 50→50).
  The merge model has no re-alignment problem to solve.
- **The content model carries justification, bold and italic and essentially nothing else.** An *edited*
  block is still rebuilt from it, so FR-A04 property inheritance (task 041) is not optional — it is all that
  stands between an edited paragraph and total formatting loss.

### What the owner still sees in dev

| Banner | Track | Owner | Gated on 031? |
|---|---|---|---|
| "Some formatting was simplified when saving" | **A** | 040–044 | **Yes** |
| "wording differs slightly from this document" | **C** | **051–053** | **No — startable now** |

Owner standing directive: *"we NEVER should get the 'wording differs slightly'."*

### Completed this session

| Task | Outcome |
|---|---|
| **017** | Track S UAT **GO** + fixed the banner that claimed the file was unchanged after it was written |
| **020** | Preservation oracle, outcome honesty, two comparison levels; 12 `Oracle_*` facts prove the instrument |
| **021** | 3 R4-breaker fixtures |
| **022** | 5 near-tier fixtures |
| **023** | **The control** + 2 oracle artifacts found and fixed + thresholds ratified + MISS condition defined |

### Defects found and fixed beyond task scope

1. `NdaSaveNo422RegressionTests` mocked the etag-less `ReplaceFileContentAsUserAsync` — **a Track S
   regression** (task 011 moved to the `If-Match` overload), invisible behind a `--filter`.
2. `ComposeReadFidelityHarnessSeamTests`' golden model dropped `w:fldSimple` cached results.
3. Oracle artifact A1 — empty `<w:pPr/>` counted as near-tier loss (**9 points** of headline).
4. Oracle artifact A2 — a dropped repeated child read as 100% near-tier while losing a footnote reference.

### Findings recorded, NOT fixed (per 023's no-`src/` constraint)

| Finding | Owner task |
|---|---|
| Content model carries only `jc` + `b` + `i` | 040 / 041 |
| Dropped footnote reference orphans its footnote in `footnotes.xml` | 041 |
| Run-property loss emits **no degradation warning at all** | 044 |
| `w14:textId` dropped unconditionally, unwarned | 042 |

### Traps (all still live)

- `git checkout <commit> -- <path>` writes the **INDEX**; the safe A/B form is `git show <commit>:<path> > <path>`.
- **Run the FULL test project before closing a task**, never `--filter`. Defect 1 hid behind a filter for two tasks.
- Bash heredocs mangle ``-style escapes inside quoted Python — write patch scripts to the scratchpad and run them.
- `refs/stash` is shared across all 60+ worktrees.
- SpaarkeAi is Vite and aliases shared-lib SOURCE — clear `dist/ node_modules/.vite/ .vite/` before every build.
- `/api/documents/{id:guid}` returns 404 for a non-GUID id — a healthy route can look unregistered.
- Use `pwsh`, not `powershell`, for the deploy hash-verify.

### Not yet deployed

Task 017's banner fix (`ComposeBannerStack.tsx`) is committed but **not deployed** — client-only, ships with
the next `sprk_spaarkeai` deploy. No BFF change pending.
