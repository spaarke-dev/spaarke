# Task 204g — Execution Results

**Task**: `204g-classB-spec-amendment-sc2-retired-onDisk-convention.poml`
**Executed**: 2026-08-24
**Rigor**: MINIMAL (spec amendment only; no code / tests / `.claude/` writes)
**Punch-list row**: B21 (Class-B Verified-Open, Wave G-8 spec-amendment task)

## Summary

Amended `spec.md` § Success Criteria § SC #2 to spec-sanction the **Wave G-6 retired-on-disk-with-banner convention** as a valid completion state. Reconciles SC #2's original zero-grep verification recipe with the retirement pattern uniformly adopted by tasks 125 (`AzCliKvSecretsWriter.cs`), 160 (`AzCliKvSecretReader.cs`), 161 (`ExchangePolicyScriptApplier.cs`), and 170 (`PlaceholderInvariantVerifier.cs`) — where retired shell-out classes are kept on disk with a top-of-file retirement banner (audit trail + reversibility) instead of physically deleted.

## Pre-amendment verification

Verified current SC #2 wording matches the punch-list B21 row description as of 2026-08-24 SESSION 7 (no drift; amendment premise still applies). Confirmed retirement-banner format is consistent across current examples:

- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/E2EAcceptance/PlaceholderInvariantVerifier.cs` (task 170, Wave G-7) — canonical example cited in amendment
- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/IntegrationWiring/ExchangePolicyScriptApplier.cs` (task 161, Wave G-6) — earlier exemplar
- Banner format: `// -----------------------------------------------------------------------------` frame, filename, `// RETIRED (task NNN, Wave G-X, YYYY-MM-DD): <what replaced it>`, rationale for on-disk retention, original scope. Consistent across all sampled files → no escalation trigger fired (the "inconsistent-format" trigger in POML `<escalation>` did not apply).

## SC #2 amendment diff

### Before (spec.md line 370, one-liner)

```
2. [ ] **19 idempotent handlers** — each of H0…H14 implements `IProvisioningHandler`, is 3-level idempotent per NFR-10, independently testable, reports outcome to the Cosmos run record, and executes pure .NET per §4.1b (H14a via sidecar) — Verify: integration test runs each handler twice; second run is no-op (L3 `completedPhases` match); grep confirms zero `ProcessStartInfo`/pwsh/az/pac in main-site handler collaborators (H14a's sidecar client excepted)
```

### After (spec.md line 370, expanded with Wave G-6 convention + amended verification recipe)

