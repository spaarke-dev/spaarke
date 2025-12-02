/**
 * Service for checking user privileges on entities using Dataverse Web API
 */

import { IEntityPrivileges, AccessRights } from "../types/CommandTypes";

/**
 * Query user's privileges for a specific entity
 */
export class PrivilegeService {
  /**
   * Check user privileges for an entity using RetrievePrincipalAccess
   * @param webAPI - PCF WebAPI instance
   * @param entityLogicalName - Entity logical name (e.g., "account", "sharepointdocument")
   * @param recordId - Optional record ID to check privileges on specific record
   * @returns Entity privileges for current user
   */
  static async getEntityPrivileges(
    webAPI: ComponentFramework.WebApi,
    entityLogicalName: string,
    recordId?: string
  ): Promise<IEntityPrivileges> {
    try {
      // If recordId is provided, use RetrievePrincipalAccess on specific record
      if (recordId) {
        return await this.getRecordPrivileges(webAPI, entityLogicalName, recordId);
      }

      // For entity-level privileges, we need to call a custom action or use WhoAmI + role privileges
      // The most reliable method is to use the dataset.security in dataset-bound mode
      // For headless mode, we'll use a test record approach or assume based on entity metadata

      // Get current user ID
      const whoAmIResponse = await this.executeWhoAmI(webAPI);
      const userId = whoAmIResponse.UserId;

      // Query user's security roles
      const roles = await this.getUserSecurityRoles(webAPI, userId);

      // Query entity privileges for those roles
      const privileges = await this.getEntityPrivilegesFromRoles(
        webAPI,
        entityLogicalName,
        roles
      );

      return privileges;
    } catch (error) {
      console.error(`Error checking privileges for ${entityLogicalName}:`, error);
      // Default to read-only on error
      return this.getNoPrivileges();
    }
  }

  /**
   * Get privileges for a specific record using RetrievePrincipalAccess
   */
  private static async getRecordPrivileges(
    webAPI: ComponentFramework.WebApi,
    entityLogicalName: string,
    recordId: string
  ): Promise<IEntityPrivileges> {
    try {
      // Call RetrievePrincipalAccess bound function on the record
      // https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/reference/retrieveprincipalaccess

      // Build the request URL - RetrievePrincipalAccess is a bound function
      const functionUrl = `${entityLogicalName}(${recordId})/Microsoft.Dynamics.CRM.RetrievePrincipalAccess()`;

      // Execute the function using fetch API
      const response = await this.executeBoundFunction(webAPI, functionUrl);

      if (response && response.AccessRights) {
        return this.parseAccessRights(response.AccessRights);
      }

      return this.getNoPrivileges();
    } catch (error) {
      console.warn(`Could not retrieve principal access for record ${recordId}:`, error);
      return this.getNoPrivileges();
    }
  }

  /**
   * Execute a bound function using the Web API endpoint
   * This is a workaround since PCF WebAPI doesn't expose executeFunction
   */
  private static async executeBoundFunction(
    webAPI: ComponentFramework.WebApi,
    functionUrl: string
  ): Promise<any> {
    // Since PCF WebAPI doesn't expose executeFunction, we need to use window.fetch
    // Get the organization URL from the webAPI context
    const context = (webAPI as any)._context || (webAPI as any).context;
    const apiUrl = context?.page?.getClientUrl?.() || window.location.origin;

    const fullUrl = `${apiUrl}/api/data/v9.2/${functionUrl}`;

    const response = await fetch(fullUrl, {
      method: "GET",
      headers: {
        "Accept": "application/json",
        "Content-Type": "application/json",
        "OData-MaxVersion": "4.0",
        "OData-Version": "4.0"
      },
      credentials: "same-origin"
    });

    if (!response.ok) {
      throw new Error(`Function call failed: ${response.status} ${response.statusText}`);
    }

    return await response.json();
  }

  /**
   * Execute WhoAmI to get current user ID
   */
  private static async executeWhoAmI(webAPI: ComponentFramework.WebApi): Promise<any> {
    try {
      // WhoAmI is an unbound function
      // Since PCF WebAPI doesn't expose executeFunction directly, we use a workaround
      // In PCF context, we can access the user context directly

      // Fallback: Use the userSettings API
      const userSettings = await webAPI.retrieveMultipleRecords(
        "usersettings",
        "?$select=systemuserid&$top=1"
      );

      if (userSettings.entities && userSettings.entities.length > 0) {
        return { UserId: userSettings.entities[0].systemuserid };
      }

      throw new Error("Could not retrieve current user ID");
    } catch (error) {
      console.error("WhoAmI failed:", error);
      throw error;
    }
  }

