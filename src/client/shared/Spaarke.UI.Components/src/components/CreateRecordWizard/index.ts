/**
 * CreateRecordWizard barrel export.
 *
 * Provides the reusable multi-step record creation wizard and its
 * supporting types. Entity-specific wizards import from here to
 * get the orchestration layer.
 */

// Main component
export { CreateRecordWizard } from './CreateRecordWizard';

// The "Next Steps" follow-on card grid + reusable follow-on steps
// (AssignWork / CreateEvent / SendEmail / DraftSummary / RecipientField) now
// live in the shared WizardFollowOns module (design.md §5.9). CreateRecordWizard
// consumes them directly; the local FollowOnSteps.tsx + those step copies were
// deleted in task 021. Import follow-on symbols from '@spaarke/ui-components'
// (the root barrel re-exports WizardFollowOns) or from the WizardFollowOns barrel.

// Step components still owned here (NOT migrated to WizardFollowOns):
//   - AssignResourcesStep: legacy "Assign Resources" step with no WizardFollowOns successor.
// SendEmailStep was retained past task 021 pending CreateWorkAssignmentWizard's (task 023)
// migration off it — task 023 completed that migration (now uses SendEmailFollowOnStep),
// so the local copy + its exports were removed here (post-021-wave cleanup, 2026-07-08).
export { AssignResourcesStep } from './steps/AssignResourcesStep';

// Types
export type {
  ICreateRecordWizardConfig,
  ICreateRecordWizardProps,
  IFinishContext,
  IFollowOnState,
  IAssignWorkFollowOnState,
  ICreateEventFollowOnState,
  IEntityInfoStep,
  IAssociateToStepConfig,
  FollowOnActionId,
  SearchCallback,
  AssociationResult,
  EntityTypeOption,
} from './types';

// Step prop types
export type { IAssignResourcesStepProps } from './steps/AssignResourcesStep';
