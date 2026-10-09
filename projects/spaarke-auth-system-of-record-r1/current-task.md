# Current Task State - spaarke-auth-system-of-record-r1

> **Last Updated**: 2026-10-09 (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Write the canonical auth architecture document (no POML; owner-directed) |
| **Step** | 1 of 4: not started — outline agreed with owner |
| **Status** | ready to start |
| **Next Action** | Draft `docs/architecture/SPAARKE-AUTH-ARCHITECTURE.md` (in this worktree) from the verified record + evidence files, using the outline under "Next Actions" step 1; then run one independent check that every statement traces to the record/evidence |

### Files Modified This Session
All committed and pushed on `work/spaarke-auth-system-of-record-r1` (HEAD `589b2f0ce` + this checkpoint commit). No uncommitted work.
- `auth-system-of-record.md` — the verified record (§13a = live batch A results)
- `live/live-test-plan.md` — 41 tests; run log at top
- `live/entra-readonly-checks.sh`, `live/batch-a-readonly-tests.sh` — read-only live scripts (default-deny masking)
- `working/a01…a09`, `x01…x07` — evidence (area traces, contradictions, docs verdicts, matrix/ADR, live read-outs)
- `notes/coordination/2026-10-09-to-provisioning-webhook-routes.md` — drafted note, NOT yet sent (owner relays)

### Critical Context
Owner wants a **canonical auth architecture** that becomes the source for ADRs and patterns. The record (`auth-system-of-record.md`) is an audit/verification record, not that document: it mixes provenance tags, ~30 checker markers, defects, dev live values. The architecture doc is derived from it, clean, in `docs/architecture/`, with the record as its verification companion.

---

## Active Task

**Canonical auth architecture doc** — `docs/architecture/SPAARKE-AUTH-ARCHITECTURE.md`. Not started.

## Next Actions

1. **Draft the architecture doc** (owner-agreed outline, 2026-10-09):
   1. System context — diagram (mermaid): clients, identity planes (workforce Spaarke tenant, CIAM `spaarkeextid`), BFF, Dataverse / SPE / Graph / AI services, provisioning control plane.
   2. Identity model — user types U1–U6 and service identities, per tenant and plane.
   3. Component catalogue — each auth component in code (`@spaarke/auth` strategies + config; external-spa standalone MSAL; Office `OfficeNaaStrategy`; BFF schemes/`AuthorizationModule`, `CallerPrincipalResolver`, `WorkforcePrincipalResolver`/member test, filters/policies; `OrderedCredentialClientProvider`, `GraphClientFactory`, Dataverse clients, impersonation; `AccessibleRecordSetService`, membership, grants, effective flags, SPE container membership; provisioning H3/H4b/H7/H7b/H11/H13): purpose, location, responsibilities, dependencies, configuration.
   4. Flows — sequence diagrams: sign-in per surface; inbound validation + plane selection; each outbound credential path (OBO, MI-FIC, app-only, impersonation).
   5. Authorization model — per-record checks, membership/accessible sets, grants, user classification.
   6. Configuration and environment model — what is set where per environment and per stamp (no dev-only values).
   7. Design rationale + governing ADRs (state where ADR-028 is stale; the record §10/§12b list the rules).
   8. Known gaps — link to the record §11, do not copy.
   - Sources: `auth-system-of-record.md` §2–§8, §12; `working/a01…a09` for line-level detail; `x01` truth overrides area files; `x04–x07` live facts (dev only — keep out of the canonical model except as "verified on dev").
   - Rule: describe current behaviour at `8a9ecaac1` (master moved since; check drift first — `git fetch` + diff the auth paths, refresh affected statements).
2. **Independent check** of the draft: every statement traces to the record/evidence or code (`path:line`); flag anything that doesn't.
3. Owner review of the doc.
4. Later, each with owner approval: supersede the old auth docs (x02: 6 keep / 162 update / 33 supersede / 9 delete-candidate); update `.claude/adr/ADR-028` (amendment from record §12b), `.claude/patterns/auth/*`, `.claude/constraints/auth.md`, and the CLAUDE.md §17 "Touch auth" pointer to the new doc.

## Open Owner Questions / Blockers

- **File defects as GitHub issues?** (CLAUDE.md §8.5: fix in scope or file + report.) Proposed via `/project-defer-issue-tracking`, routed per owner project (provisioning, UAC-r2, add-in, R3, BFF). Awaiting go-ahead — outward-facing.
- **Guest test account** (work account guest from another Entra tenant) — needed for the 8 P1 guest tests that decide Model 1 client sign-in.
- **Owner actions (live changes, owner's call):** rotate `Compose__Webhook__ClientState` (printed in clear by the first live script run; also a plain app setting) and the other three plain-setting secrets (`PowerBi__ClientSecret`, `Rag__ApiKey`, `Notifications__SignalR__ConnectionString`); decide on BFF identities holding System Administrator in dev, the prod BFF app (`92ecc702…`) as System Administrator in dev, and unconsumed/expired app secrets.
- **Send** the provisioning webhook note (drafted, committed).
- Owner is reviewing `auth-system-of-record.md`.

## Consumer on hold

`spaarke-SPA-external-access-platform-r3` (worktree `c:\code_files\spaarke-wt-SPA-external-access-platform-r3`): spec conversion HELD until the auth record/architecture exists; its Model 1 client decisions are marked PROVISIONAL in its `design.md` §4.6. Record §12a states what code + live config show for those decisions.