  /**
   * Get security roles for a user
   */
  private static async getUserSecurityRoles(
    webAPI: ComponentFramework.WebApi,
    userId: string
  ): Promise<string[]> {
    try {
      const response = await webAPI.retrieveMultipleRecords(
        "systemuserroles",
        `?$select=roleid&$filter=systemuserid eq ${userId}`
      );

      return response.entities.map(e => e.roleid);
    } catch (error) {
      console.warn("Could not retrieve user roles:", error);
      return [];
    }
  }

  /**
   * Get entity privileges from security roles
   * Queries roleprivileges and privilege tables to determine aggregate permissions
   */
  private static async getEntityPrivilegesFromRoles(
    webAPI: ComponentFramework.WebApi,
    entityLogicalName: string,
    roleIds: string[]
  ): Promise<IEntityPrivileges> {
    if (roleIds.length === 0) {
      return this.getNoPrivileges();
    }

    try {
      // Step 1: Get entity metadata to find privileges associated with this entity
      const entityMetadata = await webAPI.retrieveMultipleRecords(
        "entitydefinition",
        `?$select=logicalname,objecttypecode&$filter=logicalname eq '${entityLogicalName}'`
      );

      if (!entityMetadata.entities || entityMetadata.entities.length === 0) {
        console.warn(`Entity metadata not found for ${entityLogicalName}`);
        return this.getNoPrivileges();
      }

      // Step 2: Query privileges for this entity type
      // We need to find privilege GUIDs for Create, Read, Write, Delete, Append, AppendTo
      const privilegeFilter = roleIds.map(id => `_roleid_value eq ${id}`).join(" or ");

      const rolePrivileges = await webAPI.retrieveMultipleRecords(
        "roleprivileges",
        `?$select=privilegedepthmask&$expand=privilegeid($select=name,accessright)&$filter=(${privilegeFilter})`
      );

      // Step 3: Aggregate privileges across all roles
      let aggregateRights = AccessRights.None;

      rolePrivileges.entities.forEach((rolePrivilege: any) => {
        const privilege = rolePrivilege.privilegeid;

        // Check if this privilege applies to our entity
        // Privilege names follow pattern: prv{Action}{EntityName}
        // e.g., "prvCreateAccount", "prvReadAccount", "prvWriteAccount"

        if (privilege && privilege.accessright) {
          aggregateRights |= privilege.accessright;
        }
      });

      return this.parseAccessRights(aggregateRights);
    } catch (error) {
      console.warn("Could not retrieve role privileges:", error);

      // Fallback: Return read-only
      return this.getNoPrivileges();
    }
  }

  /**
   * Parse AccessRights value from Dataverse API response
   */
  private static parseAccessRights(accessRightsValue: string | number): IEntityPrivileges {
    let rights = 0;

    if (typeof accessRightsValue === "string") {
      // Parse comma-separated string like "ReadAccess,WriteAccess,CreateAccess"
      const rightsArray = accessRightsValue.split(",").map(r => r.trim());
      rightsArray.forEach(right => {
        if (AccessRights[right as keyof typeof AccessRights] !== undefined) {
          rights |= AccessRights[right as keyof typeof AccessRights];
        }
      });
    } else if (typeof accessRightsValue === "number") {
      rights = accessRightsValue;
    }

    return {
      canCreate: (rights & AccessRights.CreateAccess) === AccessRights.CreateAccess,
      canRead: (rights & AccessRights.ReadAccess) === AccessRights.ReadAccess,
      canWrite: (rights & AccessRights.WriteAccess) === AccessRights.WriteAccess,
      canDelete: (rights & AccessRights.DeleteAccess) === AccessRights.DeleteAccess,
      canAppend: (rights & AccessRights.AppendAccess) === AccessRights.AppendAccess,
      canAppendTo: (rights & AccessRights.AppendToAccess) === AccessRights.AppendToAccess,
    };
  }

  /**
   * Helper to create no-privilege object
   */
  private static getNoPrivileges(): IEntityPrivileges {
    return {
      canCreate: false,
      canRead: true,
      canWrite: false,
      canDelete: false,
      canAppend: false,
      canAppendTo: false,
    };
  }

  /**
   * Get privileges from dataset security object (if available)
   * This is the preferred method when used in dataset-bound mode
   */
  static getPrivilegesFromDataset(dataset: ComponentFramework.PropertyTypes.DataSet): IEntityPrivileges {
    // PCF Dataset has a security property with privilege information
    const security = (dataset as any).security;

    if (security) {
      return {
        canCreate: security.editable && security.createable !== false,
        canRead: security.readable !== false,
        canWrite: security.editable !== false,
        canDelete: security.editable && security.deletable !== false,
        canAppend: security.editable !== false,
        canAppendTo: security.editable !== false,
      };
    }

    // Fallback: if dataset.loading is false and we have no security, assume read-only
    return {
      canCreate: false,
      canRead: true,
      canWrite: false,
      canDelete: false,
      canAppend: false,
      canAppendTo: false,
    };
  }
}
