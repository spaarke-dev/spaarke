# Task 063 — protecting the forcing functions from the gate that would delete them

> Implemented 2026-08-21. FR-F0. **Carries an OPEN ADR-038 amendment proposal (CLAUDE.md §6.5 path B).**

---

## 1. Step 1 said "confirm the risk — do not assume it". It is real, and wider than the task expected

`/test-diet`'s classifier, heuristic 1, verbatim before this task:

> *"if file is NOT under `tests/integration/{auth,regression,data-mutation,tenant,contract}/**` OR
> `tests/unit/domain/**`, flag as path-violation; recommend `git mv` to canonical path **OR delete if no
> canonical path applies**."*

`tests/Spaarke.ArchTests/**` is not in that list, and there is no canonical path among the seven for a
structural fitness function. So the recommendation is **delete** — for `CredentialGuardTests`,
`CredentialCensusTests`, and equally for the pre-existing `LayerDependencyTests` and `ADR010_DITests`,
which have carried the same exposure all along.

**And the same read found a second, larger problem the task did not anticipate.** That heuristic
enumerates **six** paths. `tests/integration/seam/**` has been a KEEP path since 2026-07-09 (ADR-038 §2,
added by `spaarke-ai-architecture-redesign-r2` E-40) and `tests/CLAUDE.md` lists it among the seven — but
the skill was never updated. Every vertical-slice-seam test in the repository was a delete candidate
whenever `/test-diet` ran, **including all four of this project's auth seam files**. The gate meant to
protect them would have removed them.

## 2. The finding that reframes this from "we want an exception" to "the ADR contradicts itself"

ADR-038 line 489, in its own Consequences section:

> *"**Some discovery loss.** Wiring tests sometimes accidentally caught contract changes (e.g., a
> DI-registration test would fail if a service was renamed). Replacement: **NetArchTest-style
> architecture tests at Tier 1** plus endpoint-contract category at Tier 2/3."*

ADR-038 **prescribes** architecture tests as the sanctioned replacement for the discovery it gives up by
banning wiring tests (B1–B5) — and its deletion classifier would delete them. This is not a policy
disagreement to be argued; it is an internal inconsistency, and naming the category **restores**
consistency rather than weakening anything.

That also settles the classification question honestly. Fitness functions are not a *carve-out* from the
build-vs-maintain classifier — they are a category it was not written for. Two of its heuristics are
miscalibrated for them by construction:

- **B13 (naming)** — a fitness function's name states the **invariant** (`NoSecretBearingConfidentialClientOutsideTheAllowlist`),
  not a `{Method}_{Scenario}_{ExpectedResult}` triple. There is no "method under test"; the rule is the subject.
- **B15 (setup-to-assertion ratio)** — the arrange section is a source scan over the whole server tree.
  A high ratio is inherent, not a smell.

Applying behavioural heuristics to structural tests produces confident nonsense.

## 3. What was changed, and what deliberately was not

| File | Change |
|---|---|
| `.claude/skills/test-diet/SKILL.md` | **(a)** New heuristic **0** — files under `tests/Spaarke.ArchTests/**` classify FITNESS FUNCTION → KEEP and stop, with the ADR-489 reasoning inline. **(b)** **Drift correction**: heuristic 1's path list gains `seam` (6 → 7), and the MAINTAIN row stops saying "6 KEEP paths" |
| `tests/CLAUDE.md` | New "Structural fitness functions" section: the category, why it is not an exception, the two heuristics that must not apply, and **four authoring rules that replace them** — negative control, positive control, written reasons, in-file maintenance procedure |
| `docs/adr/ADR-038-testing-strategy.md` | **NOT CHANGED.** See §4 |

The seven existing KEEP paths and all seventeen bans are **untouched**. Nothing was weakened; one category
was named and one stale list was corrected.

The authoring rules matter as much as the protection. A deletion-protected path with no standards is how a
protected path fills with junk, so the section trades the two inapplicable heuristics for four stricter
ones — and they are not aspirational: the positive-control rule is there because it caught **two real
defects** in task 060's own detector, both of which would have flagged the sanctioned code.

