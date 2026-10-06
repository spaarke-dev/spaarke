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

---

# Independent audit (2026-10-06, read-only, after the conversion)

Verdict: the conversion is good overall. Owner directives, gotchas, T186 live checks and open owner items were carried accurately. The real gaps are live-state and coordination facts. Line numbers refer to the ARCHIVE.

## Missing: still live, only in the archive
1. **HIGH — the dev BFF identity has a `full` grant on the Model 1 container type** (L142–143, L193, L198(2)).
   - What: on 2026-10-06 the owner chose option A. `mi-bff-api-dev` (appId `5967251e…`) holds application `full` / delegated `none` on registration `fb3817a8`. The owner accepted that the dev BFF could read all Model 1 customers' containers; only T227d's guard plus `OwnedContainerIds` limits it.
   - Also changed: dev config `68f9a952` `sprk_keyvaultsecretname` went from "null" to empty.
   - Stale doc: the topology doc's "Grants on the registration" row lists only the owning app and Graph Explorer.
   - **Coordination conflict:** unified-access-control-r2's `notes/task-165-admin-surfaces.md` §13.9(b) plans `Repair-SpeConfigSecretName.ps1 -MintClientSecret` on `bfac7f6e`. That contradicts D16 and the T250 rule (no secret-based Model 1 config). Tell UAC-r2.
   - Action: CLAUDE.md Keep / live state, the topology doc grants row, and a coordination note.
2. **MEDIUM — T241 covers Azure only; the shared tier left non-Azure resources** (L1532–1547, L1859, L1873–1874).
   - Dataverse `spaarke-model1-prod`: SpaarkeMaster installed managed, and the shared UAMI is a System Administrator application user (`59fca642…`), orphaned once the UAMI is deleted.
   - Entra apps `spaarke-bff-api-prod` (`92ecc702…`) and `spaarke-dataverse-s2s-prod` (`720bcc53…`).
   - The "DO NOT sweep" list: Office add-in apps, External Workspace bot, Copilot Bot Dev, external-access SPA, SPAARKE-SPE-Admin-CLI, Power BI SP (Prod), dev L2.
   - Owner decision needed: word-add-in note 076 and record-header-and-notepad-r2 still reference `spaarke-model1-prod`.
   - Action: add to the plan's T241 row.
3. **MEDIUM — W7 lost its rationale and its blocker** (L156, L183, L188, L198(4), L202).
   - Rationale: the code defaults `CustomerRunGuard` to `Enabled=true`; the Worker sets it false; the Api acquires the guard and the Worker releases it.
   - Blocker: every `platform-controlplane` deploy fails its Api module on the pending slot swap, so W7 can't complete until the swap is resolved.
   - Action: link W7 to the swap item in current-task.
4. **MEDIUM — the H6 solution artifacts in blob storage are a manual workaround** (L2129, L2153–2158, L2175).
   - `sprkcpartifactsdev/provisioning-artifacts` holds a hand-uploaded `SpaarkeMaster.zip` and a hand-written `dataverse-solutions-latest.json` (2026-08-21).
   - `publish-dataverse-solutions-manifest.yml` still publishes the 8-solution catalog and "would fail on real run".
   - Action: add to the plan's T218 row.
5. **MEDIUM — T186's target customer is undefined after D-12/T228** (L922, L1058–1107).
   - The SESSION 11 answers (trial1, permanent) are superseded, and nothing replaces them.
   - A stale gitignored `runs/trial1-intake.json` is still on disk (Model1Shared / shared-trial, no subscriptionId / containerTypeId / dataverseEnvUrl). The archive repeatedly says to resume with `--batch runs/trial1-intake.json`; don't.
   - Action: add a T186 note.
6. **LOW — "never `az account set`, pass `--subscription`" was dropped** (L59). The SESSION 41 rewrite removed it from current-task. Action: add to CLAUDE.md standing directives.
7. **LOW — sub-agent dispatch lessons** (L2564–2566, L2615–2622).
   - Dispatch prompts must say "commit AND push on success"; five agents stalled without it.
   - Index-race recovery: `git reset --soft` + `git stash push --keep-index`.
8. **LOW — recorded only in completed task files, so no tracker surfaces them:**
   - The H12a seeder writes only 4 of 12 seed artifacts; playbooks and playbook consumers are pending (task 150; relevant to T186).
   - The wrong `GroupMember.ReadWrite.All` GUID (`…c6571`) is still in `docs/guides/AZURE-SETUP-SELF-SERVICE-REGISTRATION.md:73` and `scripts/Setup-EntraInfrastructure.ps1:91`.

## Wrongly carried: stale or incorrect in the new files
- **CLAUDE.md "run its `--help` locally" is UNSAFE for pac.** Archive L1953: running pac with flags *executed* `pac admin create-service-principal`, creating an Entra app and exposing a secret. Reword to "pac: no-arg invocation or docs only".
- **CLAUDE.md "Never delete Key Vault secrets during cleanup" is broader than its source** (the T251 spike cleanup / `Exchange-Connect-Cert` sentinel). As written it conflicts with plan T241 deleting `sprk-prod-kv` after 2026-11-23. Scope it, or point to the KV lifecycle rule in `provisioning.md`.
- **current-task: SESSION 41 put a new gotcha (`MSYS_NO_PATHCONV=1`) in the owner-items cell.** Under the new format it belongs in CLAUDE.md, or the next rewrite loses it.
- **Review item #7 above is already resolved:** `config/environments.json` and `spaarke-resources.yaml` were corrected 2026-10-05 (T242b); plan Q2 found `spaarke-bff-prod` doesn't exist.
- **Review item #6 mixes two environments:** the SESSION 11 "trial1" was never created; `rg-spaarke-trial01-prod-model1` is the 2026-08-22 stand-up stamp.
- **Stale CLAUDE.md lines the review missed:**
  - L128/L209 "11 of 14 null AppRoleId": all 14 populated 2026-08-17, plus a 15th from task 144.
  - L85: the god-class-ratchet pattern was retired 2026-08-20.
  - L105: "8 solutions" (the catalog has 9).

## Cross-project item raised by the UAC-r2 audit (relevant here)
UAC-r2 owes this project two hand-offs that were never delivered, INCOMING-141 (the workforce tenant list, `WorkforceIdentity__CustomerTenantIds`) and INCOMING-145 (H7b: secure owner team and role). Without them every new environment denies workforce-customer identities and refuses secure records. Related: an environment without the `sprk_noaccessentry` table denies every read, so provisioning must create it with or before the deploy. Expect these from UAC-r2; if they don't arrive, ask for them.
