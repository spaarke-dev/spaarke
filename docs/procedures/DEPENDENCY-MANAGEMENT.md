# Dependency Management

> **Last Updated**: April 5, 2026
> **Applies To**: All developers modifying NuGet or npm dependencies

---

## When to Follow This Procedure

- Adding, updating, or removing any NuGet or npm package
- Updating shared library versions (`@spaarke/ui-components`, `@spaarke/auth`, `@spaarke/sdap-client`)
- Resolving vulnerability audit findings from nightly or weekly quality reports
- Updating Kiota packages (requires special coordination)

## .NET Dependency Management

### Central Package Management

All .NET package versions are centralized in `Directory.Packages.props` at the repository root. Individual `.csproj` files reference packages without version numbers.

**Key file**: `Directory.Packages.props`

| Package Group | Example Packages | Update Coordination |
|---------------|-----------------|---------------------|
| Azure SDK | `Azure.Core`, `Azure.Identity`, `Azure.Security.KeyVault.Secrets`, `Azure.Messaging.ServiceBus` | Update together; verify auth flows |
| Microsoft Graph + Kiota | `Microsoft.Graph`, `Microsoft.Kiota.*` (7 packages) | ALL Kiota packages must be the same version |
| Identity | `Microsoft.Identity.Client`, `Microsoft.Identity.Web`, `System.IdentityModel.Tokens.Jwt` | Update together; test OBO flow |
| OpenTelemetry | `OpenTelemetry`, `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.*` | Update together |
| Dataverse SDK | `Microsoft.CrmSdk.CoreAssemblies`, `Microsoft.CrmSdk.Workflow`, `Microsoft.PowerPlatform.Dataverse.Client` | Update together; test plugin build |
| Test | `xunit`, `FluentAssertions`, `Moq`, `WireMock.Net`, `coverlet.collector` | Can update independently |
| PCF Build | `Microsoft.PowerApps.MSBuild.Pcf`, `Microsoft.PowerApps.MSBuild.Solution` | Must match; affects PCF build pipeline |

### Global Build Properties

**Key file**: `Directory.Build.props`

```xml
<LangVersion>latest</LangVersion>
<Nullable>enable</Nullable>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<Deterministic>true</Deterministic>
```

These apply to all .NET projects. Changes here affect every build.

### Procedure: Updating a NuGet Package

1. Edit `Directory.Packages.props` -- change the version for the target package(s)
2. If updating Kiota packages: update ALL 7 `Microsoft.Kiota.*` entries to the same version
3. Run `dotnet restore --verbosity minimal`
4. Run `dotnet build -warnaserror` to verify no breaking changes
5. Run `dotnet test` to verify functionality
6. Run `dotnet list package --vulnerable --include-transitive` to check for new vulnerabilities
7. If updating Microsoft.Graph: run `dotnet list package --include-transitive | grep -i kiota` to verify all Kiota versions match

### Kiota Version Alignment (Critical)

The Microsoft.Graph SDK depends on Kiota packages transitively. If you update only direct references, transitive packages stay at older versions, causing `FileNotFoundException` at runtime.

**Required packages (must ALL match)**:
- `Microsoft.Kiota.Abstractions`
- `Microsoft.Kiota.Authentication.Azure`
- `Microsoft.Kiota.Http.HttpClientLibrary`
- `Microsoft.Kiota.Serialization.Form`
- `Microsoft.Kiota.Serialization.Json`
- `Microsoft.Kiota.Serialization.Multipart`
- `Microsoft.Kiota.Serialization.Text`

## npm Dependency Management

### Shared Libraries (Internal)

Three internal shared libraries are consumed by PCF controls, code pages, and solutions via `file:` references:

| Library | Path | Consumers | Peer Dependencies |
|---------|------|-----------|-------------------|
| `@spaarke/ui-components` | `src/client/shared/Spaarke.UI.Components` | Code pages, solutions, PCF (via workspace) | React >=16.14.0, Fluent UI v9 packages, Lexical |
| `@spaarke/auth` | `src/client/shared/Spaarke.Auth` | Code pages, external SPA | `@azure/msal-browser` ^3.0.0 |
| `@spaarke/sdap-client` | `src/client/shared/Spaarke.SdapClient` | PCF controls | None |

**Consumption pattern** (code pages and solutions):
```json
{
  "dependencies": {
    "@spaarke/auth": "file:../../shared/Spaarke.Auth",
    "@spaarke/ui-components": "file:../../shared/Spaarke.UI.Components"
  }
}
```

**Consumption pattern** (PCF controls):
```json
{
  "dependencies": {
    "@spaarke/ui-components": "workspace:*"
  }
}
```

### Module React Version Matrix

