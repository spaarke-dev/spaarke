/**
 * Barrel exports for DailyBriefing components.
 *
 * Re-export shim (R2 task 011): the canonical implementations live in
 * `@spaarke/daily-briefing-components/components`. This barrel keeps the
 * existing call sites in `src/solutions/DailyBriefing/src/` building during
 * the hoist transition. Cleanup tracked by R2 task 017.
 */

export { DigestHeader } from "./DigestHeader";
export type { DigestHeaderProps } from "./DigestHeader";

export { EmptyState } from "./EmptyState";

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
