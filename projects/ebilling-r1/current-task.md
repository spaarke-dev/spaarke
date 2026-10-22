# Current Task State

> **Last Updated**: 2026-10-21 (checkpoint after 080/099 done, 091 dev applied)
> **Format**: CURRENT state only. Rewrite each checkpoint; target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Wave W3-6 wrapping up (091 live check left), D-176 follow-ups (119 waits on ontology 157). Model FINAL (EB-D-148); 108 POMLs; draft PR #1911. |
| **Status** | 080 ✅, 099 ✅, 115 + feeKind follow-up merged. 091 merged and applied on dev; only uac-r2 condition (c) is open (tag-write proof as the identity). |
| **Next Action** | (1) When the owner runs (or authorises) the tag-write proof, send its output to uac-r2 and close 091 (POML status, TASK-INDEX). (2) On ontology's 157 F3 fix range: scoped re-check, PASS. Then ontology merges PR #2231, which unblocks 119 (launch via task-execute, opus/high). (3) Next waves: W3-7 088 (dev schema run, owner OK given), W3-8 081, 090. |

### Lanes
| Task | State | Waiting on |
|---|---|---|
| 091 storage | Merged (d084f10a93). Dev: account `sprkspaarkedevebillsa` (rg-spaarke-dev), custom role + 3 container-scope grants to mi-bff-api-dev, scanning NOT enabled on dev (owner). Test-EbillingFileStoreRole.ps1 exit 0. Hand-off #2232 / DEF-034 filed and sent to customer-provisioning. | **uac-r2 (c):** `scripts/ebilling/test-ebilling-file-store-tag-write.sh` run AS mi-bff-api-dev (App Service SSH console, `az webapp ssh -g rg-spaarke-dev -n spaarke-bff-dev`), once per container; expect 201/403/403/200/202. Step 3 = 201 → STOP, tell uac-r2. 090 downloads stay off until it passes. |
| 119 /review Duplicate withhold | POML updated: mirrors 157 (no exemption, agreed), calls `ShownAsync`, notes (9)-(13). | ontology 157 F3 (nextLink truncation) fix → our re-check → PR #2231 merged. |
| O-14 prototype | First cut in spaarke-prototype/projects/ebilling.r1 (localhost:5177). P-1/P-2 answered from the model; P-3 owner: not in R1 (applied). | 084/089 to correct its assumed DTOs (src/procedures/types.ts). |

### Done this session (details in git log)
- 080: dev run exit 0 (created 64, changed 20); the idempotence run wrote nothing; ontology and uac-r2 verified on dev independently. `@odata.type` fix 11ead6c938. Record: notes/080-dev-run-2026-10-21.md.
- 099: merged 4a1497e252 (uac-r2 ACCEPT 54524dead8). Pre-existing #2218 / DEF-033 (BFF won't start with AI gates off) filed.
- 115 follow-up: D-39 key = (RuleType, FeeKind, exact fee-code set for `other`), merged f4874f4f8a, ontology PASS.
- Combined tip tests: unit filtered 6213 pass, ArchTests 1319 pass.

### Owner decisions 2026-10-21 (do not ask again)
- 080 re-run yes; AddedFee key by fee kind (and exact code set for `other`); 091 option A; O-14 prototype started.
- Dev file store: new dedicated account; apply after the what-if; scanning "not enabled" on dev.
- P-3: a child can't re-enable a rule its parent excluded (not in R1).
- Matter spend/budget stay visible to flagged-external users (no field security).
- Earlier: dev schema runs of 080/115/088 after ontology PASS + notices; no Key Vault changes; 096 one release after word-add-in-r1's; 095 from-email shapes frozen.

### Agreed with peers
- field-mapping 018 moves only Matter and Project quick-create; the Office Invoice leg is left for our 096 to remove. 017 is blocked on #1911.
- R3: OK'd the dev BFF deploy of master a7b956fb55. Dev BFF lacks e0df773395 (015), so the sprk_invoicestatus delete stays blocked until #1911 lands and a dev deploy carries it.
- 082 must use `BillingRuleDefinition.PrecedenceKey` for record-level precedence (in its POML).
- uac-r2 191: scorecard, Insights and Workspace AI keep FinanceAuthorizationFilter without step 0 (NonFinanceFilterUsers list).

### Standing rules (unchanged)
- Worktrees: agents work in their own worktree; remove only with Remove-WorktreeSafe.ps1; never bare git stash; never --no-verify.
- Run the broad filter after multi-range picks.
- Deploys: Import-SolutionScoped.ps1 only; dev BFF and SPA only via SPA-r3.
- Live writes and role changes need the owner's OK; schema runs need notices to ontology-r1 and uac-r2 before and after.
- Gates: no line upload route before #2047; 100 and 105 not to dev or master before the Core User check on dev; no dev invoice seeding until REAL-2026-123456.01 is answered.
