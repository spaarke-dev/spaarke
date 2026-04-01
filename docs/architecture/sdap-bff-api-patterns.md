# SDAP BFF API Patterns

> **Last Updated**: March 9, 2026
> **Applies To**: Backend API development, endpoint changes, service layer

---

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| One SPE container per environment (not per entity) | Simplifies access management; entity relationships tracked in Dataverse `sprk_document` lookups, not container structure |
| "SPE First, Dataverse Second" ordering | SPE returns `graphItemId` + `webUrl` needed by the Dataverse record; creating Dataverse record first creates an orphan if SPE upload fails |
| `DefaultContainerId` in Drive ID format (`b!xxx`) | Graph API rejects raw GUIDs for SPE operations |
| Navmap TTL: 15 minutes | Navigation metadata changes infrequently; 15 min balances freshness vs. Dataverse query cost (~2.5s cache miss vs. ~0.3s cache hit) |
| Redis-first caching (ADR-009) | All caching through IDistributedCache; no in-process L1 cache unless profiling proves need |
| IDataverseService interface segregation (R2) | 9 focused interfaces (`IDocumentDataverseService`, `IAnalysisDataverseService`, etc.) with composite `IDataverseService` for backward compatibility; new consumers inject narrowest applicable interface |
| Endpoint filters for authorization (ADR-008) | `AnalysisAuthorizationFilter`, `ExternalCallerAuthorizationFilter` applied per-endpoint; no global auth middleware |
| Dataverse ServiceClient + ClientSecret (not Managed Identity) | More reliable, Microsoft's recommended approach for ServiceClient |

---

## SPE Container Model

One container per environment. All documents (across all entity types) stored in the same container. Entity relationships tracked by `sprk_document` parent lookup fields, not container location.

**Container resolution by upload flow:**

| Flow | Container Source |
|------|----------------|
| Code Page wizard upload | `containerId` URL parameter (resolved from parent entity or Business Unit) |
| PCF upload (legacy) | Parent entity's `sprk_containerid` field |
| Email-to-Document (background job) | `EmailProcessingOptions.DefaultContainerId` from config |
| Upload before parent entity exists | `DefaultContainerId` from config |

---

## "SPE First, Dataverse Second" (MANDATORY)

All upload flows MUST:
1. Upload file to SPE → receive `driveId`, `itemId`, `webUrl`
2. Create `sprk_document` in Dataverse using SPE result to populate `sprk_graphdriveid`, `sprk_graphitemid`, `sprk_filepath`

No file moves are needed after the fact — files stay in the same container permanently.

---

## Upload Flow Variants

| Flow | Auth | Container Source | Parent Entity |
|------|------|-----------------|---------------|
| PCF upload | OBO | Parent's `sprk_containerid` | Exists (synchronous) |
| Code Page wizard | OBO | URL parameter | Exists (synchronous) |
| Email-to-Document | App-only | `DefaultContainerId` config | Optional (async job) |
| Create New Entity | OBO | `DefaultContainerId` config | Created after upload (two-phase) |

---

## Redis Caching TTLs

| Cache Key Pattern | TTL | Purpose |
|-------------------|-----|---------|
| `navmap:lookup:{entity}:{relationship}` | 15 min | Navigation property lookup metadata |
| `navmap:collection:{entity}:{relationship}` | 15 min | Collection navigation property metadata |
| Auth roles / teams | 2 min | User access roles |
| Resource access | 60s | Per-resource access check |
| Communication accounts | 5 min | `sprk_communicationaccount` list |
| Finance summary | 5 min | Pre-computed spend summary; invalidated on snapshot job completion |
| OBO Graph tokens | 55 min | Cached by SHA-256 hash of input token |

---

## Critical Field Mapping Gotchas

| Property | Dataverse Field | Type | Gotcha |
|----------|-----------------|------|--------|
| MimeType | `sprk_mimetype` | Text | NOT `sprk_filetype` |
| FileSize | `sprk_filesize` | Whole Number (int32) | Cast `(int)` — passing `long` causes type mismatch |
| FilePath | `sprk_filepath` | Text | Must be `fileHandle.WebUrl` — enables "Open in SharePoint" links |
| GraphItemId | `sprk_graphitemid` | Text | |
| GraphDriveId | `sprk_graphdriveid` | Text | Must be Drive ID format (`b!xxx`), not raw GUID |

**WCF DateTime**: Dataverse webhooks send dates as `/Date(1234567890000)/` (WCF format). Use `NullableWcfDateTimeConverter` — standard `DateTime.Parse` fails.

---

## AI Authorization Service Pattern

`AnalysisAuthorizationFilter` (endpoint filter per ADR-008) validates user Read access before AI analysis:
1. Extracts document IDs from request
2. Calls `IAiAuthorizationService.AuthorizeAsync(user, documentIds, httpContext, ct)`
3. `HttpContext` propagated through chain for OBO token extraction
4. Uses direct Dataverse query pattern (not `RetrievePrincipalAccess`) — see sdap-auth-patterns.md Pattern 5

Fail-closed: returns `AccessRights.None` on errors.

---

## Background Job Handler Pattern

Job handlers implement `IJobHandler`, registered by `JobType` string. All jobs:
- Check idempotency key before processing (returns success if already processed)
- Acquire Redis processing lock (prevents concurrent duplicate runs)
- Release lock in finally block
- Mark as processed with 7-day TTL after success

AI analysis and RAG indexing failures in job handlers are non-fatal (logged as warnings, job marked success).

---

## Common Mistakes

| Mistake | Error | Fix |
|---------|-------|-----|
| `sprk_filetype` instead of `sprk_mimetype` | Field not found | Correct field: `sprk_mimetype` |
| `FileSize` as `long` | Type mismatch | Cast to `(int)` — `sprk_filesize` is Whole Number |
| Not setting `FilePath` | "Open in SharePoint" broken | `FilePath = fileHandle.WebUrl` |
| `DefaultContainerId` as raw GUID | Graph API rejects | Use Drive ID format `b!xxx` |
| Dataverse record before SPE upload | Orphan record if SPE fails | SPE first, Dataverse second |
| Parsing WCF dates as ISO 8601 | DateTime parse error | Use `NullableWcfDateTimeConverter` |
| `IDataverseService` injection for narrow use case | Violates ADR-010 | Inject `IDocumentDataverseService` or narrowest applicable interface |

---

## Related Documentation

| Document | Purpose |
|----------|---------|
| [sdap-auth-patterns.md](sdap-auth-patterns.md) | OBO and app-only auth patterns |
| [communication-service-architecture.md](communication-service-architecture.md) | Communication module using these patterns |
| [sdap-component-interactions.md](sdap-component-interactions.md) | Cross-component impact table |

---

*Last Updated: March 9, 2026*
