# Is a manual backfill worth doing now? — measured answer: no

> **Date**: 2026-09-28
> **Why**: the owner offered to perform a manual backfill by hand rather than wait for task 080, and asked to be
> told if one was needed. This is the answer, measured live rather than reasoned from last week's numbers.
> **Conclusion**: **nothing needs manual backfilling right now. Wait for UAC-r2, then 080.**

---

## Live measurement (Dataverse MCP, 2026-09-28)

| Fact | Value |
|---|---|
| `sprk_document` rows | **515**, **all** in root `Spaarke` (`06fbf21c…`) — was 512 on 09-22, so creates are still landing in root |
| `sprk_todo` rows | **50**, all in root |
| BFF application users | **all three** in root and enabled — `# mi-bff-api-dev` (`8793f4b0…`), `# spaarke-bff-api-prod`, `SDAP-BFF-SPE-API` |
| Default owner teams | one per BU, `isdefault = true` AND `teamtype = 0` — root → `09fbf21c…`, **Spaarke Business Unit 1 → `cf15f587…`**, Secure Project → `daec0b6f…`, plus Demo / Dev 1 / Test 1 |
| **Non-default Owner teams in root** | **4**, all GUID-shaped names — the live reason the `isdefault` + `teamtype` double predicate is mandatory |

### The number that changes the decision — user distribution

| Business unit | Users |
|---|---|
| root `Spaarke` | **173** |
| `Spaarke Business Unit 1` | **1** (testuser1) |
| `Spaarke Demo` | **13** |

## 🔴 This corrects task 081's stated premise

081's POML says the gated flows are *"403 for every ordinary user"*. In this environment that is **overstated**.

Deep depth grants **own BU + descendants**. The records are root-owned, and **173 of 187 users are in root** — so
those 173 reach the records fine. The ownership defect bites only users in a **child** BU, which is **14 users**
(testuser1 + the 13 Demo users).

This is the same correction already recorded once this project: a root-BU user with Deep depth was never blocked
by ownership placement, so a 403 seen by a root account has a *different* cause (role grants). Anyone funding
081 should re-verify its "every ordinary user" claim against this distribution first.

**Production is the opposite**, and that is why 080 still matters: the tenancy model puts customer users in
child BUs, so there the defect would affect everyone. Dev's root-heavy shape is the setup artifact the owner
already flagged as non-indicative — it makes the problem *look* smaller here than it is in the model that ships.

## What a manual backfill could and could not fix

| Option | Effect | Verdict |
|---|---|---|
| **A** — move `# mi-bff-api-dev` (`8793f4b0…`) from root into `Spaarke Business Unit 1` (`cb15f587…`) | **New** app-created records land in BU 1 → readable by testuser1. Root users keep reading them (BU 1 is a descendant of root, so Deep still covers it). | Fixes going-forward creates for **one** test account. Gives **app-user** ownership, not team ownership, so it is not Goal B. Demo's 13 users are in a **sibling** BU and still would not see them. |
| **B** — reassign the 515 documents + 50 todos to a BU's default owner team | Assigned to **root's** team → changes nothing for child BUs (root is their parent). Assigned to **BU 1's** team (`cf15f587…`) → testuser1 reads them, root users still do, Demo still does not. | Fixes **existing** rows for one account. This is precisely the backfill 080 automates with a dry run. |

So the only coherent manual action is **A + B together, targeted at `Spaarke Business Unit 1`** — which buys
readability for exactly **one** test user.

## Why the recommendation is "don't"

1. **The benefit is one test account.** 173 of 187 users are unaffected; Demo's 13 stay unaffected either way
   because they are in a sibling BU.
2. **080 does it properly and reversibly.** Its escalation trigger 2 *requires* the backfill be dry-runnable and
   reversible. A hand-run reassignment of **565 records** across a shared environment has neither property, and
   undoing it means knowing the prior state — which after the fact is exactly what nobody has.
3. **080 is waiting on UAC-r2 anyway** (task 043 must reach our base, or team-owned records resolve to nobody in
   the membership resolver — issue #1011). Doing the data half early does not unblock the code half.
4. **`unified-access-control-r2` currently owns `spaarke-bff-dev`.** A 565-record ownership change in an
   environment another project is actively testing against is a coordination problem, not a chore.
5. Moving an **app user's** business unit also changes what every *other* consumer of that app registration
   creates, not just the Office paths — a wider blast radius than the problem it fixes.

## If the owner still wants it, the exact recipe

Reversible-by-construction, because it records the prior state first:

1. **Capture the before-state** so a revert is possible:
   `SELECT sprk_documentid, ownerid, owningbusinessunit FROM sprk_document` → save to a file. Same for
   `sprk_todo` (`sprk_todoid`).
2. **Option A**: set `# mi-bff-api-dev` (`systemuserid 8793f4b0-01db-f011-8406-7c1e520aa4df`) business unit to
   `cb15f587-baa0-f111-aaac-000d3a99d1d7`.
3. **Option B**: assign the 515 + 50 rows to team `cf15f587-baa0-f111-aaac-000d3a99d1d7`
   (*Spaarke Business Unit 1*, verified `isdefault = true`, `teamtype = 0`).
4. **Verify as testuser1**, not as an admin — a root/admin account can prove neither success nor failure here.
5. Tell task 080 it happened, so its backfill does not re-derive a state that already changed.

⚠️ Do not assign to root's default team (`09fbf21c…`). It looks like "fixing ownership" and changes nothing for
child-BU users, because Deep never traverses upward.
