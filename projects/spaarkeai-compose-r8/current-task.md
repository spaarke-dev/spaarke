# Current Task State — `spaarkeai-compose-r8`

> **Last Updated**: 2026-08-21 (context-handoff) · **Pushed**: PR #806, 0 unpushed, working tree CLEAN
> **Recovery**: Read "Quick Recovery" first. Everything below is recoverable from files alone.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phases 1–3** | ✅ **COMPLETE.** The architecture gate **PASSED**. |
| **Next task** | **040** — the production merge mechanism (FULL · `opus`/`max` · `parallel-safe: false`) |
| **Alternative** | **051** — Track C anchor supply. Unblocked, **not** gated on 031. Kills the *"wording differs slightly"* banner the owner keeps seeing. |
| **ADR-049 amendment** | ✅ **OWNER-ACCEPTED 2026-08-21** (*"ADR-049 is fine."*). **Nothing is blocked on the owner.** The file write is PRE-AUTHORIZED and PENDING — see "Pending ADR write" below. |
| **Next Action** | Read `tasks/040-merge-mechanism.poml` **and** [`notes/gate-decision.md`](notes/gate-decision.md) §6 — 040's POML is provisional and needs four amendments the prototype identified. |

### ⚠️ Pending ADR write — do this FIRST, before writing any 040 code

The amendment is **accepted**; only the file write remains, and it needs **no further approval**.

| Target | Content |
|---|---|
| `.claude/adr/ADR-049-compose-shadow-document.md` | the **CONCISE** section of the draft |
| `docs/adr/ADR-049-*.md` | the **FULL** section |

Source: [`notes/adr-049-third-amendment-draft.md`](notes/adr-049-third-amendment-draft.md).

**Apply at the start of task 040, not at 045.** Task 031's constraint says "with or before task 045", so
early is permitted — and while the write is outstanding, ADR-049 still tells a reader that *"render-on-save
supersedes surgical byte-patch"*, which is the exact guidance that produced the defect 040 exists to fix.
Implementing 040 against the un-amended ADR means following the wrong rule.

**MAIN SESSION ONLY** (root §3). A sub-agent dispatched to this will fail with "Edit denied" — that is the
write boundary working, not a bug.

### The one thing to understand

**The gate passed at 100%, and that does NOT mean fidelity is solved.** The oracle measures **untouched**
blocks and excludes the edited one by construction. The paragraph the user types in is still rebuilt from a
content model carrying only justification, bold and italic — it still loses font, size, colour, indentation,
spacing, tabs and numbering.

**Task 041 owns that.** Phase 4 is authorized on the explicit condition that 041 is neither optional nor
deferrable. Shipping 040 alone would not fix what the owner is looking at in dev.

### Numbers of record

| | Master today | Prototype | Gate bar |
|---|---:|---:|---|
| Overall preservation (lenient) | 18.08% | **100%** | ≥95% |
| Near tier | 6.67% | **100%** | 100% |
| Strict overall | 12.18% | no regression | ratchet only |

18 documents · 271 comparable blocks · 210 near-tier-relevant. All 18 saves terminate `persisted` with zero
honesty violations.

### Tasks complete

**Phase 0** 001 · 002 — **Phase 1 (Track S)** 010 · 011 · 012 · 013 · 014 · 015 · 016 · 018 · 017 —
**Phase 2** 020 · 021 · 022 · 023 — **Phase 3** 030 · 031 — **Phase 5** 050

**Blocked**: 074 ⛔ (`ComposeShadowPatchEngine` subsumption NOT-CONFIRMED — see gate-decision §5; FR-D01
keeps one waiver rather than deleting 3,000 lines on an inference).

### What 040 must do differently (from the prototype)

