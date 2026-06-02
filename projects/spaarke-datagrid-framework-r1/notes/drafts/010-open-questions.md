# 010 — Open Questions for Review Before Phase B Dispatch

> **Status**: Draft (3 questions surfaced during task 010 design)
> **Audience**: Project owner — review before tasks 011-014 dispatch (B-Wave-1)
> **Resolution target**: Before Phase B Wave 1 (tasks 011, 012, 013, 014) starts
> **Blocking?**: Q1 (yes — task 011 needs this), Q2 (yes — task 011 needs this), Q3 (no — verify-then-proceed)

---

## Q1. Privilege-Check Mechanism — Which Dataverse API + auth path? [BLOCKING task 011]

**Context**: The filter shape doc (`010-authorization-filter-shape.md` §2) declares an `IDataversePrivilegeChecker.HasPrivilegeAsync(userOid, entityLogicalName, privilege, ct)` interface. Task 011's implementation owner needs to know:

1. **Which Dataverse API call** backs this? Candidates:
   - **(a) `RetrievePrincipalAccessRequest`** — SDK organization-service request; returns `AccessRights` flags for a (principal, target) pair. Requires app-only `IOrganizationService` (`ServiceClient`); takes a target `EntityReference`. **Problem**: this is per-record, not per-entity. For entity-level Read privilege we need…
   - **(b) `RetrieveUserPrivileges` / `RetrieveRolePrivilegesRoleRequest`** — returns the user's effective privilege set for ALL entities. Suitable for caching the full per-user privilege set in one call. Implementation: walk the returned `RolePrivilege[]`, project to `(entity, privilege) → bool` map, cache by `userOid`.
   - **(c) Web API `RetrieveUserPrivileges` function** — `/api/data/v9.2/RetrieveUserPrivileges(UserId={oid})` — equivalent result via REST. Easier to call with `DataverseAccessDataSource`'s existing HttpClient pattern (no `ServiceClient.RetrieveMultiple` ceremony).
   - **(d) Custom OData filter against `roleprivileges` + `systemuserroles`** — least preferred; lots of joins; brittle.

   **Recommendation embedded in design**: **(b) or (c)**. Cache the FULL per-user privilege set (per the §6 6h TTL); look up `(entity, privilege)` from the cached map in-process. One Dataverse round-trip per user per 6h, regardless of how many entities the user touches.

2. **Auth path** — `ServiceClient` with app-only (managed identity) + `CallerId = userOid`, OR OBO with the caller's bearer token?
   - **Recommendation**: app-only + `CallerId` impersonation matches `DataverseAccessDataSource.cs` pattern in `Spaarke.Dataverse` (existing) AND scales better (no per-user OBO token cache for the privilege-check path; only for the actual `/api/dataverse/fetch` row read).

**Decision needed from owner**:
- ☐ Confirm: implement as `(b)` or `(c)` — full per-user privilege set, cached 6h, in-process lookup
- ☐ Confirm: app-only `ServiceClient` + `CallerId` impersonation (NOT OBO) for the privilege fetch
- ☐ Confirm: where does the implementation live? `Services/Dataverse/DataversePrivilegeChecker.cs` (BFF) OR `Spaarke.Dataverse/PrivilegeService.cs` (shared, reusable by plugins later)?

---

## Q2. FetchXML Entity Extractor — New code or reuse existing? [BLOCKING task 011]

**Context**: The filter shape declares `IFetchXmlEntityExtractor.ExtractEntities(fetchXml) → string[]` (primary + every link-entity, recursively). The implementation parses FetchXML and walks `<entity>` + `<link-entity>` elements.

