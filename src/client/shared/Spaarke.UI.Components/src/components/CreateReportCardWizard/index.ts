/**
 * index.ts
 * Public barrel export for the CreateReportCardWizard shared library component.
 *
 * Consumer usage:
 *   import { CreateReportCardWizard } from './components/CreateReportCardWizard';
 */

// Primary entry point -- the wizard component
export { CreateReportCardWizard, default, resolveReportCardAssociateToStepConfig } from './CreateReportCardWizard';
export type { ICreateReportCardWizardProps } from './CreateReportCardWizard';

// Entity-specific step component
export { CreateReportCardStep } from './CreateReportCardStep';
export type { ICreateReportCardStepProps } from './CreateReportCardStep';

// Service layer
export { ReportCardService } from './reportCardService';
export type { ICreateReportCardResult } from './reportCardService';

// Form types
export type { ICreateReportCardFormState } from './formTypes';
export { EMPTY_REPORTCARD_FORM, buildEmptyReportCardForm } from './formTypes';