1. **Drop the `ComposeBaselineParaIdStamper` promotion** — proved unnecessary; the merge never resolves a paraId.
2. **Thread cloned list items through `ListRenderState`** — a rendered list item after clones may restart at 1.
3. **Consider paraId-corroborated pairing as a FALLBACK after document-order** — never a primary key. Reorder
   currently yields zero merge benefit (degrades to R6, never fails).
4. **Verify carrier provenance end-to-end** (`ResolveSaveBaselineAsync` → renderer). A stale carrier would
   clone the WRONG blocks.
5. **044 must NARROW the accept-flatten warning taxonomy** — text-box/field/content-control warnings must fire
   only for blocks actually re-rendered, or users get warned about losses that no longer occur.

### Where the prototype lives

`ComposeDocumentRenderer.RenderIntoCarrier(…, mergeUnchangedBlocks: false)` — **opt-in, default OFF**,
deliberately NOT wired to production. Measurement:
`tests/integration/seam/Compose/MergePrototypeMeasurementTests.cs`.

### Owner-visible banners in dev

| Banner | Track | Owner | Gated on 031? |
|---|---|---|---|
| "Some formatting was simplified when saving" | **A** | 040–044 | **Yes** |
| "wording differs slightly from this document" | **C** | **051–053** | **No — startable now** |

### Not yet deployed

Task 017's banner fix (`ComposeBannerStack.tsx`) — client-only, committed, ships with the next
`sprk_spaarkeai` deploy. The merge prototype is default-OFF; **nothing from Phase 3 is live.**

### Defects found and fixed beyond task scope this session

1. **`NdaSaveNo422RegressionTests` mocked the etag-less `ReplaceFileContentAsUserAsync`** — a **Track S
   regression** (task 011 moved the save path to the `If-Match` overload; both overloads exist, so the stale
   setup compiled, never matched, and the save reported 404). Invisible because per-task runs were filtered.
2. **`ComposeReadFidelityHarnessSeamTests`' golden model dropped `w:fldSimple`** cached results — a correct
   projection read as a failure. The fix TIGHTENS the assertion.
3. **Oracle artifact A1** — empty `<w:pPr/>` counted as near-tier loss (9 points of headline).
4. **Oracle artifact A2** — a dropped repeated child read as 100% near-tier while losing a footnote reference.
5. **The save-degradation banner** claimed the file was unchanged AFTER it was written (UAT-S-01).

### Traps (all live — several cost real time this session)

- `git checkout <commit> -- <path>` writes the **INDEX**; the safe A/B form is `git show <commit>:<path> > <path>`.
- **Run the FULL test project before closing a task**, never `--filter`. Defect 1 above hid behind a filter for two tasks.
- **Warm up before measuring performance** — the first pass showed a 52 ms mean that was pure JIT noise; warmed medians were 4.7–31 ms.
- **Bash heredocs mangle `
`-style escapes inside quoted Python** — write patch scripts to the scratchpad and run them.
- `w14:paraId` must be **8 hex digits, non-zero, ≤ `0x7FFFFFFF`** — mnemonic prefixes are not valid.
- `refs/stash` lives in the common git dir — 60+ worktrees share one stash list.
- SpaarkeAi is Vite and aliases shared-lib SOURCE — clear `dist/ node_modules/.vite/ .vite/` before every build.
- `/api/documents/{id:guid}` returns **404 for a non-GUID id** — a healthy route can look unregistered.
- Use `pwsh`, not `powershell`, for the deploy hash-verify step.

### Evidence trail

`projects/spaarkeai-compose-r8/notes/` — `track-s-uat.md` · `gate-contract.md` · `control-measurement.md` ·
`merge-prototype-results.md` · `gate-decision.md` · `adr-049-third-amendment-draft.md` ·
`honest-failure-set.md` · `document-size-ceilings.md`

### Gate status at handoff

Full BFF suite **10,780 passed / 0 failed** · NetArchTest **36/36** · publish **43.68 MB** (−1.28 vs baseline)
· `/conflict-check` clean · PR **#806** open and current.
