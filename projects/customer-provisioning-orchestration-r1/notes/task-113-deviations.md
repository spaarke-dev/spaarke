# Task 113 — Deviations

**Task**: Author `scripts/provisioning/Deploy-ControlPlane.ps1` (L2 repeatable deploy script, C5.9/C1.7)

## Deviation 1 — File location: `scripts/provisioning/` instead of `scripts/` (Path C — pivot to comply)

**POML said**: `scripts/Deploy-ControlPlane.ps1` (flat, alongside `Deploy-BffApi.ps1`).

**What actually shipped**: `scripts/provisioning/Deploy-ControlPlane.ps1`.

**Why**: the dispatching orchestrator's own task instructions explicitly named `scripts/provisioning/Deploy-ControlPlane.ps1`, and task 111's `Grant-ControlPlaneIdentity.ps1` (the other L2-operator script, already landed) lives at that same path. Placing this script alongside its sibling keeps every L2-control-plane-specific operator script in one directory, distinct from customer-lifecycle scripts (`Decommission-Customer.ps1`) and the BFF's own deploy script (`Deploy-BffApi.ps1`) at `scripts/` top level. No functional difference; purely a placement clarification the POML's authoring predates.

## Deviation 2 — Fixed a discovered defect: `.Api` had no `/healthz` route (fix-at-discovery, CLAUDE.md §6.5 / repo memory principle)

**Discovered**: `Sprk.Provisioning.ControlPlane.Api/Endpoints/HealthEndpoints.cs` mapped only `GET /ping` — there was no `/healthz` route. `modules/controlplane-app-service.bicep` (task 033) has declared `healthCheckPath: '/healthz'` on the App Service resource since the module's creation, meaning the Azure App Service **platform-level instance health probe has been 404ing against the live `.Api` site** since first deploy — an independently-discovered, pre-existing defect unrelated to this task's authoring.

**What shipped**: added `GET /healthz` to `HealthEndpoints.cs`, mirroring the exact pattern the `.Worker` host already ships (task 100 `Program.cs`) — anonymous, trivial, `Results.Text("ok", ...)`. `dotnet build` clean (0 warnings/errors); full L2 unit suite still green (736/736).

**Why fixed here rather than deferred/escalated**: (a) it is a 6-line, zero-risk addition mirroring an already-proven pattern in the sibling host, not a design decision; (b) leaving it unfixed would force this script into an ugly per-target special case (`/ping` on `.Api`, `/healthz` on `.Worker`) that would silently normalize the platform-probe defect rather than closing it; (c) the repo's own operating principle ("fix drift at time of discovery... later means lost + perpetuated") directly applies. This is a Path C decision — the fix is unambiguous, not a judgment call requiring escalation.

**Scope-boundary note**: this widens the task's file footprint beyond the POML's single-output (`scripts/Deploy-ControlPlane.ps1`) to include one `.cs` file. Per root CLAUDE.md §8 Step 0.5, rigor was overridden UP from the POML's authored `STANDARD` to **FULL** for this reason (code file modified) — `code-review` + `adr-check` were run against both changed files (script + `HealthEndpoints.cs`) at Step 9.5.

## Deviation 3 — Documented (not fixed) a second, larger discovered gap: `.Worker` config-key shape still diverges from `.Api`'s NFR-05 canonical shape

**Discovered**: task 109 (DS-5 C5.1) renamed `.Api`'s Bicep-declared config keys to the canonical NFR-05 shape (`Cosmos__AccountEndpoint`, `ServiceBus__FullyQualifiedNamespace`, `ManagedIdentity__ClientId`, MI-only — no connection string). Task 109's own `TASK-INDEX.md` row explicitly files a follow-on: *"controlplane-worker-app-service.bicep (task 101) carries the identical C5.1 key-shape drift, out of this task's scope."* That follow-on was never picked up by a numbered task. `modules/controlplane-worker-app-service.bicep` (verified by direct read during this task's authoring) still emits `Cosmos__Endpoint` / `Cosmos__Database` / `Cosmos__RunsContainer` + `ServiceBus__ConnectionString` (KV-ref) — and has **no** `ManagedIdentity__ClientId` setting at all (only the Azure-native `AZURE_CLIENT_ID`).

**Why NOT fixed here**: the dispatching orchestrator's own task instructions stated *"Bicep deploy (uses task 108's queue + task 109's config-key fixes + task 101's Worker module + task 110's SB RBAC — assume all Bicep is now correct)"* — Bicep changes are explicitly out of this task's scope, assumed-correct by the orchestrating layer. Silently "fixing" Bicep here would be scope creep on a file another task line owns, and would risk colliding with whichever future task eventually closes this gap.

**What shipped instead**: `Deploy-ControlPlane.ps1`'s config-key verification uses **two distinct required-key lists** — `$script:ApiRequiredConfigKeys` (the canonical NFR-05 shape) and `$script:WorkerRequiredConfigKeys` (the Worker's actual current, pre-fix shape: `Cosmos__Endpoint`, `AZURE_CLIENT_ID`). This is documented prominently in the script's own `.DESCRIPTION` ("CONFIG-KEY VERIFICATION — DISCOVERED DRIFT") and `.NOTES` sections, plus a runtime `Write-Info` reminder printed on every successful Worker config check, so the gap stays visible rather than silently normalized. Checking the `.Api`-shaped keys against the Worker would make the script **permanently fail** for `-Target Worker/Both` today — not because the Worker is misconfigured, but because the check itself would be wrong.

