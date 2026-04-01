/**
 * reportingConfig.ts
 * Constants and lazy config getters for Power BI Embedded Reporting module.
 *
 * All environment-specific values (workspace IDs, tenant IDs, capacity IDs)
 * MUST be stored in Dataverse Environment Variables and retrieved from the BFF.
 * MUST NOT be hardcoded here per project CLAUDE.md MUST rules.
 *
 * Lazy getters follow the pattern from .claude/patterns/auth/spaarke-auth-initialization.md:
 * use functions, not module-level constants, to avoid "config not initialized" errors.
 *
 * @see ADR-021 - No hardcoded colors
 * @see ADR-026 - Vite + vite-plugin-singlefile build
 */

// ---------------------------------------------------------------------------
// Dataverse entity names
// ---------------------------------------------------------------------------

/** Dataverse entity that stores the report catalog */
export const REPORT_ENTITY_NAME = "sprk_report";

/** Dataverse environment variable logical name for module feature gate */
export const REPORTING_MODULE_ENABLED_ENV_VAR = "sprk_ReportingModuleEnabled";

/** Dataverse security role name required for Reporting access */
export const REPORTING_ACCESS_ROLE = "sprk_ReportingAccess";

// ---------------------------------------------------------------------------
// BFF API endpoint paths (relative to BFF base URL)
// ---------------------------------------------------------------------------

/** BFF endpoint to check module status (module gate + auth check without a specific report) */
export const REPORTING_STATUS_PATH = "/api/reporting/status";

/** BFF endpoint to fetch the embed token for a given report */
export const REPORTING_EMBED_TOKEN_PATH = "/api/reporting/embed-token";

/** BFF endpoint to retrieve the report catalog from sprk_report */
export const REPORTING_CATALOG_PATH = "/api/reporting/reports";

// ---------------------------------------------------------------------------
// Power BI client configuration
// ---------------------------------------------------------------------------

/**
 * Power BI OAuth scope for service principal token acquisition.
 * Used by the BFF (not the frontend) — stored here as documentation.
 */
export const POWERBI_API_SCOPE = "https://analysis.windows.net/.default";

/** Token refresh threshold — refresh at 80% of embed token TTL */
export const EMBED_TOKEN_REFRESH_THRESHOLD = 0.8;

/** Default report embed type */
export const REPORT_EMBED_TYPE = "report";

// ---------------------------------------------------------------------------
// UI constants
// ---------------------------------------------------------------------------

/** Default height for the report container (fills available space) */
export const REPORT_CONTAINER_HEIGHT = "100%";

/** Minimum height for the report iframe in pixels */
export const REPORT_MIN_HEIGHT_PX = 400;
