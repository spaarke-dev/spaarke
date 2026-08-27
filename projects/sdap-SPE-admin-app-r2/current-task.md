# Current Task State — sdap-SPE-admin-app-r2

> **Last Updated**: 2026-08-27 (by `context-handoff`)
> **Recovery**: read Quick Recovery, then §1 (the next task) and §2 (the one thing needing the operator).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **052 — item recycle bin** (FR-E03). Discovery complete, **implementation not started** |
| **Phase** | Wave W17 — Workstream E |
| **Status** | Tree clean. Branch is **identical to origin/master** (0 ahead / 0 behind) — everything through 052-discovery is merged |
| **Next Action** | Implement 052 from [`notes/task-052-findings.md`](notes/task-052-findings.md) **§3**. **Read §2 of that file first** — restore and delete fail in opposite ways |
| **Blocking?** | No. One operator item waits (§2) but does not block 052 |

### Recent commits (all merged to master)
| Commit | What |
|---|---|
| `30e6fd9cf` | branch tip == master tip |
| `c12fbeaf6` | **PR #842 merged** — tasks 041/042/050/051 + 052 discovery |
| `9c39444a2` | 052 discovery — measured recycle-bin semantics |
| `e831eb269` | 051 — storage quota (FR-E02, amended) |
| `e10de0811` | ISS-002 + DEF-001 filed |
| `71a49e739` | 050 — container archival + fabricated-status fix |

### Critical context
Tasks 050 and 051 shipped; **both escalation triggers fired and both were answered.** 052's discovery is
done and measured live — the spec's 207 premise is half wrong (§1). Nothing is uncommitted.

---

## 1. ▶ NEXT: task 052 — implement from measured semantics

Everything below is **measured live** on throwaway containers (files uploaded → deleted → probed →
containers torn down 204/204, NFR-07). Full detail + implementation plan:
[`notes/task-052-findings.md`](notes/task-052-findings.md).

### 🔴 The finding that shapes the work: restore and delete fail in OPPOSITE ways

| | all ids valid | any id invalid | body |
|---|---|---|---|
| **`restore`** | **207** | **400 `badArgument`** — nothing restored, **atomic** | ids that **SUCCEEDED** |
| **`delete`** (permanent) | **204** | **204 — and it purges the valid ones anyway**, non-atomic | **none** |

- Spec FR-E03's *"207 partial success, per-item outcomes"* is **half right**. Restore's 207 lists only
  the ids that worked — partial failure is expressed by **absence** (`requested − returned`). There is
  no per-item error object. Treating 207 as success hides the items that did not restore.
- 🔴 **Permanent delete has no 207 and no per-item reporting at all** — 204 whether it purged
  everything, some, or nothing. For an **irreversible** operation that is the worst reporting shape
  found in this project. **Re-list and diff; never trust the 204** (same discipline task 051 applied to
  the quota write).

### Four traps to carry in
1. **Do not treat 207 as success.** Diff requested vs returned ids.
2. **Do not trust delete's 204.** Re-list and diff.
3. **`deletedBy` and `title` are OpenType extras absent from the CSDL** → arrive via `AdditionalData`
   → `deletedBy` will be an **`UntypedObject`**. Third time this project has had to measure that shape
   (022 `deletedDateTime`, 050 `archivalDetails`). Copy the reader pattern from `ReadArchiveStatus`.
4. **Live-fixture uploads** go through `/drives/{driveId}/root:/{name}:/content`.
   `/containers/{id}/drive/root:/…` answers `400 "API not found"`.

⚠️ `restore`/`delete` are **beta-only** (no recycleBin actions in the v1.0 CSDL). The knowledge corpus
wrongly cites v1.0 — needs the same correction task 050 made for archival. No ADR issue; the container
surface is already beta-pinned by task 020.

⚠️ **Keep the two recycle bins distinct** (spec D3): deleted-CONTAINERS (task 022, shipped) vs
per-container deleted-ITEMS (this task).

---

## 2. 🔔 The one item needing the operator — task 050's archival opt-in

