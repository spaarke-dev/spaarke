# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-06-30 (T141 EXECUTING — Wave 12.3 Document Create Profile restoration)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | T141 — RESTORE document create profile (Dataverse data fix + Action JPS alignment + UI cosmetic) per audit 122 |
| **Task File** | `projects/spaarke-ai-platform-unification-r7/tasks/141-restore-document-create-profile.poml` |
| **Phase / Wave** | Wave 12.3 — Wizard restoration (parallel-safe sibling to T140/T142/T143) |
| **Sub-wave** | 12.3 |
| **Step** | In progress |
| **Rigor Level** | STANDARD |
| **Status** | in-progress |
| **Next Action** | Step 1 — Decide DELETE vs REPAIR via Dataverse inspection |

### Wave 12 dependency-driven execution plan (recap)

**Parallel Group A** (Dataverse + config + smoke; no BFF deploy needed):
- T130 IMembershipResolverService fix (gates T131, T135 in W12.2)
- T140 Wizard file summary config fix
- T141 Document Create Profile Dataverse fix
- T142 Project Wizard FK re-link
- T143 Matter Wizard smoke (gates T144)

**Parallel Group B** (BFF code; gates ONE BFF deploy event):
- T131 Daily Briefing collector 6-entity extension (after T130)
- T132 TLDR↔Notes chaining
- T133 Channel registry expansion
- T134 Widget tools update
- T135 Entity-link verification (after T131)
- T150 Gap A naming normalization
- T151 Gap B EntityName lazy-fetch
- T152 Gap C default PageType
- T153 Gaps D-H fixes

**Sequential Gate** (single combined deploy):
- T136 + T154 combined deploy + Daily Briefing UAT + Assistant↔Workspace UAT

**Sequential Wrap-up** (T145 wizard UAT + W12.5 close-out + R7 wrap-up).

### Recently completed (this session, 2026-06-30)

| Step | Outcome |
|---|---|
| Wave 12.1 audits (120/121/122/123) | ✅ all 4 returned; aggregate disposition: ALL RESTORATION; NO new abstractions needed; assistant↔workspace PLUMBING-ONLY (in MVP scope, no retrieval blocker) |
| R7 → master merge prep (PR #520) | ✅ created; CONFLICT in .claude/CHANGELOG.md resolved; 24 master commits integrated via merge commit; pushed |
| CI failure investigation | ✅ root cause: APPLICATIONINSIGHTS_CONNECTION_STRING required by spaarke-redis-cache-remediation-r2 FR-06 validation; NOT R7 regression |
| CI fix applied | ✅ commit `fd657e0b2` — fake instrumentation key added to ci-tier1-blocking.yml global env |
| 18 Wave 12 POMLs generated | ✅ commit `0ad2a5cb5` — pushed |

### Open items

| Item | Status |
|---|---|
| PR #520 CI re-run | 🟡 waiting (post CI-fix push) |
| PR #520 merge to master | 🟡 awaiting CI green |
| Wave 12 execution (T130 + parallel siblings) | 🔲 ready; awaits operator go-ahead |
| ISS-NNN to file (redis-cache-r2 validation in Testing env) | 🔲 to file after PR #520 merges (so the ISS reference makes sense) |
| spaarkeai-compose-r1 rebase coordination | 🔲 after PR #520 merges, notify their owner to rebase + deploy |

---

## Skills Loaded This Session

- TodoWrite (Wave 12 scaffolding + agent dispatch + POML generation)
- Bash (git ops, build verify, push, CI checks, merge conflict resolution)
- Read / Grep / Glob (audits, file inspection)
- Edit / Write (R7 file updates, 18 POML creation, CI workflow fix)
- Agent (4 parallel general-purpose agents for Wave 12.1 audits — all returned successfully)

## Knowledge Files Loaded

- projects/spaarke-ai-platform-unification-r7/notes/spikes/poc-vs-playbook-engine-architecture.md
- projects/spaarke-ai-platform-unification-r7/notes/wave12-mvp-completion-plan.md (NEW this session)
- projects/spaarke-ai-platform-unification-r7/notes/audits/wave12-{120,121,122,123}-*.md (4 NEW audit findings docs)
- projects/spaarke-ai-platform-unification-r7/CLAUDE.md (updated decisions table + hot-path)
- Root CLAUDE.md (§10 BFF Hygiene, §11 Component Justification, §3 Sub-Agent Write Boundary)

## Constraints / Patterns Applied

- CLAUDE.md §11 (Component Justification) — operator strict; T150 has explicit `<justification>` block (only POML adding new surface, a single normalization helper)
- CLAUDE.md §10 (BFF Hygiene) — every BFF-touching POML in Wave 12 has publish-size + constraints checklist requirements
- Sub-Agent Write Boundary (§3) — Wave 12 does not plan `.claude/` writes
- ADR-013 (BFF AI), ADR-029 (Publish Hygiene), ADR-038 (Testing Strategy) — applied per POML

## Quality Gates

- N/A for Wave 12.1 audits (STANDARD rigor; read-only)
- N/A for Wave 12.3 wizard restoration (mostly Dataverse data fixes)
- Wave 12.2/12.4 implementation POMLs are FULL rigor — code-review + adr-check at Step 9.5 will run for each
- T136 + T154 deploys require: publish-size ≤60 MB (NFR-01) + 0 new HIGH CVE (NFR-02)

---

*All Wave 12 planning artifacts in place. Ready to execute on operator go-ahead.*
