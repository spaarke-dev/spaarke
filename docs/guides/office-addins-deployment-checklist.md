# Spaarke Office Add-ins Deployment Checklist

> **Version**: 1.0
> **Last Updated**: January 2026
> **Purpose**: Pre-deployment verification for IT Administrators

---

## Overview

Use this checklist before deploying Spaarke Office Add-ins to your organization. Complete all items before proceeding to M365 Admin Center deployment.

---

## Pre-Deployment Checklist

### 1. Infrastructure Prerequisites

#### Azure Resources
- [ ] BFF API App Service deployed and running
  - Resource: `spe-api-prod-*` in `rg-spaarke-prod-westus2`
  - Health check passes: `GET /healthz` returns "Healthy"
- [ ] Static Web Apps deployed for add-in hosting
  - Resource: `spe-office-addins-prod`
  - Taskpane URL accessible: `https://spe-office-addins-prod.azurestaticapps.net/outlook/taskpane.html`
- [ ] Redis Cache provisioned
  - Resource: `spaarke-redis-prod`
  - Connection verified from BFF API
- [ ] Service Bus namespace and queues created
  - Queues: `office-upload-finalization`, `office-profile`, `office-indexing`
- [ ] Application Insights configured
  - Resource: `spe-insights-prod-*`
  - Connection string in BFF API settings

#### Network Access
- [ ] Firewall rules allow access to:
  - `*.azurewebsites.net` (443/HTTPS)
  - `*.azurestaticapps.net` (443/HTTPS)
  - `login.microsoftonline.com` (443/HTTPS)
  - `*.crm.dynamics.com` (443/HTTPS)
  - `*.sharepoint.com` (443/HTTPS)

### 2. Azure AD Configuration

#### Add-in App Registration
- [ ] App registration exists: `Spaarke Office Add-in`
- [ ] Client ID: `c1258e2d-1688-49d2-ac99-a7485ebd9995`
- [ ] Redirect URIs configured:
  - [ ] `brk-multihub://localhost` (NAA broker)
  - [ ] `https://spe-office-addins-prod.azurestaticapps.net/taskpane.html` (fallback)
- [ ] API permissions added:
  - [ ] `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation` (delegated)
  - [ ] `User.Read` (delegated)
- [ ] Admin consent granted for organization

#### BFF API App Registration
- [ ] App registration exists: `Spaarke BFF API`
- [ ] Client ID: `1e40baad-e065-4aea-a8d4-4b7ab273458c`
- [ ] App ID URI configured: `api://1e40baad-e065-4aea-a8d4-4b7ab273458c`
- [ ] `user_impersonation` scope exposed
- [ ] Client secret stored in Key Vault

### 3. BFF API Configuration

#### App Service Settings
- [ ] Environment set to `Production`
- [ ] `TENANT_ID` configured
- [ ] `API_APP_ID` configured
- [ ] `API_CLIENT_SECRET` references Key Vault
- [ ] Redis connection string configured
- [ ] Service Bus connection string configured
- [ ] Application Insights connection string configured

#### Office-Specific Settings
- [ ] Rate limiting settings configured:
  - `Office__RateLimiting__SavePerMinute`: 10
  - `Office__RateLimiting__SearchPerMinute`: 30
- [ ] Attachment limits configured:
  - `Office__AttachmentLimits__MaxSingleFileMb`: 25
  - `Office__AttachmentLimits__MaxTotalMb`: 100

#### Workers
- [ ] Background workers running (check logs)
- [ ] Service Bus queues processing messages

### 4. Manifest Configuration

#### Outlook Manifest (manifest.prod.json)
- [ ] All URLs updated for production:
  - [ ] Taskpane URL: `https://spe-office-addins-prod.azurestaticapps.net/outlook/taskpane.html`
  - [ ] Commands URL: `https://spe-office-addins-prod.azurestaticapps.net/outlook/commands.html`
  - [ ] Icon URLs point to production static hosting
- [ ] Manifest validates: `npx office-addin-manifest validate manifest.prod.json`
- [ ] Version number updated

#### Word Manifest (manifest.prod.xml)
- [ ] All URLs updated for production:
  - [ ] SourceLocation: `https://spe-office-addins-prod.azurestaticapps.net/word/taskpane.html`
  - [ ] IconUrl and HighResolutionIconUrl point to production
  - [ ] AppDomain includes production URL
- [ ] Manifest validates: `npx office-addin-manifest validate manifest.prod.xml`
- [ ] Version number updated

### 5. Security Review

- [ ] Azure AD permissions are minimal required
- [ ] No secrets in manifest files
- [ ] HTTPS enforced on all endpoints
- [ ] CORS configured correctly on BFF API
- [ ] Content Security Policy headers on static hosting
- [ ] Dataverse security roles configured for users

