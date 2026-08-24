# Task 030 — lifecycle constraints in the UI

> **Task 030** (spec FR-C13) · 2026-08-23 · **partially delivered — 🔔 escalation trigger fired on the quota**
> Client-only. No server file changed. No destructive or irreversible action performed.

---

## 1. Premise check — the constraint values are sound, the scope is not

Unlike the previous eleven, this POML's **facts** hold up. Every constraint it names is confirmed in
`knowledge/sharepoint-embedded/docs/learn-containertypes.md` (Microsoft Learn, fetched 2026-05-14):

| POML claim | Source | Verdict |
|---|---|---|
| owning app ↔ container type is 1:1 | `:11`, `:95` | ✅ |
| container-type ID immutable | `:13` | ✅ |
| trial cannot convert to production | `:21` | ✅ |
| standard ↔ pass-through cannot convert | `:22` | ✅ — and it is **bidirectional**; the POML states only one direction |
| max 25 per tenant | `:75` | ⚠️ correct, but the sentence sits under *"Standard container types (nontrial)"* — it reads as the production ceiling, not a total |
| at most one trial | `:61` | ✅ |
| only trial types deletable | `:109` | ✅ |

**Five constraints the POML did not name** are in the same source, and two of them are the sharpest
traps in the flow:

- 🔴 **A trial container type expires after 30 days** (`:69`) and is not renewable. Nothing in the app
  said so. This is the purest form of the defect class this project exists to remove — not "discovered
  by failing" but *never discovered*, until a working type stops working.
- 🔴 **A trial type cannot be registered on another consuming tenant** (`:71`) — yet the page offered a
  **Register** button for it. An action that could only fail.
- A standard type needs a billing profile attached in PowerShell (`:89`–`:93`), which this app cannot do.
- A trial is capped at 5 containers × 1 GB (`:67`–`:68`).
- Deleting a container type first requires permanently deleting every container of that type,
  including from the deleted-container collection (`:109`).

**Where the POML is wrong is its scope**, not its facts: three of its five constraints cannot be
satisfied client-only, because the data they need never reaches the client. See §3.

---

## 2. 🔴 Found while verifying — row selection has never worked

Not in the POML, found by tracing the identity field the constraints hang off.

The BFF serialises the identifier as **`id`** (`ContainerTypeDto`, `[JsonPropertyName("id")]`). The
client declares it as **`containerTypeId`** (`types/spe.ts:184`). Nothing converts between them, and
`speApiClient.containerTypes.list()` **casts** the response rather than parsing it — so TypeScript
never saw the mismatch:

```ts
return get<{ items: ContainerType[]; count: number }>(…).then(r => r.items);
```

Consequences, all silent:

| Site | Effect |
|---|---|
| `getRowId={(ct) => ct.containerTypeId}` | `undefined` for **every** row — duplicate React keys |
| `onRowClick(item.containerTypeId)` | sets `selectedTypeId` to `undefined` |
| `hasSelectedType` | **permanently false** |
| `RegisterWizard initialTypeId` | **always `null`** |

Clicking a row appeared to do nothing. The signature shape again — a lower layer returning something
the upper layer reads as benign absence. **Fixed** in this task, contained to the screen
(`normalizeContainerType`), because row selection is a prerequisite for the conditional Register.

---

## 3. 🔴 The server dropped the fields the constraints need — **FIXED** (operator decision, 2026-08-23)

> Originally recorded here as a handoff to tasks 023/025. The operator chose to fix it inside 030
> instead. Writing the first test over this mapping then surfaced something worse — see §7.

`owningAppId`, `azureTenantId`, and `expiryDateTime` are **absent from both** the domain record and
the DTO — they are not merely unmapped at the edge:

```csharp
record SpeContainerTypeSummary(Id, DisplayName, Description, BillingClassification, CreatedDateTime);
record ContainerTypeDto        { Id, DisplayName, Description, BillingClassification, CreatedDateTime }
```

The client asks for all three (`types/spe.ts:182-199`), so today:

- the grid's **"Owning App" column is blank for every row**;
- the grid's **"Registered" badge reads "No" for every row** (`isRegistered` is never sent, and
  `ct.isRegistered === true ? "Yes" : "No"` resolves the absence to "No");
- `ContainerTypeDetail.tsx:737` renders `owningAppId ?? "—"`;
- `ContainerTypeDetail.tsx:740` guards the **trial-expiry warning** on `expiryDateTime`, so the
  warning about the 30-day expiry **can never render**.

