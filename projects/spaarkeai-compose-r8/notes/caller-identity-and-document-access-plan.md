# Plan — Caller-identity resolution + document access model

> **Created**: 2026-08-27 · **Status**: PLAN, not yet executing
> **Origin**: UAT 2026-08-26 defect D-6 (see [`uat-2026-08-26.md`](uat-2026-08-26.md)), which opened into two
> distinct problems large enough to need their own tasks.
> **Why this file exists**: the owner asked for a durable plan so execution does not drift if context is
> refreshed mid-flight. **Read this before touching any identity or document-access code.** Everything in
> §2 is verified — do NOT re-derive it, and do not let a fresh session "discover" it differently.
> **No other project owns this work.** It was found here, so it is planned here.

---

## 1. The two problems, stated separately

They were found together and share a root cause, but they are independent and must not be conflated.

| | Problem | One-line statement |
|---|---|---|
| **P1** | **Caller-identity resolution** | Large parts of the BFF resolve the caller as Entra `sub` where Dataverse requires Entra `oid`, producing denials, 401s, over-disclosure, silent no-ops and uncorrelatable audit — depending on what each site does with the value. |
| **P2** | **Document access model** | `sprk_document` is the practical file-level security boundary, but nothing grants access to it. Access does not flow from the parent record (verified: zero parental relationships), there is no per-document sharing, and 74% of documents are owned by service principals. |

**P1 makes the gate ask the wrong question. P2 means the answer would be wrong anyway.** Fixing P1 alone
leaves the boundary nominal; fixing P2 alone leaves it asking about the wrong person. Both are required.

---

## 2. Verified facts — the evidence base (do NOT re-derive)

Each row was confirmed against code or the live `spaarkedev1` environment on 2026-08-26/27.

### 2.1 The claim mechanism

| Fact | Evidence |
|---|---|
| Inbound claim-type mapping is **ON** | `MapInboundClaims` is never assigned anywhere in `src/` or `tests/`; only 3 comment mentions. Confirmed empirically by the production log below. |
| Mapping renames claims: `sub`→`ClaimTypes.NameIdentifier`, `oid`→`…/claims/objectidentifier`, `tid`→`…/claims/tenantid` | .NET `DefaultInboundClaimTypeMap` |
| Dataverse joins the caller via `systemuser.azureactivedirectoryobjectid`, which holds **`oid`** | `Spaarke.Dataverse/DataverseAccessDataSource.cs:268,383` |
| Production proof | `sub d12L59FR…rkjg → AccessRights: None → DENIED` · `oid c74ac1af-… → RetrievePrincipalAccess SUCCESS, GrantedAccess=Read,Write,Delete,Create,Append,AppendTo,Share` |
| The log parameter is literally named `AzureAdOid=` | the code names what it expects and was handed something else |

**FOUR broken shapes** — the last two are the ones that look correct:

```csharp
FindFirst(NameIdentifier)                        // → sub
FindFirst(NameIdentifier) ?? FindFirst("oid")    // → sub; the ?? tail is DEAD (sub always present)
FindFirst("oid") ?? FindFirst(NameIdentifier)    // → sub; short "oid" DOESN'T EXIST under mapping
FindFirst("oid")                                 // → null
```

> The third shape carries the comment *"prefer Entra 'oid' claim for stability"*. The intent is right, the
> order is right, and it still resolves `sub`. **The most conscientious-looking site is broken.**

### 2.2 Three identity spaces (the conceptual fix)

| Space | Value | Joins to | Legitimate uses |
|---|---|---|---|
| Entra **`oid`** | tenant-stable GUID | `systemuser.azureactivedirectoryobjectid` | anything crossing into Dataverse |
| Entra **`sub`** | pairwise, per-application | **nothing outside this app** | local opaque key only — rate-limit partition, idempotency scope, cache key |
| Dataverse **`systemuserid`** | Dataverse PK | `ownerid`, `createdby`, every lookup | ownership/sharing comparisons |

**Nobody ever chose `sub`.** There is no site where pairwise semantics were selected deliberately; every one
is `ClaimTypes.NameIdentifier` written to mean "the user" with mapping silently substituting `sub`.
`azureactivedirectoryobjectid` is a **Microsoft platform column**, not a Spaarke design choice.

`PortfolioService` is a **space-3** bug, not space-1/2 — so "use oid everywhere" is also wrong.

