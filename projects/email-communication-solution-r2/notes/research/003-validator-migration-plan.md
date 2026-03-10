# ECS-003: ApprovedSenderValidator Migration — Detailed Implementation Plan

> **Task**: Complete ApprovedSenderValidator Migration to sprk_communicationaccount
> **Date**: 2026-03-09
> **Status**: Research & Assessment (no code changes)

---

## 1. Current State Analysis

### ApprovedSenderValidator.cs (223 lines)

**File**: `src/server/api/Sprk.Bff.Api/Services/Communication/ApprovedSenderValidator.cs`

**Purpose**: Validates that a requested sender mailbox is in the approved senders list. Has two resolution paths:
- `Resolve(string? fromMailbox)` — Synchronous, config-only (reads from `CommunicationOptions.ApprovedSenders[]`)
- `ResolveAsync(string? fromMailbox, CancellationToken ct)` — Async, merges BFF config with Dataverse `sprk_communicationaccount` records via `CommunicationAccountService`, cached in Redis

**Dependencies**:
- `CommunicationAccountService` — Injected, used to query send-enabled accounts
- `IDistributedCache` — Redis cache for merged senders list
- `CommunicationOptions` — Config fallback (`ApprovedSenders[]`, `DefaultMailbox`)

**Cache Key**: `"communication:approved-senders"` (line 18), TTL: 5 minutes

**Key Methods**:
| Method | Signature | Purpose |
|--------|-----------|---------|
| `Resolve` | `ApprovedSenderResult Resolve(string? fromMailbox)` | Sync config-only resolution |
| `ResolveAsync` | `Task<ApprovedSenderResult> ResolveAsync(string? fromMailbox, CancellationToken ct)` | Async merged resolution |
| `GetMergedSendersAsync` | `Task<ApprovedSenderConfig[]> GetMergedSendersAsync(CancellationToken ct)` | Cache + Dataverse + config merge |
| `MapToApprovedSenderConfigs` | `static ApprovedSenderConfig[] MapToApprovedSenderConfigs(CommunicationAccount[])` | Maps accounts to sender configs |
| `MergeSenders` | `static ApprovedSenderConfig[] MergeSenders(...)` | Config base, Dataverse overlay |
| `ResolveDefault` | `ApprovedSenderResult ResolveDefault(ApprovedSenderConfig[])` | Default sender resolution |
| `ResolveExplicit` | `static ApprovedSenderResult ResolveExplicit(string, ApprovedSenderConfig[])` | Explicit sender validation |

**Important**: The `ApprovedSenderValidator` itself does NOT directly reference `sprk_sendenableds` or `sprk_communiationtype`. It delegates Dataverse queries to `CommunicationAccountService`. The field name fixes are in `CommunicationAccountService` and other files.

### CommunicationAccountService.cs (164 lines)

**File**: `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationAccountService.cs`

**Purpose**: Queries `sprk_communicationaccount` records from Dataverse with Redis caching.

**Cache Keys**:
- `"comm:accounts:send-enabled"` (line 16)
- `"comm:accounts:receive-enabled"` (line 17)

**Key issue**: Line 42 uses incorrect field name in OData filter:
```csharp
"sprk_sendenableds eq true and statecode eq 0"
```
Should be:
```csharp
"sprk_sendenabled eq true and statecode eq 0"
```

Line 145 uses incorrect field name in entity mapping:
```csharp
SendEnabled = entity.GetAttributeValue<bool>("sprk_sendenableds"),
```
Should be:
```csharp
SendEnabled = entity.GetAttributeValue<bool>("sprk_sendenabled"),
```

### IDataverseService — QueryApprovedSendersAsync (Legacy Method)

The legacy method `QueryApprovedSendersAsync` still exists in the interface and implementations but is NOT called by `ApprovedSenderValidator` (which now uses `CommunicationAccountService.QuerySendEnabledAccountsAsync`). It queries the old `sprk_approvedsender` entity.

