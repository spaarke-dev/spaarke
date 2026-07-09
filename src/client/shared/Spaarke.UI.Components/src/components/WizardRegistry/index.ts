/**
 * index.ts
 * Public barrel export for the WizardRegistry shared library module.
 *
 * Consumer usage:
 *   import { resolveWizard, type WizardHostProps } from '@spaarke/ui-components';
 */
export { wizardRegistry, resolveWizard } from './wizardRegistry';
export type { WizardComponent, WizardHostProps } from './wizardRegistry';
