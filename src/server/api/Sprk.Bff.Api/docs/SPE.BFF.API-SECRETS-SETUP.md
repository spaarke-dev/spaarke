# Local Development Secrets Setup

> **🔴 REWRITTEN 2026-08-24 — `spaarke-auth-v4-dataverse-MI` task 033.**
>
> The previous version of this page was wrong in two independent ways, and following it produced a local
> environment that **silently could not authenticate**:
>
> 1. It told you to set **`Graph:ClientSecret`** and **`Dataverse:ClientSecret`**. Since task 022 **neither
>    key has any consumer in `src/`** — the credential provider resolves `AzureAd:ClientSecret` →
>    `API_CLIENT_SECRET` → `AZURE_CLIENT_SECRET`. Setting them did nothing at all, and did it quietly.
> 2. It assumed a BFF client secret exists. **It does not.** Per ADR-028 **A4** the BFF identity is
>    secret-free in every deployed environment; `BFF-API-ClientSecret` and its lowercase duplicate were
>    deleted from Key Vault on 2026-08-24.

## How the BFF authenticates (read this before setting anything)

| Environment | Credential |
|---|---|
| **Deployed** (dev / demo / prod) | A **federated credential** issued to the App Service's user-assigned managed identity. `Graph:Credentials:Order = [ManagedIdentityFederated]`, with `RequireSecretFreeIdentity=true` so the app refuses to start if a secret returns to the order. Nothing to configure locally, and nothing to rotate |
| **Local workstation** | A workstation has **no route to IMDS**, so a managed-identity federated credential cannot be minted there. `Graph:Credentials:RequireSecretFreeIdentity` is **exempt in Development** for exactly this reason (`IdentityConfigurationValidator.IsDevelopment()`) |

### ⚠️ Local OBO needs a client secret, and you can no longer fetch one from Key Vault

If you need the **OBO** (delegated / act-as-the-user) paths locally — SPE file operations, `dataverse.*`
chat tool calls, send-as-user email — you need a client secret, and the shared copy is gone.

`az keyvault secret show --vault-name spaarke-spekvcert --name bff-api-client-secret` **no longer works.**
Entra will not disclose a secret value after creation, so there is no other read path. Options:

1. **You already have it in user-secrets** → nothing to do. The app-registration secret itself remains
   valid until **2027-12-19**; only the Key Vault *copies* were removed.
2. **Recover the Key Vault copy** — it was soft-deleted, not purged, and is recoverable until
   **2026-11-22**:
   `az keyvault secret recover --vault-name spaarke-spekvcert --name bff-api-client-secret`
3. **Use the deployed dev BFF** (`https://spaarke-bff-dev.azurewebsites.net`) instead of running OBO
   locally. This is the recommended default — it exercises the real credential path.
4. **Mint a dev-only secret** on a **separate** app registration. Do **not** add one to
   `SDAP-BFF-SPE-API`: ADR-028 E-3 is transitional and *does not license expansion*.

> A **long-term replacement for the local-dev inner loop is an open owner decision** (booked to task 090).
> Do not solve it ad hoc by re-creating the shared secret.

**Everything that is not OBO works locally with no secret at all** — app-only Dataverse and Graph go
through `DefaultAzureCredential`, which chains to your `az login` session. For most local work, `az login`
is the whole setup.

## Initialize User Secrets

Run from the BFF project directory (`src/server/api/Sprk.Bff.Api`):

```bash
dotnet user-secrets init
```

## Set Required Secrets

### Client secret — ONLY if you need local OBO (see above)

Use the key the credential provider actually reads. `Graph:ClientSecret` and `Dataverse:ClientSecret`
are **dead keys** — setting them has no effect.

```bash
dotnet user-secrets set "AzureAd:ClientSecret" "your-app-client-secret"
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
export AzureAd__ClientSecret="your-secret"          # only if you need local OBO — see the top of this page
export ServiceBus__ConnectionString="your-connection-string"
```

**Note**: Use double underscores `__` for nested configuration in env vars.

> `Graph__ClientSecret` and `Dataverse__ClientSecret` were listed here until 2026-08-24 and are **dead keys** —
> nothing in `src/` reads either one. Exporting them has no effect.

> ⚠️ Do **not** set `AZURE_CLIENT_ID` on a workstation to "help" managed identity. It is read by the Azure
> Identity SDK itself (`EnvironmentCredential` / `ManagedIdentityCredential`), and pointing it at an app
> registration id while a managed identity is expected is the FR-B4 identity-conflation failure — it produces
> an opaque `AADSTS` error rather than a clear one.

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