**Files containing `QueryApprovedSendersAsync`**:
| File | Line | Role |
|------|------|------|
| `src/server/shared/Spaarke.Dataverse/IDataverseService.cs` | 476 | Interface declaration |
| `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` | 1784 | Implementation (queries `sprk_approvedsender`) |
| `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` | 1843 | Throws `NotImplementedException` |

---

## 2. Field Name Fix Inventory: `sprk_sendenableds` -> `sprk_sendenabled`

### Source Code Files

| # | File | Line | Current (old) | New (corrected) |
|---|------|------|---------------|-----------------|
| 1 | `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationAccountService.cs` | 35 | `sprk_sendenableds eq true` (XML doc comment) | `sprk_sendenabled eq true` |
| 2 | `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationAccountService.cs` | 42 | `"sprk_sendenableds eq true and statecode eq 0"` | `"sprk_sendenabled eq true and statecode eq 0"` |
| 3 | `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationAccountService.cs` | 145 | `entity.GetAttributeValue<bool>("sprk_sendenableds")` | `entity.GetAttributeValue<bool>("sprk_sendenabled")` |
| 4 | `src/server/api/Sprk.Bff.Api/Services/Communication/Models/CommunicationAccount.cs` | 15 | `/// Note: actual Dataverse field is sprk_sendenableds (trailing 's').` | `/// Maps to Dataverse field sprk_sendenabled.` |
| 5 | `src/server/api/Sprk.Bff.Api/Services/Communication/MailboxVerificationService.cs` | 38 | `Tests send (if sprk_sendenableds=true)` (doc comment) | `Tests send (if sprk_sendenabled=true)` |
| 6 | `src/server/api/Sprk.Bff.Api/Services/Communication/MailboxVerificationService.cs` | 58 | `"sprk_sendenableds"` (in string array for RetrieveAsync $select) | `"sprk_sendenabled"` |
| 7 | `src/server/api/Sprk.Bff.Api/Services/Communication/MailboxVerificationService.cs` | 291 | `entity.GetAttributeValue<bool>("sprk_sendenableds")` | `entity.GetAttributeValue<bool>("sprk_sendenabled")` |
| 8 | `src/server/shared/Spaarke.Dataverse/IDataverseService.cs` | 485 | `"sprk_sendenableds eq true and statecode eq 0"` (doc comment) | `"sprk_sendenabled eq true and statecode eq 0"` |

### Web Resource Files

| # | File | Line | Current (old) | New (corrected) |
|---|------|------|---------------|-----------------|
| 9 | `src/client/webresources/js/sprk_communication_send.js` | 757 | `sprk_sendenableds = true` (comment) | `sprk_sendenabled = true` |
| 10 | `src/client/webresources/js/sprk_communication_send.js` | 791 | `sprk_sendenableds (trailing 's')` (comment) | Update comment to reflect corrected name |
| 11 | `src/client/webresources/js/sprk_communication_send.js` | 792 | `"?$filter=sprk_sendenableds eq true and statecode eq 0"` | `"?$filter=sprk_sendenabled eq true and statecode eq 0"` |
| 12 | `infrastructure/dataverse/ribbon/CommunicationRibbons/WebResources/sprk_communication_send.js` | 757 | `sprk_sendenableds = true` (comment) | `sprk_sendenabled = true` |
| 13 | `infrastructure/dataverse/ribbon/CommunicationRibbons/WebResources/sprk_communication_send.js` | 791 | `sprk_sendenableds (trailing 's')` (comment) | Update comment |
| 14 | `infrastructure/dataverse/ribbon/CommunicationRibbons/WebResources/sprk_communication_send.js` | 792 | `"?$filter=sprk_sendenableds eq true and statecode eq 0"` | `"?$filter=sprk_sendenabled eq true and statecode eq 0"` |

### Test Files

