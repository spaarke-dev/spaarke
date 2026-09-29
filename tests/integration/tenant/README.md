# `tests/integration/tenant/**` — tenant-isolation KEEP category

> **Category authority**: [ADR-038](../../../docs/adr/ADR-038-testing-strategy.md)

## What lives here

Integration tests covering **tenant boundary enforcement**. Critical invariant: cross-tenant reads MUST return 404 (not 403), so attackers can't distinguish "exists but forbidden" from "does not exist".

## Deletion-safety rule

KEEP-protected. Deletion requires same-PR replacement.

## A note on what makes a test belong here

A test in this directory must fail when the boundary is actually crossed — it must attempt a **real read** of another tenant's data through the production path and observe the miss. Asserting on the *shape* of a partition key, a filter string or a blob path is not sufficient: those assertions stay green through the exact refactor that removes the boundary. Both files below follow the same discipline — fake only the storage/search SDK boundary, and make that fake evaluate what the production code actually produced (an OData filter; a blob name).

## Inventory status

| Added | File | Boundary guarded |
|---|---|---|
| 2026-07-26 (`ai-advanced-capabilities-nda-r1` task 052) | `Ai/ReferenceRetrievalTenantPinTests.cs` | AI Search `spaarke-rag-references` — caller tenant + the `"system"` sentinel, nothing wider |
| 2026-08-25 (`spaarkeai-compose-r8` task 060) | `Ai/SessionFileBlobStoreTenantIsolationTests.cs` | Durable session-file blob store — `{tenantId}/session-files/{sessionId}/{fileId}`; cross-tenant read, identifier injection, case sensitivity |

**2** KEEP-tenant-isolation files. The category was flagged **CRITICAL BACKFILL** in `notes/test-inventory-summary.md` at the 2026-06-26 inventory (zero compiled files at the time). Backfill continues as part of the ≥6-month cultural change window (per design.md §257) — every new persisted-data store should add a file here.
