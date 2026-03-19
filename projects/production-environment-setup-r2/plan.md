# Project Plan: Production Environment Setup R2

> **Last Updated**: 2026-03-18
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Make all Spaarke components environment-agnostic by removing hardcoded dev URLs/IDs and replacing with runtime configuration resolution. Build once, deploy anywhere.

**Scope**:
- Fix BFF API hardcoded values (3 files, 4 locations)
- Create runtime config resolution in @spaarke/auth shared library
- Migrate 9 code pages from build-time to runtime config
- Migrate 7+ PCF controls to shared auth patterns
- Parameterize 7 legacy JS webresources
- Parameterize Office add-in auth config
- Parameterize 30+ scripts
- Add Dataverse Environment Variable definitions to solution XML
- Create deployment validation script

**Timeline**: ~2 weeks | **Estimated Effort**: ~60 hours

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001**: Use Minimal API + Options pattern for env config injection
- **ADR-006**: PCF (field-bound, React 16) vs Code Page (standalone, React 18) — different config strategies
- **ADR-008**: Endpoint filters for authorization — parameterized endpoints
- **ADR-010**: DI minimalism — Options pattern with ValidateOnStart()
- **ADR-012**: Shared components — no hardcoded URLs
- **ADR-022**: PCF platform libraries — React 16 APIs, use environmentVariables.ts for config

**From Spec**:
- 5 canonical values define every environment: TenantId, BffApiUrl, BffApiAppId, MsalClientId, DataverseUrl
- Runtime resolution via Dataverse Environment Variables (not build-time)
- Fail loudly — no silent fallback to dev values
- .env.development preserved for local dev workflow

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Runtime config via Dataverse Environment Variables | Code pages can't use PCF webApi SDK; REST API available to all authenticated components | All code pages need resolveRuntimeConfig() |
| MSAL Client ID from Xrm context | Available before auth in Dataverse web resources | Solves chicken-and-egg bootstrap problem |
| Remove dev defaults, throw on missing | Dev defaults mask production config failures | All shared libs need cleanup |
| IOptions<DataverseOptions> for BFF | Standard .NET config pattern with validation | BFF API uses injected config |

### Discovered Resources

**Applicable ADRs**:
- `.claude/adr/ADR-001-minimal-api.md` — Minimal API patterns
- `.claude/adr/ADR-006-pcf-over-webresources.md` — PCF vs Code Page
- `.claude/adr/ADR-008-endpoint-filters.md` — Endpoint filters
- `.claude/adr/ADR-010-di-minimalism.md` — DI + Options pattern
- `.claude/adr/ADR-012-shared-components.md` — Shared library rules
- `.claude/adr/ADR-022-pcf-platform-libraries.md` — React versioning

**Applicable Constraints**:
- `.claude/constraints/api.md` — BFF API rules
- `.claude/constraints/auth.md` — OAuth/MSAL patterns
- `.claude/constraints/pcf.md` — PCF development rules
- `.claude/constraints/azure-deployment.md` — CORS, App Settings
- `.claude/constraints/config.md` — Feature flags, Options pattern

**Applicable Patterns**:
- `.claude/patterns/api/service-registration.md` — Options pattern example
- `.claude/patterns/auth/msal-client.md` — MSAL configuration
- `.claude/patterns/pcf/dataverse-queries.md` — Environment variable queries
- `.claude/patterns/pcf/control-initialization.md` — PCF lifecycle

**Existing Code to Reference**:
- `src/client/pcf/shared/utils/environmentVariables.ts` — PCF env var query pattern
- `src/client/shared/Spaarke.Auth/src/config.ts` — Auth config resolution chain
- `src/server/api/Sprk.Bff.Api/appsettings.template.json` — Token substitution pattern
- `scripts/Provision-Customer.ps1` — Parameterized provisioning script

**Applicable Scripts**:
- `scripts/Provision-Customer.ps1` — Customer provisioning (needs env var step)
- `scripts/Deploy-*.ps1` — Various deployment scripts (need parameterization)
- `Validate-DeployedEnvironment.ps1` — New validation script to create

---

