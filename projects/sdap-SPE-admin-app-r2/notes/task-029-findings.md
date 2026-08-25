# Task 029 — billing status: the field that did not exist anywhere

> **Task 029** (spec FR-C12) · 2026-08-24 · Workstream C
> Schema verified against `GET https://graph.microsoft.com/{v1.0,beta}/$metadata` — the OData CSDL
> Microsoft publishes, which needs no token and outranks documentation prose.

---

## 1. ✅ The POML's field names are correct — the second time that has happened

FR-C12 names `billingClassification` and `billingStatus`. Both are real, both on **v1.0**:

```
EntityType fileStorageContainerType (v1.0)
  billingClassification   graph.fileStorageContainerBillingClassification
  billingStatus           graph.fileStorageContainerBillingStatus
  createdDateTime · etag · expirationDateTime · name · owningAppId · settings
  (beta adds: permissions)
```

Enum members, identical on both versions:

| Enum | Members |
|---|---|
| `fileStorageContainerBillingStatus` | **`invalid`**, **`valid`**, `unknownFutureValue` |
| `fileStorageContainerBillingClassification` | `standard`, `trial`, `directToCustomer`, `unknownFutureValue` |

The classification members match all four live container types in Spaarke Dev exactly
([`live-verification-2026-08-24.md`](live-verification-2026-08-24.md) §1). **AC-5 satisfied.**

Note the enum type is named `fileStorageContainer…`, not `fileStorageContainerType…` — a reasonable
guess that would have been wrong.

---

## 2. 🔴 `billingStatus` did not appear anywhere in the repository

Not one occurrence, in any language, on either side:

```
grep -ri "billingstatus" src/ tests/   →  0 results
```

The domain record had no slot for it, no DTO carried it, no client type declared it, and no screen
rendered it. So the operational failure mode FR-C12 exists to surface — a container type whose billing
has lapsed — **had no route to an administrator at all**. It did not render wrongly; it did not render.

This is the project's signature shape in its purest form so far. Elsewhere a real value was collapsed
into an absent one by a bad type check (022), a wrong name (023), or a discarded field (024). Here the
value was never asked for.

### Consequence for the wave plan

**The POML's `parallel-safe=true` is wrong**, as is its note *"DTO + endpoint + client only — does NOT
modify `SpeAdminGraphService.cs`"*. Surfacing a new Graph field requires the mapping layer, and the
mapping layer is the god-file. Task 029 modifies `SpeAdminGraphService.cs` in two places.

W10 paired 029 with 026 on the stated grounds that neither touches the god-file. Had they been
dispatched as parallel agents they would have contended on it — and on
`Models/SpeAdmin/ContainerTypeDtos.cs` and `components/container-types/`, which **both** POMLs list as
modify targets. They were run serially in the main session, so no harm; recorded because the same
reasoning error would bite a future wave. **Task 026 should be re-checked before it is called
parallel-safe.**

---

## 3. 🔴 The classification split FR-C12 does not mention

FR-C12 asks for "a warning when `billingStatus` is not valid". Applied literally that produces one
generic message — and the sourced facts say a generic message would be **wrong for two of the three
classifications**:

| Classification | What the documentation says | Source |
|---|---|---|
| `standard` | "The admin in the developer tenant **must establish a valid billing profile**" | learn-containertypes.md:79 |
| `directToCustomer` | Billed to the consuming tenant; developer-tenant admins "**don't need to set up an Azure billing profile**" | :80 |
| `trial` | "**isn't linked to any Azure billing profile**" | :61 |

So "attach a billing profile with `Add-SPOContainerTypeBilling`" is correct advice **only** for a
standard type. Telling an admin to attach one to a passthrough or trial type sends them to do
something that does not apply — which is the *routing* version of the defect this task removes.

`assessBilling()` therefore branches on classification and gives each case its own consequence and
remediation. For `directToCustomer` it says plainly that the documentation **does not** state how
billing becomes invalid for a passthrough type, rather than inventing a remediation. A null
remediation is a deliberate outcome, not an oversight.

---

## 4. 🔴 A second, pre-existing defect found in passing

`UpdateContainerTypeSettingsAsync` built its result with:

```csharp
var billingClassification = updated.BillingClassification?.ToString();   // ← no normalization
```

