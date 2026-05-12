/**
 * projectService.ts
 * Project creation service for the Create New Project wizard.
 *
 * Responsibilities:
 *   1. Search reference tables (project types, practice areas, contacts)
 *   2. Create sprk_project Dataverse record via IDataService
 *
 * Follows the same patterns as CreateMatter/matterService.ts:
 *   - Navigation property discovery via EntityDefinitions metadata API
 *   - OData @odata.bind syntax for lookup bindings
 *   - Cached nav-prop map per entity
 *   - Never throws — returns result object with status/errorMessage
 *
 * @see IDataService — high-level data access abstraction (no IWebApi dependency)
 */
import type { ICreateProjectFormState } from './projectFormTypes';
import type { ILookupItem } from '../../types/LookupTypes';
import type { IDataService } from '../../types/serviceInterfaces';
export interface ICreateProjectResult {
    /** The GUID of the created sprk_project record (present on success). */
    projectId?: string;
    /** Display name of the created project. */
    projectName?: string;
    /** Whether the creation succeeded. */
    success: boolean;
    /** Human-readable error message (set when success is false). */
    errorMessage?: string;
}
export declare class ProjectService {
    private readonly _dataService;
    constructor(_dataService: IDataService);
    /**
     * Search sprk_projecttype_ref records by name fragment.
     * Returns up to 10 matching project types as ILookupItem.
     */
    searchProjectTypes(nameFilter: string): Promise<ILookupItem[]>;
    /**
     * Search sprk_practicearea_ref records by name fragment.
     * Returns up to 10 matching practice areas as ILookupItem.
     */
    searchPracticeAreas(nameFilter: string): Promise<ILookupItem[]>;
    /**
     * Search contact records by name fragment.
     * Returns up to 10 matching contacts as ILookupItem.
     * Name format: "Full Name (email)" for disambiguation.
     */
    searchContacts(nameFilter: string): Promise<ILookupItem[]>;
    /**
     * Search sprk_organization records by name fragment.
     * Returns up to 10 matching organizations as ILookupItem.
     */
    searchOrganizations(nameFilter: string): Promise<ILookupItem[]>;
    /**
     * Create a sprk_project record in Dataverse.
     *
     * Uses navigation property discovery to resolve the correct OData
     * @odata.bind syntax for each lookup field.
     *
     * Returns ICreateProjectResult — never throws.
     */
    createProject(formValues: ICreateProjectFormState): Promise<ICreateProjectResult>;
}
//# sourceMappingURL=projectService.d.ts.map