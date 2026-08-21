# Current Task State — `spaarkeai-compose-r8`

> **Last Updated**: 2026-08-21 (task-execute — 017, 020, 021, 022 all closed)
> **Recovery**: Read "Quick Recovery" first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **023** — Control measurement: run the oracle on master, publish today's real loss numbers |
| **Phase** | 2 — Oracle & Corpus (**critical path**: 023 → 030 → **031 GATE**) |
| **Rigor / Tier** | STANDARD · `opus` @ `high` · `parallel-safe: false` |
| **Status** | not-started — **startable now** (deps 020 ✅, 021 ✅, 022 ✅) |
| **Next Action** | Run `dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj --filter "FullyQualifiedName~ComposeFidelityGateHarnessTests"`, read `fidelity-gate-result.json`, and publish the 18-document control per the 023 POML. Most of the measurement already exists in [`notes/gate-contract.md`](notes/gate-contract.md). |

### Critical context

**Phase 1 (Track S) is CLOSED** — owner UAT GO 2026-08-21. **Phase 2 is 3/4 done.**

The control on current master: **8.86% overall block preservation, 2.37% near-tier**, 18 documents /
271 comparable blocks. Every save reports `persisted` and none of them lies — Track S's outcome contract
holds corpus-wide. The loss is **inside blocks, not structural** (block counts stable: 109→109, 50→50), which
is exactly the shape "clone the untouched blocks verbatim" is built for.

### What the owner still sees in dev, and who owns it

| Banner | Track | Owner |
|---|---|---|
| "Some formatting was simplified when saving" | **A** | Phases 2→3→4 (040–044), gated on 031 |
| "wording differs slightly from this document" | **C** | **051–053 — startable NOW**, not gated on 031 |

Owner standing directive: *"we NEVER should get the 'wording differs slightly'."* Track C is the shortest
path to removing the second banner and does not wait for the Phase-3 gate.

### Completed this session

| Task | Outcome |
|---|---|
| **017** | Track S UAT **GO**. Fixed the save-degradation banner that claimed *"the original file is unchanged until you save"* **after** the bytes were written. |
| **020** | The preservation oracle + outcome-honesty + two comparison levels. Ten `Oracle_*` facts prove the instrument. |
| **021** | Three R4-breaker fixtures (dup paraId in `mc:AlternateContent`, interior text boxes, multi-part paraId collision). |
| **022** | Five near-tier fixtures (char formatting, court spacing, footnotes, `REF`, content controls). |

### Defects found and fixed beyond task scope

1. **`NdaSaveNo422RegressionTests` mocked the etag-less `ReplaceFileContentAsUserAsync`** — task 011 moved the
   save path to the `If-Match` overload; both still exist, so the stale setup compiled, never matched, and the
   save reported 404. **A Track S regression**, invisible because per-task runs were filtered to `Seam.Compose`.
2. **`ComposeReadFidelityHarnessSeamTests`' golden model dropped `w:fldSimple`** — the cached result never
   entered the golden, so a correct projection read as a failure. The fix *tightens* the assertion.
3. **`w14:paraId` must be 8 hex digits ≤ `0x7FFFFFFF`** — my first fixture draft used non-hex mnemonics.

### Traps (carried forward — all still live)

- `git checkout <commit> -- <path>` writes the **INDEX**; a follow-up `git checkout -- <path>` restores the OLD
  version and silently reverts work. Safe A/B form: **`git show <commit>:<path> > <path>`**.
- **Run the FULL test project before closing a task**, not `--filter`. Defect 1 above hid behind a filter for
  two tasks.
- `refs/stash` lives in the **common git dir** — 60+ worktrees share one stash list.
- SpaarkeAi is Vite and aliases shared-lib SOURCE — clear `dist/ node_modules/.vite/ .vite/` before every build.
- `/api/documents/{id:guid}` returns **404 for a non-GUID id** — a healthy route can look unregistered.
- Use `pwsh`, not `powershell`, for the deploy hash-verify step.
- Bash heredocs in this harness mangle ``-style escapes inside quoted Python — write patch scripts to a file
  in the scratchpad and run them instead.

### Not yet deployed

The task 017 banner fix (`ComposeBannerStack.tsx`) is **committed but not deployed** — client-only, ships with
the next `sprk_spaarkeai` deploy. No BFF change is pending.
