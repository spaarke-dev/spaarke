# Sprint 8: Custom API Proxy for PCF Controls

## Sprint Overview

**Sprint Goal**: Build a reusable Custom API Proxy infrastructure that enables PCF controls to securely call external APIs (starting with Spe.Bff.Api) using Dataverse's implicit authentication model.

**Sprint Type**: Infrastructure / Architecture

**Priority**: Critical

**Status**: Not Started

---

## Background

### Problem Statement

During Sprint 7A deployment testing, we discovered that PCF controls in model-driven apps cannot directly access user authentication tokens to call external APIs like Spe.Bff.Api. This is by design in Power Apps security architecture - PCF controls run in a sandboxed client-side environment without access to Azure AD tokens.

### Current Architecture Gap

```
┌─────────────────────────────────────────────────────────────┐
│ Power Apps Model-Driven App                                 │
│                                                               │
│  ┌──────────────────────────────────────┐                   │
│  │ Universal Dataset Grid (PCF Control) │                   │
│  │                                       │                   │
│  │  ❌ Cannot get user token            │                   │
│  │  ❌ Cannot call Spe.Bff.Api directly │                   │
│  └──────────────────────────────────────┘                   │
│                                                               │
└─────────────────────────────────────────────────────────────┘
           │
           │ Blocked - No Authentication
           ▼
    ┌─────────────────┐
    │  Spe.Bff.Api    │  (Requires JWT Bearer token)
    │  Azure App      │
    └─────────────────┘
```

### Why This Sprint is Critical

1. **Prerequisite for SDAP Service**: The Custom API Proxy is the foundation for the Dataverse-to-SharePointEmbedded (SDAP) service architecture
2. **Reusability**: This infrastructure will enable PCF controls to call ANY external API, not just Spe.Bff.Api
3. **Production Security**: Proper authentication using Dataverse's implicit model meets enterprise security requirements
4. **Unblocks Sprint 7A**: File operations (Download, Delete, Replace, Upload) are blocked without this proxy

---

## Sprint Objectives

### Primary Objectives

1. ✅ **Design Custom API Proxy Architecture**
   - Define component boundaries and interfaces
   - Ensure separation from Spe.Bff.Api (reusable design)
   - Document authentication flow
   - Create deployment strategy

2. 🔲 **Implement Dataverse Custom API Infrastructure**
   - Create base Custom API plugin framework
   - Implement secure configuration management
   - Add logging and error handling
   - Build extensibility points for future external services

3. 🔲 **Implement File Operations Proxy**
   - Download File proxy
   - Delete File proxy
   - Replace/Update File proxy
   - Upload File proxy

4. 🔲 **Update PCF Control Integration**
   - Replace direct Spe.Bff.Api calls with context.webAPI calls
   - Update SdapApiClientFactory to use Custom API Proxy
   - Add error handling and retry logic
   - Update type definitions

5. 🔲 **Testing & Documentation**
   - Unit tests for Custom API plugins
   - Integration tests with PCF control
   - End-to-end testing in Dataverse environment
   - Deployment documentation
   - Architecture decision records

### Secondary Objectives

6. 🔲 **Performance Optimization**
   - Implement response caching where appropriate
   - Add request batching for multiple operations
   - Monitor and optimize latency

7. 🔲 **Observability**
   - Add Application Insights integration
   - Implement detailed tracing
   - Create monitoring dashboard

---

## Architecture Principles

### 1. Separation of Concerns
- Custom API Proxy is a **separate infrastructure component**
- NOT tightly coupled to Spe.Bff.Api
- Can proxy to ANY external API with proper configuration

### 2. Reusability
- Generic proxy framework
- External service configurations stored in Dataverse
- Easy to add new external services without code changes

### 3. Security First
- Leverage Dataverse implicit authentication
- Service-to-service authentication for external APIs
- Secrets managed in Azure Key Vault
- Audit logging for all proxy operations

### 4. Production Ready
- Comprehensive error handling
- Retry policies with exponential backoff
- Rate limiting and throttling
- Monitoring and alerting

---

## Sprint Tasks

### Phase 1: Architecture & Design (This Phase)
- [x] Create Sprint 8 folder structure
- [x] Document architecture overview
- [ ] Create detailed implementation plan
- [ ] Review architecture with stakeholders

### Phase 2: Dataverse Custom API Foundation
- [ ] Create Dataverse solution for Custom API Proxy
- [ ] Implement base plugin class with common functionality
- [ ] Create configuration entity for external services
- [ ] Add logging and telemetry infrastructure
- [ ] Write unit tests for base functionality

