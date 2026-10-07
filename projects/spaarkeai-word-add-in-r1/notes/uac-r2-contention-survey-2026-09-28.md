# Which pending tasks are contended with `unified-access-control-r2`

> **Date**: 2026-09-28
> **Why**: Owner directed that UAC-r2 completes its own work while this project continues on tasks *not*
> implicated by it. The first two tasks I picked off the index (057, 058) both turned out to be entangled, so
> this survey replaces task-by-task discovery.
> **Authoritative input**: `git diff --name-only origin/master...origin/work/unified-access-control-r2` =
> **543 files**.

---

## 🔴 Two measurement errors I made first — both would have misled

**1. `gh pr list --json files` is CAPPED AT 100 FILES.** PR #950 touches **543**. My first check asked gh
whether #950 touched `OfficeEndpoints.cs` / `OfficeService.cs` and got **"none" for all three** of task 058's
files. That was a **false negative from truncation** — the branch diff shows it touches two of them. The POML's
own `<parallel-reason>` ("shared with unified-access-control-r2") was right and my check was wrong.

> **Rule**: for contention checks, diff the **branch** (`origin/master...origin/work/<branch>`), never
> `gh pr --json files`. Verify the cap by comparing `gh pr view N --json files --jq '.files|length'` against
> the branch diff count — if it reads exactly 100, it is truncated.

**2. Intersecting ALL `<file>` elements overstates blockage badly.** A POML lists knowledge/citation files in
the same element shape as edit targets, so my first pass flagged **14 of 16** pending tasks as contended —
mostly because they merely *cite* `docs/adr/ADR-038-testing-strategy.md`. Restricting to `role="edit"`,
`role="new"` and `<outputs>` dropped it to **6 of 16**. Reading the first number as the answer would have
parked this project on false grounds.

---

## Result: 6 contended, 10 free

### 🛑 CONTENDED — do not start without coordination

All six are the same cluster: the two Office files UAC-r2 edits, plus 061's authorization-test governance.

| Task | Writes that UAC-r2 also modifies | Severity |
|---|---|---|
| **058** | `OfficeEndpoints.cs`, `OfficeService.cs` | 🔴 **HARD — delete-vs-modify on ONE method.** UAC-r2's three `OfficeService.cs` hunks (master 1696/1767/1828) all sit **inside `GetRecentDocumentsAsync`** (master 1638–1856), which is the `/office/recent` stub **058 deletes**. They are maintaining its fabricated sample data — retyping `account`→`project` after removing `Account` from `AssociationType`. Whoever merges second adjudicates; a mechanical re-run of 058 after they merge would silently drop an **owner-decided** enum change (ordinal 3 deliberately burned). |
| **059** | `OfficeService.cs` | Extracts the search cluster **out of** the file they edit. Also `deps 058`. |
| **060** | `OfficeService.cs` | Same file. Also `deps 058`. |
| **061** | `EntityAccessFilter.cs`, `OfficeEndpoints.cs`, `RouteAuthorizationGuardTests.cs` | 🔴 Route-authorization-test governance is **squarely UAC-r2's domain** and they modify all three. Strong candidate to hand over rather than coordinate. |
| **068** | `OfficeEndpoints.cs`, `OfficeService.cs`, `UploadFinalizationWorker.cs` | Three-way. Also `deps 060`. |
| **075** | `OfficeEndpoints.cs`, `OfficeService.cs` | Dead-code sweep across both contested files. Also `deps 058, 065`. |

Note the shape: **058 is the keystone.** 059, 060, 068 and 075 all depend on it directly or transitively, so
one contended task gates five.

### ✅ FREE — write nothing UAC-r2 touches

| Task | Deps | Startable now? |
|---|---|---|
| **069** — make the office-addins CI gate BIND | none | ✅ **Yes — highest leverage**: it unblocks 070, 072 and 074, which are also free. `office-addins-tests.yml` is **not** one of the three frozen tier workflows. |
| **076** — FR-13 blank matter primary name | none | ⚠️ Yes, but it **opens with an escalation** — the owner re-scoped numbering out twice, and the POML says confirm scope before implementing. |
| **081** — interim carve-out until 080 lands | none | ⚠️ Yes, but it is an authorization **loosening** whose whole purpose is to bridge until 080 ships. With 080 paused pending UAC-r2, its value went **up** — and so did its lifetime. Needs an owner call on whether a temporary carve-out should now live longer than planned. |
| **077** — FR-16's three gaps | 063 ✅ | ✅ Yes |
| **078** — FR-05 manifest contradiction | 073 ✅ | ✅ Yes |
| **070 / 072 / 074** | 069 | After 069 |
| **079** — record integrity | 076, 077, 078 | After those |
| **090** — wrap-up | 042, 079 | Last |

---

## Recommended order

**069 → (070, 072, 074) → 077 → 078 → 079**, with **076** and **081** taken when the owner answers their
respective escalations, and the contended six deferred until UAC-r2 merges or explicitly hands them over.

That is a real runway — five to eight tasks of genuinely independent work — so this project is **not** blocked
by the sequencing decision.

## Open coordination asks for UAC-r2

1. **058's `GetRecentDocumentsAsync`** — they are maintaining a stub this project deletes. Confirm they do not
   depend on `/office/recent`, then agree who lands first. If they merge first, 058 must be re-based by hand,
   not re-run, to preserve their `AssociationType` decision.
2. **061** — offer it to them outright; it is their surface.
3. **057** — the ADR-038 amendment-number collision (their A2 vs our A2). See the POML's blocked note.
