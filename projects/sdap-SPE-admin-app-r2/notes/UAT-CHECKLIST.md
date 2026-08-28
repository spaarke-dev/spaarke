# UAT checklist — sdap-SPE-admin-app-r2

> **2026-08-28** · Deployed to **Spaarke Dev** (`https://spaarkedev1.crm.dynamics.com`) ·
> BFF `spaarke-bff-dev` · code page `sprk_speadmin`
> **This gates task 090.** Do not start the wrap-up until this passes.

---

## 0. Before you start

| | |
|---|---|
| **Environment** | Spaarke Dev · container type `Spaarke PAYGO 1` (`8a6ce34c-6055-4681-8f87-2f4f9f921c06`) |
| **Hard rule (NFR-07)** | Anything **destructive** — permanent delete, container delete — uses a **throwaway container you create for the test**. Never `Spaarke Inc` or any container holding real documents. |
| **Ctrl-F5** | Force-reload the code page; the browser caches the web resource aggressively |

**What "pass" means here**: the screen tells you the truth. A blank, a zero, or a confident default
where the platform reported nothing is a **failure**, even if it looks tidy.

---

## 1. 🔴 The two acceptance items that need a specific check

### 1.1 — Task 029 AC-1: does `billingStatus` actually render?

**Container Types → select a type → Settings tab → details block at the top.**

| Observe | Verdict |
|---|---|
| `Billing status` shows **valid** / **invalid** | ✅ AC-1 fully met |
| Shows **Unknown** on all four types | ⚠️ Correct NFR-06 behaviour, but AC-1 is met only degenerately — **record this**, don't wave it through. It means Graph omits the field for these types |
| Shows blank / em-dash with no label | ❌ Fail — absence must be named |

Also: a container type with **invalid** billing must show a **warning banner above the settings form**.

### 1.2 — Task 025 AC-2: do all nine settings persist?

**Container Types → select a type → Settings tab.**

1. Note which fields say **"Not reported"** (badge on switches, placeholder in inputs). That is Graph
   reporting nothing — **not** a bug.
2. Change **one** value (e.g. toggle *Enable Discoverability*, or set *URL Template*).
3. **Save.**
4. Expect: *"Settings accepted"* — the form **re-reads from Graph's response**, so what you see after
   the save is what Graph reported back, not what you typed.

| Observe | Verdict |
|---|---|
| The changed value persists after save and after a reload | ✅ |
| **502 Bad Gateway** naming `unwrittenFields` | ✅ **This is the system working.** Graph accepted and silently discarded — the app caught it instead of lying. Record which field |
| Value reverts silently with a success message | ❌ Fail — report immediately, this is the defect class the project exists to remove |

⚠️ **Do not** expect to set a per-container storage cap. The ceiling is **type-wide** by platform
design (FR-E02 amended) and the form says so on the control.

---

## 2. Task 052 — the item recycle bin (new)

**Containers → select a container → Recycle Bin tab.**

> This is the **per-container ITEM** bin (deleted files). It is *not* the top-level **Recycle Bin**
> screen (deleted containers). Spec D3 keeps both — confirming they stay distinct is itself a test.

| # | Step | Expected |
|---|---|---|
| 2.1 | Open the tab on a container with nothing deleted | **"The recycle bin is empty"** — a clear empty state, visibly different from an error |
| 2.2 | Delete a file via the Files tab, return to Recycle Bin, Refresh | The file appears with **Name · Deleted · Deleted by · Size · Deleted from** |
| 2.3 | Check **Deleted by** | A name (e.g. *SharePoint App*), or **"Not reported"** — never a blank cell |
| 2.4 | Select one item → **Restore** | Per-item outcome list appears naming the item. Item disappears from the bin and is back in Files |
| 2.5 | Select 2+ items → **Restore** | **Every requested item is named** with its own outcome — never a single "success" banner |
| 2.6 | Restore an item, then (without refreshing) restore it again | **409** — *"Nothing was restored … refresh and retry"*. Must NOT read as a partial success |
| 2.7 | Select an item → **Delete permanently** | Confirmation modal **names each item** and says it cannot be undone |
| 2.8 | Cancel the modal | Nothing is deleted |
| 2.9 | Confirm the delete (**throwaway container only**) | Per-item outcome list; item gone from the bin; count drops |

🔴 **2.7 is a hard gate.** If the confirmation shows only a count ("Delete 3 items?") and not the
names, that is a fail — an irreversible action must not ask you to trust your own memory of the
selection.

---

## 3. Task 050 — container archival

⚠️ **Blocked until the opt-in runs** (see `current-task.md` §2). Until then:

| Step | Expected while not opted in |
|---|---|
| Containers grid → **Archive** column | Renders; shows **"Not reported"** where Graph reports no archive state |
| Select a container → **Archive** | Confirmation modal, then **409** with the remediation naming `Set-SPOContainerTypeConfiguration` |

**After the opt-in**, re-run: Archive should return **202 + pending**, and the UI must say *pending* /
*recentlyArchived* — **never "Archived"**, because acceptance is not completion.

---

## 4. Task 051 — storage quota

**Containers → select a container → Details.**

| Observe | Expected |
|---|---|
| Storage Used / Storage Limit / Remaining | Populated from the container drive |
| Storage Limit caption | *"Set on the container type — applies to every container of this type"* |
| Remaining | Graph's own figure — it may **not** equal `limit − used` when a recycle bin is non-empty. That is correct, not a rounding bug |

---

## 5. Cross-cutting

| # | Check | Expected |
|---|---|---|
| 5.1 | **Dark mode** — toggle and revisit every new surface + both modals | Fluent v9 semantic tokens throughout; no unreadable text, no hard-coded colours (ADR-021) |
| 5.2 | **Two recycle bins stay distinct** | Top-level *Recycle Bin* = deleted **containers**; container tab = deleted **items**. Neither replaces the other |
| 5.3 | **Browser console** | No unhandled errors on any new surface |
| 5.4 | **Container status column** | Shows the real status, or **"Not reported"** — never a fabricated *active* (this was a live defect fixed in 050) |
| 5.5 | **Security screen** | Alerts + Secure Score render. If the app lacks the grant, expect an explicit **403 message naming `SecurityEvents.Read.All`** — never a silent "no alerts" |

---

## 6. Recording the result

For each failure capture: **screen · what you did · what it showed · what you expected**. A screenshot
of a wrong value is worth more than a description.

**When this passes**, task 090 is unblocked — it runs `/test-diet` (BINDING), which decides:
the ~104 classified scaffolding methods, the 20 `SecurityEndpointTests` (their replacement now
exists), `SearchItemsTests` (needs an offline Dataverse double or removal), and **DEF-001**.
