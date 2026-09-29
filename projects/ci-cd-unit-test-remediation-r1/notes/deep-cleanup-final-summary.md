# Deep cleanup — final summary and honest accounting

> Required by **SC-11**: *"final count is reported in `notes/deep-cleanup-final-summary.md` with honest
> accounting of automated vs judgment-driven removals."*
>
> **2026-08-30.** This closes the Phase 2.5 deletion arc (tasks 082 → 085).

---

## 1. The number, stated plainly

| | BFF unit test attributes (`tests/unit/Sprk.Bff.Api.Tests`) |
|---|---:|
| Project start (spec §framing) | ~6,700 |
| Directional target in SC-11 | ~3,500 |
| **Actual, 2026-08-30** | **7,260** |

**The count went up, and the target was abandoned on evidence.** Anything else would be dishonest
accounting, which is the one thing SC-11 explicitly asks this document not to do.

Two reasons it rose:

1. Other active projects added tests to the same suite throughout the ~2-month window. This project never
   had exclusive ownership of the file set it was measured against.
2. The deletion arc found far less to delete than the inventory predicted — see §2.

Note SC-11 marks the 3,500 figure **"(DIRECTIONAL, non-binding)"**. Task 085's own POML goal was written as
a hard *"final BFF unit test count ≤3,500"* — **stricter than the spec it implements**. The spec governs.

---

## 2. Why the deletion arc stopped: the classifier was wrong far more often than it was right

| Task | Rows scoped for DELETE | Verified genuine | Outcome |
|---|---:|---:|---|
| **083** | 54 (B4 ctor null-guards) | **54** | ✅ executed — deleted |
| **084** | 247 (B10 coverage-fillers) | **1** | ❌ closed without executing |
| **085** | remaining sweep | — | reframed (§3) |

Task 084's per-row verification is the load-bearing evidence. Of 247 rows, **exactly one** was genuine
scaffolding. The other 246 asserted real behavior through forms the classifier could not see — assertions
delegated to helpers, assertions inherited from base classes, expression-bodied methods, chained `.And.`
continuations, and an allow-list of assertion names that could never be complete against a fluent API.

**Six classifier defects were found across rounds 3–5, and every one was an over-call.** Not a single
under-call was ever found. That asymmetry is the finding: the suite is substantially more legitimate than
the inventory implied, and the honest conclusion is that **there was no 3,000-test scaffolding pile to
delete.**

This is consistent with ADR-038 §3 (coverage/counts are observation, never a gate) and with the owner
decision recorded in #852 that *numeric reduction is a signal, not a gate*.

---

## 3. What actually shipped instead

The value of Phase 2.5 landed as **mechanism**, not as a body count:

| Deliverable | State |
|---|---|
| ADR-038 §7 — 17 build-vs-maintain bans with BAD/GOOD C# examples | ✅ (SC-11 asked for ≥12) |
| `/test-diet` skill, wired into `task-execute` Step 11 wrap-up gate | ✅ |
| **5 of 17 bans mechanically enforced** at Tier 1 (B1, B3, B4, B12, B16) | ✅ tasks 083 + 094 |
| Remaining 12 bans documented-unenforceable **with live counts** | ✅ `094-adr038-ban-census.md` |
| `.reliability-registry.json` exit rule (entries leave when a test is fixed) | ✅ #889 |
| Skipped-test quarantine census + triage recommendation | ✅ `quarantine-triage-2026-08-30.md` |

A guard that prevents a category from *returning* is worth more than a one-time sweep, and it does not
depend on a classifier being right.

---

## 4. Residual cleanup — routed, not abandoned

| Residue | Route |
|---|---|
| Scaffolding introduced by future projects | `/test-diet` at each project's `090-wrapup-*` — the sanctioned mechanism |
| B8 (12 reflection call sites, 10 files) | Blocked on a **production refactor**; per-site inventory in the census |
| B6/B5/B9 (no lexical signature) | Permanent `/test-diet` judgment |
| ~116 permanently-skipped tests | `quarantine-triage-2026-08-30.md`; gated on the live-service decision |

---

## 5. Task dispositions

- **083 — ✅ complete.** 54 tests deleted; enabled B4 to arm green.
- **084 — 🚫 closed without executing.** 1 genuine row of 247. Executing it was not worth a PR or a review
  cycle; the single test is covered by the standing `/test-diet` route.
- **085 — ✅ complete as this document.** Its deletion sweep is superseded by 084's evidence; its remaining
  genuine obligation under SC-11 was *this summary with honest accounting*, which is what you are reading.

**SC-11 binding clauses: met.** The directional 3,500 figure: **not met, and correctly so** — the evidence
says the tests it would have deleted are load-bearing.
