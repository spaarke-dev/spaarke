# Task 078 — authorize `GET /api/v1/containers/{containerId}/documents`

> **Status**: complete · **Date**: 2026-08-28 · **Rigor**: FULL · **Model**: opus @ high
> **Branch**: `worktree-agent-ad87eab968ddb1bb6`

---

## 1. What shipped

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Api/Filters/ContainerDocumentAuthorizationFilter.cs` | NEW — the gate |
| `src/server/api/Sprk.Bff.Api/Api/DataverseDocumentsEndpoints.cs:605` | `.AddContainerDocumentAuthorizationFilter()` on the route |
| `tests/integration/auth/UnifiedAccessControl/ContainerDocumentListAuthorizationTests.cs` | NEW — 5 tests |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs:305` | the task-078 Pending waiver DELETED |

**The decision**: container → owning record (task 075's `ResolveOwningRecordAsync`) → require the caller's
**Read** on that record via `AuthorizationService.GetCallerRecordAccessAsync` (task 070's entity-generic
sibling), evaluated OBO **as the caller**. An endpoint filter per ADR-008. No new component.

**Reuse, per root CLAUDE.md §11** — three existing seams, zero new ones:

| Need | Reused |
|---|---|
| container → record | `RecordContainerResolver.ResolveOwningRecordAsync` (075) — the ONE mapping, reverse direction. Its first consumer, which is what `OwningSecureRecord`'s doc comment named it for. Concrete type per ADR-010 |
| logical name → entity SET | `SemanticSearchAuthorizationFilter.TryResolveAuthorizableEntitySet` — its own remarks say "internal, and deliberately the ONLY such map" |
| rights decision | `AuthorizationService.GetCallerRecordAccessAsync` (070) |

### Why `AuthorizationService`, not `CallerRecordAccessProbe` directly

The handoff named `CallerRecordAccessProbe.GetCallerRightsAsync`. This filter uses
`AuthorizationService.GetCallerRecordAccessAsync` instead. The constraint's intent — *do not write a third
probe* — is satisfied either way; three things decided which existing seam to enter by:

1. **`SECURE-DOCUMENTS-BUILD-PLAN.md` invariant 1**: *"Every byte path and every **metadata path** routes
   through **one decision function**. A path that does not is a hole by construction — this is how all four
   Wave 1 findings happened."* This route is a metadata path. `AuthorizationService` **is** that one function;
   the probe is a sibling that bypasses it.
2. **`EntityAccessFilter`'s own remarks**: a filter should go back through `AuthorizationService` *"so there
   is one access path again"* once `IAccessDataSource` was generalized beyond documents. Task 070 generalized
   it (`GetRecordAccessAsync`). `EntityAccessFilter` uses the probe only because it predates that.
3. **Closest precedent**: `SemanticSearchAuthorizationFilter.AuthorizeEntityScopeAsync` (task 070) answers the
   *identical* question — "may this caller read this parent record, so may they see its documents" — through
   `GetCallerRecordAccessAsync`. Diverging from it would have produced two shapes for one question.

Invariant 2 is what the gate implements: *"Access flows from the parent. A caller who can read a
project/matter/work assignment can read its documents."*

---

## 2. 🔔 Escalation trigger FIRED — the modelling gap is REAL

The POML's trigger: *"If containers legitimately exist that map to no record, STOP and surface it — same
modelling gap task 073 names. Do not invent an owner."*

**They do.** `ResolveOwningRecordAsync` returns `null` whenever no **secure** record claims the container —
i.e. for every shared business-unit or archive container. That is not an edge case:

- `RecordContainerResolver.cs:61-62` — *"Three live projects currently share the root business unit's
  container id, so 'many' is the normal case, not the exotic one."*
- `RecordContainerResolver.cs:267-268` — verified live 2026-08-27, *"three of six business units have
  `sprk_containerid` unset."*

**What this task did with it: REFUSED (403), and did not invent an owner.** Rationale:

1. ADR-003 + the POML's own acceptance criterion (*"An unresolvable container id is refused"*). Unknown must
   never become permitted; "no basis to deny" is not a basis to allow.
2. A shared container legitimately holds documents of **many** records with **different** access. A
   per-container gate is structurally incapable of answering "may you see these". The correct control is
   result trimming against the caller's accessible-record set — Wave 3 / `AccessibleRecordSetService`, the
   same reasoning the Permanent waiver on `GET /api/v1/documents` already records. Until trimming exists,
   refusing is the only honest answer.

### ⚠️ This CONTRADICTS what task 075 assumed task 078 would do

`RecordContainerResolver.cs:415-425` returns early for the zero-secure-claimant case with the comment that
probing further would *"turn the ordinary shared-container case into a refusal, **breaking task 078 for every
normal container**."* Task 075 expected this filter to **allow** on `null`. It does not.

**What makes refusing safe is evidence, not preference** — see §3. There is no "normal container" list view
to break, because there is no caller at all.

**Left for the owner** (NOT silently resolved): if a future caller legitimately needs to list a *shared*
container's documents, the answer is Wave 3 result trimming, not relaxing this gate. Relaxing it re-opens the
hole. Recorded as the successor obligation.

---

## 3. Client-caller inventory (POML step 0 + constraint 4)

The POML's reference-file comment asserts: *"073 could delete because its routes had no legitimate caller;
**this one has callers**, so it must be GATED."*

**That claim is FALSE.** Verified 2026-08-28 across `src/`, `tests/`, `scripts/`:

```
grep -rn --include=*.ts --include=*.tsx --include=*.js --include=*.cs --include=*.ps1 \
         --include=*.http --include=*.json "v1/containers" src/ tests/ scripts/
```

**Zero callers.** The only hits are the registration itself, `OwningSecureRecord`'s doc comment, the
ArchTest, and historical endpoint-census JSON baselines under `projects/*/baseline/`. The `bundle.js` hits
from a looser earlier pattern were minified false positives — no `.ts`/`.tsx` source references it.

The route was nevertheless **gated, not deleted**, per the task instruction and the POML. Deleting a route is
a scope decision the task did not carry; gating is strictly safe and satisfies the forcing function.

---

## 4. ⚠️ Second contradiction found: the route cannot return real data today

Pre-existing, NOT introduced here, and NOT fixed here (out of scope) — but it materially qualifies the
POML's exploitability claim (*"Any authenticated caller can enumerate the document metadata of any container
by id"*).

`sprk_containerid` is **`NVARCHAR` Text on every entity**, holding an SPE container/drive id (`b!…`):

- `docs/data-model/sprk_financial-related-entities.md:74` — `sprk_document.sprk_containerid`, Text, max 500
- `…:204`, `…:221` — matter / project, Text, max 100
- `ProvisionProjectEndpoint.cs:668` — *"`sprk_containerid` is `NVARCHAR(100)` (live metadata) — a PLAIN
  STRING write"*
- `DocumentStorageResolver.cs:112-117` — SPE ids start `b!` and are 20+ chars

But the route validates `Guid.TryParse(containerId)` (`DataverseDocumentsEndpoints.cs:551`) and
`DataverseServiceClientImpl.GetDocumentsByContainerAsync:874` filters
`ConditionExpression("sprk_containerid", Equal, Guid.Parse(containerId))`.

**Consequence**: a real SPE container id 400s at the route; a GUID-shaped id matches no row. So the route
returns an empty list or a 400 for every input. The **structural** hole (a resource key with no
authorization) was real and is what task 074's rule detects; the **data-path** exploitability was blocked by
an unrelated type bug.

This also means the gate is currently belt-and-braces on this route — and that is the right state to be in:
the type bug is one line from being "fixed", and if it were fixed without this gate the disclosure would be
live. Gating first is the correct order.

**Filed for an owner**: reconcile the container-id type across
`DataverseDocumentsEndpoints.cs:551` + `DataverseServiceClientImpl.cs:874` with the `NVARCHAR` reality. Do
**not** do it without this gate in place.

---

## 5. Step 9.5 review outcome — one real defect found and fixed

An independent `code-review` + `adr-check` pass (categories: ADR-008 / ADR-003 fail-closed / ADR-010 / §11
reuse / captive dependency / disclosure / ADR-038 / double fidelity / the ArchTest edit / comment accuracy)
returned **no Critical findings**. ADR-008, ADR-003, ADR-010, §11 reuse, captive-dependency and ADR-038 all
came back clean, with the §11 claims verified by grep rather than assertion. Three things were acted on:

### 5a. 🔴 The denial was a four-way ORACLE (the real find)

The first version emitted a **distinct `errorCode` per branch** (`..._no_owning_record` /
`..._owner_not_authorizable` / `..._access_denied`) and let the resolver's `container_ownership_*`
`SdapProblemException` propagate as a **409** carrying *"More than one record claims this container"*.
Uniform prose with a discriminating code **is not uniform**: together those let an unauthorized caller
partition container ids by ownership state — including learning that a *secure* record claims one — **before
any rights check ran**. The sibling gate states the rule verbatim: *"the two cases must stay
indistinguishable to the caller in EVERY channel, not just the prose."*

**Fixed**: one `Denied(correlationId)` helper — one status (403), one detail, one code — for **every**
resource-side refusal, and the resolver's exception is now caught and folded into it. `Denied` takes no
parameter but the correlation id *by design*, so a future branch physically cannot supply a distinguishing
reason. New regression guard: `AllResourceSideRefusals_AreIndistinguishableToTheCaller`.

The §11 objection ("don't re-derive the status") was considered and does not apply: nothing re-derives a
status — `MiddlewarePipelineExtensions.cs:40` remains the only general `SdapProblemException` translation.
This route *collapses* its refusals, which is a different act. Diagnosability is preserved in logs (the
resolver logs both conditions at Error; the fold logs the problem code).

### 5b. Missing-bearer-token answered 403 and paid for a Dataverse round trip first

`AuthorizationService` already failed closed on a blank token, so this was never a hole — but a missing
**credential** was reported as an access **denial**, diverging from both siblings, and it ran after the
resolver's up-to-2×|securableEntities| uncached queries. **Fixed**: hoisted above the resolve, answers
**401** with its own code (credential-side, so correctly outside the resource oracle). New test:
`MissingCallerToken_IsDeniedAsUnauthorized`.

### 5c. A FALSE claim in my own comment

I had written *"Task 073 consumed the forward direction."* **False.** Verified by `git log`: the forward
direction's only consumer is `Services/Communication/Engine/CommunicationContainerResolver.cs`, and all
three of its commits are `(uac-r2 075)`. Task 073 consumed **neither** direction — it deleted
`Api/UploadEndpoints.cs`. Corrected in the filter's doc comment. (Called out here rather than quietly fixed,
since §3/§4 of these notes hold the POML to the same standard.)

