# CLAUDE.md — Spaarke Resource Architecture (R1)

> **Project status**: 🟡 DEFERRED — design only, execution waiting on `sdap-bff-api-remediation-fix` to complete.
> **Last Updated**: 2026-05-23

---

## Load Order When Working on This Project

If a task on this project resumes, load files in this order:

1. **[`README.md`](README.md)** — orientation and current state
2. **[`current-task.md`](current-task.md)** — active task or DEFERRED marker
3. **[`design.md`](design.md)** — full design context, recalibrated approach, open questions
4. **[`research/external-perspectives.md`](research/external-perspectives.md)** — verbatim AI advisor responses; the why behind specific design decisions
5. **Sister project state** — read [`projects/sdap-bff-api-remediation-fix/current-task.md`](../sdap-bff-api-remediation-fix/current-task.md) and any post-remediation design notes. The BFF surface this project touches may have changed.

## Critical Project-Specific Rules

### Before adding any catalog interface to the BFF

This project introduces typed per-family resource catalogs (`ISearchCatalog`, `IQueueCatalog`, etc.). Several rules apply that override defaults:

1. **NEVER expose a string-keyed accessor.** All catalog methods MUST take enums or sealed records. `GetIndex(SearchIndex.Files)` ✅. `GetIndex("files")` ❌. This is the highest-leverage design constraint in the project — adding an escape hatch destroys the value entirely.
2. **NEVER add a `GetByKey(string)` "escape hatch."** Even if a consumer asks for one. The right answer is to add an enum value.
3. **NEVER fall back to a string literal in code.** Implementations throw at startup if a required catalog entry is missing from configuration. There is no "silent default to `spaarke-knowledge-index-v2`."
4. **NEVER store catalog values in Dataverse.** Bootstrap loop — code that reads the catalog runs before Dataverse is reachable. Catalog values come from App Service settings (deployed by Bicep) or, in Phase 5, from Azure App Configuration.
5. **Tests get test-double catalogs.** Production catalogs are `internal sealed`. A test using `ConfigSearchCatalog` directly is breaking the abstraction.

### Before naming a new resource

1. **Use the Bicep naming function** in `infrastructure/bicep/naming.bicep` (created in Phase 1). Do not hand-write resource names in Bicep templates or scripts.
2. **Globally-unique resources** (storage, search service, hostnames) encode customer + env in name.
3. **Scoped resources** (indexes inside a search service, secrets inside KV, queues inside a SB namespace) do NOT repeat the parent's scope — just `{purpose}`.
4. **Honor length limits.** Storage accounts are 24 chars no dashes; Key Vault 24 chars with dashes. The naming function handles truncation deterministically — don't re-implement it.
5. **Use the `sprk-` prefix** — matches the Dataverse `sprk_` publisher prefix.

### Before adding a new resource family

1. **Add an enum** in `Catalog/Enums/` (e.g., `NewResource.cs`).
2. **Add a family-scoped interface** in `Services/Catalog/` (e.g., `INewResourceCatalog.cs`). Method signatures take the enum, never strings.
3. **Add a Bicep module** in `infrastructure/bicep/modules/` (or use AVM resource module if a clean fit).
4. **Add a customer-profile parameter** in `customer.bicep` with `@validate()` enforcing explicit declaration.
5. **Add App Service settings binding** in deployment outputs.
6. **Tests get a `FakeNewResourceCatalog`** before any consumer code is written.

No business logic code change involved. That's the project's success measure.

### Customer-specific by default

Every resource declared in a customer `.bicepparam` file MUST have an explicit mode declaration. `@validate()` rejects:
- Missing `mode` for any resource (no implicit "shared")
- A `mode: shared` value without an explicit acknowledgment

Mode values: `dedicated` (customer-owned per `customer.bicep`), `shared` (uses the platform default), `customerOwned` (BYOK — customer brings their own existing resource).

This is the implementation of the "all customer-specific by default" goal that drove Scope C in the original planning.

## When This Project Resumes

Suggested workflow when picking this back up:

1. **Re-read the BFF remediation outputs.** Check `projects/sdap-bff-api-remediation-fix/` for:
   - Where `Services/Ai/PublicContracts/` ended up
   - What happened to the 18 Options classes
   - Whether the 99 DI registrations got partially cleaned up
   - What namespace/folder conventions emerged for service code
