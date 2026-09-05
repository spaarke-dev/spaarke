# Task 001 — deviations and corrections

> **Task**: 001 Amend ADR-012 — enumerate the shared set, record the promotion questions
> **Date**: 2026-09-04 · **Outcome**: completed, no escalation fired

---

## 1. The POML's own note is wrong: all 15 packages have a `package.json`

Task 001's `<notes>` says:

> Measured 2026-09-04: exactly 15 directories, **including Spaarke.LegalWorkspace (which has no package.json — see task 002)**.

**The count is right; the parenthetical is wrong.** Measured from the filesystem:

```
package.json present in 15 of 15
```

`src/client/shared/Spaarke.LegalWorkspace/package.json` exists and declares `@spaarke/legal-workspace`.

**This matters for task 002.** The census can key on `package.json` uniformly across all 15 — it does **not** need a special case for LegalWorkspace, and a census written to expect 14-of-15 would fail on a correct tree.

**What the note was probably reaching for** is a real distinction, just a different one — **build-orchestrator exclusion**, not package-manifest absence. From `scripts/Build-AllClientComponents.ps1`:

| Package | In build orchestrator? | Why |
|---|---|---|
| `Spaarke.LegalWorkspace` | **Excluded** (since 2026-07-02) | Source-only RE-EXPORT barrel over files that stay under `src/solutions/LegalWorkspace/src/`; `@spaarke/*` peer deps are unresolvable in-package, so a standalone `npm run build` cannot succeed. Type-checked by each consumer's `tsc` pass (SpaarkeAi, LegalWorkspace, WorkspaceLayoutWizard). |
| `Spaarke.DailyBriefing.Components` | **Included** (restored 2026-07-08) | Previously excluded on the same basis; standalone build since restored. |

**Recommendation for task 002**: if the census asserts anything beyond directory presence, "has a `package.json`" is a uniform 15/15 property and a safe assertion. "Builds standalone" is **not** uniform and must not be asserted without the exclusion list — the exclusions are deliberate, documented, and would read as census failures.

## 2. `@spaarke/visuals` has one consumer, not zero

Task 001's `<constraint source="spec">` says:

> `@spaarke/visuals` has **0 current consumers** and MUST be recorded as legitimately anticipatory, not as a candidate for removal.

Measured 2026-09-04 (declared dependencies in non-shared `package.json` files under `src/`): **1** — the VisualHost PCF. The package's own description already said as much (*"Consumed by the VisualHost PCF and future code-page dashboards"*).

**The constraint's substance was honored, its arithmetic corrected.** ADR-012 now records `@spaarke/visuals` as legitimately anticipatory and explicitly not a removal candidate — which is the binding part — but states **one** declared consumer rather than zero. Writing "zero" into an ADR would have put a checkable falsehood into a governance document, which is the class of defect this project exists to remove.

The same paragraph notes `@spaarke/legal-workspace` and `@spaarke/ai-context` also sit at one declared consumer today, so `visuals` is not singular and does not read as an outlier needing justification.

> **Carry to spec**: `spec.md` FR-01's "0 consumers" phrasing for `@spaarke/visuals` is stale. Low priority — it changes no requirement — but P1's baseline report (task 003) should use the measured number.

## 3. In-scope addition: the second place the 2+ rule is stated

ADR-012 stated the 2+ consumer rule **twice** — in the amendment prose and again as a bare row in the *When to Add to Shared Library* table (`| Used by 2+ modules/surfaces | … |`). Amending only the first would have left the two copies disagreeing in force, and the table is the one a reader skims.

Annotated the table row as a trigger-to-evaluate and cross-linked it to the three questions. This is inside the task's stated goal ("the 2+ consumer rule is recorded as a trigger to EVALUATE"), not scope creep — but it touched a section the steps did not name, so it is recorded here.

---

## Verification

| Criterion | Result |
|---|---|
| All 15 directories named, one reason each | ✅ 15/15 named; 15 table rows |
| No "etc." in the sanctioned-set definition | ✅ one occurrence, the sentence *disclaiming* it ("There is deliberately no 'etc.'") |
| Three questions recorded as explicit non-gates | ✅ §"trigger to EVALUATE"; "None of these three is a gate" |
| §6.5 path-B note names rule / conflict / path / rationale | ✅ all four, plus an explicit scope-of-amendment line |
| Negative: nothing deprecated, un-promoted, or marked for removal | ✅ only *negations* of that language appear |
| Negative: 2+ trigger unchanged | ✅ zero occurrences of 3+/three-consumer language |

**Escalation triggers**: neither fired. The filesystem showed exactly 15 directories, and the amendment did not require touching the 2+ trigger to stay coherent.
