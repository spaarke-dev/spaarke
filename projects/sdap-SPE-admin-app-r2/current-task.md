# Current Task State — sdap-SPE-admin-app-r2

> **Last Updated**: 2026-10-07 (BFF deployed + verified; 042 closed; 050 re-probed)
> **Recovery**: read Quick Recovery, then **§0.5 (the MI fix)**, then §0, then §1. Everything else is reference.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **090 — wrap-up.** 🔲 **HELD by operator instruction** until all work is done AND UAT passes |
| **Status** | **SPE Admin is secret-free and LIVE in dev.** PR #1291 merged (`a5be02f0`); BFF deployed by the operator 2026-10-05 from master `2677d48c` — **verified in the running DLL** (new members present, `SpeAdminTokenProvider` absent, `/healthz` 200). SPE Admin page deployed 2026-10-05. Model 1 config secret field already blank |
| **Tasks** | **27 ✅ · 2 🔄 (029 = operator UAT render; 050 = platform-blocked, §9 of its findings) · 1 🔲 (090)** of 30. 042 closed — its last item was fixed upstream by uac-r2 #1312 |
| **Next Action** | **Operator**: (1) Consuming Tenants → grant `5967251e-…` `full`/`full` on Spaarke Model 1; (2) `SecurityEvents.Read.All` on `mi-bff-api-dev` (az command in the 2026-10-07 session reply: SP `9fd47efb…`, Graph SP `ba630d35…`, role `bf394140…`); (3) UAT incl. 029's billing render. **Then** 090 wrap-up |
| **Blocked?** | Nothing is code-blocked. Security **Alerts** will still fail after (2): Graph says the tenant "is not provisioned" for the Security API — a licensing condition, unchanged from the old identity |

### ✅ Starting a NEW / REMOTE session? Read this

| | |
|---|---|
| Branch `work/sdap-SPE-admin-app-r2` | `cee118e95` + this handoff · **1–2 ahead / ~1,049 behind** `origin/master` |
| Unmerged | **docs only** — the topology-doc corrections (§0.3) and this handoff. **No code is unmerged** |
| Open PRs | **0** — #859, #907, #918, #959 all MERGED |
| Uncommitted | none |

⚠️ **~1,049 behind master.** It moves very fast. **Merge master before any new PR** and rebuild —
the docs-only delta merges trivially, but a code change on a 1,000-commit-stale base will not.

---

## 0. What changed 2026-10-03/04 — READ THIS

### 0.5 ✅ SPE Admin made secret-free — the "MI issue" (2026-10-04, latest)

**Supersedes the "fix now / fix properly" lines in §0.2 and offer (c) in §0.4.** Full rationale:
[topology doc §6B](../../docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md).

- **Decision**: do NOT federate owning apps to the BFF's MI. Microsoft requires MI and app in the **same
  tenant** and allows **≤20 federated credentials per app** ⇒ 20-customer cap for Model 1, impossible for
  Model 2. Instead: container work runs as **the BFF's own app-only identity** (`IGraphClientFactory.ForApp()`
  — on Azure the UAMI `mi-bff-api-dev`, appId `5967251e…`, because `Graph__ManagedIdentity__Enabled=true`),
  with access from an **`applicationPermissionGrant` on the registration**. Grant management + Register run
  **delegated** as the signed-in admin (app-only `FileStorageContainerTypeReg.Selected` may only change
  registrations the caller OWNS; delegated `Manage.All` is consented on the BFF app).
- **Code**: `SpeAdminGraphService` — `GetClientForConfigAsync` → `ForApp()` + **fail-closed tenant guard**;
  consuming-tenant ops → `…ForUserAsync`; grant create = documented **PUT** `…/applicationPermissionGrants/{appId}`
  with **arrays** (old POST + first-element-as-string was the task-041 `apiNotFound`); update PATCHes whole
  lists; results read from Graph's response. `RegisterContainerTypeAsync` (SharePoint REST + owning-app
  secret) → `RegisterContainerTypeForUserAsync` (delegated grant; legacy names mapped, `AddAllPermissions`→`full`;
  `sharePointAdminUrl` ignored). **Deleted** `SpeAdminTokenProvider` (dead: only reachable via an uncalled
  method) + all Key Vault/secret plumbing + client caches. Config create no longer requires
  `keyVaultSecretName`; dashboard sync no longer skips configs without one; config form field optional.
