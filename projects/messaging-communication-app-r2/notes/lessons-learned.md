# Lessons Learned — messaging-communication-app-r2 (Communication Workspace)

> **Code-complete**: 2026-07-19 · 20 work tasks + wrap · 8654 tests pass / 0 fail · ~46.24 MB publish

## What went well

- **Build-and-defer-live held up end-to-end.** Dataverse MCP was offline the whole execution session, yet every task shipped a real deliverable: schema tasks (002/003) authored idempotent describe-before-write scripts; BFF tasks (010/011/050/051/070/071) compiled against field logical-name strings and unit-tested against the mocked `IImpersonatedCommunicationQuery`/Dataverse boundary; UI tasks built + Jest-tested. Live apply/deploy is a documented owner gate — the exact R1 pattern. Nothing was faked or silently skipped.
- **Parallel fan-out was the right execution model.** 21 POMLs authored by 6 wave-scoped subagents; execution ran waves of ≤5 concurrent subagents on disjoint files. The two shared-`Services/Communication/` writers (050 participant-write, 070 auto-threading) were serialized (`parallel-safe:false`) and never ran concurrently; everything else (reads, frontend, config) parallelized cleanly. Build-verify between BFF waves (568 → 585 Communication tests green) caught nothing broken because the disjoint-file discipline held.
- **Subagents caught cross-task defects the plan didn't anticipate** — and surfaced them instead of papering over: 020 found 010's `by-regarding` DTO dropped `sprk_name` (added `ThreadReadResult.Name`, additive); 022 corrected the primary-name assumption (matter/project/event use dedicated name fields, per the server's own `GetPrimaryNameField()`); 080 self-caught a tautological mirror-test (ADR-038 §7) in its own code-review; 030 fixed a real `tsc rootDir` cross-package boundary.
- **The ADR-034 path-C decision paid off.** Two typed lookups (not the 6-target tuple) gave FK integrity + DataGrid person-chip auto-derivation; ADR-048 documented it as comply-with-intent at the point of decision (§6.5), so every downstream `adr-check` validated against a landed anchor rather than flagging an undocumented divergence.

## What to watch / carry forward

- **Server-side triggers for client-side edits.** FR-07's re-derive-on-regarding-change couldn't be completed because thread edits are client-side `Xrm.WebApi` writes that never reach the BFF. The re-derive *method* is built + gated; the *trigger* needs a Dataverse plugin. Design lesson: when a BFF behavior must fire on a Dataverse mutation that originates in the UI, budget a plugin/webhook from the start.
- **"Chips auto-derive from columns" is only half-true.** `chipDiscovery.ts` skips Lookup chips in auto mode — regarding/person chips need an explicit `filterChips` block. Verify framework behavior against the code, not the header comment (041 did).
- **Schema-heavy Wave 0 is inherently mostly-sequential.** The audit (001) gates the schema tasks; the ADR (004) is main-session. Real parallelism started at the code waves. Front-load the audit.
- **Pre-existing cross-shell build gaps** (Compose deps) surface when a new shared lib forces a full bundle — not caused here, but they block the full prod build on both shells. Worth a portfolio-level fix.

## Deferred / follow-on (see README Open findings + Owner deploy gates)

1. Dataverse plugin to trigger thread-name re-derive (071).
2. Read/unread-state field + MetricCard drill-through wiring (023).
3. Cross-shell Compose-dep prod-bundle fix (030, pre-existing).
4. All live-Dataverse schema apply + PCF/page/config deploys + live verification.
