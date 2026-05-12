/**
 * Command system types
 */
/**
 * Entity privilege types from Dataverse AccessRights enum
 * Matches Microsoft.Crm.Sdk.Messages.AccessRights
 * https://learn.microsoft.com/en-us/dotnet/api/microsoft.crm.sdk.messages.accessrights
 */
export var AccessRights;
(function (AccessRights) {
    AccessRights[AccessRights["None"] = 0] = "None";
    AccessRights[AccessRights["ReadAccess"] = 1] = "ReadAccess";
    AccessRights[AccessRights["WriteAccess"] = 2] = "WriteAccess";
    AccessRights[AccessRights["AppendAccess"] = 4] = "AppendAccess";
    AccessRights[AccessRights["AppendToAccess"] = 8] = "AppendToAccess";
    AccessRights[AccessRights["CreateAccess"] = 16] = "CreateAccess";
    AccessRights[AccessRights["DeleteAccess"] = 32] = "DeleteAccess";
    AccessRights[AccessRights["ShareAccess"] = 64] = "ShareAccess";
    AccessRights[AccessRights["AssignAccess"] = 128] = "AssignAccess";
})(AccessRights || (AccessRights = {}));
//# sourceMappingURL=CommandTypes.js.map