# Phase 5 — Doc-Drift Audit Post Procedure-Doc + Constraint-Doc Update

> **Task**: 084 (`P5.5 — Doc-drift audit after procedure-doc + constraint-doc updates`)
> **Date**: 2026-06-01
> **Scope**: 4 governance-surface files touched by Phase 5 tasks 080 + 081
> **Rigor**: STANDARD (audit-only; minor P1 inline fixes acceptable per POML)
> **Verdict**: **PASS-WITH-RECOMMENDATIONS** (1 minor cross-reference gap, advisory P2; no P1 issues)

---

## 1. Audit Scope

Four governance-surface files were inspected for cross-reference symmetry, stale references, and terminology consistency after Phase 5 tasks 080 (procedure-doc update) + 081 (constraint-doc extension):

| # | File | Section under audit | Touched by |
|---|---|---|---|
| 1 | `CLAUDE.md` | §10 BFF Hygiene — Binding Governance | (pre-existing, not touched in Phase 5) |
| 2 | `.claude/constraints/bff-extensions.md` | § F (Test Update Obligation) + new § F.1 / F.2 / F.3 | Task 081 |
| 3 | `docs/procedures/testing-and-code-quality.md` | new "BFF Test Suite Repair Lessons" cluster (lines 1308–1562, 4 lessons) | Task 080 |
| 4 | `.claude/adr/ADR-030-bff-nullobject-kill-switch.md` | §10 PR review checklist + § "Integration with other ADRs" | (pre-existing from task 011; was the foundational driver for F.1) |

The Track E (task 044) recommendation explicitly called for verification that "task 080's 3 new sections are cross-referenced from CLAUDE.md §10." This audit's primary focus.

---

## 2. Cross-Reference Inventory

### 2.1 Symmetric Pair Matrix

Each row = a directional reference. ✅ = present + resolvable. ❌ = absent or broken. ➖ = N/A (out of scope for this direction).

| # | From | To | Context | Present? |
|---|---|---|---|---|
| R01 | CLAUDE.md §10 bullet 1 | `bff-extensions.md` (root link) | "Load before designing the addition" | ✅ |
| R02 | CLAUDE.md §10 bullet 6 | `bff-extensions.md` § F (anchor `#f-test-update-obligation-binding-per-fr-22--d-05`) | "Test update obligation" anchor | ✅ |
| R03 | CLAUDE.md §10 bullet 6 | `docs/procedures/testing-and-code-quality.md` | "code review checklist" | ✅ |
| R04 | CLAUDE.md §10 (any) | `bff-extensions.md` § F.1 (Tier 1.5 Asymmetric-Registration) | Task 044 recommendation | ❌ (see Finding D-01) |
| R05 | CLAUDE.md §10 (any) | `bff-extensions.md` § F.2 (Fixture-Config-FIRST) | Task 044 recommendation | ❌ (see Finding D-01) |
| R06 | CLAUDE.md §10 (any) | `bff-extensions.md` § F.3 (Empirical-Reproduction-FIRST) | Task 044 recommendation | ❌ (see Finding D-01) |
| R07 | CLAUDE.md §10 (any) | ADR-030 | New canonical pattern from r2 | ❌ (see Finding D-02) |
| R08 | `bff-extensions.md` § F.1 | ADR-030 § 10 PR review checklist | Cited as canonical pattern | ✅ |
| R09 | `bff-extensions.md` § F.1 | `docs/procedures/testing-and-code-quality.md` "Asymmetric-Registration Pre-Commit Check" | Phase 5 procedure-doc codification | ✅ (label says "§18.1" but actual section is at line 1319 under H2 "BFF Test Suite Repair Lessons" cluster — see Finding D-03) |
| R10 | `bff-extensions.md` § F.1 | `phase4-track-e-anti-drift-report-2026-06-01.md` § 2.1 + Appendix A | r2 evidence | ✅ |
| R11 | `bff-extensions.md` § F.1 | `asymmetric-registration-inventory-2026-06-01.md` | Per-service inventory | ✅ |
| R12 | `bff-extensions.md` § F.2 | `docs/procedures/testing-and-code-quality.md` "Fixture-Config-FIRST" | Procedure-doc codification | ✅ (label says "§18.2" — same labeling issue, D-03) |
| R13 | `bff-extensions.md` § F.3 | `docs/procedures/testing-and-code-quality.md` "Empirical-Reproduction-FIRST" | Procedure-doc codification | ✅ (label says "§18.3" — same labeling issue, D-03) |
| R14 | `bff-extensions.md` § F (root § F) | CLAUDE.md §10 | Cross-reference upward | ✅ (`See root [CLAUDE.md](../../CLAUDE.md) §10 for BFF Hygiene binding context`) |
| R15 | `testing-and-code-quality.md` "Asymmetric-Registration" | ADR-030 (full doc) | Canonical 3 patterns | ✅ |
| R16 | `testing-and-code-quality.md` "Asymmetric-Registration" | `bff-extensions.md` § F | Binding rule | ✅ |
| R17 | `testing-and-code-quality.md` "Asymmetric-Registration" | CLAUDE.md §10 bullet 6 | Root governance pointer | ✅ |
| R18 | `testing-and-code-quality.md` "Fixture-Config-FIRST" | Phase 4 Track E report §2.2 | r2 worked examples | ✅ |
| R19 | `testing-and-code-quality.md` "Empirical-Reproduction-FIRST" | D-07, D-09, per-fix-triple-run baselines | Path-b decision records | ✅ |
| R20 | `testing-and-code-quality.md` "TestClock + IGuidProvider" (Lesson 4) | ADR-010 + bff-extensions.md § A/B/F | Allowed-seam justification | ✅ |
| R21 | ADR-030 § 10 PR review checklist | `docs/procedures/testing-and-code-quality.md` | "Phase 5 of source project codifies in…" | ✅ (worded loosely — see Finding D-04) |
| R22 | ADR-030 "Integration with other ADRs" | CLAUDE.md §10 bullet 6 | "This ADR is the canonical mechanism by which §10 bullet 6 is satisfied" | ✅ |
| R23 | ADR-030 "References" | r2 baseline + decisions + task 011 commits | r2 source evidence | ✅ |