| Module Type | React Version | Bundling | Why |
|-------------|--------------|----------|-----|
| PCF controls | React 16/17 (platform-provided) | Not bundled | ADR-022: platform provides React |
| Code pages | React 18/19 (bundled) | Webpack bundles | Standalone HTML web resources |
| Solutions (LegalWorkspace, etc.) | React 18/19 (bundled) | Vite bundles | Standalone SPA web resources |
| Office Add-ins | React 18 (bundled) | Bundled | Standalone SPA |
| `@spaarke/ui-components` | React >=16.14.0 (peer) | Not bundled | Must be compatible with both PCF and code pages |

### Root package.json

The root `package.json` contains only dev tooling: Playwright (E2E tests), Husky (git hooks), lint-staged, and Prettier. It does not contain shared production dependencies.

### PCF Root package.json

`src/client/pcf/package.json` contains PCF build tooling: `pcf-scripts`, ESLint with `@microsoft/eslint-plugin-power-apps`, TypeScript. Individual PCF controls have their own `package.json` files inside their directories.

### Procedure: Updating an npm Package in a Shared Library

1. Update the version in the shared library's `package.json`
2. Run `npm install` in the shared library directory
3. Run `npm run build` to verify the library compiles
4. Run `npm test` if tests exist
5. For each consumer that uses `file:` references: run `npm install` to pick up changes
6. Build and test at least one consumer of each type (one PCF, one code page) to verify compatibility
7. If updating a peer dependency in `@spaarke/ui-components`: verify all consumers provide a compatible version

### Procedure: Updating Fluent UI v9

Fluent UI v9 uses selective imports (individual packages like `@fluentui/react-button`). The `@spaarke/ui-components` library declares them as peer dependencies.

1. Update peer dependency versions in `src/client/shared/Spaarke.UI.Components/package.json`
2. Update dev dependency version of `@fluentui/react-components` in the same file
3. Update `@fluentui/react-components` version in each code page and solution that consumes it
4. Do NOT update Fluent UI in PCF controls separately -- they get Fluent UI from the platform
5. Build `@spaarke/ui-components`, then build consumers

## Dependency Graph

```
Root (package.json)
  └── Prettier, Husky, lint-staged, Playwright

PCF Controls (src/client/pcf/*)
  ├── @spaarke/ui-components (workspace:*)
  ├── @spaarke/sdap-client (file: reference)
  ├── pcf-scripts (build tooling)
  └── Platform provides: React 16/17, Fluent UI v9

Code Pages (src/client/code-pages/*)
  ├── @spaarke/auth (file: reference)
  ├── @spaarke/ui-components (file: reference)
  ├── @fluentui/react-components (bundled)
  ├── react + react-dom (bundled, v18/19)
  └── webpack (build tooling)

Solutions (src/solutions/*)
  ├── @spaarke/auth (file: reference)
  ├── @spaarke/ui-components (file: reference)
  ├── @fluentui/react-components (bundled)
  ├── react + react-dom (bundled, v18/19)
  └── vite (build tooling)

.NET Projects
  └── All versions in Directory.Packages.props (central management)
```

## Checklists

### Before Updating Any Dependency

- [ ] Check if the package belongs to a group that must be updated together (see tables above)
- [ ] Review changelog/release notes for breaking changes
- [ ] Check if the package has known vulnerabilities (`dotnet list package --vulnerable` or `npm audit`)

### After Updating a .NET Package

- [ ] `dotnet restore` succeeds
- [ ] `dotnet build -warnaserror` succeeds
- [ ] `dotnet test` passes
- [ ] No new vulnerable transitive dependencies
- [ ] If Kiota-related: all 7 Kiota packages are the same version

### After Updating an npm Shared Library

- [ ] Shared library builds (`npm run build`)
- [ ] Shared library tests pass (`npm test`)
- [ ] At least one PCF consumer builds
- [ ] At least one code page consumer builds
- [ ] No peer dependency warnings during `npm install`

## Automation

| Step | Automated By | Manual? |
|------|-------------|---------|
| Vulnerability detection (.NET) | `sdap-ci.yml` code-quality job, `nightly-quality.yml` dependency-audit | No |
| Vulnerability detection (npm) | `nightly-quality.yml` dependency-audit | No |
| Build verification | `sdap-ci.yml` build-test matrix | No |
| Kiota version alignment check | Manual (see procedure above) | Yes |
| Shared library consumer testing | Manual (build representative consumers) | Yes |
| Peer dependency compatibility | npm install warnings | Partially |

## Related

- [code-review skill](../../.claude/skills/code-review/SKILL.md) -- Includes dependency review in quality checks
- [CI/CD Architecture](../architecture/ci-cd-architecture.md) -- Nightly dependency audit details
- [BFF API CLAUDE.md](../../src/server/api/Sprk.Bff.Api/CLAUDE.md) -- Kiota package management section
