# Task 050 — container archival: what the platform actually does

> **2026-08-27** · Spec FR-E01 · All findings measured live against Spaarke Dev (`spaarkedev1`),
> app-only as the owning app `170c98e1`, plus Graph CSDL (`$metadata`, no token) and reflection over
> the shipping SharePoint Online PowerShell module.
> **No secret or token value appears in this file.**

---

## 1. 🔴 The documented PowerShell remediation does not exist

Every source in this repo says the container-type archival opt-in is:

```powershell
Set-SPOContainerType -IsArchiveEnabled     # ❌ NO SUCH PARAMETER, in any module version
```

It appears in the task 050 POML (`<background>` + constraint + AC-4), spec **FR-E01**, `design.md`
§4.3, and `knowledge/sharepoint-embedded/docs/learn-containers.md`. All of them trace to the same
MC1215074 summary, which the corpus itself flags as *"Not empirically verified by any Spaarke
project."*

**Measured by reflecting the cmdlet types out of the module assembly:**

| Cmdlet | Carries `IsArchiveEnabled`? |
|---|---|
| `Set-SPOContainerType` | ❌ **No** — params are `ContainerTypeId, ContainerTypeName, AzureSubscriptionId, ResourceGroup, ApplicationRedirectUrl` only |
| **`Set-SPOContainerTypeConfiguration`** | ✅ **Yes** — `System.Nullable<Boolean>` |
| `New-SPOContainerType` | ✅ Yes (set at creation) |

**The correct remediation:**

```powershell
Set-SPOContainerTypeConfiguration -ContainerTypeId <guid> -IsArchiveEnabled $true
```

`Nullable<bool>`, not a `SwitchParameter` — so `$true` is required; a bare `-IsArchiveEnabled` will
not bind.

### ⚠️ It also requires a module upgrade

| | Version |
|---|---|
| Installed on this machine | `16.0.26413.0` — **has no archive parameter anywhere** |
| PSGallery latest (published 2026-08-03) | `16.0.27515.12000` — has it |

So an admin on the current module gets `A parameter cannot be found that matches parameter name
'IsArchiveEnabled'` even when using the *right* cmdlet. The remediation message must name the module
floor, or it sends the admin to a dead end one step later.

### Why this matters more than a doc typo

AC-4 requires the not-opted-in failure to produce *"a message naming the PowerShell remediation, not a
generic error."* Following the POML literally would have shipped an error message instructing an
administrator to run a command that does not exist — **an error message naming the wrong cause, which
is the exact defect class §2.4 charters this project to remove**, reproduced inside the feature meant
to remove it. A generic error would have been *less* harmful than the specific-but-wrong one.

Also found (useful, not used here): `Get-SPOContainer -ArchiveStatus` reads archive state outside
Graph, and the module's own enum carries a **`NotArchived`** member that Graph's CSDL does not.

---

## 2. The Graph surface — archive is beta-only, and that is consistent with what we already do

Read from Graph's own CSDL, both versions, no token required.

| | v1.0 | beta |
|---|---|---|
| `archive` action on `fileStorageContainer` | ❌ absent | ✅ present |
| `unarchive` action | ❌ absent | ✅ present |
| `archivalDetails` property | ❌ absent | ✅ present (`siteArchivalDetails`) |
| `restore`, `activate`, `permanentDelete`, `lock`, `unlock` | ✅ | ✅ |

**No ADR conflict, and no §6.5 escalation.** Task 020 did not do a blanket `/beta` → `/v1.0`
migration — it made a *per-surface measured* decision, and pinned the **container** surface to beta
because `storageUsedInBytes` does not exist in v1.0 at all. `SpeAdminGraphService.SpeContainerGraphBaseUrl`
is `https://graph.microsoft.com/beta`, guarded by
`SpeAdminGraphVersionContractTests.ContainerOperations_UseBeta_BecauseV1DoesNotDefineStorageUsedInBytes`.
Archival therefore lands on a surface **already** on beta by a documented, tested decision. Nothing new
is being risked.

⚠️ **Do not read "GA February 2026" as "in Graph v1.0."** The GA refers to the capability (admin
centre + PowerShell). The Graph API for it is beta-only as of 2026-08-27.

### `restore` ≠ `unarchive`

Both exist on v1.0/beta and they are unrelated:

- `restore` — recovers a **soft-deleted** container from `deletedContainers`. Already implemented as
  `RestoreContainerAsync`.
- `unarchive` — returns an **archived** (not deleted) container to active. Beta-only. New here.

Naming the new methods `RestoreContainer*` would have collided with a real, different, already-shipped
operation. They are named `ArchiveContainerAsync` / `UnarchiveContainerAsync`.

---

## 3. 🔴 Archive/unarchive are asynchronous — `siteArchiveStatus` has no terminal "done" on the way in

```
siteArchiveStatus = { recentlyArchived, fullyArchived, reactivating, unknownFutureValue }
```

Identical enum on v1.0 and beta. Three consequences the UI must respect:

1. **Archiving is a transition**, `recentlyArchived` → `fullyArchived`. Not a flag flip.
2. **Unarchiving is a transition**, `reactivating` → (property disappears). The container is *not*
   usable the instant the call returns 202/204.
3. **There is no `notArchived` member.** A non-archived container is expressed by the *absence* of
   `archivalDetails`, not by a value.

Reporting "Restored ✅" the moment `unarchive` returns would be this project's signature defect —
success reported for something still in flight. The UI renders `reactivating` as its own state.

---

## 4. 🔴 `status` is silently dropped on LIST — and the code fabricates `"active"` for every row

Measured on the containers LIST, beta, app-only:

