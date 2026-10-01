# Task 151 — RecordContainerResolver entity-name hardening (#1038, GitHub #1068)

> 2026-09-30 · branch `task/uac-r2-151` · rigor FULL

## What changed

| File | Change |
|---|---|
| `Infrastructure/Dataverse/RecordContainerResolver.cs` | `ResolveForRecordAsync` (both overloads; the 2-arg one delegates) normalizes the name through `DocumentAssociationMap.ToLogicalName` first. It then asks the registry ONE question (`ClassifyEntityAsync`, after review), and if the name is not a real entity it throws `SdapProblemException("container_entity_unknown", 400)`. It never returns a decision for that name. The supplied name is sanitized (identifier chars, ≤64) before it reaches the log line or the response body. |
| `Infrastructure/Dataverse/ISecurableEntityRegistry.cs` | First cut: +1 method `IsKnownEntityAsync`. **After review:** `IsSecurableAsync` and `IsKnownEntityAsync` are replaced by ONE `ClassifyEntityAsync` returning `EntitySecurability` (new enum file). It throws on metadata failure and on an empty entity set. |
| `Infrastructure/Dataverse/SecurableEntityRegistry.cs` | Keeps the entity names its existing (unfiltered-entity) metadata query already returned as the KNOWN set. Same single round trip. Both sets are cached together under the versioned key `sdap:dv:dv-securable-entities:v2`, as a `{v, known, securable}` object that is shape-checked on read. It is never cached when either set is empty. An internal constructor seam substitutes only the metadata round trip, for tests. |
| `Infrastructure/Cache/SystemCacheKeys.cs` | Documentation for the existing allow-listed constant: the real raw key, the `:v2` versioning, and the widened (still schema-only) value. **No new allow-list entry** (count stays 14). |
| `Api/OBOEndpoints.cs` | Comments only: the new code, and the alias → same-record reasoning. |

**Placement (CLAUDE.md §10 / bff-extensions.md).** This extends existing BFF components. No new service, endpoint, DI registration, package or second metadata call. The only new surface is one interface method, one problem code, and one cache-key version suffix. NuGet: unchanged, so no CVE delta is possible. Publish size was not measured; the main session measures once after the batch merges.

**Ordering (SUPERSEDED by the review fix below).** The first cut asked `IsSecurableAsync` first and `IsKnownEntityAsync` only on a "no", claiming a non-securable entity cost "one extra registry cache read". That held only on a cache HIT: each call fetched the catalog, so on a miss (Redis down, first call after the TTL) a non-securable or unknown name cost TWO full-org metadata round trips — breaking the "no second metadata round trip" constraint.

## Review fixes (verifier findings, 2026-09-30)

| # | Finding | Fix | Pinned by | Bite (seeded → red) |
|---|---|---|---|---|
| 1 | Two catalog fetches per non-securable resolve on a cache miss | `IsSecurableAsync` + `IsKnownEntityAsync` replaced by ONE `ClassifyEntityAsync` → `EntitySecurability { NotAnEntity = 0, NotSecurable, Securable }` reading ONE catalog value; the resolver makes one registry call. Zero value = `NotAnEntity`, so an unconfigured double or a `default` refuses. | `SecurableEntityRegistryTests.OneResolve_WithTheCacheDown_FetchesMetadataExactlyOnce` (real registry + real resolver over a DOWN cache, 5 names) | P1 — second `GetCatalogAsync` inside `ClassifyEntityAsync`: 3 red (`sprk_invoice`, `contact`, `sprk_projectt`) + the empty-answer fetch count |
| 2 | Lockstep of `EntityAccessFilter.EntitySetByType` and `DocumentAssociationMap` was comment-only | New `AssociationTypeLockstepTests`: every authorization key is a `DocumentAssociationMap` spelling AND its authorized entity SET is the live-verified set of the logical name the resolver reads; both tables accept the same spellings; every reached entity has a verified set. Ground truth: live `EntityDefinitions(...)?$select=LogicalName,EntitySetName`, spaarkedev1, read-only, 2026-09-30 (7 entities). Enumeration via two `internal` key accessors (`InternalsVisibleTo`, ADR-038 A2 — no reflection). | `AssociationTypeLockstepTests` (13 cases) | P2a `invoice → sprk_events`: 1 red · P2b `account` added to the filter only: 2 red · P2c todo keys removed from the filter: 2 red |
| 3 | "invoice" alias vs the byte-for-byte rule unstated | Remarks on `RecordContainerResolver.NormalizeEntityName`: the alias table WINS — the one stated exception. Bare `invoice` is also the OOB D365 Sales entity; Spaarke callers mean `sprk_invoice` and the route filter authorizes `invoice` against `sprk_invoices`. Live: spaarkedev1 has no `invoice` entity (`EntityDefinitions(LogicalName='invoice')` → 404); nor are `project`, `matter`, `workassignment`, `event`, `todo` (all 404). | `RecordContainerResolverTests.BareInvoice_ResolvesAsSprkInvoice_EvenWhenTheOrgHasTheOobInvoiceEntity` (world KNOWS OOB `invoice`, `sprk_invoice` securable as in dev) | P3 — OOB `invoice` passes through byte-for-byte: 2 red |

