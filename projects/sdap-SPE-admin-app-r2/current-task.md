# Current Task State — sdap-SPE-admin-app-r2

> **Last Updated**: 2026-08-31 (by `context-handoff`)
> **Recovery**: read Quick Recovery, then §1 (the live threads). Everything else is reference.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **090 — wrap-up.** 🔲 **HELD by operator instruction** until all work is done AND UAT passes |
| **Status** | **All code complete, merged to master, and deployed.** Nothing is unmerged |
| **Tasks** | **26 ✅ · 3 🔄 (029, 042, 050) · 1 🔲 (090)** of 30 — enumerated from TASK-INDEX rows, not from memory |
| **Next Action** | **UAT §1A.2b — create a container type.** The operator has a test ready. This is the ONLY way to verify the create fix (see §1.1) |
| **Blocked?** | Nothing is code-blocked. Every open thread waits on the operator or on elapsed time |

### ✅ Starting a NEW / REMOTE session? Read this

**You do NOT need the local worktree.** The branch is **0 commits ahead of master** — every line of this
project's work is on `origin/master`. A fresh clone of master has all of it.

| | |
|---|---|
| Branch `work/sdap-SPE-admin-app-r2` | `7a2727620` · **0 ahead / 235 behind** `origin/master` |
| Open PRs | **0** — #859, #907, #918 all MERGED 2026-08-30/31 |
| Uncommitted | none |

⚠️ **235 behind.** Master moves fast (other worktrees merge constantly). **Re-merge master before any
new PR** and re-run the build — do not trust a day-old sync.

⚠️ **If you use the LOCAL worktree**: `node_modules` is **absent everywhere** (0 directories). The
worktree was wiped and recreated 2026-08-31, and node_modules is gitignored. **Any client build fails
until** `npm install --legacy-peer-deps --no-audit --no-fund` (NOT `npm ci` — it fails on most
solutions here). Shared libs first (`Spaarke.UI.Components`, `Spaarke.Auth`), then the code pages.
The .NET side is fine.

ℹ️ VS Code may show root files with a red **`D`** badge in that worktree. **Cosmetic** — git is clean
(verified: 0 status entries, 19,057 tracked files all present). It is stale editor cache from the
midnight wipe; **Developer: Reload Window** clears it.

---

## 1. The live threads

### 1.1 🔴 UAT §1A.2b — create a container type (THE NEXT ACTION)

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

`python scratchpad/probe050_optedin.py` (also `notes/probe050_optedin.py`). The 24 h replication retry
was due **2026-08-29** and has not been run. Provisions and tears down its own container.

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
