# R7 Wave 12 — AI Platform MVP Completion Plan

> **Authored**: 2026-06-30
> **Status**: Plan approved by operator; Wave 12 audits dispatched
> **Source**: 2026-06-30 strategic discussion between operator + Claude (originating from R7 W11 T118 work and the architectural comparison doc)
> **Architectural foundation**: [`./spikes/poc-vs-playbook-engine-architecture.md`](./spikes/poc-vs-playbook-engine-architecture.md)
> **Why this is Wave 12 of R7**: scope extends R7's UAT-drive work (W11) into a coordinated push to bring three AI feature groups to functioning-in-production state. Operator-stated requirement: "we have to focus on 1 and 2 today and after those are working then we can focus on the rest."

---

## 1. Goal

Bring three groups of AI features to functioning-in-production state on a 3-6 week horizon. Use the **code-defined narrator pattern** validated by R7's W11 POC as default for narrative consumers. Use **audit-then-disposition** (restoration vs. remediation) for wizard + assistant<>workspace pieces currently broken but previously functional.

NOT a from-scratch rebuild. Targeted completion of AI feature surface worked on for ~6 months that is currently not functional per operator UAT.

---

## 2. Scope — three MVP deliverables

### 2.1 Daily Briefing — full 6-entity-type coverage with UI polish

Current POC live-render path renders 26 real notifications from a single entity type (`sprk_event`, 4 channels). MVP extends to 6 entity types with operator-specified filters.

**Records to collect**:

| # | Source | Filter | Ownership filter |
|---|---|---|---|
| 1 | Upcoming Tasks | `sprk_event`, type=Task, dueDate OR finalDueDate within next 5 days, status=Open | matter/project member OR event owner/assignee |
| 2 | Overdue Tasks | `sprk_event`, type=Task, dueDate OR finalDueDate > 5 days past, status=Open | same as #1 |
| 3 | Documents | `sprk_document`, new or modified | matter/project member |
| 4 | Matter | `sprk_matter`, modifiedon last 5 days, status=Active | member |
| 5 | Project | `sprk_project`, modifiedon last 5 days, status=Active | member |
| 6 | To Do | `sprk_todo`, dueDate=today/tomorrow | owner/assignee |

**Membership filter implementation**:
- **Preferred**: `IMembershipResolverService`
- **Fallback (pragmatic, operator pre-approved)**: inline FetchXml using these fields:
  - General/Tasks: `owner`, `sprk_assignedto`, `sprk_assignedattorney`, `sprk_assignedparalegal`
  - Matters and Projects: `sprk_assignedattorney1`, `sprk_assignedattorney2`, `sprk_assignedparalegal1`, `sprk_assignedparalegal2`
- Decision made during Wave 12 audit + implementation

**Timezone**: user timezone for date filters; no time-of-day precision required.

**UI / response shape requirements**:
- **TL;DR ↔ Activity Notes consistency**: items mentioned in TL;DR MUST have corresponding details in Activity Notes. Implementation: chain TLDR output as input to channel narrative generation.
- **Activity Notes ↔ matter links**: each bullet has clickable link to related matter. Already in `EnrichBulletWithEntityRefs`; verify across all 6 entity types.
- **Each sub-activity as its own line item**: response shape — bullets[] each renders as a distinct item.
- **Tools**: 'Add To Do' (checkmark icon) ONLY for MVP. Preserve three-dot tool menu for future additions.

### 2.2 Wizards — restoration OR remediation per audit

Five wizard features currently broken but previously functional:
1. Wizard file summary
2. Document create profile
3. **Create Matter** wizard (has Prefill from Action output schema)
4. **Create Project** wizard (has Prefill from Action output schema)
5. **Create Work Assignment** wizard (has Prefill from Action output schema)

For the three Prefill wizards: the Action output schema acts as a **contract with the wizard UI** — LLM's structured output binds to wizard form fields. Audit must determine if wizard UI is **dynamic-schema-driven** (renders fields generically from schema) or **hardcoded mapping** (UI maps known keys to known fields).

**Disposition per wizard (per audit)**:
- **R7 regression** (enum rename, DI change): restore — typically 1-2 hours per wizard
- **Inherent engine bug class** (matches /narrate failure modes): remediate to thin code-defined wrapper preserving Action output schema as contract (~50-100 LOC each)

### 2.3 Assistant ↔ Workspace ↔ Context

Operator-reported: "nothing fixed" in UAT. Assistant chat surface, LegalWorkspace dashboard, context plumbing — partially shipped but functionally incomplete.

**Scope unclear without audit** — Wave 12.1 Task 120 produces the gap list. Possible findings + scope implications:
- **Plumbing fixes** (context, session, claims): 1-2 weeks — IN MVP scope
- **Retrieval over SharePoint Embedded missing**: too big for MVP; MVP scopes down to "context-aware chat without document grep"
- **Tool-use missing** (Action Engine R1): too big for MVP; explicit defer

