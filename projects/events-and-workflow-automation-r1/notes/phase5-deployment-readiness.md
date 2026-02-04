# Phase 5 - BFF API Deployment Readiness

> **Created**: 2026-02-01
> **Task**: 058 - Deploy Phase 5 - BFF API
> **Status**: Ready for Deployment (Build Verified)

---

## API Build Status

- [x] Solution builds without errors
- [x] All projects compile successfully

**Build Command**: `dotnet build src/server/api/Sprk.Bff.Api/`

**Build Output**:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:01.33
```

---

## Endpoints Registered

Verified in `Program.cs`:

- [x] `app.MapFieldMappingEndpoints()` (line 1367)
- [x] `app.MapEventEndpoints()` (line 1370)

Both endpoint groups are registered after authentication and authorization middleware, with proper CORS and rate limiting configuration.

---

## Endpoint Summary

### Field Mapping Endpoints

| Group | Endpoint | Method | Status | Task |
|-------|----------|--------|--------|------|
| Field Mappings | `/api/v1/field-mappings/profiles` | GET | Ready | 013 |
| Field Mappings | `/api/v1/field-mappings/profiles/{sourceEntity}/{targetEntity}` | GET | Ready | 014 |
| Field Mappings | `/api/v1/field-mappings/validate` | POST | Ready | 015 |
| Field Mappings | `/api/v1/field-mappings/push` | POST | Ready | 054 |

### Event Endpoints

| Group | Endpoint | Method | Status | Task |
|-------|----------|--------|--------|------|
| Events | `/api/v1/events` | GET | Ready | 050 |
| Events | `/api/v1/events/{id}` | GET | Ready | 050 |
| Events | `/api/v1/events` | POST | Ready | 051 |
| Events | `/api/v1/events/{id}` | PUT | Ready | 051 |
| Events | `/api/v1/events/{id}` | DELETE | Ready | 052 |
| Events | `/api/v1/events/{id}/complete` | POST | Ready | 053 |
| Events | `/api/v1/events/{id}/cancel` | POST | Ready | 053 |
| Events | `/api/v1/events/{id}/logs` | GET | Ready | 055 |

**Total Endpoints**: 12 (4 Field Mapping + 8 Event)

---

## Stubs Remaining

These API stubs return mock/empty data until Dataverse entities are deployed:

### Field Mapping API Stubs

| Stub ID | Description | Resolution Trigger |
|---------|-------------|-------------------|
| S013-01 | Query `sprk_fieldmappingprofile` entity for GET /profiles | `sprk_fieldmappingprofile` deployed |
| S014-01 | Query `sprk_fieldmappingprofile` by source/target entities | `sprk_fieldmappingprofile` deployed |
| S054-01 | Retrieve source record field values for push endpoint | `IDataverseService.RetrieveAsync` implemented |
| S054-02 | Query child records and update records for push endpoint | `IDataverseService` query/update methods |

### Event API Stubs

| Stub ID | Description | Resolution Trigger |
|---------|-------------|-------------------|
| S050-01 | Query `sprk_event` entity from Dataverse | `sprk_event` entity deployed |
| S051-01 | Create `sprk_event` record in Dataverse | `sprk_event` entity deployed |
| S051-02 | Update `sprk_event` record in Dataverse | `sprk_event` entity deployed |
| S052-01 | Update `sprk_event` statuscode to Canceled (soft delete) | `sprk_event` entity deployed |
| S053-01 | Update `sprk_event` statuscode to Completed | `sprk_event` entity deployed |
| S053-02 | Update `sprk_event` statuscode to Cancelled | `sprk_event` entity deployed |
| S055-01 | Create/Query `sprk_eventlog` records in Dataverse | `sprk_eventlog` entity deployed |

**Note**: Stubs are intentional for this phase. They allow the API structure and contracts to be deployed and tested before Dataverse entities are available. Each stub is documented with expected implementation and FetchXML/OData queries.

---

## Security Configuration

All endpoints are configured with proper security:

- [x] **RequireAuthorization()** - All endpoints require authentication
- [x] **RequireRateLimiting("dataverse-query")** - Sliding window rate limiting (50 requests/minute)
- [x] **CORS** - Configured for Dataverse and PowerApps origins (*.dynamics.com, *.powerapps.com)

### Rate Limiting Policy: `dataverse-query`

```csharp
Window: 1 minute
PermitLimit: 50 requests
QueueLimit: 5
SegmentsPerWindow: 4 (15-second segments)
```

---

## API Response Patterns

All endpoints follow established patterns:

- [x] **ProblemDetails** for error responses (ADR-019)
- [x] **TypedResults** for success responses
- [x] **ValidationProblem** for input validation errors
- [x] **Structured logging** with correlation IDs

---

## Deployment Target

| Environment | URL | Status |
|-------------|-----|--------|
| Dev | `https://spe-api-dev-67e2xz.azurewebsites.net` | Ready for Deployment |

