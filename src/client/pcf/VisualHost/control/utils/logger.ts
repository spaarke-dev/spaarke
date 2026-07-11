/**
 * logger — re-export shim (VHVU-041).
 *
 * Moved to `@spaarke/visuals`. This shim preserves existing `'../utils/logger'`
 * imports across the kept PCF host code (VisualHostRoot, ChartRenderer,
 * services, ThemeProvider, index, and the self-fetch visuals). Full repoint in
 * VHVU-060.
 */

export * from '../../../../shared/Spaarke.Visuals/src/utils/logger';