**Escalation assessment**: this is surfaced here (Path C-adjacent — "pivot to comply with current reality, not the assumed-correct premise"), not raised as a full §6.5 ADR-conflict escalation, because it is not an ADR conflict — it is a factual correction to the orchestrating task's "assume Bicep is now correct" premise for ONE specific parameter (Worker config-key shape), discovered via direct file read, not inference. Recommend a follow-on task (no number assigned as of this writing) to rename the Worker's Bicep-declared keys to match `.Api`'s canonical shape; when that lands, `$script:WorkerRequiredConfigKeys` in this script needs updating too (noted inline in the script).

## Deviation 4 — Fixed a PowerShell pipeline defect discovered during `-WhatIf` testing (self-contained; also present in the task-111 sibling script)

**Discovered**: `$azProbe = az --version 2>$null | Select-Object -First 1` followed by `if ($LASTEXITCODE -ne 0 -or ...)` intermittently reports `$LASTEXITCODE` as `$null` (not `0`) after the pipeline runs, because `Select-Object -First 1` can signal early pipeline-stop before the native `az` process's exit code is captured by PowerShell. Since `$null -ne 0` evaluates `$true`, this causes a **false negative**: the script would report "Azure CLI not found" even when `az` is present, authenticated, and healthy. Reproduced directly in this environment during `-WhatIf` testing (`az` present + working, yet the pre-flight check failed until fixed).

**Same pattern exists** in `scripts/provisioning/Grant-ControlPlaneIdentity.ps1` (task 111, already landed) at its own `$azProbe = az --version 2>$null | Select-Object -First 1` line — flagged here for awareness but **NOT fixed in that file** (out of this task's scope; that script belongs to task 111's completed deliverable). Recommend a small follow-on fix to that script if/when it is next touched.

**What shipped in THIS script**: capture the native command's full output to a variable BEFORE piping through `Select-Object -First`, avoiding the early-pipeline-stop entirely. Verified fixed via direct reproduction (`$LASTEXITCODE` correctly reads `0` after the fix; pre-flight now passes against a real, authenticated `az` session).

## Verification performed (author-time, no live Azure deploy per task instructions)

- `Invoke-ScriptAnalyzer` against `PSScriptAnalyzerSettings.psd1`: **clean, zero findings**.
- `dotnet build` on `.Api` (post-`/healthz`-fix): 0 warnings / 0 errors.
- `dotnet test` on `Sprk.Provisioning.ControlPlane.Tests`: **736/736 pass** (no regression from the `HealthEndpoints.cs` change).
- Live `-WhatIf` dry-run against `-Target Api`: real `dotnet publish` + `Compress-Archive` (10.01 MB zip) succeeded; `az webapp deploy` call correctly skipped under `-WhatIf` (`What if: Performing the operation...`); exit code 0.
- Live `-WhatIf` dry-run against `-Target Worker`: pre-flight passed; `dotnet publish` correctly FAILED (exit 1, clear diagnostic message) because `Sprk.Provisioning.ControlPlane.Worker/Dispatch/ProvisioningHandlerDispatcher.cs` is a currently-untracked (`git status` shows `??`), mid-edit file belonging to the concurrently-running sibling agent for task 102 (the dispatcher) in this same shared worktree — NOT a defect in this script. This is expected concurrent-worktree behavior (per root CLAUDE.md's coordination-hygiene guidance) and, incidentally, positively demonstrates that the script's build-failure path surfaces a clear, actionable error and a non-zero exit code rather than failing silently.
- `-Target Bogus` correctly rejected by `[ValidateSet('Api','Worker','Both')]` before any work begins.
- `Get-Help -Full` renders full comment-based help including `-WhatIf`/`-Confirm` in the auto-generated syntax block (proof `SupportsShouldProcess = $true` is wired).
- Acceptance criterion #3 (manual smoke test — verification is load-bearing, not decorative when a config key is deliberately broken) was **not** exercised against a LIVE broken app-setting, per the task's explicit "do NOT execute against live Azure" instruction. The `Get-MissingRequiredConfigKeys` function's logic was read-reviewed and its failure path is unconditionally wired into `$script:VerificationFailures` (which drives the non-zero exit) — the same code path structurally proven live via the Worker publish-failure test above.

## Component justification (CLAUDE.md §11) — carried from POML `<notes>`

Existing — `Deploy-BffApi.ps1` is the proven sibling pattern for App Service publish + zip-deploy + verify (incl. the slot-swap-with-rollback flow this script reuses almost verbatim for `.Api`). Extension — this script extends that pattern to L2's two-site topology (`.Api` WITH a slot, `.Worker` WITHOUT one per DS-3 §3 Option 2) plus two L2-specific verification steps (fleet queue session/dedup properties, NFR-05 config-key presence) the BFF script has no equivalent for; it deliberately does NOT reuse `Deploy-BffApi.ps1`'s Windows/IIS-specific web.config stdout-logging step or its SHA-256 file-hash "did the DLL actually get replaced" verification, since both are workarounds for Windows App Service quirks that do not apply to L2's Linux App Service targets. Cost-of-doing-nothing — without a repeatable deploy script, every subsequent Wave G task needing a live deploy (dispatcher smoke test, sidecar verification, H13 live probes) repeats ad-hoc `az` commands, perpetuating the exact "live binary ahead of committed source" drift DS-5 §B.6 flags today.
