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

export { DailyBriefingApp } from "./DailyBriefingApp";
export type { DailyBriefingAppProps } from "./DailyBriefingApp";

export { DigestHeader } from "./DigestHeader";
export type { DigestHeaderProps } from "./DigestHeader";

export { EmptyState } from "./EmptyState";
export type { EmptyStateProps } from "./EmptyState";

export { TldrSection } from "./TldrSection";
export type { TldrSectionProps } from "./TldrSection";

export { ActivityNotesSection } from "./ActivityNotesSection";
export type { ActivityNotesSectionProps } from "./ActivityNotesSection";

export { CaughtUpFooter } from "./CaughtUpFooter";
export type { CaughtUpFooterProps } from "./CaughtUpFooter";

export { PreferencesDropdown } from "./PreferencesDropdown";
export type { PreferencesDropdownProps } from "./PreferencesDropdown";

export { ChannelHeading } from "./ChannelHeading";
export type { ChannelHeadingProps } from "./ChannelHeading";

export { NarrativeBullet } from "./NarrativeBullet";
export type { NarrativeBulletProps } from "./NarrativeBullet";

export { getChannelIcon } from "./channelIcons";