| # | File | Line | Current (old) | New (corrected) |
|---|------|------|---------------|-----------------|
| 15 | `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationAccountServiceTests.cs` | 74 | `entity["sprk_sendenableds"] = sendEnabled;` | `entity["sprk_sendenabled"] = sendEnabled;` |
| 16 | `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationAccountServiceTests.cs` | 154 | `f.Contains("sprk_sendenableds eq true")` | `f.Contains("sprk_sendenabled eq true")` |
| 17 | `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/ApprovedSenderMergeTests.cs` | 85 | `entity["sprk_sendenableds"] = true;` | `entity["sprk_sendenabled"] = true;` |
| 18 | `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/ApprovedSenderMergeTests.cs` | 418 | `dvEntity["sprk_sendenableds"] = true;` | `dvEntity["sprk_sendenabled"] = true;` |
| 19 | `tests/unit/Sprk.Bff.Api.Tests/Integration/CommunicationIntegrationTests.cs` | 464 | `dataverseSenderEntity["sprk_sendenableds"] = true;` | `dataverseSenderEntity["sprk_sendenabled"] = true;` |
| 20 | `tests/unit/Sprk.Bff.Api.Tests/Integration/CommunicationIntegrationTests.cs` | 787 | `accountEntity["sprk_sendenableds"] = true;` | `accountEntity["sprk_sendenabled"] = true;` |
| 21 | `tests/unit/Sprk.Bff.Api.Tests/Integration/CommunicationIntegrationTests.cs` | 893 | `accountEntity["sprk_sendenableds"] = true;` | `accountEntity["sprk_sendenabled"] = true;` |
| 22 | `tests/unit/Sprk.Bff.Api.Tests/Integration/CommunicationIntegrationTests.cs` | 970 | `dataverseSenderEntity["sprk_sendenableds"] = true;` | `dataverseSenderEntity["sprk_sendenabled"] = true;` |

**Total occurrences in code/tests/webresources: 22**

---

## 3. Field Name Fix Inventory: `sprk_communiationtype` -> `sprk_communicationtype`

### Source Code Files

| # | File | Line | Current (old) | New (corrected) |
|---|------|------|---------------|-----------------|
| 1 | `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs` | 547 | `["sprk_communiationtype"] = new OptionSetValue(...)` | `["sprk_communicationtype"] = new OptionSetValue(...)` |
| 2 | `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs` | 681 | `["sprk_communiationtype"] = new OptionSetValue(...)` | `["sprk_communicationtype"] = new OptionSetValue(...)` |
| 3 | `src/server/api/Sprk.Bff.Api/Services/Communication/IncomingCommunicationProcessor.cs` | 135 | `sprk_communiationtype (intentional typo)` (comment) | Update comment |
| 4 | `src/server/api/Sprk.Bff.Api/Services/Communication/IncomingCommunicationProcessor.cs` | 279 | `["sprk_communiationtype"] = new OptionSetValue(...)` | `["sprk_communicationtype"] = new OptionSetValue(...)` |
| 5 | `src/server/api/Sprk.Bff.Api/Services/Communication/Models/CommunicationType.cs` | 4 | `Maps to Dataverse sprk_communiationtype (note: typo is intentional)` (comment) | `Maps to Dataverse sprk_communicationtype.` |

### Web Resource Files

| # | File | Line | Current (old) | New (corrected) |
|---|------|------|---------------|-----------------|
| 6 | `src/client/webresources/js/sprk_communication_send.js` | 360 | `sprk_communiationtype - intentional typo` (comment) | Update comment |
| 7 | `src/client/webresources/js/sprk_communication_send.js` | 362 | `formContext.getAttribute("sprk_communiationtype")` | `formContext.getAttribute("sprk_communicationtype")` |
| 8 | `infrastructure/dataverse/ribbon/CommunicationRibbons/WebResources/sprk_communication_send.js` | 360 | `sprk_communiationtype - intentional typo` (comment) | Update comment |
| 9 | `infrastructure/dataverse/ribbon/CommunicationRibbons/WebResources/sprk_communication_send.js` | 362 | `formContext.getAttribute("sprk_communiationtype")` | `formContext.getAttribute("sprk_communicationtype")` |

### Test Files