Also re-proved the refusal guard on the new shape: P4 — `NotAnEntity` folded into the non-securable branch: 6 red. Every perturbation was restored from a backup copy; the `git diff` SHA-256 was identical before and after (`f3883afd…`).

Verification gotcha (recorded so it is not repeated): restoring a perturbed file by `Copy-Item` from a backup keeps the BACKUP's older timestamp, so MSBuild's incremental check judged the P4-built DLL up to date. The first two full-suite runs therefore tested the PERTURBED binary (exactly P4's 6 reds). After touching the restored files: targeted 99/99, then the full suite was green.

Results on the clean build: BFF unit suite **13,106 passed / 0 failed / 56 skipped (13,162)**; NetArchTest **337 / 337**.

Test-double consolidation: the five registry doubles (resolver, record-keyed route, Compose collaborators, two Office hosts) now state only their world and call the shared `tests/integration/Shared/Dataverse/TestEntityCatalog.Classify`, one model of the production rule rather than five copies. `KnownEntityMetadataFailure_Propagates` was folded into `MetadataFailure_Propagates_AndDoesNotDefaultToNonSecure`, which now covers a securable and a non-securable name.

## Caller audit (re-run on this branch after the #1045 merge)

| Caller | Name source | Verdict |
|---|---|---|
| `ExternalProjectDataEndpoints.cs:598` | literal `sprk_project` | unchanged |
| `ComposeService.cs:1396` | constant `sprk_matter` | unchanged |
| `CommunicationContainerResolver.cs:102` | names from `GetSecurableEntitiesAsync` (securable, so never reach the known check) | unchanged |
| `OfficeService.cs:246` | `ToLogicalName(type) ?? type`; the Office save allow-list only admits mapped types | unchanged |
| `OBOEndpoints.cs:119`, `:363` | route value, pre-filtered by `EntityAccessFilter.EntitySetByType`, every key of which is in `DocumentAssociationMap` (lockstep invariant) | alias now resolves the same record. Previously: the misleading 409 |

No caller passes a non-entity, non-alias name, so escalation trigger 1 did not fire. `ToLogicalName` landed as described (contact included), so trigger 2 did not fire. On trigger 3, `@spaarke/sdap-client`'s `UploadOperation.put` maps **every 409** to `UploadNameConflictError` and branches on no other status. A 400 reaches the generic `SdapHttpError` carrying the server `detail`. That means no existing branch collides, and the previous alias 409 had actually been shown to users as a *name conflict*. Trigger 3 did not fire.

## Bite transcript (each guard reverted, then restored; the git-diff hash was identical before and after both rounds)

| Guard | Perturbation | Red |
|---|---|---|
| G1 alias mapping | `NormalizeEntityName` → plain lower-case | 15 (all alias theories, secure / non-secure / fail-closed, plus the 4 route-alias tests) |
| G2 unknown refusal | `IsKnownEntityAsync` check removed | 7 (5 unknown-name theories, route-unknown, and the known-check failure test) |
| G2b failure ≠ unknown | known-check exception swallowed as "unknown" | 1 (`KnownEntityMetadataFailure_Propagates`) |
| G3 known = every returned entity | `known.Add` removed / only for securable | 9 / 9 |
| G4 empty known set throws | → `return false` | 1 |
| G5 empty answer not cached | condition made unreachable (`Count < 0`) | 2 (empty-answer and no-securable tests) |
| G6 key `:v2` | key reverted to unversioned | 1 (`PreviousBuildKey_IsNeverConsulted`) |
| G7a/f format version check | removed | 1 (v1 case) |
| G7b subset check | removed | 1 (⊄ case) |
| G7d empty-securable-on-read | removed | 2 |
| G7e null-set check | removed | **build fails** (CS8604 under warnings-as-errors). The compiler is the guard. |

The first round's array-level `Length > 0` checks did not bite, because they duplicated the set-count check that follows. They were removed, and the set check was simplified to `securable.Count == 0 || !securable.IsSubsetOf(known)`, where "known non-empty" is implied. Both remaining conditions bite (G7b, G7d).

## Tests

- `RecordContainerResolverTests` (+20 cases):
  - alias secure ×4 (incl. `"  Project "`), alias fail-closed ×3, alias non-secure ×4;
  - real non-securable ×3 (`sprk_invoice`, `contact`, `account` — the last is in no alias table);
  - unknown ×5 (`not_an_entity`, `sprk_projectt`, `sprk_projects`, `projects`, `organization`), across both overloads with a fallback supplied, asserting no record read;
  - known-check metadata failure propagates.
- `RecordKeyedUploadAuthorizationTests` (+5 cases): `project` and `matter` authorize the plural set AND resolve the same logical record to the BU container (non-secure) and to the own container (secure). An unknown route entity is denied by the filter and refused by the resolver.
- `SecurableEntityRegistryTests` (**new file**, same KEEP path, +13 cases). One-line justification: AC 7 and AC 8 (failure propagates / empty not cached / old cache format never read) are properties of the registry, not the resolver, and cannot be asserted through the resolver's substituted registry.
- Doubles updated (the interface gained a question; no assertion changed):
  - the two Office test-host registry doubles (logical-name model via `DocumentAssociationMap`; 55 tests went red without it);
  - the Compose create-on-save cache seed (now written through `SecurableEntityRegistry.SerializeCacheEntry`);
  - `ComposeServiceCollaborators.Resolver`.
  - The three Communication-path Moq doubles were left unchanged. They model neither `IsSecurableAsync` nor `IsKnownEntityAsync`, and no current test reaches the resolver through them.

## Manual live gate — read-only, run 2026-09-30 against spaarkedev1

This was the same request message as the registry (`RetrieveMetadataChanges`: unfiltered entity query, attribute query filtered to `sprk_issecure`, properties LogicalName/Attributes), issued through the Web API function form with the operator's own token. It made no writes.

- **1,027 entities returned → known-set size 1,027**, of which **1,023 came back with an empty attribute collection**. That proves the "unfiltered entity query returns every entity" premise.
- All required members are present: sprk_project, sprk_matter, sprk_workassignment, sprk_document, sprk_invoice, sprk_event, sprk_todo, sprk_communication, contact, email.
- No alias or entity-set name is known (`project`: false, `sprk_projects`: false).
- **Securable subset: `sprk_invoice`, `sprk_matter`, `sprk_project`, `sprk_workassignment` — FOUR, not three.** The task's projection is byte-identical, so the subset is unchanged by this task. But the POML's (and task 075's tests') premise that `sprk_invoice` is non-securable is **false in dev**: invoice carries `sprk_issecure`. Not an escalation for this task. Flagged for the main session/owner: either invoice is meant to be securable (then nothing provisions or labels invoice containers), or the attribute is stray schema.
- **Still pending (post-deploy):** confirm the deployed BFF's own `[SECURABLE-ENTITIES] Derived 4 securable … out of N known entities` log line on first resolve.

## Not done here (main session owns)

- Commenting on #1038 and closing #1068 (no GitHub writes from this worktree).
- `TASK-INDEX.md` / `current-task.md` (main session).
- Publish-size measurement (batch).
