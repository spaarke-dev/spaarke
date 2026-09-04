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

// Assistant hand-off pre-seed mapper (spaarkeai-assistant-enhancements-r1 UAT #1):
// maps a 012 hand-off seed onto the project wizard's initial form values.
export { mapProjectHandoffSeed } from './handoffSeedMapping';

// ── Form types ──────────────────────────────────────────────────────────────
export { type ICreateProjectFormState, EMPTY_PROJECT_FORM } from './projectFormTypes';

// ── Services ────────────────────────────────────────────────────────────────
export { ProjectService, type ICreateProjectResult } from './projectService';

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
// `ProvisioningProgressStep` was exported here until 2026-09-04 and is DELETED. It had zero mounts in
// EITHER tree: barrel-exported but never rendered (CreateProjectWizard imports only
// `provisionSecureProject` and runs provisioning silently inside `onFinish`), and the LegalWorkspace
// twin had no importers at all. Two copies of a component nothing displayed.
// If a visible provisioning step is ever wanted, build ONE against the current wizard — do not
// resurrect this from history; it froze at 2026-03 semantics.
export { CloseProjectDialog, type ICloseProjectDialogProps } from './CloseProjectDialog';
