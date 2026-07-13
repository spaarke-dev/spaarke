/**
 * @spaarke/visuals — utils barrel.
 *
 * Presentational helpers (moved from the VisualHost PCF in VHVU-041):
 *  - cardConfigResolver — resolve merged card configuration
 *  - chartColors        — chart/status color palettes
 *  - gradeUtils         — letter-grade + grade-color helpers
 *  - logger             — lightweight namespaced console logger
 *  - tokenSetColors     — Fluent token-set → color resolution
 *  - trendAnalysis      — linear-regression slope + trend direction
 *  - valueFormatters    — number/percentage/currency/grade formatting
 */

export * from './cardConfigResolver';
export * from './chartColors';
export * from './gradeUtils';
export * from './logger';
export * from './tokenSetColors';
export * from './trendAnalysis';
export * from './valueFormatters';