## 3. Implementation Approach

### Phase Structure (Optimized for Parallel Execution)

```
Phase 1: Foundation (Tasks 001-009)
├─ GROUP A (parallel): BFF API C# fixes (3 independent files)
├─ GROUP B (parallel with A): Dataverse solution XML + env var definitions
├─ GROUP C (parallel with A): Infrastructure script parameterization
└─ GROUP D (sequential after A): appsettings template additions

Phase 2: Shared Library Core (Tasks 010-014) ← CRITICAL PATH
├─ Task 010: resolveRuntimeConfig() in @spaarke/auth (BLOCKS Phase 3-5)
├─ Task 011: Clean environmentVariables.ts defaults (parallel with 010)
├─ Task 012: Standardize window globals + scope format
├─ Task 013: @spaarke/auth build + publish
└─ Task 014: Verify shared lib integration test

Phase 3: Code Page Migration (Tasks 020-028) ← ALL PARALLEL
├─ Task 020: AnalysisWorkspace (pilot — verify pattern)
├─ Task 021: PlaybookBuilder
├─ Task 022: SprkChatPane
├─ Task 023: LegalWorkspace
├─ Task 024: DocumentUploadWizard
├─ Task 025: SpeAdminApp
├─ Task 026: DocumentRelationshipViewer (code page)
├─ Task 027: SemanticSearch (code page)
└─ Task 028: External SPA

Phase 4: PCF Control Migration (Tasks 030-037) ← ALL PARALLEL
├─ Task 030: UniversalQuickCreate
├─ Task 031: DocumentRelationshipViewer (PCF)
├─ Task 032: SemanticSearchControl
├─ Task 033: RelatedDocumentCount
├─ Task 034: UniversalDatasetGrid
├─ Task 035: EmailProcessingMonitor
├─ Task 036: AssociationResolver
└─ Task 037: ScopeConfigEditor

Phase 5: Legacy JS + Office Add-ins (Tasks 040-046) ← ALL PARALLEL
├─ Task 040: sprk_subgrid_parent_rollup.js
├─ Task 041: sprk_emailactions.js
├─ Task 042: sprk_DocumentOperations.js
├─ Task 043: sprk_communication_send.js
├─ Task 044: sprk_aichatcontextmap_ribbon.js
├─ Task 045: Office Add-in authConfig + manifests
└─ Task 046: Ribbon webresource JS files (infrastructure/dataverse/ribbon/)

Phase 6: Validation & Cleanup (Tasks 050-055)
├─ Task 050: Create Validate-DeployedEnvironment.ps1
├─ Task 051: Update Provision-Customer.ps1 (add env var creation step)
├─ Task 052: Script parameterization batch (30+ scripts)
├─ Task 053: Run validation in dev environment
├─ Task 054: Update deployment documentation
└─ Task 090: Project wrap-up
```

### Critical Path

**Blocking Dependencies:**
- Phase 2 (Task 010: resolveRuntimeConfig) BLOCKS Phase 3 (all code pages)
- Phase 2 (Task 011: environmentVariables.ts) BLOCKS Phase 4 (all PCF controls)
- Phase 1 BLOCKS Phase 2 (appsettings tokens needed for shared lib)
- All phases 1-5 BLOCK Phase 6 (validation)

**High-Risk Items:**
- MSAL client ID bootstrap (chicken-and-egg) — Mitigation: Xrm context investigation in Task 010
- Removing dev defaults may break local dev — Mitigation: Keep .env.development files

### Parallel Execution Groups

| Group | Tasks | Prerequisite | Agent Count | Notes |
|-------|-------|--------------|-------------|-------|
| 1A | 001, 002, 003 | None | 3 | BFF API files are independent |
| 1B | 004, 005 | None | 2 | Dataverse + infra scripts, independent of BFF |
| 2 | 010, 011, 012 | Phase 1 complete | 2-3 | Shared libs, some dependencies |
| 3 | 020-028 | Task 010 complete | Up to 9 | All code pages fully independent |
| 4 | 030-037 | Task 011 complete | Up to 8 | All PCF controls fully independent |
| 5 | 040-046 | Task 010 complete | Up to 7 | All legacy JS/add-ins independent |
| 6 | 050-054, 090 | All prior complete | 1-2 | Validation and cleanup |

