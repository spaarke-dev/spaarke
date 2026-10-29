---
name: dataverse-cascade-delete-privileges-archive-2026-10-17
description: Cascade-delete child privilege check (documented: whole op fails), CascadeConfiguration.Archive "internal use only", PUT relationship update; error code/limits undocumented
metadata:
  type: reference
---

**Q**: sprk_matter -> sprk_policy 1:N Delete=Cascade: child privilege checks, error, async/limits, Archive, Web API update.

- Child privs: DeleteRequest remarks (Learn, current): "If Cascade All for Delete... attempt to delete related records. The entire operation fails if the caller does not have the delete privilege or access rights for any of these records." Documented. Ownership-type differences: not documented.
- Error code: NOT documented for cascade. Inferred: 0x80040220 prvDeleteX (standard SecLib privilege error).
- Async/limits: only async cascade doc'd = cascadeAsync_FailureAPI/SuccessAPI messages (config.-entity-relationship-cascading-behavior). No count limit documented; Delete cascade sync-vs-async not stated.
- CascadeConfiguration.Archive = "For internal use only." (Web API ref). Delete valid values: Cascade/RemoveLink/Restrict only. Long-term retention doc has NO relationship/cascade behaviour mention.
- Update: PUT RelationshipDefinitions(id) full replace + MSCRM.MergeLabels:true + publish; PATCH unsupported. Parental rule: no new relationship with Cascade All/Active/UserOwned if child already child in another such relationship; custom primary -> system related can't cascade.
- Open: self-referential Delete=Cascade refusal not found in docs.

Sources: learn.microsoft.com/en-us/dotnet/api/microsoft.xrm.sdk.messages.deleterequest; .../power-apps/maker/data-platform/create-edit-entity-relationships; .../developer/data-platform/configure-entity-relationship-cascading-behavior; .../webapi/create-update-entity-relationships-using-web-api; .../webapi/reference/cascadeconfiguration

**OBSERVED CONTRADICTION (spaarkedev1, 2026-10-18, ontology 102b live check):** with `sprk_policy_matter` Delete=Cascade, a user holding NO privilege on `sprk_policy` (Spaarke Basic + Core User, via admin token + MSCRMCallerID) deleted the parent matter: 204, and the child policy was deleted too, audited under that user. So the per-child privilege check in the DeleteRequest remark did NOT apply on this path. Trust the live test over the remark; re-test per configuration.
