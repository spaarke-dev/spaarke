/**
 * Visual Host PCF Types — re-export shim (VHVU-041).
 *
 * The presentational view-model types moved to `@spaarke/visuals`
 * (`src/client/shared/Spaarke.Visuals/src/types`). This shim keeps every
 * existing `'../types'` import across ChartRenderer / VisualHostRoot /
 * services / the self-fetch visuals working with a single-line change.
 * The full repoint to `@spaarke/visuals` happens in VHVU-060.
 */

export * from '../../../../shared/Spaarke.Visuals/src/types';