### The root cause was the projection, not the mapping

The Graph request carried a hand-maintained `$select`:

```csharp
config.QueryParameters.Select = new[] { "id", "name", "billingClassification", "createdDateTime" };
```

**A property the projection does not ask for is a property the caller silently never sees.** No amount
of mapping downstream could have recovered these fields.

**Fix: the `$select` was removed entirely, not extended.** Naming the properties explicitly would work
today but re-arms the failure this workstream exists to remove — a wrong or version-absent name in
`$select` is a hard 400 that breaks the whole list, exactly as `storageUsedInBytes` does on v1.0
(`notes/beta-vs-v1-surface-verification.md`). Omitting `$select` returns the resource's default
projection instead, and there is no size argument against it: Microsoft caps a tenant at 25 container
types.

`owningAppId` and `expirationDateTime` now flow through `SpeContainerTypeSummary` (optional trailing
params, so existing positional construction still compiles) → `ContainerTypeDto` → all three endpoint
mapping sites → the client. `azureTenantId` was **not** added: the Graph container-type resource does
not expose one, and inventing a mapping would be the fabrication this project removes.

---

## 4. 🔔 Escalation — the quota cannot be computed honestly

The POML's trigger, verbatim:

> *"If any constraint value cannot be determined from the API (e.g. tenant-wide container-type count is
> not readable under the app's permissions, making the 25-limit quota display impossible), STOP and
> escalate — displaying a guessed quota is worse than displaying none."*

**It fires, on exactly the named case.** Container-type LIST runs delegated as the signed-in user
(`ListContainerTypesForUserAsync` → `IGraphClientFactory.ForUserAsync`), and task 012 proved the BFF
cannot observe whether that user holds the Entra role that widens visibility tenant-wide — the `wids`
claim never reaches the token, even for a **confirmed** role holder. So the list is a **lower bound**
on the tenant's true count, never a census. `22 of 25 remaining` would be a guess wearing a fact's
clothing.

### But the two limits are not symmetric, and that is usable

| Limit | Can the visible list prove it? |
|---|---|
| **one trial per tenant** | **Yes, in the blocking direction.** Seeing a trial type *proves* one exists → block with certainty. Not seeing one proves nothing → must not claim the slot is free. |
| **25 per tenant** | **No, in either direction.** A lower bound can neither prove the ceiling is reached nor that headroom exists. |

So the delivered behaviour asserts only what the data supports: it **states both documented limits**,
reports the observed count **labelled for what it is**, blocks trial creation when a trial is visible,
and **never publishes a "remaining" number**. `describeProductionQuota()` returns `atLimit: false`
unconditionally, with a comment saying why.

