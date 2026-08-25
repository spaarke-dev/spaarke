# Task 025 — the nine settings, verified against Graph's own metadata

> **Task 025** (spec FR-C07) · 2026-08-24 · AC-5 (schema verification) satisfied here.
> Source: `GET https://graph.microsoft.com/{v1.0,beta}/$metadata`, the OData CSDL Microsoft publishes —
> a stronger authority than documentation prose, and it needs no token.

---

## 1. The real surface

**`ComplexType Name="fileStorageContainerTypeSettings"`**

| # | Property | Type | v1.0 | beta |
|---|---|---|---|---|
| 1 | `consumingTenantOverridables` | `fileStorageContainerTypeSettingsOverride` (enum flags) | ✅ | ✅ |
| 2 | `isDiscoverabilityEnabled` | `Edm.Boolean` | ✅ | ✅ |
| 3 | `isItemVersioningEnabled` | `Edm.Boolean` | ✅ | ✅ |
| 4 | `isSearchEnabled` | `Edm.Boolean` | ✅ | ✅ |
| 5 | `isSharingRestricted` | `Edm.Boolean` | ✅ | ✅ |
| 6 | `itemMajorVersionLimit` | **`Edm.Int64`** | ✅ | ✅ |
| 7 | `maxStoragePerContainerInBytes` | **`Edm.Int64`** | ✅ | ✅ |
| 8 | `sharingCapability` | `sharingCapabilities` (enum) | ✅ | ✅ |
| 9 | `urlTemplate` | `Edm.String` | ✅ | ✅ |
| — | `isOfficeRestricted` | `Edm.Boolean` | ❌ | ✅ **beta-only** |

**v1.0 has exactly nine.** The count in FR-C07 is right. The *list* is not.

---

## 2. 🔴 The POML swapped a real property for a fictional one

> *"The v1.0 shape is: urlTemplate, isDiscoverabilityEnabled, isSearchEnabled, isItemVersioningEnabled,
> itemMajorVersionLimit, maxStoragePerContainerInBytes, isSharingRestricted, consumingTenantOverridables,
> **agent.chatEmbedAllowedHosts**."*

**`agent.chatEmbedAllowedHosts` does not exist.** Not as a property, not as a type, not on either
version:

```
[chatEmbedAllowedHosts]         present in v1.0 metadata: False
[fileStorageContainerTypeAgent] present in v1.0 metadata: False
[chatEmbedAllowedHosts]         present in beta metadata: False
```

Confirmed three ways: absent from both CSDL documents, absent from the SDK's generated
`FileStorageContainerTypeSettings`, and absent from the live payload of all four container types in
Spaarke Dev.

And the POML **omits `sharingCapability`**, which *is* one of the real nine — and which task 023
already wired.

So FR-C07's list is eight real properties, one fictional, one missing. The task's substance survives;
its enumeration does not.

### Consequences for scope

| Property | State |
|---|---|
| `sharingCapability`, `isItemVersioningEnabled`, `itemMajorVersionLimit`, `maxStoragePerContainerInBytes` | ✅ **already wired by task 023** |
| `urlTemplate`, `isDiscoverabilityEnabled`, `isSearchEnabled`, `isSharingRestricted`, `consumingTenantOverridables` | **025's work — five, not nine** |
| ~~`agent.chatEmbedAllowedHosts`~~ | **dropped — does not exist.** AC-4's "malformed host" validation is therefore unbuildable and is not implemented. |
| `isOfficeRestricted` | beta-only. Surfaced **read-only**; see §4. |

The spec's framing of the *search/discoverability* trio as "the only R2-relevant slice of the
SPE-knowledge-source question" holds for `isSearchEnabled` and `isDiscoverabilityEnabled`. The agent
third of that trio was imaginary.

---

## 3. 🔴 The SDK's override enum is behind the live tenant

`consumingTenantOverridables` is enum **flags**, serialised by OData as a comma-delimited string. Live
value on all four container types:

