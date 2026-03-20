# Lessons Learned: Production Environment Setup R2

> Project: production-environment-setup-r2
> Completed: 2026-03-20
> Total Tasks: 39 (37 parallel + 2 sequential capstone tasks)

---

## What Went Well

### Parallel Execution Strategy

The project was designed from the start for maximum parallelism across 8 execution groups. Phases 3, 4, and 5 ran with up to 9 concurrent Claude Code agents executing simultaneously (all 9 code pages in parallel, all 8 PCF controls in parallel, all 8 legacy JS/add-in tasks in parallel). This reduced total wall-clock time from an estimated 72 hours sequential to approximately 10-12 hours.

Key success factors:
- Task files were written with zero inter-task dependencies within each phase
- Each POML task was self-contained with full context (file paths, patterns, acceptance criteria)
- Agents never blocked each other because they owned completely separate file trees

### The resolveRuntimeConfig() Pattern

The central architectural decision — introducing `resolveRuntimeConfig()` in `@spaarke/auth` — proved to be the right abstraction. All 9 code pages and several PCF controls adopted this single function. It handles:
- Dataverse Environment Variable querying via REST
- 5-minute caching (consistent with existing PCF patterns)
- Clear error messages when config is missing (fail-loud strategy)
- Fallback to `window.__SPRK_RUNTIME_CONFIG__` for test environments

The pattern created a clean contract: infrastructure provisions the Dataverse Environment Variables, and all client code reads from them uniformly.

### POML Task Structure

The POML task format proved highly effective for parallel AI agent execution. Each task file contained:
- Exact file paths to modify
- The precise change required (not vague direction)
- Acceptance criteria phrased as verifiable checks
- Constraints that prevented scope creep

Agents could execute tasks cold with no prior session context, which was essential for parallel execution where each agent started fresh.

### Fail-Loud Configuration Philosophy

Removing dev defaults (instead of keeping them as silent fallbacks) proved to be the right call. Agents encountered no ambiguity about which behavior was expected. The "fail loudly with a clear error" approach means misconfigured deployments are caught immediately at startup rather than silently degrading to dev behavior in production.

---

## Challenges Encountered

### OAuth Token Expiry During Long Parallel Sessions

Several agents running Phase 3 and Phase 4 tasks encountered OAuth token expiry mid-task. These agents had been idle in queue while earlier agents ran, and by the time they started their assigned task the Azure CLI / PAC CLI authentication had expired. Mitigation: re-ran those specific tasks in a new session with fresh credentials. Two tasks (task 035, task 047) needed re-execution for this reason.

### BFF_API_SCOPE Missing from Some Migrations

During initial Phase 3 parallel execution, several code page agents completed their tasks without updating the `BFF_API_SCOPE` alongside `BFF_API_BASE_URL`. The issue was that the POML task spec mentioned scope in the acceptance criteria but not explicitly in the step-by-step instructions. These were caught during Phase 6 validation (task 050) and corrected by re-running the affected tasks. Lesson: acceptance criteria alone is not sufficient — required changes must also appear in the implementation steps.

### One Task Required Re-Execution (Task 033 — RelatedDocumentCount)

Task 033 was initially executed by an agent that misread the `authInit.ts` pattern and used a slightly different function signature than the `resolveRuntimeConfig()` contract established in task 010. This was caught during code review (task 050 validation grep) and the task was re-executed cleanly. Root cause: the task file did not include the exact function signature as a constraint.

### No Visual Confirmation of Dataverse Environment Variable Queries at Runtime

The `resolveRuntimeConfig()` pattern works correctly but the only way to verify it at runtime is to watch network traffic or read the Dataverse REST response. A future improvement would be to add a `window.__SPRK_RESOLVED_CONFIG__` debug global that gets populated after resolution, so developers can confirm in the browser console what config was loaded.

---

## Key Architectural Decisions Made

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Runtime resolution via Dataverse Environment Variables | Avoids N×M build matrix (N code pages × M environments); single build artifact deploys everywhere | Implemented across all 9 code pages |
| `resolveRuntimeConfig()` in `@spaarke/auth` shared library | Single implementation point, consistent caching, testable | Adopted by all code pages and several PCF controls |
| Remove dev defaults entirely (no fallback) | Silent fallback to dev in production is worse than a clear error | All `DEFAULT_CLIENT_ID` and `DEFAULT_BFF_SCOPE` constants removed |
| Keep `.env.development` untouched | Local dev workflow should not be disrupted | All `.env.development` files preserved; only `.env.production` changed |
| 5 canonical environment values | Tenant ID, BFF API URL, BFF App ID, MSAL Client ID, Dataverse Org URL — all flow from one provisioning step | `Validate-DeployedEnvironment.ps1` checks exactly these 5 |
| `Validate-DeployedEnvironment.ps1` as deployment gate | Catch missing env vars at provisioning time, not at runtime | Script created and integrated into `Provision-Customer.ps1` |
| Scripts accept `-OrgUrl` parameter with `$env:DATAVERSE_ORG_URL` fallback | Enables both interactive and CI/CD usage | Applied to all 30+ scripts |

---

## Recommendations for Future Environment-Agnostic Projects

### 1. Establish the Shared Config Pattern Before Writing Any Tasks

The `resolveRuntimeConfig()` function in `@spaarke/auth` was the critical path blocker (Phase 2, task 010). All subsequent phases depended on it. In the next project of this type, establish the shared resolution layer first and get it reviewed before writing the downstream migration tasks. The downstream tasks can reference the exact API signature.

### 2. Include Exact Function Signatures in POML Tasks

When multiple parallel agents must implement the same interface/pattern, include the exact TypeScript signature in each task's constraints section, not just the acceptance criteria. This prevents subtle signature drift across agents.

### 3. Add Scope + URL Together as a Single Unit in Acceptance Criteria AND Steps

Every place that references `BFF_API_BASE_URL` also needs `BFF_API_SCOPE`. These always travel together. Future tasks should treat them as a single migration unit and list both in the implementation steps (not just in acceptance criteria).

### 4. Build a Debug Global for Runtime Config Verification

Add `window.__SPRK_RESOLVED_CONFIG__: SprkkRuntimeConfig | null` as a debug aid that gets populated after `resolveRuntimeConfig()` completes. This makes local verification trivial (open browser console, inspect the global) and speeds up debugging when a config value is wrong.

### 5. Stage Parallel Agents in Waves, Not All at Once

Rather than spawning all 9 Phase 3 agents simultaneously, spawn 3-4 first to validate the pattern, then spawn the rest once the first wave succeeds. This catches pattern errors (like the BFF_API_SCOPE omission) early, before the same mistake propagates to all 9 agents.

### 6. Include a "Verify by Search" Step in Each Task

Each task should include a final step: "Run `grep -r '<pattern>' src/ --include='*.ts'` and confirm zero results." This makes acceptance self-evident and catches the cases (like task 033) where the wrong pattern was used.

### 7. Lock the resolveRuntimeConfig() API Before Parallel Execution Begins

Write a short stub implementation and a type definition file that is committed before Phase 3/4/5 agents start. Agents importing from `@spaarke/auth` will then get TypeScript type errors immediately if they use the wrong signature, rather than silently shipping wrong code.
