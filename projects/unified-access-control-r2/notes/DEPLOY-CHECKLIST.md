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

Plus **two operator actions** that are NOT deploys and must happen at specific points (§5), and the
reconciliation job's **decided posture** (§4: schedule ON, writes OFF until the owner reviews one report — owner
round 7 item 1, 2026-10-02).

---

## 1. ✅ `sprk_noaccessentry` ALREADY EXISTS — **this section's original claim was WRONG**

> 🔴 **CORRECTION, 2026-09-21.** This section originally called `sprk_noaccessentry` a HARD BLOCKER
> requiring operator creation. **That was wrong, and it was wrong for this project's signature reason:
> I inferred "not created live" from the schema doc being NEW on the branch, and never checked against
> live Dataverse.** `describe('tables/sprk_noaccessentry')` returns the table fully formed — every
> column, the FR-23 description, `statecode`/`statuscode`. **Nothing needs creating.**

### Verified live, 2026-09-21 — the complete schema answer

| Schema item | Live? |
|---|---|
| `sprk_noaccessentry` (FR-23 deny-list table) | ✅ **exists**, all columns |
| `sprk_contactorganization.sprk_startdate` / `.sprk_enddate` | ✅ exist, both **DATE ONLY** |
| `sprk_externalrecordaccess` (all columns incl. `sprk_expiresdate`) | ✅ exists |
| `sprk_project` → `sprk_issecure`, `sprk_securitybu`, `sprk_containerid`, `sprk_externalaccount` | ✅ all four exist |
| `contact.sprk_standinggrant`, `contact.sprk_externalobjectid` | ✅ exist |
| **`sprk_accessevent`** (task 086) | ❌ **does NOT exist** — see below |
| `sprk_specontainerid` | n/a — a **name that was never real**; the field is `sprk_containerid` |

**No schema work is required for this branch to deploy.**

### The one missing table, and why it is not a blocker *yet*

`sprk_accessevent` does not exist live. It has **zero code consumers** — its writer is task **087**,
still open. It becomes a deploy dependency the moment 087 ships, and not before.

### The reasoning that was worth keeping (it just applies to nothing today)

`NoAccessListReader` **is** a VETO reader and **is** deliberately fail-CLOSED toward denial — its own
doc comment says *"an unreadable deny list must DENY … cannot prove not-denied."* That is the correct
direction for a veto. So the *mechanism* described here is real: a BFF whose deny-list table were
missing would deny every candidate on the external path. The error was asserting that situation
exists. It does not.

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

## 2. ✅ BFF API — **DEPLOYED TO DEV 2026-09-21** (commit `e45627fde`)

Deployed via `scripts/Deploy-BffApi.ps1 -Environment dev` (direct deploy; **dev has no staging slot**).
Package **45.58 MB**, under the 60 MB ceiling. **SHA-256 file-replacement verification passed on all 4
critical files** — that check is not optional: the skill records that `az webapp deploy --type zip` can
return HTTP 200 + Kudu `status=4 success` while the running .NET host's file locks silently prevent the
DLLs being replaced. Health green, both declared CORS origins present.

**Verified by probe — all six branch routes went 404 → live:**

| Route | Before | After |
|---|---|---|
| `can-manage-access` | 404 | **401** (exists, auth required) — this is the v1.0.31 PCF fix |
| `user-shares` | 404 | 401 |
| `set-record-share-expiry` · `share-user` · `unshare-user` · `unsecure-project` | 404 | 405 (POST-only) |

### 🔴 What was overwritten, and the ONLY correct way to restore it

Dev was running **`work/spaarkeai-word-add-in-r1`'s unmerged BFF code**, build dated 2026-09-19 14:04
— established by pulling the deployed `wwwroot` via Kudu and finding `OfficeVersionSaveAuthorizationFilter`,
`QuickCreateSourceAccessFilter`, `DocumentUrlIdentityFilter` and `SharingUrlToken` in the assembly
(none exist on master), while `TodoSourceAccessFilter` — present at their branch tip — was absent.

**Cleared with that project's session before deploying.** They confirmed no dependency: their work is
local `dotnet build`/`test`, and live verification for their tasks 062/063/064/067 is recorded as
pending and unscheduled.

**Rollback artifact** (byte-exact, keep it):
```
C:\tmp\bff-dev-rollback\bff-dev-wwwroot-2026-09-21-preUACdeploy.zip
51,022,445 bytes · sha256 35c8e161305d0a8a31f69c98d68063d397fde488d13be8e3592951731bc807a2
```