**Resolution summary**: 19 of 23 references present and resolvable. 4 cross-reference gaps (D-01 (×3) + D-02) and 2 labeling/consistency notes (D-03, D-04). None are P1 (none break navigation).

---

## 3. Drift Findings

### 3.1 Per-Finding Detail

#### D-01 (P2 — Advisory): CLAUDE.md §10 does not name F.1 / F.2 / F.3 explicitly

**What was checked**: Per Track E (task 044) recommendation: "add check that task 080's 3 new sections are cross-referenced from CLAUDE.md §10."

**Observed state**:
- CLAUDE.md §10 bullet 6 (line 179) references `bff-extensions.md` § F via the `#f-test-update-obligation-binding-per-fr-22--d-05` anchor — which resolves to the parent § F section header.
- The new sub-sections § F.1 / F.2 / F.3 added by task 081 are NOT individually named in CLAUDE.md §10.
- Effect on navigation: reader following §10 bullet 6 → § F anchor → lands at the F-section header → scrolls naturally through F.1/F.2/F.3 sub-sections. Navigation is preserved. The reader DOES reach the sub-sections via the parent anchor.

**Why this is P2 not P1**:
- The Track E recommendation was advisory ("add a check"), not a blocking gap. r2's `design.md` D-13 framed task 081's binding-rule additions as constraint-doc-resident — CLAUDE.md §10 was deliberately kept thin per the §10 last paragraph ("This is **not advisory**. It is a binding workflow rule…") and the longer governance lives in the constraint doc.
- The anchor `#f-test-update-obligation-binding-per-fr-22--d-05` deliberately points to the F-parent, which is the entry-point for the test-update obligation cluster (F + F.1 + F.2 + F.3). Linking to F.1/F.2/F.3 individually from CLAUDE.md §10 would over-extend the top-level doc.

**Severity reasoning**: P2 (advisory — improves discoverability but does not break navigation). r3 carry-forward candidate if the team wants surfacing of F.1/F.2/F.3 names on the top-level CLAUDE.md surface.

**Recommended fix** (if main session opts to apply):

Append to CLAUDE.md §10 (after current line 179, as a new optional bullet 7 or expand bullet 6):

```markdown
7. **Apply the binding sub-protocols** from `bff-extensions.md` when designing new BFF code (codified 2026-06-01 from `sdap.bff.api-test-suite-repair-r2` Phase 5):
   - § F.1 — Asymmetric-Registration Tier 1.5 Anti-Pattern (latent variant of bullet 6's rule; static-scan recipe required for new `*Module.cs` registrations)
   - § F.2 — Fixture-Config-FIRST Inspection Protocol (before declaring a Skip'd test "subsumed by" an upstream fix)
   - § F.3 — Empirical-Reproduction-FIRST Protocol (before applying a ledger entry's recommended fix when >1-line change)
```

