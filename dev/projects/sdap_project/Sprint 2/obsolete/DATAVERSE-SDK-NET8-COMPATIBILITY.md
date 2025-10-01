# Dataverse SDK .NET 8.0 Compatibility Issue

## Status
**Known Issue** - Tracked for Sprint 3 resolution

## Problem
`Microsoft.PowerPlatform.Dataverse.Client` version 1.1.32 has a runtime compatibility issue with .NET 8.0 applications. The SDK attempts to load `System.ServiceModel, Version=4.0.0.0` which is a .NET Framework assembly not available in .NET 8.0.

### Error Message
```
System.IO.FileNotFoundException: Could not load file or assembly 'System.ServiceModel, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089'. The system cannot find the file specified.
```

### Affected Components
- [Spaarke.Dataverse](../../src/shared/Spaarke.Dataverse/DataverseService.cs:41) (DataverseService constructor)
- DocumentEventHandler (Task 2.2 background service operations)
- Any code path requiring Dataverse connectivity via ServiceClient

## Impact
- **Task 2.2 Background Service**: Async processing pipeline works end-to-end (Service Bus → Processor → Handler) but Dataverse write-back operations are blocked
- **API Endpoints**: CRUD operations via DataverseDocumentsEndpoints may experience connection issues
- **Local Development**: All environments using .NET 8.0 runtime

## Root Cause
The Power Platform Dataverse Client SDK uses WCF (Windows Communication Foundation) internally, which depends on .NET Framework 4.x assemblies. While `System.ServiceModel.Primitives` and `System.ServiceModel.Http` packages (version 8.0.0) are installed, the SDK specifically requires the legacy .NET Framework 4.0 version.

##  Workarounds

### Option 1: Use Dataverse Web API (Recommended for Sprint 3)
Bypass ServiceClient and use REST API directly via HttpClient.

**Implementation**: [DataverseWebApiClient.cs](../../src/shared/Spaarke.Dataverse/DataverseWebApiClient.cs)

**Advantages**:
- No .NET Framework dependencies
- Faster and more lightweight
- Full control over HTTP requests
- Better async/await support

**Migration Path**:
1. Create IDataverseWebApiService interface matching IDataverseService
2. Implement CRUD operations using HttpClient + Azure.Identity for auth
3. Update DI registration to use Web API implementation
4. Test all endpoints and background services
5. Remove ServiceClient dependency

### Option 2: Wait for SDK Update
Microsoft may release an updated Dataverse Client SDK with full .NET 8.0 support.

**Tracking**: Check [NuGet - Microsoft.PowerPlatform.Dataverse.Client](https://www.nuget.org/packages/Microsoft.PowerPlatform.Dataverse.Client) for updates

### Option 3: Separate .NET Framework Service (Not Recommended)
Create a separate microservice targeting .NET Framework 4.8 to handle Dataverse operations.

**Disadvantages**:
- Adds architectural complexity
- Requires additional deployment/hosting
- Increases latency for Dataverse operations

## Current Mitigation
Task 2.2 DocumentEventHandler includes TODO comments marking Dataverse operations as temporarily disabled:
- `UpdateDocumentStatusAsync`: Logs intent but skips actual update
- `UpdateDocumentFileMetadataAsync`: Logs intent but skips actual update

## Recommendation
**Implement Option 1 (Dataverse Web API) in Sprint 3**

This provides a permanent, maintainable solution without waiting for Microsoft SDK updates and aligns with modern REST API practices.

## Testing Proof
End-to-end async pipeline verified working:
1. ✅ Test message sent to Service Bus queue (`document-events`)
2. ✅ DocumentEventProcessor received message
3. ✅ Message deserialized correctly as DocumentEvent
4. ✅ DocumentEventHandler.HandleEventAsync invoked
5. ✅ Retry logic triggered on failure (3 attempts)
6. ✅ Message moved to dead-letter queue after max retries
7. ❌ Dataverse write-back blocked by SDK compatibility

## References
- Task 2.2: [Background Service Implementation](../../dev/projects/sdap_project/Sprint 2/Task-2.2-Background-Service-Implementation.md)
- Related Files:
  - [DocumentEventProcessor.cs](../../src/api/Spe.Bff.Api/Services/Jobs/DocumentEventProcessor.cs)
  - [DocumentEventHandler.cs](../../src/api/Spe.Bff.Api/Services/Jobs/Handlers/DocumentEventHandler.cs)
  - [DataverseService.cs](../../src/shared/Spaarke.Dataverse/DataverseService.cs)
  - [DataverseWebApiClient.cs](../../src/shared/Spaarke.Dataverse/DataverseWebApiClient.cs) (implementation ready)

## Date Identified
2025-09-30

## Target Resolution
Sprint 3 - Task 3.x (Dataverse Web API Migration)