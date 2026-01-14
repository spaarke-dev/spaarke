# Phase 5 Deployment Checklist

## Pre-Deployment

### Code Validation
- [x] All Phase 5 code builds successfully (`dotnet build`)
- [x] Unit tests pass (1132+ tests)
- [x] No compiler warnings or errors
- [x] Code review completed

### Documentation
- [x] Production runbook created (RUNBOOK.md)
- [x] Admin guide created (ADMIN-GUIDE.md)
- [x] Load test scripts documented (tests/load/README.md)
- [x] API endpoints documented

### Configuration Verified
- [ ] `Email:BatchMaxConcurrency` set appropriately (default: 5)
- [ ] `Email:BatchProcessingBatchSize` set appropriately (default: 50)
- [ ] `Jobs:ServiceBus:MaxConcurrentCalls` matches BatchMaxConcurrency
- [ ] `Jobs:ServiceBus:PrefetchCount` set (recommended: 10)
- [ ] Redis connection string configured
- [ ] Service Bus connection string configured

---

## Deployment Steps

### 1. Azure App Service Deployment
- [ ] Build release package: `dotnet publish -c Release`
- [ ] Deploy to staging slot first
- [ ] Verify staging slot health: `GET /healthz`
- [ ] Run smoke tests against staging
- [ ] Swap to production slot
- [ ] Verify production slot health

### 2. Configuration Updates
- [ ] Update App Service configuration with new settings
- [ ] Verify Redis connection
- [ ] Verify Service Bus connection
- [ ] Verify Dataverse connection

### 3. Azure Monitoring Setup
- [ ] Configure DLQ depth alert (threshold: 10)
- [ ] Configure error rate alert (threshold: 5%)
- [ ] Configure processing time alert (threshold: 120s P95)
- [ ] Create Application Insights dashboard

### 4. Service Bus Configuration
- [ ] Verify queue exists: `sdap-jobs`
- [ ] Set max delivery count: 5
- [ ] Set lock duration: 5 minutes
- [ ] Enable dead-letter on expiration

---

## Post-Deployment Verification

### Smoke Tests
- [ ] `GET /healthz` returns 200
- [ ] `GET /api/admin/email-processing/stats` returns data
- [ ] `GET /api/v1/emails/admin/rules` returns rules
- [ ] `GET /api/v1/emails/admin/dlq` returns (empty) list

### Functional Tests
- [ ] Manual email conversion works (test with single email)
- [ ] Webhook processing works (create test email in Dataverse)
- [ ] Batch processing works (small batch of 10 emails)
- [ ] DLQ listing works
- [ ] Statistics endpoint returns accurate data

### Performance Baseline
- [ ] Measure API response times
- [ ] Measure webhook processing latency
- [ ] Verify no unusual resource consumption

---

## Rollback Plan

If issues are discovered:

1. **Immediate**: Swap back to previous slot
2. **Configuration**: Revert App Service settings
3. **Database**: No schema changes in Phase 5 (safe)
4. **Messages**: Service Bus messages persist (no data loss)

---

## Sign-Off

| Role | Name | Date | Signature |
|------|------|------|-----------|
| Developer | | | |
| DevOps | | | |
| QA | | | |
| Product Owner | | | |

---

## Notes

- Phase 5 adds batch processing, DLQ handling, and load testing infrastructure
- No breaking changes to existing functionality
- All new features are additive

---

*Created: January 2026*