- **Tests**: new `tests/integration/contract/SpeAdmin/SpeAdminIdentityAndGrantContractTests.cs` (18);
  `GraphWireMockFixture.StubPut`; 13 contract tests' constructors updated; live fixture uses a stated
  TEST-only owning-app credential; obsolete SharePoint-URL unit tests removed; `CredentialCensusTests` +
  `CredentialGuardTests` SpeAdmin rows **removed** (ratchet tightened). **SpeAdmin filter 310/310, ArchTests
  349/349**, BFF build 0/0, SPE Admin `vite build` OK.
- **Verified live (read-only probe)**: dev type `Spaarke PAYGO 1` already grants the BFF MI **`full`**
  (app + delegated) — dev loses nothing. MI app roles: has `FileStorageContainer.Selected` +
  `FileStorageContainerTypeReg.Selected`; **lacks `SecurityEvents.Read.All`** (owning app had it).

**🔔 Operator steps after deploy** (Entra admin actions — not done by Claude):

1. **Grant the BFF MI on Model 1**: SPE Admin → Container Types → Spaarke Model 1 → **Consuming Tenants → Add**:
   appId `5967251e-171c-46fe-a6c2-ef843c90309d`, application `full`, delegated `full`. Needs the SharePoint
   Embedded Administrator role. Up to 1 h to propagate.
2. **Grant `SecurityEvents.Read.All` (application) to `mi-bff-api-dev`** — or the Security tab will 403.
3. **Clear `null`** from the Model 1 config's Key Vault Secret Name (now optional and unused).
4. **Search**: the old owning app had `Files.ReadWrite.All`; the MI doesn't. Verify item search in UAT.

### 0.1 ✅ The container-type create fix is PROVEN

UAT §1A.2b was the one fix shipped as *"reasoned, not proven"* (create is delegated-only; app-only
probes get 403). The operator's live create returned:

```
speInvalidOperation: CreateContainerType(Spaarke Model 1,delegated):
  The owning app id is already used by another container type.
```

That error is **only reachable if `owningAppId` was sent, well-formed, and looked up by Graph** —
the previous error was the anonymous `invalidRequest: One of the provided arguments is not acceptable`.
**§1A.2b: PASS.** The new error itself is rule **R1** (one owning app ↔ one container type): the app
creates from the config's owning app `170c98e1`, which already owns `Spaarke PAYGO 1`.

### 0.2 🔴 Model 1 container type created — but its config is non-functional

- **Entra app**: `Spaarke SPE Model 1 Owner` · client id `bfac7f6e-9fa0-4664-8492-c7a1dfe73d5e` ·
  tenant `a221a95e-…` · `My organization only` · **0 secrets, 0 certs, 1 federated credential**
  (`sprk-controlplane-dev-uami-assertion` — trusts the **control-plane UAMI**, NOT the BFF).
  Granted: application `FileStorageContainer.Selected` + `FileStorageContainerTypeReg.Selected`.
  *Not granted*: the **delegated** `FileStorageContainer.Selected` (`085ca537-…`) — not needed for the
  owning-app role; grant for parity if anything will act delegated as this app.
- **Container type**: created via **SharePoint admin center → SharePoint Embedded → Apps → Create app**,
  billing type **Owner org** (= `standard`).
- **Dataverse config**: the operator entered the literal string **`null`** in *Key Vault Secret Name*
  (the field is required). 🔴 **This does not work.** `SpeAdminGraphService` authenticates as the owning
  app with `ClientSecretCredential` only (:5698, :5886) — the string `"null"` goes to Key Vault, the
  lookup fails, and **every app-only operation for that config fails**. Container-type ops (delegated)
  still work; **listing/creating containers (app-only) does not**.
- ~~Fix now: add a client secret…~~ / ~~Fix properly: FIC support…~~ — **SUPERSEDED by §0.5**: no secret
  and no federated credential is needed; grant the BFF managed identity on the Model 1 registration instead.

### 0.3 Topology doc corrected (commit `cee118e95`, NOT yet on master)

[`docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md`](../../docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md):

- **Inventory**: **four** container types already exist, not one — Spaarke Demo Documents (Owner org),
  Spaarke PAYGO 1 (Owner org), Spaarke DMS Dev 1 (**User org**), Spaarke DMS-SPE Trial (**trial**).
  **4 of 25 used**, and 🔴 **the one permitted trial slot is already taken.**
