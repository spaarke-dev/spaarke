# Phase 3 Task 020 — Configure Entra External ID Identity Provider

> **Task**: 020
> **Phase**: 3 — Power Pages Configuration
> **Status**: Documented (manual operator steps required)
> **Estimated effort**: 4 hours
> **Prerequisite**: Task 019 (BFF API Phase 2 deployed to Azure)

---

## Overview

This guide covers configuring Microsoft Entra External ID (formerly Azure AD External Identities / Azure AD B2C) as the sole identity provider for the Spaarke Power Pages portal. External users authenticate against the Entra External ID tenant; upon first login Power Pages automatically creates an `adx_externalidentity` record and links it to a `contact` record in Dataverse.

**Architecture summary**:

```
External User
    │
    ▼
Power Pages Portal (OIDC client)
    │  OpenID Connect authorization code flow
    ▼
Entra External ID Tenant  ──► issues id_token + access_token
    │
    ▼
Power Pages maps claims → Contact (emailaddress1, firstname, lastname)
    │
    ▼
adx_externalidentity links B2C identity to Contact row
```

---

## Step 1 — Prepare or Verify the Entra External ID Tenant

### Option A: Use an existing Entra External ID tenant

If a dedicated external-identity tenant already exists (e.g., `spaarkeexternal.onmicrosoft.com`):