```
2. [ ] **19 idempotent handlers** — each of H0…H14 implements `IProvisioningHandler`, is 3-level idempotent per NFR-10, independently testable, reports outcome to the Cosmos run record, and executes pure .NET per §4.1b (H14a via sidecar). **Retired-on-disk-with-banner is a valid completion state** (Wave G-6 convention, spec-sanctioned 2026-08-24 task 204g per punch list B21): superseded shell-out classes MAY be retained on disk carrying a retirement-banner header comment (canonical form: `// RETIRED (task NNN, Wave G-X, YYYY-MM-DD): <what replaced it>` at top of file, within a `// ---` frame) instead of being physically deleted. Rationale: retained-with-banner preserves the audit trail of what was replaced + gives operators a one-file diff if the swap ever needs to be reverted (delete the swap line in the owning `*Module.cs`, add back the `AddSingleton<TInterface, RetiredClass>` line — no other change needed). Canonical example: `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/E2EAcceptance/PlaceholderInvariantVerifier.cs` (task 170, Wave G-7); earlier exemplars: `AzCliKvSecretReader.cs` (task 160), `ExchangePolicyScriptApplier.cs` (task 161), `AzCliKvSecretsWriter.cs` (task 125). **Prohibition preserved**: the retirement banner is on-disk-only — a retired class MUST NOT appear in any `*Module.cs` `services.AddSingleton<TInterface, RetiredClass>()` (or equivalent) DI registration; if it does, the amended check STILL fails. — Verify: (a) integration test runs each handler twice; second run is no-op (L3 `completedPhases` match); (b) shell-out primitive scan — any `Sprk.Provisioning.ControlPlane.Core/Handlers/**/*.cs` matching `ProcessStartInfo`/pwsh/az/pac MUST either be an allowed exclusion (H14a's sidecar client) OR carry a top-of-file retirement banner (i.e., `grep -L "// RETIRED" <matched-files>` returns empty after excluding H14a's sidecar client); (c) active-registration scan — `grep <RetiredClassName> **/*Module.cs` returns zero for every retired class (no on-disk banner rescues an active DI registration).
```

## Amendment intent (design rationale)

- **Preserved**: SC #2's core intent (zero active shell-out primitives in registered handler collaborators; each handler idempotent + testable). No handler-quality bar has been lowered.
- **Added**: explicit sanction for the Wave G-6 retired-on-disk-with-banner convention (already in use across 4+ files as of task 170).
- **Amended verification recipe**: split the original single grep check into three sub-checks (a) idempotency test unchanged, (b) shell-out primitive scan now allows banner-headed files, (c) NEW check that no `*Module.cs` DI registration references a retired class. This preserves the intent (no LIVE shell-out) while accepting the physical presence of retired files.
- **Canonical example cited**: `PlaceholderInvariantVerifier.cs` (task 170, Wave G-7) per POML instruction; three earlier Wave G-6 exemplars listed for completeness.
- **Sub-check (c) is the load-bearing new safeguard**: a retirement banner in the file is NOT sufficient by itself — the class must also be unregistered. This closes the loophole where someone could add a banner without removing the DI wiring.

## Acceptance criteria (all met)

- [x] spec.md § SC #2 explicitly allows retired-on-disk-with-banner as a valid completion state (see "After" diff above, sentence 2 + "**Retired-on-disk-with-banner is a valid completion state**" heading).
- [x] SC #2 verification recipe updated to distinguish retained-with-banner (Pass) from actively-registered (Fail) — new sub-checks (b) + (c).
- [x] SC #2 cross-references `PlaceholderInvariantVerifier.cs` retirement banner as current example — sentence 5 of amended text.
- [x] Amendment retains prohibition on ACTIVE registration of retired classes — explicit "**Prohibition preserved**" clause + sub-check (c) enforces it.
- [ ] `notes/task-202-punch-list.md` B21 row annotated — **DEFERRED per user instruction** (concurrent-write conflict avoidance; punch-list annotation is main-session responsibility, per user's explicit deviation from POML step 4).

## Escalation triggers (checked — none fired)

- **Escalation 1 (spec drift)**: SC #2 wording matches punch-list B21 row description; no drift. Amendment premise applies. Not escalated.
- **Escalation 2 (banner-format inconsistency)**: Both sampled examples (`PlaceholderInvariantVerifier.cs`, `ExchangePolicyScriptApplier.cs`) use consistent `// ---` frame + `// RETIRED (task NNN, Wave G-X, YYYY-MM-DD)` prefix. Not escalated. Canonical format inlined into SC #2 amendment for future authors.

## Files changed

| File | Change |
|---|---|
| `projects/customer-provisioning-orchestration-r1/spec.md` | Amended SC #2 (line 370): expanded one-liner to multi-sentence entry per diff above |
| `projects/customer-provisioning-orchestration-r1/notes/task-204g-execution-results.md` | New (this file) — per-task execution record; substitutes for the punch-list annotation (main session applies that) |

## Files NOT changed (per user instruction)

- `projects/customer-provisioning-orchestration-r1/notes/task-202-punch-list.md` — punch-list B21 annotation deferred to main session (concurrent-write conflict avoidance).
- No code files touched (per MINIMAL rigor scope).
- No `.claude/` files touched (per sub-agent write boundary + task scope).

## Downstream implication

Amendment MUST land BEFORE any future task retires additional shell-out classes via the Wave G-6 convention (e.g., a hypothetical task 204c that retires `PlaceholderInvariantVerifier.cs` would rely on Wave G-6 convention being spec-sanctioned to avoid a false-negative SC #2 fail on its own output). This task unblocks that pattern going forward.
