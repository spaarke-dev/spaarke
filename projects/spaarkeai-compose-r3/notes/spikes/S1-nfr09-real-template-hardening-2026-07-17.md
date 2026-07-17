# NFR-09 real-firm-template hardening gate — VERDICT: ❌ FAIL (task 022 BLOCKED)

> **Date**: 2026-07-17
> **Task**: 003 (NFR-09 hardening gate) · FULL · opus @ xhigh
> **Harness**: `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/Nfr09RealTemplateHardeningTests.cs` (10 tests, green) — re-runs the S1/S1b Docxodus `WmlComparer` fidelity harness through the REAL production pipeline (`ComposeParagraphSpliceService` task 020 → `ComposeRedlineComparerService` task 021) on genuinely Word-authored templates.
> **Gate rule (NFR-09)**: a FAIL on any real template GATES the E1 delta-save cutover (task 022). **Task 022 MUST NOT proceed** until this is resolved.

---

## TL;DR

The shipped **Docxodus 6.4.0** `WmlComparer` — validated by the pre-spec spike **S1 on 7.1.0 (net10)**, but the net8 BFF ships **6.4.0** — **fails two fidelity invariants on real firm templates** that S1 reported as PASS on 7.1.0:

1. **Strips `w14:paraId`** from every paragraph of its output (replaces it with a leftover internal `pt14:Unid`). → defeats paraId-primary re-anchoring across a save (Approach A).
2. **Drops an unchanged top-level table** on the real Cloud Service Agreement (I made zero table edits). → NFR-07 structural-fidelity violation.

