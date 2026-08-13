/**
 * `EmailAssociationsAndTracking` — barrel for the reading-pane ASSOCIATIONS +
 * TRACKING sub-views (email-communication-solution-r5 task 035). Fills the
 * task-032 `renderConnections`/`renderTracking` shell slots. NOT wired into
 * `src/components/index.ts` here — the orchestrator adds the top-level
 * barrel export after the Wave-5 parallel group lands (per task guardrail).
 */
export { EmailConnectionsReview } from './EmailConnectionsReview';
export { EmailTrackingPanel } from './EmailTrackingPanel';
export { ConfirmedChip } from './EmailConnectionsReviewRows';
export { useConnectionsReviewStyles } from './EmailConnectionsReview.styles';
export type {
  EmailConnectionsReviewProps,
  EmailTrackingPanelProps,
  CreatedRecordRef,
  EmlSource,
} from './EmailAssociationsAndTracking.types';
