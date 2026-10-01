# Task 151 — RecordContainerResolver entity-name hardening (#1038, GitHub #1068)

> 2026-09-30 · branch `task/uac-r2-151` · rigor FULL

## What changed

| File | Change |
|---|---|
| `Infrastructure/Dataverse/RecordContainerResolver.cs` | `ResolveForRecordAsync` (both overloads; the 2-arg one delegates) normalizes the name through `DocumentAssociationMap.ToLogicalName` first. For a name the registry says is NOT securable, it now asks `IsKnownEntityAsync`, and if the name is not a real entity it throws `SdapProblemException("container_entity_unknown", 400)`. It never returns a decision for that name. The supplied name is sanitized (identifier chars, ≤64) before it reaches the log line or the response body. |
| `Infrastructure/Dataverse/ISecurableEntityRegistry.cs` | +1 method `IsKnownEntityAsync`. It throws on metadata failure and on an empty entity set. |
| `Infrastructure/Dataverse/SecurableEntityRegistry.cs` | Keeps the entity names its existing (unfiltered-entity) metadata query already returned as the KNOWN set. Same single round trip. Both sets are cached together under the versioned key `sdap:dv:dv-securable-entities:v2`, as a `{v, known, securable}` object that is shape-checked on read. It is never cached when either set is empty. An internal constructor seam substitutes only the metadata round trip, for tests. |
| `Infrastructure/Cache/SystemCacheKeys.cs` | Documentation for the existing allow-listed constant: the real raw key, the `:v2` versioning, and the widened (still schema-only) value. **No new allow-list entry** (count stays 14). |
| `Api/OBOEndpoints.cs` | Comments only: the new code, and the alias → same-record reasoning. |

**Placement (CLAUDE.md §10 / bff-extensions.md).** This extends existing BFF components. No new service, endpoint, DI registration, package or second metadata call. The only new surface is one interface method, one problem code, and one cache-key version suffix. NuGet: unchanged, so no CVE delta is possible. Publish size was not measured; the main session measures once after the batch merges.

**Ordering, deliberately.** The resolver asks `IsSecurableAsync` first and `IsKnownEntityAsync` only on a "no". Both sets come from one query, so securable ⊆ known. A securable entity therefore costs exactly what it did before. A non-securable entity costs one extra registry cache read: a Redis GET, with no Dataverse round trip.

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