---

## 4. Phase Breakdown

### Phase 1: Foundation (Tasks 001-009)

**Objectives:**
1. Fix all BFF API hardcoded values
2. Add Dataverse Environment Variable definitions to solution XML
3. Add new tokens to appsettings.template.json
4. Parameterize infrastructure scripts with hardcoded org URLs

**Deliverables:**
- [ ] OfficeDocumentPersistence.cs uses IOptions<DataverseOptions>
- [ ] OfficeService.cs uses IOptions for Dataverse URL and share link URL
- [ ] appsettings.template.json has new tokens (SHARE_LINK_BASE_URL, DATAVERSE_APP_ID)
- [ ] SpaarkeCore solution XML includes env var definitions
- [ ] update-theme-icons.ps1 and Deploy-EventsSitemap.ps1 parameterized

**Inputs**: spec.md, current source files
**Outputs**: Fixed C# files, updated solution XML, parameterized scripts

### Phase 2: Shared Library Core (Tasks 010-014) — CRITICAL PATH

**Objectives:**
1. Add resolveRuntimeConfig() to @spaarke/auth
2. Clean environmentVariables.ts defaults (remove dev fallbacks)
3. Standardize window global names and BFF scope format

**Deliverables:**
- [ ] resolveRuntimeConfig() function in @spaarke/auth queries Dataverse Environment Variables
- [ ] environmentVariables.ts throws on missing values instead of returning dev defaults
- [ ] Window global names standardized (__SPAARKE_BFF_BASE_URL__ everywhere)
- [ ] BFF scope format standardized (user_impersonation everywhere)
- [ ] @spaarke/auth builds successfully with new exports

**Critical Tasks:**
- Task 010 (resolveRuntimeConfig) — MUST BE FIRST — BLOCKS all code page migrations

**Inputs**: Spaarke.Auth/src/config.ts, environmentVariables.ts, spec.md
**Outputs**: Updated shared libraries, new exports

### Phase 3: Code Page Migration (Tasks 020-028) — ALL PARALLEL

**Objectives:**
1. Migrate each code page from build-time .env.production to runtime Dataverse Environment Variable resolution
2. Remove hardcoded BFF URLs and client IDs from source code
3. Update bootstrap sequence to use resolveRuntimeConfig()

**Deliverables:**
- [ ] Each code page resolves BFF URL from Dataverse at runtime
- [ ] Each code page resolves MSAL Client ID from Xrm context or Dataverse
- [ ] .env.production files cleaned or removed
- [ ] DefinePlugin/Vite define shims removed where no longer needed

**Inputs**: resolveRuntimeConfig() from Phase 2, each code page's source
**Outputs**: Updated code page source files

### Phase 4: PCF Control Migration (Tasks 030-037) — ALL PARALLEL

**Objectives:**
1. Replace hardcoded msalConfig.ts in each PCF control with @spaarke/auth + environmentVariables.ts
2. Remove hardcoded CLIENT_ID, BFF_APP_ID, API base URL from all controls

**Deliverables:**
- [ ] Each PCF control uses @spaarke/auth for MSAL config
- [ ] Each PCF control reads BFF URL from environmentVariables.ts
- [ ] All hardcoded IDs removed from PCF source
- [ ] Old msalConfig.ts files removed

**Inputs**: Updated @spaarke/auth, cleaned environmentVariables.ts
**Outputs**: Updated PCF control source files

### Phase 5: Legacy JS + Office Add-ins (Tasks 040-046) — ALL PARALLEL

**Objectives:**
1. Remove hardcoded BFF URL and app IDs from legacy JS webresources
2. Parameterize Office add-in auth config and manifests

**Deliverables:**
- [ ] Legacy JS webresources query Dataverse Environment Variables or use @spaarke/auth
- [ ] Office add-in authConfig.ts has no dev-specific fallbacks
- [ ] Office add-in manifest parameterized for per-environment deployment

