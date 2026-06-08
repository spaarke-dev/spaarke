/**
 * index.ts
 * Public barrel export for the CreateTodoWizard shared library component (R3).
 *
 * Per smart-todo-decoupling-r3 spec FR-15 / OS-1: this wizard creates
 * first-class `sprk_todo` records. The legacy `sprk_event` + `sprk_todoflag=true`
 * model has been retired (no compat shim per NFR-12 / OS-1).
 *
 * Consumer usage:
 *   import { CreateTodoWizard } from './components/CreateTodoWizard';
 */

// Primary entry point — the wizard component
export { TodoWizardDialog as CreateTodoWizard, default } from './TodoWizardDialog';
export type { ICreateTodoWizardProps } from './TodoWizardDialog';

// Entity-specific step component
export { CreateTodoStep } from './CreateTodoStep';
export type { ICreateTodoStepProps } from './CreateTodoStep';

// Service layer
export { TodoService } from './todoService';
export type { ICreateTodoResult } from './todoService';

// Form types
export type { ICreateTodoFormState, IInitialRegarding, AssociationResult } from './formTypes';
export { EMPTY_TODO_FORM } from './formTypes';
