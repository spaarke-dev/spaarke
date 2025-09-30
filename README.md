# Spaarke - SharePoint Document Access Platform (SDAP)

A modern, enterprise-grade SharePoint Embedded integration platform built with .NET 8, Power Platform, and Microsoft Graph.

## 🏗️ **Architecture Overview**

SDAP provides secure, scalable document management through SharePoint Embedded with comprehensive Power Platform integration, featuring clean architecture, thin plugins, and robust authorization patterns.

### **Core Components**
- **🔌 SharePoint Embedded API**: .NET 8 Minimal API with Graph SDK v5
- **⚡ Power Platform Plugins**: Lightweight validation and projection plugins (<50ms)
- **🔐 Authorization Framework**: Rule-based access control with secure defaults
- **📊 Monitoring & Observability**: Structured logging with correlation tracking
- **🧪 Comprehensive Testing**: Unit, integration, and end-to-end test coverage

## 🚀 **Quick Start**

### **Prerequisites**
- .NET 8 SDK
- .NET Framework 4.8 Developer Pack
- Visual Studio 2022 or VS Code
- Azure CLI

### **Get Started**
```bash
# Clone repository
git clone https://github.com/spaarke-dev/spaarke.git
cd spaarke

# Restore packages
dotnet restore

# Build solution
dotnet build

# Run API
dotnet run --project src/api/Spe.Bff.Api/
```

**API Available**: `https://localhost:5001`

## 📋 **Current Status**

| Component | Status | Notes |
|-----------|--------|-------|
| **Architecture** | ✅ Complete | Clean layered architecture with ADR compliance |
| **API Endpoints** | ⚠️ DI Issues | Endpoint structure complete, dependency injection needs fixes |
| **Power Platform** | ✅ Complete | Plugins follow ADR-002 thin architecture |
| **CI/CD Pipeline** | ✅ Complete | GitHub Actions with security scanning |
| **Documentation** | ✅ Complete | Comprehensive ADRs and guides |
| **Testing** | ⚠️ Failing | 68/97 tests failing due to DI configuration |

**Next Phase**: [Dependency Injection Fixes & Graph SDK Migration](docs/_build/sdap_project/SDAP_Project_Restart_Guide.md)

## 📚 **Documentation**

### **🎯 Essential Reading**
- **[Project Restart Guide](docs/_build/sdap_project/SDAP_Project_Restart_Guide.md)** - Complete development continuation guide
- **[Code Review Assessment](docs/_build/sdap_project/SDAP_Code_Review_Assessment.md)** - Senior developer analysis and action plan
- **[Repository Structure](docs/Repository_Structure.md)** - Complete codebase organization

### **🏛️ Architecture Decisions (ADRs)**
- **[ADR-002](docs/adr/ADR-002-no-heavy-plugins.md)** - Thin plugin architecture (<50ms, <200 LoC)
- **[ADR-008](docs/adr/ADR-008-authorization-endpoint-filters.md)** - Authorization endpoint filters
- **[All ADRs](docs/adr/)** - Complete architectural decision record

### **📖 Implementation Guides**
- **[Monitoring Strategy](docs/operations/monitoring.md)** - Observability and alerting
- **[Architecture Simplification](docs/guides/SDAP_Architecture_Simplification_Guide.md)** - Design principles
- **[Development Tasks](docs/_build/sdap_project/docs/dev/tasks/)** - Original implementation tasks

## 🧪 **Testing**

```bash
# Run all tests
dotnet test --collect:"XPlat Code Coverage"

# Run specific test suite
dotnet test tests/unit/Spe.Bff.Api.Tests/

# Check coverage
# Results: TestResults/coverage.cobertura.xml
```

**Current Coverage**: 29.9% (improving after DI fixes)

## 🔧 **Development Environment**

### **Local Configuration**
Create these files in repository root:
```json
// azure-config.local.json
{
  "ClientId": "your-client-id",
  "TenantId": "your-tenant-id"
}

// sharepoint-config.local.json
{
  "SiteUrl": "your-sharepoint-site",
  "ClientId": "your-spe-client-id"
}
```

### **Health Checks**
- **Ping**: `GET /ping` - Service information
- **Health**: `GET /healthz` - Dependency health status

## 🎯 **Performance Targets**

| Component | Target | Status |
|-----------|--------|--------|
| **API Health Checks** | < 100ms p95 | ⚠️ Pending DI fixes |
| **Plugin Execution** | < 50ms p95 | ✅ ADR-002 compliant |
| **Document Operations** | < 2s p95 | ⚠️ Graph SDK migration needed |
| **System Availability** | 99.9% uptime | 🎯 Target defined |

## 🔐 **Security**

- **🛡️ Authorization**: Rule-based access control with secure defaults
- **🔒 Authentication**: Bearer token validation with OBO support
- **📋 Security Headers**: Comprehensive security middleware
- **🔍 Security Scanning**: Automated vulnerability scanning in CI/CD

## 🚦 **CI/CD Pipeline**

**GitHub Actions Workflows**:
- **Build & Test**: Automated testing with coverage reporting
- **Security Scan**: Trivy vulnerability scanning
- **Azure Deployment**: Infrastructure as Code with Bicep
- **Quality Gates**: Code coverage and security thresholds

## 🏢 **Enterprise Features**

- **📊 Monitoring**: Application Insights integration with custom metrics
- **🗃️ Caching**: Redis-first caching strategy (ADR-009)
- **⚡ Background Jobs**: Async job processing with reliable patterns
- **📱 Power Platform**: Canvas apps, model-driven apps, and PCF components
- **🔌 Office Add-ins**: Word and Outlook integration

## 🤝 **Contributing**

1. **Review**: [Project Restart Guide](docs/_build/sdap_project/SDAP_Project_Restart_Guide.md)
2. **Follow**: Architecture Decision Records (ADRs)
3. **Test**: Comprehensive testing required
4. **Document**: Update relevant documentation

## 📈 **Roadmap**

### **Phase 1: Critical Fixes** (Current)
- ⚠️ Fix dependency injection configuration
- ⚠️ Complete Graph SDK v5 migration
- ⚠️ Implement authentication configuration

### **Phase 2: Production Readiness**
- 🎯 Implement Redis caching
- 🎯 Complete performance optimization
- 🎯 Security hardening

### **Phase 3: Enhancement**
- 🎯 Advanced monitoring dashboards
- 🎯 >80% test coverage
- 🎯 Performance benchmarking

## 📞 **Support**

- **Issues**: [GitHub Issues](https://github.com/spaarke-dev/spaarke/issues)
- **Documentation**: `docs/` directory
- **Architecture**: See ADRs for design decisions
- **Development**: [Restart Guide](docs/_build/sdap_project/SDAP_Project_Restart_Guide.md)

---

**Built with**: .NET 8 • Microsoft Graph • SharePoint Embedded • Power Platform • Azure

**License**: [Your License Here]