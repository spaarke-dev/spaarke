# Task 004: Outlook Unified Manifest Configuration

> **Status**: Complete
> **Format**: Unified JSON Manifest (GA for Outlook)
> **Location**: `src/client/office-addins/outlook/manifest.json`

## Overview

The Outlook add-in uses the unified JSON manifest format, which is the newer, cleaner format for Office Add-ins that is GA (Generally Available) for Outlook. This format replaces the traditional XML manifest and provides better integration with the Microsoft 365 admin center.

## Manifest Configuration Summary

### Identity

| Field | Value | Notes |
|-------|-------|-------|
| Schema | Teams Manifest Schema | Required for unified format |
| ID | `${ADDIN_CLIENT_ID}` | Replaced at build time |
| Version | 1.0.0 | Semantic versioning |
| Name | Spaarke | Short name for UI |

### Host Requirements

| Requirement | Value |
|-------------|-------|
| Host | Outlook (Mail) |
| Mailbox API | 1.8+ |

The add-in requires Mailbox 1.8+ for NAA (Nested App Authentication) support.

### Extension Points

#### Read Mode (mailRead context)

| Button | Action | Purpose |
|--------|--------|---------|
| Save to Spaarke | Opens taskpane | Save current email and attachments to Spaarke DMS |

#### Compose Mode (mailCompose context)

| Button | Action | Purpose |
|--------|--------|---------|
| Share from Spaarke | Opens taskpane | Insert sharing link to a Spaarke document |
| Grant Access | Opens taskpane | Grant recipients access before sending |

### Runtimes

| Runtime | Type | Purpose |
|---------|------|---------|
| TaskpaneRuntime | general | Task pane UI |
| CommandRuntime | general | Function commands (for future use) |

### Resources

| Resource Type | Location |
|---------------|----------|
| Taskpane HTML | `https://localhost:3000/outlook/taskpane.html` |
| Commands HTML | `https://localhost:3000/outlook/commands.html` |
| Icons | `https://localhost:3000/assets/` |

## Icon Requirements

The manifest references these icon files (need to be created):

| Icon | Size | Path |
|------|------|------|
| icon-16.png | 16x16 | assets/icon-16.png |
| icon-32.png | 32x32 | assets/icon-32.png |
| icon-80.png | 80x80 | assets/icon-80.png |
| icon-outline.png | 32x32 | assets/icon-outline.png |
| icon-color.png | 192x192 | assets/icon-color.png |
| save-16.png | 16x16 | assets/save-16.png |
| save-32.png | 32x32 | assets/save-32.png |
| save-80.png | 80x80 | assets/save-80.png |
| share-16.png | 16x16 | assets/share-16.png |
| share-32.png | 32x32 | assets/share-32.png |
| share-80.png | 80x80 | assets/share-80.png |
| grant-16.png | 16x16 | assets/grant-16.png |
| grant-32.png | 32x32 | assets/grant-32.png |
| grant-80.png | 80x80 | assets/grant-80.png |

## Environment Variable Replacement

The manifest uses environment variable placeholders:

| Placeholder | Environment Variable | Purpose |
|-------------|---------------------|---------|
| `${ADDIN_CLIENT_ID}` | ADDIN_CLIENT_ID | Azure AD App Registration Client ID |

These are replaced at build time by webpack using DefinePlugin or at deployment.

## Production Deployment

Before production deployment:

1. **Update URLs**: Replace `localhost:3000` with production domain
2. **Update App ID**: Replace placeholder with actual Azure AD client ID
3. **Add Icons**: Create and deploy all icon resources
4. **Configure SSO**: Update WebApplicationInfo section with correct resource URI

## Testing Locally

1. Sideload the manifest in Outlook:
   - Outlook Web: Settings → Integrated Apps → Upload custom app
   - Outlook Desktop: File → Manage Add-ins → My Add-ins → Add custom add-in

2. Start the dev server: `npm run start:outlook`

3. The add-in buttons should appear in the Home tab ribbon

## Validation

Run Office Add-in Manifest Validator:
```bash
npx office-addin-manifest validate src/client/office-addins/outlook/manifest.json
```

## References

- [Unified Manifest Overview](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/unified-manifest-overview)
- [Outlook Add-in Manifests](https://learn.microsoft.com/en-us/office/dev/add-ins/outlook/manifests)
- [Task 004 POML](../tasks/004-create-outlook-manifest.poml)