**Inputs**: Updated @spaarke/auth
**Outputs**: Updated JS files, updated add-in config

### Phase 6: Validation & Cleanup (Tasks 050-055, 090)

**Objectives:**
1. Create deployment validation script
2. Update provisioning script
3. Parameterize remaining scripts
4. Validate end-to-end in dev
5. Update documentation

**Deliverables:**
- [ ] Validate-DeployedEnvironment.ps1 created and passing in dev
- [ ] Provision-Customer.ps1 sets Dataverse Environment Variables
- [ ] All 30+ scripts parameterized
- [ ] Deployment guides updated
- [ ] Project wrap-up complete

---

## 5. Dependencies

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| @spaarke/auth | src/client/shared/Spaarke.Auth/ | Ready — needs new function |
| environmentVariables.ts | src/client/pcf/shared/utils/ | Ready — needs cleanup |
| appsettings.template.json | src/server/api/Sprk.Bff.Api/ | Ready — needs new tokens |
| Provision-Customer.ps1 | scripts/ | Ready — needs env var step |
| SpaarkeCore solution | (Dataverse) | Ready — needs env var definitions |

### External Dependencies

| Dependency | Status | Risk |
|------------|--------|------|
| Dataverse Environment Variables API | GA | Low |
| Xrm.Utility.getGlobalContext() | GA | Low |
| Azure App Service settings | GA | Low |

---

## 6. Testing Strategy

**Unit Tests**: Not primary focus — this is configuration/infrastructure work
- Test resolveRuntimeConfig() handles missing env vars correctly
- Test config resolution chain (Xrm → env var → error)

**Integration Tests**:
- Code page loads with runtime-resolved config
- PCF control initializes with environmentVariables.ts config
- BFF API starts with IOptions-injected values

**E2E Validation**:
- Validate-DeployedEnvironment.ps1 checks all components in deployed environment
- Zero "spaarkedev1" in production config
- All Dataverse Environment Variables populated

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 1:**
- [ ] BFF API builds with zero hardcoded environment-specific values
- [ ] dotnet build succeeds

**Phase 2:**
- [ ] @spaarke/auth exports resolveRuntimeConfig()
- [ ] npm run build succeeds for shared library

**Phase 3:**
- [ ] All code pages build successfully
- [ ] No .env.production with dev URLs remains

**Phase 4:**
- [ ] All PCF controls build successfully (npm run build in each)
- [ ] No hardcoded CLIENT_ID in any PCF source

**Phase 5:**
- [ ] No hardcoded 1e40baad or 170c98e1 in legacy JS files

**Phase 6:**
- [ ] Validate-DeployedEnvironment.ps1 passes in dev
- [ ] grep for "spaarkedev1" returns zero hits in production code

### Business Acceptance

- [ ] Build once, deploy anywhere — verified by deploying same artifacts to different environment
- [ ] Configuration failures produce clear error messages (no silent fallback)

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | MSAL client ID bootstrap (chicken-and-egg) | Low | High | Xrm context available pre-auth; build-time fallback |
| R2 | Runtime query adds ~100ms latency | High | Low | 5-minute cache, tiny query |
| R3 | Removing dev defaults breaks local dev | Low | Medium | .env.development untouched |
| R4 | Code page migration breaks existing pages | Medium | High | Incremental: pilot one, verify, then batch |
| R5 | Missing env vars in deployed environment | Low | High | Validate-DeployedEnvironment.ps1 catches immediately |
| R6 | Legacy JS webresources can't easily query Dataverse | Medium | Medium | Use @spaarke/auth or inline fetch |

---

## 9. Next Steps

1. **Execute tasks** — Begin with Phase 1 (Foundation), tasks 001-005 in parallel
2. **Phase 2** — Critical path shared library work
3. **Phases 3-5** — Massively parallel code page + PCF + legacy JS migrations
4. **Phase 6** — Validation and cleanup

---

**Status**: Ready for Tasks
**Next Action**: Execute Phase 1 tasks (001-005) in parallel

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks. Tasks are designed for concurrent agent execution — see Parallel Execution Groups table.*