### 6. Testing

#### Staging Environment
- [ ] Add-in loads in test Outlook account
- [ ] Add-in loads in test Word account
- [ ] Authentication works (NAA primary, Dialog fallback)
- [ ] Save flow completes successfully
- [ ] Search returns expected results
- [ ] Share flow generates valid links

#### Compatibility Testing
- [ ] New Outlook (Windows) - verified
- [ ] New Outlook (Mac) - verified
- [ ] Outlook Web - verified
- [ ] Word Desktop (Windows) - verified
- [ ] Word Desktop (Mac) - verified
- [ ] Word Web - verified

### 7. Monitoring Setup

- [ ] Application Insights alerts configured
- [ ] Action Group created for notifications
- [ ] Email recipients configured
- [ ] Teams webhook configured (optional)
- [ ] Dashboard created: "SDAP Office Integration"

### 8. Documentation and Communication

- [ ] User documentation available
- [ ] Admin documentation available
- [ ] Support team briefed
- [ ] Help desk articles updated
- [ ] Deployment window communicated to stakeholders
- [ ] Pilot group identified (if phased rollout)

### 9. Rollback Plan

- [ ] Previous manifest versions backed up
- [ ] Previous static assets available
- [ ] Slot swap tested (if using deployment slots)
- [ ] Rollback procedure documented
- [ ] Emergency contacts identified

---

## Deployment Checklist

### M365 Admin Center Deployment

#### Outlook Add-in
- [ ] Navigate to M365 Admin Center > Settings > Integrated apps
- [ ] Click "Upload custom apps"
- [ ] Select "Office Add-in"
- [ ] Upload `manifest.prod.json`
- [ ] Select deployment scope:
  - [ ] Pilot group (recommended for initial deployment)
  - [ ] Entire organization (after successful pilot)
- [ ] Review permissions and deploy
- [ ] Note deployment timestamp: _______________

#### Word Add-in
- [ ] Navigate to M365 Admin Center > Settings > Integrated apps
- [ ] Click "Upload custom apps"
- [ ] Select "Office Add-in"
- [ ] Upload `manifest.prod.xml`
- [ ] Select same deployment scope as Outlook
- [ ] Review permissions and deploy
- [ ] Note deployment timestamp: _______________

---

## Post-Deployment Verification

### Immediate Checks (within 1 hour)

- [ ] Deployment status shows "Deployed" in Admin Center
- [ ] API health check still passing
- [ ] No errors in Application Insights
- [ ] Pilot users notified

### After Propagation (12-24 hours)

#### Outlook Add-in Verification
- [ ] Add-in visible in New Outlook desktop
- [ ] Add-in visible in Outlook Web
- [ ] Read mode: "Save to Spaarke" button works
- [ ] Compose mode: "Share from Spaarke" button works
- [ ] Task pane opens correctly
- [ ] Authentication succeeds
- [ ] Save flow completes successfully

#### Word Add-in Verification
- [ ] Add-in visible in Word Desktop (Windows)
- [ ] Add-in visible in Word Desktop (Mac)
- [ ] Add-in visible in Word Web
- [ ] All ribbon buttons appear
- [ ] Task pane opens correctly
- [ ] Authentication succeeds
- [ ] Save flow completes successfully

### Pilot Feedback (1-2 weeks)

- [ ] Pilot users surveyed for feedback
- [ ] Issues documented and addressed
- [ ] Performance metrics reviewed
- [ ] Decision made: expand to organization or address issues

---

## Sign-Off

| Role | Name | Date | Signature |
|------|------|------|-----------|
| IT Administrator | | | |
| Security Reviewer | | | |
| Project Manager | | | |

---

## Emergency Contacts

| Role | Name | Contact |
|------|------|---------|
| On-Call Engineer | | |
| IT Help Desk | | |
| Spaarke Support | | support@spaarke.com |

---

## Checklist Completion Summary

| Section | Items | Completed |
|---------|-------|-----------|
| Infrastructure | 5 | /5 |
| Azure AD | 10 | /10 |
| BFF API | 7 | /7 |
| Manifests | 8 | /8 |
| Security | 6 | /6 |
| Testing | 8 | /8 |
| Monitoring | 5 | /5 |
| Documentation | 6 | /6 |
| Rollback | 5 | /5 |
| **Total** | **60** | **/60** |

**Checklist completed by**: ____________________
**Date**: ____________________
**Ready for deployment**: [ ] Yes  [ ] No

---

*For detailed procedures, see the [Office Add-ins Administrator Guide](office-addins-admin-guide.md).*
