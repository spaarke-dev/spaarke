# Task 023 — settings PATCH property names and the ceiling/consumption split

> **Task 023** (spec FR-C04 + FR-C05) · 2026-08-24 · **complete**
> Escalation trigger evaluated and did **NOT** fire — no write returned 200-without-persisting after
> the fix, because the writes are now correctly shaped. (Live read-back still outstanding — §6.)

---

## 1. Premise check — the POML's names were RIGHT. Its diagnosis was incomplete.

**First time a POML's specific claim has held.** `itemMajorVersionLimit` and
`maxStoragePerContainerInBytes` are exactly right.

Verified **authoritatively without a live call**, by reflecting over the installed SDK — the constraint
said "re-verify rather than trusting this text", and `Microsoft.Graph` 6.5.0 types the whole settings
model, which is a stronger source than documentation:

```
=== Microsoft.Graph.Models.FileStorageContainerTypeSettings ===
   ConsumingTenantOverridables · IsDiscoverabilityEnabled · IsItemVersioningEnabled
   IsSearchEnabled · IsSharingRestricted · ItemMajorVersionLimit
   MaxStoragePerContainerInBytes · SharingCapability · UrlTemplate
```

But fixing those two names would **not** have made a single settings write work.

---

## 2. 🔴 The write path was broken at three independent points

Any one of them alone was sufficient to make every write a silent no-op.

| # | Break | Effect |
|---|---|---|
| **1** | **Wrong shape.** The service wrote settings as **top-level** properties on the container type. They are a **nested `settings` object**. Graph ignores unknown top-level members on a merge-PATCH. | 200, nothing changed |
| **2** | **Wrong names, server side.** `majorVersionLimit`, `storageUsedInBytes`, `isVersioningEnabled` — none exists on the resource. | 200, nothing changed |
| **3** | **Name mismatch at the DTO boundary.** The **client already sent the correct names** `isItemVersioningEnabled` and `itemMajorVersionLimit`; the server DTO declared `isVersioningEnabled` and `majorVersionLimit`. JSON binding matched neither, so both arrived **null** — read as "no change". The client also sent `maxStoragePerBytes` (the Dataverse column name leaking onto the wire) against a DTO expecting `storageUsedInBytes`. | value discarded before reaching the service |

Break 3 is the one nobody could see from either side alone: the client looks right, the service looks
wrong, and the DTO between them silently drops the value. Even a perfect service fix would have written
nulls.

### The semantic error underneath

`storageUsedInBytes` is a consumption **metric** on a *container*. `maxStoragePerContainerInBytes` is a
quota **ceiling** on a *container type*. Different concept, different resource. Modelling a limit as a
measurement is why the storage story never cohered (spec §3.2), and it is the confusion task 024
inherits on the consumption side.

---

## 3. 🔴 Fourth defect, not in the POML — and it had test coverage protecting it

```csharp
public static readonly IReadOnlySet<string> ValidSharingCapabilities =
    new HashSet<string>(...) { "disabled", "view", "edit", "full" };
```

**Three of those four are not Graph values.** The real set is the members of
`Microsoft.Graph.Models.SharingCapabilities`: `Disabled`, `ExternalUserSharingOnly`,
`ExistingExternalUserSharingOnly`, `ExternalUserAndGuestSharing` — which is **exactly what the SPE Admin
client has always sent** (`types/spe.ts`).

This set is the **endpoint's validation allow-list**. So every value the client could send except
`disabled` was rejected with a **400 by our own validator**, before the request reached Graph. Sharing
capability could not be changed to anything else, and the failure was reported as the caller's fault.

**Why it survived: 10 tests asserted the wrong values were correct.**

```
ValidSharingCapabilities_ContainsAllAllowedValues(capability: "view")   ❌
ValidSharingCapabilities_IsCaseInsensitive(capability: "EDIT")          ❌
SharingCapability_ValidValues_PassValidation(capability: "full")        ❌
```

Anyone who corrected the list would have "broken tests", which made the wrong values look load-bearing.
The tests did not just fail to catch the defect — **they defended it**.