> 🔴 **If dev ever needs the add-in project's code back, restore THAT ZIP. Do NOT rebuild from their
> branch tip.** Their tip is deliberately not deploy-ready: every record the BFF creates app-only lands
> in the ROOT business unit (nothing sets `ownerid`), users sit in child BUs, and Deep depth traverses
> downward only — so their tip would 403 Run Index for everyone and regress Outlook's
> create-To-Do-from-email. That is their task **080**, unfinished. The snapshot is the **pre-gate**
> build and has none of that problem. Rebuilding "helpfully" from their tip would be strictly worse
> than what was overwritten.

### ⚠️ Process gap found while doing this

**The `Deploy BFF API` workflow has not succeeded since June 2026** — every run since is a failure, and
all recent deploys were hand-pushed `OneDeploy` with **no author recorded**. That is why nobody could
say what was running on dev without disassembling the deployed DLL. Worth fixing on its own.

Also: **basic auth is disabled on the SCM site** (`allow: false`), so Kudu needs an **AAD bearer token**;
publishing credentials return 401.

### Original section

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

## 3. ✅ PCF `TrackingFieldTrio` — **BUILT 2026-09-21. Ready to upload.**

> **Artifact**: `src/client/pcf/TrackingFieldTrio/Solution/bin/TrackingFieldTrioSolution_v1.0.31.zip`
> (280,941 bytes). **Live deployed version confirmed as 1.0.29** (installed 2026-06-30, read from the
> `solution` table), so 1.0.31 is a clean increment and matches the footer task 118's operator step checks.
>
> **Building it found two defects only a compile could find:**
> 1. `pack.ps1` was still `$version = "1.0.29"` — the 5th of the 5 version locations, and the only one
>    missed. It would have emitted a zip named `_v1.0.29.zip` against 1.0.31 manifests.
> 2. **TS2305 ×2** — the PCF host imports `IUserPick` and `ISecureOwnerInfo` from the
>    `AccessGrantModal` barrel, which never re-exported them (both are plain `export interface` in
>    `./types`, alongside seven siblings that *are* re-exported). Fixed by adding the two names.
>
> **Verified empirically, not by version string**: the new bundle **contains** `can-manage-access` (the
> v1.0.31 server gate) and **does not contain** `hasEntityPrivilege` (the retired fail-open check).
> 986,739 bytes vs the known-good 981,909 — consistent, so `build:prod` is correctly configured.
>
> Build note: the control had **no `node_modules`** in this worktree. `webpack.config.js` resolves via
> `require.resolve('react/package.json', { paths: [process.cwd()] })` — i.e. from the **control**
> directory — so it fails with *"Cannot find module 'react/package.json'"* even though `react` is
> declared in the control's own `package.json`. Fix: `npm install --legacy-peer-deps --no-audit --no-fund`
> in the control.

### Original finding, kept for the record

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

## 4. Config — the reconciliation job: SCHEDULED, REPORT-ONLY (owner round 7 item 1, task 137)

Both switches belong to the task 117 reconciliation job (`ExternalAccessReconciliationJob`). The owner decided the
posture on 2026-10-02 (§4.1). **Neither is a deploy step you choose: the code ships the decided posture.**

| Switch | Ships as | Meaning |
|---|---|---|
| Scheduled-job registration | **enabled** (`AddScheduledJob<ExternalAccessReconciliationJob>(DefaultCronSchedule)`, daily `0 5 * * *`) | every tick runs and reports |
| `ExternalAccess:Reconciliation:WritesEnabled` | **`false`** (explicit in `appsettings.template.json`; absent / empty / unparseable also = `false`) | report-only: R1/R2/R3/R4 are counted and listed, nothing is written |

**R4** *(owner round 71, 2026-10-06)*: an ACTIVE grant whose record is gone (`sprk_project`, `sprk_matter`,
`sprk_workassignment` all empty — a deleted matter / work assignment leaves its grants active, RemoveLink) is
deactivated; it confers nothing, so R4 removes no access, only a misleading "active" row. It rides the same switch.
Post-deploy check: create a throwaway matter + a contact grant on it, delete the matter, then
`POST /api/admin/jobs/external-access-reconciliation/trigger` → `GET …/status`: `R4-deactivate-grant-whose-record-is-gone`
`planned: 1`, `changed: 0` (report-only), and a `before-state … rule=R4-… sprk_matter=(empty)` trace. The ledger side
(`sprk_assignedaccess` rows of a deleted record → `Revoked` / `root-deleted`) is done by the Assigned-To job, which
writes it without a switch (no access changes) after reading the record and getting "not found".

