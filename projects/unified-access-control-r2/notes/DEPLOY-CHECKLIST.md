# `unified-access-control-r2` — what needs to be deployed

> **Written for**: whoever performs the deploy, plus the owner deciding sequencing.
> **Compiled** 2026-09-21 (session 22) by measuring `origin/master...HEAD`, not from memory.
> **Branch**: `work/unified-access-control-r2` · **PR** #950 · **Status**: NOT merged, NOT deployed.
>
> ⚠️ **This branch is not finished.** 27 tasks remain open. This document describes what *the work
> already on the branch* would require if it were deployed today. Re-measure before an actual deploy —
> the numbers below are a snapshot, and this project's most repeated defect is a note outliving its
> subject.

---

## 0. The short answer

**Five deployable artifacts, and three of them have an ordering constraint you cannot get wrong.**

| # | Artifact | State | Blocker? |
|---|---|---|---|
| 1 | **Dataverse schema** — `sprk_noaccessentry` (new table) | docs only, **not created live** | 🔴 **HARD BLOCKER** — see §1 |
| 2 | **BFF API** (`Sprk.Bff.Api`) | 98 files changed, not deployed | 🔴 must follow #1 |
| 3 | **PCF `TrackingFieldTrio`** v1.0.29 → **v1.0.31** | manifests bumped, **bundle NOT rebuilt** | 🔴 see §3 |
| 4 | **PCF `RegardingResolver`** v1.4.9 → **v1.5.0** | bundle rebuilt ✅ | ready |
| 5 | **Code page `LegalWorkspace`** | real code changes, needs rebuild | ready to build |

Plus **two operator actions** that are NOT deploys and must happen at specific points (§5), and **two
config switches that must stay OFF** (§4).

---

## 1. 🔴 `sprk_noaccessentry` MUST exist before the BFF deploys

**This is the one that takes the system down if missed.**

The FR-23 deny-list table `sprk_noaccessentry` has **live server consumers at HEAD** —
`Infrastructure/ExternalAccess/NoAccessListReader.cs` and `Infrastructure/DI/ExternalAccessModule.cs`.
Its schema is in the branch as **documentation only** (`src/solutions/SpaarkeCore/.../sprk_noaccessentry/entity-schema.md`,
216 lines, new). Per the owner's standing rule, schema work in this project is **code + docs only; the
live change is an operator step.** Nobody has performed that step.

**Why it is a hard blocker and not a degradation**: `NoAccessListReader` is a **VETO** reader and is
deliberately **fail-CLOSED toward denial**. Its own doc comment states the design: *"an unreadable deny
list must DENY … cannot prove not-denied."* That is the correct direction for a veto — but it means a
BFF deployed against a tenant where the table does not exist will fail every deny-list read and
therefore **deny every candidate on the external access path**.

> **Order: create the table, verify a read succeeds, then deploy the BFF.** Reversed, external access
> is down until the table exists.

**Note the contrast**: `sprk_accessevent` (task 086) is also schema-only, but has **zero** code
consumers — its writer is task **087**, still open. It is not a deploy dependency today. Do not treat
the two the same way just because both are new tables.

---

## 2. BFF API — `Sprk.Bff.Api`

98 changed files. Deploy is **operator-driven**: the `Deploy BFF API` workflow is `disabled_manually`.

**Publish size**: measured repeatedly this project against a fresh master worktree — the largest
single-task delta recorded was **+1,354 bytes** (task 108). Absolute ≈ **45.6 MB** compressed incl.
PDBs, against a **60 MB hard ceiling**. No size concern; re-measure per root CLAUDE.md §10 anyway, and
**compare file counts on both sides** — unequal counts mean one publish is incomplete and the delta is
meaningless.

**Endpoint added that the PCF depends on** (task 118):
`GET /api/v1/external-access/can-manage-access?recordType={t}&recordId={id}` → `200 {"canManageAccess":bool}`.
A **404** here means the BFF is not yet deployed — that is the check §5 uses.

---

## 3. 🔴 PCF `TrackingFieldTrio` — the bundle was never rebuilt

| | Value |
|---|---|
| `ControlManifest.Input.xml` | 1.0.29 → **1.0.31** ✅ bumped |
| `Solution/solution.xml` | 1.0.29 → **1.0.31** ✅ bumped |
| `index.ts` | ✅ changed (the fail-closed gate) |
| **`Solution/Controls/.../bundle.js`** | ❌ **NOT in the diff** |

The shipped `bundle.js` last changed on **2026-08-12** — it predates the v1.0.31 behaviour entirely.
The manifests advertise 1.0.31; the compiled artifact is a month behind them. A solution built from
this tree right now would **import as 1.0.31 while running 1.0.29 code**, which is worse than not
deploying, because the §5 version check would read 1.0.31 and pass.

