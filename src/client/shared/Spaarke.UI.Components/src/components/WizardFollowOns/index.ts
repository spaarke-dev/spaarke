/**
 * WizardFollowOns barrel export.
 *
 * The single, config-driven home for the "Next Steps" follow-on card grid and
 * the reusable follow-on step components shared across every Spaarke create
 * wizard (design.md §5.9, visual-host-create-button-r1). Replaces the four
 * duplicated Next-Steps / SendEmail implementations (CreateRecordWizard,
 * CreateWorkAssignmentWizard, CreateMatterWizard, SummarizeFilesWizard) which
 * are migrated onto this module and deleted per-wizard in tasks 021–024.
 *
 * `DraftSummaryFollowOnStep` (+ its internal `RecipientField` and the
 * `IRecipientItem` type) was DELETED 2026-10-03 (reuse audit C-26): never
 * mounted by any wizard. CreateMatterWizard's draft-summary step is its own
 * `CreateMatterWizard/DraftSummaryStep.tsx`.
 */

// Config contract + canonical maps
export type { FollowOnCardConfig, FollowOnId } from './followOnTypes';
export { FOLLOW_ON_ID_MAP, FOLLOW_ON_LABEL_MAP, FOLLOW_ON_CANONICAL_ORDER, followOnStepId } from './followOnTypes';

// The config-driven card grid
export { FollowOnGrid } from './FollowOnGrid';
export type { IFollowOnGridProps } from './FollowOnGrid';

// Reusable follow-on steps
export { SendEmailFollowOnStep } from './steps/SendEmailFollowOnStep';
export type { ISendEmailFollowOnStepProps } from './steps/SendEmailFollowOnStep';

export { AssignWorkFollowOnStep, WORK_ASSIGNMENT_PRIORITY } from './steps/AssignWorkFollowOnStep';
export type { IAssignWorkFollowOnStepProps, WorkAssignmentPriorityValue } from './steps/AssignWorkFollowOnStep';

export { CreateEventFollowOnStep } from './steps/CreateEventFollowOnStep';
export type { ICreateEventFollowOnStepProps } from './steps/CreateEventFollowOnStep';

// Net-new: Add To Do follow-on (step + create handler)
export { AddTodoFollowOnStep, createTodoRegardingChild } from './steps/AddTodoFollowOnStep';
export type { IAddTodoFollowOnStepProps, CreatedChildRef } from './steps/AddTodoFollowOnStep';