Writes stay off because rules R2 and R3 **remove access that exists today**, and R1 turns a row that (since task 107)
confers nothing into one that confers access for 90 more days (`ExternalAccessModule.cs`, the comment above the
registration). **Do not set `WritesEnabled` to `true` until the owner has reviewed one report** (§4.1).

### 4.1 Task 137 (#1060) — the reconciliation job's posture: ✅ DECIDED (owner round 7 item 1, 2026-10-02)

Owner, verbatim ("follow recommended", recorded in `session27-owner-decisions-and-research.md`, round 7 item 1):

> - Enable the schedule in **report-only** mode now. Enable writes only after the owner has reviewed one report.
> - Inactive contacts and inactive roots stay READ guards only, so reactivating one restores access with no data repair.

Applied by task 137 r3 (begun on `task/uac-r2-137-b1`, interrupted; finished and verified on `task/uac-r2-137-b2`),
in one change:

- `ExternalAccessModule.cs`: `enabled: false` removed — the job is registered ENABLED on its daily schedule
  (`0 5 * * *`, unchanged). The owner's answer names no schedule, and R3/R4's "safety net at ≤ 5 min" is scoped to
  tasks 142/143. It does not bind this job: none of R1–R3 changes when access ends. The read path already stops a
  grant under an inactive organization and a membership past its end date (task 109), and it stops an undated grant
  (task 107). The job only makes the row's own state match.
- `ExternalAccess:Reconciliation:WritesEnabled`: **`false`** — now written explicitly in `appsettings.template.json`
  (beside a comment naming this decision) so the switch the owner will flip is discoverable; absent still means `false`.
- No writer rule for inactive contacts or inactive roots — they remain read-time guards (`AccessibleRecordSetService`).
- Pinned by `ExternalAccessReconciliationTests.S2_TheJobShipsScheduled_AndItsShippingConfigurationIsReportOnly`
  (registration enabled, schedule `0 5 * * *`, writes off with no key) and the existing S1 tests (every value but
  `true` is report-only).

**Owner's next action (not this task's):** after the first scheduled (or manually triggered) run on dev, review its
report, then decide whether to set `ExternalAccess__Reconciliation__WritesEnabled = true` in the App Service
configuration. The report comes from two sources, and only one of them lasts:

- **Durable: Application Insights traces.** Each run writes one `[EXT-ACCESS-RECON] heartbeat` line with
  `mode=report-only`, the per-rule `r1/r2/r3 Scanned / Planned / Changed / Failed` counts, `trigger=` and `runId=`.
  It also writes one `[EXT-ACCESS-RECON] before-state` line for EVERY row a write run would change. Those lines are the
  complete list. For example:
  `traces | where message startswith "[EXT-ACCESS-RECON]" | where timestamp > ago(2d) | order by timestamp asc`.
- **Process-local: the admin status endpoint.** `GET /api/admin/jobs/external-access-reconciliation/status` (as a
  `SystemAdmin`) returns `RecentRuns[0].ResultJson`: `mode`, `writesEnabled`, the per-rule `planned` counts and up to 200
  sampled ids per rule (`MaxSampledIdsPerRule`). The BFF's job store is `InMemoryBackgroundJobStore` (`SchedulingModule`), so this
  history is lost on restart. It is also visible only on the instance that ran the job. **No `sprk_backgroundjobrun`
  row is written.** Corrects the earlier notes; the Dataverse-backed store is ADR-036's target, not today's.

Before-state (dev, read-only, 2026-10-02 — `notes/task-137-soft-revocation.md` §2):

| Rule | Rows that would change | Note |
|---|---|---|
| R1 — active grant with no expiry | **0** | of 31 active grants (26 contact-only, 5 carrying an organization — one of them also names a contact) |
| R2 — active grant under an inactive organization | **0** | |
| R3 — active membership past its end date | **0** | 2 active memberships, both with no end date |
| (task 137) active grant whose CONTACT is inactive | **0** | read guard, no writer rule (owner round 7 item 1) |
| (task 137) active grant whose ROOT is inactive | **0** | read guard, no writer rule (owner round 7 item 1) |

The before-state was re-taken with the job's own scan shape (grants scanned 5, memberships scanned 0;
R1 = R2 = R3 = 0). With the schedule now enabled, the first report-only RUN happens at the next 05:00 UTC tick after the
dev deploy. It writes no Dataverse data at all: the run record goes to the in-memory job store, and the report goes
to the log lines above. To get it sooner, a `SystemAdmin` caller runs
`POST /api/admin/jobs/external-access-reconciliation/trigger`, then `GET …/status`. Expect `mode: report-only`, every
rule `planned: 0` and `changed: 0`, and a matching heartbeat trace (`notes/task-137-soft-revocation.md` §2). This run
is still a pending live gate (needs the deploy). The manual trigger is a live call into dev, so the main session runs
it.

