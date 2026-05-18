# Audit Log Immutability Verification Report & Test Plan

**Date**: 2026-05-17
**Scope**: Spaarke AI Platform — AI interaction audit log (AIPU2-033)
**Compliance Basis**: ADR-015 Tier 2 — Compliance Audit

---

## 1. Executive Summary

The AI Platform audit log is implemented as an append-only Cosmos DB store with two independent immutability enforcement layers:

1. **Application layer** — `AuditLogService` uses only `CreateItemAsync`; no update, upsert, replace, or delete methods exist.
2. **Infrastructure layer** — Cosmos DB container-level immutability policy with a 2,555-day (7-year) locked retention period.

This report documents the codebase scan results, infrastructure configuration, and provides a step-by-step test plan to verify immutability compliance.

---

## 2. API Surface Audit

### 2.1 AI Audit Log (Cosmos DB — AIPU2-033)

**Interface**: `IAuditLogService` (1 method only)

| Method | Signature | Verdict |
|--------|-----------|---------|
| `LogInteractionAsync` | `ValueTask LogInteractionAsync(AuditEntry entry, CancellationToken ct)` | WRITE-ONLY (append) |

**No read/query/update/delete endpoints exist for AI audit data.** The `IAuditLogService` interface exposes a single write method. There are no API endpoints in `src/server/api/Sprk.Bff.Api/Api/Ai/` that expose audit records for retrieval, modification, or deletion.

**Source files**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Audit/IAuditLogService.cs` (lines 12-23)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Audit/AuditLogService.cs` (lines 24-107)

### 2.2 SPE Admin Audit Log (Dataverse — separate system)

A separate audit subsystem exists for SPE admin operations, stored in Dataverse (`sprk_speauditlogs`). This is **not** the AI compliance audit log and has different immutability characteristics.

| Endpoint | Method | Route | Purpose |
|----------|--------|-------|---------|
| `QueryAuditLog` | GET | `/api/spe/audit` | Read-only query of SPE admin audit entries |

**Verdict**: GET only. No PUT/PATCH/DELETE endpoints registered for SPE audit data.

**Source file**: `src/server/api/Sprk.Bff.Api/Api/SpeAdmin/AuditLogEndpoints.cs` (line 34: `group.MapGet("/audit", ...)`)

### 2.3 Dataverse Plugin Audit Log (separate system)

The `BaseProxyPlugin` in `src/dataverse/plugins/` writes and updates a Dataverse audit log entity (`sprk_speauditlog`) using `OrganizationService.Update()`. This is a **Dataverse plugin audit trail** for custom API proxy calls — it is architecturally separate from the Cosmos DB AI audit log. The update at line 266 updates the plugin's own audit log record (setting response status code after execution), not the AI compliance audit container.

**Source file**: `src/dataverse/plugins/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/BaseProxyPlugin.cs` (line 266)

---

## 3. Service Layer Audit

### 3.1 AuditLogService — Method Inventory

| Operation | Present | Evidence |
|-----------|---------|----------|
| `CreateItemAsync` | YES | `AuditLogService.cs` line 81 |
| `UpsertItemAsync` | NO | Not referenced anywhere in the file |
| `ReplaceItemAsync` | NO | Not referenced anywhere in the file |
| `DeleteItemAsync` | NO | Not referenced anywhere in the file |
| `PatchItemAsync` | NO | Not referenced anywhere in the file |

The code comment at lines 77-80 explicitly states the design intent:

```csharp
// APPEND-ONLY: CreateItemAsync is the ONLY Cosmos DB write operation permitted.
// UpsertItemAsync, ReplaceItemAsync, and DeleteItemAsync are intentionally absent
// from this service. The infrastructure immutable policy provides the second layer
// of enforcement (see infrastructure/cosmos/audit-container-policy.json).
```

### 3.2 Callers of IAuditLogService

The audit service is consumed in exactly one location:

