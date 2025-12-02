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

## 📂 **Repository Structure**

The repository is organized as follows:

```
artifacts/          # Build artifacts (zips, packages)
config/             # Configuration files (local settings, props)
docs/               # Documentation (ADRs, guides, project management)
scripts/            # Build, deploy, and utility scripts
src/
  client/           # Client-side components
    canvas-apps/    # Canvas Apps source
    model-driven-apps/ # Model Driven Apps source
    pcf/            # PCF Controls (UniversalQuickCreate, etc.)
    power-pages/    # Power Pages sites
    webresources/   # Web Resources (JavaScript, HTML)
  server/           # Server-side components
    plugins/        # Dataverse Plugins (C#)
  solutions/        # Dataverse Solution projects
tests/              # Unit and Integration tests
tools/              # Development tools (PAC CLI, Postman)
```

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
dotnet run --project src/server/api/Spe.Bff.Api/
```

**API Available**: `https://localhost:5001`

## 🔧 **Development Environment**

### **Local Configuration**
Create these files in the `config/` directory:
```json
// config/azure-config.local.json
{
  "ClientId": "your-client-id",
  "TenantId": "your-tenant-id"
}

// config/sharepoint-config.local.json
{
  "SiteUrl": "your-sharepoint-site",
  "ClientId": "your-spe-client-id"
}
```

### **Health Checks**
- **Ping**: `GET /ping` - Service information
- **Health**: `GET /healthz` - Dependency health status

## 📚 **Documentation**

### **🎯 Essential Reading**
- **[Project Restart Guide](docs/project-management/sdap_project/Sprint%202/SDAP_Project_Restart_Guide.md)** - Complete development continuation guide
- **[Repository Structure](docs/Repository_Structure.md)** - Complete codebase organization

### **🏛️ Architecture Decisions (ADRs)**
- **[ADR-002](docs/adr/ADR-002-no-heavy-plugins.md)** - Thin plugin architecture (<50ms, <200 LoC)
- **[All ADRs](docs/adr/)** - Complete architectural decision record

## 🧪 **Testing**

```bash
# Run all tests
dotnet test --collect:"XPlat Code Coverage"

# Run specific test suite
dotnet test tests/unit/Spe.Bff.Api.Tests/
```

## 🤝 **Contributing**

1. **Review**: [Project Restart Guide](docs/project-management/sdap_project/Sprint%202/SDAP_Project_Restart_Guide.md)
2. **Follow**: Architecture Decision Records (ADRs)
3. **Test**: Comprehensive testing required
4. **Document**: Update relevant documentation

---

**Built with**: .NET 8 • Microsoft Graph • SharePoint Embedded • Power Platform • Azure
