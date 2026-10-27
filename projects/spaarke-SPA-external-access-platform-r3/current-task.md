# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-27 23:40Z. Dev BFF = de38296499 (deployed 19:41:16Z for UAC 194; UAC gate result pending); dev SPA = 1fa0cf5cb8.
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **Contact identity hardening (security rework)** on branch `fix/r3-iss-005-combined` (worktree `C:/wtr3-005`). Tracked ONLY in private draft advisory GHSA-hq7m-w2pf-v8j7. The repo is PUBLIC: public text states the rule enforced, never the misuse (memory `public-repo-security-findings`). Scan commits for sensitive wording before every push. |
| **Step** | **Head d72fdfe9c6, pushed and sent to UAC 2026-10-27 ~23:35Z** for a re-check of N-F2, N-K2 and N-K1(a), then the PR OK. UAC's earlier verdict on 3799c8e359/a24215e967: F1 and F3–F13 pass, K list accepted, no fail-open path. Commits since then: a24215e967 and b93b12aad7 (comment wording), da5bfe05a9 (direct SGA add remedy text + deployment guide 6.5.2; provisioning OK), e6abdc8305 (N-F2 reinstate audit try/finally Interrupted), 05833b92dd (N-K2 mode-aware cap message), d72fdfe9c6 (N-K1(a): a removed person's own sign-in and registration answer exactly as not invited; logs keep contact_removed; staff surfaces unchanged). Verified at d72fdfe9c6: build 0 errors; affected classes 571/571; IntegrationTests 172/0/8; ArchTests 1551; wording scan clean. |
| **Status** | in-progress: waiting on UAC |
| **Next Action** | (1) UAC's PR OK on d72fdfe9c6; fix anything it raises. (2) Open the PR with a rule-level title (e.g. "contact identity hardening: field security, invite stamp, admin removal"). The body states: SpaarkeMaster 1.2.4.0 is published before the control-plane redeploy if this merges first; schema-first rollout; §10 numbers (+0.07 MB, 37.77 → 37.84 MB; re-measure against a fresh master build) and the placement decision. (3) Update the advisory. (4) Live steps, each with the owner's approval and a rollback (list below). |