Corrected, plus the three retired names added as explicit *negative* cases so re-adding any of them
fails. The set is now **derived from the SDK enum** rather than hand-listed, so it cannot drift again.

---

## 4. The fix — typed settings, so the compiler owns the names

```csharp
var settings = new FileStorageContainerTypeSettings { ItemMajorVersionLimit = …, … };
var patchBody = new FileStorageContainerType { Settings = settings };
```

Every property name is now **compiler-enforced**. A misspelled setting is a build error, not a 200 that
does nothing — this defect class cannot recur on this surface.

`sharingCapability` is parsed to the enum and **rejected here if unrecognised**, rather than forwarded:
Graph's response to an unparseable enum is not reliably distinguishable from success, which is the
failure mode this task exists to remove. `UnknownFutureValue` is excluded — it is Kiota's
forward-compatibility sentinel, not a settable value.

Renamed through the whole chain: service parameters → `UpdateContainerTypeSettingsRequest` →
endpoint validation and forwarding → the client's request payload. A ceiling is never called usage at
any hop.

**Also added (AC-6):** a non-positive storage ceiling is now rejected with a real validation error.
Sending `0` would either be ignored or applied as "no storage", and both outcomes look identical to the
caller once the PATCH returns 200.

---

## 5. Tests

| Test file | Change |
|---|---|
| `SpeAdminContainerTypeSettingsPatchTests.cs` **(new, 9 tests)** | Body **shape** (nesting — the load-bearing one), real property names, **retired names absent from the raw JSON anywhere**, the allow-list is Graph's values, every client-sendable value is accepted, an unknown value never reaches Graph, and unset values are **omitted** rather than sent as explicit nulls (a null in merge-PATCH means *clear*, which would wipe settings the caller never mentioned). |
| `UpdateContainerTypeSettingsTests.cs` | 10 sharing-capability cases corrected (§3); retired names added as negatives. **Header flags the file for task 042** — its property assertions are ADR-038 §7 **B16 scaffolding**, and this task is the evidence: renaming four DTO properties broke every one of them without a single one having detected the defect being fixed. Cost without protection. |
| `Phase2IntegrationTests.cs` | Mechanical rename only. |

---

## 6. Gates

- **BFF build ✅** — 0 errors, 7 pre-existing warnings.
- **Tests ✅** — **10,665 passing** (+12), 0 failed, 97 skipped. **ArchTests 36/36.**
- **Publish ✅** — **43.67 MB compressed incl. PDBs, 0 MB delta**. Ceiling 60 MB. No package change.
- **Code page ✅** — `vite build`, 13.90 s. 0 type errors in the touched files.

**Placement justification (root CLAUDE.md §10):** no new endpoint, service, DI registration, or
package. One rebuilt method body, four renamed DTO properties, one added validation branch.

⚠️ **AC-2 not live-verified.** "Set a value, read it back from the live tenant" requires an
interactive Azure login this session cannot perform. The three structural breaks are each pinned by
WireMock against the real SDK serializer — which is where all three lived — but a live write→read-back
remains outstanding, alongside the standing UI-verification gap (001 / 003 / 012 / 021 / 022 / 030).

---

## 7. Handoff

| Item | Owner |
|---|---|
| **`isSearchEnabled` is sent by the client and silently discarded** — no such property on the settings request DTO. One of the five remaining settings. | **025** (FR-C07) |
| **4 more `CreatedDateTime ?? DateTimeOffset.UtcNow`** at `:693`, `:1031`, `:1115`, `:1165` — the *containers* surface. A container of unknown age renders as created today. These are the same four methods 024 already edits for `StorageUsedInBytes`. | **024** |
| The remaining `AdditionalData` fallbacks — this task removed the container-type settings ones entirely by moving to typed properties; the audit handed over by task 030 §7 now covers a smaller surface. | **024 / 025** |
| `azureTenantId` declared in `types/spe.ts` with no Graph source | **025** |
| `UpdateContainerTypeSettingsTests.cs` B16 scaffolding, with this task as documented evidence | **042** |
