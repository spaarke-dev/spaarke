# Task 008 — the delegation rule: who may change who can reach a record

> **Date**: 2026-08-22 · **Spec**: FR-07 · **Finding**: A-6 (High) · **Owner decision**: B-14
> design.md §6: *"This must ship before the + User button, or a read-only user gains a one-click path
> to Full Access on a confidential matter."*

---

## 1. What was wrong

`/api/v1/external-access` carried `RequireAuthorization()` and nothing else
(`ExternalAccessEndpoints.cs:109-111`). That gate asks *"are you anyone?"*. Behind it sat six writes:

| Route | What an arbitrary authenticated caller could do |
|---|---|
| `/grant` | Mint a Full Access grant on any project/matter/work assignment |
| `/invite-and-grant` | Same, plus provision a CIAM identity |
| `/invite` | Provision a CIAM identity and create/resolve a Contact |
| `/revoke` | Strip anyone's access |
| `/close-project` | Cascade-revoke every external grant on a project |
| `/provision-project` | **Create a Dataverse business unit** |

Every one then executed **app-only**, so nothing downstream re-asked the question either. Task 001
pinned it empirically: `/grant` answered `400` for a malformed body, which can only happen inside the
handler — the caller had passed every gate in front of it.

## 2. The rule

> **You may change who can reach a record only if you hold `Write` on that record**, evaluated as the
> caller. (B-14)

One filter, attached at the **group**, parameterized by target-resolution (ADR-010's constraint —
*"not six bespoke filters"*).

### Route → target record

| Route | Target | How it is resolved |
|---|---|---|
| `/grant` | the grant root (project·matter·work assignment) | `GrantExternalAccessEndpoint.ResolveGrantRoot` |
| `/invite-and-grant` | same | same, via the shim `InviteAndGrantExternalUserEndpoint` already builds |
| `/invite` | same | same |
| `/revoke` | the **root of the row named by `AccessRecordId`** | `RetrieveRowAsync` → `DeriveKey` |
| `/close-project` | `sprk_projects(ProjectId)` | direct |
| `/provision-project` | `sprk_projects(ProjectId)` | direct |

**Dispatch is on the bound request TYPE, not the path.** Each route binds a distinct DTO
(`/invite` and `/invite-and-grant` share one, and correctly share a target). Path strings would have to
be duplicated out of five other files and would drift silently. The `switch` has a **default that
denies**, so a seventh route added to this group tomorrow is gated from its first request rather than
inheriting the A-6 hole. That failure is loud and immediate; the alternative is silent.

### Two resolutions that are load-bearing

**`/revoke` follows the row, not the body.** The request names an *access record*; the record whose
access changes is that row's root. The body also carries a legacy `projectId` — checking THAT would let
a caller with Write on any project of their choosing revoke grants on a matter they cannot touch.
Pinned by `PostRevoke_ChecksTheCallersWriteOnTheGrantRowsRoot_NotTheRequestBody`.

**`/grant` follows `recordType`+`recordId` over the legacy `projectId`.** Same shape: authorizing
against a field the write will ignore authorizes the wrong record. Pinned by
`PostGrant_WithMatterRoot_ChecksTheCallersWriteOnThatMatter`.

## 3. Why a new probe rather than the existing authorization path

The POML's `<notes>` allowed either; only one actually works.

**`AuthorizationService` cannot answer this question.** It routes to `DataverseAccessDataSource`, which
hard-codes `sprk_documents({resourceId})` in BOTH its `RetrievePrincipalAccess` target (`:387`) and its
fallback read probe (`:461`). Asked about a project it returns `None` for every caller, however
privileged — the filter would deny universally and fail the no-over-denial criterion. Generalizing that
seam changes `IAccessDataSource` for every consumer and is **task 032's** scope.

