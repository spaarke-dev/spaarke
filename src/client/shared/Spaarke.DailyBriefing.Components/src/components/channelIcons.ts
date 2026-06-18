/**
 * channelIcons — Maps CHANNEL_REGISTRY iconName strings to Fluent v9 icon
 * components. Centralised here to keep ChannelCard clean.
 *
 * Only imports from @fluentui/react-icons (ADR-021).
 *
 * Hoisted into `@spaarke/daily-briefing-components/components` by R2 task 011
 * (Wave 3 / Group A) alongside ActivityNotesSection, which depends on it.
 * Source of truth; the original-location file at
 * `src/solutions/DailyBriefing/src/components/channelIcons.ts` is now a
 * re-export shim pending full cleanup in R2 task 017.
 */

import type { FluentIcon } from "@fluentui/react-icons";
import {
  WarningRegular,
  ClockRegular,
  DocumentRegular,
  MailRegular,
  CalendarRegular,
  BriefcaseRegular,
  PeopleRegular,
  InfoRegular,
} from "@fluentui/react-icons";

const ICON_MAP: Record<string, FluentIcon> = {
  Warning: WarningRegular,
  Clock: ClockRegular,
  Document: DocumentRegular,
  Mail: MailRegular,
  Calendar: CalendarRegular,
  Briefcase: BriefcaseRegular,
  People: PeopleRegular,
  Info: InfoRegular,
};

/**
 * Resolves a CHANNEL_REGISTRY iconName to its Fluent v9 icon component.
 * Falls back to InfoRegular for unknown icon names.
 */
export function getChannelIcon(iconName: string): FluentIcon {
  return ICON_MAP[iconName] ?? InfoRegular;
}
