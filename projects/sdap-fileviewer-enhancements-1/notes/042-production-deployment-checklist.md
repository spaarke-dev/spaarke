# Task 042 - Production Deployment Checklist

## Date: December 4, 2025

## Status: Awaiting Pilot GO Decision

This document provides the production deployment checklist and procedures.

---

## Pre-Deployment Requirements

### Go/No-Go Decision
- [ ] Pilot validation complete (Task 041)
- [ ] GO decision documented
- [ ] All critical issues resolved
- [ ] Sign-off from stakeholders

### Code Readiness
- [ ] All tests passing
- [ ] Version numbers updated
- [ ] Release notes prepared
- [ ] Rollback tested in test environment

### Environment Readiness
- [ ] Production App Service accessible
- [ ] Production Dataverse accessible
- [ ] Azure CLI authenticated with production credentials
- [ ] PAC CLI authenticated with production environment
- [ ] Application Insights configured

### Communication
- [ ] Deployment window scheduled
- [ ] Stakeholders notified of window
- [ ] Support staff on standby
- [ ] Escalation contacts identified

---

## Deployment Window

**Recommended:** Low-usage period (e.g., early morning or late evening)

| Item | Value |
|------|-------|
| Scheduled Date | TBD |
| Scheduled Time | TBD |
| Duration | 1 hour |
| Change ID | TBD |

---

## Deployment Steps

### Phase 1: Pre-Deployment (15 minutes before)

```bash
# Verify Azure CLI connection
az account show

# Verify PAC CLI connection
pac auth list

# Check current production health
curl https://[production-bff-url]/healthz
curl https://[production-bff-url]/ping
```

- [ ] Azure CLI connected
- [ ] PAC CLI connected
- [ ] Production BFF healthy
- [ ] Team notified deployment starting

### Phase 2: Deploy BFF (15 minutes)

```bash
cd /c/code_files/spaarke/src/server/api/Spe.Bff.Api

# Build release
dotnet publish -c Release -o ./publish

# Deploy to production
az webapp deploy \
    --resource-group rg-spaarke-prod \
    --name [production-app-service-name] \
    --src-path ./publish \
    --type zip

# Wait for deployment to complete
az webapp show \
    --resource-group rg-spaarke-prod \
    --name [production-app-service-name] \
    --query state
```

- [ ] BFF deployed successfully
- [ ] No deployment errors

### Phase 3: Verify BFF (5 minutes)

```bash
# Check health endpoints
curl https://[production-bff-url]/healthz
curl https://[production-bff-url]/ping
curl https://[production-bff-url]/status

# Check Application Insights for errors
# Navigate to Azure Portal > Application Insights > Failures
```

- [ ] /healthz returns 200
- [ ] /ping returns "pong"
- [ ] /status returns service info
- [ ] No errors in Application Insights

### Phase 4: Deploy PCF (10 minutes)

```bash
cd /c/code_files/spaarke/src/client/pcf/SpeFileViewer

# Import solution to production
pac solution import \
    --path ./SpeFileViewerSolution/bin/Release/SpeFileViewerSolution.zip \
    --environment https://[production-org].crm.dynamics.com \
    --async
```

- [ ] Solution import started
- [ ] Solution import completed
- [ ] No import errors

### Phase 5: Verify PCF (5 minutes)

1. Open production Power Apps environment
2. Navigate to sprk_document record
3. Verify FileViewer loads
4. Verify Edit button visible
5. Click Edit button
6. Verify desktop app opens

- [ ] FileViewer loads
- [ ] Edit button visible
- [ ] Desktop app opens

### Phase 6: Post-Deployment Monitoring (30 minutes)

Monitor for the following:

| Metric | Threshold | Status |
|--------|-----------|--------|
| Error rate | < 1% | [ ] |
| Response time (P95) | < 3s | [ ] |
| Failed requests | 0 | [ ] |
| Support tickets | 0 | [ ] |

```bash
# Monitor Application Insights
# Check every 5 minutes for 30 minutes
```

- [ ] 5 min check - OK
- [ ] 10 min check - OK
- [ ] 15 min check - OK
- [ ] 20 min check - OK
- [ ] 25 min check - OK
- [ ] 30 min check - OK

---

## Rollback Procedure

### If Issues Detected:

**BFF Rollback:**
```bash
# Swap deployment slots (if using slots)
az webapp deployment slot swap \
    --resource-group rg-spaarke-prod \
    --name [production-app-service-name] \
    --slot staging \
    --target-slot production

# Or redeploy previous version
az webapp deploy \
    --resource-group rg-spaarke-prod \
    --name [production-app-service-name] \
    --src-path ./previous-build/publish \
    --type zip
```

**PCF Rollback:**
```bash
pac solution import \
    --path ./previous/SpeFileViewerSolution_previous.zip \
    --environment https://[production-org].crm.dynamics.com
```

**Communication:**
1. Notify team of rollback
2. Update change ticket
3. Document issues encountered
4. Schedule post-mortem

---

## Success Criteria

Deployment is successful when:

- [ ] BFF deployed and healthy
- [ ] PCF deployed and loading
- [ ] No errors for 30 minutes
- [ ] Edit functionality working
- [ ] Support received no incident reports

---

## Post-Deployment

### Communication

**Success Email Template:**
```
Subject: FileViewer Enhancements - Deployed to Production

The FileViewer enhancements have been successfully deployed to production.

New Features:
- "Open in Desktop" button for Word, Excel, and PowerPoint documents
- Improved loading experience with visual feedback
- Faster document preview loading

Users can now click the "Open in Desktop" button to edit documents directly
in their desktop Office applications.

If you encounter any issues, please contact [support channel].

Thank you,
[Your Name]
```

### Documentation Updates

- [ ] Update project README status to Complete
- [ ] Update TASK-INDEX.md with completion
- [ ] Archive project documentation
- [ ] Update user documentation (if applicable)

### Project Closure

```markdown
## Project Completion Summary

**Project:** SDAP FileViewer Enhancements 1
**Completion Date:** YYYY-MM-DD
**Duration:** X weeks

### Delivered Features
1. "Open in Desktop" button for Office documents
2. Improved loading experience
3. Performance optimizations

### Metrics
- Tasks Completed: 17/17
- Tests Passing: XX
- Performance: < 3s preview load

### Lessons Learned
1.
2.
3.
```

---

## Emergency Contacts

| Role | Name | Contact |
|------|------|---------|
| Tech Lead | TBD | TBD |
| DevOps | TBD | TBD |
| Support | TBD | TBD |
| Escalation | TBD | TBD |

---

## Sign-off

| Role | Name | Signature | Date |
|------|------|-----------|------|
| Deployment Lead | | | |
| QA | | | |
| Product Owner | | | |
