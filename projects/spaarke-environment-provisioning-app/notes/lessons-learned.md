# Lessons Learned — spaarke-environment-provisioning-app

> **Date**: 2026-06-12
> **Project**: Dataverse Environment Registry + Registration Provisioning Refactor

## What went well

1. **Platform-entity framing paid off.** Designing `sprk_dataverseenvironment` as a reusable
   platform registry (not a registration-specific table) means customer management, deployment
   tooling, and the r2 systematization effort can consume it without schema changes.
2. **Per-URL token cache pattern** (`ConcurrentDictionary<string, AccessToken>` with
   semaphore-protected refresh in `RegistrationDataverseService`) cleanly enabled
   cross-environment provisioning without a second service class. Reusable pattern for any
   future multi-environment Dataverse operation.
3. **Validation in both layers** (ribbon JS alert + API 400 ProblemDetails) per the no-plugins
   constraint (ADR-002) worked without server-side gaps — the API check is authoritative,
   the JS check is UX.

## What bit us

1. **"Validated but not wired" defect (D-040-01).** The approve endpoint parsed the
   per-environment license JSON for FR-12 validation but never passed it into provisioning —
   `AssignLicensesAsync` silently kept using global appsettings. Acceptance criteria that say
   "different environments can have different X" need an E2E assertion that the per-environment
   value actually *reaches* the consumer, not just that it parses. Caught only in task 040.
2. **Seed-data fidelity (D-040-02).** FR-14 seeding copied repo appsettings placeholders
   (empty SKU IDs) instead of the real values living in Azure App Service settings. When config
   migrates from Azure → Dataverse, seed from the *deployed* config (`az webapp config
   appsettings list`), not the repo template.
3. **Column-name drift between spec and deployment.** `sprk_accountdomain` → deployed as
   `sprk_envaccountdomain`, `sprk_appid` → `sprk_mdaappid` (commit d1450fc2 fixed collisions).
   The DTO was updated, but spec.md/design.md still show the old names. When a schema script
   hits reserved/colliding names, back-propagate the final names into the spec data model.
4. **Worktree drift.** The provisioning worktree sat at a pre-merge commit while later work
   (schema deployment d1450fc2) landed on master via a different checkout; `current-task.md`
   and `TASK-INDEX.md` disagreed by months. Always `git merge origin/master` a resumed worktree
   FIRST and trust the merged TASK-INDEX over `current-task.md`.
5. **Obsolete-config removal blocked by a second consumer.** Criterion 10 (delete
   `DemoProvisioning__Environments__*` from Azure) assumed the registration flow was the only
   consumer, but `DemoExpirationService.ResolveDefaultEnvironment()` still reads it. Before
   promising "remove legacy config" in a spec, enumerate all readers of the config section.

## Carry-overs → r2 systematization

| Item | Detail |
|---|---|
| DemoExpirationService migration | Resolve environment per-request via `sprk_dataverseenvironmentid` lookup; then delete `DemoProvisioning__Environments__*` + `__DefaultEnvironment` from Azure (closes criterion 10) |
| Live provisioning sign-off | Criteria 5, 8, 9, 11 — one approve per environment + bulk grid approve, operator-supervised |
| Doc drift | `auth-azure-resources.md` App Service names stale (`spe-api-dev-67e2xz` → `spaarke-bff-dev`/`rg-spaarke-dev`) |
| Spec data-model names | Update spec.md/design.md column names to deployed reality (`sprk_envaccountdomain`, `sprk_mdaappid`) |
| Lookup type filtering | Filter Target Environment lookup by environment type (deferred from r1 scope) |
| Azure resource automation | Entra group / Conditional Access / SPE container setup per environment is still manual (r1 out-of-scope) |
