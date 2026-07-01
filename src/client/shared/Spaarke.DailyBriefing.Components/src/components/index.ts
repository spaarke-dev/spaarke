/**
 * @spaarke/daily-briefing-components — components barrel
 *
 * UI components for the Daily Briefing surface. Populated by R2 task 011
 * (Wave 3 / Group A) with the 9 components from FR-04:
 *   - DailyBriefingApp (top-level composer)
 *   - TldrSection
 *   - ActivityNotesSection
 *   - ChannelHeading
 *   - NarrativeBullet
 *   - PreferencesDropdown
 *   - CaughtUpFooter
 *   - DigestHeader
 *   - EmptyState
 *
 * Plus the channel-icon resolver helper used by ActivityNotesSection
 * (carries the original `getChannelIcon` export contract).
 */

export { DailyBriefingApp } from './DailyBriefingApp';
export type { DailyBriefingAppProps } from './DailyBriefingApp';

export { DigestHeader } from './DigestHeader';
export type { DigestHeaderProps } from './DigestHeader';

export { EmptyState } from './EmptyState';
export type { EmptyStateProps } from './EmptyState';

export { TldrSection } from './TldrSection';
export type { TldrSectionProps } from './TldrSection';

export { ActivityNotesSection } from './ActivityNotesSection';
export type { ActivityNotesSectionProps } from './ActivityNotesSection';

export { CaughtUpFooter } from './CaughtUpFooter';
export type { CaughtUpFooterProps } from './CaughtUpFooter';

export { PreferencesDropdown } from './PreferencesDropdown';
export type { PreferencesDropdownProps } from './PreferencesDropdown';

export { ChannelHeading } from './ChannelHeading';
export type { ChannelHeadingProps } from './ChannelHeading';

export { NarrativeBullet } from './NarrativeBullet';
export type { NarrativeBulletProps } from './NarrativeBullet';

export { NarrativeCitedText } from './NarrativeCitedText';
export type { NarrativeCitedTextProps } from './NarrativeCitedText';

// Sub-list slot components (FR-11..FR-14). Task 020 (Wave 8) lays the
// skeleton + slot files; tasks 021/022/023 (Wave 9) implement per-row
// link / To-Do / Dismiss behavior. Slots are exported here so consumers
// (tests, future shells) can target each individually.
export { SubRow } from './SubRow';
export type { SubRowProps } from './SubRow';

export { SubRowLink } from './SubRowLink';
export type { SubRowLinkProps } from './SubRowLink';

export { SubRowTodo } from './SubRowTodo';
export type { SubRowTodoProps } from './SubRowTodo';

export { SubRowDismiss } from './SubRowDismiss';
export type { SubRowDismissProps } from './SubRowDismiss';

export { getChannelIcon } from './channelIcons';
