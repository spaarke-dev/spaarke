/**
 * Command system types
 */
import { IDatasetRecord } from './DatasetTypes';
/**
 * Command context provided to command handlers
 */
export interface ICommandContext {
    selectedRecords: IDatasetRecord[];
    entityName: string;
    webAPI: ComponentFramework.WebApi;
    navigation: ComponentFramework.Navigation;
    refresh?: () => void;
    parentRecord?: ComponentFramework.EntityReference;
    emitLastAction?: (action: string) => void;
}
/**
 * Command handler function signature
 */
export type CommandHandler = (context: ICommandContext) => Promise<void>;
/**
 * Command definition
 */
export interface ICommand {
    key: string;
    label: string;
    icon?: React.ReactElement;
    handler: CommandHandler;
    requiresSelection?: boolean;
    multiSelectSupport?: boolean;
    confirmationMessage?: string;
    group?: 'primary' | 'secondary' | 'overflow';
    description?: string;
    keyboardShortcut?: string;
    iconOnly?: boolean;
    dividerAfter?: boolean;
    refresh?: boolean;
    successMessage?: string;
}
/**
 * Custom command configuration (from manifest)
 */
export interface ICustomCommandConfig {
    key: string;
    label: string;
    actionType: 'workflow' | 'customapi' | 'action' | 'function';
    actionName: string;
    icon?: string;
    requiresSelection?: boolean;
}
/**
 * Entity privilege types from Dataverse AccessRights enum
 * Matches Microsoft.Crm.Sdk.Messages.AccessRights
 * https://learn.microsoft.com/en-us/dotnet/api/microsoft.crm.sdk.messages.accessrights
 */
export declare enum AccessRights {
    None = 0,
    ReadAccess = 1,
    WriteAccess = 2,
    AppendAccess = 4,
    AppendToAccess = 8,
    CreateAccess = 16,
    DeleteAccess = 32,
    ShareAccess = 64,
    AssignAccess = 128
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
    canReadUnmasked?: boolean;
}
/**
 * Field security information for a specific column/attribute
 */
export interface IFieldSecurity {
    fieldName: string;
    isSecured: boolean;
    permissions: IFieldSecurityPermissions;
}
//# sourceMappingURL=CommandTypes.d.ts.map