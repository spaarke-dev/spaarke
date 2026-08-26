# Current Task State — sdap-SPE-admin-app-r2

> **Last Updated**: 2026-08-25 (by `context-handoff`)
> **Recovery**: read "Quick Recovery" first. History lives in [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **none in progress.** Workstreams A–C **DONE** + 060. Remaining: 041, 042, 050, 051, 052, 061, 062, 090. |
| **Status** | ✅ **MERGED TO MASTER · CI GREEN · DEPLOYED TO DEV (code page AND BFF)** |
| **Branch** | `work/sdap-SPE-admin-app-r2` — clean, 0 unpushed, level with origin |
| **Next Action** | **041** (LiveIntegration suite + throwaway container fixture) → 042 → 051/050/052 → 061/062 → **090** (`/test-diet` is a BINDING gate). See §7 for a suggested re-ordering. |

### ✅ Shipped and verified live (2026-08-25)

| | |
|---|---|
| Merged to master | `d1f4470cd..ec28cd3b2`, verified ancestors of master head |
| CI | **green** (`SDAP CI on 220252a81: success`) — it was **failing before** this merge |
| Code page | **spaarkedev1**, web resource `sprk_speadmin` (2,302 KB) |
| BFF | **spaarke-bff-dev** — hash-verified 4/4 files, healthy, CORS gate passed |
| **Proof the BFF is current** | `GET /api/spe/containertypes/test/owners` → **401** (route is new today; was **404** before) |

**Open it**: `https://spaarkedev1.crm.dynamics.com/WebResources/sprk_speadmin`

**Worth walking**: settings saves that now persist (the etag fix) · container URL + Purview routing ·
the Owners tab · Manage-Permissions landing on the right screen · the expired-trial warning.

---

## 1. 🔑 THE headline finding — the PATCH-400 was a missing `etag`

**Two days of "container-type writes are impossible" was a missing required body property.**

Graph's Update API lists **`etag` as REQUIRED in the request BODY**, and Microsoft's own
*"Example 2: Update without ETag"* documents the response as `400 Bad Request` — our exact symptom.

| Identical no-op PATCH | |
|---|---|
| without `etag` | **400** |
| with `etag` in the body | **200** ✅ (beta AND v1.0) |
| round-trip: wrote 499 → read back **499 PERSISTED** → restored 500 | ✅ **task 023 AC-2** |

⚠️ **It is a BODY property, NOT the `If-Match` header.** An earlier session tried the header, correctly
recorded that it changed nothing, and read that as "the etag is irrelevant" — which aimed the whole
investigation at auth. The recorded hypothesis ("only the owning app may modify its container type")
was **wrong**, and disproving it would have cost a throwaway container type or a change to the
**production SPA registration**. Neither was needed.

Fix: `UpdateContainerTypeSettingsAsync` is a read-modify-write (GET → etag → PATCH) and **throws rather
than sending a doomed write** when Graph returns no etag. Full record:
[`notes/patch-400-resolution.md`](notes/patch-400-resolution.md).

**Unblocked 023 (✅ complete), 025, 026, 029.** ⚠️ 025's form rebinding and 026's AC-2 finding were never
about the 400 — this removed the blocker, not their scope.

> ### 📌 THE LESSON — and it happened TWICE in one day
> Both 400s (settings PATCH, owner POST) were **documented requirements** returned as `invalidRequest` /
> *"One of the provided arguments is not acceptable"*, naming no cause. Both were **one fetch of
> Microsoft's reference page away**. The corpus and the CSDL being silent is not the platform being silent.
> **→ Read the vendor's doc for the exact operation BEFORE hypothesising about auth.**

---

## 2. Tasks closed this session

| Task | Outcome |
|---|---|
| **011** | GET/CREATE/SAVE moved to the delegated path (all three were app-only ⇒ guaranteed 403) |
| **023** | ✅ **COMPLETE** — AC-2 proven live by the etag fix |
| **027** | ✅ **COMPLETE** — owners verified live: add **201**, remove **204**, list back to 0 |
| **028** | ✅ container URL + Purview routing |
| **060** | ✅ dead stub deleted, misfiled endpoints moved (9 routes diffed byte-identical) |
| **2 unowned UI defects** | ✅ Manage-Permissions deep link; expired-trial invisibility |

### 🔴 Premise errors found (9 for 9 — assume the next POML is wrong too)

- **027**: "supersedes the ContainerTypePermissions screen" — **false**. `applicationPermissions`
  (which APPS may access) vs `permissions` (which PEOPLE own) are orthogonal. **Retired nothing.**
  Guarded structurally: route is `/owners`, separate DTOs, separate tab.
- **027**: `permissions` is **beta-only** while container types are **delegated-only** — those axes had
  never crossed. Added `GraphClientFactory.ForUserBetaAsync` (same OBO exchange, same cached token, only
  the base address differs ⇒ **no ADR-028 A4/E-3 surface**).
- **028**: `fileStorageContainer` has **no URL property in either version**; on the COLLECTION Graph
  accepts `$expand=drive($select=webUrl)`, returns **200**, echoes it in `@odata.context`, and **omits it
  from every row**. The natural implementation ships an empty column backed by a 200.
- **027 correction**: I recorded the 3-owner limit as "unsourced". **Microsoft documents it** (max 3,
  4th → 400), plus: only `owner` is a valid role, and only existing owners / SPE Admins / Global Admins
  may add one.

---

## 3. Live-tenant facts (verified 2026-08-25 — do not re-derive)

Four container types, **each owned by a different app**:

| Type | id | owningApp | billing |
|---|---|---|---|
| Spaarke PAYGO 1 | `8a6ce34c-6055-4681-8f87-2f4f9f921c06` | `170c98e1` | standard / **valid** |
| Spaarke DMS-SPE Trial | `ef8e5d5b-f9c1-4cdb-9b4f-8ca50d070255` | `2c708318` | trial / valid · **expired 2025-10-10** |
| Spaarke DMS Dev 1 | `5c1ea58e-1052-49db-841a-6ecc3a2269ad` | `fd1325aa` | directToCustomer / valid |
| Spaarke Demo Documents | `362f90b3-7b72-4ab1-bb4c-20a1399ca838` | `da03fe1a` | standard / valid |

- **`billingStatus` is `valid` on all four** — matching the operator's M365 screenshot. The "Unknown"
  seen in local review was a **fixture artefact**, not a code defect. Task 029's mapping is correct.
- **An expired trial still reports `billingStatus: valid`** — billing health and usability are
  independent. That is why the expiry work keys off `expirationDateTime`.
- **Zero owners on every container type.** Bootstrapping one needs a directory-role holder.
- Listing containers of a type the caller's app does not own → **403**.

### 🔑 Live verification recipe

`notes/verify-container-type-owners.py` and `notes/delegated-diagnostics.py` (device-code,
**auto-renewing** — three codes expired unused before that was added). Delegated needs ~30s of operator
time at <https://microsoft.com/devicelogin>. App-only works for containers/files/storage but is **403 on
container types**. Graph's OData `$metadata` needs **no token** and has settled four tasks — reach for it
first.

⏰ The `spe-owning-app-secret` credential **expires 2028-08-24**.

---

## 4. ⚠️ Deploy procedure — I got this wrong; do not repeat it

**Use the skills**: `/bff-deploy` for the BFF · `/code-page-deploy` for code pages (operator direction,
2026-08-25).

- 🔴 **We have NEVER deployed with slots.** `config/environments.json` declares
  `appServiceSlot: staging`, and **`spaarke-bff-dev` has ZERO slots**. I passed `-UseSlotDeploy`, the
  deploy failed at step 3/7 with `ResourceNotFound`, and — worse — I had told the operator the deploy was
  *safe because of* the slot-swap rollback that field implied. Nothing was deployed; the claim was still
  wrong. **Plain `Deploy-BffApi.ps1`, no slot flags.**
- **Invoke with `pwsh`**, not `powershell` — 5.x lacks `Get-FileHash` (logged 2026-05-27 incident).
- **Clean `deploy/api-publish` + `src/.../publish` first** (MSB3030 nested-publish guard).
- **Hash-verify vs health check are different signals**: hash-verify passing + healthz timing out means
  the deploy WORKED and the app is still booting (Linux cold start 90–120s). **Do not redeploy there.**
- **Verify by route probe**: any `RequireAuthorization` route must return **401**, never 404.

### SPE Admin is NOT a special case (operator asked)

| Family | Build | Count | Deploy |
|---|---|---|---|
| `src/client/code-pages/` | Webpack | 6 | what `/code-page-deploy` documents |
| `src/solutions/` | **Vite single-file** | 30+ | **one `Deploy-*.ps1` per solution** |

SpeAdminApp is the second family; per-solution scripts are the norm there (`Deploy-CalendarSidePane.ps1`,
`Deploy-DailyBriefing.ps1`, `Deploy-EventsPage.ps1`, ~15 more). **Drift worth fixing**:
`/code-page-deploy`'s `appliesTo` claims `**/solutions/**` but its body documents only the Webpack
convention.

---

## 5. ⚠️ Repo hazards hit this session

1. **The main repo regenerates stale build artifacts.** `c:/code_files/spaarke` had **140 untracked
   `.js`/`.d.ts` files from Feb–June** inside `Spaarke.UI.Components/src/`. Rollup resolved
   `themeStorage` to the stale June `.js` (predating `getDisplaySizePreference`) and the code-page build
   **failed**. My worktree built fine on the identical commit. I removed the 140 and preserved the one
   tracked file (`src/__mocks__/diff.js`, a hand-written mock). **They will come back** the next time
   `tsc` runs in that library.
   → *Deploying from master rather than the worktree is what surfaced this.*
2. **Master moves constantly** — it advanced **5 times** during this session (102 commits, then 5, 55,
   and more). **Re-verify your commits are ancestors immediately before building/deploying**; I once
   started a deploy on a commit I had never tested.
3. **`git stash` hazard**: other projects' stashes live in this repo. Never `git stash pop` blindly.
4. **Pushing repeatedly cancels your own in-flight CI runs** (concurrency group).

---

## 6. Master was RED before this merge — and is green now

`Spe.Integration.Tests` asserted six endpoints auth-v4 had **deliberately deleted** (commit `c17e856f4`:
a per-resource auth requirement on collection routes, *"structurally unsatisfiable"*). auth-v4 updated
`EndpointGroupingTests.cs` but missed that project. Proven pre-existing by running them against pristine
master in a throwaway worktree.

**Two were passing vacuously**: they asserted a no-access user gets 403 while *every* user got 403. They
could not fail. Removed, with the reasoning recorded at the point of absence. Coverage is not lost —
auth-v4 added 65 tests under `tests/integration/seam/Auth/**`.

⚠️ **The auth-v4 merge also broke 66 SpeAdmin contract tests** — `DataverseWebApiClient` gained credential
selection in its ctor and now requires `TENANT_ID` when MI is disabled. Fixed by passing the
`UnusableCredential` each test already defines (takes the "selection bypassed" branch, and still throws if
anything genuinely asks for a token). **A clean textual merge is not a clean semantic one.**

---

## 7. Remaining work

| Order | Task | Note |
|---|---|---|
| 1 | **041** LiveIntegration suite + throwaway container | Provisions the throwaway container that makes 052 safe; 042 depends on it |
| 2 | **042** retire scaffolding tests | ⚠️ `UpdateContainerTypeSettingsTests.cs` flagged B16 — renaming 4 DTO props broke every test in it without one catching the defect |
| 3 | **051** quota ceiling · **050** archival · **052** item recycle bin | Real ceilings measured: **25 TiB** standard, **200 MiB** trial. 052 is irreversible-ops — throwaway container ONLY |
| 4 | **061** knowledge corpus · **062** billing handoff | |
| 5 | **090** wrap-up | 🔔 `/test-diet` is a BINDING gate |

### 🔴 Two gaps I would put AHEAD of parts of that list

1. **SpeAdminApp has NO test runner** — no vitest, no jest, and its `lint` script invokes an ESLint that
   is not installed. Every client-side assessment (billing, trial expiry, overridables parser, compliance
   module) is verified by **`tsc` alone**. Given this project keeps finding defects specifically in client
   logic — row selection that never worked, an empty billing badge, a deep link that went nowhere — this
   is the largest remaining blind spot.
2. **Deployment has no task in the WBS.** It rides 090 by default. Both surfaces are now deployed
   manually; if iterative deploys are wanted, that should be its own task.

### Smaller, recorded

- **027**: the owners list renders a **raw GUID** — Graph returns `grantedToV2.user` with only `id`, no
  name/email. Honest but not useful. Fixing = an N+1 `/users/{id}` resolve on a list capped at 3.
- **025**: settings **form** still bound to the Dataverse config record, not the Graph settings DTO.
- **026**: AC-2 stands independently of the 400 — `consumingTenantOverridables` is a **permission**, not a
  state, and an owning tenant structurally cannot read a consuming tenant's overrides.
- **021**: the `sprk_graphendpoint` Dataverse column still exists (schema change = operator action).
- **013**: Alerts still 403 — needs a **Defender workload**, not a permission. No grant can fix it.

---

## 8. Recurring defect shape — the through-line

*A lower layer collapses a real value (or a failure) into an absent/empty result that an upper layer reads
as benign.*

Found in: 003 (Dataverse outage → green dashboard) · 005 (audit table silently 0 rows) · 022 (`is string`
could never match Kiota's `DateTime`) · 024 (861 MB rendered as "0 B") · 029 (`billingStatus` in 0 files
repo-wide) · 030 (row selection never worked) · **028 (Graph itself does it — 200 plus a context header
claiming a field it omits)** · **the PATCH-400 (a documented requirement returned as a generic "arguments"
message)**.

**Verify a task's premise before implementing to it.** Nine POMLs, nine wrong-or-incomplete premises.
