# Spaarke Document Management Project Restart Guide

**Project**: Spaarke Document Management with SharePoint Embedded Integration
**Current State**: Ready for Task-Based Implementation Phase
**Next Phase**: Dataverse Entity Creation and Document CRUD Implementation
**Last Updated**: September 29, 2025

---

## 🚀 **Quick Start Summary**

### **Current Project Status**
- ✅ **Architecture**: Complete enterprise-grade foundation with async processing
- ✅ **Documentation**: Comprehensive task files, ADRs, and implementation guides
- ✅ **DataverseService**: Complete service layer implementation with managed identity auth
- ✅ **Task Planning**: Individual task files with detailed AI instructions
- ✅ **API Foundation**: BFF API structure ready for document endpoints
- ⚠️ **CRITICAL NEXT**: Task 1.1 (Dataverse Entity Creation) - **BLOCKING ALL OTHER WORK**

### **Repository State**
- **Branch**: `master` (current development state)
- **Solution File**: `Spaarke.sln`
- **Target Framework**: .NET 8 (API), .NET Framework 4.8 (Plugins)
- **Key Implementation**: Complete async processing architecture designed

---

## 📋 **Immediate Next Steps (Implementation Phase)**

### **🚨 CRITICAL PATH: Start with Task 1.1**

**The entire project is blocked on Dataverse entity creation. This must be completed first.**

#### **Task 1.1: Dataverse Entity Creation (4-6 hours)**
**Status**: ⚠️ PENDING - HIGH PRIORITY
**Location**: [Task-1.1-Dataverse-Entity-Creation.md](./Task-1.1-Dataverse-Entity-Creation.md)

**What to Create**:
```sql
-- sprk_document entity with fields:
sprk_name (String, 255, required)
sprk_containerid (Lookup to sprk_container, required)
sprk_documentdescription (String, 2000) -- ALREADY ADDED VIA CLI
sprk_hasfile (Boolean, default false)
sprk_filename (String, 255)
sprk_filesize (BigInt)
sprk_mimetype (String, 100)
sprk_graphitemid (String, 100)
sprk_graphdriveid (String, 100)
sprk_status (Choice: Draft=1, Active=2, Processing=3, Error=4)

-- sprk_container entity with field:
sprk_documentcount (Integer, default 0)
```

**Note**: We already have a Dataverse environment set up and successfully added the `sprk_documentdescription` field via Power Platform CLI. You can validate the environment or create the remaining entity structure.

#### **After Task 1.1: Implementation Order**
1. **Task 1.3**: Document CRUD API Endpoints (8-12 hours)
2. **Task 2.1**: Thin Plugin Implementation (6-8 hours)
3. **Task 2.2**: Background Service Implementation (10-12 hours)
4. **Task 3.1**: Model-Driven App Configuration (6-8 hours)
5. **Task 3.2**: JavaScript File Management Integration (10-14 hours)

---

## 📚 **Current Code Implementation Status**

### **✅ COMPLETED: DataverseService Layer**

**Location**: `src/shared/Spaarke.Dataverse/`

**Key Files**:
- `DataverseService.cs` - Complete CRUD implementation with managed identity auth
- `IDataverseService.cs` - Full interface definition
- `Models.cs` - Complete entity models matching planned Dataverse schema
- Test script: `test-dataverse-connection.cs` - Ready to validate entities

**Implementation Highlights**:
```csharp
// Complete service ready for use
public async Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
public async Task<DocumentEntity?> GetDocumentAsync(string id, CancellationToken ct = default)
public async Task UpdateDocumentAsync(string id, UpdateDocumentRequest request, CancellationToken ct = default)
public async Task DeleteDocumentAsync(string id, CancellationToken ct = default)
public async Task<IEnumerable<DocumentEntity>> GetDocumentsByContainerAsync(string containerId, CancellationToken ct = default)
```

### **✅ COMPLETED: Task-Based Implementation Guides**

**Location**: `docs/tasks/`

| Task File | Status | Purpose |
|-----------|--------|---------|
| **Task-1.1-Dataverse-Entity-Creation.md** | ⚠️ Ready to Execute | Entity creation with complete specifications |
| **Task-1.3-Document-CRUD-API-Endpoints.md** | 🔴 Ready After 1.1 | REST API implementation |
| **Task-2.1-Thin-Plugin-Implementation.md** | 🔴 Ready After 1.3 | Event capture plugin |
| **Task-2.2-Background-Service-Implementation.md** | 🔴 Ready After 2.1 | Async event processing |
| **Task-3.1-Model-Driven-App-Configuration.md** | 🔴 Ready After 1.1 | Power Platform UI |
| **Task-3.2-JavaScript-File-Management-Integration.md** | 🔴 Ready After 3.1+1.3 | File operations UI |
| **README.md** | ✅ Complete | Master task index and dependencies |

### **✅ COMPLETED: Configuration Framework**

**Key Configuration Files**:
- `docs/CONFIGURATION_REQUIREMENTS.md` - Environment setup requirements
- `docs/Power-Platform-CLI-Capabilities.md` - AI-directed CLI instructions
- `appsettings.json` / `dataverse-config.local.json` - Ready for connection strings

