export { ThreePaneLayout } from './ThreePaneLayout';
export { useThreePaneLayout } from './useThreePaneLayout';
// Note: SplitterHandlers is intentionally not re-exported here — it is already
// exported from the library's hooks barrel (via useTwoPanelLayout) to avoid ambiguity.
export type { ThreePaneLayoutProps, UseThreePaneLayoutResult } from './ThreePaneLayout.types';
export type { UseThreePaneLayoutOptions } from './useThreePaneLayout';
