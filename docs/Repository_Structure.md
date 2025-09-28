# Spaarke Repository Structure

This document provides a comprehensive overview of the Spaarke repository structure, including all directories, key files, and their purposes within the Spaarke Document Access Platform (SDAP).

## Repository Overview

The Spaarke repository follows a modern software development structure with clear separation between source code, documentation, infrastructure, testing, and development tools.

```
spaarke/
├── .claude/                           # Claude Code configuration
├── .github/                          # GitHub configuration and workflows
├── .vscode/                          # Visual Studio Code settings
├── dev/                              # Development workspace and tools
├── docs/                             # Documentation and specifications
├── infrastructure/                   # Infrastructure as Code and deployment
├── power-platform/                   # Power Platform components
├── src/                              # Source code
├── tests/                            # Test projects
├── tools/                            # Utility tools and scripts
├── .dockerignore                     # Docker ignore patterns
├── .gitattributes                    # Git attributes configuration
├── .gitignore                        # Git ignore patterns
├── Directory.Packages.props          # Centralized package management
├── README.md                         # Repository overview (if exists)
├── Spaarke.sln                       # Visual Studio solution file
└── Configuration Files               # Local configuration files
    ├── app-registrations.local.json
    ├── azure-config.local.json
    ├── dataverse-config.local.json
    ├── keyvault-config.local.json
    └── sharepoint-config.local.json
```

## Detailed Directory Structure

### `.claude/` - Claude Code Configuration
```
.claude/
└── settings.local.json               # Local Claude Code permissions and settings
```

**Purpose**: Configuration for Claude Code development environment, including permissions for automated development tasks.

### `.github/` - GitHub Configuration
```
.github/
├── workflows/                        # GitHub Actions CI/CD workflows
│   ├── build-only.yml               # Build and test validation
│   ├── deploy-to-azure.yml          # Azure deployment pipeline
│   ├── dotnet.yml                   # .NET CI pipeline
│   └── test.yml                     # Test workflow
└── CODEOWNERS                       # Code ownership definitions
```

**Purpose**: GitHub Actions workflows for continuous integration, deployment, and repository governance.

### `.vscode/` - Visual Studio Code Settings
```
.vscode/
└── .gitkeep                         # Placeholder for VS Code configuration
```

**Purpose**: Shared Visual Studio Code settings and extensions for consistent development experience.

### `dev/` - Development Workspace
```
dev/
├── ai-workspace/                    # AI development tools and context
│   ├── active-context/             # Current development context
│   ├── sessions/                   # Development session history
│   │   └── 2025-01/               # Monthly session organization
│   ├── patterns/                   # Code patterns and templates
│   │   ├── api/                   # API development patterns
│   │   ├── dataverse/             # Dataverse integration patterns
│   │   └── security/              # Security implementation patterns
│   └── prompts/                    # AI prompts for development
│       ├── implementation/         # Implementation-focused prompts
│       ├── review/                # Code review prompts
│       └── debugging/             # Debugging assistance prompts
├── design/                         # Design artifacts and mockups
│   ├── wireframes/                # UI/UX wireframes
│   ├── user-flows/                # User experience flows
│   ├── data-models/               # Data modeling diagrams
│   └── specifications/            # Technical specifications
├── research/                      # Research and analysis
│   ├── competitor-analysis/       # Market research
│   ├── technology-evaluations/    # Technology assessment
│   ├── poc-results/              # Proof of concept outcomes
│   └── external-docs/            # External documentation
├── standards/                     # Development standards and guidelines
├── onboarding/                    # New team member resources
└── scripts/                       # Development utility scripts
```

**Purpose**: Comprehensive development workspace with AI-assisted development tools, design artifacts, and team resources.

