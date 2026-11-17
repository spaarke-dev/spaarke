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

// No step components are owned here any more. The legacy "Assign Resources" step (inline lookups, no consumer) was retired
// by ontology task 184 (importer audit: none); a wizard person/organization field is DataverseLookupField and a wizard
// email is EmailComposer (.claude/patterns/ui/wizard-side-pane-lookup-and-email-compose.md).
// SendEmailStep was retained past task 021 pending CreateWorkAssignmentWizard's (task 023)
// migration off it — task 023 completed that migration (now uses SendEmailFollowOnStep),
// so the local copy + its exports were removed here (post-021-wave cleanup, 2026-07-08).

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