### Phase 3: Spe.Bff.Api Proxy Implementation
- [ ] Implement Download File proxy
- [ ] Implement Delete File proxy
- [ ] Implement Replace/Update File proxy
- [ ] Implement Upload File proxy
- [ ] Add integration tests

### Phase 4: PCF Control Integration
- [ ] Update SdapApiClientFactory to use context.webAPI
- [ ] Replace all Spe.Bff.Api direct calls
- [ ] Update error handling
- [ ] Add retry logic
- [ ] Update TypeScript type definitions

### Phase 5: Deployment & Testing
- [ ] Deploy Custom API solution to spaarkedev1
- [ ] Configure external service connections
- [ ] End-to-end testing with Universal Dataset Grid
- [ ] Performance testing
- [ ] Security review

### Phase 6: Documentation & Handoff
- [ ] Complete architecture documentation
- [ ] Create deployment runbook
- [ ] Document how to add new external services
- [ ] Create troubleshooting guide
- [ ] Update Sprint 7A completion status

---

## Success Criteria

### Must Have
- ✅ Custom API Proxy architecture designed and documented
- 🔲 All 4 file operations working through Custom API Proxy
- 🔲 PCF control successfully calls Spe.Bff.Api via Custom API Proxy
- 🔲 End-to-end file operations tested in spaarkedev1
- 🔲 Architecture supports adding new external services

### Should Have
- 🔲 Comprehensive error handling and retry logic
- 🔲 Application Insights integration
- 🔲 Unit and integration test coverage > 80%
- 🔲 Deployment automation

### Nice to Have
- 🔲 Response caching
- 🔲 Request batching
- 🔲 Monitoring dashboard
- 🔲 Performance benchmarks

---

## Dependencies

### Upstream Dependencies
- Sprint 7A: Universal Dataset Grid PCF control deployed and tested
- Spe.Bff.Api: File operations endpoints functional
- Dataverse environment: spaarkedev1.crm.dynamics.com available

### Downstream Dependencies
- Future sprints can leverage Custom API Proxy for other external services
- SDAP Dataverse-to-SharePointEmbedded service will build on this infrastructure

---

## Risks & Mitigations

### Risk 1: Custom API Performance
**Risk**: Custom API adds latency compared to direct API calls
**Mitigation**:
- Implement response caching
- Use async patterns
- Monitor and optimize hot paths

### Risk 2: Dataverse API Limits
**Risk**: Custom API calls count against Dataverse API limits
**Mitigation**:
- Implement request batching
- Add client-side caching
- Monitor API usage

### Risk 3: Configuration Complexity
**Risk**: Managing external service configurations in Dataverse could become complex
**Mitigation**:
- Design simple, clear configuration model
- Provide configuration UI tools
- Document configuration thoroughly

### Risk 4: Debugging Challenges
**Risk**: Additional layer makes debugging more complex
**Mitigation**:
- Comprehensive logging at all layers
- Correlation IDs for request tracing
- Clear error messages

---

## Timeline

**Estimated Duration**: 5-7 days

- **Phase 1**: 0.5 days (Architecture & Design) - IN PROGRESS
- **Phase 2**: 1.5 days (Dataverse Custom API Foundation)
- **Phase 3**: 2 days (Spe.Bff.Api Proxy Implementation)
- **Phase 4**: 1 day (PCF Control Integration)
- **Phase 5**: 1 day (Deployment & Testing)
- **Phase 6**: 0.5 days (Documentation & Handoff)

---

## Related Documentation

- [Sprint 7A Authentication Architecture Issue](../Sprint%207/AUTHENTICATION-ARCHITECTURE-ISSUE.md)
- [Custom API vs MSAL Analysis](../Sprint%207/CUSTOM-API-VS-MSAL-ANALYSIS.md)
- [Dataverse Authentication Guide](../../../docs/DATAVERSE-AUTHENTICATION-GUIDE.md)
- [PCF Control Standards](../../../docs/KM-PCF-CONTROL-STANDARDS.md)

---

## Notes

This sprint is a **critical prerequisite** for the Dataverse-to-SharePointEmbedded service. The Custom API Proxy infrastructure must be:
- **Separate and reusable** - not tightly coupled to any single external service
- **Production-ready** - comprehensive error handling, monitoring, security
- **Well-documented** - clear guidance for adding new external services

The architecture decisions made in this sprint will impact all future PCF-to-external-API integrations in the SDAP project.