Both defects are in **Docxodus 6.4.0 (task 021's engine)** — the task-020 splice is clean (verified: it preserves all 6 tables and all paraIds). This is exactly the spike-vs-shipped-version gap NFR-09 exists to catch, surfaced **before** the irreversible cutover.

**This requires an owner decision under CLAUDE.md §6.5 (see "Resolution paths").**

---

## Templates (genuinely Word-authored, public CC BY 4.0)

Synthetic/SDK fixtures were explicitly rejected per NFR-09 (the gate "cannot be certified on synthetic fixtures alone"). Two candidate synthetic sources (sample-files.com) were downloaded and **rejected** — they carry **0 `w14:paraId`** (not Word-authored). The two kept templates are real Common Paper standards (see `tests/.../Fixtures/Compose/RealTemplates/README.md`):

| Template | Body paras | `w14:paraId` | Tables (top/nested) | Numbering | Other parts |
|---|---|---|---|---|---|
| **Cloud Service Agreement** (CSA + SLA v1.1) | 345 | 395 (100%, unique) | 6 (3 top + 3 nested) | 9-level (ilvl 0–8) | 3 hdr, 3 ftr, footnotes, styles |
| **Mutual NDA** (v1) | 56 | 71 (unique) | 3 | none | hdr, ftr, footnotes, styles |

Coverage: **nested tables** ✓ and **deep multi-level numbering** ✓ on real Word OOXML. The one uncovered S1b stressor is **cross-reference fields** (`PAGEREF`/`REF`) — neither template uses them; deferred to the browser-verified G-R3 UAT (task 082).

---

## Results

### ✅ What 6.4.0 does correctly on real templates (harness green)

- Runs `WmlComparer` on both real docs **without exception** (incl. whole-paragraph delete + paragraph split — S1b edge cases).
- Emits **minimal** `w:ins`/`w:del` (a tiny fraction of ~345 paragraphs; not a whole-body rewrite) with correct **author attribution**.
- **Format-Change Detection works**: bolding a word in a real paragraph yields `rPr`/`pPrChange`, asserted **not** a full-run del+ins (FR-05 / D4).
- Preserves the **non-table structural parts**: styles, numbering (9-level survives), footnotes, headers, footers.

### ❌ Gate-blocking defects (pinned as characterization tests)

**DEFECT 1 — `w14:paraId` stripped (all paragraphs).**
CSA input: 345 paragraphs, 345 `w14:paraId`, 0 `pt14:Unid`. Comparer output: 345 paragraphs, **0 `w14:paraId`, 345 `pt14:Unid`** (fresh 32-hex GUIDs unrelated to the originals). S1 reported the OPPOSITE on 7.1.0 ("does not drop or regenerate `w14:paraId`"). Under **Approach A** (save the comparer output directly), every save destroys the paraId anchor substrate; on reload, task 010 re-mints fresh ids and `AnnotationReanchorService` is forced onto its **fuzzy** fallback after *every* save — defeating the deterministic paraId-primary anchoring that E2 (tasks 010/011/012) exists to provide.

**DEFECT 2 — unchanged top-level table dropped.**
CSA (and the post-splice edited doc) carry **3 top-level + 3 nested = 6** tables. No table was edited. Comparer output carries **2 top-level + 3 nested = 5** — one unchanged top-level table is **dropped**. This is a hard **NFR-07** structural-fidelity violation (a historical WmlComparer weak spot on complex/adjacent tables, flagged in S1b, now confirmed materializing on a real doc).

Isolation (rules out our code): the task-020 splice output was verified to hold all 6 tables + all paraIds; both defects appear only after `WmlComparer.Compare` (task 021 / Docxodus 6.4.0).

---

## Gate verdict

| Template | No-exception | Minimal ins/del + author | Format-change not del+ins | Structural parts (non-table) | **paraId preserved** | **tables preserved** | Verdict |
|---|---|---|---|---|---|---|---|
| CSA | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ | **FAIL** |
| NDA | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ (no top-level table dropped) | **FAIL** (paraId) |

**NFR-09 GATE: ❌ FAIL. Task 022 (E1 keystone cutover) is BLOCKED.**

---

## Resolution paths (CLAUDE.md §6.5 — owner decision required)

🔔 **ADR / design conflict — resolution required.** Approach A ("save the `WmlComparer` output directly as the dirty-save result", design §4.2, recommended for MVP) is **not fidelity-safe on Docxodus 6.4.0**. Options:

- **Path A/B (RECOMMENDED) — adopt Approach B (graft revisions onto the retained original).** Instead of persisting the comparer output, extract the comparer's synthesized `w:ins`/`w:del` and splice them back into the **retained-original bytes** (which keep their real `w14:paraId` and every table). The design already names Approach B as the byte-fidelity hardening step (§4.2) — these findings **promote it from optional to required**. Sidesteps BOTH defects. Cost: task 022 gains a "map comparer revisions onto the retained original by position" step (real engineering; the hardest part of E1). *Project-scoped design amendment of §4.2 Approach A→B.*
- **Path B-alt — evaluate the Codeuctivity fork** (`Codeuctivity.OpenXmlPowerTools`, net8-compatible; the design's documented fallback). If its `WmlComparer` preserves `w14:paraId` + tables, a dependency swap (redo task 001) is cheaper than Approach B. Needs a 1-day spike (re-run THIS harness against it).
- **Path C (REJECTED) — accept degraded + fuzzy fallback.** Tolerating the paraId loss (fuzzy re-anchor after every save) might be arguable, but the **table drop is a hard NFR-07 violation** — Approach A cannot ship. Reject.

**Recommendation**: spike Path B-alt (Codeuctivity, ~1 day) FIRST — if it's clean, it's the cheapest fix and keeps Approach A. If not, commit to Path A/B (Approach B) and re-scope task 022. Either way, **task 003 re-runs THIS harness to certify the chosen engine/approach before task 022 proceeds.**

---

## Meta (for the procedure/spike team)

This is the **third** spike-vs-shipped-version gap this project (Docxodus net10→net8 6.4.0; TipTap v3→v2 2.27.2; now WmlComparer paraId/table fidelity differs 7.1.0→6.4.0). The S1/S1b spikes validated the recipe on a **newer major (7.1.0/net10) than the codebase can run (net8 → 6.4.0)**, giving a false-green on the single most load-bearing fidelity claim. Reinforces the standing meta-finding: **run spikes against the target project's actual dependency majors.** NFR-09 is the backstop that caught it — working as designed.
