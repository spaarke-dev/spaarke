# Deploy + Verify — spaarkeai-assistant-enhancements-r3 (task 080)

> **Deployed**: 2026-08-11 from `work/spaarkeai-assistant-enhancements-r3` @ `8b2c6e6f7` (== `origin/master` — the fully-integrated master, R3 + all concurrently-merged worktrees). Owner-gated deploy, owner-authorized.

## Preconditions (all met)
- **Master re-sync**: branch was 42 behind master (session drift); merged clean (0 textual conflicts), 1 semantic conflict reconciled (`ContextBinderOrgContextTests` allow-list `OpenTabContextTypes`), full BFF suite 10347 pass / 0 fail, then **merged to master** (FF `b963411fb → 8b2c6e6f7`) + main repo synced.
- **Publish size**: 47.08 MB compressed (incl PDBs) — ≤60 MB ceiling ✅, −2.55 MB vs ~49.63 baseline.
- **CVE scan**: `dotnet list package --vulnerable --include-transitive` → "no vulnerable packages" ✅ (no new HIGH).

## BFF deploy (Step 4) — ✅ COMPLETE
- Script: `scripts/Deploy-BffApi.ps1` (hardened; `pwsh`).
- Target: `spaarke-bff-dev` / `rg-spaarke-dev` (verified Running pre-flight).
- Package: 48.46 MB. **All 4 critical files SHA-256-verified** on server (genuine replacement, not silent file-lock success). Health check passed.
- Endpoint sanity: `/healthz` → 200; `POST /api/ai/chat/sessions` → 401 (route registered + auth-gated, not 404).

## SpaarkeAi code page deploy (Step 5) — ✅ COMPLETE
- Vite-aliases shared libs to SOURCE → bundle picks up R3 shared-lib changes directly (no separate dist pre-build).
- Cache cleared (`rm -rf dist/ node_modules/.vite/ .vite/`) + clean `npm run build`.
- **Bundle freshness verified**: `dist/spaarkeai.html` contains R3 client strings — "pending action" (task 041 ProactiveCardStack), "Reply All" + "Summarize the thread" (task 025 email cards).
- Script: `scripts/Deploy-SpaarkeAi.ps1`. Target: web resource `sprk_spaarkeai` on `spaarkedev1.crm.dynamics.com` (id `5206a442-…`). Updated + customizations published. 5244 KB.

## Runtime DoD verification (Steps 6–7) — ⏳ OWNER UAT PENDING
Deploy mechanics are verified (BFF hash-verified + healthy + routes registered; code page uploaded + published + fresh bundle confirmed). The interactive flows need the deployed UI exercised (browser-level; no automated Chrome session in this run):
- [ ] **Overview DoD**: in SpaarkeAi, open a grid tab (e.g. My Tasks) and ask "how many overdue tasks?" → correct count, no query error, no date prompt, no duplicate tab (the `spaarke.grid_overview` tool over OBO with server `today`).
- [ ] **Per-item DoD**: select an email in the Email tab → Assistant shows Reply/Reply All/Forward/Summarize cards → Reply auto-drafts AND the composer preserves the quoted thread ([AI draft] + separator + [quoted thread]).
- [ ] **Dual-mount parity (NFR-05)**: open the standalone `sprk_emailpage` code page → EmailWorkspace still renders/works unchanged.

**Note**: because R3's awareness re-point (task 011) now feeds the prompt from live `session.Tabs`, the "Assistant sees the open tabs" behavior (the R2 UAT gap) should now work in the deployed env — worth confirming during UAT.
