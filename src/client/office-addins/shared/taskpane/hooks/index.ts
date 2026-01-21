// Theme hooks
export { useOfficeTheme } from './useOfficeTheme';
export type { UseOfficeThemeResult, OfficeThemeType } from './useOfficeTheme';

export { useTheme } from './useTheme';
export type {
  UseThemeResult,
  ThemePreference,
  ResolvedThemeType,
} from './useTheme';

// Entity search hook
export { useEntitySearch } from './useEntitySearch';
export type {
  EntityType,
  EntitySearchResult,
  RecentEntity,
  UseEntitySearchOptions,
  UseEntitySearchResult,
} from './useEntitySearch';
export { ALL_ENTITY_TYPES, ENTITY_LOGICAL_NAMES } from './useEntitySearch';

// Save flow hook
export { useSaveFlow } from './useSaveFlow';
export type {
  SourceType,
  SaveFlowState,
  ProcessingOptions,
  StageStatus,
  JobStatus,
  SaveResponse,
  SaveContentData,
  SaveMetadata,
  SaveRequest,
  SaveFlowContext,
  UseSaveFlowOptions,
  UseSaveFlowResult,
} from './useSaveFlow';

// Accessibility hooks
export { useAnnounce, useAnnounceOnChange } from './useAnnounce';
export type {
  AnnounceMode,
  UseAnnounceOptions,
  UseAnnounceResult,
} from './useAnnounce';
