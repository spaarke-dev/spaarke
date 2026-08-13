# External Access — Polymorphic Tier-2 Scoping (design + build spec)

> 2026-08-10. Owner-clarified across UAT of the R2 grid widgets. Supersedes the partial
> "documents-scoped-by-project" fix (commit `bff7e82e5`) with the correct multi-parent model.
> Grounded in live Dataverse schema (MCP) + the ADR-024 regarding family.

## 1. Core concept (plain terms)

**Access is held at "root" records; child records roll up to any accessible root.**

- **Roots** = the records a caller is given access to: **Project, Matter, Work Assignment** (and, internal-only, **Service Requests they submitted**).
- **Children** = **Documents, Invoices** — visible when attached to an accessible root.
- A child can attach to *more than one kind of root* (a Document links to a Project **or** Matter **or** Work Assignment), so each grid checks **all** its parent links and OR's them ("multi-dimension scoping"). Checking only one link (today: project) silently hides everything attached the other ways.

## 2. Access sources (how a caller gets roots) — BINDING

- **Outside-counsel partner (CIAM): grant-only, explicit.** Every root a partner sees comes from an active `sprk_externalrecordaccess` row of that record type. No assignment- or rollup-derived access. (Rationale: a firm may be advisory on internally-assigned work → access must be an explicit grant.) Verified: grant table has `sprk_project`, `sprk_matter`, `sprk_workassignment` typed lookups + `sprk_recordtype` discriminator — **no schema change needed.**
- **Internal workforce user: membership/ownership/assignment ∪ own-contact grants.** Entity-generic `MembershipResolverService` already resolves "records I'm on" for any entity. Service Requests: the ones where `sprk_requestedby` (lookup→contact) = the caller's contact.

## 3. Accessible root sets composed per caller

| Set | Partner (CIAM) | Internal (workforce) |
|---|---|---|
| **P** projects | grants `recordtype=Project` | membership ∪ contact project grants |
| **M** matters | grants `recordtype=Matter` | membership ∪ contact matter grants |
| **W** work assignments | grants `recordtype=WorkAssignment` | assigned-to-me ∪ contact WA grants |
| **S** service requests | — (n/a) | `sprk_requestedby == my contact` |

## 4. Modules → scope dimensions (row visible if ANY dimension matches)

| Module (tab) | Entity | Scope dimensions | Partner | Internal |
|---|---|---|---|---|
| Projects | `sprk_project` | `sprk_projectid ∈ P` | ✅ | ✅ |
| Matters | `sprk_matter` | `sprk_matterid ∈ M` | ✅ | ✅ |
| Work Assignments | `sprk_workassignment` | `sprk_workassignmentid ∈ W` | ✅ | ✅ |
| Documents | `sprk_document` | `sprk_project∈P` **or** `sprk_matter∈M` **or** `sprk_workassignment∈W` | ✅ | ✅ |
| Invoices | `sprk_invoice` | `sprk_matter∈M` **or** `sprk_project∈P` | ✅ | ✅ |
| **Service Requests** | `sprk_servicerequest` | `sprk_requestedby == caller contact` | ❌ **excluded** | ✅ |

Verified lookups: `sprk_document.sprk_workassignment` ✅, `sprk_invoice.sprk_matter`/`sprk_project` ✅, `sprk_servicerequest.sprk_requestedby` (per owner).

## 5. Build steps

1. **Compose N root sets** (P/M/W [+S internal]) per caller — extend `ExternalParticipationService` to read grants of all record types (not just project); extend `AccessibleRecordSetService` grant term beyond `sprk_project`; both plane strategies compose all sets onto `CallerPrincipal`.
2. **`CallerPrincipal`** — carry accessible ids per root type (`GetAccessibleProjectIds`/`…MatterIds`/`…WorkAssignmentIds`).
3. **Generalize `ExternalModuleDescriptor`** from one `RecordIdAttribute`+`AccessibleRecordIds` to a **list of scope dimensions** `{attribute, accessibleIds(principal)}`; row accessible if ANY dimension matches (`ScopeRows` OR); all-empty → 0 rows.
4. **`Tier2ScopeFilterInjector`** — emit `<filter type="or">` across the non-empty dimensions (generalize from single-attribute).
5. **Register modules** per §4; Service Requests scoped by `sprk_requestedby`.
6. **Grid config records** project the parent lookups each grid's scoping reads (Documents +`sprk_matter`+`sprk_workassignment`; Invoices +`sprk_matter`; new Service Requests config).
7. **Client** — add Service Requests tab **internal-only**; keep Work Assignments on partner; entitlement-gate SR to workforce.
8. **Tests** (OR injector, multi-dimension descriptor, per-plane contract) + **BFF redeploy** + live UAT both planes.

Reuse: `sprk_recordtype_ref` (record-type → regarding-field/logical-name map) for the lookup↔type mapping; do NOT reuse the write-side `PolymorphicResolverService`/`IncomingAssociationResolver` (documents lack the denormalized regarding fields → typed-lookup OR is required).

## 6. Out of scope (this build)
- Direct Document/Invoice-level grants (access derives from roots).
- Organization-parent ("whole firm") access.
- Service Request *creation* wizard + law-dept management (P3 Legal Front Door).
- Field-level financial redaction for partners (granted matter = full read today).
- Work-assignment's other regarding parents (invoice/event/communication) as document dimensions.