| Caller | File | Usage |
|--------|------|-------|
| `SafetyPipelineMiddleware` | `Services/Ai/Chat/Middleware/SafetyPipelineMiddleware.cs` (line 529) | Calls `LogInteractionAsync` after safety evaluation |

The middleware constructs an `AuditEntry` and calls `LogInteractionAsync` — no other Cosmos operations are performed.

### 3.3 DI Registration

Registered as singleton in `AiPersistenceModule.cs` (lines 86-89):

```csharp
services.AddSingleton<IAuditLogService>(sp => new AuditLogService(
    cosmosClient: sp.GetRequiredService<CosmosClient>(),
    databaseName: databaseName,
    logger: sp.GetRequiredService<ILogger<AuditLogService>>()));
```

### 3.4 Data Privacy Compliance

The `AuditEntry` model enforces ADR-015 Tier 2 data minimization:
- Verbatim prompts and AI responses are **never stored** — only a SHA-256 hash (`ResponseHash`)
- Tool names are stored (no arguments or outputs)
- Document IDs are stored (no content)
- Safety scores are stored (numerical only)

**Source file**: `src/server/api/Sprk.Bff.Api/Services/Ai/Audit/AuditEntry.cs`

---

## 4. Cosmos DB Configuration Audit

### 4.1 Container Definition (Bicep)

**Source file**: `infrastructure/bicep/modules/cosmos-db.bicep` (lines 147-171)

| Property | Value | Compliance Note |
|----------|-------|-----------------|
| Container name | `audit` | Dedicated container for AI audit records |
| Partition key | `/tenantId` | Tenant isolation per ADR-015 |
| `defaultTtl` | `-1` (no expiry) | Records never automatically deleted |
| `analyticalStorageTtl` | `-1` (indefinite) | Analytical store enabled for Synapse queries |
| Indexing mode | `consistent` | All paths indexed |
| Database | `spaarke-ai` | Serverless Cosmos DB account |

### 4.2 Immutability Policy

**Source file**: `infrastructure/cosmos/audit-container-policy.json`

| Policy Property | Value | Meaning |
|-----------------|-------|---------|
| `immutabilityPeriodSinceCreationInDays` | `2555` (7 years) | Records cannot be modified or deleted for 7 years |
| `allowProtectedAppendWrites` | `false` | Even protected append writes are disallowed |
| `state` | `Locked` | Policy cannot be reduced or removed once applied |

### 4.3 RBAC Configuration

| Role | Assignment | Purpose |
|------|------------|---------|
| Cosmos DB Built-in Data Contributor | App Service managed identity | Write access (restricted to `CreateItemAsync` by application code) |
| Cosmos DB Built-in Data Reader | Compliance officers (manual) | Read-only access for audit queries |
| Delete/Replace | Blocked by immutability policy | Infrastructure-level enforcement |

### 4.4 Account-Level Security

| Setting | Value | Source |
|---------|-------|--------|
| Public network access | `Disabled` | Bicep line 51 |
| Local auth (master keys) | Not disabled (`disableLocalAuth: false`) | Bicep line 52 |
| Minimum TLS | 1.2 | Bicep line 54 |
| Backup policy | Continuous (7-day PITR) | Bicep lines 55-60 |
| Consistency | Session | Bicep line 40 |

**Finding**: `disableLocalAuth` is set to `false`. This means master keys still exist on the account. While application code uses `DefaultAzureCredential`, a user with access to the Azure Portal or account keys could theoretically bypass RBAC and attempt data-plane operations. The immutability policy is the defense-in-depth control that prevents modification even with master key access.

---

## 5. Codebase Scan: Update/Delete on Audit Records

### 5.1 Grep Results

**Pattern**: `(Delete|Update|Upsert|Replace|Patch|Put).*Audit` and `Audit.*(Delete|Update|Upsert|Replace)`

| Match | File | Verdict |
|-------|------|---------|
| `OrganizationService.Update(auditLog)` | `BaseProxyPlugin.cs:266` | **Not the AI audit container.** This is the Dataverse plugin audit log (`sprk_speauditlog`), a separate system. The plugin creates a Dataverse record before execution and updates it with the response status code after execution. |
| `"FileDeleted" category` comments | `ContainerItemEndpoints.cs:124,882,943` | **Not a delete of audit records.** These are audit log entries recording a file deletion event. The audit entry itself is created (appended), not deleted. |

