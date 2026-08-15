# Archived R3-Draft Reconciliation — 12 Items VERIFY'd (task 090)

> **Date**: 2026-08-14 · **Spec**: FR-22 (no item dropped silently) · **Source**: design.md §3
> Each item's disposition (CARRY / ABSORB / UPGRADED) is confirmed against what the program actually did.

| # | Item | Disposition | VERIFY (actual outcome) |
|---|---|---|---|
| 1 | `OfficeService.cs` decomposition (1,951 LOC God class) | CARRY → BFF/shared-server | ✅ God-class guard armed (`GodClassGuardTests`, baseline 4950 incl. this class); LOC re-measured in the BFF assessment; decomposition tracked under the God-class fitness function (forcing-function prevents further growth). |
| 2 | 3 ADR-022 violations (ReactControl pattern) | CARRY → PCF assessment | ✅ Re-scanned in the PCF surface assessment (task 013); dispositions in that surface's design. PCF graded C+. |
| 3 | `@spaarke/auth` workspace package | CARRY → shared-client-libs | ✅ Assessed (task 010, client-libs B–); `@spaarke/auth` is the canonical auth contract (ADR-028) used by the Finance auth closure (task 023). |
| 4 | Integration-test infra (138 failing, KV-dependent) | CARRY → test-quality horizontal | ✅ Reconciled with ADR-038; task 031 conservative sweep (`1d885d6c9`); project-close `/test-diet` clean (this task, `notes/test-diet-report.md`). |
| 5 | ESLint reduction (181) + `--max-warnings 0` gate | CARRY → forcing-functions + client surfaces | ✅ C# mechanical baseline ACTIVATED repo-wide (task 041; BFF last opt-out flipped, warnings fixed not suppressed). TS `--max-warnings 0` per-surface activation is documented ongoing follow-on (`notes/task-041-mechanical-baseline-activation.md`) — per-surface by design (§4A). |
| 6 | `console.log` cleanup → shared logger (117) | CARRY → client surfaces + `no-console` gate | ✅ `no-console` is part of the TS mechanical baseline (task 041); per-surface activation follow-on documented. BFF observability (task 033) scrubbed server-side console/PII. |
| 7 | PCF build infra (memory, per-control CI) | CARRY → build/config-sprawl surface | ✅ Assessed under config-deployment (task 017, 69 package.json roots noted); publish/bundle-size budget authored in task 042 (CI wiring deferred to coordinated PR). |
| 8 | God-class audit (>800 LOC / >12 ctor deps) | ABSORB → re-baseline method (§6) | ✅ Became the rubric's D-dimension method + the `GodClassGuardTests` fitness function (task 040). "Feeds future R4" → this program IS it. |
| 9 | `BaseProxyPlugin` invert vs decommission (ADR-002) | CARRY → plugins surface | ✅ Assessed (task 015): recommendation = **decommission** (retired `[Obsolete]`; pattern docs updated task 034). Plugins D3=D pending the live decommission (deferred; a deployment/live-env action, not r3 code). |
| 10 | Two parallel Dataverse implementations | UPGRADED — live bug + split | ✅ Confirmed live: 13→1 downcast collapse via `UnwrapServiceClient` (task 028) + `DataverseServiceClientDowncastTests` fitness function (task 040). Full stack-unification split to NG1/task-011 (assess-then-decide, Idea #742). |
| 11 | Unit-test coverage gaps in R2-decomposed services | CARRY → test-quality horizontal | ✅ Coverage = observation, not gate (ADR-038); reconciled in task 031 + project-close `/test-diet`. |
| 12 | PCF bundle-size (tree-shaking, `pcf-scripts`-blocked) | CARRY → client surfaces (informational) | ✅ Noted informational in the PCF/client assessment; tooling block still applies; bundle-size budget authored (task 042, CI wiring deferred). |

**Net: no item dropped silently.** 10 CARRY items folded into a surface assessment or the
forcing-functions layer; #8 became the method itself; #10 was upgraded (live bug) + split (child fix +
deferred NG1 architecture project). Items #5/#6 carry a documented per-surface TS-activation follow-on;
#9 carries a deferred live-env decommission — both tracked, neither dropped.