- **Admin-center create path is now PREFERRED** (delegated, sidesteps R5). Vocabulary:
  **Owner org = `standard`**, **User org = `directToCustomer`**.
- **§6A**: config resolution reads exactly 5 columns. **Storage & Sharing, Permissions, and Consuming
  App Registration on the config form are collected, stored, and NEVER READ.** "Consuming App
  Registration" is unwired Phase-3 scaffolding — leave it empty.
- **§6B**: owning-app credential is secret-only; a `null` placeholder fails silently-looking.

⚠️ The **published artifact** (<https://claude.ai/code/artifact/07b17fb1-a9d1-42bf-8165-758002704f43>)
is now **stale** against these corrections — refresh it from the markdown.

### 0.4 ⏳ Operator decisions pending (offered, not yet done)

| # | Offer | Notes |
|---|---|---|
| a | **Remove the inert config-form fields** | `src/solutions/SpeAdminApp/src/components/settings/ContainerTypeConfig.tsx` — keys `maxStoragePerBytes`, `sharingCapability`, `isItemVersioningEnabled`, `delegatedPermissions`, `applicationPermissions`, consuming-app pair. Leave the Dataverse columns. Needs `node_modules` restored first |
| b | **Create the Model 2 container type** | Name it **`Spaarke Model 2`** — ONE type for ALL Model 2 customers (R4), **not** per client. Needs a **multi-tenant** (`AzureADMultipleOrgs`) app registration; billing **User org**. Takes budget to 5 of 25 |
| c | ~~FIC support in `SpeAdminGraphService`~~ | **DONE differently — §0.5.** Federating owning apps doesn't scale (same-tenant + 20-FIC limits); SPE Admin now uses the BFF's own MI + registration grants |
| d | **Refresh the published artifact** | Stale — see §0.3 |

Also done by the operator: **deleted the duplicate `Paygo` config** (same container type + owning app
as `Spaarke PAYGO 1`). Verified first: no code references either config.

ℹ️ 2026-10-04: `src/solutions/SpeAdminApp/node_modules` **is now installed** (the SPE Admin code page
builds). Note `npx tsc --noEmit` reports **123 pre-existing errors** (shared libs + older screens; none from
§0.5) — `vite build` does not type-check, so the code page has never been tsc-clean.

⚠️ **If you use the LOCAL worktree**: `node_modules` was **absent everywhere** (0 directories). The
worktree was wiped and recreated 2026-08-31, and node_modules is gitignored. **Any client build fails
until** `npm install --legacy-peer-deps --no-audit --no-fund` (NOT `npm ci` — it fails on most
solutions here). Shared libs first (`Spaarke.UI.Components`, `Spaarke.Auth`), then the code pages.
The .NET side is fine.

ℹ️ VS Code may show root files with a red **`D`** badge in that worktree. **Cosmetic** — git is clean
(verified: 0 status entries, 19,057 tracked files all present). It is stale editor cache from the
midnight wipe; **Developer: Reload Window** clears it.

---

## 1. The live threads

### 1.1 ✅ UAT §1A.2b — create a container type — **PASSED 2026-10-03, see §0.1** (history below)

The operator has a container-type create ready to test. **This is the highest-value open item**, because
the fix behind it is **reasoned, not proven**.

**What was wrong** (UAT 2026-08-28): `invalidRequest: One of the provided arguments is not acceptable`
— an error naming no argument. Three defects on one path:

1. **`owningAppId` was never sent**, and Graph requires it (beta CSDL: `Nullable="false"`).
2. **The billing allow-list was `{standard, premium}`** — "premium" has never existed in Graph, and
   `trial` + `directToCustomer` were rejected by our own validator.
3. **An unparseable classification silently became `standard`** — and the classification is
   **permanent**, so that substitution was unrecoverable.

⚠️ **SCOPE OF PROOF.** Container-type create is **delegated-only**; an app-only token gets **403**, so
the failure is **unreachable from a probe** (recorded in `notes/probe_containertype_create.py`). The fix
rests on the Graph beta CSDL + Microsoft's documented body. **A delegated session is the only
verification.**

**If it still fails → the next hypothesis is a tenant/licensing precondition, NOT the payload.**
Capture the full message.

**Trial-type limits** (so a legitimate platform refusal isn't mistaken for our bug): one trial container
type per tenant · 5 containers · 1 GB each · 30 days · cannot be registered in another tenant.

### 1.2 ⏳ The rest of UAT

[`notes/UAT-CHECKLIST.md`](notes/UAT-CHECKLIST.md) §1A:

- **§1A.1 Security tab** — one "Secure Score" header (not two), donut chart, and the corrected
  access-denied message. Toggle **dark mode** — the donut arc uses Fluent palette tokens.
- **§1A.2 Add Property** — 🔴 **(c) is the one to watch**: add a second property and confirm the
  **first survives**. Graph merges partial writes; if the first disappears that is silent data loss.
- **§1A.3 `Add Permission`** — ⚠️ **SKIPPED, NOT PASSED.** The probe identity could not resolve a grant
  subject. **No evidence either way** — exercise it deliberately.

### 1.3 ⏳ Task 050 — archival probe, overdue

✅ **Re-run 2026-10-07** with `python projects/sdap-SPE-admin-app-r2/notes/probe050_archival.py` (re-created +
committed — the old `probe050_optedin.py` never existed in `notes/`; it was lost with a scratchpad). The 403
refusal is **gone**; archive now returns **503 `serviceNotAvailable`**. Platform-blocked — see
`notes/task-050-findings.md` §9. Provisions and tears down its own container.

The opt-in **is** set (`IsArchiveEnabled : True`) but Graph returned a byte-identical 403 naming
*"this **APPLICATION**"*, not the container type. 🔴 **Do NOT conclude an app-level capability from that
sentence** — reading a vendor error string as precise system state is how a nonexistent PowerShell
command got into five documents.

### 1.4 ⏳ `SearchItemsTests` — the one real test action

7 HTTP contract tests at `tests/unit/Sprk.Bff.Api.Tests/SpeAdmin/` — **not a KEEP path**. Content is
maintain-class; only the location is wrong. **But one method makes a real outbound Dataverse call**
(~100 s timeout, intermittently), so a plain `git mv` would relocate a network dependency **into** a
KEEP path — worse than leaving it. Needs an offline Dataverse double, or deletion of that one method.
**Not acceptable**: adding it to `tests/.reliability-registry.json` for retries.

---

## 2. What this session (2026-08-30/31) shipped

| | |
|---|---|
| **PR #859** | SPE Admin R2 code complete — merged |
| **PR #907** | `owningAppId` validation fix + test-diet report — merged |
| **PR #918** | r3 project seed — merged |

### Defects found and fixed

- 🔴 **5th fabrication defect — a fabricated CAUSE, not a value.** Security said *"most common cause is
  a missing SecurityEvents.Read.All grant"* on every denial, while Graph said **"Account is not
  provisioned"** and that grant was **already made**. `AccessDeniedSummary` now branches on Graph's own
  words and says **"cannot tell"** when the cause is genuinely ambiguous.
- 🔴 **6th — `Add Property` had NEVER worked.** PATCHed the *container* with a `{customProperties:{…}}`
  wrapper → `400 Unsupported request body property`. Belongs at
  `PATCH /containers/{id}/customProperties` with the map as the **body root**. Proven live, both shapes
  back to back. **Why it hid: reads use a different, valid shape** — a working read beside a broken
  write survives inspection indefinitely.
- 🔴 **7th/8th/9th** — the three container-type create defects (§1.1).
- 🔴 **10th, found by `/code-review` on my own changes** — the fix for §1.1 put GUID validation in the
  *endpoint*, but `CreateContainerTypeForConfigAsync` resolves `owningAppId` from a Dataverse **text**
  column and passed it to a bare `Guid.Parse`, throwing `FormatException` that its `ODataError`-only
  catch doesn't map → raw 500. **Validation now sits with the parse.** The same "error doesn't say
  which argument" failure, reintroduced one layer down by its own fix.

### Docs produced (all on master)

- [`docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md`](../../docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md)
  — five binding rules, app-registration topology, create + registration procedures.
  Artifact: <https://claude.ai/code/artifact/07b17fb1-a9d1-42bf-8165-758002704f43>
- [`notes/test-diet-report.md`](notes/test-diet-report.md) — the BINDING 090 gate, **already satisfied**.
- `projects/sdap-SPE-admin-app-r3/` — the decomposition project (§4).

---

## 3. `/test-diet` — DONE. Two corrections that change 090

**The gate is satisfied**; 090 can cite [`notes/test-diet-report.md`](notes/test-diet-report.md).

1. 🔴 **The old "~104 scaffolding methods held for /test-diet" claim is STALE.** All four files
   (`SecurityEndpointTests`, `ContainerTypeEndpointsTests`, `SpeAdminGraphServiceTests`,
   `ContainerEndpointsTests`) were **already deleted** by `ci-cd-unit-test-remediation-r1` and arrived
   via a master merge. **Do not carry that number into the wrap-up PR.**
2. ⚠️ **The skill's default scope is wrong for this branch.** `{start-commit}..HEAD` returns **354 test
   files**, because this branch merged master three times and that range sweeps in other projects'
   tests. Scoped against master the real delta is **6 files / 41 methods**, **zero scaffolding**.

---

## 4. Successor: `sdap-SPE-admin-app-r3` (seeded, not started)

`projects/sdap-SPE-admin-app-r3/` — decompose `SpeAdminGraphService.cs`. `/design-to-spec` **not run**.

**The god file grew during r2**: **4,320 → 6,545 lines (+52%)**, 168 public methods, 111 public async
across **nine** domains. r2's deferral pointed at `speadmingraphservice-decomposition-r1`, **which was
never created** — r3 is the correction.

🔴 **Operator constraint (binding)**: **CI must NOT gate on this file.** No LOC gate, no re-instated
`GodClassGuardTests`, no wiring `report-large-server-files.ps1` into CI. Verified: nothing gates on size
today, and the three ArchTests referencing the file check *content*, not size, and pass.

---

## 5. Recipes that earned their keep

- **Scope a project's test delta against MASTER**, not `start..HEAD` — see §3.2.
- **A working READ beside a broken WRITE is invisible.** Never let a read stand in for a write; they are
  frequently different URLs.
- **Probe BOTH shapes before declaring code broken.** When our payload 400'd, testing the alternative on
  the same container is what turned "something's wrong" into a specific, fixable defect.
- **Verify a deploy by reading bytes back from Dataverse** — the deploy script reported the same
  "2335 KB" for two different builds, so size proves nothing. `notes/verify_deployed_page.py`.
- 🔴 **`git stash pop` in a shared-`.git` worktree can pop ANOTHER project's stash.** A failed `cd` meant
  my stash never ran, but the `&&`-chained `pop` fired and applied another project's WIP here.
  **Never chain `stash`/`pop` behind a `cd`; check `git stash list` first.** For baselines prefer a
  throwaway worktree.
- ⚠️ **A throwaway-worktree baseline gives phantom ArchTest failures** — a fresh worktree has never built
  the provisioning DLLs those tests inspect. **Read the failure message before believing the count.**
- **`git diff --name-only A..B` is a two-dot DIFF, not a range.** Use `A...B`.
- **Emoji counting**: PowerShell chokes on surrogate pairs (`[char]0x1F501` throws). Use Python with
  `PYTHONIOENCODING=utf-8`. Counting the raw ✅ character over-counts — it appears in prose; enumerate
  table ROWS.
- **Live probing**: app-only as owning app `170c98e1` via `spe-owning-app-secret` in `sprk-prod-kv`.
  ⚠️ App-only gets **403 on all container-type endpoints** — delegated-only.
- **Throwaway teardown**: `DELETE /containers/{id}` **then** `DELETE /deletedContainers/{id}`. Both.

---

## 6. Reference

| Item | Where |
|---|---|
| **UAT checklist** (§1A is the live part) | [`notes/UAT-CHECKLIST.md`](notes/UAT-CHECKLIST.md) |
| Test diet report (090's gate) | [`notes/test-diet-report.md`](notes/test-diet-report.md) |
| Container-type topology | [`docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md`](../../docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md) |
| Task 050 archival + still-403 | [`notes/task-050-findings.md`](notes/task-050-findings.md) §8 |
| Task 052 recycle bin | [`notes/task-052-findings.md`](notes/task-052-findings.md) §5 |
| Probes (reproducible) | `notes/probe_add_paths.py` · `notes/probe_customprops_shape.py` · `notes/probe_containertype_create.py` |

**Known, not in the backlog**: client typecheck 124-error pre-existing baseline · I2 cross-tenant search
bleed (waived, not fixed) · container-type DELETE does not exist · **21 stray `check-*`/`search-*`/
`read-logs*` debug scripts committed at repo ROOT** (replicate into ~80 worktrees; `check-deadletter.ps1`
is broken — undefined `$deadletterqueue`, and greps a log path that doesn't exist).
