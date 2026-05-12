/**
 * eventService.ts
 * Event creation service for the Create New Event wizard.
 *
 * Creates a sprk_event record in Dataverse via IDataService.
 * Follows the same nav-prop discovery pattern as ProjectService.
 *
 * @see IDataService — high-level data access abstraction (no IWebApi dependency)
 */
import type { ICreateEventFormState } from './formTypes';
import type { ILookupItem } from '../../types/LookupTypes';
import type { IDataService } from '../../types/serviceInterfaces';
export interface ICreateEventResult {
    eventId?: string;
    eventName?: string;
    success: boolean;
    errorMessage?: string;
}
export declare class EventService {
    private readonly _dataService;
    constructor(_dataService: IDataService);
    /**
     * Search sprk_eventtype_ref records by name fragment.
     */
    searchEventTypes(nameFilter: string): Promise<ILookupItem[]>;
    /**
     * Create a sprk_event record in Dataverse.
     *
     * @param formValues - Event form state. When `regardingRecordId` is set and
     *   `regardingEntityName` is provided, the event is linked to the parent
     *   matter or project via the appropriate nav-prop discovered at runtime.
     * @param regardingEntityName - Optional logical name of the parent entity
     *   (e.g. 'sprk_matter', 'sprk_project'). When supplied together with
     *   `formValues.regardingRecordId`, the event is associated via the
     *   N:1 relationship nav-prop resolved through metadata discovery.
     */
    createEvent(formValues: ICreateEventFormState, regardingEntityName?: string): Promise<ICreateEventResult>;
}
//# sourceMappingURL=eventService.d.ts.map