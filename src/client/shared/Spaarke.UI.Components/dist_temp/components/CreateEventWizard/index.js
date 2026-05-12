/**
 * index.ts
 * Public barrel export for the CreateEventWizard shared library component.
 *
 * Consumer usage:
 *   import { CreateEventWizard } from './components/CreateEventWizard';
 */
// Primary entry point -- the wizard component
export { CreateEventWizard, default } from './CreateEventWizard';
// Entity-specific step component
export { CreateEventStep } from './CreateEventStep';
// Service layer
export { EventService } from './eventService';
export { EMPTY_EVENT_FORM } from './formTypes';
//# sourceMappingURL=index.js.map