### `docs/` - Documentation
```
docs/
├── adr/                            # Architecture Decision Records
│   ├── ADR-001-minimal-api-and-workers.md
│   ├── ADR-002-no-heavy-plugins.md
│   ├── ADR-003-lean-authorization-seams.md
│   ├── ADR-004-async-job-contract.md
│   ├── ADR-005-flat-storage-spe.md
│   ├── ADR-006-prefer-pcf-over-webresources.md
│   ├── ADR-007-spe-storage-seam-minimalism.md
│   ├── ADR-008-authorization-endpoint-filters.md
│   ├── ADR-009-caching-redis-first.md
│   └── ADR-010-di-minimalism.md
├── guides/                         # Implementation guides
│   ├── SDAP_Architecture_Simplification_Guide.md
│   └── SDAP_Refactor_Playbook_v2.md
├── snippets/                       # Code examples and patterns
│   ├── Access_DataverseAccessDataSource.cs
│   ├── Auth_AuthorizationService.cs
│   ├── Cache_DistributedCacheExtensions.cs
│   ├── Cache_RequestCache.cs
│   ├── EndpointFilter_DocumentAuthorization.cs
│   ├── Middleware_SpaarkeContextMiddleware.cs
│   ├── Program_MinimalApi_PipelineAndDI.cs
│   ├── Storage_SpeFileStore.cs
│   ├── Workers_OcrWorker.cs
│   └── README_snippets.md
├── specs/                          # Specification documents
│   ├── Documents_Module.docx
│   ├── SDAP_V2.docx
│   ├── Spaarke_Technical_Architecture.docx
│   └── ~$DAP_V2.docx
├── _build/                         # Built documentation
│   ├── Documents_Module.docx
│   ├── SDAP_V2.docx
│   ├── Spaarke_Technical_Architecture.docx
│   └── terms-audit.csv
├── README-ADRs.md                  # ADR documentation overview
├── Repository_Structure.md         # This document
└── SDAP_Spe_Bff_Api_Code_Review.md # Comprehensive API code review
```

**Purpose**: Comprehensive project documentation including architectural decisions, implementation guides, and technical specifications.

### `infrastructure/` - Infrastructure as Code
```
infrastructure/
├── bicep/                          # Azure Bicep templates
│   └── modules/                   # Reusable Bicep modules
└── scripts/                       # Infrastructure deployment scripts
```

**Purpose**: Infrastructure as Code definitions for Azure resources and deployment automation.

### `power-platform/` - Power Platform Components
```
power-platform/
├── solutions/                      # Power Platform solutions
│   ├── spaarke_core/              # Core Spaarke solution
│   │   ├── Entities/              # Custom entities
│   │   ├── PluginAssemblies/      # Plugin assemblies
│   │   ├── WebResources/          # Web resources
│   │   ├── Workflows/             # Workflow definitions
│   │   ├── SecurityRoles/         # Security role definitions
│   │   ├── SiteMap/               # Site map configuration
│   │   └── Other/                 # Other solution components
│   └── spaarke_documents/         # Document management solution
│       ├── Entities/              # Document-related entities
│       │   ├── sprk_document/     # Document entity
│       │   ├── sprk_documentassociation/ # Document associations
│       │   ├── sprk_documentitem/ # Document items
│       │   └── sprk_aiprofile/    # AI profile entity
│       ├── PluginAssemblies/      # Document plugins
│       │   └── CustomAPIs/        # Custom API definitions
│       └── Other/                 # Other components
├── pcf/                           # Power Apps Component Framework
│   ├── DocumentViewer/            # Document viewing component
│   ├── DocumentGrid/              # Document grid component
│   ├── AIMetadataExtractor/       # AI metadata extraction
│   └── shared/                    # Shared PCF resources
│       ├── components/            # Reusable components
│       └── utils/                 # Utility functions
├── plugins/                       # Power Platform plugins
│   └── Spaarke.Plugins/          # Spaarke plugin assembly
│       └── CustomAPIs/           # Custom API implementations
├── model-driven-apps/             # Model-driven applications
│   ├── SpaarkeMain/              # Main Spaarke application
│   ├── SpaarkeDocuments/         # Document management app
│   └── SpaarkeAdmin/             # Administrative application
├── canvas-apps/                   # Canvas applications
│   └── SpaarkeIntake/            # Document intake application
└── power-pages/                   # Power Pages sites
    └── SpaarkePortal/            # External portal
        ├── web-templates/         # Page templates
        ├── page-templates/        # Content templates
        └── web-files/            # Static web files
```

**Purpose**: Complete Power Platform solution including entities, plugins, PCF components, and applications for the document management platform.

