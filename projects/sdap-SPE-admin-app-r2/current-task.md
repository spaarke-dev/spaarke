# Current Task State — sdap-SPE-admin-app-r2

> **Last Updated**: 2026-08-28 (by `context-handoff`)
> **Recovery**: read Quick Recovery, then §1 (the two live threads). Everything else is reference.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **090 — wrap-up.** 🔲 **HELD by operator instruction** until all work is done AND UAT passes |
| **Status** | **All code complete and DEPLOYED to Spaarke Dev.** Tree clean, branch pushed, PR [#859](https://github.com/spaarke-dev/spaarke/pull/859) open |
| **Tasks** | **26 ✅ · 3 🔄 (029, 042, 050) · 1 🔲 (090)** of 30 — enumerated from TASK-INDEX, not from memory |
| **Next Action** | **(a)** Re-run `scratchpad/probe050_optedin.py` on/after **2026-08-29** — the 24 h replication retry (§1.1). **(b)** Operator runs [`notes/UAT-CHECKLIST.md`](notes/UAT-CHECKLIST.md) (§1.2) |
| **Blocked?** | Nothing is code-blocked. Both open threads are *waiting*, not stuck |
| **Rigor** | 090 is TEST-MODIFYING → quality gates run **unconditionally** when it runs |

### Deployed right now (2026-08-28)

| Surface | State |
|---|---|
| BFF `spaarke-bff-dev` | 45.12 MB, **SHA-256 hash-verified on-server**, `/healthz` **200**, CORS OK |
| New 052 routes | **401** (registered + protected); a fake route still **404**, proving the 401s are real registrations and not a blanket auth wall |
| Code page `sprk_speadmin` | Published, 2335 KB, byte-identical to the artifact whose strings I verified |

### ⚠️ Repo state
Branch is **13 ahead / 13 behind** `origin/master` — master moved again after this session's merge.
**Merge master before the 090 PR**, then re-run the ArchTest baseline.

---

## 1. The two live threads

### 1.1 🔴 Task 050 — the opt-in IS set, and Graph still refuses

The operator ran the corrected command against the right tenant and it took:
`IsArchiveEnabled : True`, confirmed on an independent `Get-SPOContainerTypeConfiguration`.

**Graph is unchanged.** Probed 2026-08-28 on a fresh throwaway container:

```
POST /beta/storage/fileStorage/containers/{id}/archive
  → 403 notAllowed: "Archival operation cannot proceed because this
                     application does not currently support archiving."
```

Byte-identical to the pre-opt-in response.

**The wording is the clue, and it is not the one we assumed.** It names *"this **APPLICATION**"* — the
owning app `170c98e1` — **not** the container type. Task 050's original reading ("the container type
has not opted in") is now **unproven**.

| # | Hypothesis | Test |
|---|---|---|
| **1** ⭐ | **Replication lag** — SPE container-type settings propagate up to **24 h**. Spec FR-C08 exists for exactly this | Re-probe on/after 2026-08-29 |
| **2** | A separate **application-level** capability | Only if (1) is ruled out |

🔴 **Do NOT conclude (2) from the sentence alone.** Reading a vendor error string as a precise
statement of system state is how `Set-SPOContainerType -IsArchiveEnabled` — a command that does not
exist — got into five documents.

**Re-run**: `python scratchpad/probe050_optedin.py` — provisions and tears down its own container and
checks archive, `archivalDetails`, and unarchive end to end.

⚠️ **`archivalDetails` has still never been seen on the wire.** An active container returns exactly
`containerTypeId, createdDateTime, description, displayName, id, lockState, ownershipType, settings,
status`. If it stays absent after a successful archive, the grid must source archive state from the
**action outcome**, not the property — isolated in one mapper (`ReadArchiveStatus`).

### 1.2 ⏳ UAT — the gate on 090

[`notes/UAT-CHECKLIST.md`](notes/UAT-CHECKLIST.md). Two items are acceptance criteria, not polish:

- **029 AC-1** — does Spaarke Dev actually return `billingStatus`? All-"Unknown" is correct NFR-06
  behaviour but satisfies AC-1 only degenerately; **record it either way**.
- **025 AC-2** — do all nine settings persist? A **502 naming `unwrittenFields` is a PASS** — that is
  the read-back verification catching Graph accepting and silently discarding.

Section 3 (archival) is still testable today: while Graph refuses, Archive must produce a **409 with
remediation**, not a crash or a false success.

---

## 2. What this session shipped

| Task | Outcome |
|---|---|
| **052** ✅ | Per-container **item** recycle bin (FR-E03). Live-verified 18/18 on a throwaway container |
| **025** ✅ | The deferred **settings form** — all nine settings render, bound to Graph |
| **026** ✅ | Complete **as amended** — cross-tenant override display dropped (operator decision) |
| **042** 🔄 | Security escalation **resolved** (11 contract tests); ~104 scaffolding methods held for `/test-diet` |

### Four decisions worth keeping

1. **Restore and permanent delete fail in OPPOSITE ways.** Restore → 207 listing only successes,
   atomic on rejection; delete → 204 regardless of what it did, non-atomic. So delete **lists the bin
   BEFORE and AFTER and diffs**. The *before* list is load-bearing: without it an id that was never in
   the bin reads as "purged" — a fabricated success on an irreversible operation.
2. **Unverifiable delete → 207 + `verified:false`**, never 5xx. The delete *was* issued and data may
   be gone; an error status asserts the opposite unestablished thing.
3. **Graph's error CODE for a rejected restore is NOT stable** — `badArgument` and `invalidRequest`
   hours apart for the identical condition. The detector keys on the **400 status**; the contract test
   is a `[Theory]` over both payloads.
4. **`undefined` stays `undefined` in the settings form.** `<Switch checked={undefined}>` renders
   identically to `false`, so unreported settings get a "Not reported" badge **and are omitted from
   the save**. An unknown must not become a write.

### Defects found in passing

- 🔴 **4th fabrication defect** — `extractSettingsFromConfig` invented every missing settings value
  (`?? "disabled"`, `?? false`, `?? 100`, `?? 1 GB`, `isSearchEnabled: true` hard-coded). A container
  type with search **off** showed the switch **on**. Fixed.
- 🔴 **Caught before shipping** — `setContainerType(updated)` after a save would have blanked
  `owningAppId`/`expiryDateTime`/`region`, making a *successful save* look like data loss. Merged instead.
- 🔴 **Stale caveat** — 025's note said *"every PATCH returns 400, nothing is writable"*, superseded
  2026-08-25 (`etag` is a REQUIRED **body** property; 023 proved the write live 499→499). Two notes
  disagreed for two days. **A stale caveat is indistinguishable from a live blocker.**
- 🔴 **My own error** — I derived the SPO admin URL from the **Dataverse** org name. The SharePoint
  tenant is **`spaarke`**, not `spaarkedev1`; verified from a container's drive `webUrl`. Corrected in
  both docs with an inline note. ⚠️ Containers therefore live on the **production** SharePoint tenant.
- ⚠️ **`SearchItemsTests`** makes a **real outbound Dataverse call from `tests/unit/**`** and timed out
  after ~100 s having passed twice the same session. Stash-proven pre-existing. For `/test-diet` the
  choice is an offline Dataverse double or removal — **not** merely tightening the assertion.
- ⚠️ SPO exposes **`CopilotEmbeddedChatHosts`** — so FR-C07's `agent.chatEmbedAllowedHosts` concept is
  real but **PowerShell-only**, not a Graph settings property. FR-C07 looked in the wrong API.

---

## 3. Held for 090's `/test-diet` (BINDING) — do not action early

~104 classified scaffolding methods across 6 files · the 20 `SecurityEndpointTests` (replacement now
exists, so the hold reason is gone) · `SearchItemsTests` · **DEF-001** (3 owning-app methods with zero
callers, still DI-registered) · every `// AMBIGUOUS (task 042):` marker.

**ISS-002** ([#839](https://github.com/spaarke-dev/spaarke/issues/839)) — PR **#847** is another session acting on it.

---

## 4. Recipes that earned their keep

- **Prove a failure is pre-existing**: `git stash -u` → run → `git stash pop`. Used for both the
  ArchTest baseline and `SearchItemsTests`.
- **Read Graph's own CSDL before believing a doc**: `curl https://graph.microsoft.com/{v1.0,beta}/$metadata`, no token.
- **Publish size is measured COMPRESSED.** Uncompressed is ~138 MB and looks catastrophic;
  `Compress-Archive -CompressionLevel Optimal` reproduces the ~45 MB gated figure.
- **Don't derive one system's hostname from another's.** Read it off the wire.
- **`gh pr view --json files` caps at 100** — use `gh api --paginate .../pulls/{n}/files`.
- **Emoji `grep` on TASK-INDEX silently returns nothing** — the shell mangles it, and an empty result
  reads exactly like "no tasks remain". Enumerate with Python + `PYTHONIOENCODING=utf-8`. This produced
  a wrong "all tasks complete" reading once already.
- **Live probing**: app-only as owning app `170c98e1` via `spe-owning-app-secret` in `sprk-prod-kv`.
  Recipe in [`notes/live-verification-credential.md`](notes/live-verification-credential.md).
- **Throwaway teardown uses `DELETE /storage/fileStorage/deletedContainers/{id}`** — an earlier probe
  used a `permanentDelete` action, got 400, and leaked a container.
- **Verify a client build actually contains the change**: `grep` the built bundle for a known string
  **and** a negative control for a string that should be gone.

---

## 5. Reference

| Item | Where |
|---|---|
| Task 052 full record | [`notes/task-052-findings.md`](notes/task-052-findings.md) §5 |
| Task 050 archival + the still-403 result | [`notes/task-050-findings.md`](notes/task-050-findings.md) §8 |
| Task 025 form + stale-caveat correction | [`notes/task-025-schema-verification.md`](notes/task-025-schema-verification.md) §6 |
| Task 026 escalation + amendment | [`notes/task-026-findings.md`](notes/task-026-findings.md) §6 |
| Test retirement + Security resolution | [`notes/test-retirement-inventory.md`](notes/test-retirement-inventory.md) |
| **UAT checklist** | [`notes/UAT-CHECKLIST.md`](notes/UAT-CHECKLIST.md) |

**Not in the POML backlog**: the client typecheck+vitest gap (124-error pre-existing baseline) ·
I2 cross-tenant search bleed (waived on the deployment, not fixed) · container-type DELETE does not exist.
