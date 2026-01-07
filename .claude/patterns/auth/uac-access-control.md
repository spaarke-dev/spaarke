# UAC Access Control Pattern

> **Domain**: Authorization, Permission Checking
> **Lines**: ~80
> **Full Reference**: [docs/architecture/uac-access-control.md](../../../docs/architecture/uac-access-control.md)

---

## When to Use

- Implementing authorization checks in endpoint filters
- Adding new operations to `OperationAccessPolicy`
- Understanding why access was denied
- Debugging permission issues

---

## Quick Reference

### Authorization Check Pattern

```csharp
// 1. Create authorization context
var authContext = new AuthorizationContext
{
    UserId = userId,                    // Azure AD OID (from 'oid' claim)
    ResourceId = documentId.ToString(), // Dataverse record ID
    Operation = "read_metadata",        // Must exist in OperationAccessPolicy
    CorrelationId = httpContext.TraceIdentifier
};

// 2. Check authorization
var result = await _authorizationService.AuthorizeAsync(authContext);

// 3. Handle result
if (!result.IsAllowed)
{
    return Results.Problem(
        statusCode: 403,
        title: "Forbidden",
        detail: "Access denied",
        extensions: new Dictionary<string, object?>
        {
            ["reasonCode"] = result.ReasonCode  // e.g., "sdap.access.deny.insufficient_rights"
        });
}
```

### Common Operations

| Operation | Required Rights | Use Case |
|-----------|-----------------|----------|
| `read_metadata` | Read | View document properties, AI analysis |
| `driveitem.preview` | Read | Generate preview |
| `driveitem.content.download` | **Write** | Download file content |
| `driveitem.content.upload` | Write + Create | Upload new file |
| `driveitem.update` | Write | Update properties |
| `driveitem.delete` | Delete | Delete document |
| `driveitem.createlink` | Share | Create sharing link |

### AccessRights Flags

```csharp
[Flags]
public enum AccessRights
{
    None = 0,
    Read = 1,       // Preview, list, view metadata
    Write = 2,      // Modify, download
    Delete = 4,     // Delete items
    Create = 8,     // Create new items
    Append = 16,    // Append to items
    AppendTo = 32,  // Allow appending
    Share = 64      // Share with others
}
```

---

## Key Principles

1. **Use `OperationAccessPolicy` operations** - Don't use generic `"read"` or `"write"`
2. **Fail-closed** - Errors result in deny, never silent allow
3. **Single rule model** - Dataverse `RetrievePrincipalAccess` handles all permission computation
4. **Extract `oid` claim** - Use Azure AD Object ID for Dataverse user lookup

---

## Error Handling

| ReasonCode | Meaning |
|------------|---------|
| `sdap.access.deny.insufficient_rights` | User lacks required AccessRights |
| `sdap.access.deny.unknown_operation` | Operation not in OperationAccessPolicy |
| `sdap.access.deny.no_rule` | No rule made a decision (default deny) |
| `sdap.access.error.system_failure` | Exception during authorization |

---

## DI Registration

```csharp
// In SpaarkeCore.cs
services.AddScoped<IAuthorizationRule, OperationAccessRule>();
services.AddScoped<Spaarke.Core.Auth.AuthorizationService>();
services.AddScoped<IAuthorizationService>(
    sp => sp.GetRequiredService<AuthorizationService>());
```

---

## See Also

- [Full UAC Architecture](../../../docs/architecture/uac-access-control.md) - Complete reference
- [Auth Constraints](../../constraints/auth.md) - MUST/MUST NOT rules
- [Endpoint Filters Pattern](../api/endpoint-filters.md) - Filter implementation