---

## 3. Acceptance Criteria (Wave 12)

### Daily Briefing
- [ ] AC1: Widget renders 6 channels matching operator-specified entity filters
- [ ] AC2: Each channel populated with operator's real Dataverse records (operator-verified spaarkedev1)
- [ ] AC3: Membership filter correctly limits records to user's matters/projects/assignments
- [ ] AC4: TL;DR mentions items that also appear in Activity Notes (no orphan references)
- [ ] AC5: Each Activity Notes bullet has working entity link
- [ ] AC6: 'Add To Do' tool checkmark present; three-dot menu preserved
- [ ] AC7: Timezone correctly applied to date-window filters

### Wizards
- [ ] AC8: Wizard file summary returns structured summary matching Action output schema
- [ ] AC9: Document create profile returns structured profile fields matching Action output schema
- [ ] AC10: Create Matter / Project / Work Assignment wizards prefill form fields from LLM output
- [ ] AC11: Action output schema editing in maker portal continues to affect wizard behavior (preserved tunable surface)
- [ ] AC12: All 5 wizards operator-verified end-to-end in spaarkedev1

### Assistant ↔ Workspace
- [ ] AC13: Assistant chat in workspace context knows current matter ID
- [ ] AC14: Assistant responses reference matter-specific data when present (not generic)
- [ ] AC15: Operator-verified end-to-end UAT (specifics TBD post-audit)

### System
- [ ] AC16: BFF publish size stays ≤60 MB compressed (ADR-029 / NFR-01)
- [ ] AC17: 0 new HIGH-severity CVEs (NFR-02)

---

## 4. Out of Scope (explicit deferrals)

| Item | Reason | Disposition |
|---|---|---|
| Tunable config tables (`sprk_briefingdatasource` Tier B) | Operator agreed: config-table-with-rules IS interpreter; same bug surface | Future project IF justified |
| `IConsumerDescriptor` + maker portal "playbook view" | Operator visibility/UX layer; not blocking MVP | Future visibility/UX project |
| `IMembershipResolverService` root-cause fix | If FetchXml fallback works for MVP, resolver becomes own project | Follow-on |
| Retrieval over SharePoint Embedded | R5 deferred; if Assistant↔Workspace audit shows it's blocker, separate project | `spaarke-ai-retrieval-r1` (proposed) |
| Tool-use / Action Engine R1 | Independent project on hold | Continues on hold |
| Code-compiler tool (visual designer → emits narrator) | Only justified at N≥5 narrative consumers | Far-future IF warranted |

---

## 5. Wave 12 sub-wave structure

| Sub-wave | Goal | Estimated effort | Tasks |
|---|---|---|---|
| W12.1 | Audits (4 read-only investigations) | 3 working days parallel, 5 sequential | 120, 121, 122, 123 |
| W12.2 | Daily Briefing — 6 entity types, membership filter, TLDR↔Notes chaining, UI fixes | 3-5 days | 130-136 (placeholders; POMLs post-audit) |
| W12.3 | Wizards — restoration OR remediation per audits | 3-10 days | 140-145 (placeholders; POMLs post-audit) |
| W12.4 | Assistant↔Workspace — fixes per audit | 1-4 weeks (audit-dependent) | 150-152 (placeholders; POMLs post-audit) |
| W12.5 | Wave 12 wrap-up + UAT close-out + lessons-learned | 1-2 days | 160-162 |

**Critical assumption**: Wave 12.1 Task 120 (Assistant↔Workspace audit) does NOT surface "retrieval over SPE doesn't exist" as the blocker. If it does, Wave 12.4 scopes to plumbing-only; retrieval becomes a separate project.

---

## 6. Sequencing (4-week happy-path)

| Week | Focus |
|---|---|
| **Week 1** | W12.1 audits (120-123 parallel). Daily Briefing collector extension begins as soon as audit 120 produces membership-filter recommendation. |
| **Week 2** | W12.2 Daily Briefing implementation + UI fixes. W12.3 wizard restoration/remediation begins per audits 121-123. W12.4 Assistant↔Workspace fixes begin per audit 120. |
| **Week 3** | Daily Briefing deploy + UAT. Wizard deploys + UAT. Assistant↔Workspace plumbing ongoing. R7's other open tasks (W5-T056, W6 docs, W7 sequential skill rewrites, W8-T087/T089/T089d, W11-T119, W10-T101) interleave as time permits. |
| **Week 4** | UAT iteration. Assistant↔Workspace UAT. W12.5 wrap-up. |
| **Weeks 5-6 buffer** | If Assistant↔Workspace audit surfaces larger work; if membership resolver requires real fix; if wizard remediation cost > restoration. |

---

