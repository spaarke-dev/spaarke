# Power BI Embedded Reporting R1 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-31
> **Source**: design.md
> **Module Name**: Reporting

## Executive Summary

Embed Power BI reports and dashboards into Spaarke's MDA via a full-page "Reporting" Code Page (`sprk_reporting`), using "App Owns Data" with service principal profiles for multi-tenant isolation, Import mode with scheduled Dataverse refresh, in-browser report authoring for customers, and 4-layer security (module gate + security role + workspace isolation + BU RLS). The module is gated by environment variable and supports all three deployment models without per-user Power BI licensing.

## Scope

### In Scope
- `sprk_reporting` Code Page (React 19, Vite single-file build)
- BFF `ReportingEmbedService` with service principal profile management
- BFF embed token generation with Redis caching and BU RLS
- `sprk_report` Dataverse entity for report catalog
- Report selector dropdown grouped by category
- View mode: embedded report rendering with BU RLS filtering
- Edit mode: in-browser report authoring (create, edit, save, save-as) — no PBI Desktop/license needed
- Module gating via `sprk_ReportingModuleEnabled` Dataverse environment variable
- User access control via `sprk_ReportingAccess` security role (Viewer/Author/Admin)
- Business unit RLS via EffectiveIdentity in embed tokens
- Token auto-refresh at 80% TTL via `report.setAccessToken()` (no page reload)
- Export to PDF/PPTX via Power BI REST API
- 5 standard product reports (.pbix templates in source control)
- Report deployment pipeline (`Deploy-ReportingReports.ps1`)
- Report versioning in source control (`reports/` folder)
- Dark mode support (transparent PBI background + Fluent v9)
- Customer onboarding scripts (workspace + profile + report deployment)
- Environment variable-based config (BYOK-compatible)

### Out of Scope
- Paginated reports (RDLC-style — planned R2)
- Fabric Lakehouse / Direct Lake data source
- Real-time streaming datasets
- Dashboard tiles on entity forms (PCF)
- Power BI alerts and subscriptions
- Custom visuals marketplace integration
- Semantic model authoring by customers (requires PBI Desktop)
- Report scheduling (email delivery)
- Capacity management admin UI
- Cross-customer analytics

### Affected Areas
- `src/server/api/Sprk.Bff.Api/Api/Reporting/` — New: ReportingEndpoints.cs, ReportingEmbedService.cs, ReportingProfileManager.cs
- `src/solutions/Reporting/` — New: sprk_reporting Code Page (Vite + React 19)
- `src/solutions/SpaarkeCore/` — New entity: sprk_report; new security role: sprk_ReportingAccess; new env var: sprk_ReportingModuleEnabled
- `scripts/Deploy-ReportingReports.ps1` — New: deployment pipeline script
- `reports/` — New: .pbix templates and CHANGELOG.md

## Requirements

### Functional Requirements

1. **FR-01**: `sprk_reporting` Code Page renders an embedded Power BI report in view mode — Acceptance: Report loads and displays data within 3 seconds of page open
2. **FR-02**: Report dropdown lists all `sprk_report` records grouped by category (Financial, Operational, Compliance, Documents, Custom) — Acceptance: Dropdown populated from Dataverse, default report auto-selected
3. **FR-03**: BFF generates embed tokens via service principal with per-customer profile isolation — Acceptance: Token generated with correct workspace, RLS identity, and SP profile header
4. **FR-04**: Embed tokens cached in Redis with auto-refresh at 80% TTL — Acceptance: `report.setAccessToken()` called without page reload; `tokenExpired` event never fires in normal operation
5. **FR-05**: Business unit RLS filters report data per user's BU hierarchy — Acceptance: User sees only data for their BU and child BUs; verified with test users in different BUs
6. **FR-06**: Users with Author role can create new blank reports bound to customer's semantic model — Acceptance: [New Report] creates report via REST API, opens in edit mode, creates `sprk_report` record
7. **FR-07**: Users with Author role can edit existing reports using embedded edit mode — Acceptance: Full authoring toolbar visible; can add/remove/resize visuals, bind data fields, set filters, add pages
8. **FR-08**: Save and Save As persist reports to customer workspace and update catalog — Acceptance: `report.save()` and `report.saveAs()` work; `sprk_report` record created/updated
9. **FR-09**: Export reports to PDF and PPTX via Power BI REST API — Acceptance: Export button triggers server-side render; file downloads in browser
10. **FR-10**: 5 standard product reports deployed and visible in report dropdown — Acceptance: Matter Pipeline, Financial Summary, Document Activity, Task Overview, Compliance Dashboard all render with customer data
11. **FR-11**: Module hidden when `sprk_ReportingModuleEnabled` = false — Acceptance: Menu item hidden; `/api/reporting/*` endpoints return 404; Code Page shows "Module not available"
12. **FR-12**: Users without `sprk_ReportingAccess` role cannot access reporting — Acceptance: BFF returns 403; Code Page shows access denied
13. **FR-13**: Author/Admin privileges control edit/create/delete buttons — Acceptance: Viewers see view-only; Authors see Edit + New Report; Admins see Edit + New + Delete
14. **FR-14**: Deployment script imports .pbix templates to customer workspaces — Acceptance: Script rebinds dataset to customer Dataverse, sets refresh schedule, seeds `sprk_report` records
15. **FR-15**: Report versioning tracked in source control — Acceptance: `.pbix` files stored in `reports/{version}/`, CHANGELOG.md updated per release
16. **FR-16**: Customer onboarding provisions workspace, SP profile, and reports — Acceptance: New customer has working Reporting module within onboarding flow
17. **FR-17**: Works across all 3 deployment models — Acceptance: Tested in multi-customer, dedicated, and customer tenant configurations

