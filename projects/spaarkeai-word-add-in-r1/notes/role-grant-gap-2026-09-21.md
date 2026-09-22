# 🔴 The new authorization gates require rights the shipped Spaarke roles do not grant

> **Verified live against the dev Dataverse environment via MCP, 2026-09-21.** Every row below is a query
> result, not an inference.
>
> **Status: OWNER DECISION REQUIRED before any deploy carrying tasks 062, 063 or 064.**
>
> This is one finding, not four. Tasks 062, 063, 064 and 066 each hit a face of it, and each recorded it as
> an unmet "no live verification" criterion. Read together they say something the individual notes do not:
> **the Spaarke end-user security roles were built for a BFF that did everything app-only. Adding real
> per-caller authorization exposes that end users were never granted the underlying Dataverse rights.**

---

## 1. What each gate needs, and what the roles actually grant

| Gate | Requires | `Spaarke Office Add In User` | `Spaarke Basic User` | Result for a normal user |
|---|---|---|---|---|
| **062** entity picker | read on Matter/Project/Invoice/Account/Contact | ❌ **nothing** | ✅ `prvReadsprk_Matter` depth 4 | works **only** if the user also holds Basic User |
| **063** Run Index | **`prvWritesprk_document`** | ❌ nothing | ❌ **nothing** | 🔴 **403 for every non-admin user** |
| **064** `/todo`, document source | `prvReadsprk_Document` | ❌ nothing | ✅ depth 4 | works with Basic User |
| **064** `/todo`, communication source | `prvReadsprk_Communication` | ❌ nothing | ⚠️ depth **1** (own records only) | 🔴 **403 for everyone** — see §3 |
| **066** communications routes | any discriminating read | ❌ nothing | ⚠️ depth 1 | filter cannot discriminate at all — task escalated |

## 2. 🔴 The worst one: nobody can write `sprk_document`

`prvWritesprk_document` is granted by **exactly three roles**, all at depth 8 (Global):

| Role | Depth |
|---|---|
| Service Writer | 8 |
| System Administrator | 8 |
| System Customizer | 8 |

**None is an end-user role.** Neither add-in role has it. So task 063's per-document `Write` gate — which is
correct, because the route *mutates* the row — refuses **every ordinary user**, including `Test User 1`.
Only an administrator could run Find → Run Index.

This is not conditional on how a tenant provisions roles, the way §1's 062 row is. It fails for everyone.

## 3. The communication row is worse than a missing grant

`Spaarke Basic User` does grant `prvReadsprk_Communication`, but at **depth 1 — own records only**. Task 066
established live that **100% of `sprk_communication` rows are owned by a BFF application user**, because
`EmailUploadCaptureService.BuildCommunicationEntity` never sets `ownerid`. A user owns none of them.

So depth-1 read is not a partial grant, it is an **empty** one. This is also exactly why task 066 could not
ship a filter: ownership-depth security over a table whose rows all share one owner degenerates into a
table-level on/off switch.

## 4. Why every task missed it, and why that was reasonable

Each task verified its own mechanism and recorded the role question as explicitly unverified:

- **062** — I closed the risk on the wrong precondition. I verified the **BFF app user** may impersonate
  (`prvActOnBehalfOfAnotherUser` — true, and note that in this environment **System Administrator grants it**,
  contrary to the usual belief that Delegate is required). I did not check whether the **impersonated end
  user** may read the target tables. Impersonation has two preconditions; I checked one and reported clear.
- **063** and **064** — both wrote "no live verification that add-in users hold the needed rights; confirm
  before deploy" as an unmet criterion. Both were right to.
- **066** — found the ownership half, and escalated instead of shipping a filter that could not work.

**Dev masks all of it**: `Test User 1` holds *both* `Spaarke Basic User` and `Spaarke Office Add In User`, and
no user in the environment holds the add-in role alone.

## 5. Options

These are not mutually exclusive, and (1) is required for 063 regardless.

| # | Option | Cost / risk |
|---|---|---|
| **1** | **Grant the missing rights to the add-in role** — read on the five searched tables, **write on `sprk_document`**, read on `sprk_communication` | Security-role change. **Depth is the decision, not a detail** — depth IS the trim for 062, so picking depth 8 would hand back the enumeration F1 just closed. |
| **2** | **Re-scope 063's gate from `Write` to `Read`** | Cheaper, but wrong on the merits: the route mutates `sprk_searchindexed`. A read gate on a write operation is the shape this whole wave exists to remove. Not recommended. |
| **3** | **Make refusals legible** — permission-denied renders "you do not have access", not a 500 (062) or a bare 403 (063/064) | Doesn't fix access; makes misconfiguration diagnosable instead of looking like an outage. Worth doing alongside (1). |
| **4** | **Set `ownerid` on communication rows at create** | Unblocks depth-based security for `sprk_communication` and is the prerequisite for task 066's real fix. Data-model change owned by the Communication project; does not help existing rows. |