Also applied from the review: test DisplayName reworded from *"still reaches the document listing"* to
*"the filter passes an authorized caller through"* (the stronger claim is false end-to-end per §4 — a scope
note now sits in the test-class remarks); pass-through asserted with a **sentinel** via `BeSameAs` instead
of `BeNull` (which was `null == null` against the double); container ids given an `_` so the fixture's
LIKE-escape round-trip is actually live; the ambiguity test re-pointed from the *mechanism* (a thrown
exception) to the *outcome* (refused, indistinguishably), which is what let 5a be fixed without a false
failure; and a dead `IsSecurableAsync` stub removed.

---

## 6. Perturbation checks (four ran; four bit)

The project has been burned by 45 dedicated tests that stayed green against a broken read, so every claim
below was executed, not reasoned.

| Perturbation | Expected | Observed |
|---|---|---|
| Rights check disabled (`HasFlag(None)`) | `CallerWithoutReadOnOwningRecord_IsDenied` reddens | ✅ 2 failed / 5 passed |
| `owner is null` → `next(context)` (075's assumed design) | `ContainerWithNoEstablishableOwner_IsRefused` reddens | ✅ 1 failed / 4 passed |
| Per-branch error code re-introduced | the uniformity guard reddens | ✅ 1 failed / 6 passed |
| Resolver 409 allowed to propagate (`when (ex.Code == "never")`) | ambiguity + uniformity tests redden | ✅ 2 failed / 5 passed |
| `.AddContainerDocumentAuthorizationFilter()` removed | ArchTest Rule A fails naming the route | ✅ *"Failed Task 074 Rule A … GET /api/v1/containers/{containerId}/documents"* |

All restored; all green after restore. (`when (false)` was rejected as a perturbation — CS8360/CS0162 under
warnings-as-errors — so a runtime-false filter was used instead.)

---

## 7. Verification

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | ✅ succeeded, **0 warnings**, 0 errors |
| Task 078 tests | ✅ **5/5** passed |
| Neighbours (`RecordContainerResolverTests`, `SecureContainerDecisionTableTests`, `EndpointAuthorizationCharacterizationTests`) + 078 | ✅ **56/56** passed |
| Whole `Sprk.Bff.Api.Tests.AccessControl` namespace (scoped regression) | 451 passed / **1 failed — pre-existing** |
| `RouteAuthorizationGuardTests` (all 10) | ✅ **10/10** passed |
| `Spaarke.ArchTests` full | 121 passed / **6 failed — identical to the clean-tree baseline** (verified by `git stash`) |
| Publish size (compressed, incl. 4 PDBs) | **45.12 MB** vs 44.96 baseline = **+0.16 MB**; ceiling 60 ✅ |
| `dotnet list package --vulnerable --include-transitive` | ✅ *"no vulnerable packages"* |
| Packages added | **none** (`git diff` on `*.csproj` / `Directory.Packages.props` empty) |

### Pre-existing test failure found by the scoped regression run (NOT task 078)

`DocumentDestroyAuthorizationTests.CheckoutFamilyRoute_ForCallerWithWriteRight_IsNotDeniedByAuthorization(route: "checkin")`
fails after **1 m 40 s** — a timeout signature, on the checkin route, unrelated to container listing.
Confirmed **identical on the stashed clean tree** (same test, same 1 m 40 s). Filed here so the next agent
does not re-diagnose it: only the `"checkin"` parameterization fails; the sibling `"checkout"` case passes.

### Baseline-failure attribution correction

The handoff described the 6 pre-existing ArchTest failures as all being in
`Sprk.Provisioning.ControlPlane.Core`. Accurate for 4 of 6 (`FR-27`, `FR-27 positive`, `FR-F1`, `FR-F2`).
The other two are separate pre-existing baseline breaches:

- **`ADR-010: Services should be concrete unless seam required`** — the 1:1-interface ratchet is *already
  breached* on master: *"count increased from 153 to 156"*. This is exactly the failure mode
  `OwningSecureRecord.cs:12-19` warns about ("would … make the next interface added anywhere in the BFF
  assembly fail the build blaming an unrelated project"). Confirmed present on the stashed clean tree, so
  not caused by task 078 — and a further reason this task added **no** interface.
- **`ServiceBusClientGuardTests.ServiceBusClient_IsConstructedOnlyInTheFactory`**

---

## 8. Placement Justification (root CLAUDE.md §10)

**In the BFF, and it could not be anywhere else.** This is an authorization filter on an existing BFF route;
the decision needs `HttpContext` (route values, caller claims, bearer token) and the BFF's OBO credential.
`Spaarke.Core` deliberately has no ASP.NET Core dependency (`LayerDependencyTests` guards it), so the filter
cannot live there. No new endpoint, no new package, no new DI registration, no background work, and no
CRUD→AI dependency — the filter composes three services that are already registered unconditionally
(`RecordContainerResolver` Scoped at `Program.cs:63`, `AuthorizationService` Scoped at `SpaarkeCore.cs:26`).
Unconditional attachment, so there is no ADR-032 asymmetric-registration question (§10 F.1).

**Component justification (§11)** — one NEW type, `ContainerDocumentAuthorizationFilter`:

- **Existing**: no filter authorizes a container-keyed route against its owning record.
  `DocumentAuthorizationFilter` resolves *document* rights and `ExtractResourceId` would hand it a container
  id — finding #4's wrong-domain shape, the exact defect task 073 retired. `SemanticSearchAuthorizationFilter`
  answers the same *question* but reads its subject from a request body, not a route value.
- **Extension**: not possible without making one of those two filters conditional on which resource key it
  found, which is how the wrong-domain bug got in.
- **Cost of doing nothing**: `GET /api/v1/containers/{containerId}/documents` remains structurally
  ungated — and per §4 it is one type-bug fix away from being a live cross-matter disclosure.

---

## 9. Successor obligations

1. **Wave 3 / `AccessibleRecordSetService`** — result trimming is the only correct control for a *shared*
   container's document list. Until it exists this route refuses that case (§2).
2. **Container-id type reconciliation** (§4) — must not land before this gate. When it lands, the
   `owner is null → 403` path gets its first exercise against production data, so pair it with a live check.
3. **Route-template ↔ `ContainerRouteParameter` coupling is untested.** If `{containerId}` is renamed in the
   template, the filter takes its no-container-id branch and denies 100% of requests — fail-closed, but a
   *silent outage*, and ArchTest Rule A cannot see it (Rule A only checks a filter is ATTACHED). Note the
   obvious fix is **forbidden**: building the template from the constant makes it an interpolated string,
   which Rule A's path regex (`^\s*\.Map\w+\s*\(\s*"([^"]*)"`) cannot read — the route would scan as
   `<unresolved>` and be reported as an ungated hole. Closing this properly needs one `WebApplicationFactory`
   request against the literal route. Recorded in the constant's own doc comment.
4. **ADR-010 ratchet is already over its ceiling** (153 → 156) on master. Not this task's to fix, but the
   next task that adds any interface to the BFF assembly will be blamed for it — and the ArchTest suite is
   red on this branch until someone owns it.