2. **Re-validate this design** against the post-remediation BFF state (likely 1-2 hours).
3. **Update [`design.md`](design.md)** with any deltas. Mark the original-decision rationale where things change so the audit trail stays intact.
4. **Confirm scope with owner** before starting Phase 0 (inventory + ADR + naming convention ratified).
5. **Phase 1 (AI Search family)** is the first execution slice. Do not start before Phase 0 is signed off.

## What Did NOT Make the Final Design (and Why)

Captured here so the same arguments don't get re-litigated:

| Considered approach | Rejected because |
|---|---|
| Standalone `spaarke-resource-manifest.yaml` in source control | Claude's reframing: this would create a synchronization problem with Bicep. The Bicep params + generated solution constants + typed catalogs ARE the manifest. A separate file is redundant indirection. |
| Single `IResourceCatalog` god-interface | All four advisors pushed back. Grows into a 30-method monster, couples unrelated consumers. Per-family interfaces with enum accessors are the right shape. |
| Including Dataverse entity schema in catalog | Schema lives in solution-versioned generated constants. Mirroring it in a catalog creates a lie when solution evolves. Dataverse infrastructure refs (env URL, MI IDs, app registrations) DO belong in catalog. |
| Storing catalog values in Dataverse | Bootstrap loop. Catalog must be readable before Dataverse is reachable. |
| App Configuration as first-class architectural layer in Phase 1 | Premature. App Service settings populated by Bicep outputs handle 95% of the case. App Config comes online when there's a clear runtime-mutability need (Phase 5). |
| Big-bang refactor of all 13 resource families | One family per slice. AI Search first because it's the immediate dev unblock and proves the pattern. |
| AVM rip-and-replace | Opportunistic migration only. New resources use AVM; existing modules swap when touched. |

## Adjacent ADRs and Constraints

When this project runs, several existing ADRs and constraints are load-bearing:

- **[ADR-027 — Subscription Isolation and Dataverse Solution Management](../../.claude/adr/ADR-027-subscription-isolation-and-dataverse-solution-management.md)** — Current `platform.bicep` / `customer.bicep` split. This project extends it, doesn't supersede.
- **[ADR-013 — AI Architecture](../../.claude/adr/ADR-013-ai-architecture.md)** — BFF Hygiene rules. Clean catalogs make placement criteria easier to satisfy.
- **[ADR-028 — Spaarke Auth Architecture](../../.claude/adr/ADR-028-spaarke-auth-architecture.md)** — Managed Identity and auth resource references flow through catalog.
- **[ADR-010 — DI Minimalism](../../.claude/adr/ADR-010-di-minimalism.md)** — BFF has 99 DI registrations vs. the ≤15 target. Catalog work will help — it consolidates a lot of disparate config bindings into family-scoped interfaces.
- **[`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md)** — BFF placement criteria. Clean catalogs surface boundary contracts cleanly.
- **[`.claude/FAILURE-MODES.md#g-4`](../../.claude/FAILURE-MODES.md)** — The `privilege_group_ids` filterable=false gotcha. Phase 1 (AI Search rename + new index from corrected schema) resolves this.

## Related Skills

- **`adr-aware`** — automatically load relevant ADRs. Will pick up the new "Spaarke Resource Catalog" ADR when it's written.
- **`adr-check`** — validate code changes against ADR constraints. Will need updating once the new ADR is ratified.
- **`code-review`** — quality gate. The lint rule blocking string-literal resource names lives here.
- **`bff-deploy`** — BFF deployment. May need updates if catalog refactor changes the appsettings binding pattern.
- **`task-execute`** — load-bearing execution protocol. Any task on this project runs through task-execute.

---

## Important Notes for the Agent

- **This project is foundational.** Decisions made here ripple through the entire system. Bias toward consensus and explicit owner sign-off, not speed.
- **Don't start writing code until Phase 0 is complete.** The naming convention + ADR + inventory must land first. Phase 1's first task is the catalog interface design, not the rename.
- **External advisor reasoning is preserved in `research/external-perspectives.md`** — if you find yourself making a decision contrary to one of those, document why. The synthesis-to-design-decision trace at the bottom of that file is the audit trail.
- **The Spaarke Connect project depends on this work.** When this project executes, Connect's prerequisites need updating. Don't lose sight of that handoff.