> Side note worth keeping: `EntityAccessFilter` (the POML's named canonical reference) already passes a
> type-qualified `"{entityType}:{entityId}"` resource id into that same document-only data source. On
> the evidence above it can only ever resolve `None`. Not investigated further — out of scope — but
> recorded as an owner item because it means the Office save path's entity check may be inert.

**`IDataverseUserClient` is the right shape but unusable.** Entity-generic, OBO-only, fail-closed — and
registered inside a compound AI gate AND behind `ToolFramework:Enabled`
(`AnalysisServicesModule.cs:209`, `:1664`). Six unconditionally-mapped routes depending on a
twice-gated service is the asymmetric-registration anti-pattern (CLAUDE.md §10 F.1 / ADR-032) with the
worst possible blast radius, and it would be a CRUD→AI dependency besides (§10 bullet 3).

**`DataverseWebApiClient`** — already injected into all six handlers — is app-only. An app-only Write
probe answers *"can the application write"*, which is finding A-2 rebuilt.

So: `CallerRecordAccessProbe`, in `Infrastructure/ExternalAccess/` beside `ExternalGrantLifecycle`.

### Caller-scoped by construction, not by parameter

Two OBO calls: `WhoAmI()` for the principal, then `RetrievePrincipalAccess` for the rights.

`WhoAmI` matters more than it looks. `RetrievePrincipalAccess` takes the principal as an **argument**, so
an app-only implementation would carry the caller's identity as *data* — and a wrong or defaulted id
would silently answer about the wrong person. That is the exact shape that let A-2 survive. Under an OBO
token `WhoAmI` cannot name anyone but the caller, so the identity is the **credential** and there is no
id to get wrong. It also needs no privilege beyond being a user, and no oid→systemuser mapping that can
miss.

### No Read-shaped consolation prize

Every failure path returns `AccessRights.None`: no token, OBO rejected, `WhoAmI` unavailable,
`RetrievePrincipalAccess` unavailable, unparseable body, transport error.

There is deliberately **no fallback to a record read**. `DataverseAccessDataSource` may degrade to a read
probe because it is answering *"can you see this document"*. This type may not, because it is answering
*"may you hand this record to someone else"* — and treating Read as licence to grant is precisely the
privilege escalation FR-07 exists to close. Pinned by
`PostGrant_ForCallerHoldingEveryRightExceptWrite_IsStillDenied`.

**Consequence, stated plainly**: a systematic `RetrievePrincipalAccess` outage denies all six mutations
rather than silently widening them. Failures log `DELEGATION-RPA-UNAVAILABLE`. Live verification of the
function under a delegated token is **task 034**, which already carries the same obligation for task
005's document-path use of it — the two now share one failure mode and one verification.

## 4. Escalation triggers — both evaluated

| Trigger | Fired? | Why |
|---|---|---|
| **`provision-project` may have no target record** | **No — the premise is false** | The handler's own Step 1 requires the project to already exist with `sprk_issecure = true` (`ProvisionProjectEndpoint.cs:91-129`). There IS a target, so the ordinary rule applies and no role model had to be invented. See the residual question below |
| **Revoke needs an extra read** | **No** | The handler already performs the identical `RetrieveRowAsync` as its own first step (task 010). One extra Dataverse GET on a low-volume admin mutation does not "materially change request cost" |

**Why the duplicate read is not passed through `HttpContext.Items`.** It could be. It should not be: a
handler that trusted a row cached by a filter would be trusting authorization state it cannot verify,
and the coupling would make the filter load-bearing for handler correctness rather than only for
admission.

### Residual question for the owner — `provision-project`

The rule now applied is *Write on the project*. That is strictly better than bare
`RequireAuthorization()` and satisfies FR-07's fifth acceptance criterion. But provisioning **creates a
business unit** — a tenant-level security object — and it is a fair question whether Write on one
project should confer that. The escalation trigger forbade inventing a role model silently, so none was
invented; the question is surfaced instead (owner item, §7).

## 5. `/invite` — a deliberate behaviour change

`/invite` writes no grant row, so it was arguable whether FR-07 reaches it. It does: it provisions a
**CIAM identity** and creates/resolves a Contact, and identity provisioning is a privilege. The DTO
already carries the root, and the only first-party caller
(`external-spa/src/auth/bff-client.ts` `InviteUserRequest`) sends `projectId` as a **required** field.

The change: `/invite` now requires a resolvable root, where since task 070 it tolerated none. Nothing
that exists today sends a rootless invite. Recorded here because it is a contract narrowing, not a pure
addition.

## 6. Placement Justification (CLAUDE.md §10) + §11

| Component | Placement | Why |
|---|---|---|
| `DelegationRuleFilter` (new) | `Api/ExternalAccess/` | Beside the routes it gates; ADR-008 says filters live at route registration |
| `CallerRecordAccessProbe` (new) | `Infrastructure/ExternalAccess/` | Beside `ExternalGrantLifecycle`; it is infrastructure (HTTP + MSAL), not endpoint code |
| `DataverseAccessRightsMapper` | `internal` → `public` | Second production consumer, different assembly |
| DI | `ExternalAccessModule` | Where the rest of this surface registers. **Unconditional** — see §7 |

**§11 three questions, for `CallerRecordAccessProbe`.** *Existing*: `AuthorizationService` /
`IAccessDataSource` overlap in intent but are document-only; `IDataverseUserClient` overlaps in
mechanism but is feature-gated and AI-internal. *Extension*: generalizing `IAccessDataSource` is the
right long-term move and is **task 032's** — doing it here would change a shared seam for every consumer
mid-Phase-0. *Cost of doing nothing*: without an entity-generic caller-scoped rights read there is no
way to evaluate B-14 at all, and the "+ User" button (task 065) ships onto an ungated endpoint.

**§11, for `DelegationRuleFilter`.** *Existing*: `EntityAccessFilter` is the nearest neighbour.
*Extension*: no — it is Office-save-specific (binds `SaveRequest.TargetEntity`) and would have to absorb
six heterogeneous DTOs from another feature. Pattern reused, type new. *Cost of doing nothing*: A-6
stays open.

**Why the mapper went public rather than `InternalsVisibleTo("Sprk.Bff.Api")`.** The alternative exposes
every internal in `Spaarke.Dataverse` to the BFF to share one pure function. The other alternative — a
second copy of the name→flag table — is exactly how an `AppendAccess`/`AppendToAccess` transposition
gets introduced in one of them, which task 003 filed as a binding obligation precisely because it is
silent.

## 7. ADR compliance

| ADR | Verdict |
|---|---|
| ADR-001 / 007 / 009 / 013 / 019 / 038 | ✅ |
| **ADR-008** | ✅ Endpoint filter at route registration, no middleware |
| **ADR-010** | ✅ ONE filter parameterized by target-resolution; concrete registrations, no interface — the substitution seam is `virtual`, per the `DataverseWebApiClient` precedent (ADR-038 §4) |
| **ADR-028** | ✅ on OBO — the rights come from the caller's own token, never app-only |
| **ADR-003** | ⚠️ **Known tension, already owned.** ADR-003 says *"MUST implement new auth logic as `IAuthorizationRule`"* and *"MUST NOT create new service layers for auth"*. The project's own ADR-tensions table already routes ADR-003 to a **path-B amendment (task 030)** on exactly this ground: the rules-only shape *"cannot carry rights or vetoes"*. This filter is another instance of the same tension, not a new one. Cited, not silently violated |
| **ADR-028 A4** | ❌ **NEW `.WithClientSecret(...)` site — needs an owner decision.** See below |

### 🔔 ADR Conflict — ✅ RESOLVED: path A accepted by the owner, 2026-08-23

> *"for ADR-028 yes add it and we will address as part of the broader MI migration."*
> Recorded in [`design.md` §9 ADR tensions](../design.md). The site is now #8 on the E-3 migration
> list; net operational impact today is none — it reads the same `AzureAd:ClientSecret` the other
> seven already require, so it cannot fail independently of them.

The conflict as raised:

- **ADR in question**: ADR-028 Amendment A4 — secret-free confidential credential
- **Specific rule**: no `.WithClientSecret(...)` on a BFF-identity client; use an MI-FIC assertion or a
  Key Vault certificate. Exception **E-3** covers enumerated transitional sites and explicitly
  *"does not license expansion"* — a NEW site must be flagged.
- **Conflict**: `CallerRecordAccessProbe` performs an OBO exchange, and ADR-028 A4 itself notes that
  `DefaultAzureCredential` **cannot** perform that exchange — a confidential credential is required.
  There is **no `WithClientAssertion` anywhere in the repository**: all seven existing OBO/confidential
  sites use `WithClientSecret`, so complying would mean inventing the shared MI-FIC assertion provider
  inside an authorization filter, in a task about delegation.
- **Proposed path**: **A — project-scoped exception.**
- **Rationale**: the new site uses the same configuration keys and the same shape as the two nearest OBO
  precedents (`DataverseUserClient`, `DataverseAccessDataSource`), so when the shared MI-FIC provider is
  built all eight sites migrate together. Building it here would be a large, unscoped change on the
  authorization path.
- **Impact if accepted**: one additional site on the E-3 migration list — 7 → 8.
- **Alternative considered and rejected**: **Path C** (build MI-FIC now) — correct eventually, wrong
  here: it is its own task, touching every OBO call site, and would put unproven credential plumbing
  under a security gate on its first day. **Path B** (amend A4) — no: the rule is right; we simply have
  not built the mechanism yet.

**Publish size**: 43.69 MB compressed incl. PDBs (prior 43.66, baseline 44.96, ceiling 60) — **+0.03 MB**,
no packages added. No vulnerable packages.

## 8. Test coverage

`tests/integration/auth/UnifiedAccessControl/` — the ADR-038 §2 security-auth KEEP path. (The POML named
`tests/unit/Sprk.Bff.Api.Tests/AccessControl/…`, which is not a KEEP path and does not exist; the
project's POML paths have been wrong on tasks 002, 005 and 006 as well.)

- `DelegationRuleCharacterizationTests.cs` — 19 tests
- `DelegationRuleTestFixture.cs` — substitutes the probe at its `virtual` seam
- `EndpointAuthorizationCharacterizationTests.cs` — two task-001 characterizations flipped
- `ExternalAccessContractTests.cs` — fixture caller given Write (see below)

### The probe MUST be substituted, or the suite is vacuous

Offline the real probe correctly answers "no rights" for everyone, so every request 403s — and a test
asserting 403 would pass equally against a filter that denied unconditionally, or against no filter at
all. Every negative here has a **positive twin differing only in the caller's rights**. The rights are
stated through the bearer token (`Bearer rights=ReadAccess,WriteAccess`), which is what the real probe
consumes, so the double stays a function of the credential and the fixture stays immutable and
shareable.

The flipped tests in `EndpointAuthorizationCharacterizationTests` use the REAL probe and therefore
cannot discriminate on their own; their doc comments say so and point at the file that can. Neither is
sufficient alone.

### Verified empirically, not argued

| Perturbation | Result |
|---|---|
| Detach `.AddDelegationRuleFilter()` from the group | **17 of 36 fail** |
| Weaken the rule to "any rights at all" (`rights != None`) | **8 of 36 fail** |
| Resolve `/revoke`'s target from `request.ProjectId` instead of the row's root | **1 of 19 fails** — exactly the test that isolates it |
| All restored | 36/36 |

### Two pre-existing contract tests had to be updated

`ExternalAccessContractTests.InviteAndGrant_*` began returning 403 — correctly: their fixture has no
substituted probe. They assert what `/invite-and-grant` **does for an entitled caller**, not who is
entitled, so the fixture's caller was given Write via `EntitledCallerRecordAccessProbe`. The production
rule was not weakened to accommodate them.

### A test-host fact worth knowing

`/invite` and `/invite-and-grant` initially answered **500 before the filter ran**: Minimal API resolves
a handler's DI arguments BEFORE the endpoint-filter pipeline, and `CiamUserProvisioningService`'s
constructor throws without `Ciam:Domain`. Not a security hole (nothing is written), but it means an
endpoint filter cannot be relied on to run when DI is broken — and a 403-free assertion on such a route
would have proved nothing. The fixture now supplies the CIAM keys.

## 9. Follow-on obligations

| # | Obligation | Owner |
|---|---|---|
| 1 | ~~**`provision-project`** gate question~~ — **RESOLVED 2026-08-23**, see §10 | ✅ done |
| 2 | `RetrievePrincipalAccess` under a delegated token is still unverified against a live tenant. It now gates SIX mutation endpoints as well as the document read path. Grep production logs for `DELEGATION-RPA-UNAVAILABLE` and `RPA-FALLBACK` | **task 034** |
| 3 | ~~`EntityAccessFilter` may be inert~~ — **CONFIRMED AND FIXED 2026-08-23**, see §10 | ✅ done |
| 4 | When `IAccessDataSource` is generalized to non-document entities, `CallerRecordAccessProbe` should collapse into it — it exists only because that seam is document-only | **task 032** |
| 5 | `IAccessDataSource` is **Scoped** over a transient typed HttpClient, which is what makes `DataverseAccessDataSource`'s `DefaultRequestHeaders.Authorization` mutation safe. Making it a singleton would turn that into a cross-user OBO-token bleed. Invariant, not a defect | **task 032** (that file) |
| 6 | The PCF "+ User" button (**task 065**) is now unblocked: FR-07's gate is in place | task 065 |

---

## 10. Owner-authorised follow-ups (2026-08-23)

Three items were surfaced at task-008 review. All three are now closed.

### 10.1 `EntityAccessFilter` — confirmed inert, fixed

**Confirmed before fixing.** `EntityAccessFilterCharacterizationTests` was written first and run against
the unchanged code: **7 of 9 failed**, `ProbedTargets` was **empty**, and a caller Dataverse reports as
holding `AppendToAccess` on the target matter received **403**. So the reading was right —
`POST /api/office/save` carrying a `targetEntity` was refused for every caller, however privileged.

**User-visible effect while broken**: filing an email or document to a matter from the Outlook/Word
add-in failed; filing *without* choosing a matter worked, because the filter short-circuits when
`TargetEntity` is null. That asymmetry is why it read as flakiness rather than a systematic break.

**The fix.** `EntityAccessFilter` now resolves the target's own Dataverse collection from a closed map
and asks `CallerRecordAccessProbe` — the entity-generic, caller-scoped (OBO) rights read built for
task 008. `OperationAccessPolicy` remains the single authority for *which* right
`entity.associate_document` costs (`AppendTo`); only the **source** of the rights changed. So there is
still exactly one place that decides what the operation requires.

The old `IsValidEntityType` boolean is gone, folded into the entity-set map. Validating a type and
knowing where to look it up are the same question; keeping them apart is how a type gets accepted with
nowhere to check it.

**Why the probe rather than fixing `IAccessDataSource`**: that is task 032's seam change, and this
filter should go back through `AuthorizationService` once it lands — noted in the code so the temporary
second access path is not mistaken for the intended shape.

**Verified by perturbation**: pointing the check back at `sprk_documents` fails **6 of 9**.

### 10.2 `provision-project` — Write is the right gate; the risk was idempotency

**The owner asked whether the scope should be `Create` rather than `Write`.** It should not, for two
reasons:

1. **Dataverse cannot express it.** `CreateAccess` is an entity-level privilege (`prvCreateX`), not a
   right a principal holds *on an existing record*. `RetrievePrincipalAccess` answers about a specific
   record, and for a record that already exists it does not report `CreateAccess`. Requiring it would
   deny every caller and break secure-project creation outright.
2. **Write is what the endpoint actually needs.** Provisioning's final act (`StoreProjectReferencesAsync`)
   is an `UpdateAsync` stamping three fields onto the project row. Requiring Write means the caller can
   perform, themselves, the write the endpoint performs on their behalf. That is the correct
   correspondence — anything less and the endpoint launders a write the caller could not do.

Also worth stating: the business unit is created by the **app-only identity**, so the caller's own
BU-create privilege is never consulted by Dataverse regardless of what we check. Expressing "may create
business units" would require either an entity-level privilege check on the caller (a different
mechanism) or an App Role — and an admin role was explicitly ruled out.

**So the real exposure was never the gate — it was that the operation was not idempotent.** That is now
fixed, which removes the damage the gate question was worried about.

**The guard.** Step 1b refuses with **409** when the project already carries `sprk_securitybuid` OR
`sprk_specontainerid`. EITHER reference is enough: the reference-stamping step is non-fatal, so a
half-provisioned project is a real state — and it is the state where a blind re-run does the most
damage, because the unrecorded half becomes invisible garbage. The 409 names the existing BU and
container so an operator can tell "already done" from "something is wrong" without guessing.

**How double-provisioning was reachable** (the owner's question): not by double-clicking. Nothing on the
path deduped — not the client (`provisioningService.ts` posts once and does not retry, but neither does
it dedupe), not the route (unlike `/office/save`, this group has no `IdempotencyFilter`), not Dataverse.
Provisioning makes three slow remote calls, so a client or proxy timeout mid-flight is ordinary, and
Step 5 is deliberately non-fatal — a run that failed to record its own output still returns 200 having
created real infrastructure. Both cases invite exactly one thing: run it again.

**Verified by perturbation**: disabling the guard fails **4 of 5**.

### 10.3 The replication-lag 403

The wizard calls `/provision-project` within seconds of creating the project row, so the delegation
check can ask Dataverse about a record that has not replicated. `CallerRecordAccessProbe` now retries a
**bounded** schedule (400 ms, then 1200 ms) when Dataverse reports the target as not found.

**The tradeoff is real and is documented in the code.** Under OBO, Dataverse answers "not found" both
for a record that does not exist yet AND for one the caller cannot see — the two are indistinguishable
by design. So the backoff also lands on genuine no-access denials, costing them ~1.6 s. Acceptable
*here specifically*: these are six low-volume admin mutations plus the Office save gate, none
latency-sensitive — and a caller who can SEE the record but lacks rights gets a 200 with a rights
string, so the common denial is not the one being slowed.

The schedule is a pure `internal static` (`NotFoundRetryDelay`) so it is assertable without a transport
mock (ban B1), tested at `tests/unit/domain/Auth/`. One of those tests caps total added latency below
2 s — the number that has to stay defensible, so a well-meant extra retry reads as a regression.

`IStorageRetryPolicy` already exists for this lag class but its 2s/4s/8s schedule is far too slow to sit
in front of an authorization decision; hence a separate, much shorter one.

### 10.4 `tests/integration/data-mutation/**` backfilled

The provisioning guard is a write-path idempotency test, which belongs in the `data-mutation` KEEP path
— and that path turned out to be **the last of the seven with no csproj glob**. Worse than empty: a test
placed there compiled nowhere and ran never, so an author following `tests/CLAUDE.md`'s routing table
would have got silent zero coverage. Now globbed, matching the auth backfill task 001 did.

**All seven ADR-038 KEEP paths now compile.**

### 10.5 Gates

BFF **10,715 passed / 0 failed** (+20 net) · ArchTests 36/36 · Core 45/45 · publish **43.69 MB**
compressed incl. PDBs (unchanged) · no vulnerable packages.
