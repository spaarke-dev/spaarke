/**
 * Privilege + field-security types (consumed by PrivilegeService / FieldSecurityService).
 *
 * The command-system types that used to live here (ICommand, ICommandContext,
 * CommandHandler, ICustomCommandConfig) were DELETED with the rest of the dead
 * UniversalDatasetGrid command cluster (C-19 + #1113, 2026-10-03): their only
 * consumers were CommandRegistry / CommandToolbar / PageChrome CommandBar /
 * useKeyboardShortcuts / CommandExecutor, all deleted in the same change.
 */

/**
 * Entity privilege types from Dataverse AccessRights enum
 * Matches Microsoft.Crm.Sdk.Messages.AccessRights
 * https://learn.microsoft.com/en-us/dotnet/api/microsoft.crm.sdk.messages.accessrights
 */
export enum AccessRights {
  None = 0,
  ReadAccess = 1,
  WriteAccess = 2,
  AppendAccess = 4,
  AppendToAccess = 8,
  CreateAccess = 16,
  DeleteAccess = 32,
  ShareAccess = 64,
  AssignAccess = 128,
}

/**
 * Entity privileges for current user (row-level security)
 */
export interface IEntityPrivileges {
  canCreate: boolean;
  canRead: boolean;
  canWrite: boolean;
  canDelete: boolean;
  canAppend: boolean;
  canAppendTo: boolean;
}

/**
 * Field-level security permissions
 * Matches Dataverse Field Security Profile permissions
 */
export interface IFieldSecurityPermissions {
  canRead: boolean;
  canUpdate: boolean;
  canCreate: boolean;
  canReadUnmasked?: boolean; // For masked data support
}

/**
 * Field security information for a specific column/attribute
 */
export interface IFieldSecurity {
  fieldName: string;
  isSecured: boolean;
  permissions: IFieldSecurityPermissions;
}