History: escalation 1 fired 2026-10-02 (rounds 1–6 did not answer it — round 4 item 6's "reconciliation job" is task
141's `IdentityLinkReconciliationJob`); answered by round 7 item 1 the same day.

### 4.2 Task 137 — manual live gate (dev, no CI) — ⏳ PENDING, needs live WRITES

Run by the main session with existing test data only (no user relocation): the CIAM Test User contact
`394fda9f-ab95-f111-b8dc-7ced8ddc4cc6`, which holds a direct grant on matter `2444af6d-e1f2-f011-8406-7ced8d1dc988`
(View Only, row `0452ab4b-…`) and inherits organization grants through organization `67577f8c-4301-f111-8407-7ced8d1dc988`
(e.g. matter `042f4462-860e-f111-8342-7c1e520aa4df`, Full Access org grant row `9aed8ab9-c29c-f111-b8de-7ced8ddc4a05`
— chosen because the contact holds NO direct grant on that matter, unlike `b68299c6…`).
Requires the task 137 BFF deployed to dev first.

1. Deactivate matter `2444af6d…` (statecode 1, statuscode 2) → the external SPA loses it on the next request.
2. Reactivate it → it returns, with no other change.
3. Deactivate the contact `394fda9f…` → the next sign-in is refused (`sdap.access.deny.contact_inactive`), and an
   already-signed-in session loses every record on its next request. Reactivate afterwards.
4. As an existing non-admin Write-holder, revoke the organization grant `9aed8ab9…` → the CIAM session loses matter
   `042f4462…` on the next request (no 60-second wait). Restore the grant afterwards (re-grant the organization at
   Full Access, expiry 2026-12-10). If no existing non-admin Write-holder exists at that level, ask the owner to
   create one (owner round 4 item 1) rather than reusing the root-BU users.
5. Record evidence (timestamps, responses) in `notes/task-137-soft-revocation.md` §8.

---

## 4a. 🔴 CONFIRMED IN THE FIELD 2026-09-21 — PCF v1.0.31 DEPLOYED AHEAD OF THE BFF

**Symptom**: the Manage Access (grant) icon is disabled with the tooltip *"You do not have permission
to grant access"* — for a **Dataverse System Administrator**.

**This is not a permissions problem, and admin rights are irrelevant BY DESIGN.** v1.0.31 deliberately
stopped asking Dataverse *"may you create rows in the `sprk_externalrecordaccess` table, anywhere?"*
and now asks the BFF *"do you hold Write on THIS record?"* — the same question `DelegationRuleFilter`
answers. The endpoint that answers it is **not deployed**, and the gate fails **CLOSED** on anything
that is not a 200 (`index.ts` v1.0.31 header: *"anything other than a 200 naming this record disables
the affordance"*). So the control correctly disabled itself.

**Evidence** (unauthenticated probes against `https://spaarke-bff-dev.azurewebsites.net`):

| Probe | Result | Reading |
|---|---|---|
| `/healthz` | **200** | the BFF is up |
| `/api/v1/external-access/can-manage-access?...` | **404** | the route does not exist |
| `/api/v1/external-access/revoke` (control) | **405** Method Not Allowed | external-access routes **are** served |

The 405 control is what makes this conclusive: the BFF is serving that route area, so the 404 is
specifically "this route is not deployed" — not an auth rejection, not a missing area, not a cold app.

**Fix**: deploy the BFF. Both paths are operator-driven —
`/bff-deploy` from this worktree (deploys the branch **without merging**), or `workflow_dispatch` on
`Deploy BFF API` (currently `disabled_manually`, so it needs enabling first).

**Rollback option if the BFF deploy is not imminent**: re-import PCF **v1.0.29**. That restores the
old client-side privilege check and the button works again — but it is the *wrong rule* (fail-OPEN,
table-wide rather than per-record), which is exactly what v1.0.31 exists to retire. Prefer deploying
the BFF.

> ⚠️ **Generalised**: §5b already said "code before config" for the privilege removal. This is a
> THIRD ordering edge that section did not name — **BFF before PCF**. Deploying the PCF first does not
> break anything permanently, but it disables Manage Access for **everyone**, including admins, until
> the BFF catches up. Full order: **BFF → PCF → remove `Create` privilege.**

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