#### D-02 (P2 — Advisory): CLAUDE.md §10 does not reference ADR-030

**What was checked**: ADR-030 is the canonical pattern for resolving the asymmetric-registration anti-pattern referenced by CLAUDE.md §10 bullet 6. ADR-030 reverse-references CLAUDE.md §10 bullet 6 in its "Integration with other ADRs" table — but the reference is one-way.

**Observed state**:
- CLAUDE.md §10 bullet 6 says "endpoints that map unconditionally must have unconditional service registration" (cites RB-T028-03/04/05/06).
- ADR-030 is the canonical mechanism by which this rule is satisfied when a service must remain feature-gated (Null-Object pattern).
- CLAUDE.md §10 does not mention ADR-030 or link to it.

**Why this is P2 not P1**:
- CLAUDE.md §16 (Pointers table line 261) links to the ADR INDEX (`.claude/adr/INDEX.md`) where ADR-030 would be listed — readers can navigate there.
- The full canonical pattern lives in the ADR itself and in the new procedure-doc Asymmetric-Registration section, both reachable via the existing bullet-6 anchor chain.

**Severity reasoning**: P2 (advisory — improves cross-referencing). Same as D-01: r3 carry-forward candidate.

**Recommended fix** (if main session opts to apply): Add to CLAUDE.md §10 bullet 6 sentence: "…(per RB-T028-03/04/05/06, filed 2026-05-31 by `sdap-bff.api-test-suite-repair`; canonical mechanism is **ADR-030 BFF Null-Object Kill-Switch Pattern**)." Plus optionally a line in the §16 pointers table:

```markdown
| **BFF Null-Object kill-switch pattern (asymmetric-registration prevention)** | [`.claude/adr/ADR-030-bff-nullobject-kill-switch.md`](.claude/adr/ADR-030-bff-nullobject-kill-switch.md) |
```

#### D-03 (P3 — Cosmetic): Section-label mismatch ("§18.1/§18.2/§18.3" in constraint doc vs actual numbered cluster)

**What was checked**: The constraint doc § F.1, F.2, F.3 cross-references the procedure-doc target as "§18.1", "§18.2", "§18.3".

**Observed state**:
- `bff-extensions.md` § F.1 line 116: "Phase 5 procedure-doc codification — `docs/procedures/testing-and-code-quality.md` §18.1"
- `bff-extensions.md` § F.2 line 131: "…§18.2"
- `bff-extensions.md` § F.3 line 147: "…§18.3"
- BUT `testing-and-code-quality.md` does NOT use literal "§18.X" section numbers. The procedure doc uses descriptive ## headers (no numbering). The actual sub-sections live at:
  - "Asymmetric-Registration Pre-Commit Check (Lesson #1)" — line 1319 (H3 under H2 "BFF Test Suite Repair Lessons")
  - "Fixture-Config-FIRST Inspection Protocol (Lesson #2)" — line 1388
  - "Empirical-Reproduction-FIRST Protocol (Lesson #3)" — line 1424
  - "Deterministic Test Data: TestClock + IGuidProvider Pattern (FR-13)" — line 1472

**Why this is P3 not P2**:
- A reader following the link `[docs/procedures/testing-and-code-quality.md](../../docs/procedures/testing-and-code-quality.md) §18.1` will land at the document root (no anchor), see the TOC + headers, and find "BFF Test Suite Repair Lessons" → "Asymmetric-Registration Pre-Commit Check" naturally. Navigation is not broken.
- The "§18.1" labels reflect the constraint-doc author's mental numbering (the cluster was the 18th major section topic added in Phase 5 task 080). They are informal navigational hints, not anchor IDs.

**Severity reasoning**: P3 (cosmetic — informal labels in cross-references). Two options:
1. Leave as-is (the labels are informal; the link still navigates).
2. Replace the "§18.X" suffixes with the actual section header names ("Asymmetric-Registration Pre-Commit Check (Lesson #1)" etc.).

**Recommendation**: Option 2 if main session wants symmetric/canonical labeling. Three edits in `.claude/constraints/bff-extensions.md`:

- Line 116: change `§18.1` → `"Asymmetric-Registration Pre-Commit Check (Lesson #1)" sub-section`
- Line 131: change `§18.2` → `"Fixture-Config-FIRST Inspection Protocol (Lesson #2)" sub-section`
- Line 147: change `§18.3` → `"Empirical-Reproduction-FIRST Protocol (Lesson #3)" sub-section`

This is `.claude/` territory — MAIN SESSION ONLY per CLAUDE.md §3.

