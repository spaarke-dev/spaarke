# Task 026 — replication state and consuming-tenant overrides

> **Task 026** (spec FR-C08) · 2026-08-24 · Workstream C
> Schema facts verified against `GET https://graph.microsoft.com/{v1.0,beta}/$metadata`.
> Documentation facts cited into `knowledge/sharepoint-embedded/docs/learn-containertypes.md`.

---

## 1. ✅ Step 1 answered: Graph exposes **no** replication signal, anywhere

The POML's step 1 asks whether Graph exposes any replication-state signal, because that decides between
live state and a stated expectation. It does not:

```
[names matching  replicat*  anywhere in the CSDL]   v1.0: NONE      beta: NONE
```

Not a property, not a type, not an enum member, on either API version. `fileStorageContainerType`
carries exactly `billingClassification · billingStatus · createdDateTime · etag · expirationDateTime ·
name · owningAppId · settings` (beta adds `permissions`). There is no `replicationStatus`, no
`lastReplicatedDateTime`, no progress field. `etag` is a concurrency token, not a replication marker.

**So the honesty constraint resolves to: state the expectation, never imply tracking.** The 24-hour
figure is sourced, not invented — `learn-containertypes.md:101`:

> "Updating settings on a container type may take up to **24 hours** for the new values to be
> replicated on all consuming tenants. If a consuming tenant applied overrides on container type
> settings, the new values aren't applied and the overrides remain in place."

That single sentence is the source for **both** states this task renders.

---

## 2. 🔴 `consumingTenantOverridables` is a PERMISSION, not a STATE

This is the premise error at the centre of the task, and it is easy to miss.

The POML says *"Task 025 exposed `consumingTenantOverridables` as metadata; this task renders what it
means."* But `consumingTenantOverridables` lists which settings a consuming tenant **MAY** override. It
does not say which settings **ARE** overridden, and it carries no effective value.

Rendering it as "these settings are overridden" would state something the data never said — the exact
defect class this project exists to remove, reintroduced by the task meant to remove an instance of it.

### Where the real answer lives

`fileStorageContainerTypeRegistration` — a separate v1.0 entity, reached by the nav property
`containerTypeRegistrations` — carries its own `settings`:

| `fileStorageContainerTypeRegistrationSettings` (8) | `fileStorageContainerTypeSettings` (9) |
|---|---|
| isDiscoverabilityEnabled · isItemVersioningEnabled · isSearchEnabled · isSharingRestricted · itemMajorVersionLimit · maxStoragePerContainerInBytes · sharingCapability · urlTemplate | the same 8, **plus** `consumingTenantOverridables` |

Exactly the owner's settings minus the overridables permission — i.e. the **effective** per-consuming-
tenant view. So the comparison AC-2 needs is:

```
owner value      = containerType.settings.X
effective value  = containerTypeRegistration.settings.X
overridden       = the two differ   (and X is listed in consumingTenantOverridables)
```

**The existing consuming-tenant code cannot answer this.** `ListConsumingTenantsAsync`
(`SpeAdminGraphService.cs:3843`) reads the **`applicationPermissions`** endpoint and maps permission
grants; `SpeConsumingTenant` carries `AppId · DisplayName · TenantId · DelegatedPermissions ·
ApplicationPermissions` and **no settings at all**. Registration *settings* are a different read.

### 🔴 …but the owning tenant cannot see a remote consuming tenant's overrides

`containerTypeRegistrations` is **not** a child of a container type. It is declared on the
`fileStorage` entity itself, as a **sibling** of `containerTypes`:

```
EntityType fileStorage
    containerTypes             -> Collection(fileStorageContainerType)
    containerTypeRegistrations -> Collection(fileStorageContainerTypeRegistration)
```

so the path is `GET /storage/fileStorage/containerTypeRegistrations` — **scoped to the calling
tenant**. Registration is an act performed *in the consuming tenant*, and this collection returns the
registrations belonging to whoever holds the token.

The consequence is structural, not a permissions gap:

| Question | Answerable from the owning tenant? |
|---|---|
| What is effective for **this** tenant's registration? | ✅ yes |
| What has **another** consuming tenant overridden? | ❌ **no** — their registrations are in their tenant |

