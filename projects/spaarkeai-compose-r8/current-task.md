# Current Task State — `spaarkeai-compose-r8`

> **Last Updated**: 2026-08-21 (task-execute — **the gate prototype PASSED**)
> **Recovery**: Read "Quick Recovery" first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **031 — THE GATE DECISION** + ADR-049 third-amendment draft |
| **Phase** | 3 — Model proof |
| **Rigor / Tier** | FULL · `opus` @ **`max`** · `parallel-safe: false` |
| **Status** | not-started — **startable** (dep 030 ✅) |
| **Next Action** | Read `tasks/031-gate-decision-adr-amendment.poml`. The evidence is [`notes/merge-prototype-results.md`](notes/merge-prototype-results.md); the bar and MISS condition are in [`notes/control-measurement.md`](notes/control-measurement.md). |
| **⚠️ Owner** | 031 writes to `.claude/adr/` — **main session only** (root §3 sub-agent write boundary). An ADR amendment is a **CLAUDE.md §6.5 Path B** and needs owner sign-off. |

### The gate result — no miss condition fired

| | Control (R6) | Prototype | Bar |
|---|---:|---:|---|
| Overall preservation | 18.08% | **100%** | ≥95% |
| Near tier | 6.67% | **100%** | 100% |
| Documents at 100% | 1/18 | **18/18** | — |

All five MISS conditions evaluated **not met**. Neither escalation trigger fired. N=5 round trip: zero
cumulative drift. Cost +2 to +19 ms/save (within NFR-07). No new NuGet.

### ⚠️ The caveat that must travel with the 100%

**The oracle measures UNTOUCHED blocks and excludes the edited one.** The paragraph the user actually typed
in is still rebuilt from a content model carrying only justification, bold and italic — it still loses its
font, size, colour, indentation, spacing, tabs and numbering.

One damaged paragraph instead of forty pages is a colossal improvement. It is **not** "fidelity solved".
**FR-A04 property inheritance (task 041) closes it, and task 030 does not exercise it at all.**

### What 040 must do differently (from 030's results)

1. Thread cloned list items through `ListRenderState` (a rendered list item after clones may restart at 1).
2. Consider paraId-corroborated pairing as a **fallback after** document-order — reorder currently yields
   zero merge benefit (degrades to R6, never fails).
3. Pair with **041** — without property inheritance the edited block is still destroyed.
4. Verify carrier provenance end-to-end (`ResolveSaveBaselineAsync` → renderer).
5. `ComposeBaselineParaIdStamper` promotion proved **unnecessary** — the merge never resolves a paraId.

### Where the prototype lives

`ComposeDocumentRenderer.RenderIntoCarrier(…, mergeUnchangedBlocks: false)` — **opt-in, default OFF**. No
production behaviour changed. Measurement: `tests/integration/seam/Compose/MergePrototypeMeasurementTests.cs`.

### Completed this session

**017** (Track S UAT GO + banner fix) · **020** (oracle) · **021** (R4-breaker corpus) · **022** (near-tier
corpus) · **023** (the control + 2 oracle artifacts fixed + thresholds ratified) · **030** (merge prototype —
**gate cleared**).

### Owner-visible banners

| Banner | Track | Owner | Gated on 031? |
|---|---|---|---|
| "Some formatting was simplified when saving" | **A** | 040–044 | **Yes** |
| "wording differs slightly from this document" | **C** | **051–053** | **No — startable now** |

### Traps (all live)

- `git checkout <commit> -- <path>` writes the **INDEX**; safe A/B form is `git show <commit>:<path> > <path>`.
- **Run the FULL test project before closing a task**, never `--filter`.
- **Warm up before measuring performance** — the first pass showed a 52 ms mean that was pure JIT noise;
  warmed medians were 4.7–31 ms.
- Bash heredocs mangle ``-style escapes inside quoted Python — write patch scripts to the scratchpad.
- `refs/stash` is shared across all 60+ worktrees.
- SpaarkeAi is Vite and aliases shared-lib SOURCE — clear `dist/ node_modules/.vite/ .vite/` before building.
- `/api/documents/{id:guid}` returns 404 for a non-GUID id.
- Use `pwsh`, not `powershell`, for the deploy hash-verify.

### Not yet deployed

Task 017's banner fix (`ComposeBannerStack.tsx`) — client-only, ships with the next `sprk_spaarkeai` deploy.
The merge prototype is default-OFF and deliberately NOT wired to production.