### `src/` - Source Code
```
src/
├── api/                           # API projects
│   ├── Spe.Bff.Api/             # SharePoint Embedded BFF API
│   │   ├── Api/                  # API endpoint definitions
│   │   │   ├── OBOEndpoints.cs   # On-behalf-of endpoints
│   │   │   ├── SecurityHeadersMiddleware.cs # Security middleware
│   │   │   └── UserEndpoints.cs  # User identity endpoints
│   │   ├── Infrastructure/       # Infrastructure concerns
│   │   │   ├── Graph/           # Microsoft Graph integration
│   │   │   │   ├── GraphClientFactory.cs # Graph client factory
│   │   │   │   ├── IGraphClientFactory.cs # Factory interface
│   │   │   │   ├── ISpeService.cs # SPE service interface
│   │   │   │   ├── SimpleTokenCredential.cs # Token management
│   │   │   │   └── SpeService.cs # SPE service implementation
│   │   │   ├── Dataverse/       # Dataverse integration
│   │   │   │   ├── Repositories/ # Data access repositories
│   │   │   │   └── Security/    # Dataverse security
│   │   │   ├── Resilience/      # Resilience patterns
│   │   │   │   └── RetryPolicies.cs # Polly retry policies
│   │   │   ├── Validation/      # Input validation
│   │   │   │   └── PathValidator.cs # File path validation
│   │   │   └── Errors/          # Error handling
│   │   │       └── ProblemDetailsHelper.cs # RFC 7807 errors
│   │   ├── Services/            # Business logic services
│   │   │   ├── BackgroundServices/ # Background processing
│   │   │   ├── IOboSpeService.cs # OBO service interface
│   │   │   └── OboSpeService.cs # OBO service implementation
│   │   ├── Models/              # Data transfer objects
│   │   │   ├── Entities/        # Entity models
│   │   │   ├── DTOs/           # Data transfer objects
│   │   │   ├── Enums/          # Enumeration types
│   │   │   ├── ContainerModels.cs # Container DTOs
│   │   │   ├── FileOperationModels.cs # File operation DTOs
│   │   │   ├── ListingModels.cs # File listing DTOs
│   │   │   ├── UploadModels.cs  # Upload operation DTOs
│   │   │   └── UserModels.cs    # User identity DTOs
│   │   ├── Middleware/          # Custom middleware
│   │   ├── Properties/          # Project properties
│   │   │   └── launchSettings.json # Development settings
│   │   ├── Program.cs           # Application entry point
│   │   ├── Spe.Bff.Api.csproj  # Project file
│   │   ├── Spe.Bff.Api.http     # HTTP test requests
│   │   └── appsettings.json     # Application configuration
│   └── Spaarke.Integration.Api/ # Integration API (placeholder)
├── shared/                       # Shared libraries
│   ├── Spaarke.Core/            # Core shared functionality
│   │   ├── Entities/            # Core entity definitions
│   │   ├── Interfaces/          # Shared interfaces
│   │   └── Constants/           # Application constants
│   └── Spaarke.Dataverse/       # Dataverse shared components
│       ├── Services/            # Dataverse services
│       └── Extensions/          # Extension methods
├── office-addins/               # Office Add-ins
│   ├── outlook-addin/           # Outlook add-in
│   │   └── src/                # Outlook add-in source
│   └── word-addin/             # Word add-in
└── agents/                      # AI agents and automation
    ├── copilot-studio/         # Microsoft Copilot Studio agents
    └── semantic-kernel/        # Semantic Kernel implementations
```

**Purpose**: All source code including APIs, shared libraries, Office add-ins, and AI agents for the platform.

### `tests/` - Test Projects
```
tests/
├── unit/                          # Unit tests
│   ├── Spe.Bff.Api.Tests/        # BFF API unit tests
│   │   ├── Mocks/                # Mock implementations
│   │   │   ├── FakeGraphClientFactory.cs # Graph client mocks
│   │   │   └── MockOboSpeService.cs # OBO service mocks
│   │   ├── CorsAndAuthTests.cs   # CORS and authentication tests
│   │   ├── CustomWebAppFactory.cs # Test web application factory
│   │   ├── FileOperationsTests.cs # File operation tests
│   │   ├── HealthAndHeadersTests.cs # Health and header tests
│   │   ├── ListingEndpointsTests.cs # Listing endpoint tests
│   │   ├── ProblemDetailsHelperTests.cs # Error handling tests
│   │   ├── Spe.Bff.Api.Tests.csproj # Test project file
│   │   ├── UploadEndpointsTests.cs # Upload endpoint tests
│   │   └── UserEndpointsTests.cs # User endpoint tests
│   └── Spaarke.Core.Tests/       # Core library tests
├── integration/                   # Integration tests
│   └── API.Integration.Tests/    # API integration tests
└── e2e/                          # End-to-end tests
    └── cypress/                  # Cypress E2E tests
```

