/**
 * Service for checking user privileges on entities using Dataverse Web API
 */
import { IEntityPrivileges } from '../types/CommandTypes';
/**
 * Query user's privileges for a specific entity
 */
export declare class PrivilegeService {
    /**
     * Check user privileges for an entity using RetrievePrincipalAccess
     * @param webAPI - PCF WebAPI instance
     * @param entityLogicalName - Entity logical name (e.g., "account", "sharepointdocument")
     * @param recordId - Optional record ID to check privileges on specific record
     * @returns Entity privileges for current user
     */
    static getEntityPrivileges(webAPI: ComponentFramework.WebApi, entityLogicalName: string, recordId?: string): Promise<IEntityPrivileges>;
    /**
     * Get privileges for a specific record using RetrievePrincipalAccess
     */
    private static getRecordPrivileges;
    /**
     * Execute a bound function using the Web API endpoint
     * This is a workaround since PCF WebAPI doesn't expose executeFunction
     */
    private static executeBoundFunction;
    /**
     * Execute WhoAmI to get current user ID
     */
    private static executeWhoAmI;
    /**
     * Get security roles for a user
     */
    private static getUserSecurityRoles;
    /**
     * Get entity privileges from security roles
     * Queries roleprivileges and privilege tables to determine aggregate permissions
     */
    private static getEntityPrivilegesFromRoles;
    /**
     * Parse AccessRights value from Dataverse API response
     */
    private static parseAccessRights;
    /**
     * Helper to create no-privilege object
     */
    private static getNoPrivileges;
    /**
     * Get privileges from dataset security object (if available)
     * This is the preferred method when used in dataset-bound mode
     */
    static getPrivilegesFromDataset(dataset: ComponentFramework.PropertyTypes.DataSet): IEntityPrivileges;
}
//# sourceMappingURL=PrivilegeService.d.ts.map