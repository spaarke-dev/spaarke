/**
 * Worklist row contract types — the data a MatterCard renders.
 *
 * Shaped like the BUILT schema (task 007), never v4's mock types: one `WorklistItem` is one `sprk_signal` row as the
 * BFF Signal read route returns it (display columns only); one `WorklistCore` is the catalog-driven identity of the
 * item's CORE record (`sprk_corerecordtype` / `sprk_corerecordid`, D-34/D-36). The component computes neither membership
 * nor rank: it renders the order it is given (row-contract requirement 2).
 *
 * Task: spaarke-ontology-platform-r1, task 051.
 */

/** `sprk_signal.sprk_lane`: Decide (100000000) always above Do (100000001); the route maps the option value. */
export type WorklistLane = 'Decide' | 'Do';

/** Wording for a Do item past its due date. v4: tasks read "5d overdue", work assignments "3d late". */
export type PastDueWording = 'overdue' | 'late';

/** One Work Item: the display columns of one `sprk_signal` row. */
export interface WorklistItem {
  /** `sprk_signalid` — the object this line resolves to (row-contract requirement 1). */
  signalId: string;
  /** `sprk_lane`. Decide lines show age; Do lines show the due state. */
  lane: WorklistLane;
  /** `sprk_regardingrecordname` — the subject's name (the denormalized trio written by the resolver). */
  subjectName?: string | null;
  /** `sprk_name` — the Signal's stored short headline; shown only when the subject name is missing. */
  title?: string | null;
  /** The policy's `sprk_shortname` (the rule's display name). */
  ruleShortName?: string | null;
  /** `sprk_firstdetected` (ISO 8601) — the age of a Decide item. */
  raisedOn?: string | null;
  /** `sprk_duedate` (Date Only, `YYYY-MM-DD`) — the due state of a Do item. */
  dueDate?: string | null;
  /** Supplied by the route from the policy's work type; defaults to `overdue`. */
  pastDueWording?: PastDueWording | null;
}

/**
 * The core record a card groups under — matter, project, work assignment, service request or a future type.
 * Label, name and number come from the route (catalog-driven, D-36); the component has no per-type branch.
 */
export interface WorklistCore {
  /** `sprk_corerecordtype` — the catalog row's logical name (for example `sprk_matter`). */
  recordType: string;
  /** `sprk_corerecordid`. */
  recordId: string;
  /** The catalog's display name for the type (for example "Matter"). */
  typeLabel?: string | null;
  /** The record's name (the catalog's display-name field). */
  name?: string | null;
  /** The record's number (the catalog's number field). */
  number?: string | null;
}

/** Raised when a line is activated. Consumed by the decision wizard host (tasks 058 / 059). */
export interface OpenItemEvent {
  /** The Signal id of the activated line. */
  itemId: string;
  /** The card's core record; `null` for a "Not filed" card (D-35). */
  core: { recordType: string; recordId: string } | null;
  /** The ids of every item visible in the list, in the order shown — the wizard's browse set. */
  visibleList: readonly string[];
}
