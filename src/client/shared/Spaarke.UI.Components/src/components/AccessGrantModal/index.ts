export { AccessGrantModal } from './AccessGrantModal';
export type {
  IAccessGrantModalProps,
  IAccessGrantCandidate,
  IAccessGrantRecord,
  IContactSearchResult,
  IOrganizationPick,
  // Re-exported 2026-09-21: both are `export interface` in ./types and are consumed by the
  // TrackingFieldTrio PCF host through THIS barrel, but were never listed here. Nothing caught it
  // because the host file had been reviewed and never compiled — the first production build of
  // v1.0.31 failed on TS2305 for exactly these two names.
  IUserPick,
  ISecureOwnerInfo,
  ExternalGrantRootType,
  IAccessLevelOption,
  AccessPermissionState,
} from './types';
export { DEFAULT_ACCESS_LEVEL_OPTIONS } from './types';