```
"sharingCapability,itemMajorVersionLimit,isOfficeRestricted"
```

But the SDK's generated `FileStorageContainerTypeSettingsOverride` declares only:

```
UrlTemplate · IsDiscoverabilityEnabled · IsSearchEnabled · IsItemVersioningEnabled
ItemMajorVersionLimit · MaxStoragePerContainerInBytes · UnknownFutureValue
```

**Two of the three live flags — `sharingCapability` and `isOfficeRestricted` — are not members of the
SDK enum.** Parsing this value through the typed enum would therefore drop or fail on real data.

Handled by reading the **raw string** rather than the typed enum. That is the opposite of task 023's
choice (typed over untyped) and deliberately so: there, typing eliminated a class of name error; here,
the type is provably narrower than reality. Recorded for **task 026**, which owns the override state.

---

## 4. Read-only vs writable — an honest non-answer

The POML asks which properties are writable, verified by read-back. **That cannot be determined right
now**: every PATCH against a container type returns `400 invalidRequest`, in every shape tested,
including a no-op writing the current value back (task 023 §2 / `live-verification-2026-08-24.md`).

So no property's writability is empirically established — not the five added here, and not the four
task 023 wired.

What was done instead:

- All nine are wired on the **write** path, because the metadata declares them as settable properties
  of the settings complex type and nothing distinguishes them from one another.
- `isOfficeRestricted` is surfaced **read-only**: it is beta-only, absent from the SDK's typed model,
  and therefore cannot be written through the typed settings object without reintroducing exactly the
  untyped string-key pattern task 023 removed.
- **AC-2 is not met**, for the same reason it is not met for task 023. The escalation is shared, not
  duplicated.

---

## 5. What was delivered

**Server — complete.** All nine v1.0 settings now flow in both directions:

| Path | Change |
|---|---|
| **Read** (new) | `SpeContainerTypeSettings` domain record + `MapContainerTypeSettings`, surfaced as `ContainerTypeSettingsDto` on list, get, create **and** on the settings-update response. **Before this task no settings value reached the client at all** — the Settings screen could only ever show what the user had just typed. |
| **Write** | The five remaining properties added to the typed settings object; `consumingTenantOverridables` written as the raw flag string via `AdditionalData` (§3). |
| **Read-back** | `ContainerTypeSettingsResponseDto.Settings` carries the post-update state, so a caller can confirm a write applied instead of trusting a 200 — the FR-C04 constraint, now structurally possible. |

**Tests** — 3 added (12 in the file): all nine present in the PATCH body; overridables survives as the
raw string including flags outside the SDK enum; and a guard that the fictional
`agent.chatEmbedAllowedHosts` is never sent, so a future reader of FR-C07 cannot "restore" it.

### ⚠️ Not delivered — the settings FORM

AC-1 says the nine must *render in the UI*. The server now supplies them; the form does not yet show
the five new ones.

The reason is structural, not effort: `ContainerTypeSettingsForm` is bound to the **Dataverse config
record** (`SpeContainerTypeConfig`, fields like `maxStoragePerBytes`), not to the Graph settings DTO
this task introduced. Rebinding it is a distinct piece of work, and doing it half-way — adding controls
that read from one source and write to another — would recreate exactly the client/server name
mismatch task 023 spent its length untangling.

**Deferred deliberately, not forgotten.** It needs its own pass with the form's data flow in view.

### Gates

- BFF build **0 errors**, 7 pre-existing warnings · **10,673 tests** (+3), 0 failed · ArchTests **36/36**
- Publish **43.67 MB compressed incl. PDBs, 0 MB delta** (ceiling 60) · no new NuGet
- Code page builds · 0 type errors in the touched client files

**Placement (root CLAUDE.md §10):** no new endpoint, service, DI registration, or package — one new
DTO + domain record on existing read/write paths.

⚠️ **AC-2 unmet, shared with task 023**: every PATCH to a container type returns 400 in every shape
tested, so no property's writability is empirically established. Same escalation, not a second one.