| # | File | Line | Current (old) | New (corrected) |
|---|------|------|---------------|-----------------|
| 10 | `tests/unit/Sprk.Bff.Api.Tests/Integration/CommunicationIntegrationTests.cs` | 218 | `e["sprk_communiationtype"]` | `e["sprk_communicationtype"]` |
| 11 | `tests/unit/Sprk.Bff.Api.Tests/Integration/CommunicationIntegrationTests.cs` | 691 | `ContainKey("sprk_communiationtype"` | `ContainKey("sprk_communicationtype"` |
| 12 | `tests/unit/Sprk.Bff.Api.Tests/Integration/CommunicationIntegrationTests.cs` | 692 | `"Field uses intentional Dataverse schema typo: sprk_communiationtype"` | Update assertion message |
| 13 | `tests/unit/Sprk.Bff.Api.Tests/Integration/CommunicationIntegrationTests.cs` | 695 | `capturedEntity["sprk_communiationtype"]` | `capturedEntity["sprk_communicationtype"]` |
| 14 | `tests/unit/Sprk.Bff.Api.Tests/Integration/CommunicationIntegrationTests.cs` | 696 | `capturedEntity["sprk_communiationtype"]` | `capturedEntity["sprk_communicationtype"]` |
| 15 | `tests/unit/Sprk.Bff.Api.Tests/Integration/CommunicationIntegrationTests.cs` | 1448 | `capturedEntity["sprk_communiationtype"]` | `capturedEntity["sprk_communicationtype"]` |
| 16 | `tests/unit/Sprk.Bff.Api.Tests/Integration/CommunicationIntegrationTests.cs` | 1449 | `capturedEntity["sprk_communiationtype"]` | `capturedEntity["sprk_communicationtype"]` |
| 17 | `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/InboundPipelineTests.cs` | 587 | `entity.GetAttributeValue<OptionSetValue>("sprk_communiationtype")` | `entity.GetAttributeValue<OptionSetValue>("sprk_communicationtype")` |
| 18 | `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/InboundPipelineTests.cs` | 589 | `"sprk_communiationtype should be Email"` (assertion message) | Update message |
| 19 | `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/DataverseRecordCreationTests.cs` | 148 | `#region Communication Type Field (sprk_communiationtype - intentional typo)` | Update region name |
| 20 | `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/DataverseRecordCreationTests.cs` | 162 | `_capturedEntity!["sprk_communiationtype"]` | `_capturedEntity!["sprk_communicationtype"]` |

**Total occurrences in code/tests/webresources: 20**

---

## 4. Method Rename Plan: `QueryApprovedSendersAsync` -> `QueryCommunicationAccountsAsync`

`QueryCommunicationAccountsAsync` already exists in the codebase. The legacy `QueryApprovedSendersAsync` queries the old `sprk_approvedsender` entity and is no longer called by `ApprovedSenderValidator` (which now uses `CommunicationAccountService`).

### Action: Remove Legacy Method

The `ApprovedSenderValidator` already delegates to `CommunicationAccountService.QuerySendEnabledAccountsAsync()` (line 108). No call sites exist for `QueryApprovedSendersAsync` in test files (confirmed by grep). The method should be **removed** from:

| # | File | Line | Action |
|---|------|------|--------|
| 1 | `src/server/shared/Spaarke.Dataverse/IDataverseService.cs` | 470-476 | Remove `QueryApprovedSendersAsync` declaration and doc comments |
| 2 | `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` | 1780-1805 | Remove `QueryApprovedSendersAsync` implementation + section comment |
| 3 | `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` | 1843-1848 | Remove `QueryApprovedSendersAsync` NotImplementedException stub |

**Risk Assessment**: LOW. Grep confirms zero call sites in tests or application code. The method queries `sprk_approvedsender` which is the old entity. All functionality has been migrated to `CommunicationAccountService.QuerySendEnabledAccountsAsync()` which queries `sprk_communicationaccount`.

---

## 5. Cache Key Changes

### ApprovedSenderValidator Cache

| Location | Current Key | New Key | File:Line |
|----------|------------|---------|-----------|
| ApprovedSenderValidator.cs | `"communication:approved-senders"` | `"communication:approved-senders"` | Line 18 |

**Decision**: The task POML says to update from `approved-senders` to `communication-accounts`. Proposed change:

```
Old: "communication:approved-senders"
New: "communication:accounts:merged"
```

