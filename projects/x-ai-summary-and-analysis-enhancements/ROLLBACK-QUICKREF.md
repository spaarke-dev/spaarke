# Quick Reference: Rollback Procedures

> **For detailed procedures, see DEPLOYMENT-INVENTORY.md Section 6**

---

## Emergency Rollback Commands

### Scenario 1: Rollback API Only

```bash
# Swap deployment slot back
az webapp deployment slot swap \
  --slot staging \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2

# Verify old endpoint restored
curl https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/document-intelligence/analyze
```

**Duration:** 2-3 minutes
**Impact:** PCF will work with old endpoint (if still on v3.9.0)

---

### Scenario 2: Rollback PCF Only

⚠️ **Not recommended if API already deployed** - old endpoint removed in Task 020

```bash
# Export current state first (backup)
pac solution export \
  --name "UniversalQuickCreate" \
  --path "./UniversalQuickCreate_backup.zip" \
  --managed false

# Import previous version (if available)
pac solution import \
  --path "./UniversalQuickCreate_v3.9.0.zip" \
  --publish-changes
```

**Duration:** 5-10 minutes
**Impact:** AI Summary will not work (old PCF calls removed endpoint)

---

### Scenario 3: Full Rollback (Both API + PCF)

⚠️ **LAST RESORT** - Use fix-forward when possible

```bash
# 1. Rollback API to commit before Task 020
git checkout b21971a  # Commit before Task 020
# Deploy this version to Azure

# 2. Rollback PCF to v3.9.0
pac solution import \
  --path "./UniversalQuickCreate_v3.9.0.zip" \
  --publish-changes

# 3. Verify old flow works
# Test Matter form → Quick Create → AI Summary
```

**Duration:** 30-45 minutes
**Impact:** System back to pre-deployment state

---

## Rollback Decision Tree

```
Is the issue in API or PCF?
├── API (500 errors, endpoint not found)
│   └── Rollback API only (Scenario 1)
├── PCF (control errors, UI issues)
│   └── Fix forward (don't rollback - old endpoint gone)
└── Both broken
    ├── Can you fix playbook data? → Fix forward (create playbook)
    └── Data issue unfixable? → Full rollback (Scenario 3)
```

---

## Fix-Forward (Preferred)

### Missing Playbook Error

```bash
# Verify playbook exists
curl -H "Authorization: Bearer $TOKEN" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/playbooks/by-name/Document%20Profile

# If 404, check Dataverse directly
# Navigate to: Advanced Find → Playbooks
# Look for: sprk_name = "Document Profile"

# If missing, restore from seed data:
# See: scripts/seed-data/playbooks.json
```

---

## Emergency Contacts

- **On-Call Engineer**: [Contact Info]
- **DevOps Team**: #devops-support
- **Deployment Lead**: [Contact Info]

---

## Post-Rollback Actions

After any rollback:

1. [ ] Notify users in #general channel
2. [ ] Create incident report
3. [ ] Schedule post-mortem
4. [ ] Update deployment checklist with lessons learned
5. [ ] Fix root cause before next deployment attempt

---

**See:** DEPLOYMENT-INVENTORY.md for detailed procedures
