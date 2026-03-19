/**
 * CreateProjectWizard barrel export.
 *
 * Provides the reusable Create Project wizard and its supporting types,
 * services, and sub-components. Entity-specific consumers import from here.
 *
 * NOTE: Do NOT add this to the parent components/index.ts barrel — that
 * is handled in a separate task (UDSS-005c).
 */

// ── Main component ──────────────────────────────────────────────────────────
export { CreateProjectWizard, type ICreateProjectWizardProps } from './CreateProjectWizard';

// ── Form types ──────────────────────────────────────────────────────────────
export {
  type ICreateProjectFormState,
  EMPTY_PROJECT_FORM,
} from './projectFormTypes';

// ── Services ────────────────────────────────────────────────────────────────
export {
  ProjectService,
  type ICreateProjectResult,
} from './projectService';

export {
  provisionSecureProject,
  PROVISIONING_STEPS,
  type IProvisionProjectRequest,
  type IProvisionProjectResponse,
  type IProvisionProjectResult,
  type ProvisioningStepKey,
} from './provisioningService';

export {
  closeSecureProject,
  type ICloseProjectRequest,
  type ICloseProjectResponse,
  type ICloseProjectResult,
} from './closureService';

// ── Sub-components ──────────────────────────────────────────────────────────
export { CreateProjectStep, type ICreateProjectStepProps } from './CreateProjectStep';
export { SecureProjectSection, type ISecureProjectSectionProps } from './SecureProjectSection';
export {
  ProvisioningProgressStep,
  type IProvisioningProgressStepProps,
  type IProvisioningStepState,
  type ProvisioningStepStatus,
} from './ProvisioningProgressStep';
export {
  CloseProjectDialog,
  type ICloseProjectDialogProps,
} from './CloseProjectDialog';