### Deployment Options

1. **Deploy-BffApi.ps1** script (recommended):
   ```powershell
   .\scripts\Deploy-BffApi.ps1 -Environment dev
   ```

2. **Azure CLI**:
   ```bash
   dotnet publish -c Release -o ./publish
   az webapp deploy --resource-group spe-infrastructure-westus2 \
       --name spe-api-dev-67e2xz --src-path ./publish.zip
   ```

---

## Testing Endpoints

After deployment, endpoints can be tested with:

### Example: Get Events
```bash
curl -H "Authorization: Bearer {token}" \
     https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/events
```

### Example: Get Field Mapping Profiles
```bash
curl -H "Authorization: Bearer {token}" \
     https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/field-mappings/profiles
```

### Example: Validate Field Mapping
```bash
curl -X POST \
     -H "Authorization: Bearer {token}" \
     -H "Content-Type: application/json" \
     -d '{"sourceFieldType": "Text", "targetFieldType": "Text"}' \
     https://spe-api-dev-67e2xz.azurewebsites.net/api/v1/field-mappings/validate
```

---

## Dependencies

### Completed Tasks (Required)

| Task | Title | Status |
|------|-------|--------|
| 013 | Create Field Mapping API - GET profiles | Completed |
| 014 | Create Field Mapping API - GET profile by source/target | Completed |
| 015 | Create Field Mapping API - POST validate | Completed |
| 050 | Create Event API - GET endpoints | Completed |
| 051 | Create Event API - POST/PUT endpoints | Completed |
| 052 | Create Event API - DELETE endpoint | Completed |
| 053 | Create Event API - complete/cancel actions | Completed |
| 054 | Create Field Mapping API - POST push | Completed |
| 055 | Implement Event Log creation on state changes | Completed |
| 057 | Write integration tests for Field Mapping API | Completed |

### Pending Task

| Task | Title | Status | Notes |
|------|-------|--------|-------|
| 056 | Write integration tests for Event API | Pending | Blocked by this deployment task |

---

## Blocking Tasks

This deployment (Task 058) blocks the following tasks:

| Task | Title | Phase |
|------|-------|-------|
| 060 | E2E test - Event creation with regarding record | 6 |
| 061 | E2E test - Field mapping auto-application | 6 |
| 062 | E2E test - Refresh from Parent flow | 6 |
| 063 | E2E test - Update Related push flow | 6 |

---

## Verification Checklist

Pre-Deployment:
- [x] API builds successfully
- [x] All 12 endpoints registered
- [x] Security configured (auth, rate limiting, CORS)
- [x] ProblemDetails error handling in place
- [x] Stubs documented with expected implementation

Post-Deployment (to be verified):
- [ ] Health check passes (`GET /healthz`)
- [ ] Ping endpoint responds (`GET /ping`)
- [ ] Status endpoint shows version (`GET /status`)
- [ ] Event endpoints return 401 without token
- [ ] Field Mapping endpoints return 401 without token
- [ ] Authenticated requests return 200 with empty data (stubs)

---

*This document was generated by task-execute skill for Task 058.*
