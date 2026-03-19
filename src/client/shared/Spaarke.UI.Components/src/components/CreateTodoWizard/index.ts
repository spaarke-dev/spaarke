/**
 * index.ts
 * Public barrel export for the CreateTodoWizard shared library component.
 *
 * Consumer usage:
 *   import { CreateTodoWizard } from './components/CreateTodoWizard';
 */

// Primary entry point -- the wizard component
export { TodoWizardDialog as CreateTodoWizard, default } from './TodoWizardDialog';
export type { ICreateTodoWizardProps } from './TodoWizardDialog';

// Entity-specific step component
export { CreateTodoStep } from './CreateTodoStep';
export type { ICreateTodoStepProps } from './CreateTodoStep';

// Service layer
export { TodoService } from './todoService';
export type { ICreateTodoResult } from './todoService';

// Form types
export type { ICreateTodoFormState } from './formTypes';
export { EMPTY_TODO_FORM } from './formTypes';