| Request | `status` | `archivalDetails` |
|---|---|---|
| LIST `$select=id,displayName,status,archivalDetails` | ❌ **omitted** (200; rows carry only `id`, `displayName`) | ❌ omitted |
| LIST no `$select` | ❌ absent (rows carry `containerTypeId, createdDateTime, displayName, id, ownershipType, settings, storageUsedInBytes`) | ❌ absent |
| GET-single `$select=…` | ✅ `"active"` | ❌ omitted — **though `@odata.context` echoes it** |
| GET-single no `$select` | ✅ `"active"` | ❌ absent |

This is the task-028 `$expand=drive` shape exactly: request accepted, **200**, `@odata.context` echoes
the requested field, body omits it. Silently.

### The live defect this exposes

`SpeAdminGraphService.cs:746`, inside `ListContainersAsync`:

```csharp
var status = container.AdditionalData?.TryGetValue("status", out var s) == true && s is string ss ? ss : "active";
```

Graph **never** returns `status` on a LIST. So the `: "active"` fallback fires for **100% of rows**,
and the Containers grid's Status column has been asserting "active" for every container regardless of
truth since it was written. Absent collapsed into a benign-looking value — §2.4, shipping.

**Fixed in this task** because the archival state renders in the same column and would have inherited
the same lie: `SpeContainerSummary.Status` is now `string?`, LIST leaves it **null**, and the grid
renders null as an explicit "—" with a tooltip, never as "Active".

⚠️ **Do not "fix" the null by adding `status` to the LIST `$select`.** It is already there, and Graph
drops it. Verified 2026-08-27. Same trap as `WebUrl`.

---

## 5. Live probe — the opt-in is genuinely off, and the error is specific

Provisioned a throwaway container per **NFR-07** (`ZZ-Task050-ArchivalProbe-…`), activated it, probed,
then tore it down. Not run against any pre-existing container.

```
POST /beta/storage/fileStorage/containers            → 201  (status: inactive)
POST /beta/storage/fileStorage/containers/{id}/activate → 204
GET  /beta/storage/fileStorage/containers/{id}       → 200  status=active, archivalDetails absent
POST /beta/storage/fileStorage/containers/{id}/archive  → 403
     notAllowed: "Archival operation cannot proceed because this application
                  does not currently support archiving."
DELETE …/containers/{id}                             → 204
DELETE …/deletedContainers/{id}                      → 204   (teardown verified)
```

**The 403 is semantic, not routing.** A missing route returns 404 naming the resource; this named the
*capability*. So the beta `archive` action is live and reachable on this tenant — only the container
type has not opted in. That distinction is what makes the feature safe to ship unverified: the code
path reaches Graph and Graph answers meaningfully.

`notAllowed` + that message is the string the not-opted-in branch keys on (§6).

---

## 6. What was built

| Layer | Change |
|---|---|
| `SpeAdminGraphService` | `ArchiveContainerAsync`, `UnarchiveContainerAsync` via `SendGraphJsonAsync` (SDK 6.5.0 models v1.0 and does not generate the beta actions); `ArchiveStatus` read into `SpeContainerSummary`; `Status` made nullable |
| `ContainerEndpoints` | `POST /api/spe/containers/{id}/archive`, `POST /api/spe/containers/{id}/unarchive` on the existing `/api/spe` group + existing `SpeAdminAuthorizationFilter` |
| Not-opted-in | Graph `notAllowed` → **409** ProblemDetails naming `Set-SPOContainerTypeConfiguration … -IsArchiveEnabled $true` **and** the module floor |
| Client | Archive/Restore commands, archive state in the grid, `ConfirmModal` stating the content-availability consequence |
| Tests | WireMock request-shape + error-translation coverage |

Detection is by Graph's `code` (`notAllowed`) with the message as a secondary signal — keying on
English message text alone would break the moment Microsoft rewords it.

---

## 7. ⛔ What is NOT verified, and cannot be from here

**AC-1 and AC-2 (archive succeeds / restore returns to active) are NOT live-verified.** The container
type has not opted in, and the opt-in is an operator action:

1. It is a **tenant-level change to a shared container type** (`Spaarke PAYGO 1`) that other projects
   and sessions use — not a local change.
2. It requires the SPO module upgrade in §1.
3. Per this project's live-tenant rules and root CLAUDE.md, an outward-facing irreversible-ish change
   to shared infrastructure is the operator's call, not an agent's.

The POML's `<escalation><trigger>` is written for exactly this and **has fired**. Everything not
gated on it was completed.

### To finish the verification

```powershell
Update-Module Microsoft.Online.SharePoint.PowerShell      # need >= 16.0.27515.12000
Connect-SPOService -Url https://spaarke-admin.sharepoint.com     # NOT spaarkedev1-admin — the SharePoint tenant is
#                                                        # `spaarke`, verified from a container's drive
#                                                        # webUrl (https://spaarke.sharepoint.com/...).
#                                                        # The Dataverse org name (spaarkedev1) and the
#                                                        # SharePoint tenant name are different things.
Set-SPOContainerTypeConfiguration -ContainerTypeId 8a6ce34c-6055-4681-8f87-2f4f9f921c06 -IsArchiveEnabled $true
Get-SPOContainerTypeConfiguration -ContainerTypeId 8a6ce34c-6055-4681-8f87-2f4f9f921c06   # confirm
```

Then re-run `scratchpad/probe2.py`-equivalent, or drive Archive from the UI against a throwaway
container. Expect `recentlyArchived`, and expect `archivalDetails` to begin appearing on GET-single.

⚠️ **The one thing to watch**: if `archivalDetails` *still* does not appear on GET-single after a
successful archive, then the property is unserved on this tenant despite being in the CSDL — the
`webUrl` situation again — and the grid must show archive state from the action outcome plus
`Get-SPOContainer -ArchiveStatus`, not from `archivalDetails`. The code isolates this in one mapper.