**AC-2 as written is therefore not fully achievable.** It says *"Given a setting overridden by a
consuming tenant … both the owner value and the effective override are shown"*. For a remote consuming
tenant there is no API surface from here that reports it. What *is* achievable is a different, narrower
feature — "what is effective in this tenant" — and in Spaarke Dev, where the owning tenant and the
consuming tenant are the same tenant, that is the only registration that exists at all.

Shipping the narrower feature under AC-2's label would be scope drift, and shipping the broader one is
impossible. **Escalated rather than guessed** — see §5.

---

## 3. 🔴 Graph's own published enum is narrower than the live tenant

`fileStorageContainerTypeSettingsOverride` members:

| Version | Members |
|---|---|
| v1.0 | urlTemplate, isDiscoverabilityEnabled, isSearchEnabled, isItemVersioningEnabled, itemMajorVersionLimit, maxStoragePerContainerInBytes, unknownFutureValue |
| beta | the above **+ isOfficeRestricted** |

The live value on all four Spaarke Dev container types is:

```
"sharingCapability,itemMajorVersionLimit,isOfficeRestricted"
```

**`sharingCapability` is not a member of either published enum**, yet the live tenant returns it — and
`sharingCapability` *is* a real member of `fileStorageContainerTypeRegistrationSettings`, so it is
plainly overridable in practice.

This sharpens task 025's finding. 025 recorded that the **SDK** enum was narrower than reality; in fact
**Graph's own CSDL is narrower than Graph's own responses**. Reading this value as a raw string rather
than through any typed enum was the right call, and is now justified twice over. Task 026 parses the
raw string and treats unrecognised flags as real, not as noise to drop.

---

## 4. 🔔 The three states the constraint demands are NOT all distinguishable

The POML's binding constraint: *"Three states MUST be distinguishable — saved-and-replicated,
saved-pending-replication (up to 24h), and overridden-by-consuming-tenant. Collapsing any two
reproduces the confusion this task removes."*

Given §1, that cannot be fully honoured:

| Pair | Distinguishable? | Why |
|---|---|---|
| pending **vs** overridden | ✅ yes | an override is observable by comparing owner vs registration settings |
| replicated **vs** pending | ❌ **no** | there is no replication signal of any kind to read |

After a save, an administrator's true position is *"the write was accepted; whether it has reached
consuming tenants is unknowable from this API for up to 24 hours."* There is no moment at which the UI
can honestly flip from "pending" to "replicated", because nothing reports that transition.

**The POML's literal escalation trigger did NOT fire.** It names *"if Graph exposes no way to
distinguish 'not yet replicated' from 'overridden downstream'"* — and those two **are** distinguishable.
The indistinguishable pair is a different one, which the trigger does not cover.

Resolution taken: render **two** honest states (pending-or-replicated-unknown, and overridden) rather
than three, and say plainly that replication completion is not observable. Inventing a "replicated"
state on a timer, or flipping it after an arbitrary delay, would be a fabricated fact of exactly the
kind NFR-06 forbids — and would be worse than the bare "Saved" this task exists to replace, because it
would look authoritative.

---

## 5. What was delivered

**Client only.** No `.cs` file changed: the data this task needed — `settings.consumingTenantOverridables`
— was already reaching the client from task 025. No server change, therefore no publish-size movement
and no new BFF surface.

| Change | Why |
|---|---|
| `REPLICATION_MAX_HOURS`, `SAVE_ACCEPTED_TITLE`, `SAVE_ACCEPTED_DETAIL` in `containerTypeLifecycle.ts` | Sourced constants, kept as data next to the other lifecycle facts so they can be asserted and re-checked against the corpus (task 061) |
| `parseConsumingTenantOverridables()` | Splits the comma-delimited flag string. **Preserves unrecognised flags** — see §3; filtering to the published enum would drop `sharingCapability`, which is live on all four container types |
| `isOverridableByConsumingTenant()`, `labelForSetting()` | Permission lookup + display labels, with the raw flag name as fallback so a future flag renders as itself rather than vanishing |
| Post-save banner rewritten | Was `intent="success"` + *"Settings saved successfully."* Now `intent="info"` + "Saved — replication is pending", with the 24-hour expectation **and** the fact that an existing override never picks the value up |
| Overridable-settings notice | Lists which settings a consuming tenant **may** override, worded as a permission, and states outright that overrides applied in another tenant are not visible here |