050 shipped but **FR-E01 acceptance criteria 1 and 2 are NOT met**, so 050 is 🔄 not ✅.

```
POST /beta/storage/fileStorage/containers/{id}/archive
  → 403 notAllowed: "Archival operation cannot proceed because this
                     application does not currently support archiving."
```

Semantic, not routing — the beta action exists and is reachable; the container type has not opted in.

**To finish the verification** (operator action — tenant-level change to a **shared** container type):
```powershell
Update-Module Microsoft.Online.SharePoint.PowerShell      # need >= 16.0.27515.12000
Connect-SPOService -Url https://spaarkedev1-admin.sharepoint.com
Set-SPOContainerTypeConfiguration -ContainerTypeId 8a6ce34c-6055-4681-8f87-2f4f9f921c06 -IsArchiveEnabled $true
```

⚠️ **`Set-SPOContainerType -IsArchiveEnabled` does not exist** — that parameter is on
`Set-SPOContainerTypeConfiguration`. All 5 repo docs corrected by task 050.

⚠️ **Watch item**: `archivalDetails` has **never been seen on the wire**, even with an explicit
`$select` that `@odata.context` echoes. If it is still absent after a successful archive, the property
is unserved and the grid must source archive state from the action outcome +
`Get-SPOContainer -ArchiveStatus` instead. The code isolates this in one mapper.

---

## 3. What shipped (042 / 050 / 051) — and the three defects fixed

| Task | Outcome |
|---|---|
| **042** | SpeAdmin tests **722 → 207 cases**, 0 skipped. Keepers **relocated, not deleted** |
| **050** | Container archival — archive/restore, archive state in grid, ADR-050 confirmation, 16 contract tests |
| **051** | Storage quota (FR-E02 **amended**) — type-scope ceiling + per-container read-only quota, 8 contract tests |

**Three defects found and fixed:**

1. **`status` fabricated as `"active"` for 100% of responses.** It is a *typed* SDK property, so the
   `AdditionalData` lookup behind all four mapping sites could never match — the `: "active"` fallback
   fired every time, including GET and CREATE where Graph really returns the value. Client had a second
   `?? "active"`. Now nullable → "Not reported".
2. **A CATASTROPHIC secret guard was dead, not passing** (`CosmosProvisioningSecretGuardTests`) —
   repaired; now reports 8 findings in another project's code. **Filed as ISS-002 / [#839](https://github.com/spaarke-dev/spaarke/issues/839).**
3. **Two documented PowerShell remediations were wrong** — corrected across 5 docs.

**FR-E02 was amended** (operator chose option A): Graph has **no per-container ceiling**, and
`PATCH /containers/{id}` carrying it returns **200 while silently discarding the value**. The task's own
*"confirm by read-back, not by a 200"* constraint is what caught it.