### 2.3 The document access model

| Fact | Evidence |
|---|---|
| SPE enforces **containers only** | only per-item Graph calls are `CreateLink`; no per-item permission grants |
| The user genuinely HAS container access | all five URL-minting routes reach SPE via `*AsUserAsync → ForUserAsync` (real OBO) |
| The BFF is the **sole client path** | clients get `api://{bff}/SDAP.Access`; OBO to `FileStorageContainer.Selected` is server-side and never reaches the client |
| ⇒ the `sprk_document` row is the practical file-level boundary, enforced at the **API layer** | — |
| **ZERO of 27** `sprk_document` relationships are Parental | live metadata query 2026-08-27: all `Share=NoCascade, Assign=NoCascade`, including both `sprk_matter` and both `sprk_project` lookups |
| No per-document sharing in the normal path | `GrantAccess`/`RevokeAccess`/POA appear only in the ExternalAccess module |
| Ownership is mostly service principals | `SDAP-BFF-SPE-API` **318** · Ralph Schroeder **115** · `# mi-bff-api-dev` **13** (74% SP-owned) |
| Dev org masks it | the tester has full rights on an MI-owned document → role scope is org-wide here |

**Consequence.** Document access resolves to one of two things, neither of which is per-document:

- **Org-scoped role** → user sees every document; the gate ≈ container access, i.e. nominal
- **User-scoped role** → user sees only what they own, and they own ~26% of documents → locked out of their own uploads

### 2.4 Why this was never caught

| Fact | Evidence |
|---|---|
| **45 test fixtures** assign `oid` and `NameIdentifier` the **same constant** | full audit; only 5 are divergent |
| No fixture can reproduce the mapping state at all | test auth handlers mint `ClaimsPrincipal` directly, bypassing the JWT handler where mapping lives |
| This class has bitten **at least three times** | OFFICE_009 (`OfficeEndpoints.cs:215-224`) · F8 (`unified-access-control-r2` notes) · UAT 2026-08-26 |
| OFFICE_009 was **shadowed, not fixed** | a correct source was placed in front; the broken chain remains as a live fallback |
| F8 named the exact file and line, then the **same project armed it** | F8 cites `DocumentAuthorizationFilter.cs:48`; task 022 (`f076b1e38`) then attached that filter to five more routes. It was catalogued as "another silent-deny source", not escalated as a blocker. |
| Three repo comments assert "MIW v3+ defaults `MapInboundClaims=false`" — **false** | `AuditLogMiddlewareTests.cs:233`, `AuditLogMiddleware.cs:313`, `AuditEnrichmentMiddleware.cs:90-92` |

### 2.5 Why `f076b1e38` exists (it was correct)

`unified-access-control-r2` task 022. SPE permission is container-scoped and coarser than per-document
Dataverse rights, so a caller with container access but no Read on the row previously succeeded. That is a
genuine cross-client disclosure, and spec FR-01 exists to close it. **The gate is right.** It simply
depends on a filter that F8 had already documented as broken.

---

## 3. Design decisions

### D-1 — Three named resolvers; no fallback ranking (supersedes the current `CallerResolution`)

The shipped `CallerResolution` is `oid ?? schema-oid ?? NameIdentifier`. **The tail is the OFFICE_009
pattern**: it does not remove the wrong approach, it ranks it — and silent fall-through is what caused
every one of these bugs. Its only real consumer is test fixtures that set `NameIdentifier` alone (Entra
callers always have an `oid`; `ApiKeyAuthenticationHandler` mints neither claim, so it returns null
regardless). The tail's only beneficiary is the blind spot we are removing.

```
ResolveObjectId(principal)        -> oid (either claim form) or NULL. NO NameIdentifier tail.
                                     NULL => 401 explicitly. Never a guess, never a fallback.

ResolveOpaqueCallerKey(principal) -> a LOCAL key, documented as such. Legal for rate-limit
                                     partitions, idempotency scope, cache keys. MUST NOT cross
                                     an application boundary.

ResolveSystemUserId(oid)          -> Dataverse systemuserid. Required wherever the value is
                                     compared to ownerid / createdby / any Dataverse lookup.
                                     BriefingService already does this correctly — hoist it.
```

