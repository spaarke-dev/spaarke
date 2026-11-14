/**
 * RECORD_ACTION_ICONS — the one icon map for record-level actions.
 *
 * FR-05 (spaarke-SPA-external-access-platform-r4): every record action uses
 * the 20Regular icon below, from `@fluentui/react-icons`. Keys are action ids,
 * not entity names. The map is frozen; add an entry only through a reviewed
 * change (the closing phase proposes this map as a repo standard, FR-34).
 *
 * Usage:
 *   const RefreshIcon = RECORD_ACTION_ICONS.refresh;
 *   { id: 'refresh', icon: <RefreshIcon />, ... }
 *
 * Fluent icons render with `currentColor`, so they follow the theme
 * (light, dark, high contrast) without any colour styling (ADR-021).
 */

import {
  ArrowClockwise20Regular,
  ArrowDownload20Regular,
  ArrowUpload20Regular,
  CalendarAdd20Regular,
  ChatAdd20Regular,
  Delete20Regular,
  History20Regular,
  Link20Regular,
  Open20Regular,
  ReceiptAdd20Regular,
  Send20Regular,
  TaskListAdd20Regular,
} from '@fluentui/react-icons';

export const RECORD_ACTION_ICONS = Object.freeze({
  refresh: ArrowClockwise20Regular,
  upload: ArrowUpload20Regular,
  newMessage: ChatAdd20Regular,
  addTodo: TaskListAdd20Regular,
  addEvent: CalendarAdd20Regular,
  submitInvoice: ReceiptAdd20Regular,
  copyLink: Link20Regular,
  expandSection: Open20Regular,
  download: ArrowDownload20Regular,
  delete: Delete20Regular,
  history: History20Regular,
  send: Send20Regular,
} as const);

/** An action id that has an icon in {@link RECORD_ACTION_ICONS}. */
export type RecordActionIconId = keyof typeof RECORD_ACTION_ICONS;