#### D-04 (P3 — Cosmetic): ADR-030 §10 header references procedure-doc loosely

**What was checked**: ADR-030 line 168 contains the bracketed clause: "(governance enforcement — Phase 5 of source project codifies in `docs/procedures/testing-and-code-quality.md`)".

**Observed state**:
- The header in ADR-030 is now factually accurate (Phase 5 task 080 HAS codified). The clause was phrased forward-looking ("Phase 5… codifies") at ADR-030 authorship time (when task 080 had not yet executed).
- Reading this AFTER task 080 completion, the phrasing reads as if the codification is still future. Minor stylistic drift.

**Severity reasoning**: P3 (cosmetic — verb tense). Optional fix:
- Change "Phase 5 of source project codifies in" → "codified by source project Phase 5 task 080 in".

**Recommendation**: Either skip (verb tense is minor) or apply in main session as a 1-line edit. Not blocking.

### 3.2 Terminology Consistency Audit (Pass)

Scanned all 4 files for the new terminology cluster:
- "Tier 1.5" → ✅ consistently used in F.1 (line 93–117) + procedure-doc Lesson #1 (lines 1323, 1352)
- "LATENT" (capitalized) → ✅ consistent in F.1 + procedure-doc Lesson #1 + ADR-030 line 119
- "Asymmetric-Registration" (hyphenated, title case) → ✅ consistent
- "Null-Object" (hyphenated, title case) → ✅ consistent across ADR-030 and F.1
- "P1/P2/P3" pattern labels (ADR-030 Three Patterns table) → ✅ consistent
- "FeatureDisabledException" + "503 ProblemDetails" + "AsFeatureDisabled503()" → ✅ consistent
- "RB-T028 cluster" naming → ✅ consistent (RB-T028-03/04/05/06 in CLAUDE.md §10 + ADR-030 + bff-extensions.md F.1; RB-T028-02 separately scoped per task 012)
- "Fixture-Config-FIRST" + "Empirical-Reproduction-FIRST" (all caps FIRST) → ✅ consistent across constraint-doc + procedure-doc
- Path-b decision record convention (`D-XX-{ledger-id}-resolution.md`) → ✅ consistent

**No terminology drift found.** This is the strongest signal that tasks 080 + 081 + ADR-030 (task 011) were authored as a coherent cluster.

### 3.3 Structural Consistency Audit (Pass)

- Both the constraint-doc § F.1/F.2/F.3 and the procedure-doc Lessons #1/#2/#3 follow the same internal structure: **Source → Why this lives here → When to apply → Binding rule / Static-scan recipe → Anti-pattern → Cross-references**. ✅ Pattern symmetric.
- ADR-030's §10 PR review checklist mirrors the procedure-doc Lesson #1's 4-step static-scan recipe. ✅
- All three documents use H3 sub-section headers under H2 parents. ✅

### 3.4 Link Resolution Spot-Checks (Pass)

Spot-checked relative path targets:
- ✅ `../../projects/sdap.bff.api-test-suite-repair-r2/baseline/phase4-track-e-anti-drift-report-2026-06-01.md` — exists at `projects/sdap.bff.api-test-suite-repair-r2/baseline/phase4-track-e-anti-drift-report-2026-06-01.md`
- ✅ `../../projects/sdap.bff.api-test-suite-repair-r2/baseline/asymmetric-registration-inventory-2026-06-01.md` — exists
- ✅ `../../projects/sdap.bff.api-test-suite-repair-r2/baseline/phase4-track-c-testclock-poc-2026-06-01.md` — exists
- ✅ `../../projects/sdap.bff.api-test-suite-repair-r2/decisions/D-07-insights-layer2-resolution.md` — exists
- ✅ `../../projects/sdap.bff.api-test-suite-repair-r2/decisions/D-09-nullobject-design.md` — exists
- ✅ `../../projects/sdap.bff.api-test-suite-repair-r2/baseline/per-fix-triple-run-rb-t028-02-2026-06-01.md` — exists
- ✅ `../../.claude/adr/ADR-030-bff-nullobject-kill-switch.md` (from procedure doc) — exists
- ✅ `../../docs/procedures/testing-and-code-quality.md` (from bff-extensions.md and ADR-030) — exists
- ✅ Anchor `#f-test-update-obligation-binding-per-fr-22--d-05` in CLAUDE.md →`bff-extensions.md` — resolves to "### F. Test Update Obligation (Binding per FR-22 / D-05)" at line 73 (GitHub markdown slug normalization confirmed).

No broken links detected.

---

## 4. Audit Verdict

**PASS-WITH-RECOMMENDATIONS**

