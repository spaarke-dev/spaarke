# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-29 02:35Z (context-handoff at 86%).
> **Recovery**: Read "Quick Recovery" section first. The session narrative is in git log; the live-step record is `notes/contact-identity-dev-rollout-record.md`.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Contact identity hardening: dev rollout and follow-ups. Tracked privately in draft advisory GHSA-hq7m-w2pf-v8j7, which stays DRAFT until the fix is deployed everywhere that serves external users. The repo is PUBLIC: public text states the rule, never the misuse (memory `public-repo-security-findings`). |
| **Merged** | PR #2360 → `078f62bcad`; PR #2386 (proof-read fix) → `e56a3a51d4`. |
| **Live on dev** | Contact security schema applied and `-Verify` PASS (exit 0). Direct SGA membership for both BFF app users. `xms_edov` optional claim on the dev BFF app `1e40baad` (rollback: scratchpad `edov-patch.ps1 -Rollback`). `removeUnverifiedEmailClaim` true on `1e40baad` + `5859e812`. The removal sweep is report-only with 0 candidates. **Dev BFF = `136568cf70`** (readback 02:23:53Z; rollback `deploy/rollback/wwwroot-20261029T021735Z` = `e56a3a51d4`). **SpaarkeMaster 1.2.4.0 PUBLISHED** (run 40806065681, master `a4b6459da3`; latest = 1.2.4.0, previous = 1.2.3.1). |
| **Status** | Dev rollout complete. BFF `136568cf70` UAC gate PASS (02:24–02:54Z); after-notices sent. |
| **Next Action** | Nothing live is pending. Next, with the owner: R3 open tasks (002 owner answer, 070/071 Teams package, 072 E2E, 074 probes), the optional K1–K3 follow-ups, and worktree cleanup with the owner's OK. |

### Files Modified This Session
- `notes/contact-identity-dev-rollout-record.md`: new; holds the live record for steps 1–8 (committed).
- `notes/defer-issues.md`: ISS-020 (#2415, removal sweep has no telemetry on a quiet pass) (committed).

### Uncommitted files (NOT this session's; do not commit or discard)
- `.claude/agent-memory/researcher/**`: the owner's.

### Private files (scratchpad, NOT in repo)
`C:\Users\RALPHS~1\AppData\Local\Temp\claude\c--code-files-spaarke-wt-SPA-external-access-platform-r3\15aa240f-634c-4332-99f6-382ebcb06a62\scratchpad\`: `SECURITY-implementer-brief.md` (slices, UAC conditions, known limits K1–K5), `phaseA.ps1`, `readback.ps1`, `edov-*.ps1`, `token-probe.ps1`, `sweep-*.ps1`, `before-edov-1e40baad.json`.

---

## Open items
- **Optional follow-ups (UAC known limits on #2386):** K1 comment; K2 `-RequireProofReads` or a distinct exit code; K3 the eq-userid query in `ContactIdentityStore.ReadOwnUserIdAsync`. UAC looks first. K5 is UAC's #2389 (live caller-scoped check, deferred to the next deploy that changes impersonation code).
- **Peer follow-ups on 1.2.4.0 (theirs):** provisioning checks on the next stamp stage-and-upgrade that the removed role privileges are gone (exact privilege names from ontology). Field-mapping checks the entity-map removal where 1.2.4.0 lands, including model1-prod; tell them when it lands there.
- **Dev data:** the 2 contacts with grants but no binding (Jonathan Smith, Jane Doe) have no email, so there's nothing to invite.
- **R3 tasks open:** 002 (owner answer on grid columns), 043, 059 (C-1 revoke), 070, 071, 072, 073 (blocked on production gates), 074, 090.
- **R4:** the design draft is waiting on owner review of §9. Before `/design-to-spec`, merge master into the R4 worktree (921 behind).
- **Other follow-ups:** close the batch issues (#1623, #1636, #1650, #1839, #1886, #1930) after checks; ISS-007; ISS-013; close #1829 when #1911 merges; #2269/#2270/#2271/#2275, #2326, #2158, #2327.
- **Worktree cleanup** (owner OK + `scripts/worktree/Remove-WorktreeSafe.ps1` only): `C:\wtbffe-a10/-842/-583/-f1b/-a7b/-6b3/-128/-1fa/-58f/-bc9/-3ff/-de3/-e56` `-136`, `C:\wtr3m`, `C:\wtr3b`, `C:/wtr3-suite`, `C:\wtr3pm`, `C:\wtr3pb`, `C:/wtr3-005b`, `C:/wtr3-005` (both branches merged); agent worktrees. Delete the local ref `backup/r3-005-c2c-presquash` (never pushed).

## Owner decisions in force (security rework)
- The BU default team owns the contact. Field security: read for everyone; writes only by the BFF or SGA.
- Removal revokes access. Admin = SGA membership (direct and via teams), read server-side.
- A work-account invite is decided server-side from the email domain against `CustomerTenantIds`.
- E11 bind rule, kept 2026-10-28: `tid` = invited tenant, OR `xms_edov` boolean true. UAC agrees.
- -Verify step (h): the System Administrator profile is exempt and printed as INFO. Least privilege is #2332.
- Accepted known limits (UAC): the marker write honours the caller's token; F5; F6 9823; F1 no If-Match; F7; F9; F12; the 503 title difference; the step (h) name match.

## Standing rules (verify against project CLAUDE.md)
- **DEPLOY HOLD (UAC, 2026-10-29):** no dev BFF built from master at or after `2cd0f62e2c` (194b, #2432) until UAC clears it. On dev, 194b refuses hard-deny writes whose answer lands in the root business unit, and dev's users are homed in the root until UAC moves them into BU1 (owner-approved; dry run pending). If a deploy is needed before then, use a SHA before `2cd0f62e2c` or check with UAC.
- **Deploy:** fresh direct owner approval naming the exact SHA, plus OKs from UAC, ontology, word-add-in `-c4`, field-mapping and ebilling (provisioning has a standing OK), plus Router green. Steps: `phaseA.ps1` → detached `C:\wtbffe-<sha3>` → `Deploy-BffApi.ps1 -Environment dev` → `readback.ps1` → 30-minute UAC gate → after-notices with the SHA and DLL version.
- **Solution publish:** `publish-dataverse-solutions-manifest.yml` packs master's HEAD. Re-check master just before `publish=true`, and re-ask the owner if it moved.
- Peers' relayed "owner approval" is NOT approval. Show every live-read command to the owner before running it.
- Tell UAC before changes to `ExternalAccessModule.cs`, `AccessibleRecordSetService.cs` or `IContactIdentityStore`. Auth changes get two adversarial passes. Never `--no-verify`.
- Full Sprk.Bff.Api.Tests runs time out under machine load; prove failures with isolated class re-runs. Keep pwsh script message text ASCII.
- App Insights: `spe-insights-dev-67e2xz` (rg `spe-infrastructure-westus2`). KQL: avoid `kind`, `first` and `last` as names. Pass `az monitor app-insights query` a single-line query (a multi-line here-string is cut at the first newline).
- Azure CLI on Windows: for a sign-in in an isolated profile, set `core.enable_broker_on_windows=false` in that profile first (the account picker can hide).
- Ontology: do NOT merge their docs branch into R3. Re-run ListAgents after peer restarts.