**Investigation done at task 010 time**: A quick `Grep` for `FetchXml` parsers in `src/server/api/Sprk.Bff.Api/` and `src/server/shared/Spaarke.Dataverse/` did NOT surface an existing parser that does entity extraction. (Closest match: `injectContextFilter` on the client side in `VisualHost` — but that's TypeScript and only handles primary-entity context, not link-entity extraction.)

**Recommendation embedded in design**: New code in `Services/Dataverse/FetchXml/FetchXmlEntityExtractor.cs` using `System.Xml.Linq.XDocument` (in-box, no new package). ~30 lines. Pattern:

```csharp
public IReadOnlyList<string> ExtractEntities(string fetchXml)
{
    XDocument doc;
    try { doc = XDocument.Parse(fetchXml); }
    catch (XmlException ex) { throw new FetchXmlParseException(ex.Message, ex); }

    var primary = doc.Root?.Element("entity")?.Attribute("name")?.Value;
    if (string.IsNullOrWhiteSpace(primary))
        throw new FetchXmlParseException("FetchXML root element must contain <entity name='…'>");

    var linkEntities = doc.Descendants("link-entity")
        .Select(e => e.Attribute("name")?.Value)
        .Where(n => !string.IsNullOrWhiteSpace(n))
        .Cast<string>();

    return new[] { primary }.Concat(linkEntities).ToList();
}
```

**Decision needed from owner**:
- ☐ Confirm: new code in `Services/Dataverse/FetchXml/FetchXmlEntityExtractor.cs` (BFF) using `System.Xml.Linq`
- ☐ Confirm: if any existing parser EXISTS that this writer missed, reply with its location; otherwise proceed with new code

---

## Q3. Cache Backend — Reuse the existing Redis instance? [Verify-then-proceed]

**Context**: The filter shape doc §6 calls out three cache layers — privilege metadata (6h), entity metadata (6h, version-pinned), savedquery (1h). All use `IDistributedCache`.

**Existing infra**: `Microsoft.Extensions.Caching.StackExchangeRedis` 10.0.1 is already in `Sprk.Bff.Api.csproj`. `GraphMetadataCache.cs` (`Infrastructure/Graph/`) uses the same `IDistributedCache` for Graph metadata at 5min / 2min / 24h TTLs. The Redis instance is configured at app startup per `azure-deployment.md`.

**Question**: do we use the SAME Redis instance/database (most likely) — or is there a desire to isolate per-feature for ops reasons (cost, eviction policy, observability)?

**Recommendation embedded in design**: SAME instance, SAME `IDistributedCache` registration. Keys are namespaced (`sdap:dv:*` vs `sdap:graph:*`) — Redis handles this fine. Pattern matches the existing `GraphMetadataCache` precedent.

**Decision needed from owner**:
- ☐ Confirm: use the existing Redis instance, same `IDistributedCache` registration
- ☐ If NO: identify the alternative cache infra (and likely write a follow-on ADR — `azure-deployment.md` and ADR-029 both assume single Redis instance for BFF)

---

## Q4 (informational — not blocking). Whose audit log captures the privilege denied event?

The filter logs `Warning` on deny. Existing `AuditEnrichmentMiddleware` (per `Sprk.Bff.Api/CLAUDE.md`) enriches the log with `oid`/`appid`/`obo`/`tenantId`/`correlationId` automatically. **Expected**: that's enough — no additional audit-log sink needed for R1. Confirm at PR review time.

---

## Summary — Resolution Status

| # | Question | Owner | Resolution |
|---|---|---|---|
| Q1 | Privilege-check API + auth path | TBD (project owner) | ☐ Open |
| Q2 | FetchXML extractor — new vs. reuse | TBD (project owner) | ☐ Open |
| Q3 | Cache backend — reuse existing Redis | TBD (project owner) | ☐ Open (low risk; verify-then-proceed) |
| Q4 | Audit log path for deny events | TBD (project owner) | ☐ Open (informational) |

**Default plan if no decisions land before Phase B dispatch**: task 011 owner proceeds with the recommendations embedded above (Q1 = option b/c, Q2 = new code, Q3 = existing Redis, Q4 = AuditEnrichmentMiddleware sufficient). Owner can correct course at PR review time without architectural rework.

---

## 2026-06-01 — User decisions (recorded)

**Q1**: Use `RetrieveUserPrivileges` (per-user full set, cached 6h sliding / 24h absolute max, app-only ServiceClient with CallerId impersonation). Task 011 owns the IDataversePrivilegeChecker interface + UserPrivilegeChecker implementation since they're tightly coupled to the filter class.

**Q2**: New code at `Services/Dataverse/FetchXml/FetchXmlEntityExtractor.cs` using `System.Xml.Linq.XDocument`. Task 013 owns this since FetchService is the only runtime consumer.

**Q3**: Reuse existing Redis `IDistributedCache` registered in Sprk.Bff.Api (matches GraphMetadataCache precedent). No new cache instance.

**Q4**: AuditEnrichmentMiddleware sufficient for deny events. No new audit code.

These decisions are now binding for B-Wave-1 (tasks 011-014). Course-correction at PR review still permitted but design assumes these as final.