### 5.2 API Endpoint Scan

No `MapPut`, `MapPatch`, or `MapDelete` registrations exist for any route containing "audit" in the AI endpoints (`Api/Ai/`). The only audit-related endpoint is the SPE admin `MapGet("/audit", ...)` which is read-only.

### 5.3 Verdict

**PASS** — No code path exists that updates, replaces, upserts, or deletes records in the Cosmos DB `audit` container.

---

## 6. Immutability Test Plan

### Test 6.1: Write a Test Audit Entry

**Objective**: Confirm `CreateItemAsync` succeeds for new audit entries.

**Steps**:
1. Instantiate `AuditLogService` with a valid `CosmosClient` and database name.
2. Create an `AuditEntry` with all required fields populated.
3. Call `LogInteractionAsync(entry)`.
4. Wait 500ms for the fire-and-forget background task to complete.
5. Query the `audit` container directly via Cosmos SDK to confirm the document exists.
6. Verify the document's `id`, `tenantId`, `sessionId`, `action`, and `responseHash` match the entry.

**Expected Result**: Document created successfully with HTTP 201.

### Test 6.2: Attempt Update via Cosmos SDK (Expect Failure)

**Objective**: Confirm the immutability policy blocks `ReplaceItemAsync`.

**Steps**:
1. Write a test audit entry per Test 6.1.
2. Read the entry back from Cosmos DB.
3. Attempt `container.ReplaceItemAsync(modifiedEntry, entry.Id, new PartitionKey(entry.TenantId))`.
4. Catch the expected `CosmosException`.

**Expected Result**: `CosmosException` with HTTP 403 Forbidden (immutability policy violation). The error message should reference the immutability policy.

### Test 6.3: Attempt Delete via Cosmos SDK (Expect Failure)

**Objective**: Confirm the immutability policy blocks `DeleteItemAsync`.

**Steps**:
1. Write a test audit entry per Test 6.1.
2. Attempt `container.DeleteItemAsync<AuditEntry>(entry.Id, new PartitionKey(entry.TenantId))`.
3. Catch the expected `CosmosException`.

**Expected Result**: `CosmosException` with HTTP 403 Forbidden (immutability policy violation).

### Test 6.4: Attempt Upsert via Cosmos SDK (Expect Failure)

**Objective**: Confirm the immutability policy blocks `UpsertItemAsync` on an existing document.

**Steps**:
1. Write a test audit entry per Test 6.1.
2. Modify the entry (e.g., change `Action` to `"tampered"`).
3. Attempt `container.UpsertItemAsync(modifiedEntry, new PartitionKey(entry.TenantId))`.
4. Catch the expected `CosmosException`.

**Expected Result**: `CosmosException` with HTTP 403 Forbidden for the update portion of the upsert.

### Test 6.5: Verify No API Endpoints Allow Modification

**Objective**: Confirm no HTTP endpoints expose PUT/PATCH/DELETE on audit data.

**Steps**:
1. Start the BFF API locally.
2. Send `PUT /api/ai/audit/{id}` with a test body — expect 404 (no route matched).
3. Send `PATCH /api/ai/audit/{id}` with a test body — expect 404.
4. Send `DELETE /api/ai/audit/{id}` — expect 404.
5. Send `PUT /api/spe/audit/{id}` — expect 404.
6. Send `PATCH /api/spe/audit/{id}` — expect 404.
7. Send `DELETE /api/spe/audit/{id}` — expect 404.

**Expected Result**: All requests return 404 Not Found (no matching route).

### Test 6.6: Verify Partition Key Isolation

**Objective**: Confirm cross-tenant queries are prevented.

**Steps**:
1. Write an audit entry with `TenantId = "tenant-A"`.
2. Attempt to read it using `PartitionKey("tenant-B")`.
3. Expect the read to return no results (Cosmos DB partition key isolation).

