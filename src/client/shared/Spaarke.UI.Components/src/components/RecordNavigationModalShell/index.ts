/**
 * Barrel exports for `RecordNavigationModalShell`.
 */

export { RecordNavigationModalShell, default } from './RecordNavigationModalShell';
export type {
  IRecordNavigationModalShellProps,
  IDirtyCheckRequest,
  IDirtyCheckResponse,
  RecordNavigationDirection,
} from './types';
export { DIRTY_CHECK_REQUEST_TYPE, DIRTY_CHECK_RESULT_TYPE } from './types';
// The parent side of the dirty-check round trip as a function, for a modal that guards its own `SprkModal` browse nav (task 195).
export { queryDirtyState } from './queryDirtyState';
export type { QueryDirtyStateOptions } from './queryDirtyState';
