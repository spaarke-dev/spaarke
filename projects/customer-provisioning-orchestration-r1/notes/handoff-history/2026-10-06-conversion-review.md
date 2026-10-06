# current-task.md conversion — items to verify (2026-10-06)

`current-task.md` was converted from a 450 KB stacked journal to a current-state file, per the repo procedure change in `.claude/skills/context-handoff/SKILL.md` "State, not history".

- **Nothing was deleted.** The original is at `current-task-archive-2026-10-06.md` in this folder, byte-identical (it includes the SESSION 40 END checkpoint, commit `fd49c6513`).
- **Current state** was taken from that checkpoint plus the open lists that are still current.
- **Standing items** were moved to the project `CLAUDE.md` "Standing directives & gotchas".

The items below could not be classified confidently. Line numbers refer to the ARCHIVE (approximate: two header lines were added after the extraction read it). Resolve them when convenient.

## Possibly superseded directives
1. ~L1656, L1999–2001 (2026-08-23 owner Q1–Q3): "Do NOT enable Managed Environments / Env Groups / PAYG for Model 1" — decided for the SHARED Model 1 environment; still true for dedicated per-customer Dataverse environments after D-12?
2. ~L1937 vs L77: the 2026-08-23 lesson says pac has no `--help` and running it with flags EXECUTED `create-service-principal`; S39 says run `--help` locally. CLAUDE.md keeps the S39 rule marked *(verify)*. Confirm the safe way to check pac usage.
3. ~L568 (S22): "use 'tenant' only for the Entra tenant; 'customer' everywhere else" — scoped to T216 (dropped). Standing naming rule or not?
4. ~L611 + memory `reference_azure_fresh_sub_regional_gotchas` (westus2 platform + westus3 OpenAI): T247 moved to one DataZoneStandard set with `openAiLocation` checked by H0 — is the westus3 default superseded? If so, update the memory file.
5. ~L2264 (2026-08-20): "use existing `SpaarkeMaster` (not v2)" vs T218 (2026-09-28) allowing new/consolidated solutions — T218 should resolve.
6. ~L1050 (S11 Q4) "KEEP trial1 env PERMANENTLY" — almost certainly superseded by D-12 (T241 deletes `rg-spaarke-trial01-prod-model1`), but nothing says so explicitly.

## Possibly still-open items with no recorded outcome
7. ~L182 (S36 item 5): demo entries in `config/environments.json` + `spaarke-resources.yaml` name `spaarke-bff-prod` / `rg-spaarke-platform-prod` — owner to confirm which is right.
8. ~L170 / L186: `RoutingConsumerTypeHealthCheck FAILED: AI catalog drift` on dev BFF — "reported to owner", no outcome. Under fix-drift-at-discovery this may be an open item.
9. ~L177 / L193: BFF MI lacks `SecurityEvents.Read.All` (§6B Security tab) — no owner/task.
10. Dev Redis memory-trend recheck (S37) — kept in the new file as live state; may already be closed.

## Stale lines noticed in the project CLAUDE.md itself
- Line ~119: "MUST NOT delete `Dataverse-ClientSecret` / `BFF-API-ClientSecret` (BINDING per r3 handoff)" — replaced by the KV credential-lifecycle section in `.claude/constraints/provisioning.md`.
- Line ~116: "MUST use confidential-client (app-only) token for SPE container-type creation (T6)" — contradicts topology §R5 / H8-B (container-type creation is delegated-only).
- Line ~11: "812 commits behind master" — outdated.