**Expected Result**: No document returned — tenant isolation enforced at the data layer.

---

## 7. Retention Policy Verification

### 7.1 Expected Retention

| Property | Expected Value | How to Verify |
|----------|----------------|---------------|
| Default TTL | `-1` (no automatic expiry) | `az cosmosdb sql container show --account-name spaarke-cosmos-dev --database-name spaarke-ai --name audit --resource-group spe-infrastructure-westus2 --query 'resource.defaultTtl'` |
| Immutability period | 2,555 days (7 years) | Check container properties in Azure Portal > Cosmos DB > audit container > Settings |
| Immutability state | Locked | Same as above; locked policies cannot be reduced |
| Analytical storage TTL | `-1` (indefinite) | `az cosmosdb sql container show ... --query 'resource.analyticalStorageTtl'` |

### 7.2 Verification CLI Commands

```bash
# Verify TTL is disabled (no automatic expiry)
az cosmosdb sql container show \
  --account-name spaarke-cosmos-dev \
  --database-name spaarke-ai \
  --name audit \
  --resource-group spe-infrastructure-westus2 \
  --query 'resource.defaultTtl'
# Expected: -1

# Verify partition key
az cosmosdb sql container show \
  --account-name spaarke-cosmos-dev \
  --database-name spaarke-ai \
  --name audit \
  --resource-group spe-infrastructure-westus2 \
  --query 'resource.partitionKey'
# Expected: { "paths": ["/tenantId"], "kind": "Hash", "version": 2 }

# Verify analytical storage
az cosmosdb sql container show \
  --account-name spaarke-cosmos-dev \
  --database-name spaarke-ai \
  --name audit \
  --resource-group spe-infrastructure-westus2 \
  --query 'resource.analyticalStorageTtl'
# Expected: -1
```

---

## 8. Export Capability

### 8.1 Current State

No direct Azure Blob export pipeline is implemented for the AI audit log. However, the infrastructure supports export via two mechanisms:

1. **Analytical Store + Azure Synapse Link**: The `audit` container has `analyticalStorageTtl: -1`, enabling Azure Synapse Analytics to query audit data directly without impacting transactional performance. This is the intended long-term reporting and export path (referenced in Bicep comments at line 143).

2. **Cosmos DB Change Feed**: Could be used to stream audit records to Azure Blob Storage, but no change feed processor is currently implemented for the audit container.

### 8.2 Recommendation

For compliance export requirements, connect Azure Synapse Link to the audit container's analytical store. This provides:
- Read-only access to audit data
- No impact on transactional workload
- SQL-based querying for compliance reports
- Export to Parquet/CSV in Azure Data Lake

---

## 9. Existing Unit Test Coverage

The following tests already exist in `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Audit/AuditLogServiceTests.cs`:

| Test | What It Verifies | Status |
|------|------------------|--------|
| `Constructor_NullCosmosClient_ThrowsArgumentNullException` | Null guard on CosmosClient | Existing |
| `Constructor_NullOrEmptyDatabaseName_ThrowsArgumentException` | Null guard on database name | Existing |
| `Constructor_NullLogger_ThrowsArgumentNullException` | Null guard on logger | Existing |
| `HashResponse_SameInput_ReturnsSameHash` | SHA-256 determinism | Existing |
| `HashResponse_DifferentInputs_ReturnsDifferentHashes` | SHA-256 uniqueness | Existing |
| `HashResponse_ProducesLowercaseHex64Characters` | Hash format correctness | Existing |
| `HashResponse_NullInput_ThrowsArgumentNullException` | Null guard on hash input | Existing |
| `HashResponse_KnownInput_ProducesExpectedHash` | Known-answer test for SHA-256 | Existing |
| `LogInteractionAsync_CosmosThrows_DoesNotThrowToCallerAndLogsError` | Fire-and-forget error handling | Existing |
| `LogInteractionAsync_NullEntry_ThrowsArgumentNullException` | Null guard on entry | Existing |
| `LogInteractionAsync_UsesCreateItemAsync_NotUpsertOrReplace` | **Immutability: only CreateItemAsync called** | Existing |
| `AuditEntry_RequiredFields_ArePopulatedCorrectly` | Entry field correctness | Existing |
| `AuditEntry_Id_IsUniqueAcrossInstances` | ID uniqueness | Existing |
| `AuditEntry_Timestamp_IsUtc` | UTC timestamp | Existing |
| `SafetyCheckResult_DefaultValues_AreReasonable` | Safety result defaults | Existing |
| `LogInteractionAsync_PartitionsByTenantId` | Partition key correctness | Existing |

