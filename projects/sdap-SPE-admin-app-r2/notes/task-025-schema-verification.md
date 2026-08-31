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

### ⚠️ Not delivered at the time — the settings FORM (✅ DELIVERED 2026-08-27, see §6)

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

> 🔴 **THE PARAGRAPH ABOVE IS STALE — corrected 2026-08-27.** It was written before, and never
> reconciled with, [`patch-400-resolution.md`](patch-400-resolution.md) (2026-08-25), which found the
> cause and states in its own header that it **"Unblocks 023, 025, 026, 029"**.
>
> **`etag` is a REQUIRED property in the PATCH request BODY** — not the `If-Match` header — and every
> write this product ever attempted omitted it. Microsoft's reference documents the exact symptom
> under *"Example 2: Update without ETag → 400 Bad Request"*. The fix is in the code
> (`SpeAdminGraphService.cs` reads the current etag and sets `patchBody.Etag` before the PATCH) and was
> proven live on 2026-08-25 with an identical no-op payload, so the etag was the only variable.
>
> Two notes therefore disagreed for two days: task 051 §3 reasoned from *"the container-TYPE settings
> PATCH does persist"* while this one asserted *"returns 400 in every shape tested"*. 051 was right.
>
> **What is still genuinely open** is narrower and is a UAT item, not a blocker: whether **each** of the
> nine properties individually persists. Task 051 added read-back verification
> (`SettingsNotPersistedException` → 502 with `unwrittenFields`), so a silent per-property discard would
> now be *reported* rather than absorbed — which is the structural half of AC-2. The empirical half
> needs one save against Spaarke Dev.
>
> Lesson, and it is the project's own: a stale caveat is indistinguishable from a live blocker to the
> next reader. This one would have carried a false "nothing is writable" into the wrap-up.

---

## 6. ✅ The settings FORM — delivered 2026-08-27 (AC-1 now met)

§5 deferred this deliberately: the form was bound to the **Dataverse config record**, not to the Graph
settings DTO the task introduced, and rebinding was judged a distinct piece of work. It was, and this
is it.

### 6.1 🔴 What the rebind exposed — the form was FABRICATING every value

`extractSettingsFromConfig` read `selectedConfig` (Dataverse) and filled every gap with an invented
default:

```ts
sharingCapability: (config.sharingCapability as …) ?? "disabled",
isItemVersioningEnabled: config.isItemVersioningEnabled ?? false,
itemMajorVersionLimit:   config.itemMajorVersionLimit   ?? 100,
maxStoragePerBytes:      config.maxStoragePerBytes      ?? 1_073_741_824,
isSearchEnabled: true, // Graph API search is enabled by default; no Dataverse field yet
```

Every one of those is a guess presented as configuration, **on the screen whose entire purpose is to
report the configuration.** The last line is the starkest: `isSearchEnabled` was hard-coded `true`
with a comment asserting a Graph default that nobody measured — so an administrator looking at a
container type with search **off** saw a switch in the **on** position.

This is the project's signature defect (spec §2.4) found inside the settings screen, and it is the
same shape as task 050's fabricated `"active"` status. It is a *fourth* instance.

It was invisible before this task because the server never sent settings at all (§5), so there was
nothing to disagree with the fabrication.

### 6.2 What shipped

| Change | Detail |
|---|---|
| **Form rebound to Graph's shape** | Props now take `ContainerTypeSettings` from `types/spe.ts` — the 9 v1.0 properties + beta `isOfficeRestricted`, every member optional. The form's own duplicate `ContainerTypeSettings` interface (5 required props, Dataverse-named `maxStoragePerBytes`) is gone; there was a genuine **type-name collision** between the two |
| **Seeded from Graph** | `settingsFromContainerType(ct)` replaces `extractSettingsFromConfig(config)`. Initial state is `{}` — empty, not defaulted, so nothing is asserted while the fetch is in flight |
| **All 9 render** | Added `isDiscoverabilityEnabled`, `isSharingRestricted`, `urlTemplate` (editable) and `consumingTenantOverridables` (read-only) + `isOfficeRestricted` (read-only) |
| **`TriStateSwitch`** | A boolean setting can be `true`, `false`, or **not reported**. `<Switch checked={undefined}>` renders identically to `false`, so a "Not reported" badge keeps the third state visible |
| **Undefined stays undefined** | Only established values are sent (`definedSettings`). The BFF applies non-null fields and leaves the rest alone, so an unreported setting is *omitted* rather than written as `false`. **Coercing an unknown to a default would turn a gap in knowledge into a configuration change** |
| **Read-back after save** | Re-seeds from the PUT response, not from what was sent — task 025 added that response payload for exactly this (FR-C04), and task 051 measured Graph accepting a settings write while discarding a property. Echoing the request back would hide it |

### 6.3 A bug caught before it shipped

The first version of the save handler did `setContainerType(updated)`. The PUT returns
`ContainerTypeSettingsResponseDto` — a deliberately **narrower** shape (id, displayName, billing,
createdDateTime, settings) that does **not** carry `owningAppId`, `expiryDateTime` or `region`.
Assigning it wholesale would have blanked those from the details panel above the form, so a
*successful save* would have looked like data loss.

Fixed by merging (`{ ...containerType, ...updated }`) — object spread only copies keys the response
actually has, so the omitted ones survive.

### 6.4 Deliberate non-goals

- `consumingTenantOverridables` **renders read-only.** AC-1 asks the nine to *render*. It is override
  PERMISSION metadata, not a setting value, and it is a comma-delimited flag string whose live members
  include values outside the SDK's enum — a free text box would let a typo silently widen or revoke
  what a consuming tenant may override. Task 026 renders its meaning in prose above the form.
- `isOfficeRestricted` **renders read-only** — beta-only, absent from the v1.0 schema this form writes
  through, so there is nothing to write it with.

### 6.5 Gates

- Client typecheck **124 errors = the pre-existing baseline exactly; 0 in the touched files**
- `npm run build` succeeds · BFF build **0 errors** (no server change was needed — 025's server half
  already accepted all nine)
- No new NuGet, no new endpoint, no new DI registration

⚠️ **AC-2 remains unmet and is unchanged** — shared with task 023, not a second escalation.