**One identity per purpose, named for its purpose.** A site needing a Dataverse row comparison calls the
third function and *cannot* accidentally receive an Entra id — which makes the `PortfolioService` class of
bug structurally impossible rather than merely fixed.

### D-2 — Access flows from the parent record (owner decision, 2026-08-27)

> *"If the user has access to the parent record (e.g., matter or project) then access will flow to the
> Document."* — and explicitly **NO** per-document explicit sharing.

#### ⛔ REVISED 2026-08-27 — cascade is NOT the mechanism. Do not implement it.

An earlier draft of this plan proposed Parental/configurable cascading on `sprk_matter_document`.
**That was wrong**, and the correction is recorded here rather than silently replaced because the
reasoning matters:

1. **Cascade propagates EVENTS, not evaluated access.** `Assign` fires on a parent **owner change**;
   `Share`/`Unshare` fire on **new explicit shares** (pre-existing shares do not replay). A user who
   reaches the Matter through **role or business-unit scope** — the normal case — receives **nothing**
   on its documents. Cascade therefore cannot express "has access to the parent ⇒ has access to the
   child," which is precisely the stated rule.
2. **Dataverse permits ONE parental relationship per child table.** `sprk_matter` and `sprk_project`
   cannot both be parental. Choosing matter leaves every project-filed document exactly as it is now.
   `sprk_invoice`, `sprk_workassignment`, `sprk_relatedcommunication` and the self-referential
   `sprk_parentdocument` are permanently excluded from the slot.
3. **Orphans are the dominant case, not the edge.** Documents created with NO matter/project lookup:
   REST create (**always**), all five communication-archive paths (`sprk_relatedcommunication` only),
   Compose promote for non-PDF sources, upload-worker "document-only save", Office save without
   `TargetEntity`, email attachments without an association, and the client's unassociated mode.
   Cascade is a no-op for every one of them.

#### The mechanism that DOES implement the rule: parent-fallback authorization in the BFF

`DocumentAuthorizationFilter` already resolves the caller and asks `IAccessDataSource` about the
document row. Extend it: **when the caller lacks the required right on the document, evaluate the same
right on the document's parent (`sprk_matter` / `sprk_project`) and allow on success.**

Why this is the right shape:

- It expresses the owner's rule **literally**, including the role/BU-scope case that cascade cannot reach.
- It works for matter **and** project simultaneously — no parental-slot conflict.
- **No schema change, no cascade, no backfill** for associated documents.
- It reuses the existing `AuthorizationService` → `IAccessDataSource` path, including the access cache,
  so the cost is one additional cached lookup on the miss path only.
- The BFF is the sole client path (§2.3), so an API-layer rule is the same boundary the current gate
  already relies on — this adds no new trust assumption.

Open sub-decisions: the orphan rule (Q3), and whether the fallback applies to **write** operations or
reads only.

#### Ownership: de-scoped, but still wrong

With parent-fallback authorization, ownership stops being the access mechanism. It remains worth fixing
for **audit correctness and UX**, and one item is a genuine bug:

- Server creates are **app-only** — `IDataverseService` is a singleton connecting with MI/client
  credentials, and **no server path sets `ownerid` or impersonates**. Hence 318 + 13 SP-owned rows.
- Client creates (upload wizards, `EntityCreationService`, manual MDA forms) run as the signed-in user →
  user-owned. Hence the 115. **Two ownership regimes depending on which path created the row.**
- 🐛 **`DataverseDocumentsEndpoints.cs:53-78` publishes a `MembershipChangedEvent` with
  `SourceField="ownerid", Role="owner", PersonId=<caller oid>`, commented as "defaulted by Dataverse to
  the OBO caller". That comment is false** — the create is app-only, so the MI owns the row. The
  membership junction is being told a human owns a row the service principal owns.

---

## 4. Workstreams and tasks

### Workstream A — Stop the bleeding (P1 live defects)

Fix by routing every site through the D-1 resolvers. **Not 20 hand-written chains — that is how we got here.**

