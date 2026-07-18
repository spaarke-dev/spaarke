/**
 * CommunicationTimeline - Polling conversation/timeline component (task 060, FR-10)
 *
 * Renders a thread's persisted `sprk_communication` rows (email + chat
 * interleaved, channel-badged, reply-nested), a compose/send box reusing
 * `<EmailComposer/>` sub-components, an unread indicator, and polls the BFF
 * on a configurable ~5s interval. Reads persisted records only — NO
 * client-side ACS SDK (NFR-04). Packaged as a form-bound PCF by task 061
 * (ADR-026 Path-A exception).
 */

// Component
export { CommunicationTimeline } from './CommunicationTimeline';

// Public types
export type {
  TimelineChannelType,
  TimelineBodyFormat,
  TimelineAttachment,
  TimelineMessage,
  TimelineThread,
  UnreadState,
  CommunicationTimelinePrefill,
  QuoteIntoEmailPayload,
  CommunicationTimelineProps,
} from './CommunicationTimeline.types';
export {
  COMMUNICATION_TYPE_EMAIL,
  COMMUNICATION_TYPE_MESSAGE,
  BODY_FORMAT_PLAIN_TEXT,
  BODY_FORMAT_HTML,
} from './CommunicationTimeline.types';

// Reducer + state (exported for task 080 unit tests / advanced composition)
export { communicationTimelineReducer, initialTimelineState } from './CommunicationTimeline.reducer';
export type {
  CommunicationTimelineState,
  CommunicationTimelineStatus,
  CommunicationTimelineAction,
} from './CommunicationTimeline.reducer';

// Pure ordering/nesting + DTO-mapping helpers (task 080 unit-tests these directly)
export { buildTimeline, mapThreadMessageDtoToTimelineMessage } from './CommunicationTimeline.buildTimeline';
export type { TimelineEntry } from './CommunicationTimeline.buildTimeline';

// Poll hook
export { useThreadPoll } from './hooks/useThreadPoll';
export type { UseThreadPollOptions, UseThreadPollResult } from './hooks/useThreadPoll';

// Sub-components (exported for advanced/direct composition)
export { ChannelBadge } from './subcomponents/ChannelBadge';
export type { IChannelBadgeProps } from './subcomponents/ChannelBadge';

export { UnreadIndicator } from './subcomponents/UnreadIndicator';
export type { IUnreadIndicatorProps } from './subcomponents/UnreadIndicator';

export { MessageRow } from './subcomponents/MessageRow';
export type { IMessageRowProps } from './subcomponents/MessageRow';

export { TimelineComposeBox } from './subcomponents/TimelineComposeBox';
export type { ITimelineComposeBoxProps, ITimelineSendPayload } from './subcomponents/TimelineComposeBox';