| Category | Count | Severity |
|---|---|---|
| P1 (breaks navigation, must fix) | 0 | — |
| P2 (advisory — improves discoverability) | 2 | D-01, D-02 |
| P3 (cosmetic — labeling / verb tense) | 2 | D-03, D-04 |
| Stale references | 0 | — |
| Broken links | 0 | — |
| Terminology drift | 0 | — |
| Structural drift | 0 | — |

**Rationale**: All canonical cross-references (CLAUDE.md ↔ bff-extensions.md ↔ ADR-030 ↔ procedure doc) are present and resolvable. The 4 findings are advisory/cosmetic and do NOT break the navigation surface. The Phase 5 cluster was authored as a coherent unit — terminology, structural patterns, and link conventions are all symmetric across the 4 governance assets.

The Track E recommendation that triggered this audit ("add check that task 080's 3 new sections are cross-referenced from CLAUDE.md §10") is satisfied at the parent-§ F-anchor level. Direct F.1/F.2/F.3 linking from CLAUDE.md §10 is a P2 enhancement, not a P1 gap.

---

## 5. Recommended Fixes (Main Session)

All 4 findings are in `.claude/` territory — MAIN SESSION ONLY per CLAUDE.md §3 sub-agent boundary. None applied by this audit. Main session may apply selectively:

1. **D-01** (P2, CLAUDE.md §10): Add bullet 7 with sub-section name pointers to F.1/F.2/F.3. Estimated 5 lines.
2. **D-02** (P2, CLAUDE.md §10 + §16): Add ADR-030 reference to bullet 6 sentence + pointers table. Estimated 2 line edits.
3. **D-03** (P3, bff-extensions.md F.1/F.2/F.3): Replace "§18.1/§18.2/§18.3" with sub-section header names. 3 edits.
4. **D-04** (P3, ADR-030 line 168): Change verb tense from forward-looking to past. 1 edit.

**Net recommendation**: Apply D-01 (the original Track E recommendation) + D-02 (its natural companion). Defer D-03 + D-04 unless trivially batched. None of the four are required for task 084 to close at PASS.

---

## 6. r3 Carry-Forward (per D-04)

None required at P1 severity. P2 findings (D-01, D-02) are reasonable r3 candidates if the team elects to surface ADR-030 + F.1/F.2/F.3 names directly on the top-level CLAUDE.md surface for discoverability. P3 (D-03, D-04) can be deferred indefinitely.

---

## 7. Acceptance Criteria Trace

| Criterion (POML) | Met? | Evidence |
|---|---|---|
| Audit report exists at expected path | ✅ | This file at `projects/sdap.bff.api-test-suite-repair-r2/baseline/phase5-doc-drift-audit-2026-06-01.md` (per user instruction overriding POML's `audits/` path) |
| Scope statement (which docs audited) | ✅ | §1 — 4 governance-surface files enumerated |
| Findings by priority (P1/P2/P3) | ✅ | §3.1 — D-01, D-02 (P2); D-03, D-04 (P3); zero P1 |
| Fixes applied (P1) and deferred (P2/P3) | ✅ | §5 — 0 P1 applied (none exist); 4 P2/P3 deferred to main session |
| Explicit verdict (PASS / PARTIAL / FAIL) | ✅ | §4 — PASS-WITH-RECOMMENDATIONS |
| No src/ or test logic modifications | ✅ | Read-only audit; no edits applied by this sub-agent |
| P1 fixes inline; no large refactoring | ✅ | No P1 fixes needed |

---

## 8. References

- **Phase 5 task POML**: `projects/sdap.bff.api-test-suite-repair-r2/tasks/084-doc-drift-audit-post-procedure-update.poml`
- **Track E anti-drift report** (origin of Track E recommendation): `projects/sdap.bff.api-test-suite-repair-r2/baseline/phase4-track-e-anti-drift-report-2026-06-01.md`
- **Audited files**:
  - `CLAUDE.md` (§10)
  - `.claude/constraints/bff-extensions.md` (§ F + F.1/F.2/F.3)
  - `docs/procedures/testing-and-code-quality.md` (lines 1308–1562)
  - `.claude/adr/ADR-030-bff-nullobject-kill-switch.md` (§10 + Integration table)
- **Decision record context**: `projects/sdap.bff.api-test-suite-repair-r2/decisions/D-04-r2-pilot-grade-scope.md`, `D-13-task-081-extension-warrant.md`

---

**Auditor**: Phase 5 task 084 sub-agent (STANDARD rigor; audit-only deliverable).