### Non-Functional Requirements
- **NFR-01**: Embed token generation < 500ms (cached) or < 2s (uncached)
- **NFR-02**: Report render time < 3 seconds for standard reports with typical data volume
- **NFR-03**: Token refresh must be seamless — no visual interruption or page reload
- **NFR-04**: All PBI configuration via environment variables — no hardcoded tenant/workspace/capacity IDs
- **NFR-05**: No end user requires a Power BI Pro or Premium Per User license
- **NFR-06**: Dark mode support — transparent PBI background adapts to Fluent v9 theme
- **NFR-07**: Module naming uses "Reporting" exclusively — no "Analytics" or "Analysis" in any label, endpoint, or entity name

## Technical Constraints

### Applicable ADRs
- **ADR-001**: BFF Minimal API pattern for `/api/reporting/*` endpoints
- **ADR-006**: Code Page for standalone reporting page (not PCF)
- **ADR-008**: Endpoint filters for authorization on reporting endpoints
- **ADR-009**: Redis-first caching for embed tokens
- **ADR-010**: DI minimalism — ReportingEmbedService + ReportingProfileManager (2 registrations)
- **ADR-012**: Shared components from `@spaarke/ui-components` (header, dropdown)
- **ADR-021**: Fluent UI v9 exclusively; dark mode via transparent PBI background
- **ADR-026**: Vite + vite-plugin-singlefile build for Code Page

### MUST Rules
- MUST use "App Owns Data" pattern (service principal, not user auth)
- MUST use service principal profiles for multi-tenant workspace isolation
- MUST gate module via `sprk_ReportingModuleEnabled` environment variable
- MUST enforce user access via `sprk_ReportingAccess` security role privileges
- MUST enforce business unit RLS via EffectiveIdentity in embed tokens
- MUST use Import mode for data source (not DirectQuery or Direct Lake in R1)
- MUST cache embed tokens in Redis (ADR-009)
- MUST auto-refresh tokens at 80% TTL via `report.setAccessToken()`
- MUST use `powerbi-client-react` 2.0.2 (React 18+ — Code Page only, not PCF)
- MUST store report catalog in `sprk_report` Dataverse entity
- MUST store .pbix templates in source control with version tracking
- MUST use environment variables for all PBI configuration (BYOK-compatible)
- MUST NOT hardcode workspace IDs, capacity IDs, or tenant IDs
- MUST NOT require end users to have Power BI licenses
- MUST NOT use "Analysis" or "Analytics" in module naming (conflicts with AI features)

### Existing Patterns to Follow
- See `src/server/api/Sprk.Bff.Api/Api/Ai/AiToolEndpoints.cs` — BFF endpoint pattern with endpoint filters
- See `src/solutions/PlaybookLibrary/` — Code Page build pattern (Vite + React 19 + single-file)
- See `scripts/Deploy-ReportingReports.ps1` — follows existing `Deploy-*.ps1` script patterns
- See `.claude/patterns/api/` for API endpoint patterns
- See `.claude/patterns/auth/` for service principal auth patterns