1. Sign in to [https://entra.microsoft.com](https://entra.microsoft.com) using the tenant-admin account for the external tenant.
2. Confirm the tenant type is **External** (shown in the top-right tenant switcher as "External tenant").
3. Note the **Tenant ID** (GUID) and **Primary domain** — you will need both below.

### Option B: Create a new Entra External ID tenant

1. In the Azure portal ([https://portal.azure.com](https://portal.azure.com)), search for **Microsoft Entra External ID**.
2. Click **Create a tenant** → select **External**.
3. Provide:
   - **Organization name**: Spaarke External
   - **Initial domain name**: `spaarkeexternal` (results in `spaarkeexternal.onmicrosoft.com`)
   - **Location**: United States (or match your data residency requirement)
4. Complete the wizard and wait for tenant provisioning (~2 minutes).
5. Switch to the new tenant via the top-right tenant switcher.

**Record the following values before proceeding:**

| Value | Where to Find | Example |
|-------|--------------|---------|
| Tenant ID | Entra admin center → Overview | `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` |
| Tenant domain | Entra admin center → Overview | `spaarkeexternal.onmicrosoft.com` |
| OIDC Issuer URL | `https://spaarkeexternal.ciamlogin.com/{tenant-id}/v2.0` | — |

> **Note for Entra External ID (CIAM)**: The OIDC well-known endpoint uses the `ciamlogin.com` subdomain, not `login.microsoftonline.com`. The authority URL format is `https://{tenant-domain}.ciamlogin.com/{tenant-id}/v2.0`.

---

## Step 2 — Configure User Flows in the External Tenant

User flows define the sign-up and sign-in experience for external users.

1. In the Entra External ID admin center, navigate to **External Identities** → **User flows**.
2. Click **New user flow**.
3. Configure the flow:
   - **Name**: `SignUpSignIn`
   - **Identity providers**: Email with password (minimum); optionally add Microsoft, Google.
   - **User attributes to collect on sign-up**: Given name, Surname, Email Address.
   - **Application claims to return in token**: Given name (`given_name`), Surname (`family_name`), Email Address (`email`), User's object ID (`oid`).
4. Click **Create**.
5. Note the flow name — it becomes part of the authority URL in some configurations.

---

## Step 3 — Register the Power Pages App in Entra External ID

1. In the Entra External ID admin center, navigate to **Applications** → **App registrations**.
2. Click **New registration**:
   - **Name**: `Spaarke Power Pages Portal`
   - **Supported account types**: Accounts in this organizational directory only (Single tenant — because this is the dedicated external tenant)
   - **Redirect URI**: Leave blank for now (configured in the next step)
3. Click **Register**.
4. On the newly created app registration:
   - Copy the **Application (client) ID** — this is the `ClientId` used in Power Pages.
   - Copy the **Directory (tenant) ID** — confirms tenant scope.

### Configure Redirect URIs

1. On the app registration, go to **Authentication** → **Add a platform** → **Web**.
2. Add the redirect URI:
   ```
   https://{your-power-pages-portal-domain}/signin-oidc
   ```
   Example:
   ```
   https://spaarke-portal.powerappsportals.com/signin-oidc
   ```
3. Also add the logout URI:
   ```
   https://{your-power-pages-portal-domain}/Account/Login/LogOff
   ```
4. Under **Implicit grant and hybrid flows**, check:
   - ID tokens (used for sign-in)
5. Click **Save**.

> **Power Pages portal domain**: Find this in the Power Platform admin center → Environments → [your env] → Power Pages sites → your site URL.

### Add API Permissions

1. On the app registration, go to **API permissions** → **Add a permission** → **Microsoft Graph**.
2. Select **Delegated permissions** and add:
   - `openid`
   - `profile`
   - `email`
   - `offline_access`
3. Click **Add permissions**.
4. Click **Grant admin consent for [tenant]** → **Yes**.

---

## Step 4 — Create a Client Secret and Store in Key Vault

1. On the app registration, go to **Certificates & secrets** → **Client secrets** → **New client secret**.
2. Configure:
   - **Description**: `PowerPagesPortal-Prod`
   - **Expires**: 24 months (adjust per your rotation policy)
3. Click **Add**.
4. Copy the **Value** immediately — it is only shown once.

### Store Secret in Azure Key Vault

```bash
az keyvault secret set \
  --vault-name spaarke-spekvcert \
  --name "EntraExternalId-PowerPages-ClientSecret" \
  --value "<paste-secret-value-here>"
```

Verify:

```bash
az keyvault secret show \
  --vault-name spaarke-spekvcert \
  --name "EntraExternalId-PowerPages-ClientSecret" \
  --query "value" -o tsv
```

> The BFF API App Service uses Managed Identity to read Key Vault secrets. Power Pages itself cannot read from Key Vault directly — the secret is entered manually into the Power Pages admin center (Step 5) and is stored encrypted in the portal configuration. Rotate this secret annually or per your security policy.

---

## Step 5 — Configure the Identity Provider in Power Pages Admin Center

1. Sign in to the Power Platform admin center: [https://admin.powerplatform.microsoft.com](https://admin.powerplatform.microsoft.com).
2. Navigate to **Environments** → select the dev environment → **Power Pages sites**.
3. Click **Manage** next to the Spaarke portal.
4. In the Power Pages admin center, go to **Authentication** → **Identity providers**.
5. Click **Add provider**.
6. Select **Other** → **OpenID Connect**.
7. Enter the provider details:

| Field | Value |
|-------|-------|
| **Display name** | Spaarke Login |
| **Authority** | `https://spaarkeexternal.ciamlogin.com/{tenant-id}/v2.0` |
| **Client ID** | Application (client) ID from Step 3 |
| **Client Secret** | Secret value from Step 4 |
| **Redirect URI** | `https://{portal-domain}/signin-oidc` |
| **Scope** | `openid email profile offline_access` |
| **Response type** | `code` |
| **Response mode** | `form_post` |

8. Expand **Additional settings** and configure claim mappings:

| Claim | Contact field |
|-------|--------------|
| `email` | `emailaddress1` |
| `given_name` | `firstname` |
| `family_name` | `lastname` |
| `sub` or `oid` | (used by adx_externalidentity automatically) |

9. Click **Save**.

> **Claim mapping note**: Power Pages maps the `sub` claim from the OIDC token to the `adx_externalidentity` record's external identity field automatically. The email, given name, and family name claims populate the linked Contact. If the Contact already exists (matched by email), it is linked rather than duplicated.

---

## Step 6 — Disable Default Identity Providers (Optional but Recommended)

For security, disable all identity providers except Entra External ID:

1. In Power Pages admin center → **Authentication** → **Identity providers**.
2. Disable or remove:
   - Local authentication (username/password)
   - Azure AD (internal users — only if external-only access is required)
3. Leave only the Entra External ID provider enabled.

> If internal Spaarke staff also need portal access, keep Azure AD enabled and configure separate web roles for authenticated-internal vs. authenticated-external users.

---

## Step 7 — Enable External User Registration

1. In Power Pages admin center → **Authentication** → **General settings**.
2. Ensure:
   - **Allow registration**: Enabled
   - **Open registration**: Enabled (users can self-register via the Entra External ID user flow)
3. Optionally set:
   - **Require account confirmation**: Enabled (forces email verification before portal access)

> Open registration allows any user who completes the Entra External ID sign-up flow to access the portal. The Power Pages table permissions (Task 021) control what data they can see — a newly registered user with no `sprk_externalrecordaccess` grants will see no project data.

---

## Step 8 — Apply Portal Settings via Site Settings Table (Optional Override)

Some authentication behaviors can also be controlled via the **Site Settings** table in the Portal Management model-driven app. Navigate to **Portal Management** → **Site Settings** and verify or add:

| Name | Value | Description |
|------|-------|-------------|
| `Authentication/OpenIdConnect/EntraExternalId/Enabled` | `true` | Activates the OIDC provider |
| `Authentication/Registration/Enabled` | `true` | Allows new user registration |
| `Authentication/Registration/OpenRegistrationEnabled` | `true` | Self-service sign-up enabled |
| `Authentication/UserManager/UserValidator/RequireUniqueEmail` | `true` | Prevents duplicate email registrations |

---

## Step 9 — Test the Authentication Flow

### Test 1: Sign-up (new external user)

1. Open an InPrivate/Incognito browser window.
2. Navigate to: `https://{portal-domain}/`
3. Click **Sign In** (or the Spaarke Login button).
4. You should be redirected to the Entra External ID sign-in page.
5. Click **Sign up now** (or equivalent in the user flow).
6. Complete sign-up with a test email address not in Dataverse.
7. After completing the flow, you should be redirected back to the portal and logged in.

**Verify in Dataverse**:
- Open the Dataverse environment (`https://spaarkedev1.crm.dynamics.com`).
- Navigate to **Contacts** → check that a new Contact was created with the correct email, first name, and last name.
- Navigate to **External Identities** (`adx_externalidentity` table) → verify a record exists linking the Contact to the Entra External ID identity.

### Test 2: Sign-in (returning user)

1. Open an InPrivate window.
2. Navigate to the portal and sign in with the same test credentials.
3. Verify: no duplicate Contact is created — the same Contact is linked.

### Test 3: Verify claim mapping

In the Portal Management app, find the newly created Contact and confirm:
- `emailaddress1` = the email used during sign-up
- `firstname` = given name from sign-up
- `lastname` = family name from sign-up

---

## Acceptance Criteria Checklist

- [ ] Entra External ID tenant exists and has the `SignUpSignIn` user flow configured
- [ ] App registration `Spaarke Power Pages Portal` exists in the external tenant
- [ ] Redirect URI `https://{portal-domain}/signin-oidc` is registered
- [ ] Client secret is stored in Key Vault as `EntraExternalId-PowerPages-ClientSecret`
- [ ] Power Pages identity provider configured (Authority, ClientId, ClientSecret, Scope, claim mappings)
- [ ] External user can complete sign-up via portal login page
- [ ] Contact record created with correct email, firstname, lastname after sign-up
- [ ] `adx_externalidentity` record exists and links to Contact
- [ ] Returning user sign-in does not create duplicate Contact

---

## Troubleshooting

| Symptom | Likely Cause | Resolution |
|---------|-------------|------------|
| Redirect URI mismatch error | URI in app registration does not match portal domain exactly | Verify no trailing slash, correct `signin-oidc` path |
| Claims not mapping to Contact | Claim names differ from user flow output claims | Check user flow claims match `email`, `given_name`, `family_name` |
| User stuck in redirect loop | OIDC provider disabled or misconfigured | Check Power Pages admin center → Authentication → Identity providers |
| Duplicate contacts created | Email claim not being matched | Confirm `RequireUniqueEmail` site setting is `true` |
| Authority URL rejected | Using `login.microsoftonline.com` instead of `ciamlogin.com` | Entra External ID (CIAM) uses `{domain}.ciamlogin.com` format |
