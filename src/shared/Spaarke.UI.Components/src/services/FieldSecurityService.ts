/**
 * Service for checking field-level security (column-level security)
 * Uses Dataverse Field Security Profiles
 */

import { IFieldSecurity } from "../types/CommandTypes";

/**
 * Field security service for column-level permissions
 */
export class FieldSecurityService {
  /**
   * Get field security permissions for all fields in an entity
   * @param webAPI - PCF WebAPI instance
   * @param entityLogicalName - Entity logical name
   * @param fieldNames - Array of field logical names to check
   * @returns Map of field name to security permissions
   */
  static async getFieldSecurityPermissions(
    webAPI: ComponentFramework.WebApi,
    entityLogicalName: string,
    fieldNames: string[]
  ): Promise<Map<string, IFieldSecurity>> {
    const fieldSecurityMap = new Map<string, IFieldSecurity>();

    try {
      // Step 1: Get current user ID
      const userId = await this.getCurrentUserId(webAPI);

      // Step 2: Query field security profiles for the user
      const userProfiles = await this.getUserFieldSecurityProfiles(webAPI, userId);

      if (userProfiles.length === 0) {
        // User has no field security profiles - all secured fields are inaccessible
        return await this.getFieldSecurityMetadata(webAPI, entityLogicalName, fieldNames);
      }

      // Step 3: Query field permissions for each field
      for (const fieldName of fieldNames) {
        const fieldSecurity = await this.getFieldPermissions(
          webAPI,
          entityLogicalName,
          fieldName,
          userProfiles
        );
        fieldSecurityMap.set(fieldName, fieldSecurity);
      }

      return fieldSecurityMap;
    } catch (error) {
      console.error("Error retrieving field security permissions:", error);

      // Default: assume all fields are readable but not updateable
      fieldNames.forEach(fieldName => {
        fieldSecurityMap.set(fieldName, {
          fieldName,
          isSecured: false,
          permissions: {
            canRead: true,
            canUpdate: true,
            canCreate: true
          }
        });
      });

      return fieldSecurityMap;
    }
  }

  /**
   * Get current user ID from usersettings
   */
  private static async getCurrentUserId(webAPI: ComponentFramework.WebApi): Promise<string> {
    const userSettings = await webAPI.retrieveMultipleRecords(
      "usersettings",
      "?$select=systemuserid&$top=1"
    );

    if (userSettings.entities && userSettings.entities.length > 0) {
      return userSettings.entities[0].systemuserid;
    }

    throw new Error("Could not retrieve current user ID");
  }

  /**
   * Get field security profiles assigned to the user
   */
  private static async getUserFieldSecurityProfiles(
    webAPI: ComponentFramework.WebApi,
    userId: string
  ): Promise<string[]> {
    try {
      // Query systemuserprofiles association
      const response = await webAPI.retrieveMultipleRecords(
        "systemuserprofiles",
        `?$select=fieldsecurityprofileid&$filter=systemuserid eq ${userId}`
      );

      return response.entities.map(e => e.fieldsecurityprofileid);
    } catch (error) {
      console.warn("Could not retrieve user field security profiles:", error);
      return [];
    }
  }

  /**
   * Get field security metadata to determine which fields are secured
   */
  private static async getFieldSecurityMetadata(
    webAPI: ComponentFramework.WebApi,
    entityLogicalName: string,
    fieldNames: string[]
  ): Promise<Map<string, IFieldSecurity>> {
    const fieldSecurityMap = new Map<string, IFieldSecurity>();

    try {
      // Query attribute metadata to check IsSecured property
      const filter = fieldNames.map(f => `LogicalName eq '${f}'`).join(" or ");

      const attributes = await webAPI.retrieveMultipleRecords(
        "attributedefinition",
        `?$select=logicalname,issecured&$filter=EntityLogicalName eq '${entityLogicalName}' and (${filter})`
      );

      attributes.entities.forEach((attr: any) => {
        const isSecured = attr.issecured === true;
        fieldSecurityMap.set(attr.logicalname, {
          fieldName: attr.logicalname,
          isSecured: isSecured,
          permissions: {
            canRead: !isSecured, // If secured and no profile, cannot read
            canUpdate: !isSecured,
            canCreate: !isSecured
          }
        });
      });

      // For fields not found in metadata, assume not secured
      fieldNames.forEach(fieldName => {
        if (!fieldSecurityMap.has(fieldName)) {
          fieldSecurityMap.set(fieldName, {
            fieldName,
            isSecured: false,
            permissions: {
              canRead: true,
              canUpdate: true,
              canCreate: true
            }
          });
        }
      });

      return fieldSecurityMap;
    } catch (error) {
      console.warn("Could not retrieve field security metadata:", error);

      // Default: all fields accessible
      fieldNames.forEach(fieldName => {
        fieldSecurityMap.set(fieldName, {
          fieldName,
          isSecured: false,
          permissions: {
            canRead: true,
            canUpdate: true,
            canCreate: true
          }
        });
      });

      return fieldSecurityMap;
    }
  }

