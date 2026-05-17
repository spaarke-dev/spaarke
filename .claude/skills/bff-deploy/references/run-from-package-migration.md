# Future Migration: WEBSITE_RUN_FROM_PACKAGE (planned, not yet executed)

> **Parent skill**: [bff-deploy](../SKILL.md)
> **Status**: queued, not yet performed
> **Extracted**: 2026-05-16 from SKILL.md by ai-procedure-quality-r1 (Phase 2b Wave 2b-B)

The current mitigation (hash verify + auto-recover) is reliable but inelegant — every deploy that hits a file lock pays a stop/start cycle. The long-term fix is to migrate the App Service to **Run-From-Package mode**, where the deployed zip is mounted as a read-only filesystem and wwwroot is never written to. File locks become impossible because there are no physical files to lock.

**Status**: queued, not yet performed. The hardened script handles the current pain reliably so there's no urgency, but the migration eliminates the failure class entirely.

## Risk-managed migration procedure

When ready to switch, follow these steps in order. Do NOT skip steps — each catches a class of breakage.

1. **Audit assumptions of mutable wwwroot**:
   - Search the repo for any code that writes to `wwwroot/`, `/home/site/wwwroot/`, `/site/wwwroot/`, or `D:\home\site\wwwroot`. None should exist in BFF runtime code (the BFF doesn't self-modify), but check.
   - Search Kudu console history for ad-hoc edits (e.g., someone hand-editing `web.config` or `appsettings.json` in the portal). Document and re-apply those as build-time changes.
   - Check CI/CD pipelines for assumptions about deploy targets.

2. **Test on a staging slot first** (NOT directly on dev):
   ```bash
   # Create a staging slot if it doesn't exist
   az webapp deployment slot create --name spe-api-dev-67e2xz \
     --resource-group spe-infrastructure-westus2 --slot staging-rfp-test

   # Enable run-from-package on the staging slot only
   az webapp config appsettings set --name spe-api-dev-67e2xz \
     --resource-group spe-infrastructure-westus2 --slot staging-rfp-test \
     --settings WEBSITE_RUN_FROM_PACKAGE=1

   # Deploy to the staging slot with the hardened script
   .\scripts\Deploy-BffApi.ps1 -UseSlotDeploy -SlotName staging-rfp-test
   ```

3. **Smoke test the staging slot for at least one full workday**:
   - All BFF endpoints respond
   - Sign-in flow works
   - File upload + AI processing works end-to-end
   - Background workers (Service Bus consumers) still process messages
   - No new errors in Application Insights vs. the production slot baseline

4. **Verify the deploy mechanism itself**:
   - Make a small text-only change (e.g., a log message)
   - Re-deploy to the staging slot
   - Confirm the new code is running (use `/swagger` or a known endpoint that returns the changed text)
   - The hardened script's hash-verify should still PASS — it just verifies files inside the mounted zip rather than on wwwroot

5. **Cutover**:
   - Enable `WEBSITE_RUN_FROM_PACKAGE=1` on the production slot
   - Deploy with the hardened script
   - Verify hash-match and health endpoints
   - Monitor Application Insights for 15-30 min for new error patterns

6. **Document in this skill**: once cutover is complete and stable, update the parent `bff-deploy/SKILL.md` to note the new mode is live + remove the "future migration" framing. Note any operational quirks discovered.

7. **Rollback path** (keep handy during migration):
   ```bash
   # If run-from-package causes problems, disable it and redeploy
   az webapp config appsettings delete --name spe-api-dev-67e2xz \
     --resource-group spe-infrastructure-westus2 \
     --setting-names WEBSITE_RUN_FROM_PACKAGE
   .\scripts\Deploy-BffApi.ps1
   ```

## Things that break under Run-From-Package

- Hot-editing files in Kudu console (wwwroot is read-only) — must use proper deploy
- Anything that writes to `wwwroot/` at runtime (logs, generated files) — should already be writing to `/home/LogFiles/` or `/home/site/logs/`, but verify
- Diagnostic file uploads via Kudu VFS PUT — read-only

## Things that DON'T break

- `/home/data/`, `/home/LogFiles/`, `/home/site/deployments/` — all remain writable
- Application Insights, Service Bus, Key Vault — unaffected
- Slot deploys + swap — fully supported
- The hardened deploy script — its hash-verify works identically because Kudu VFS transparently reads from the mounted zip