This aligns with the `CommunicationAccountService` pattern (`comm:accounts:send-enabled`, `comm:accounts:receive-enabled`) and clarifies that this key holds the *merged* result (config + Dataverse).

### CommunicationAccountService Cache (No Change Needed)

These keys are already correct and do not need updating:
- `"comm:accounts:send-enabled"` (line 16)
- `"comm:accounts:receive-enabled"` (line 17)

### Test Files Referencing Cache Keys

| File | Line | Current | Action |
|------|------|---------|--------|
| `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/ApprovedSenderMergeTests.cs` | 94+ | Tests use `_cacheMock` with `It.IsAny<string>()` | No change needed (wildcard match) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationAccountServiceTests.cs` | 89, 172, 320 | References `"comm:accounts:send-enabled"` | No change needed (CommunicationAccountService keys unchanged) |

### Documentation References

| File | Line | Current | Action |
|------|------|---------|--------|
| `docs/architecture/communication-service-architecture.md` | 339 | `communication:approved-senders` | Update to new key |

---

## 6. OData Filter Updates

### CommunicationAccountService.QuerySendEnabledAccountsAsync

**Current** (line 42):
```csharp
"sprk_sendenableds eq true and statecode eq 0"
```

**New**:
```csharp
"sprk_sendenabled eq true and statecode eq 0"
```

### Web Resource OData Query

**Current** (`sprk_communication_send.js` line 792):
```javascript
var filter = "?$filter=sprk_sendenableds eq true and statecode eq 0" +
```

**New**:
```javascript
var filter = "?$filter=sprk_sendenabled eq true and statecode eq 0" +
```

### QueryReceiveEnabledAccountsAsync (Already Correct)

Line 55 uses `"sprk_receiveenabled eq true and statecode eq 0"` which is already correct (no typo).

---

## 7. Test Update Plan

### Test Files Requiring `sprk_sendenableds` -> `sprk_sendenabled` Fix

| File | Occurrences | Lines |
|------|-------------|-------|
| `CommunicationAccountServiceTests.cs` | 2 | 74, 154 |
| `ApprovedSenderMergeTests.cs` | 2 | 85, 418 |
| `CommunicationIntegrationTests.cs` | 4 | 464, 787, 893, 970 |
| **Subtotal** | **8** | |

### Test Files Requiring `sprk_communiationtype` -> `sprk_communicationtype` Fix

| File | Occurrences | Lines |
|------|-------------|-------|
| `CommunicationIntegrationTests.cs` | 7 | 218, 691, 692, 695, 696, 1448, 1449 |
| `InboundPipelineTests.cs` | 2 | 587, 589 |
| `DataverseRecordCreationTests.cs` | 2 | 148, 162 |
| **Subtotal** | **11** | |

### Test Files Requiring NO Changes (ApprovedSenderValidator-specific tests)

| File | Lines | Reason |
|------|-------|--------|
| `ApprovedSenderValidatorTests.cs` | 218 lines | Tests `Resolve()` (sync, config-only). No Dataverse field references. No changes needed. |

### Assertion Text Updates

Several test assertion messages reference the "intentional typo" — these need updating:

| File | Line | Old Assertion Text | New Assertion Text |
|------|------|-------------------|-------------------|
| `CommunicationIntegrationTests.cs` | 692 | `"Field uses intentional Dataverse schema typo: sprk_communiationtype"` | `"sprk_communicationtype field should be set"` |
| `InboundPipelineTests.cs` | 589 | `"sprk_communiationtype should be Email (100000000)"` | `"sprk_communicationtype should be Email (100000000)"` |
| `DataverseRecordCreationTests.cs` | 148 | `#region Communication Type Field (sprk_communiationtype - intentional typo)` | `#region Communication Type Field (sprk_communicationtype)` |

---

## 8. Complete Change Summary

### Files to Modify (by priority)

