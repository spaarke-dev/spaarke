export { TrackingFieldTrio } from './TrackingFieldTrio';
export type { ITrackingFieldTrioProps, IAccessPermissionOption, GrantModalSection } from './types';
// Task 153: the record's access status (064's per-record read) — the host's fail-closed parse and the indicator rule.
export {
  parseAccessStatusResponse,
  readAccessStatus,
  resolveAccessIndicator,
  ACCESS_STATUS_UNAVAILABLE,
} from './accessStatus';
export type { AccessSignalState, ITrackingAccessStatus, AccessIndicatorView } from './accessStatus';