**Purpose**: Comprehensive testing strategy with unit, integration, and end-to-end tests.

### `tools/` - Utility Tools
```
tools/
├── docs/                         # Documentation tools
│   ├── code_block_mappings.json # Code block replacement mappings
│   ├── doc_term_replacements.json # Document term replacements
│   ├── docx-audit-terms.ps1     # Document term auditing
│   ├── docx-auto-replace-code.ps1 # Automated code replacement
│   ├── docx-batch-find-replace.ps1 # Batch find/replace tool
│   └── docx-batch-find-replace.ps1.bak # Backup of batch tool
├── pac-cli/                      # Power Platform CLI tools
└── postman/                      # API testing collections
```

**Purpose**: Development and documentation tools for automation and testing.

## Configuration Files

### Local Configuration Files
```
├── app-registrations.local.json   # Azure app registration settings
├── azure-config.local.json        # Azure subscription configuration
├── dataverse-config.local.json    # Dataverse connection settings
├── keyvault-config.local.json     # Azure Key Vault configuration
└── sharepoint-config.local.json   # SharePoint Embedded settings
```

**Purpose**: Local environment configuration files for development and testing (excluded from git).

### Project Configuration Files
```
├── Directory.Packages.props       # Centralized NuGet package management
├── Spaarke.sln                   # Visual Studio solution file
├── .dockerignore                 # Docker build exclusions
├── .gitattributes                # Git line ending configuration
└── .gitignore                    # Git exclusion patterns
```

**Purpose**: Project-wide configuration for builds, dependencies, and version control.

## Key File Purposes

### Core Application Files

| File | Purpose | Location |
|------|---------|----------|
| `Program.cs` | Main API entry point and configuration | `src/api/Spe.Bff.Api/` |
| `Spaarke.sln` | Visual Studio solution file | Root |
| `Directory.Packages.props` | Centralized package management | Root |
| `.gitignore` | Git exclusion patterns | Root |

### Configuration Files

| File | Purpose | Environment |
|------|---------|-------------|
| `appsettings.json` | Application configuration | All |
| `azure-config.local.json` | Azure settings | Local Dev |
| `app-registrations.local.json` | App registration details | Local Dev |
| `keyvault-config.local.json` | Key Vault configuration | Local Dev |

### CI/CD Files

| File | Purpose | Platform |
|------|---------|----------|
| `build-only.yml` | Build and test workflow | GitHub Actions |
| `deploy-to-azure.yml` | Azure deployment pipeline | GitHub Actions |
| `dotnet.yml` | .NET CI workflow | GitHub Actions |

### Documentation Files

| File | Purpose | Audience |
|------|---------|----------|
| `ADR-*.md` | Architectural decisions | Development Team |
| `SDAP_Spe_Bff_Api_Code_Review.md` | API code review | Architects |
| `Repository_Structure.md` | This document | All Team Members |

## Repository Statistics

- **Total Directories**: 80+
- **Configuration Files**: 10+
- **Documentation Files**: 25+
- **Source Code Projects**: 6+
- **Test Projects**: 3+
- **CI/CD Workflows**: 4
- **ADR Documents**: 10

## Development Workflow

The repository structure supports the following development workflows:

1. **Feature Development**: Use `dev/` workspace for design and planning
2. **Code Implementation**: Implement in appropriate `src/` directories
3. **Testing**: Add tests in corresponding `tests/` directories
4. **Documentation**: Update relevant documentation in `docs/`
5. **CI/CD**: Automated through `.github/workflows/`
6. **Deployment**: Infrastructure managed through `infrastructure/`

## Conclusion

The Spaarke repository structure provides a comprehensive, well-organized foundation for enterprise-scale development. It supports:

- **Clean Architecture**: Clear separation between source, tests, documentation, and infrastructure
- **Modern Development Practices**: CI/CD, automated testing, and Infrastructure as Code
- **Team Collaboration**: Shared development standards and comprehensive documentation
- **Multi-Platform Support**: APIs, Power Platform, Office Add-ins, and AI agents
- **Enterprise Requirements**: Security, monitoring, compliance, and scalability considerations

This structure enables efficient development, maintenance, and scaling of the Spaarke Document Access Platform.