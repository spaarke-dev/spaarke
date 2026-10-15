# Context from uac-r2 (2026-10-14)

A pointer map for the design session. **Code wins over this note**: re-check every path on master before relying on it.

## Code: today's secure-record machinery (master)

| Concern | Where | Note |
|---|---|---|
| Secure-if-any ownership of children | `src/server/api/Sprk.Bff.Api/Services/Access/SecureChildReconciler.cs` | Decides each child's owner from ALL its parent lookups (`ParentsOf` ~:1278). `ReownOrdinaryRowAsync` (uac-r2 task 183) refuses moves into or out of the Secure Record BU. |
| Which lookups make a child | `.../Services/Access/SecureChildLineage.cs` | The lineage map, i.e. the walk this project replaces with a stored key. |
| Share mirror | `.../Services/Access/SecureChildShareSynchronizer.cs` | Root shares are copied to every child. `ResolveSecureBusinessUnitAsync` and `ResolveSecureOwnerTeamAsync` live here. |
| Inheritance and flag cascade | `.../Services/Access/SecureRootInheritance.cs`, `SecureRootInheritance.Cascade.cs` | Effective secure flags (round 87: the parent sets a floor). `TypedLookupsOf`. |
| Effective root flags (external) | `.../Infrastructure/ExternalAccess/EffectiveRootFlags.cs` | A non-secure root under a secure ancestor counts as secure. |
| External multi-root rule | `.../Infrastructure/ExternalAccess/ExternalMultiRootRule.cs` | The visibility rule. uac-r2 task 187 adds live parents through `ParentLineage` (PR #1861). |
| Containers | `.../Infrastructure/Dataverse/RecordContainerResolver.cs` (+ `.DocumentPointer.cs`) | One container per secure record today (#1506). "different + secure → ambiguous" (~:806). |
| Core-ancestor stamps | `.../Services/Dataverse/CoreAncestorResolver.cs`, `CoreAncestorRestamper.cs`, `CoreAncestorStampReconciliationJob.cs` | The ordinary-record one-hop key (matter / project / WA / SR copies), kept fresh by the restamper and a 5-minute job. `StampedOutsideAccessTaxonomy` (Signals and DRs, ontology task 144). |
| Ownership resolution | `.../Services/Dataverse/RecordOwnershipResolver.cs` | D-130: the owner of a written row. The family write path plugs in here. |
| Securable tables | `.../Infrastructure/Dataverse/SecurableEntityRegistry.cs` | Which tables carry secure semantics. |
| Write path | `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`; entry point `Services/Dataverse/CoreAncestorResolver.cs` | One server-side owner per invariant; no plugins. |
| SRO role census | `config/secure-record-owner-role.json`, `scripts/Set-SecureRecordOwnerRolePrivileges.ps1 -Verify` | NFR-05. 40 privileges, depth 1 (Basic). |

## Related uac-r2 work

- **Task 158** (`notes/task-158-secure-inherit-filed-records.md`): inherited-secure records. This is the nested-family question (D3).
- **Task 175** (`notes/task-175-child-access-cascade.md`): child access cascade.
- **Task 177** (`notes/task-177-relocated-document-index-parent.md`): a relocated document's index parent.
- **Task 178** (`tasks/178-secure-family-single-container.poml`, open): one SPE container per family (round 88, #1506). It moves into this project.
- **Task 183:** `ReownOrdinaryRowAsync`, the re-own entry point for ordinary Signal and DR rows.
- **Task 187** (PR #1861, #1857): external reads check live parents. Its parent walk is the interim mechanism; the follow-up replaces it with the family key.
- **Task 046** (`notes/task-046-secure-project-owner-role.md`): proved secure projects are not isolated from `Spaarke Basic User`'s Deep-depth reads. Owner item: BU restructure, or Deep → Local.
- Ontology's standard child contract (`../unified-access-control-r2/notes/ontology-standard-child-contract-2026-10-12.md`): Signals and DRs follow the subject's family (round 95).

## Open issues to carry

- **#1506:** one container per secure record, so uploads in a nested family are refused.
- **#1857:** the external stale and carrier stamp (task 187).
- **#1343:** the Spaarke Demo team holds System Administrator, which also bypasses field security.

## Invariants that must survive the change

- **D-123:** nothing behind a secure wall is readable by someone outside it, internally (Dataverse) or externally (BFF).
- **Security fails closed:** an undecidable family, an unreadable parent or a refused write means the row is withheld, never on a wider owner.
- **FR-22:** the Secure veto. Only direct grants admit an external caller to a secure record.
- **Round 87:** the parent sets a floor, and a child may be stricter.
- **Round 88:** the top-most secure record owns the family's container, and making a family not secure moves its files.

## Lessons from uac-r2 (do not re-learn)

- **A copied key goes stale.** Every stamp-based check needs a defined freshness owner: the 5-minute job missed "carrier" rows (DirectRootLink) permanently (#1857). The family key needs one writer, and a drift query that a guard runs.
- **A tool finds only what it was built to look for.** Census guards must be pinned both ways, with must-fire controls (uac-r2 task 091's nine admin routes).
- **Dataverse's 403 from the Web API arrives without its error code** (`EnsureSuccessStatusCode`), so classify on the status, not the message (ontology #1854).
- **Bounded walks must degrade per row, not per page** (task 187 review: a bound that skips a whole level emptied a normal list).
- **The external plane reads app-only**, so the BFF's own check IS the wall there. Dataverse ownership protects only the internal plane.