while the **list** path ran the identical value through the normalizer. The SDK enum stringifies to its
C# member name, so the same container type came back as **`"Trial"` from a settings save** and
**`"trial"` from the list** — the spelling depending on which endpoint the client happened to ask.

Any client comparing the two, or comparing either against Graph's own lowercase value, fails silently
on exactly one path. Fixed; pinned by `SettingsPatch_ReturnsBillingFields_InTheSameCasingTheListDoes`.

The helper was also renamed `NormalizeBillingClassification` → **`NormalizeEnumMemberName`**. It had
already outgrown its name when task 025 pointed `sharingCapability` at it; task 029 would have been the
third caller.

---

## 5. 🔴 Making the client type honest surfaced three more sites

The client declared `billingClassification: ContainerTypeStatus` — **required** — while the BFF has
always sent it nullable. Because responses are cast rather than parsed, TypeScript could not see the
difference. It was not hypothetical: the value was **null for every container type** between the Graph
6 upgrade (2026-08-13) and task 030's fix (2026-08-23).

During that window the grid rendered `capitalize(undefined)`, which returns its falsy input unchanged —
so the cell showed an **empty `informative` badge**. Not "Unknown"; a blank badge that reads as a real
neutral state.

Making the field optional (`billingClassification?`) immediately produced two compile errors in
`SelectContainerTypeStep.tsx` and `ConfirmRegistrationStep.tsx`, both of which had the same assumption
in duplicated helpers. All three sites now render an explicit **Unknown**.

> Minor smell not addressed: `billingLabel` / `billingBadgeColor` are duplicated verbatim across those
> two step files and partially in two more. Consolidating into `containerTypeLifecycle.ts` is the
> obvious home but touches render sites beyond this task's scope. Left as-is deliberately.

---

## 6. What was delivered

**Server** — `billingStatus` flows end-to-end for the first time:

| Layer | Change |
|---|---|
| `SpeAdminGraphService.cs` | `BillingStatus` on `SpeContainerTypeSummary` + `ContainerTypeSettingsResult`; read typed-first / `AdditionalData`-second (the task-030 pattern, resilient to SDK model drift in either direction); classification normalization fixed on the settings path; helper renamed |
| `ContainerTypeDtos.cs` | `BillingStatus` on `ContainerTypeDto` + `ContainerTypeSettingsResponseDto` |
| `ContainerTypeEndpoints.cs` | 3 projection sites |
| `ContainerTypeSettingsEndpoints.cs` | 1 projection site |

No `$select` change was needed — task 025 removed it from the container-type list, so a newly-surfaced
property arrives without a code change. That decision paid off here for the first time.

**Client** — three states everywhere, never two:

- `BillingStatus` type; `billingStatus?` and `billingClassification?` on `ContainerType`
- `assessBilling()` / `toBillingStanding()` in `containerTypeLifecycle.ts` — classification-aware,
  sourced, pure data (ADR-021: no presentation in that module)
- Grid: new **Billing Status** column + classification cell fixed to say Unknown
- Grid: page-level `warning` MessageBar naming the affected types, the consequence, and the
  remediation — shown only for a **reported** `invalid`
- Detail panel: header badge (always rendered, including Unknown) + an actionable warning above the
  settings form, placed away from the save banners because it is not a save failure

**Anything unrecognised — including `unknownFutureValue` and any member added later — lands in UNKNOWN
rather than being coerced to `valid`.**

**Tests** — 3 added:

| Test | Protects |
|---|---|
| `BillingStatus_ReachesTheSummary_InGraphsWireCasing` | the field maps at all, AND that `"Invalid"` is normalized to `"invalid"` |
| `AbsentBillingStatus_IsNull_RatherThanValid` | NFR-06, in its expensive direction |
| `SettingsPatch_ReturnsBillingFields_InTheSameCasingTheListDoes` | §4's endpoint-dependent-casing defect |

---

## 7. Gates

- BFF build **0 errors**, 7 pre-existing warnings · **11,195 tests pass, 0 failed**
  (`Sprk.Bff.Api.Tests` 10,673 → **10,676**, +3) · ArchTests **36/36**
- Code page builds (2.34 MB, gzip 607 kB) · `tsc --noEmit`: **0 new errors** against a stash-captured
  baseline of 38 pre-existing (the 2 my change introduced were fixed — see §5)