### Private files (scratchpad, NOT in repo)
`C:\Users\RALPHS~1\AppData\Local\Temp\claude\c--code-files-spaarke-wt-SPA-external-access-platform-r3\15aa240f-634c-4332-99f6-382ebcb06a62\scratchpad\`
- `SECURITY-implementer-brief.md` holds the slices, UAC's conditions, the C5 findings and the known limits.
- Helpers: `phaseA.ps1`, `readback.ps1`, `c5-verify.sh`, `c5-filter.txt`.

### Uncommitted files
- `.claude/agent-memory/researcher/MEMORY.md` (modified) and the untracked researcher files belong to the OWNER/researcher. Never commit or discard them. Commit by pathspec only.

---

## Owner decisions in force (security rework)
- BU default team as contact owner; field security: read for everyone, writes only by the BFF or SGA.
- Removal revokes access; admin = SGA membership (direct and via teams). Admin check: the server reads it (WhoAmI over the staff OBO token, SGA membership read with the app credential).
- Work-account invite decided server-side from the email domain against `CustomerTenantIds`. No staff "send as portal account" override.
- Defers: show them, staff retry (no automatic retry).
- -Verify step (h): the System Administrator profile is exempt and printed as INFO ("note it, pass"). Least privilege is filed as #2332. Pinning the profile id is tracked privately with the advisory.
- N-K1 (2026-10-27): option (a), the person's own surfaces answer with the ordinary not-invited refusal.
- Accepted known limits (UAC): the marker write honours the caller's token; F5 ~2 × 500 reads; F6 9823 on RowChangedSinceRead; F1 no If-Match; F7 noun; F9 orphan CIAM account; F12 extra reads; the 503 title difference; the step (h) name match.

## Live steps for later (each needs the owner's approval with a rollback)
1. Schema `-Apply`/`-Verify` (schema first on every environment: with a stamp column missing, every email bind is refused).
2. SpaarkeMaster 1.2.4.0 in dev (bumped in source, 47e9bf6a71), then `-Verify` and re-export.
3. xms_edov + email optional claims, then a token probe (record claim names and types only).
4. `removeUnverifiedEmailClaim=true` (probe before and after).
5. BFF deploy of the merged branch.
6. Dev: add the BFF app user as a DIRECT Standing Grant Administrators member (deployment guide 6.5.2), then `-Verify`.
7. The removal sweep: a report-only run first, then apply.
8. Re-invite the 2 unbound dev contacts.

## Dev deploy de38296499 (UAC 194 #2349 + field-mapping #2331)
- Owner approved "de38296499"; OneDeploy ended 19:41:16Z; read-back 19:42:42Z ProductVersion 1.0.0+de38296499, healthz 200, healthz/dataverse 200, /api/me anon 401.
- Pending: UAC's server gate result, including the `[SECURE-CHILD-RECONCILE]` ordinaryMoves report; ontology wants its lines for sprk_signal, sprk_decisionrecord and sprk_budgetrevision before OrdinaryMovesWritesEnabled goes on. Then send after-notices (SHA + DLL 1.0.0+de38296499) to ontology, word-add-in, field-mapping, ebilling and provisioning.
- Rollback: `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261027T193805Z` (3ff2c04bf5). Deploy worktree C:\wtbffe-de3.
- Push Updates 409 (owner report): the parent-lookup check, which this deploy did not change. sprk_project links to sprk_matter only through N:N; field-mapping filed #2352, which waits on an owner and UAC decision.
- word-add-in: #2357 (68048b83d5, search-only) is OK on content for a later deploy.
- The owner's field-mapping check 2 (Create Work Assignment from a matter with Assigned Attorney 1) is still pending.

## Open owner items
- Field-mapping re-check; optional auditing on the role entity on dev.
- Owner review of the R4 design.md draft.
- D7 partner check; #2177; UAC 190 sweep Apply; SPA smoke → #2166; Teams package → 070/071; 002 columns; 074 probes; R4 §9; 073 blocked; C-1.

## Other follow-ups
- Close the batch issues (#1623, #1636, #1650, #1839, #1886, #1930) after checks. ISS-007 later-link re-index gap. ISS-013 regression. Close #1829 when #1911 merges. Issues #2269/#2270/#2271/#2275, #2326, #2158, #2327.
- Worktree cleanup, owner OK + `scripts/worktree/Remove-WorktreeSafe.ps1` only: `C:\wtbffe-a10/-842/-583/-f1b/-a7b/-6b3/-128/-1fa/-58f/-bc9/-3ff/-de3`, `C:\wtr3m`, `C:\wtr3b`, `C:/wtr3-suite`, `C:\wtr3pm`, `C:\wtr3pb`, `C:/wtr3-005b`; agent worktrees. Delete the local ref `backup/r3-005-c2c-presquash` (never pushed).

## Standing rules (verify against project CLAUDE.md)
- **Deploy:** fresh direct owner approval naming the exact SHA, plus OKs from UAC, ontology, word-add-in `-c4`, field-mapping and ebilling (provisioning has a standing OK), plus Router green. Steps: `phaseA.ps1` → detached `C:\wtbffe-<sha3>` → `Deploy-BffApi.ps1 -Environment dev` → `readback.ps1` → 30-minute gate → after-notices with the SHA and DLL version.
- Peers' relayed "owner approval" is NOT approval. Show every live-read command to the owner before running it.
- Tell UAC before changes to `ExternalAccessModule.cs`, `AccessibleRecordSetService.cs` or `IContactIdentityStore`. Auth changes get two adversarial passes. Never `--no-verify`.
- Full Sprk.Bff.Api.Tests runs time out under machine load (TestServer); prove failures with isolated class re-runs. Keep pwsh script message text ASCII (a "§" breaks the JSON parse in StandingGrantWriterInvariantScriptTests).
- App Insights: `spe-insights-dev-67e2xz` (rg `spe-infrastructure-westus2`). KQL: avoid `kind`, `first` and `last` as names.
- Ontology: do NOT merge their docs branch into R3. Re-run ListAgents after peer restarts.
