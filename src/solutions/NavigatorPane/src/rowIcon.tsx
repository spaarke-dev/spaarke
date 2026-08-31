/**
 * rowIcon — shared record-type icon helper for Navigator rows
 * (spaarke-side-pane-navigation-history-r1 UAT-driven redesign).
 *
 * Every row across the Recent/Bookmarks/Monitored tabs now leads with a
 * small (24px, Regular weight) Fluent v9 icon on the FAR LEFT indicating the
 * row's record type / target kind, replacing the removed type-chip `Badge`
 * pills. This is the ONE place that maps a row's `sprk_pagetype` +
 * `sprk_targetlogicalname` (or the Monitored/Edited-shaped equivalent) to an
 * icon — `RecentTab.tsx`, `BookmarksTab.tsx`, and `MonitoredTab.tsx` all call
 * {@link rowIconFor} rather than each re-deriving their own mapping
 * (CLAUDE.md §11 — a real THIRD consumer of the identical mapping earns the
 * shared extraction, unlike the tabs' still-local `formatEntityLabel`/chip
 * helpers, each of which has its own slightly different label-domain need).
 *
 * Mapping (closed set):
 *   - `EntityRecord` + known `sprk_*` logical name -> a domain-specific icon
 *     (`sprk_matter` -> Briefcase, `sprk_document` -> Document, `sprk_project`
 *     -> Folder, `sprk_event` -> CalendarLtr, `sprk_todo` -> CheckboxChecked,
 *     `sprk_communication` -> Mail).
 *   - `EntityRecord` + any OTHER/unknown logical name -> a generic Document
 *     icon (default record icon).
 *   - `EntityList` (a saved view) -> Navigation (list/grid icon).
 *   - `WebLink` -> Globe.
 *   - `Custom` -> Document (mirrors the generic-record fallback — an
 *     unresolvable custom page has no more specific icon to reach for).
 *
 * Uses `NavItemPageType`'s numeric option-set constants (never the raw
 * `100000000`-style literals) so callers pass the SAME `row.sprk_pagetype`
 * value they already have, with no re-encoding.
 *
 * ADR-021: icon color is `tokens.colorNeutralForeground3` (subtle, matches
 * the removed chip's `subtle`/`informative` tones) — no hardcoded color, so
 * it tracks light/dark automatically via the ambient `FluentProvider`.
 *
 * @see RecentTab.tsx / BookmarksTab.tsx / MonitoredTab.tsx — the three callers
 * @see navItemRepository.ts — `NavItemPageType` constants reused here
 */

import * as React from 'react';
import { tokens } from '@fluentui/react-components';
import {
  Briefcase24Regular,
  CalendarLtr24Regular,
  CheckboxChecked24Regular,
  Document24Regular,
  Folder24Regular,
  Globe24Regular,
  Mail24Regular,
  Navigation24Regular,
} from '@fluentui/react-icons';
import { NavItemPageType } from '@spaarke/ui-components/services/navigator/navItemRepository';

/** A Fluent v9 24px-Regular icon component — the shape every mapped icon shares. */
type RowIconComponent = React.ComponentType<React.SVGAttributes<SVGSVGElement>>;

/** Closed set of `sprk_*` logical names with a domain-specific icon. Any other `EntityRecord` logical name falls back to {@link DEFAULT_RECORD_ICON}. */
const ENTITY_ICON_MAP: Record<string, RowIconComponent> = {
  sprk_matter: Briefcase24Regular,
  sprk_document: Document24Regular,
  sprk_project: Folder24Regular,
  sprk_event: CalendarLtr24Regular,
  sprk_todo: CheckboxChecked24Regular,
  sprk_communication: Mail24Regular,
};

const DEFAULT_RECORD_ICON: RowIconComponent = Document24Regular;

export interface RowIconInput {
  /** One of {@link NavItemPageType} — the row's `sprk_pagetype` (or the Monitored/Edited-shaped equivalent, always `EntityRecord`). */
  pageType: number;
  /** The row's `sprk_targetlogicalname` (or Monitored/Edited item's `targetLogicalName`). Ignored for non-`EntityRecord` pagetypes. */
  logicalName?: string | null;
}

/**
 * Resolve the far-left row icon for a Navigator row. Always returns an
 * element (never `null`) — an unknown/unmapped `EntityRecord` logical name
 * (or a missing one) renders the generic {@link DEFAULT_RECORD_ICON} rather
 * than omitting the icon, so every row keeps consistent left-alignment.
 */
export function rowIconFor({ pageType, logicalName }: RowIconInput): React.ReactElement {
  const iconStyle: React.CSSProperties = { color: tokens.colorNeutralForeground3, flexShrink: 0 };

  switch (pageType) {
    case NavItemPageType.EntityList:
      return <Navigation24Regular style={iconStyle} aria-hidden="true" />;
    case NavItemPageType.WebLink:
      return <Globe24Regular style={iconStyle} aria-hidden="true" />;
    case NavItemPageType.Custom:
      return <Document24Regular style={iconStyle} aria-hidden="true" />;
    case NavItemPageType.EntityRecord:
    default: {
      const Icon = (logicalName && ENTITY_ICON_MAP[logicalName]) || DEFAULT_RECORD_ICON;
      return <Icon style={iconStyle} aria-hidden="true" />;
    }
  }
}

export default rowIconFor;