### The `success` → `info` change is the point, not a detail

A green "Settings saved successfully" asserts the change is in effect. It is not: it may take 24 hours,
and where a consuming tenant has overridden the setting it will **never** take effect. An administrator
who saved, saw green, checked a consuming tenant and found the old value would reasonably conclude the
tool is broken — which is FR-C08's stated rationale, and an instance of this project's core defect
appearing in the *success* path rather than the failure path.

**AC-1, AC-3 (as far as it can be met), AC-4, AC-5, AC-6, AC-7 met.**

- **AC-6 is met by construction**: no timestamp, no progress bar, no countdown. There is nothing to
  fabricate them from, and the copy says so.
- **AC-4** (a pending state must not read as plain success): the intent change plus the explicit title.
- **AC-5** (an override must not read as a failed save): the overridables notice is `info`, sits above
  the save banners, and is worded as a property of the container type rather than an outcome.

---

## 6. 🔔 AC-2 escalated — the broad form is not achievable, the narrow form is a different feature

**AC-2**: *"Given a setting overridden by a consuming tenant, when it renders, then both the owner value
and the effective override are shown and labelled."*

| Fact | Established by |
|---|---|
| The effective value exists on `fileStorageContainerTypeRegistration.settings` — 8 properties, exactly the owner's minus `consumingTenantOverridables` | v1.0 CSDL |
| That collection hangs off `fileStorage`, **not** off a container type, and is scoped to the calling tenant | v1.0 CSDL (§2) |
| So the owning tenant can read **its own** registrations and **not** a remote consuming tenant's | follows from the above |
| In Spaarke Dev the owning tenant *is* the consuming tenant, so only one registration exists and a cross-tenant override cannot be produced to test against | project CLAUDE.md + `live-verification-2026-08-24.md` |

Three paths, none of which should be chosen silently:

1. **Drop AC-2** — accept that an owning-tenant admin console cannot report remote overrides, and that
   the honest surface is the permission list plus the warning already shipped.
2. **Re-scope AC-2** to "show what is effective in *this* tenant's registration", implemented against
   `GET /storage/fileStorage/containerTypeRegistrations`. Buildable and verifiable in Spaarke Dev, but
   it answers a **different question** than FR-C08 asks, and would need its own task (new Graph read,
   domain record, DTO, endpoint, client type, UI).
3. **Escalate to product** — decide whether SPE Admin should ever claim to show cross-tenant override
   state, given the API cannot support it.

**Recommendation: 2, as a separate task**, with FR-C08 amended to say what it can mean. The narrow
feature is genuinely useful — it is the only way to see whether *this* tenant's effective settings
diverge from the owner's — but calling it AC-2 would be scope drift, and building it inside 026 would
ship an unverifiable claim under a label that promises more than it delivers.

**The POML's literal escalation trigger did not fire** (§4). This escalation is raised on the CLAUDE.md
§6 ground of *scope expansion beyond task boundaries* plus an acceptance criterion that the platform
cannot satisfy — the same shape as task 025's `agent.chatEmbedAllowedHosts`, where AC-4 was unbuildable
because the property did not exist. Here the property exists; the **tenant boundary** is what blocks it.

---

## 7. Gates

- **No `.cs` changed** — no BFF build, publish-size, NuGet, or CVE delta. .NET suite untouched at
  **11,195 pass / 0 fail**.
- Code page builds (2.34 MB, gzip 607.7 kB) · `tsc --noEmit` **0 new errors** against the same
  stash-captured 38-error baseline used for task 029.
- ADR-021: Fluent `MessageBar` intents + existing token-based styles only; no hex literals added.
- **Client lint still unavailable** — SpeAdminApp has no ESLint dependency, config, or install
  (recorded under task 029 §7). `tsc --noEmit` substituted again.
- No client test infrastructure exists in SpeAdminApp (no vitest/jest), so
  `parseConsumingTenantOverridables` — whose unrecognised-flag behaviour is load-bearing per §3 — is
  **not pinned by a test**. Adding a runner is new tooling surface (CLAUDE.md §11) and belongs with the
  ESLint gap in its own task.

**Placement (root CLAUDE.md §10):** no BFF change at all. §11: no new component — `assessBilling`'s
module was extended rather than a new one added.