- No new NuGet · no new endpoint, service, or DI registration
- ADR-021: no hex literals added (verified over the diff) · ADR-007: no Graph SDK type in any DTO ·
  ADR-038: both test files sit under `tests/integration/contract/**`, a KEEP path, with the repo's
  `[Trait("Category", …)]` convention and WireMock at the HTTP boundary (no `Mock<HttpMessageHandler>`)

### 🔴 The client lint gate does not exist and never has

Step 9.5 calls for `npm run lint`. In `src/solutions/SpeAdminApp` that script is:

```
"lint": "eslint src --ext .ts,.tsx"
```

but the package has **no ESLint dependency, no ESLint config file, and no ESLint installed**:

| Check | Result |
|---|---|
| `.eslintrc*` / `eslint.config.*` | **none** |
| `"eslint"` in `package.json` deps | **not declared** |
| `node_modules/.bin/eslint` | **not installed** |

So the script fails with *"'eslint' is not recognized"* — it has never linted this code page. (`--ext`
is also removed in ESLint 9+, so the invocation would need updating even once ESLint were installed.)

An initial filtered run appeared to report "no findings in the changed files". That was a **false
negative** — ESLint never executed. Recorded rather than quietly counted as a pass.

**Not fixed here.** Adding ESLint + a flat config to this package is new tooling surface (CLAUDE.md
§11) and affects every file in the code page, not just task 029's. It wants its own task. The
substitute actually run — `tsc --noEmit` diffed against a stashed baseline — is stronger than lint for
type correctness and silent about style.

### ⚠️ The recorded publish-size baseline is not reproducible — the method drifted, not the artifact

Measured **44.99 MB** compressed incl. PDBs, against a recorded baseline of 43.67 MB — an apparent
**+1.32 MB** for a change that adds two record properties and no packages.

That was not plausible, so it was checked rather than reported: the task-029 changes were stashed and
the publish measured again from the same clean output directory.

| Measurement | Compressed incl. PDBs |
|---|---|
| Baseline (029 stashed) | **44.99 MB** |
| With task 029 | **44.99 MB** |
| **True delta** | **0.00 MB** |

So the artifact did not grow. The 43.67 MB figure recorded on 2026-08-24 is not reproducible with
`Compress-Archive -CompressionLevel Optimal` over `dotnet publish -c Release`; whatever produced it
used a different method. **Ceiling is 60 MB and we are 15.01 MB under it**, so nothing is at risk — but
the next task to measure will see the same phantom +1.32 MB and should not chase it. Someone should
settle on one command and record it, or the number is not comparable across tasks.

**Placement (root CLAUDE.md §10):** no new endpoint, service, DI registration, or package — two record
properties, two DTO properties, four projection sites, and one client module extended. §11: no new
component; `assessBilling` extends the existing `containerTypeLifecycle.ts` rather than adding a
module.

---

## 8. ⚠️ AC-1 not fully verified — live render outstanding

AC-1 requires both fields to render **against Spaarke Dev**. What is established:

| Claim | How |
|---|---|
| Both field names exist on v1.0 | Graph's own CSDL — authoritative, no token |
| The SDK types `BillingStatus` | compilation |
| The value maps end-to-end through the real SDK + HTTP boundary | WireMock contract tests |
| Absence stays absent | contract test |

What is **not** established: that Spaarke Dev's four container types actually **return** `billingStatus`.
The 2026-08-24 live capture recorded a curated table (name / owningAppId / billing / expirationDateTime)
rather than the raw payload, so it does not answer this.

**If Graph omits the field for these types, all four render "Unknown"** — correct behaviour, and
exactly what NFR-06 demands, but AC-1 would then be satisfied only in that degenerate sense. That
distinction should not be papered over.

**The escalation trigger did NOT fire.** It names *"if `billingStatus` cannot be read under the app's
current permissions"*. Permissions are not the blocker: `FileStorageContainerType.Manage.All`
(delegated) is granted and admin-consented, and the 2026-08-24 session read container types with it
successfully. The blocker is that container types are **delegated-only**, so a token needs an
interactive device-code sign-in — a few minutes of operator time, not a permission gap.

**One command settles it**, and it is read-only:

```
GET https://graph.microsoft.com/v1.0/storage/fileStorage/containerTypes
```

with a delegated token via `SPAARKE-SPE-Admin-CLI` (`68cf5a14-1efb-4254-80bf-2761ffc89373`), per
[`live-verification-credential.md`](live-verification-credential.md) §4.