| Task | Site | Failure | Confidence |
|---|---|---|---|
| A-1 | `WorkspaceAuthorizationFilter` → `PortfolioService` | **disclosure** — all active matters to any caller | verified in code + singleton DI proves app-identity |
| A-2 | `WorkspaceLayoutEndpoints` (+ orphaned filter) | ownership bypass on `GET /layouts/{id}` | agent-traced — **CONFIRM before claiming** |
| A-3 | `PlaybookEndpoints` ×6 | 401 for every caller | verified in code |
| A-4 | `PermissionsEndpoints` | every capability `false` → UI lockout | agent-traced |
| A-5 | `DocumentAuthorizationFilter` + 10 siblings | 403 on 8 document routes | **already in PR #832** — rework onto D-1 |
| A-6 | `EventEndpoints:400`, `DataverseDocumentsEndpoints:40` | membership events silently never published | verified in code |
| A-7 | `RateLimitingModule` | per-user limiting degraded to per-IP | agent-reported |
| A-8 | `ComposeService:3408`, `GrantExternalAccess`, `CommunicationsEndpoints` ×3, Workspace audit family | notification never sent; audit lineage lost | agent-traced |
| A-9 | `OfficeEndpoints` ×9 latent tails | dead today; live if a route is ever mapped without the filter | verified — this is the OFFICE_009 shadow |

### Workstream B — Make it detectable (do this WITH A, not after)