### **✅ COMPLETED: API Foundation**

**Location**: `src/api/Spe.Bff.Api/`

**Ready for Extension**:
- Authentication infrastructure in place
- Error handling patterns established
- Health check endpoints functional
- CORS configuration ready
- Dependency injection framework ready for DataverseService

---

## 🏗️ **Current Architecture Status**

### **System Architecture (Designed & Partially Implemented)**
```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│  Power Platform │    │    BFF API       │    │   Dataverse     │
│                 │───▶│                  │───▶│                 │
│ - Model App     │    │ ✅ Foundation    │    │ ⚠️ NEEDS ENTITIES│
│ - JavaScript    │    │ ⚠️ Need Doc APIs │    │ ✅ Service Ready │
│ - Forms/Views   │    │ ✅ Auth Ready    │    │ ⚠️ Need Tables  │
└─────────────────┘    └──────────────────┘    └─────────────────┘
                                │
                                ▼
                       ┌──────────────────┐    ┌─────────────────┐
                       │   Service Bus    │    │ Background Svc  │
                       │                  │───▶│                 │
                       │ ✅ Design Ready  │    │ ✅ Design Ready │
                       │ ⚠️ Need Config   │    │ ⚠️ Need Impl    │
                       │ ⚠️ Need Queues   │    │ ⚠️ Need Deploy  │
                       └──────────────────┘    └─────────────────┘
```

### **Data Flow (Fully Designed)**
1. **User Action** → Power Platform UI (Task 3.1, 3.2)
2. **UI Action** → JavaScript Web Resource (Task 3.2)
3. **JavaScript** → BFF API REST endpoints (Task 1.3)
4. **API** → DataverseService (✅ COMPLETED) + SharePoint Embedded
5. **Dataverse Event** → Thin Plugin (Task 2.1)
6. **Plugin** → Service Bus Event Queue (Task 2.1, 2.2)
7. **Service Bus** → Background Service (Task 2.2)
8. **Background Service** → Business Logic + External Integrations

---

## 🔧 **Development Environment Setup**

### **Prerequisites Verified Working**
- ✅ .NET 8 SDK
- ✅ Visual Studio/VS Code
- ✅ Power Platform environment (with sprk_documentdescription field added)
- ✅ Azure subscription access
- ✅ Git repository with latest code

### **Current Configuration Files**
```json
// dataverse-config.local.json (exists, ready for connection string)
{
  "Dataverse": {
    "ServiceUrl": "https://your-environment.crm.dynamics.com",
    "ClientId": "your-managed-identity-client-id"
  }
}

// azure-config.local.json (ready for Service Bus)
{
  "ServiceBus": {
    "ConnectionString": "your-service-bus-connection-string"
  }
}
```

### **Build & Run Status**
```bash
# Current status - API runs successfully
dotnet restore  # ✅ Works
dotnet build    # ✅ Works
dotnet run --project src/api/Spe.Bff.Api/  # ✅ Starts on https://localhost:7034

# Health checks available
curl https://localhost:7034/ping      # ✅ Returns pong
curl https://localhost:7034/healthz   # ✅ Returns healthy status
```

---

## 🎯 **Ready-to-Execute Implementation Plan**

### **🔥 START HERE: New AI Session Instructions**

When starting a new AI session for coding:

1. **Load Context**:
   - Read: `Task-1.1-Dataverse-Entity-Creation.md`
   - Read: `README.md` for overall context
   - Read: `src/shared/Spaarke.Dataverse/` to understand existing service layer

2. **Validate Environment**:
   - Run: `test-dataverse-connection.cs` to check if entities exist
   - If entities missing: Execute Task 1.1 (Dataverse Entity Creation)
   - If entities exist: Proceed to Task 1.3 (API Endpoints)

3. **Execute Tasks in Order**:
   - Task 1.1 → Task 1.3 → Task 2.1 → Task 2.2 → Task 3.1 → Task 3.2

### **🎖️ Success Criteria for Each Phase**

#### **Phase 1: Foundation (Tasks 1.1, 1.3)**
- [ ] Dataverse entities created with all required fields
- [ ] Document CRUD API endpoints functional
- [ ] DataverseService integration tested end-to-end
- [ ] API authentication and authorization working

#### **Phase 2: Async Processing (Tasks 2.1, 2.2)**
- [ ] Plugin captures document events and queues to Service Bus
- [ ] Background service processes events and executes business logic
- [ ] Complete async workflow functional
- [ ] Error handling and retry logic operational

#### **Phase 3: User Interface (Tasks 3.1, 3.2)**
- [ ] Model-driven app provides document management UI
- [ ] JavaScript enables file upload/download operations
- [ ] End-to-end user experience complete
- [ ] Security and permissions properly enforced

---

## 📊 **Implementation Metrics**

### **Code Completion Status**
- **DataverseService**: ✅ 100% Complete (ready for entities)
- **API Foundation**: ✅ 80% Complete (ready for document endpoints)
- **Plugin Framework**: ✅ 90% Complete (ready for document plugin)
- **Background Service Framework**: ✅ 85% Complete (ready for document handlers)
- **Power Platform Foundation**: ✅ 70% Complete (ready for document app)