  /**
   * Get permissions for a specific field based on user's field security profiles
   */
  private static async getFieldPermissions(
    webAPI: ComponentFramework.WebApi,
    entityLogicalName: string,
    fieldName: string,
    profileIds: string[]
  ): Promise<IFieldSecurity> {
    try {
      // Step 1: Check if field is secured
      const attributeMetadata = await webAPI.retrieveMultipleRecords(
        "attributedefinition",
        `?$select=logicalname,issecured&$filter=EntityLogicalName eq '${entityLogicalName}' and LogicalName eq '${fieldName}'`
      );

      if (!attributeMetadata.entities || attributeMetadata.entities.length === 0) {
        // Field not found - assume not secured
        return {
          fieldName,
          isSecured: false,
          permissions: {
            canRead: true,
            canUpdate: true,
            canCreate: true
          }
        };
      }

      const isSecured = attributeMetadata.entities[0].issecured === true;

      if (!isSecured) {
        // Field is not secured - full access
        return {
          fieldName,
          isSecured: false,
          permissions: {
            canRead: true,
            canUpdate: true,
            canCreate: true
          }
        };
      }

      // Step 2: Query field permissions from fieldsecurityprofile
      const attributeId = attributeMetadata.entities[0].metadataid;
      const profileFilter = profileIds.map(id => `_fieldsecurityprofileid_value eq ${id}`).join(" or ");

      const permissions = await webAPI.retrieveMultipleRecords(
        "fieldpermission",
        `?$select=canread,canupdate,cancreate,canreadunmasked&$filter=_attributeid_value eq ${attributeId} and (${profileFilter})`
      );

      // Aggregate permissions across all profiles (OR logic - if any profile grants, user has permission)
      // Permission values per Microsoft documentation:
      // field_security_permission_type: 0 = Not Allowed, 4 = Allowed
      // field_security_permission_readunmasked: 0 = Not Allowed, 1 = One Record, 3 = All Records
      let canRead = false;
      let canUpdate = false;
      let canCreate = false;
      let canReadUnmasked = false;

      permissions.entities.forEach((perm: any) => {
        // Check for value 4 (Allowed) per Microsoft field_security_permission_type choice
        if (perm.canread === 4 || perm.canread === true) canRead = true;
        if (perm.canupdate === 4 || perm.canupdate === true) canUpdate = true;
        if (perm.cancreate === 4 || perm.cancreate === true) canCreate = true;

        // Check canreadunmasked: 1 (One Record) or 3 (All Records) means unmasked allowed
        if (perm.canreadunmasked === 1 || perm.canreadunmasked === 3) canReadUnmasked = true;
      });

      return {
        fieldName,
        isSecured: true,
        permissions: {
          canRead,
          canUpdate,
          canCreate,
          canReadUnmasked
        }
      };
    } catch (error) {
      console.warn(`Could not retrieve field permissions for ${fieldName}:`, error);

      // Default to no access for secured fields on error
      return {
        fieldName,
        isSecured: true,
        permissions: {
          canRead: false,
          canUpdate: false,
          canCreate: false
        }
      };
    }
  }

  /**
   * Get field security from dataset column metadata (dataset-bound mode)
   * This is the preferred method when available
   */
  static getFieldSecurityFromDataset(
    dataset: ComponentFramework.PropertyTypes.DataSet
  ): Map<string, IFieldSecurity> {
    const fieldSecurityMap = new Map<string, IFieldSecurity>();

    // PCF dataset columns have security information
    dataset.columns.forEach(column => {
      const security = (column as any).security;

      if (security) {
        fieldSecurityMap.set(column.name, {
          fieldName: column.name,
          isSecured: security.secured === true,
          permissions: {
            canRead: security.readable !== false,
            canUpdate: security.editable !== false,
            canCreate: security.editable !== false
          }
        });
      } else {
        // No security metadata - assume accessible
        fieldSecurityMap.set(column.name, {
          fieldName: column.name,
          isSecured: false,
          permissions: {
            canRead: true,
            canUpdate: true,
            canCreate: true
          }
        });
      }
    });

    return fieldSecurityMap;
  }
}
