# Current Task State — sdap-SPE-admin-app-r2

> **Last Updated**: 2026-08-27 (by `task-execute` Step 8.5 checkpoint)
> **Recovery**: read Quick Recovery, then §1 (in-flight work) and §2 (the operator item).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **052 — item recycle bin** (FR-E03). 🔄 **IN PROGRESS** |
| **Step** | 5 of 11 — BFF server layer DONE + building; next is contract tests |
| **Status** | Branch merged from master (96 commits, clean, 0 conflicts). Build **0 errors** |
| **Next Action** | Write `tests/integration/contract/SpeAdmin/SpeAdminRecycleBinItemContractTests.cs` — WireMock cases per §1.3 |
| **Rigor** | FULL · sonnet-tier @ high · steps `directional` |

### Recent commits
| Commit | What |
|---|---|
| (uncommitted) | 052 server layer — service + endpoints |
| `5ca4ae79a` | checkpoint — master CI green |
| `c12fbeaf6` | **PR #842 merged** — 041/042/050/051 + 052 discovery |

---

## 1. ▶ IN FLIGHT: task 052

### 1.1 What is DONE (builds clean, not yet tested)

| Layer | Added |
|---|---|
| `SpeAdminGraphService.cs` | `SpeRecycleBinItem`, `SpeRecycleBinItemOutcome`, `SpeRecycleBinRestoreResult`, `SpeRecycleBinDeleteResult`, `RecycleBinRestoreRejectedException`; `ListRecycleBinItemsAsync` / `RestoreRecycleBinItemsAsync` / `PermanentDeleteRecycleBinItemsAsync` + 3 `…ForConfigAsync` wrappers; helpers `RecycleBinItemsUrl`, `TryMapItemNamesAsync`, `ReadReturnedIds`, `ParseRecycleBinItem` |
| `Api/SpeAdmin/RecycleBinEndpoints.cs` | 3 routes under the existing `/api/spe` group + 5 DTOs + 3 shared helpers |

**No `Program.cs` change and no new DI registration** — `MapRecycleBinEndpoints` was already wired
(`Api/SpeAdminEndpoints.cs:50`). Clean §10 outcome. **No new NuGet.**

### 1.2 Design decisions worth keeping

1. **Raw JSON via `SendGraphJsonAsync` for all three ops**, not Kiota request builders. The beta
   actions force it anyway, and it **dissolves** discovery trap #3 — `deletedBy` never becomes an
   `UntypedObject` because we parse the response ourselves. Better than writing a third reader.
2. **Restore → 200 only when ALL restored; 207 otherwise.** Graph's 207 lists only successes, so
   partial failure = `requested − returned`. Never collapse.
3. **Restore rejection → 409 Conflict**, not 400. Well-formed request; stale client view; atomic, so
   nothing was restored. Carries `remediation` + `requestedIds` + `graphMessage`.
4. **Delete never trusts the 204.** Lists the bin BEFORE and AFTER and diffs. The before-list is what
   separates "purged by us" from "was never here" — without it a never-present id reports as purged.
5. **Unverified delete → 207 with `verified: false`**, NOT 5xx. The delete WAS issued and data may be
   gone; an error status would imply nothing happened. 207 + explicit flag asserts nothing unestablished.
6. **Batch cap 200 ids** — a deliberate guard on an irreversible op.

### 1.3 Remaining steps

| # | Work |
|---|---|
| 5 | Contract tests (WireMock): 207-all, 207-partial, 400-rejected, delete-204-that-purged-nothing, delete-unverified, empty-bin, `deletedBy` mapping |
| 6 | Client: recycle-bin **items** surface distinct from deleted-CONTAINERS; per-item outcomes; ADR-050 `ConfirmModal` naming what is destroyed |
| 7 | `dotnet test` + client typecheck/build |
| 8 | Live verify on a **throwaway** container (NFR-07) — upload → delete → list → restore some → purge others |
| 9 | Step 9.5 gates (`code-review` + `adr-check`); publish size; TASK-INDEX ✅; notes |

### 1.4 Traps still live
- Live-fixture uploads go through `/drives/{driveId}/root:/{name}:/content`.
  `/containers/{id}/drive/root:/…` answers `400 "API not found"`.
- `restore`/`delete` are **beta-only**; knowledge corpus wrongly cites v1.0 — needs the same
  correction task 050 made for archival.
- Keep the two recycle bins distinct (spec D3).

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
Update-Module Microsoft.Online.SharePoint.PowerShell      # need >= 16.0.27515.12000
Connect-SPOService -Url https://spaarkedev1-admin.sharepoint.com
Set-SPOContainerTypeConfiguration -ContainerTypeId 8a6ce34c-6055-4681-8f87-2f4f9f921c06 -IsArchiveEnabled $true
```

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

## 7. Wave state

| Task | Status |
|---|---|
| 041, 042, 051 | ✅ |
| 050 | 🔄 — code shipped; pending the operator opt-in (§2) |
| **052** | 🔄 **IN PROGRESS** — server layer done, tests + client remain |
| 060, 061, 062 | 🔲 |
| **090** | 🔲 `/test-diet` BINDING gate; also decides **DEF-001** and re-examines every `// AMBIGUOUS (task 042):` marker |
| 025, 026, 029 | 🔄 **PARTIAL, not open** — do not restart |

**Not in the POML backlog**: the client typecheck+vitest gap · I2 cross-tenant search bleed (waived on
the deployment, not fixed) · container-type DELETE does not exist · Security-endpoint contract coverage
(no test exists anywhere — escalated in 042).
