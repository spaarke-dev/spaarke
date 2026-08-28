# Current Task State — sdap-SPE-admin-app-r2

> **Last Updated**: 2026-08-27 (by `task-execute` Step 8.5 checkpoint)
> **Recovery**: read Quick Recovery, then §1 (in-flight work) and §2 (the operator item).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **090 — wrap-up.** 🔲 **HELD by operator instruction**: do not start until all work is done AND UAT has passed |
| **Phase** | Project close (gated) |
| **Status** | **All code complete + DEPLOYED to Spaarke Dev.** BFF `spaarke-bff-dev` (45.12 MB, SHA-256 verified, healthz 200, 3 new routes 401-not-404) · code page `sprk_speadmin` published (2335 KB). PR [#859](https://github.com/spaarke-dev/spaarke/pull/859). **Awaiting UAT** |
| **Next Action** | **Run [`notes/UAT-CHECKLIST.md`](notes/UAT-CHECKLIST.md)** + the 050 operator action (§2). 090 stays held until UAT passes |
| **Rigor** | 090 is TEST-MODIFYING → quality gates run **unconditionally** when it does run |

### 🔔 §0. What is actually left

**27 of 30 tasks ✅ · 2 🔄 (029 UAT-only, 050 operator) · 1 🔲 (090, held).** All code work is done.

#### (a) ✅ ANSWERED — task 026 AC-2 dropped (operator, 2026-08-27)

Cross-tenant override display is **dropped, not deferred** — it is not something the platform can do.
Spec FR-C08 amended. **No code change was needed**: the shipped UI already renders the overridable
*permission* list and states plainly that overrides applied in another tenant are not visible here.

#### (b) ONE operator action — task 050's archival opt-in (§2)

#### (c) UAT items — none are blockers, all need a live session

| Item | What it needs |
|---|---|
| **029 AC-1** | Confirm Spaarke Dev's container types actually return `billingStatus`. Container types are **delegated-only**, so this needs an interactive device-code sign-in — a few minutes, not a permission gap. If Graph omits it, all four render "Unknown", which is correct NFR-06 behaviour but satisfies AC-1 only degenerately |
| **025 AC-2** | One settings save against Spaarke Dev to confirm each of the nine persists. 051's read-back verification would *report* a silent discard as 502 + `unwrittenFields` |
| **052 / 050 / 051** | UI walkthrough of the new surfaces (recycle-bin tab, archive column, quota rows, settings form) |

#### (d) Held for 090's `/test-diet` — do not action early

~104 scaffolding methods across 6 files (classified, not removed); the 20 `SecurityEndpointTests`
(their replacement now exists, so the hold reason is gone); and `SearchItemsTests`, which needs an
offline Dataverse double or removal — see §4.

### Recent commits
| Commit | What |
|---|---|
| `a55a147e5` | **025 settings form + 042 Security contract coverage** |
| `2c8b7dbdb` | checkpoint — 052 complete |
| `a3a897ba2` | **052 — item recycle bin (FR-E03)**, live-verified |
| `c12fbeaf6` | **PR #842 merged** — 041/042/050/051 + 052 discovery |

⚠️ **`grep` for `🔲`/`🔄` in this repo's TASK-INDEX silently returns nothing** — the shell mangles the
emoji and the empty result reads exactly like "no tasks remain". Enumerate with Python + explicit
`encoding="utf-8"` and `PYTHONIOENCODING=utf-8`. This produced a wrong "all tasks complete" reading
once already; it is the same one-observation-cached-as-truth failure the project exists to remove.

---

## 1. ✅ Task 052 — complete and live-verified

Full record: [`notes/task-052-findings.md`](notes/task-052-findings.md) §5. Design decisions worth
carrying: restore and permanent delete fail in OPPOSITE ways, so delete re-lists the bin and diffs
BEFORE and AFTER rather than trusting its 204; an unverifiable re-read returns 207 + `verified:false`,
never a 5xx; and Graph's error CODE for a rejected restore is not stable, so the detector keys on the
400 STATUS.

---

## 2. 🔔 The one item needing the operator — task 050's archival opt-in

050 shipped but **FR-E01 acceptance criteria 1 and 2 are NOT met**, so 050 is 🔄 not ✅.

```
POST /beta/storage/fileStorage/containers/{id}/archive
  → 403 notAllowed: "Archival operation cannot proceed because this
                     application does not currently support archiving."
```

Semantic, not routing — the action exists and is reachable; the container type has not opted in.

```powershell
# 1. Uninstall the MSI "SharePoint Online Management Shell" (Apps & Features) — see blocker below
# 2. Then, in Windows PowerShell 5.1:
Install-Module Microsoft.Online.SharePoint.PowerShell -Force -Scope CurrentUser -AllowClobber
Connect-SPOService -Url https://spaarke-admin.sharepoint.com     # NOT spaarkedev1-admin — the SharePoint tenant is
#                                                        # `spaarke`, verified from a container's drive
#                                                        # webUrl (https://spaarke.sharepoint.com/...).
#                                                        # The Dataverse org name (spaarkedev1) and the
#                                                        # SharePoint tenant name are different things.
Set-SPOContainerTypeConfiguration -ContainerTypeId 8a6ce34c-6055-4681-8f87-2f4f9f921c06 -IsArchiveEnabled $true
```

### 🔴 Attempted 2026-08-27 — got partway, then hit two hard blockers

**Progress made (this de-risks the operator's run):**
- Installed **16.0.27612.12000** from PSGallery — comfortably above the 16.0.27515.12000 floor.
- **Confirmed the version floor is real.** Loaded the currently-active **16.0.26413.0** in PS 5.1:
  `Set-SPOContainerTypeConfiguration` exists but `HasIsArchiveEnabled: False`. Task 050's finding is
  now verified from two directions — the parameter is absent from `Set-SPOContainerType` in every
  version *and* absent from `Set-SPOContainerTypeConfiguration` below the floor.

**Blocker 1 — the new module will not load in either host.**
`16.0.26413.0` is **MSI-installed** at `C:\Program Files\SharePoint Online Management Shell\` and its
`Microsoft.SharePoint.Client.dll` wins the assembly load, so importing 27612 fails with:

```
Could not load type 'Microsoft.SharePoint.Client.Sharing.MainLinkAudience'
from assembly 'Microsoft.SharePoint.Client, Version=16.0.0.0, …'
```

Identical failure in PS 7 and in PS 5.1, and it persists with the Program Files entry stripped from
`PSModulePath` — so it is assembly resolution, not module discovery. **Fix: uninstall the MSI**
("SharePoint Online Management Shell" in Apps & Features). That is an admin action on the operator's
machine and is not something this session should force.

⚠️ Also note the gallery install landed under **PowerShell 7's** user path
(`Documents\PowerShell\Modules`), not 5.1's (`Documents\WindowsPowerShell\Modules`). The classic SPO
module is .NET Framework and only runs under **5.1**, so after the MSI is gone the install must be
re-run *from `powershell.exe`*, not `pwsh`.

**Blocker 2 — `Connect-SPOService` requires interactive sign-in.** There is no app-only or
service-principal path for it, and MFA rules out `-Credential`. Even with the module fixed, this step
needs a human at a browser. **This was always an operator action; the module work above just removes
the surprises from it.**

⚠️ **`Set-SPOContainerType -IsArchiveEnabled` does not exist** — that parameter is on
`Set-SPOContainerTypeConfiguration`. All 5 repo docs corrected by task 050.

⚠️ **Watch item**: `archivalDetails` has **never been seen on the wire**. If still absent after a
successful archive, the grid must source archive state from the action outcome +
`Get-SPOContainer -ArchiveStatus`. The code isolates this in one mapper.

---

## 3. What shipped earlier (042 / 050 / 051)

| Task | Outcome |
|---|---|
| **042** | SpeAdmin tests **722 → 207 cases**, 0 skipped. Keepers relocated, not deleted |
| **050** | Container archival + the fabricated-status fix; 16 contract tests |
| **051** | Storage quota (FR-E02 **amended**) — type-scope ceiling + per-container reporting; 8 contract tests |

**Three defects fixed**: `status` fabricated as `"active"` for 100% of responses; a CATASTROPHIC
secret guard that was dead rather than passing (→ **ISS-002 / [#839](https://github.com/spaarke-dev/spaarke/issues/839)**); two wrong documented
PowerShell remediations (corrected across 5 docs).

### Open filings
| ID | What | Where |
|---|---|---|
| **ISS-002** | 5 ArchTest findings incl. the dead secret guard | [#839](https://github.com/spaarke-dev/spaarke/issues/839) → **PR #847 is now acting on this** |
| **DEF-001** | 3 owning-app methods with zero callers, still DI-registered | `notes/cross-project-handoffs.md` — §11 decision by task 090 |

---

## 4. Repo/CI health

- ✅ **Master CI green** at `30e6fd9cf` — Code Quality **passed** (first completed verdict since
  17:55), confirming the 5 pre-existing ArchTest failures do not fail it. `ADR Violations Report`
  shows `skipped` (conditional, not run on master pushes). PR #841's concurrency fix works.
- ⚠️ **Branch protection on master is DISABLED** despite `merge-to-master` documenting it as
  protected since 2026-06-02. `gh pr merge --auto` therefore had no required checks and **PR #842
  merged instantly rather than on CI-green**. Retroactively covered by the green run above.
- **PR #847** (`fix/archtest-guard-adjudication`) is another session acting on ISS-002. No file
  overlap with 052 — verified via `gh api --paginate`.

---

## 5. Verification recipes worth reusing

- **Prove a failure is pre-existing**: `git stash -u` → run → `git stash pop`.
- **Read Graph's own CSDL before believing a doc**: `curl https://graph.microsoft.com/{v1.0,beta}/$metadata`, no token.
- **Reflect a PowerShell module rather than trusting its docs**: `Save-Module` → `Assembly.LoadFrom`
  → enumerate `CmdletAttribute` types. Catch `ReflectionTypeLoadException`, use `.Types`.
- **`gh pr view --json files` caps at 100 files.** Use `gh api --paginate repos/{owner}/{repo}/pulls/{n}/files`.
- **Test a "does the API reject X" hypothesis with a WELL-FORMED invalid value.** A malformed id is
  rejected on format and looks identical to rejection on existence.
- **Live probing needs no new setup**: app-only token as owning app `170c98e1` via
  `spe-owning-app-secret` in `sprk-prod-kv`. Recipe in `notes/live-verification-credential.md`.

---

## 6. Orchestration lessons (preserved)

1. `parallel-safe: true` describes the work, not the bookkeeping.
2. Agents given explicit standing to push back will.
3. **A CI observation and a `git push` cannot be interleaved.**
4. Stale POML `<status>`/`<deps>` have misled four times. `TASK-INDEX.md` is authoritative.
5. Partition agent file-sets disjointly and forbid them running `dotnet build`.
6. **The POML constraints earned their keep twice** — 051's *"confirm by read-back, not by a 200"*
   and 050's escalation trigger both caught real platform defects. Read `<constraints>` before `<steps>`.
7. **Bash working directory persists across calls** and drifts into the scratchpad after probe runs —
   prefix with `cd /c/code_files/spaarke-wt-sdap-SPE-admin-app-r2` when it matters.

---

## 7. Wave state — enumerated from TASK-INDEX 2026-08-27 (30 rows)

**27 ✅ · 2 🔄 · 1 🔲 (held)**

| Task | Status |
|---|---|
| 041, 051, **052**, **025**, 060, 061, 062 + 19 others | ✅ |
| 050 | 🔄 — code shipped + contract-tested; **pending the operator opt-in (§2)** |
| 026 | ✅ **as AMENDED** — operator dropped cross-tenant override display (not a thing the platform can do). Spec FR-C08 amended; no code change was needed, the shipped UI already states the limit |
| 042 | 🔄 — Security escalation **resolved**; ~104 scaffolding methods held for `/test-diet` at 090 |
| 029 | ⏳ **UAT only** — code complete; AC-1's live render needs a device-code session (§0c) |
| **090** | 🔲 **HELD** by operator instruction until all work is done and UAT passed. `/test-diet` BINDING gate; also decides **DEF-001** and every `// AMBIGUOUS (task 042):` marker |

⚠️ My previous handoff listed 060/061/062 as 🔲. **They were already ✅** — stale, exactly the failure
lesson #4 records. TASK-INDEX is authoritative; re-enumerate rather than trusting a prior summary.

**Not in the POML backlog**: the client typecheck+vitest gap · I2 cross-tenant search bleed (waived on
the deployment, not fixed) · container-type DELETE does not exist · Security-endpoint contract coverage
(no test exists anywhere — escalated in 042).