**Required before packaging:**

```
npm run build:prod          # NOT `npm run build` — root CLAUDE.md §12 / FAILURE-MODES AP-1
```

⚠️ Compare with `RegardingResolver`, which did this correctly: its `bundle.js` **is** in the diff
alongside its 1.4.9 → 1.5.0 bump. That is what a complete PCF change looks like here.

**What v1.0.31 changes**: the Manage Access affordance moves from a **fail-OPEN** client-side table
privilege check (`hasEntityPrivilege`) to a **fail-CLOSED** call to the server's real rule
(`evaluateGrantGate()` → the §2 endpoint). Defaults were inverted accordingly
(`canGrantAccess === true`, host default `false`).

---

## 4. Config — two switches that must stay OFF

Both belong to the task 117 reconciliation job. **Neither is a deploy step; both are owner decisions.**

| Switch | Ships as | Meaning |
|---|---|---|
| Scheduled-job registration | `enabled: false` | the job does not run |
| `ExternalAccess:Reconciliation:WritesEnabled` | **absent → `false`** | report-only even if triggered |

This is belt **and** braces on purpose: even a manual admin trigger of the disabled job writes nothing,
because writes are separately gated. The reason is in `ExternalAccessModule.cs:360-372` — rules R2 and
R3 **remove access that exists today**, and R1 turns a row that (since task 107) confers nothing into
one that confers access for 90 more days.

Default cron if ever enabled: `0 5 * * *`. **Do not enable either without an owner decision and a
before-state count.**

---

## 5. Operator actions — ordering is load-bearing

### 5a. ✅ Task 107 pre-deploy COUNT gate — **MEASURED IN DEV 2026-09-21: 0 of 28. Still UNMET for production.**

**Dev result**: **0** Active grants carry a null `sprk_expiresdate` (all **28** Active grants have one),
and **0** are already past expiry. **Nothing loses access on a dev deploy.** Owner decision D-1's
"blast radius zero" claim — recorded as never re-verified — is now verified against dev.
Evidence: `notes/task-109-junction-read-two-named-sets.md`.

⚠️ **A dev result does not discharge a production gate.** Re-run §4.2a against the target tenant
before deploying there. The original wording follows, because it is what the production run must do:

Task 107 inverted the grant-expiry read so that an **undated** grant confers **nothing**. Every
`Active` grant with a null `sprk_expiresdate` **loses access on deploy**.

The exact OData COUNT query is in `docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md` **§4.2a**. Run it and
record the number **before** deploying. D-1's "blast radius zero" assumption was **never re-verified
against a live tenant** — that is precisely why the gate exists.

### 5b. Task 118 `Create`-privilege removal — **AFTER** the deploy, never before

Remove `Create` on `sprk_externalrecordaccess` from each human security role (starting `SDAP User
(Core)`), leaving `Read` and `Write` as they are.

**Sequence, per `notes/task-118-manage-access-gate-write-on-record.md` §8 — code before config:**

1. Deploy the **BFF**, then verify: `GET …/can-manage-access?recordType=project&recordId={a project you can write}` returns **200**. A 404 means not deployed.
2. Deploy **PCF v1.0.31**, then verify: open a record carrying `TrackingFieldTrio` and confirm the footer reads **v1.0.31** (hard-refresh `Ctrl+Shift+R`; the version footer exists for exactly this check). ⚠️ This check is only trustworthy if §3 was done — otherwise the footer reads 1.0.31 over 1.0.29 code.
3. **Only then** remove the privilege.

**Reversed, Manage Access vanishes for everyone**: the still-deployed old client gates on the
privilege, so `computeCanGrantAccess` returns `false` once it is removed. Rollback is clean — the new
code does not consult the privilege either way, so restoring it returns the prior state exactly.

---

## 6. Not deployable yet — do not go looking for these

| Item | Why not |
|---|---|
| Task **101** "External shares by expiration" Dataverse views | task still **open**; views not authored |
| Task **087** access-event write hooks | **open** — which is why `sprk_accessevent` has no consumers |
| Task **109**'s membership date bounds (D-2 + **D-10**) | **open**. When it ships it **removes live access** at *both* ends of the date range and requires its own three-category before-state count |

---

## 7. Gate that is still unmet for merge, independent of deploy

**`push-to-github` Step 1.7 — real-Dataverse smoke.** This branch changes Dataverse-querying code on
the access path (107 inverts the expiry read, 117 adds a reconciliation writer, 118 adds a gate
endpoint, 109 will rewrite the junction read). Mock-only verification is explicitly **not sufficient**
for that gate. It has not been run.

**CI**: `Router` is the single required check. `gh pr checks` **cannot see it** — probe by name via
`gh api repos/.../commits/$SHA/check-runs`, **once**, at the moment the merge decision is taken.