That is a deliberate deviation from acceptance criterion 3 (*"Remaining quota is displayed … at the
limit, creation is blocked"*). **Half of it is met** — the limits are displayed and trial creation is
blocked on proof. The "remaining" figure is **knowingly not implemented**, per the trigger.

### Options

| | Action | Cost |
|---|---|---|
| **A — accept as delivered (recommended)** | State limits + observed count + scope caveat; block only on proof. | AC-3 partially met. No "remaining" figure anywhere. |
| **B — make the count authoritative** | Have the BFF read container types with an identity holding the SPE Administrator role, so the list is a true census. | New app-only path for a **delegated-only** API (403 on both versions — task 010). Not currently possible. |
| **C — display remaining from the visible count** | Compute `25 − observed`. | Publishes a guess as a fact. This is the defect class the project removes. **Rejected.** |

### ✅ RESOLVED — operator chose **A** (2026-08-23)

No code change: option A is what shipped. The deviation from acceptance criterion 3 is **accepted
knowingly** — the limits are stated and trial creation is blocked on proof, but no "remaining" figure
is published anywhere.

**Do not add one later.** `describeProductionQuota()` returns `atLimit: false` unconditionally on
purpose, and says so in a comment. Reopening this requires an identity that can enumerate the tenant's
container types, which option B describes and task 010 proved unavailable today.

---

## 5. What was delivered

| Constraint (POML) | Status |
|---|---|
| 1 — immutable ID + owning app, no edit affordance | ✅ **already satisfied**; verified, not assumed. `ContainerTypeSettingsForm` edits only sharing capability, versioning, version limit, max storage, item search. ID renders as a badge; owning app as read-only text. No disabled-looking input exists. |
| 2 — permanence stated before submit | ✅ **delivered in full**, and broadened to the five unnamed constraints in §1 |
| 3 — quota | ⚠️ **partial by decision** — see §4 |
| 4 — delete conditional on trial | ❌ **not delivered** — see below |
| 5 — 1:1 owning app explicit in creation | ⚠️ **stated, not shown** — the statement is in the flow; the owning app *value* cannot be displayed (§3) |

### Constraint 4 has nothing to make conditional

There is **no container-type delete affordance anywhere** — not in the page, not in the detail panel —
and **no `containerTypes.delete` in the API client**. The constraint asks that an existing affordance
become conditional; the affordance does not exist. Building one means a new BFF DELETE endpoint plus
destructive verification against a live tenant holding real working documents (project CLAUDE.md
live-tenant safety). Both are outside a client-only task.

What *was* done instead: the creation flow now states plainly, before submit, that a standard or
pass-through type **can never be deleted** — which is the decision point where that fact actually
changes an admin's behaviour.

### Files changed

**Client** (inside `<outputs>`, plus the type contract those files depend on):

| File | Change |
|---|---|
| `containerTypeLifecycle.ts` **(new)** | The sourced constraint data — every fact carries a line reference into the knowledge corpus. Pure data, no JSX, so it is assertable in tests and re-checkable when the corpus refreshes (task 061). |
| `CreateContainerTypeDialog.tsx` | Per-classification consequences shown live before submit, severity-coded; quota bar; acknowledgment gate for the classifications that can never be deleted; blocks submit when a limit is proven. |
| `ContainerTypesPage.tsx` | `normalizeContainerType` (§2); Register disabled when nothing is selected **or** the selection is trial, with a tooltip naming the reason; "Owning App" renders `—` rather than blank; **"Registered" gained a third state** — `undefined` now reads *Unknown*, not *No*. |
| `types/spe.ts` | `owningAppId` and `azureTenantId` made optional, with the reason: absent means **unknown**, not "none". |

**Server** (added by operator decision — see §3, §7):

| File | Change |
|---|---|
| `SpeAdminGraphService.cs` | `$select` removed from the container-type list (§3); `OwningAppId` + `ExpirationDateTime` on `SpeContainerTypeSummary` as optional trailing params; `MapContainerType` reads typed-first / `AdditionalData`-second; `NormalizeBillingClassification`, `ReadAdditionalString`, `ReadAdditionalTimestamp` helpers. |
| `ContainerTypeDtos.cs` | `OwningAppId` + `ExpiryDateTime` on `ContainerTypeDto`, both nullable. |
| `ContainerTypeEndpoints.cs` | All three DTO construction sites (list, get, create) pass the new fields through. |
| `SpeAdminContainerTypeMappingTests.cs` **(new)** | 6 WireMock contract tests. Three failed on first run — that is how §7 was found. |

**Placement justification (root CLAUDE.md §10):** no new endpoint, service, DI registration, package,
or background work. This is a mapping correction inside an existing `Infrastructure/Graph` method plus
two nullable fields on an existing DTO — the narrowest change that makes the response carry what the
client already asked for. Publish size unchanged (0 MB delta).

**Design note — the acknowledgment gate is proportionate, not blanket.** Trial is deletable and
expires, so showing the consequences is enough. Standard and pass-through can *never* be deleted, so
those require an explicit checkbox. Gating everything equally would train the reflex it is meant to
interrupt.

### Gates

- **BFF build ✅** — 0 errors, 7 pre-existing warnings.
- **Tests ✅** — **10,652 passing** (+6), 0 failed, 97 skipped. **ArchTests 36/36** (ADR-007 holds — the
  Graph client is still built and consumed entirely inside the service).
- **Publish size ✅** — **43.67 MB compressed incl. PDBs, 0 MB delta** vs the task-020 baseline.
  Ceiling 60 MB. No new NuGet, so no new CVE surface.
- **Code page build ✅** — `vite build`, 19.26 s.
- **Client type-check** — **0 errors in the 4 files touched.** 38 pre-existing errors elsewhere in the
  app (unrelated: unresolved `@lexical/*`, `@azure/msal-browser`, `@hello-pangea/dnd` in the shared
  libraries, plus `"tinted"`/`"tint"` typos in `containers/`). Not introduced here, not in scope.
- **No destructive action** — AC-7 holds; nothing was created, modified, or deleted in any tenant.
- ⚠️ **The UI itself is unverified.** The `<ui-tests>` need a deployed app; and container-type GET and
  CREATE still return 403 (task 011), so the creation flow cannot be exercised end-to-end regardless.
  Same standing gap as tasks 001 / 003 / 012.

### ADR compliance

- **ADR-021** ✅ — Fluent v9 semantic tokens only; no hex literals. Severity is carried by icon **and**
  wording, not colour alone.
- **ADR-050** — n/a as scoped. The constraint applies to *confirmation dialogs*; the only confirmation
  this task would have added belongs to the delete affordance that does not exist. The creation dialog
  is a pre-existing Fluent `Dialog`, not converted here — converting it is a `SprkModal` migration, not
  a lifecycle-constraint change, and `SprkModal` is not currently exported from
  `@spaarke/ui-components`'s public index.

---

## 7. 🔴 The biggest finding — `billingClassification` has been null since the Graph 6 upgrade

Found only because §3's fix came with the first test ever written over this mapping. **Three of the
five new tests failed on the first run, and one of them was testing pre-existing behaviour.**

`MapContainerType` read the value exclusively from the untyped `AdditionalData` bag, on the strength
of this comment:

```csharp
// BillingClassification: Graph SDK 5.101.0 does not include the typed enum
// FileStorageContainerTypeBillingClassification in the installed version.
```

That was true at 5.101.0. **The repo moved to `Microsoft.Graph` 6.5.0 on 2026-08-13**
(`dotnet-10-upgrade-r1` task 033), and 6.5.0 *does* type it. So Kiota began binding the value to the
typed property, `AdditionalData` stopped carrying it, and the read has returned **null for every
container type ever since**. The comment stayed accurate about 5.101.0 and became a lie about the
code.

**Blast radius.** Every lifecycle rule in the UI keys off this field — which types are deletable,
which can be registered, which quota applies — as does the grid's classification badge. The whole of
task 030's UI would have been driven by a permanently-null input. Both of this task's other client
fixes (the trial-Register block, the trial quota block) depend on it and would have silently no-opped.

**Why nothing caught it.** The upgrade was verified by build + test suite, and the suite contained no
test that exercised this mapping — the 359 SpeAdmin tests made no HTTP call and stood up no host
(project design.md §1). This is precisely the gap FR-D01 exists to close, and its stated acceptance
criterion — *"a wrong property name fails CI"* — is now met for this surface by demonstration rather
than by assertion.

**Fix**: typed property first, `AdditionalData` second, so the mapping survives the SDK typing the
property *or* untyping it again. Plus `NormalizeBillingClassification`, because the SDK enum
stringifies to its C# member name (`Trial`, `DirectToCustomer`) while Graph, the API contract, and
every client comparison use camelCase (`trial`, `directToCustomer`) — emitting the C# spelling would
make the DTO's value depend on which SDK version happened to be installed, which is the same coupling
that hid the bug.

**The generalisable lesson**: a comment naming a specific dependency version is a claim with an expiry
date. Six `AdditionalData` fallbacks elsewhere in `SpeAdminGraphService.cs` were written under the
same 5.x assumption and are worth auditing on the same basis — handed to task 023.

---

## 8. Handoff

| Item | Owner |
|---|---|
| ~~`owningAppId` / `expiryDateTime` on summary + DTO~~ | ✅ **done here** (§3), operator decision |
| ~~`billingClassification` null since Graph 6~~ | ✅ **done here** (§7) |
| **Audit the other 6 `AdditionalData` fallbacks** in `SpeAdminGraphService.cs` — all written under the same "SDK 5.x does not type this" assumption that §7 proved expired | **023** |
| **`CreatedDateTime: ct.CreatedDateTime ?? DateTimeOffset.UtcNow`** — a container type of unknown age renders as *created today*. Left deliberately: fixing it makes the DTO field nullable, which is a contract change reaching the client | **023** |
| `azureTenantId` — the client declares it; the Graph resource does not expose one. Either source it elsewhere or drop it from the client type | **023** or **025** |
| Container-type DELETE endpoint + trial-conditional affordance (§5) | new task, or **050**'s wave — needs a throwaway type |
| `speApiClient` **casts** responses instead of parsing them (§2) — the class of bug, not just this instance. §2 and §7 are both instances of it | **042** (test retirement) or a follow-up |
| Quota census (§4 option B) | blocked on the delegated-only constraint from task 010 |
| **Container-type GET and CREATE are still 403** (app-only against a delegated-only API) — so the detail panel cannot load and nothing in this task's creation flow can be exercised end-to-end yet | **011** (already 🔄 partial) |