## 4. 🔔 ADR Conflict — Resolution Required (CLAUDE.md §6.5)

The escalation trigger on this task is explicit: *"amending the KEEP-path list requires an ADR-038
amendment rather than a directive edit — escalate as a §6.5 path B rather than editing the ADR
unilaterally."* It fires. `tests/CLAUDE.md` states the seven paths are *"canonical at runtime per
ADR-038"*, so the directive cannot add an eighth on its own authority.

- **ADR in question**: ADR-038 — Testing Strategy
- **Specific rule**: §2, *"Encode 7 KEEP path categories as MUST rules (deletion requires same-PR
  replacement)"* — an enumerated list that omits `tests/Spaarke.ArchTests/**`
- **Conflict**: ADR-038's Consequences section names *"NetArchTest-style architecture tests at Tier 1"* as
  the sanctioned replacement for the discovery lost to bans B1–B5, while its KEEP-path list leaves those
  same tests unprotected — so `/test-diet`, which the ADR mandates at every project close, recommends
  deleting the mechanism the ADR prescribes. For this project that is not incidental: **success criterion
  12** ("a deliberate ninth secret-bearing confidential client must fail the build") is invalidated the
  moment `CredentialGuardTests` is deleted.
- **Proposed path**: **B — ADR amendment.** Add an eighth category, `tests/Spaarke.ArchTests/**`
  (*structural fitness functions*), with the note that B13 and B15 do not apply to it and the four
  authoring rules that replace them.
- **Rationale**: the gap is **general, not project-scoped** — `LayerDependencyTests` and `ADR010_DITests`
  have had this exposure since before auth-v4 existed. A project-scoped exception (path A) would protect
  this project's two files and leave the pre-existing ones exposed, which is the wrong shape for a defect
  in the classifier itself.
- **Impact if accepted**: one new row in ADR-038 §2 plus a short note that two of the seventeen bans do not
  apply to that row. No existing path or ban changes.
- **Alternative considered and rejected**: **path A**, a project-scoped carve-out — rejected as above, and
  the POML's own constraint prefers the general fix. **Path C**, comply by moving the ArchTests under an
  existing KEEP path — rejected because none of the seven describes a structural fitness function, and
  filing them under, say, `tests/integration/contract/**` would misclassify what they assert and put them
  back under B13/B15.

**Pending that decision**, the directive and skill changes above keep `/test-diet` from acting on the
contradiction. They are deliberately marked *ratification open* in both files rather than presented as
settled.

## 5. Verification

| Criterion | Evidence |
|---|---|
| `/test-diet` classifies the forcing functions as MAINTAIN, not scaffolding | Heuristic **0** returns FITNESS FUNCTION → KEEP before any deletion heuristic runs. Verified by reading the classifier against the actual file paths, **not** by a full skill run — stated plainly rather than claimed |
| The rationale is recorded, not left to reviewer memory | This document, plus the reasoning inline in both changed files, where the person who trips the rule will actually read it |
| **Negative**: the 7 KEEP paths and 17 bans are unchanged | No path removed, no ban removed or softened. One category added; one stale enumeration corrected to match its own canonical source |
| **Negative**: if the risk proved not to exist, record it and change nothing | The risk exists and is documented at §1, with the verbatim heuristic text that creates it |

## 6. Booked onward

- **Owner** — the §4 path-B decision. Nothing in this project is blocked on it; the protection is already
  effective through the directive and skill.
- **090** — when `/test-diet` runs at wrap-up, expect `CredentialGuardTests`, `CredentialCensusTests` and
  `SourceScan.cs` to come back **KEEP**. If any is a delete candidate, heuristic 0 did not fire and the
  report must not be accepted. `SourceScan.cs` in particular has no `[Fact]` at all, so it can only be
  reached as a whole-file deletion — worth checking explicitly.
- **Repo-wide** — the `seam` drift correction protects every project, not just this one. Worth mentioning
  in the wrap-up PR so other worktrees know their seam tests were exposed.
