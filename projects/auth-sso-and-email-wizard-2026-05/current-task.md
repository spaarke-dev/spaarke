# Current Task State — Auth SSO Propagation + Email Wizard

> **Last Updated**: 2026-05-13 (handoff before compaction)
> **Recovery**: Read **`CONTEXT.md`** in this folder for full context.

---

## Quick Recovery

| Field | Value |
|-------|-------|
| **Active Work** | Propagate `@spaarke/auth` tenant-authority fix to remaining ~28 consumers + verify email wizard |
| **Status** | Library fix in master. 3 of ~30 consumers rebuilt. User testing pending. |
| **Blocker** | User reports popups on every tab open — Daily Briefing + Workspace rebuilt, but other components loaded on Corporate Counsel app may still bundle old library |
| **Master HEAD** | `658e5944` |

---

## Done This Session (2026-05-12)

- [x] Phase A semantic search server-side filtering + threshold + dedup count (cbb6a64a)
- [x] Empty-query threshold skip + Linux deploy fallback (fca3a6cd)
- [x] Phase B foundation: shared components in @spaarke/ui-components (2a1a34c1)
- [x] Email wizard wired into PCF v1.1.40 (9f977809)
- [x] 3 critical bugs: tenant authority + BFF attachment + correct playbook (9e480d75)
- [x] Daily Briefing rebuilt + deployed dev1 + demo (658e5944)
- [x] Hardened Deploy-BffApi.ps1 with SHA-256 hash verify + auto-recover

## Awaiting User Verification (Before More Work)

1. **Auth popup gone?** Open Corporate Counsel app after clearing localStorage/sessionStorage/cookies + browser close. Expected: no popup.
2. **Email wizard works end-to-end?** Multi-doc select → Step 1 → Step 2 combined summary → Step 3 compose → Send. Expected: success.

## Pending (Do After Verification)

- [ ] Batch-rebuild remaining ~28 `@spaarke/auth` consumers (full list in CONTEXT.md)
- [ ] Demo BFF deploy of email attachment fix (deferred yesterday)
- [ ] Phase C: Wire Email button into DocumentRelationshipViewer
- [ ] Update `docs/architecture/sdap-auth-patterns.md` with binding requirements + bundling reality

## Key Files

See **`CONTEXT.md`** in this folder for the full file map, binding requirements, deploy commands, and recovery instructions.
