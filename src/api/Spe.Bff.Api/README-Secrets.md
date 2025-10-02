# Local Development Secrets Setup

## Initialize User Secrets

Run from the `src/api/Spe.Bff.Api` directory:

```bash
dotnet user-secrets init
```

## Set Required Secrets

### Graph API
```bash
dotnet user-secrets set "Graph:ClientSecret" "your-app-client-secret"
```

### Dataverse
```bash
dotnet user-secrets set "Dataverse:ClientSecret" "your-dataverse-client-secret"
```

### Service Bus
```bash
dotnet user-secrets set "ServiceBus:ConnectionString" "Endpoint=sb://your-servicebus.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=your-key"
```

### (Optional) Redis - Only if Redis:Enabled = true
```bash
dotnet user-secrets set "Redis:ConnectionString" "localhost:6379"
```

## Verify Secrets

```bash
dotnet user-secrets list
```

## Alternative: Environment Variables

You can also set these via environment variables (useful for Docker):

```bash
export Graph__ClientSecret="your-secret"
export Dataverse__ClientSecret="your-secret"
export ServiceBus__ConnectionString="your-connection-string"
```

**Note**: Use double underscores `__` for nested configuration in env vars.

## Required Secrets for Spaarke Development

Based on the current `appsettings.Development.json`, you need:

1. **Graph:ClientSecret** - Client secret from Azure AD app registration `170c98e1-d486-4355-bcbe-170454e0207c`
2. **Dataverse:ClientSecret** - Client secret from the same app registration (or separate Dataverse app)
3. **ServiceBus:ConnectionString** - Connection string to your development Service Bus namespace

## Getting Secrets from Azure

### Graph/Dataverse Client Secret
1. Go to Azure Portal → Azure Active Directory → App Registrations
2. Find app `170c98e1-d486-4355-bcbe-170454e0207c`
3. Go to "Certificates & secrets"
4. Create new client secret or use existing
5. Copy the secret value (shown only once!)

### Service Bus Connection String
1. Go to Azure Portal → Service Bus namespace
2. Go to "Shared access policies"
3. Click on "RootManageSharedAccessKey" (or create new policy)
4. Copy "Primary Connection String"

## Troubleshooting

### "Configuration validation failed" on startup
- Check that all required secrets are set
- Use `dotnet user-secrets list` to verify
- Ensure secret keys match exactly (case-sensitive)

### "Graph:ClientSecret is required when ManagedIdentity is disabled"
- You're running in local dev mode (ManagedIdentity.Enabled = false)
- Set the Graph:ClientSecret via user-secrets or environment variable

### Secrets not loading
- Ensure you ran `dotnet user-secrets init` from the correct directory
- Check that `.csproj` has `<UserSecretsId>` element
- Try deleting and re-adding secrets