**Priority 1 — Source Code (BFF API)**:
1. `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationAccountService.cs` — 3 changes (`sprk_sendenableds`)
2. `src/server/api/Sprk.Bff.Api/Services/Communication/MailboxVerificationService.cs` — 3 changes (`sprk_sendenableds`)
3. `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs` — 2 changes (`sprk_communiationtype`)
4. `src/server/api/Sprk.Bff.Api/Services/Communication/IncomingCommunicationProcessor.cs` — 2 changes (`sprk_communiationtype`)
5. `src/server/api/Sprk.Bff.Api/Services/Communication/ApprovedSenderValidator.cs` — 1 change (cache key)
6. `src/server/api/Sprk.Bff.Api/Services/Communication/Models/CommunicationAccount.cs` — 1 change (doc comment)
7. `src/server/api/Sprk.Bff.Api/Services/Communication/Models/CommunicationType.cs` — 1 change (doc comment)

**Priority 2 — Shared Library (Remove Legacy Method)**:
8. `src/server/shared/Spaarke.Dataverse/IDataverseService.cs` — Remove `QueryApprovedSendersAsync` + update doc comment
9. `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` — Remove `QueryApprovedSendersAsync` implementation
10. `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` — Remove `QueryApprovedSendersAsync` stub

**Priority 3 — Web Resources**:
11. `src/client/webresources/js/sprk_communication_send.js` — 4 changes (both field names)
12. `infrastructure/dataverse/ribbon/CommunicationRibbons/WebResources/sprk_communication_send.js` — 4 changes (mirror of above)

**Priority 4 — Tests**:
13. `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationAccountServiceTests.cs` — 2 changes
14. `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/ApprovedSenderMergeTests.cs` — 2 changes
15. `tests/unit/Sprk.Bff.Api.Tests/Integration/CommunicationIntegrationTests.cs` — 11 changes
16. `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/InboundPipelineTests.cs` — 2 changes
17. `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/DataverseRecordCreationTests.cs` — 2 changes

**Priority 5 — Documentation**:
18. `docs/architecture/communication-service-architecture.md` — Update cache key reference

### Total Change Count

| Change Category | Occurrences |
|----------------|-------------|
| `sprk_sendenableds` -> `sprk_sendenabled` | 22 |
| `sprk_communiationtype` -> `sprk_communicationtype` | 20 |
| Cache key update | 1 (+ 1 doc reference) |
| Legacy method removal (`QueryApprovedSendersAsync`) | 3 files |
| **Total distinct changes** | **~46 edits across 18 files** |

---

## 9. Build Verification

After all changes are applied:

```bash
# Step 1: Build the entire solution
dotnet build

# Step 2: Run all tests
dotnet test

# Step 3: Specifically run communication tests
dotnet test --filter "FullyQualifiedName~Communication"
```

### Expected Test Behavior
- All existing tests should pass with updated field names
- No new tests are needed (the logic is unchanged, only string literals change)
- `ApprovedSenderValidatorTests.cs` (sync/config-only tests) should pass without any modifications

### Risk Areas
1. **Dataverse field name mismatch**: If the actual Dataverse schema still uses the old field names, the corrected code will break at runtime. Confirm that the Dataverse entity `sprk_communicationaccount` has been updated with `sprk_sendenabled` (no trailing 's') before deploying.
2. **Web resource deployment**: Both `src/client/webresources/js/sprk_communication_send.js` and `infrastructure/dataverse/ribbon/CommunicationRibbons/WebResources/sprk_communication_send.js` must be deployed to Dataverse after changes.
3. **Redis cache invalidation**: After deployment, existing cached entries under old key `"communication:approved-senders"` will become stale orphans. They will expire naturally in 5 minutes. No manual invalidation needed.

---

## 10. Files NOT Requiring Changes

These files reference the old field names only in project documentation, notes, or closed task files and do NOT need updating:

- `projects/x-email-communication-solution-r1/` — Closed R1 project (archived)
- `projects/email-communication-solution-r2/CLAUDE.md` — Will be updated separately as part of project context maintenance
- `projects/email-communication-solution-r2/notes/research/r1-assessment-phase-a.md` — Research notes (historical)
- `projects/email-communication-solution-r2/tasks/001-*.poml` — Completed task (historical)
- `projects/email-communication-solution-r2/spec.md` — Design spec references (will be updated separately)
