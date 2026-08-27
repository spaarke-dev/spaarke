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

The native Dataverse mechanism is a **Parental relationship with configurable cascading**
(`Assign=Cascade`, `Share=Cascade`; `Delete` deliberately left as-is). Today all 27 are `NoCascade`.

⚠️ **Cascade alone is not sufficient**, and this is the most important nuance in this document:
cascade **Assign** fires on a parent owner *change*; it does **not** retroactively fix existing rows and
does **not** help a document created directly under a service principal. So D-2 needs three parts:

1. make the primary parent relationship Parental (Assign + Share),
2. fix **creation-time ownership** so a new document is owned by the caller (or the parent's owner), and
3. a **backfill** for the 331 existing SP-owned rows.

Without (2), every newly created document still lands owned by the MI and cascade never fires for it.

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

| Task | What |
|---|---|
| D-1t | Decide the **primary** parent relationship — `sprk_document` has two matter lookups (`sprk_matter`, `sprk_relatedmatter`) and two project lookups. Only one can be the filing parent. |
| D-2t | Set that relationship to **Parental / configurable cascade**: `Assign=Cascade`, `Share=Cascade`; leave `Delete` unchanged unless deliberately decided. Verify no other relationship conflicts. |
| D-3t | **Creation-time ownership** — every `sprk_document` creation path must own the row to the caller (or the parent's owner), not the service principal. Without this, cascade never fires for new documents. |
| D-4t | **Backfill** the 331 existing SP-owned rows to the correct owner. |
| D-5t | Handle **orphans** — documents with a null parent lookup have no cascade source. Decide the rule (deny? owner-only? a fallback container-level check?). |
| D-6t | Re-verify the whole model in an org with **User-scoped** roles, not just the dev org's org-wide roles. The dev org cannot detect this class of failure. |

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
| Q1 | Which of the two matter / two project lookups is the true filing parent? | investigation in flight |
| Q2 | Should `sprk_document` `Delete` cascade from the parent, or stay `RemoveLink`? | **owner decision** — deleting a matter deleting its documents is a real consequence |
| Q3 | What is the rule for orphan documents (null parent)? | **owner decision** |
| Q4 | Confirm the `WorkspaceLayoutEndpoints` ownership bypass empirically before asserting it | A-2 |
| Q5 | Does MIW 4.14.2 set `TokenValidationParameters.RoleClaimType` internally? Decides whether ControlPlane 403s after an E flip. Cannot be answered from this repo — verify at runtime. | E |