### Open filings
| ID | What | Where |
|---|---|---|
| **ISS-002** | 5 ArchTest findings incl. the dead secret guard | [#839](https://github.com/spaarke-dev/spaarke/issues/839) → `customer-provisioning-orchestration-r1` |
| **DEF-001** | 3 owning-app methods with **zero callers**, still DI-registered and shipped | `notes/cross-project-handoffs.md` — §11 delete-or-document decision by task 090 |

---

## 4. ⚠️ Repo/CI health — worth raising, not caused by this project

- **Branch protection on master is DISABLED.** The `merge-to-master` skill documents it as protected
  since 2026-06-02; it is not. Consequence: `gh pr merge --auto` had no required checks to gate on, so
  **PR #842 merged instantly rather than on CI-green**. The merge is backed by local verification
  (build 0/0 · 10,683 tests · ArchTests 112/117 with the same 5 pre-existing), not by CI.
- **Four consecutive master SDAP CI runs were cancelled** (20:10 → 20:52), so **no completed Code
  Quality / ADR-Violations verdict on master since 17:55** across PRs #842, #841, #838, #840 —
  including #840, a `!` breaking change touching 41 identity sites.
- **PR #841 is the fix** (*"key router concurrency on sha for master"*) and is now on master; the run at
  `30e6fd9cf` is the first under it.
- ✅ **#840 verified locally against this work**: merged clean, build 0/0, **10,683 tests pass**,
  ArchTests **112 pass / 5 fail** (117 total — #840 added 6 new `CallerIdentityGuardTests`, all
  passing). The semantic risk flagged for `SpeAdminTenantScope.cs` did **not** materialise.

---

## 5. Verification recipes worth reusing

- **Prove a failure is pre-existing**: `git stash -u` → run → `git stash pop`. Also used for the client
  typecheck baseline (124 errors — compare counts, not impressions).
- **Read Graph's own CSDL before believing a doc**: `curl https://graph.microsoft.com/{v1.0,beta}/$metadata`,
  no token. Settled the archival version question (050) and the quota-scope question (051) definitively.
- **Reflect a PowerShell module rather than trusting its docs**: `Save-Module` to scratch →
  `Assembly.LoadFrom` → enumerate `CmdletAttribute` types → read parameters. Catch
  `ReflectionTypeLoadException` and use `.Types` (missing deps are normal). This is what proved
  `Set-SPOContainerType -IsArchiveEnabled` does not exist.
- **`gh pr view --json files` caps at 100 files.** Use
  `gh api --paginate repos/{owner}/{repo}/pulls/{n}/files` — two PRs (177 and 348 files) were silently
  truncated during conflict-check.
- **Test a "does the API reject X" hypothesis with a WELL-FORMED invalid value.** A malformed id gets
  rejected on format and looks identical to rejection on existence — this nearly produced the wrong
  conclusion about 052's 207.
- **Live probing needs no new setup**: app-only token as owning app `170c98e1` via the
  `spe-owning-app-secret` in `sprk-prod-kv`. Working probe scripts are in the session scratchpad
  (`probe_archive.py`, `probe051.py`, `probe052*.py`) — the recipe is in `notes/live-verification-credential.md`.

---

## 6. Orchestration lessons (preserved)

1. `parallel-safe: true` describes the work, not the bookkeeping.
2. Agents given explicit standing to push back will — one correctly refused a deletion I ordered.
3. **A CI observation and a `git push` cannot be interleaved.** Compounded this session: master merges
   cancel each other's runs.
4. Stale POML `<status>`/`<deps>` have misled four times. `TASK-INDEX.md` is authoritative.
5. Partition agent file-sets disjointly and forbid them running `dotnet build`.
6. **The POML constraints earned their keep twice** — 051's *"confirm by read-back, not by a 200"* and
   050's escalation trigger both caught real platform defects. Read `<constraints>` before `<steps>`.
7. **Bash working directory persists across calls** and drifts into the scratchpad after probe runs —
   prefix with `cd /c/code_files/spaarke-wt-sdap-SPE-admin-app-r2` when it matters.

---

## 7. Wave state

| Task | Status |
|---|---|
| 041, 042, 050, 051 | ✅ / 🔄 complete — see §3 (050 is 🔄 pending the operator opt-in) |
| **052** | ▶ **NEXT** — discovery done, implementation not started |
| 060, 061, 062 | 🔲 |
| **090** | 🔲 `/test-diet` is a BINDING gate; also decides **DEF-001** and re-examines every `// AMBIGUOUS (task 042):` marker |
| 025, 026, 029 | 🔄 **PARTIAL, not open** — do not restart |

**Not in the POML backlog**: the client typecheck+vitest gap · I2 cross-tenant search bleed (waived on
the deployment, not fixed) · container-type DELETE does not exist · Security-endpoint contract coverage
(no test exists anywhere — escalated in 042).

✅ **Closed this session**: the `communications`/`emails`/`exports` folder origin — `communications`
(2026-03-11) and `emails` (2026-01-13) were created by **"SharePoint App"**, Spaarke's own app-only
identity; `exports` (2026-03-22) by the operator interactively. Nothing foreign. The
throwaway-container rule stands on its other two reasons (repeatability, shared tenant).
