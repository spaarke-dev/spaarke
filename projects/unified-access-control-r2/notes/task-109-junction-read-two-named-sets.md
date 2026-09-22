# Task 109 — ONE junction read, two named sets

> **Status**: **NOT IMPLEMENTED.** This file currently holds only the **Step 0 metadata verification**
> and the **Step 1 before-state**, both measured live on **2026-09-21** (session 22). The executor of
> task 109 should read this instead of re-running them — but should re-run them anyway if more than a
> few days have passed, because they are measurements of live data, not facts about code.
>
> ⚠️ **Environment: DEV.** The owner recorded (2026-09-17) that dev's records are TEST records.
> Everything below is true of dev and says **nothing** about a production tenant.

---

## Step 0 — live metadata verification (COMPLETE)

The task POML flagged both of these as **UNVERIFIED**, because the Dataverse MCP server was down
(`CONNECTION_CLOSED`) when the task was authored. The server is reachable again and both are now
verified against live metadata via `describe('tables/sprk_contactorganization')`:

| Column | Type | Consequence |
|---|---|---|
| `sprk_enddate` | **DATE ONLY** | ✅ The escalation trigger — *"if `sprk_enddate` is DateTime/UserLocal, a read-time date comparison is the wrong instrument"* — **does NOT fire**. A Date Only comparison is correct, and `ge` (not `gt`) is the right boundary, mirroring `ExpiryPredicate`. |
| `sprk_startdate` | **DATE ONLY** | ✅ Confirms task 117's finding and owner decision **D-10**'s mechanics: `le` not `lt`, because access holds **on** the start date. |

Neither escalation trigger fires. Task 109 may proceed on the date-comparison design as written.

Full column list also confirms the junction's shape used by the query: `sprk_contact` and
`sprk_organization` lookups, `statecode` STATE (Active 0 / Inactive 1), collection name
`sprk_contactorganizations`.

---

## Step 1 — before-state (COMPLETE): every category is ZERO, and each zero has a denominator

D-2 part 3 and D-10 both require counting and listing what loses access **before** any behaviour
ships. All three categories are **0**. That is only meaningful with the population size beside it —
this project's own task 047 records the lesson that a bare zero is indistinguishable from an empty
table, so the denominators are given.

| # | Category | Count | Denominator |
|---|---|---|---|
| 1 | **D-2 part 1** — `statecode=0` **and** `sprk_enddate` in the past | **0** | of **2** junction rows (both Active) |
| 2 | **D-10** — `statecode=0` **and** `sprk_startdate` in the future | **0** | of **2** junction rows |
| 3 | **ISS-026** — Active org-keyed grant whose `sprk_organization` is Inactive | **0** | of **5** org-keyed Active grants; **all 5** sit under Active organizations |

### The entire junction table — both rows, listed in full (D-2 part 3 asks for the list, not just a count)

| Id | `statecode` | `sprk_startdate` | `sprk_enddate` | Under D-2 + D-10 |
|---|---|---|---|---|
| `a00736f3-8f96-f111-b8db-0022482fb5a7` | 0 Active | **2026-08-12** (past) | null | **still confers** ✅ |
| `0f2cace5-5296-f111-b8db-3833c5e5d030` | 0 Active | **null** | null | **still confers** ✅ |

### 🔴 The finding that matters most here

**One of the two live rows has a NULL `sprk_startdate`** — so **half the live data exercises D-10's
`eq null` branch.** That branch is not a theoretical edge case being defended on principle. If an
executor "harmonises" the date columns by mirroring **task 107**'s null-branch inversion (107 made an
undated GRANT confer nothing), this row **loses access immediately** — and it is a perfectly ordinary,
currently-valid membership.

This is exactly the failure D-10's constraint and the null-start-date acceptance criterion exist to
prevent, and the live data says the trap is live, not hypothetical. **Pin the null case with a test.**

---

## Adjacent gate discharged in the same pass — task 107 §4.2a

Not task 109's, but measured while the connection was open, and it had been sitting **UNMET**:

| Query | Result |
|---|---|
| Active grants with **null** `sprk_expiresdate` | **0** |
| Active grants **already past** expiry | **0** |
| Active grants, total | **28** (all 28 carry an expiry date) |

**Task 107's pre-deploy COUNT gate PASSES in dev: 0 of 28 grants lose access on deploy.**

Owner decision **D-1**'s "blast radius zero" claim was explicitly recorded as *never re-verified*. It
is now verified — **against dev**. ⚠️ **The gate must be re-run against any production tenant before
deploying there.** A dev result does not discharge a production gate; the query is in
`docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md` §4.2a.

---

## What is NOT done

Everything else in task 109: the junction query change, the `ActiveOrgMemberships` extension carrying
two named sets, the ISS-019 fault-reporting fix, the ISS-026 read guard, the three retired-rule
artifact corrections, and all tests. **Steps 0 and 1 only.**

### Method note

Counts came from the Dataverse MCP `read_query` tool. Two cautions for whoever repeats this:

- `read_query` caps at **TOP 20**; a larger `TOP` is rejected outright rather than silently truncated.
- Category 1 and 2 were first measured with `COUNT(column)` (which counts non-nulls) and then
  **re-confirmed with an explicit `IS NULL` predicate**, because relying on `COUNT(col)` null
  semantics to discharge a deploy gate is the kind of shortcut that produces a confident wrong number.
