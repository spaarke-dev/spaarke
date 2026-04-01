# Lessons Learned — Power BI Embedded Reporting R1

> **Project**: spaarke-powerbi-embedded-r1
> **Completed**: 2026-04-01
> **Author**: Claude Code

---

## What Went Well

- **Parallel task execution**: Phases 1 and 4 (BFF API and Dataverse schema) ran independently in parallel, compressing the overall timeline significantly. Group E tasks (030-036) required no Phase 1 dependencies and could start on day one.
- **Comprehensive spec**: The AI-optimized spec.md had sufficient detail on service principal profiles, token caching strategy, and security architecture that ambiguity was rare during implementation. The `<must-rules>` section translated directly into testable acceptance criteria.
- **Good pattern library**: The existing `.claude/patterns/` library (service-principal auth, distributed-cache, endpoint-definition, full-page-custom-page) mapped cleanly onto the Power BI Embedded requirements, avoiding design rework at the implementation phase.
- **EventsPage reference**: The canonical `src/solutions/EventsPage/` Code Page was a reliable scaffold reference for `src/solutions/Reporting/`, producing a working Vite + React 19 + single-file baseline on the first task.
- **ADR coverage**: All 8 applicable ADRs (ADR-001, 006, 008, 009, 010, 012, 021, 026) were identified during pipeline initialization and remained the authoritative constraints throughout — no violations required rework.

---

## Technical Decisions That Worked

- **Service principal profiles for multi-tenancy**: Using per-customer SP profiles (rather than one SP per customer or one workspace per SP) proved to be the right abstraction. It keeps the Entra app registration surface to a single object while achieving workspace isolation between customers.
- **Redis token caching with 80% TTL auto-refresh**: The `report.setAccessToken()` pattern from `powerbi-client-react` combined with Redis-backed token storage eliminated page reloads on token expiry. The 80% threshold gave sufficient lead time for silent refresh without wasted token generations.
- **Transparent PBI background for dark mode**: Setting `background: models.BackgroundType.Transparent` on the embed configuration and relying on Fluent v9 design tokens for the container background produced correct dark mode rendering without custom CSS overrides or theme injection into the .pbix files.
- **Import mode over DirectQuery**: For the R1 use case (4x daily refresh sufficient, no real-time requirements) Import mode was the right choice — lower F-SKU capacity consumption, simpler embed token generation, and no query passthrough latency.
- **`powerbi-client-react` 2.0.2 pinned**: Pinning the version at project start avoided mid-project breaking changes from the package, which has a history of minor-version API shifts.

---

## Areas for Improvement

- **.pbix reports require human creation in Power BI Desktop**: The 5 standard product report templates (task 034) are placeholders — actual DAX measures and visual layouts must be authored by a human in Power BI Desktop and connected to the semantic model. Future projects should plan this as a separate, non-AI deliverable with dedicated time for a PBI developer.
- **BU RLS needs live testing with real BU data**: The BU RLS implementation (EffectiveIdentity in embed tokens) was validated in integration tests with mocked data, but end-to-end verification requires two or more active Business Units with distinct data in the Dataverse environment. This testing was documented but deferred to a live environment smoke test.
- **F-SKU capacity prerequisite blocks early testing**: The requirement for F2+ capacity to be provisioned before any embed testing created a blocking dependency on environment setup that could not be automated. Future projects should front-load this prerequisite verification.
- **Onboarding script complexity**: `Onboard-ReportingCustomer.ps1` requires coordinated execution across three systems (Entra, Power BI service, Dataverse). Documentation is clear, but the multi-step manual process is error-prone. A future improvement would be a single idempotent script with rollback capability.

---

## Recommendations for R2

- **Direct Lake data source**: Migrate from Import mode to Direct Lake (Fabric Lakehouse) for real-time data scenarios. This eliminates the 4x daily refresh limitation and enables sub-second data freshness for high-velocity Dataverse data (e.g., recent document activity, live pipeline metrics).
- **Paginated reports (RDLC-style)**: Add paginated report support for tabular outputs (invoice registers, compliance summaries, audit trails) that require print-quality layout. Power BI REST API supports paginated report embedding with the same embed token mechanism.
- **Dashboard tiles on entity forms (PCF)**: Create a lightweight PCF control that embeds a single Power BI dashboard tile on Dataverse entity forms (e.g., a KPI tile on the Matter record). This extends reporting to in-context analytics without requiring navigation to the full Reporting Code Page.
- **Report scheduling (email delivery)**: Implement subscription-based report delivery using the Power BI REST API subscriptions endpoint, enabling scheduled PDF/PPTX delivery to Dataverse users. Pair with the existing Spaarke notification infrastructure.
- **Semantic model authoring UI**: Consider a simplified semantic model configuration page for Spaarke admins to define custom measures and calculated columns without requiring Power BI Desktop — possible via the XMLA endpoint or Power BI REST API dataset operations.

---

*Generated during project wrap-up (task 090)*
