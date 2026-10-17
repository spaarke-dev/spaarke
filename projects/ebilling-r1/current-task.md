# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-16
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Parallel mode. Done (40/79): 001-005, 010-014, 016-025, 027-029, 040-048, 054, 078, 106, 107, 108, 109. WIP: 026 (fix-round-1 running, agent a89b3b0fa808c1b23), 070 (waits for the master merge + ontology paired change). ADR-010 ceiling 165; route ledger 131 |
| **Step** | 026 fix-round-1 running. It merges three reviews: ours, ontology-r1 K1-K5 and uac-r2 (no fix-now; condition 4 accepted as met, with the deviation carried in #2047). Items: refusal kinds (MultipleRoots → CoreRecordAmbiguous); sprk_firmreason field security per D-95; line core = derived core guards; deploy-order step (SpaarkeMaster import before the BFF) in 053 + DEF-021; RemoveLink doc; notes corrections; order-sensitive lockstep; project CLAUDE.md line "no line upload/storage route before #2047" |
| **Status** | in-progress (parallel mode) |
| **Next Action** | When 026 reports: cherry-pick (trailer rewrite, background, 600 s), build + Signals/Finance filter + ArchTests. Send the range to ontology-r1 (all) and uac-r2 (only the CoreAncestorResolver commits; check existing callers are unchanged). After both OK, mark 026 ✅. Then merge origin/master (>= e702c3401; conflicts expected in ADR010_DITests, the route ledger (master 129 vs ours 131), SignalReevaluationJobTests), then 070 fix-round-2 = ontology paired change (OntologyAdminAuthoringService:1226 passes RuleDomain.Ontology + RuleTypeWrongDomain → "ruleType"; OntologyDomain + OntologyAdminRulesService :48/:142 use RuleDomains; dry-run 400 test), send to ontology-r1, then 070 ✅. Then 015 (owner OK, destructive) |

### Files Modified This Session
- All committed (last: 794dd173c D-95a).

### Critical Context
- Decisions: D-87…D-91 (earlier); D-92 schema on ontology policy tables approved as built; D-93 code-set matching tuning (task 054, done); D-94 sprk_billingevent as an intermediate (uac-r2); D-95 Ontology Service AppendTo on billing event + invoice, firm reason under field security; D-95a ontology-r1 applied the grants live and owns Roles/Spaarke Ontology Service.xml + its guard test (026 must not touch them).
- Standing: no line (sprk_billingevent) upload/storage route before uac-r2 #2047.
- 053 deploy: 047 seeding order (Ensure-ActionRow → Deploy-ActionMirrors → BFF deploy + Seed-PlaybookConsumers -DiffOnly), 054 rerun Deploy-ActionMirrors -Filter invoice-codeset-match + tuning seed; SpaarkeMaster import before the BFF (026).
- 029: Activate/Deactivate hide is source-only until a release import; pac in pwsh: prepend %LOCALAPPDATA%/Microsoft/PowerAppsCLI to PATH.
- Owner action pending: add the 7 new tables to the Matter Management and Spaarke Administration apps in the app designer (save + publish).
- Open: 015 owner OK; optional owner question on 106 rate history (whole section vs firm-wide cards when the core read is refused); DEF-015 Procedure Library; ISS-009; #2010, #2011, #2045 (DEF-021); write-path registry row for the tracking number not yet in DATAVERSE-WRITE-PATH-ARCHITECTURE.md.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 026 (agent fix-round-1); 070 queued behind the master merge |

## Next Action

See Quick Recovery.
