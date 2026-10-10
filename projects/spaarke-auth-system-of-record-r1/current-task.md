# Current Task State - spaarke-auth-system-of-record-r1

> **Last Updated**: 2026-10-10 (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Next phase: **remediation** of the issues no other project owns (owner 2026-10-10: "the work needs to get done … whatever is most efficient") |
| **Step** | Not started. The architecture doc is done and reviewed (owner: "from an Auth perspective all is correct") |
| **Status** | queued behind the R3 `/project-pipeline` run (R3 worktree) |
| **Next Action** | Write a short remediation `spec.md` in this project covering the unowned issues below, then `/project-pipeline` here; start with the broken buttons (#1556–#1558) |

### State of the work (all committed and pushed on `work/spaarke-auth-system-of-record-r1`)
- `docs/architecture/SPAARKE-AUTH-ARCHITECTURE.md` — the canonical architecture. Trace-checked (x08); owner-reviewed 2026-10-10 with no holes found.
- `auth-system-of-record.md` — the verified record.
- `working/x09a`–`x09d` — owner Q&A research (mail + webhooks, Model 1 client config, membership + guests, broken buttons + triage).
- `notes/defer-issues.md` — 20 ISS + 2 DEF with GitHub links (#1556–#1571, #1573–#1574, #1576–#1579; #1572 is a closed duplicate). All are on the Spaarke Core board with Type set.
- `notes/coordination/2026-10-09-to-provisioning-webhook-routes.md` — delivered to provisioning, which acted on it: H14b/H14c removed (#1560 fixed on their branch, PR #1535).

## Remediation scope (no other owner)

| Issue | What |
|---|---|
| #1556 | Email "Archive Email" posts to a removed route |
| #1557 | Matter form OnLoad: insight relative URL + no bearer; KPI/rollup packaged copies with no bearer + `/api/api` (see also #1489) |
| #1558 | Registration Approve/Reject runs the legacy packaged script (packaging fix) |
| #1559 | Remove orphan web resources |
| #1573 | SpaarkeMaster env-var defaults are dev values; template lacks extra audiences |
| #1574 | BFF pipeline hardening (agent filter, Admin role, token logger, CORS echo, RAG tenant) |
| #1577 | Outbound credential hygiene |
| #1578 | Dev identity posture (owner decisions; dev only; not critical path) |
| #1579 | Hygiene umbrella |

## Owned elsewhere (track, don't do)

- **Provisioning:** #1560 (fixed), #1562 (per-customer shared mailbox approved), #1565, #1570.
- **Email-comms:** #1561.
- **UAC-r2:** #1564, #1567, #1576.
- **Add-in:** #1571 and #1464 (guest mail).
- **R3:** #1563, #1566, #1568.

## Later (owner approval each)

Step 4 of the original plan:
- supersede the old auth docs (x02);
- amend ADR-028 — record §12b, **plus the R3 decisions**: A2 single-tenant client, A3 modules by user type, the A1 guest-scope clarification, `user_impersonation` everywhere, self-registration;
- update `.claude/patterns/auth/*`, `.claude/constraints/auth.md` and the CLAUDE.md §17 pointer.

Also update the architecture doc §5.3/§5.8/§8.2 once R3 lands its decisions in code.

## Open Owner Items

- Live security actions (not critical path): rotate the four plain-setting secrets; the dev BFF app's secret; Azure CLI pre-authorization; the production BFF app as System Administrator in dev (#1578).