### **Estimated Remaining Effort**
- **AI Coding Time**: 8-12 hours (pure implementation)
- **Configuration & Testing**: 15-20 hours (environment-specific)
- **Integration & Validation**: 12-16 hours (end-to-end testing)
- **Documentation & Deployment**: 8-12 hours (production readiness)

**Total Estimated**: 43-60 hours for complete production-ready system

### **Critical Path Dependencies**
```
Task 1.1 (Entity Creation) → 4-6 hours → UNBLOCKS ALL OTHER WORK
    ↓
Task 1.3 (API Endpoints) → 8-12 hours → ENABLES UI AND PROCESSING
    ↓
Task 2.1 (Plugin) → 6-8 hours → ENABLES ASYNC PROCESSING
    ↓
Task 2.2 (Background Service) → 10-12 hours → COMPLETES BACKEND
    ↓
Task 3.1 (Power Platform UI) → 6-8 hours → ENABLES USER INTERFACE
    ↓
Task 3.2 (JavaScript Integration) → 10-14 hours → COMPLETES SYSTEM
```

---

## 🔍 **Validation & Testing Strategy**

### **Pre-Implementation Validation**
```bash
# Verify current system state
cd C:\code_files\spaarke
dotnet build
dotnet run --project src/api/Spe.Bff.Api/

# Test DataverseService connection (once entities exist)
dotnet run --project test-dataverse-connection.cs
```

### **Per-Task Validation**
Each task file includes:
- **✅ Validation Steps**: Comprehensive testing procedures
- **🔍 Troubleshooting Guide**: Common issues and solutions
- **🎯 Success Criteria**: Clear completion definition
- **🔄 Next Step Instructions**: Handoff to subsequent tasks

### **End-to-End Validation**
```bash
# Complete system test (after all tasks)
# 1. Create document via Power Platform
# 2. Upload file via JavaScript UI
# 3. Verify async processing via background service
# 4. Download file via API
# 5. Delete document and verify cleanup
```

---

## 🚦 **Risk Mitigation & Known Considerations**

### **Environment-Specific Risks**
| Risk | Impact | Mitigation |
|------|--------|------------|
| **Dataverse Environment Access** | High | Validate permissions before Task 1.1 |
| **Service Bus Configuration** | Medium | Use existing Azure resources or create new |
| **Power Platform Licensing** | Medium | Verify developer/test environment access |
| **Authentication Setup** | Medium | Use managed identity patterns already implemented |

### **Technical Dependencies**
- **Dataverse Publisher Prefix**: Using `sprk_` (already confirmed)
- **API Authentication**: Managed identity framework ready
- **CORS Configuration**: Power Platform domain integration ready
- **File Storage**: SharePoint Embedded integration designed

---

## 📞 **Support Resources**

### **Implementation Guides**
- **Task Files**: Complete implementation instructions in `docs/tasks/`
- **Architecture**: ADRs and design documents in `docs/adr/`
- **Configuration**: Setup requirements in `docs/CONFIGURATION_REQUIREMENTS.md`
- **CLI Automation**: Power Platform CLI guide in `docs/Power-Platform-CLI-Capabilities.md`

### **Code References**
- **DataverseService**: `src/shared/Spaarke.Dataverse/` - Complete and ready
- **API Patterns**: `src/api/Spe.Bff.Api/Api/` - Existing endpoint patterns to follow
- **Plugin Patterns**: `power-platform/plugins/` - Existing plugin structure
- **Background Service Patterns**: `src/api/Spe.Bff.Api/Services/` - Framework ready

### **External Resources**
- **Power Platform**: https://docs.microsoft.com/en-us/power-platform/
- **Dataverse**: https://docs.microsoft.com/en-us/powerapps/developer/data-platform/
- **SharePoint Embedded**: https://docs.microsoft.com/en-us/sharepoint/dev/embedded/
- **Azure Service Bus**: https://docs.microsoft.com/en-us/azure/service-bus-messaging/

---

## 🔄 **Starting New AI Session Protocol**

### **Session Initialization Checklist**
1. [ ] Load task context from `docs/tasks/README.md`
2. [ ] Verify environment status with build/run commands
3. [ ] Check Dataverse entity status (Task 1.1 completion)
4. [ ] Review existing DataverseService implementation
5. [ ] Begin with appropriate task based on current state

### **Context Loading Priority**
1. **Task Files**: Start with Task 1.1 if entities not created
2. **Existing Code**: Review DataverseService and API structure
3. **Configuration**: Understand environment setup requirements
4. **Architecture**: Reference ADRs and design decisions

### **Development Workflow**
1. **Read** relevant task file completely
2. **Validate** prior task dependencies
3. **Implement** following task specifications
4. **Test** according to validation steps
5. **Document** completion and handoff to next task

---

**This guide provides everything needed to restart development efficiently. The foundation is solid, the architecture is complete, and the implementation path is clearly defined through detailed task files.**

**🚀 READY TO START: Begin with Task 1.1 (Dataverse Entity Creation) in new AI session.**