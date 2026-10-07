export {
  AccessGrantModal,
  describeAccessPermission,
  // Task 142: Assigned-To suggestions + the residual read-time access notice (criterion 17).
  describeResidualAccess,
  buildRevokeNotice,
  ASSIGNED_ACCESS_LEVEL,
  // Task 114: the label of an external user's share on a Restricted record.
  EXTERNAL_USER_NO_ACCESS_LABEL,
} from './AccessGrantModal';
export type { IAssignedAccessEntry } from './AccessGrantModal';
// Task 138: the host's fail-closed Access Permission + Secure → state mapping (pure, host supplies the integers).
export { resolveAccessPermissionState } from './accessPermissionState';
export type { IAccessPermissionValues } from './accessPermissionState';
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
  IUserPickOptions,
  ISecureOwnerInfo,
  ExternalGrantRootType,
  IAccessLevelOption,
  AccessPermissionState,
} from './types';
export { DEFAULT_ACCESS_LEVEL_OPTIONS } from './types';