## 7. Risk register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| W12.1 Task 120 audit reveals retrieval-over-SPE as Assistant↔Workspace blocker | Medium | High — pushes feature out of MVP | Scope reduces to plumbing-only; file retrieval as separate project; communicate scope change at end of W12.1 |
| Membership resolver root-cause is complex | Medium | Medium — pushes Daily Briefing to FetchXml fallback | Operator pre-approved fallback with specific field criteria |
| Wizard remediation cost > restoration (all 5) | Low-Medium | High — adds 3-4 weeks | If inherent bug class, drop to top-2 wizards; rest stay broken-but-documented |
| R7 wrap-up tasks interleave poorly | Low | Low | R7 tasks well-bounded; sequence opportunistically |
| Sub-Agent Write Boundary friction (W7 sequential) | Known | Low | Schedule operator session-time blocks |
| BFF publish size approaches 60 MB ceiling | Low | Medium | Per-task verification per ADR-029; current ~47 MB; significant headroom |
| Other-project deployment conflict with R7 deployed state | **Active concern** | High | See §9 below |

---

## 8. Architectural decisions captured (2026-06-30)

| Decision | Rationale |
|---|---|
| Wave 12 = MVP completion within R7 (not separate project) | Operator direction: "no keep this as r7". Wave 12 absorbs the coordinated MVP push. |
| Code-defined narrator pattern (W11 POC) is default for narrative consumers | W11 POC empirically validated; ~10× less runtime code; 0 vs ~6 bug classes |
| Tunable config tables (Tier B) DEFERRED | Operator: "config-table-with-rules IS an interpreter; same bug surface" |
| Wizards: audit-then-decide (restoration vs remediation) | Simple playbooks often work fine on engine; broken state likely R7 regression |
| Three Prefill wizards' Action output schema MUST be preserved as wizard-UI contract | Operator-flagged 2026-06-30 |
| Membership filter: prefer resolver; fall back to inline FetchXml | Operator pre-approved fallback fields |
| Chat-summarize stays on playbook engine | Per W11 architecture doc §5 — chat genuinely needs streaming, dynamic context, tool calls |
| Timezone: user TZ, no time-of-day precision | Operator-stated |
| Daily Briefing 6 entity types (vs current 4 channels of sprk_event) | Operator-provided record list |
| Assistant↔Workspace is MVP-critical (not mid-term) | Operator correction |
| No new shared abstractions unless 2+ demonstrated consumer need | Operator pushback against premature infrastructure |
| Action output schema preserved as operator-tunable surface for wizards (across restoration OR remediation) | Operator value preservation |

---

## 9. Active concern — deployment coordination with another project (operator-raised 2026-06-30)

Another project (TBD identified) needs to deploy to spaarkedev1 BFF + SpaarkeAi code page. Currently deployed state in spaarkedev1:

| Surface | Current deployed state | Source |
|---|---|---|
| BFF (`spaarke-bff-dev`) | R7 POC narrator + DailyBriefingCollector + `/api/ai/daily-briefing/render` endpoint + scheduler params + `Features__NarrateUseCodeBasedNarrator=true` | R7 W11 commits `3affa952f`, `85c762081` |
| SpaarkeAi widget (`sprk_spaarkeai`) | `USE_LIVE_RENDER=true` flag in deployed bundle | R7 W11 commit `85c762081` |

**Risk if another project deploys BFF or widget**: those R7 deployed-but-not-merged changes get reverted. Daily Briefing widget breaks in spaarkedev1.

**Recommendation depends on clarifying answers** (see "Open Questions for Operator" §10).

Practical mitigations regardless of merge sequence:
- Tag current deployed BFF + widget commit hashes (so we know "known good" state)
- Use App Service deployment slots if available — deploy to staging slot, smoke, swap only if green
- Smoke Daily Briefing widget BEFORE AND AFTER any deploy
- Have rollback plan

**Root-cause framing**: the reason this coordination question is hard is that R7 has been holding deployed code in a worktree without merging for weeks. R7 closing ASAP removes the worktree-divergence risk for ALL future projects. Wave 12 itself should NOT delay R7 close further — Waves 12.2+ work merges as it lands, in small commits.

---

## 10. Open Questions for Operator

1. **Membership filter approach** for Daily Briefing — fix `IMembershipResolverService` (durable) OR inline FetchXml (faster)? Audit 120 will produce recommendation; operator decision drives Wave 12.2.
2. **Wizard binding pattern** — are the three Prefill wizard UIs dynamic-schema-driven or hardcoded? Audit 123 will determine.
3. **Other-project deployment coordination** — which project, which branch, what scope, when does it need to ship? Without these answers, the recommendation in §9 is generic.
4. **Portfolio registration** — should Wave 12 / this MVP completion work get its own portfolio Issue, or stay folded into R7's existing Project Issue #501?

---

*End of Wave 12 plan v0.1. Operator can amend and re-approve as audits return findings.*