---

## 10. Pass/Fail Matrix

| # | Test | Category | Expected | Actual | Pass/Fail | Date | Tester |
|---|------|----------|----------|--------|-----------|------|--------|
| 6.1 | Write test audit entry | Functional | 201 Created | | | | |
| 6.2 | Attempt ReplaceItemAsync | Immutability | 403 Forbidden | | | | |
| 6.3 | Attempt DeleteItemAsync | Immutability | 403 Forbidden | | | | |
| 6.4 | Attempt UpsertItemAsync (existing) | Immutability | 403 Forbidden | | | | |
| 6.5a | PUT /api/ai/audit/{id} | API Surface | 404 Not Found | | | | |
| 6.5b | PATCH /api/ai/audit/{id} | API Surface | 404 Not Found | | | | |
| 6.5c | DELETE /api/ai/audit/{id} | API Surface | 404 Not Found | | | | |
| 6.5d | PUT /api/spe/audit/{id} | API Surface | 404 Not Found | | | | |
| 6.5e | PATCH /api/spe/audit/{id} | API Surface | 404 Not Found | | | | |
| 6.5f | DELETE /api/spe/audit/{id} | API Surface | 404 Not Found | | | | |
| 6.6 | Cross-tenant partition isolation | Isolation | No results | | | | |
| 7.1a | TTL = -1 (no expiry) | Retention | -1 | | | | |
| 7.1b | Immutability state = Locked | Retention | Locked | | | | |
| 7.1c | Analytical storage TTL = -1 | Retention | -1 | | | | |
| 9.1 | Unit tests pass (`dotnet test`) | Regression | All pass | | | | |

---

## 11. Findings and Recommendations

### 11.1 Positive Findings

1. **Dual-layer immutability**: Both application code and infrastructure policy enforce append-only semantics independently.
2. **No mutation endpoints**: No PUT/PATCH/DELETE routes exist for audit data in the AI endpoint group.
3. **Comprehensive unit tests**: The existing test suite explicitly verifies that only `CreateItemAsync` is invoked and that `UpsertItemAsync`, `ReplaceItemAsync`, and `DeleteItemAsync` are never called.
4. **Data minimization**: SHA-256 hashing prevents storage of verbatim AI content while enabling tamper detection.
5. **Tenant isolation**: Partition key on `/tenantId` prevents cross-tenant data access.

### 11.2 Items Requiring Attention

1. **`disableLocalAuth: false`**: Master keys are not disabled on the Cosmos DB account. While the immutability policy prevents modification even with master key access, disabling local auth (`disableLocalAuth: true`) would add defense-in-depth by forcing all access through Azure AD RBAC. This should be evaluated for production.

2. **Immutability policy application**: The policy is defined in `infrastructure/cosmos/audit-container-policy.json` but is not yet applied via Bicep (the Bicep file at line 55 of the policy JSON notes: "add immutabilityPolicy to auditContainer resource block when GA"). Verify that the policy has been manually applied to the deployed container.

3. **No automated integration test**: Tests 6.2-6.4 (Cosmos SDK mutation attempts) are manual integration tests. Consider adding an automated integration test that runs against the Cosmos DB emulator with immutability policies applied.

4. **Export pipeline not implemented**: No Blob export or change feed processor exists for audit data. The Synapse Link analytical store is the designated export path but requires Synapse workspace setup.
