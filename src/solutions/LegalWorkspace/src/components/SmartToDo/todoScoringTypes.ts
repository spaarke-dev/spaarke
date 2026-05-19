/**
 * todoScoringTypes.ts
 *
 * Shared type definitions for the To-Do AI Summary dialog and its sub-cards
 * (PriorityScoreCard, EffortScoreCard). Previously these types lived in the
 * useTodoScoring hook; that hook was unused (no call sites) and was removed
 * during the auth v2 cleanup. The types are preserved here so the existing
 * dialog scaffolding still type-checks; a new hook that consumes
 * @spaarke/auth's authenticatedFetch can re-introduce the data path later.
 */

/** A single priority factor contributing to the priority score */
export interface ITodoScoringPriorityFactor {
  /** Display name of the factor */
  name: string;
  /** Formatted value string (e.g. "12 days", "87%", "2 grades") */
  value: string;
  /** Points contributed (0-max per factor) */
  points: number;
}

/** A complexity multiplier that may apply to the effort score */
export interface ITodoScoringMultiplier {
  /** Display name of the multiplier */
  name: string;
  /** Multiplier value (e.g. 1.3) */
  value: number;
  /** Whether this multiplier was applied to the current event */
  applied: boolean;
}

/** Priority scoring result */
export interface ITodoPriorityScore {
  /** Aggregate priority score (0-100, capped) */
  score: number;
  /** Derived level from score */
  level: 'Urgent' | 'High' | 'Normal' | 'Low';
  /** Breakdown of contributing factors */
  factors: ITodoScoringPriorityFactor[];
}

/** Effort scoring result */
export interface ITodoEffortScore {
  /** Aggregate effort score (0-100, capped) */
  score: number;
  /** Derived level from score */
  level: 'High' | 'Med' | 'Low';
  /** Base effort before multipliers are applied */
  baseEffort: number;
  /** All possible multipliers with applied flag */
  multipliers: ITodoScoringMultiplier[];
}

/** Suggested action returned in the scoring result */
export interface ITodoScoringAction {
  /** Display label for the action button */
  label: string;
  /** Icon name key (resolved locally by the dialog) */
  icon: 'ArrowUpRegular' | 'PersonSwapRegular' | 'MoneyRegular' | 'TaskListSquareRegular' | 'FolderOpenRegular';
}

/** Full scoring result from BFF or mock */
export interface ITodoScoringResult {
  /** Priority score with factor breakdown */
  priority: ITodoPriorityScore;
  /** Effort score with multiplier list */
  effort: ITodoEffortScore;
  /** AI-generated analysis text */
  analysis: string;
  /** Suggested actions for the user */
  suggestedActions: ITodoScoringAction[];
  /** Whether this result came from mock data (true) or the live BFF (false) */
  isMockData: boolean;
}

/** Context passed to openScoring to identify the event */
export interface ITodoScoringEventContext {
  eventId: string;
  eventTitle: string;
}