### Key Reference Documents
- [Power BI Playground](https://playground.powerbi.com/)
- [Service principal profiles for multi-tenant](https://learn.microsoft.com/en-us/power-bi/developer/embedded/embed-multi-tenancy)
- [Embedded report authoring](https://learn.microsoft.com/en-us/javascript/api/overview/powerbi/report-authoring-overview)
- [Power BI REST API](https://learn.microsoft.com/en-us/rest/api/power-bi/)
- [powerbi-client-react 2.0.2](https://www.npmjs.com/package/powerbi-client-react)
- [RLS with App Owns Data](https://learn.microsoft.com/en-us/power-bi/developer/embedded/embedded-row-level-security)
- [AppOwnsDataMultiTenant sample](https://github.com/PowerBiDevCamp/AppOwnsDataMultiTenant)

## Success Criteria

1. [ ] Reporting Code Page renders embedded Power BI report — Verify: visual inspection + console error check
2. [ ] Report dropdown shows catalog grouped by category — Verify: sprk_report records populated, dropdown renders
3. [ ] Embed token generated with SP profile isolation — Verify: API call returns valid token, X-PowerBI-Profile-Id header set
4. [ ] Token cached in Redis, auto-refreshes without page reload — Verify: Redis key exists; tokenExpired event never fires
5. [ ] BU RLS filters data correctly per user — Verify: two users in different BUs see different data
6. [ ] In-browser report authoring works (create, edit, save, save-as) — Verify: new report created, visuals added, saved to workspace
7. [ ] Export to PDF/PPTX works — Verify: file downloads with correct content
8. [ ] 5 standard reports deployed and visible — Verify: all 5 render with customer data after deployment script
9. [ ] Module hidden when env var = false — Verify: menu hidden, endpoints 404, Code Page shows disabled message
10. [ ] Non-authorized users blocked — Verify: 403 returned, no embed token generated
11. [ ] Author/Admin privilege tiers work — Verify: Viewer=view only, Author=edit+create, Admin=edit+create+delete
12. [ ] Dark mode renders correctly — Verify: transparent background, no hard-coded colors
13. [ ] Works in all 3 deployment models — Verify: tested in multi-customer, dedicated, customer tenant
14. [ ] No per-user PBI license required — Verify: standard Dataverse user (no PBI license) can view and edit reports
15. [ ] Deployment pipeline deploys .pbix to customer workspaces — Verify: script runs, rebinds dataset, sets refresh
16. [ ] Report versioning in source control — Verify: .pbix files in `reports/`, CHANGELOG.md updated
17. [ ] Onboarding provisions workspace + profile + reports — Verify: new customer has working module end-to-end

## Dependencies

### Prerequisites
- F-SKU capacity provisioned (at least F2 for dev)
- Entra ID app registration with Power BI API permissions (`Dataset.ReadWrite.All`, `Content.Create`, `Workspace.ReadWrite.All`)
- Service principal added to Power BI workspace as Admin/Member
- Service principal enabled in Power BI admin tenant settings
- Dataverse connector credentials configured for Import mode refresh

### External Dependencies
- Power BI REST API (Microsoft service)
- Power BI JavaScript SDK / powerbi-client-react 2.0.2 (npm package)
- Microsoft.PowerBI.Api NuGet package (for BFF)

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Data source | Lakehouse or Import mode? | Import mode with scheduled refresh | No Lakehouse infra; lower cost; hourly freshness |
| Module naming | "Analytics" or "Reporting"? | Reporting — avoids AI Analysis conflict | All names use "Reporting" |
| Security gate | Env var, security role, or both? | Both — env var gates module; security role gates user access | 2-layer access control |
| Customer authoring | In R1 or R2? | R1 — embedded edit mode in browser | Full authoring in Code Page |
| Deployment pipeline | How to move reports dev→prod? | .pbix in source control → Deploy script per customer | Automated, versioned |
| BU RLS | How to enforce intra-customer data security? | EffectiveIdentity with BusinessUnitFilter RLS role in .pbix | DAX filter on USERNAME() |
| Capacity cost | Per-customer or shared? | Shared pool for most; dedicated for large customers | $100-500/mo per customer typical |

## Assumptions

- **Refresh frequency**: 4x daily refresh is sufficient for legal ops reporting. If customers need more frequent refresh, can increase to hourly within Import mode limits (8 refreshes/day on shared capacity, 48 on dedicated).
- **Data volume**: Most customers have <1GB compressed data in the semantic model. If a customer exceeds this, evaluate migration to Direct Lake in R2.
- **Customer report count**: Customers will create <50 custom reports per workspace. If exceeded, consider report folder/categorization enhancements.
- **PBI Desktop for Spaarke authors**: Spaarke developers building standard reports use Power BI Desktop (free). Only Spaarke developers need Desktop — customers never do.

## Unresolved Questions

None — all design decisions resolved during conversation.

---

*AI-optimized specification. Original: design.md*
