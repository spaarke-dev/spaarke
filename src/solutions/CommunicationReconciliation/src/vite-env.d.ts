/// <reference types="vite/client" />

/**
 * Vite build-time environment variables for the Communication Reconciliation Code Page.
 *
 * All VITE_* variables are public — do not place secrets here.
 *
 * There is deliberately NO build-time App Insights key (#1537): a key baked into
 * the build sent every customer's browser telemetry to the environment the build
 * was made for. main.tsx initialises AppInsightsService at runtime with this
 * environment's connection string from the BFF (`getTelemetryConnectionString`).
 */
