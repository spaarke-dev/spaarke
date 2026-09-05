# ADR routing — every ADR has a home (task 011 / spec FR-06)

> **Date**: 2026-09-04 · Derived mechanically from [task 010's classification](adr-classification-2026-09.md) as corrected by the [accuracy re-verification](adr-accuracy-reverification-2026-09.md).
> **Scope**: routing only. No arch test written — P2b does that, for the narrower FR-07 criterion subset.

---

## Coverage: **50 / 50 routed**

Not "17 of 49 enforced". Every ADR has a mechanism that carries it, and an ADR deliberately left unenforced is **a decision on the record rather than a gap**. That reframing is the whole point of FR-06.

*(50 = 49 concise ADR files + ADR-038, canonical in `docs/adr/` and indexed here. ADR-035 exists in neither tier.)*

| Mechanism | ADRs | Scheduled now | Blocked by FR-08 |
|---|---|---|---|
| **arch test (blocking)** | **21** | 16 | 5 |
| **arch test + nightly review** | **26** | 20 | 6 |
| **nightly review** | **2** | 1 | 1 |
| **deliberately unenforced** | **1** | — | (ADR-023, already stale) |
| **Total** | **50** | **37** | **13** |

The routing rule mapped cleanly for all 50 — no ADR needed a fifth destination, so the escalation trigger did not fire.

### The rule, applied literally

| Classification | → Mechanism |
|---|---|
| enforceable | arch test (blocking) |
| partially-enforceable | arch test + nightly review |
| judgment-only + checkable-by-reading | nightly review |
| judgment-only + aesthetic | deliberately unenforced |

---

## Routed but NOT scheduled — 13 ADRs held by FR-08

Per FR-08, a stale or contested ADR is routed but **not** scheduled for enforcement until task 012 resolves it. Enforcing a rule that may be wrong is worse than not enforcing it.

| ADR | Why held | Resolution path |
|---|---|---|
| **005** 🔴 | Drift — names `sprk_documentassociation`, which exists nowhere | Amend to match the code, or explain the divergence |
| **033** 🔴 | Drift — names `WorkingDocumentHandler`/`WorkingDocumentTools.cs`; code has `WorkingDocumentService` | Amend to match the code |
| **014, 016, 017, 018, 019, 020** 🟠 | Orphaned `Proposed` — never ratified, no gate | **Ratify or withdraw.** ADR-019 is the priority (555 citations, 187 implementing files) |
| **041, 042, 043, 047** 🔵 | `Proposed` pending a **named gate** | Check whether the gate has been reached. **ADR-047's has** — `spine-r1` is 21/22 and its task 090 explicitly does the promotion |
| **023** | Stale — Superseded 2026-03-19 | Already routed to *deliberately unenforced*; consistent |

**Nothing on this list is scheduled for enforcement.** Negative criterion satisfied.

---

## FR-23 nightly-reviewer scope — the extracted input for tasks 053/054

The reviewer's scope is **28 ADRs**: the 26 partially-enforceable (whose non-structural clauses no test can assert) plus the 2 judgment-only + checkable-by-reading.

**Judgment-only + checkable-by-reading (2)** — ADR-025 (Icon Library) · ADR-041 (Judgment/Confirmation, currently ⛔ FR-08).

**Partially-enforceable (26)** — 004, 006, 011, 014, 015, 016, 017, 021, 022, 024, 026, 030, 031, 033, 034, 036, 037, 039, 042, 045, 046, 047, 048, 049, 050, 051.

Of these, **22 are schedulable today**; 6 are held by FR-08.

> **Sizing note for task 052/054.** 28 ADRs is a real scope — enough to justify wiring the reviewer, and small enough that its output stays readable. Had routing sent it only the 2 judgment-only ADRs, wiring a nightly reviewer for one schedulable item would have been hard to justify, and that is worth knowing before task 052's spike rather than after.

---

## A tension worth naming before P2b

**12 of the 15 ADRs that name no checkable artifact are routed to an arch test** (6 blocking, 6 arch-test-plus-review).

That looks contradictory and mostly is not — **"names no artifact" is about verifying the ADR's *accuracy*, not about whether its *rule* is testable.** ADR-002 names nothing yet has a named, working test: "plugins are not an execution runtime" is a structural prohibition a scan decides easily without the ADR naming a single type. Same for ADR-008.

But it does mean P2b will hit ADRs where **the rule is testable and the ADR gives you nothing to anchor the test to** — you must reconstruct the intent from prose. The three that matter: **ADR-039** (858 citations), **ADR-019** (555, also ⛔), **ADR-040** (483).

**The cheap remedy stands** (task 012 candidate, one sentence per ADR): have each name **one** canonical artifact — the file or type that is the decision's home. It converts an unverifiable ADR into a checkable one, and it is what makes routing more than bookkeeping: you cannot attach a mechanism to an ADR that names nothing for the mechanism to hold onto.

---

## Deviations

**1. 50 entries, not 49.** Same cause as task 010's INDEX row count: ADR-038 is a real ADR (Accepted, named test, 1,778 citations) whose concise version lives in its INDEX row rather than a `.claude/adr/` file. Routing it is correct; excluding it to reach a round 49 would drop an enforced ADR from the coverage statement. The acceptance criterion's "49/49" is superseded by "50/50" for the same reason.

**2. The headline is coverage, not enforcement** — per the FR-06 constraint, "n/49 enforced" is deliberately *not* the headline. For the record, since task 010 corrected it: mechanical enforcement stands at **17 of 50** (8 named tests + 9 unnamed guards), not the spec's 7.
