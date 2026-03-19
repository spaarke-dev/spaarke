/**
 * index.ts
 * Public barrel export for the CreateEventWizard shared library component.
 *
 * Consumer usage:
 *   import { CreateEventWizard } from './components/CreateEventWizard';
 */

// Primary entry point -- the wizard component
export { CreateEventWizard, default } from './CreateEventWizard';
export type { ICreateEventWizardProps } from './CreateEventWizard';

// Entity-specific step component
export { CreateEventStep } from './CreateEventStep';
export type { ICreateEventStepProps } from './CreateEventStep';

// Service layer
export { EventService } from './eventService';
export type { ICreateEventResult } from './eventService';

// Form types
export type { ICreateEventFormState } from './formTypes';
export { EMPTY_EVENT_FORM } from './formTypes';