| Task | What |
|---|---|
| B-1 | Give `ClaimTypes.NameIdentifier` a **non-GUID, sub-shaped** value in the ~10 shared test handlers. Divergent-but-both-GUIDs is not enough; a sub-shaped value makes every `Guid.TryParse` identity path fail LOUDLY. One bounded edit converts all 45 collapsed fixtures from masking to detecting. |
| B-2 | Keep/extend `CallerObjectIdResolutionTests` (already written, in PR #832). It asserts **which identifier reached `IAccessDataSource`**, not merely that access was granted — a status-code-only assertion passes against the broken code. Verified non-vacuous. |
| B-3 | Add a regression test for the disclosure direction: a caller whose identity fails to resolve must produce **zero rows**, never an unfiltered query. This is the assertion that would have caught `PortfolioService`. |

### Workstream C — Make it non-recurring

| Task | What |
|---|---|
| C-1 | **Census ArchTest** in `tests/Spaarke.ArchTests`: fail the build when a *new* file reads `NameIdentifier` for identity. `SourceScan` already reassembles multi-line `??` chains; `CredentialGuardTests` is the allowlist-with-reason pattern to copy. A census beats a claim-order parser (too many allowlists). |
| C-2 | Correct the three false "MIW defaults mapping off" comments. Prose that misstates the mapping state is how this returns a fourth time. |
| C-3 | Delete the shadowed fallbacks rather than leaving them ranked (the OFFICE_009 lesson). |

### Workstream D — Document access model (P2)

**Revised 2026-08-27** after the ownership/parentage audit. Cascade tasks are struck; see D-2 for why.

| Task | What | Status |
|---|---|---|
| D-1t | **Parent-fallback authorization** in `DocumentAuthorizationFilter`: on a document-level denial, evaluate the same right on `sprk_matter` / `sprk_project` and allow on success. Reuses `AuthorizationService` + the access cache. **This is the task that implements the owner's rule.** | primary |
| D-2t | Decide + implement the **orphan rule** — documents with no parent (the dominant case: REST create always, all communication archives, Compose non-PDF promote, document-only uploads). Deny? Owner-only? Container-level fallback? | needs Q3 |
| D-3t | Decide whether parent-fallback applies to **write** operations or reads only. | owner decision |
| D-4t | Fix the **false membership event** at `DataverseDocumentsEndpoints.cs:53-78` — it claims the caller owns a row the MI owns. Either set ownership to the caller or stop asserting it. | bug |
| D-5t | **Creation-time ownership consistency** — server paths create SP-owned rows, client paths create user-owned rows. Pick one regime. *Audit/UX correctness, no longer the access mechanism.* | reduced scope |
| D-6t | Re-verify in an org with **User-scoped** roles. The dev org's org-wide roles structurally cannot distinguish "working" from "vacuous". | gate |
| ~~D-old~~ | ~~Parental cascade on `sprk_matter_document`~~ · ~~backfill 331 SP-owned rows~~ | **DROPPED** — cascade cannot express the rule; see D-2 |

**If cascade is ever revisited** (it should not be needed): the relationship is `sprk_matter_document`
(`ReferencingAttribute sprk_Matter`), checked in at
`src/dataverse/solutions/spaarke_containers/Other/Relationships/sprk_Matter.xml`. `sprk_relatedmatter` /
`sprk_relatedproject` are **invoice-flow reference lookups, never the filing parent** — and no
first-party code writes them at all. Note also a documentation discrepancy to reconcile first: the ERD
says `Delete=Restrict`, the solution XML says `RemoveLink`.

### Workstream E — Explicitly OUT of scope here (file separately)

| Item | Why separate |
|---|---|
| `MapInboundClaims = false` | The root fix, and **safer than first assessed**: zero null-outs for identity/tenant; it would *fix* the reversed chains. Real casualty is ControlPlane `RequireRole` (`RoleClaimType` configured nowhere) + the SystemAdmin `scp` leg. Do it **per-scheme** in the existing `PostConfigure<JwtBearerOptions>` block, never the global flag, and only **after B** — until fixtures diverge, a ~74-site change ships unverifiable. |
| `SpeDocumentViewer` PCF | Source deleted by `5b4cca898` as an "orphan" but **live on the `sprk_document` main form** (confirmed: it is the only one of the three retired that exists in Dataverse). Retirement was misclassified; needs reverting. Unfixable and unreviewable until then. |
| `QueryReadAccessByProbeAsync` 404→403 | A missing row reports as "no permission". Own ticket. |

---

## 5. Sequencing and gates

```
A (+B in the same PRs)  ->  C  ->  D  ->  E
```

- **A before all** — it is stopping active harm, including a live disclosure.
- **B ships WITH A, not after.** Without B, A's fixes are unverifiable and the fourth occurrence is
  already scheduled.
- **D is independent of A/C** and can run in parallel, but must not be bundled into A's PRs — it is a
  Dataverse schema + data change with its own rollback story.
- **E requires B complete.** Flipping mapping across ~74 sites during a live outage with a blind test
  suite is the single worst ordering available.

**Gate on A:** every changed site routes through a D-1 resolver; no `??` chain that ranks identity forms
survives in changed code.
**Gate on D:** verified in an org with **User-scoped** roles — the dev org's org-wide roles cannot
distinguish "working" from "vacuous".

---

## 6. Anti-patterns this work must not repeat

1. **Do not shadow, fix.** OFFICE_009 put a correct source in front of the broken chain and left it. Nine
   latent sites are the result.
2. **Do not rank identity forms with `??`.** Silent fall-through is the defect, in all four shapes.
3. **Do not file a precise finding as an inventory item.** F8 named the file and line; the same project
   then armed it.
4. **Do not enumerate call forms.** A form-based grep missed `FindFirstValue` entirely and would never
   have found `PortfolioService`, which contains no claim read at all. **Enumerate sinks, trace backward.**
5. **Do not paginate ripgrep across runs** — result ordering is nondeterministic and files are silently
   dropped. (This is how `DocumentCheckoutService` was missed.)
6. **Do not trust a green suite** whose fixtures collapse the values under test.
7. **Do not treat a failed query as a negative result.** Confirm by a second route before reporting absence.

---

## 7. Open questions

| # | Question | Owner |
|---|---|---|
| Q1 | ~~Which lookup is the filing parent?~~ **ANSWERED**: `sprk_matter` / `sprk_project` are the filing parents; `sprk_relatedmatter` / `sprk_relatedproject` are invoice-flow references that no first-party code writes. | closed |
| Q2 | ~~Should Delete cascade?~~ **MOOT** — cascade dropped entirely (D-2). Separately: the ERD says `Delete=Restrict` while the solution XML says `RemoveLink`; reconcile the docs. | doc fix |
| Q3 | What is the rule for orphan documents (null parent)? | **owner decision** |
| Q4 | Confirm the `WorkspaceLayoutEndpoints` ownership bypass empirically before asserting it | A-2 |
| Q6 | Does parent-fallback authorization apply to **writes** or reads only? | **owner decision** (D-3t) |
| Q7 | Orphan-document rule — deny, owner-only, or container-level fallback? Dominant case, not an edge. | **owner decision** (D-2t) |
| Q8 | Which ownership regime for creation — server paths make SP-owned rows, client paths make user-owned. | **owner decision** (D-5t) |
| Q5 | Does MIW 4.14.2 set `TokenValidationParameters.RoleClaimType` internally? Decides whether ControlPlane 403s after an E flip. Cannot be answered from this repo — verify at runtime. | E |
