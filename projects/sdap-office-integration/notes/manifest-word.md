# Task 005: Word XML Manifest Configuration

> **Status**: Complete
> **Format**: XML Add-in-only Manifest
> **Location**: `src/client/office-addins/word/manifest.xml`

## Overview

The Word add-in uses the traditional XML manifest format. As of January 2026, the unified JSON manifest is still in preview for Word, so XML is required for production deployments. This manifest configures ribbon buttons and task pane functionality.

## Manifest Configuration Summary

### Identity

| Field | Value | Notes |
|-------|-------|-------|
| ID | `${ADDIN_CLIENT_ID}` | Replaced at build time |
| Version | 1.0.0 | Semantic versioning |
| Provider | Spaarke | Company name |
| Display Name | Spaarke | Shown in Office UI |

### Host Requirements

| Requirement | Value |
|-------------|-------|
| Host | Document (Word) |
| WordApi | 1.3+ |

The add-in requires WordApi 1.3+ for document body access (OOXML, HTML).

### Ribbon Buttons

All buttons are placed in the Home tab, Spaarke group:

| Button ID | Label | Action | Purpose |
|-----------|-------|--------|---------|
| SaveButton | Save to Spaarke | ShowTaskpane | Save current document to Spaarke DMS |
| SaveVersionButton | Save Version | ShowTaskpane | Save new version of existing document |
| ShareButton | Share | ShowTaskpane | Insert sharing link to a document |
| GrantAccessButton | Grant Access | ShowTaskpane | Manage document access permissions |

### Version Overrides

The manifest includes both V1.0 and V1.1 version overrides for maximum compatibility:

- **V1.0**: Basic ribbon commands
- **V1.1**: Enhanced features including WebApplicationInfo for SSO

### Resources

| Resource ID | Type | URL |
|-------------|------|-----|
| Taskpane.Url | URL | `https://localhost:3000/word/taskpane.html` |
| Commands.Url | URL | `https://localhost:3000/word/commands.html` |

### Strings

| ID | Value |
|----|-------|
| SpaarkeGroup.Label | Spaarke |
| SaveButton.Label | Save to Spaarke |
| SaveButton.Title | Save to Spaarke |
| SaveButton.Description | Save this document to Spaarke Document Management System |
| SaveVersionButton.Label | Save Version |
| SaveVersionButton.Title | Save Version |
| SaveVersionButton.Description | Save a new version of this document to Spaarke |
| ShareButton.Label | Share |
| ShareButton.Title | Share from Spaarke |
| ShareButton.Description | Insert a sharing link to a document from Spaarke |
| GrantAccessButton.Label | Grant Access |
| GrantAccessButton.Title | Grant Document Access |
| GrantAccessButton.Description | Grant access to this document for specific users |

## Icon Requirements

The manifest references these icon files:

| Image ID | Size | Path |
|----------|------|------|
| Icon.16x16 | 16x16 | assets/icon-16.png |
| Icon.32x32 | 32x32 | assets/icon-32.png |
| Icon.80x80 | 80x80 | assets/icon-80.png |
| Save.16x16 | 16x16 | assets/save-16.png |
| Save.32x32 | 32x32 | assets/save-32.png |
| Save.80x80 | 80x80 | assets/save-80.png |
| SaveVersion.16x16 | 16x16 | assets/save-version-16.png |
| SaveVersion.32x32 | 32x32 | assets/save-version-32.png |
| SaveVersion.80x80 | 80x80 | assets/save-version-80.png |
| Share.16x16 | 16x16 | assets/share-16.png |
| Share.32x32 | 32x32 | assets/share-32.png |
| Share.80x80 | 80x80 | assets/share-80.png |
| Grant.16x16 | 16x16 | assets/grant-16.png |
| Grant.32x32 | 32x32 | assets/grant-32.png |
| Grant.80x80 | 80x80 | assets/grant-80.png |

## XML Schema Namespaces

The manifest uses these namespaces:

| Prefix | Namespace | Purpose |
|--------|-----------|---------|
| (default) | `http://schemas.microsoft.com/office/appforoffice/1.1` | Core schema |
| bt | `http://schemas.microsoft.com/office/officeappbasictypes/1.0` | Basic types |
| ov | `http://schemas.microsoft.com/office/taskpaneappversionoverrides` | Version overrides |

## Permissions

| Permission | Description |
|------------|-------------|
| ReadWriteDocument | Full access to document content (required for Save Version) |

## App Domains

Trusted domains for cross-origin requests:

- `https://localhost:3000` (development)
- `https://login.microsoftonline.com` (AAD authentication)

## SSO Configuration (WebApplicationInfo)

```xml
<WebApplicationInfo>
  <Id>${ADDIN_CLIENT_ID}</Id>
  <Resource>api://${ADDIN_CLIENT_ID}</Resource>
</WebApplicationInfo>
```

This enables SSO token acquisition for NAA-based authentication.

## Environment Variable Replacement

| Placeholder | Environment Variable | Purpose |
|-------------|---------------------|---------|
| `${ADDIN_CLIENT_ID}` | ADDIN_CLIENT_ID | Azure AD App Registration Client ID |

## Production Deployment

Before production deployment:

1. **Update URLs**: Replace `localhost:3000` with production domain
2. **Update App ID**: Replace placeholder with actual Azure AD client ID
3. **Add Icons**: Create and deploy all icon resources
4. **Add App Domains**: Add production domain to AppDomains list
5. **Update Support URL**: Set actual support page URL

## Testing Locally

1. Sideload the manifest in Word:
   - Word Web: Insert → Add-ins → Upload My Add-in
   - Word Desktop: Developer tab → Add-ins → My Add-ins

2. Start the dev server: `npm run start:word`

3. The Spaarke group should appear in the Home tab ribbon

## Validation

Run Office Add-in Manifest Validator:
```bash
npx office-addin-manifest validate src/client/office-addins/word/manifest.xml
```

## Migration Path

When unified manifest reaches GA for Word, consider migrating to JSON format:
- Cleaner syntax
- Better admin center integration
- Single manifest format for all hosts

## References

- [XML Manifest Reference](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/add-in-manifests)
- [VersionOverrides Reference](https://learn.microsoft.com/en-us/javascript/api/manifest/versionoverrides)
- [Task 005 POML](../tasks/005-create-word-manifest.poml)
