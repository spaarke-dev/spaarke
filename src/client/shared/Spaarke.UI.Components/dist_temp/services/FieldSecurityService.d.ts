/**
 * Service for checking field-level security (column-level security)
 * Uses Dataverse Field Security Profiles
 */
import { IFieldSecurity } from '../types/CommandTypes';
/**
 * Field security service for column-level permissions
 */
export declare class FieldSecurityService {
    /**
     * Get field security permissions for all fields in an entity
     * @param webAPI - PCF WebAPI instance
     * @param entityLogicalName - Entity logical name
     * @param fieldNames - Array of field logical names to check
     * @returns Map of field name to security permissions
     */
    static getFieldSecurityPermissions(webAPI: ComponentFramework.WebApi, entityLogicalName: string, fieldNames: string[]): Promise<Map<string, IFieldSecurity>>;
    /**
     * Get current user ID from usersettings
     */
    private static getCurrentUserId;
    /**
     * Get field security profiles assigned to the user
     */
    private static getUserFieldSecurityProfiles;
    /**
     * Get field security metadata to determine which fields are secured
     */
    private static getFieldSecurityMetadata;
    /**
     * Get permissions for a specific field based on user's field security profiles
     */
    private static getFieldPermissions;
    /**
     * Get field security from dataset column metadata (dataset-bound mode)
     * This is the preferred method when available
     */
    static getFieldSecurityFromDataset(dataset: ComponentFramework.PropertyTypes.DataSet): Map<string, IFieldSecurity>;
}
//# sourceMappingURL=FieldSecurityService.d.ts.map