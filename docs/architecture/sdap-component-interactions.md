# Component Interactions Guide

> **Purpose**: Help AI coding agents understand how Spaarke components interact, so changes to one component can be evaluated for impact on others.
> **Last Updated**: January 2026

---

## Quick Impact Reference

When modifying a component, check this table for potential downstream effects:

| If You Change... | Check Impact On... |
|------------------|-------------------|
| BFF API endpoints | PCF controls, Office add-ins, tests, API documentation |
| BFF authentication | PCF auth config, Office add-in auth, Dataverse plugin auth |
| PCF control API calls | BFF endpoint contracts |
| Dataverse entity schema | BFF Dataverse queries, PCF form bindings, Office workers |
| Shared libraries | All consumers (search for ProjectReference) |
| Email processing options | Webhook handler, polling service, job handler |
| Email filter rules schema | EmailFilterService, EmailRuleSeedService |
| Webhook endpoint | Dataverse Service Endpoint registration |
| Office add-in entity models | UploadFinalizationWorker, IDataverseService, Dataverse schema |
| ProcessingJob schema | UploadFinalizationWorker, Office add-in tracking |
| IDataverseService interface | Both implementations (DataverseServiceClientImpl, DataverseWebApiService) |

---

## Pattern 1: Document Upload Flow

**Primary (Code Page wizard):**
- User → ribbon button → DocumentUploadWizard Code Page → BFF → Graph → SPE → Xrm.WebApi → Dataverse → Document Profile playbook → RAG indexing

**Legacy (PCF form-embedded):**
- User → UniversalQuickCreate PCF → BFF → Graph → SPE + Dataverse (same SPE-first ordering)

**Change impact:**

| Change | Impact |
|--------|--------|
| Modify upload endpoint signature | Update shared upload services (used by both Code Page and PCF) |
| Change file size limits | Update both BFF config and shared UI messaging |
| Modify `sprk_document` field names | Update BFF field mapping, PCF form bindings, Office workers |

---

## Pattern 5: Email-to-Document Conversion

See [email-to-document-automation.md](email-to-document-automation.md) for full design. Key cross-component impacts:

| Change | Impact |
|--------|--------|
| Modify webhook endpoint URL | Update Dataverse Service Endpoint registration |
| Change job payload schema | Update webhook handler AND job handler |
| Modify filter rule schema | Update EmailFilterService, seed service, Dataverse entity |
| Change default container | Update EmailProcessingOptions configuration |
| Modify attachment parent-child link | `sprk_email` must NOT be set on child documents (alternate key constraint) |
| Change AI playbook name | Update `EnqueueAiAnalysisJobAsync` constant |

---

## Pattern 7: AI Authorization Flow (OBO for Dataverse)

Sequence: PCF acquires token → BFF `AnalysisAuthorizationFilter` → `AiAuthorizationService` → `DataverseAccessDataSource` OBO exchange → Dataverse direct query → authorization decision.

**Two critical bugs fixed in this flow:**
1. OBO token obtained but never set on HttpClient → 401. Fix: `_httpClient.DefaultRequestHeaders.Authorization = Bearer {oboToken}` immediately after MSAL exchange.
2. `RetrievePrincipalAccess` returns 404 with OBO (delegated) tokens. Fix: Direct query `GET /sprk_documents({id})?$select=sprk_documentid` — success = Read access granted.

**Change impact:**

| Change | Impact |
|--------|--------|
| Modify `IAiAuthorizationService` signature | Update all authorization filters |
| Change OBO token acquisition | Update MSAL configuration, test all AI operations |
| Add new `IAccessDataSource` | Implement interface, register in DI |

---

## Pattern 9: RAG File Indexing — Dual Entry Points

Two indexing paths with different auth patterns:

| Entry Point | Auth | Use Case |
|-------------|------|---------|
| `POST /api/ai/rag/index-file` | OBO (user token) | User-initiated via PCF |
| `POST /api/ai/rag/enqueue-indexing` | X-Api-Key header | Background jobs, bulk ops, scripts |

The async path submits to Service Bus; `RagIndexingJobHandler` processes using app-only auth (`ForApp()`). The synchronous path uses OBO (`ForUserAsync(ctx)`). Both ultimately call `FileIndexingService`.

**Change impact:**

| Change | Impact |
|--------|--------|
| Modify indexing entry point auth | Update both OBO path (filter) and API key path (validation) |
| Change `IFileIndexingService` interface | Update both sync and async handlers |
| Change RAG index schema | Update both indexing services and search queries |

---

## Related Documentation

| Document | Purpose |
|----------|---------|
| [sdap-auth-patterns.md](sdap-auth-patterns.md) | Auth pattern details (OBO bugs, dual strategies) |
| [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) | BFF field mappings, container model, caching TTLs |
| [email-to-document-automation.md](email-to-document-automation.md) | Email pipeline design decisions |
| [uac-access-control.md](uac-access-control.md) | Three-plane access control model |

---

*Last Updated: January 2026*