**Recommendation: (1) + (3).** (1) with depth chosen deliberately per table, (3) so the next
role misconfiguration is self-diagnosing rather than a support ticket.

## 6. What this does NOT mean

- **Do not revert 062, 063 or 064.** The gaps they closed are real: an untrimmed enumeration of every
  Matter/Project/Invoice/Account/Contact (F1, HIGH), a caller-chosen tenant index partition (F2), and an
  app-only write of caller-supplied GUIDs plus an existence oracle (F3). The gates are correct. The **role
  grants are the thing that is missing**, and they were missing before this project started — the gates just
  made it visible.
- **Do not read §1 as a production statement.** Verified in **dev only**. Production roles may differ, and
  `spaarke-bff-api-prod` was not examined.

## 7. Verification queries used

All against `roleprivileges` ⋈ `privilege` ⋈ `role`, and `systemuserroles` ⋈ `role` ⋈ `systemuser`:

- roles granting `prvWritesprk_document` → Service Writer, System Administrator, System Customizer (depth 8)
- roles granting `prvReadsprk_communication` → +Service Reader, Support User (1), **Spaarke Basic User (1)**
- `prvReadsprk_matter` / `prvReadsprk_document` on the two Spaarke roles → Basic User depth 4 only
- users holding either Spaarke role → `Test User 1` (both), `Ralph Schroeder` (Basic User)

---

## 8. ✅ RESOLVED 2026-09-21 — owner granted the rights to `Spaarke Core User`

Verified live via MCP **after** the change. All six gating privileges are present at **depth 4 (Deep)**:

| Privilege | Depth | Unblocks |
|---|---|---|
| `prvReadsprk_Matter` / `_Project` / `_Invoice` | 4 | **062** entity picker |
| `prvReadsprk_Document` | 4 | **064** document-source To Do |
| **`prvWritesprk_Document`** | 4 | **063** Run Index — *was the hard blocker; no end-user role had it* |
| `prvReadsprk_Communication` | 4 | 064 / 066 — **but see §8.2** |

Also granted at depth 4: `prvAppendTosprk_Matter` / `_Project` / `_Invoice` / `_Document` / `_WorkAssignment`
/ `_Event` / `_Communication`. **This closes task 065's separate finding** that no end-user role held
`AppendTo`, which meant filing a document to a Matter was admin-only on shipped code.

### 8.1 Depth 4 (Deep) rather than the recommended depth 2 (BU) — consequence, not a defect

For a user in a **leaf** BU (e.g. `Test User 1` in *Spaarke Business Unit 1*, which owns the Matter rows)
Deep and BU are **identical**, so the picker trims exactly as designed.

For a user in the **root** BU *Spaarke* (e.g. the owner account), Deep spans **all child BUs including
"Secure Project"**. If Secure Project is meant to be invisible to root-BU staff, depth 2 is the correct
setting for the five searched tables. Flagged, not blocking — it is a deliberate owner choice and matters
only for root-BU users.

### 8.2 ⚠️ The communication grant does NOT unblock 064's communication carrier or task 066

`prvReadsprk_Communication` at depth 4 **looks** like it resolves them. It does not, for a structural reason:

- Every `sprk_communication` row is owned by a **BFF application user** (`bb5a90e5…` = SDAP-BFF-SPE-API,
  `8793f4b0…` = mi-bff-api-dev) — confirmed again here.
- Those rows sit in the **ROOT** business unit `06fbf21c…` (*Spaarke*).
- **Deep traverses DOWNWARD** — own BU plus descendants. A user in a *child* BU does **not** reach rows owned
  in the *parent*.

So `Test User 1` still cannot read any communication row. Only **Global (8)** would reach them — which is
precisely the tenant-wide disclosure task 066 escalated about, so granting it would re-open F9 rather than
close it.

**Unchanged conclusions**: task 066 stays escalated; 064's communication-sourced To Dos still 403; the real
fix remains setting `ownerid` on communication rows at create time
(`EmailUploadCaptureService.BuildCommunicationEntity`), which is a Communication-project data-model change and
does not help rows that already exist.

### 8.3 Still not verified

Dev only. Production roles were not examined, and `spaarke-bff-api-prod` was not checked. The end-to-end
behaviour of 062/063/064 under the new grants has **not** been exercised against a live host — that is task
042's UAT, and it should now be expected to pass rather than 403/500.
