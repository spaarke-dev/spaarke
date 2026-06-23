# Current Task State

> **Last Updated**: 2026-06-22 (Phase A Channels 1–5 patched LIVE; Ch.6 + Ch.7 deferred; moving to Phase B)
> **Recovery**: Read "Quick Recovery" section first
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | R2.2 SHIPPED + DEPLOYED. Producer-fix Phase A: Channels 1–5 fetchXml patches LIVE on spaarkedev1. Channels 6+7 deferred (design captured for Ch.7). Moving to Phase B (isread/toasttype + TTL). |
| **Step** | Phase A complete; Phase B not yet started |
| **Status** | Phase A done; ready for commit + Phase B kickoff |
| **Next Action** | (1) Commit new Patch-PlaybookQueryFetchXml.ps1 script + Ch.7 design notes + this checkpoint. (2) Admin-merge PR #413 (known StorageRetryPolicyTests flake). (3) Design Phase B fix for isread/toasttype mismatch + TTL increase. |
| **Branch** | `work/spaarke-daily-update-service-r2.2-patch-fix` (head: `1282c1335`, +1 uncommitted: new patch script + Ch.7 notes + this update) |
| **Open PRs** | **PR #413**: scripts/Patch-NotificationPlaybookDueDate.ps1 field-name fix — awaiting admin-merge (Release CI flake on StorageRetryPolicyTests) |

### Files Modified This Session (after context-handoff resume)

- `scripts/Patch-PlaybookQueryFetchXml.ps1` (NEW) — reusable producer-fix script: patches sprk_configjson.fetchXml + description + optional entityLogicalName + optional ClearParameters. Used to apply Ch.1–5 patches.
- `projects/spaarke-daily-update-service-r2/notes/channel-7-work-assignments-design.md` (NEW) — captured design for deferred Channel 7 (sprk_workassignment + Option C + createdon OR sprk_responseduedate semantics)
- `projects/spaarke-daily-update-service-r2/current-task.md` (THIS FILE) — Phase A wrap-up + Phase B kickoff

### Live Dataverse mutations (after resume) — all on spaarkedev1

| Node | Channel | What changed |
|---|---|---|
| `1cbd78b2-5f2d-…` Query Overdue Tasks | 1 | fetchXml: sprk_todoflag→sprk_eventtype_ref=Task + statuscode=Open + (sprk_duedate OR sprk_finalduedate < today); sort: finalduedate then duedate |
| `79f77aa5-5f2d-…` Query Tasks Due Soon | 2 | fetchXml: same shape as Ch.1 but BETWEEN today AND {{dueSoonWindowUtc}} (default 7d) |
| `2b50e587-5f2d-…` Query New Documents | 3 | fetchXml: removed sprk_matterteammember; replaced with outer join on sprk_matter + OR (m.ownerid=me, doc.ownerid=me) |
| `2d2fdd9e-5f2d-…` Query New Events | 4 | fetchXml: appointment→sprk_event with type IN {Action,Deadline,Meeting,Milestone,Reminder} + sprk_RegardingMatter outer join + Option C OR; entityLogicalName: appointment→sprk_event; cleared stale parameters |
| `d9468c8e-5f2d-…` Query New Emails | 5 | fetchXml: email→sprk_communication with type=Email (100000000) + sprk_RegardingMatter outer join + Option C OR; entityLogicalName: email→sprk_communication; cleared stale parameters |

### Critical Context (must read before continuing)

**1. Phase A done. Producer is now functional.** All 5 active Daily Briefing channels query the right entities with the right filters. Channels 6 + 7 explicitly deferred — Ch.7 design captured for future implementation.

**2. Architectural patterns established (apply to any future producer fixes):**
   - **"My" filter (interim)**: `ownerid eq-userid` (BFF rewrites at runtime per `QueryDataverseNodeExecutor.cs:187-195`). Phase C will design richer "My" resolver (team membership, regarding, etc.).
   - **Option C (matter-owner OR record-owner)**: when source entity has `sprk_RegardingMatter` lookup (sprk_event, sprk_communication) OR direct matter lookup (sprk_document), use outer-link + `entityname="m"` cross-entity OR pattern.
   - **Date-range OR**: when a record has multiple date fields (sprk_duedate, sprk_finalduedate, sprk_responseduedate), OR the range conditions together inside the AND filter.
   - **Two-level sort**: primary by stricter/canonical date, tiebreaker by secondary date.
   - **entityLogicalName must match `<entity name=>`**: BFF builds entitySet URL from `sprk_configjson.entityLogicalName`. Patching fetchXml without entityLogicalName fix = URL/entity mismatch error.
   - **Stale parameters block** (`{{timeWindow}}`, `templateParameters.dueWithinDays`, etc.) — BFF only supports `parameters.dueSoonDays` and `parameters.timeWindowHours`. Remove anything else.

**3. Test data verification (still pending on user):**
   - Set sprk_event records' owner to Ralph Schroeder for Ch.1/2/4 testing.
   - For Ch.5 — emails owned by service principal won't surface unless owner is fixed OR we add `sprk_to` UPN match in Phase C.
   - For Ch.3 — Ralph already owns most matters + docs, so existing data works.
   - Verification: wait for next playbook tick (≤1h) OR invoke manually; check appnotification records by category.

**4. Architectural bugs still pending (Phase B):**
   - **isread / toasttype mismatch** in `notificationService.ts:120` — reads `toasttype === 200000000` (which schema says is "Timed", not "Dismissed"). Line 299 writes `webApi.updateRecord('appnotification', id, {isread: true})` — `isread` may not even exist on appnotification schema. Need to verify field names and align read/write paths.
   - **TTL = 3 days** (`CreateNotificationNodeExecutor.cs:489`, `ttlinseconds = 259200`) may be too short — accidental TTL purge on 2026-06-22 wiped 36 records the user expected. Proposed: 7 or 14 days.

**5. Phase C (out of scope here, tracked):**
   - R3 (`spaarke-platform-foundations-r3`) provides scheduler robustness + run-now admin endpoints
   - "My" resolver design (richer than ownerid)
   - Sync mechanism between repo JSON and deployed playbook configs (repo drift caused the original misdiagnosis)

### Recovery steps for next session

1. Read this entire Quick Recovery section
2. Check branch: `git branch --show-current` should show `work/spaarke-daily-update-service-r2.2-patch-fix`
3. Verify PR #413: `gh pr view 413 --json state,mergedAt` (may have been admin-merged)
4. Spot-check live state of Ch.1–5 nodes: `mcp__dataverse__read_query SELECT sprk_playbooknodeid, sprk_name, sprk_configjson FROM sprk_playbooknode WHERE sprk_playbooknodeid IN (...)`
5. Next concrete action: Phase B design (see notes/phase-b-design.md if created)

---

## Full State (Detailed)

### R2.2 final state (unchanged from prior checkpoint)

| Artifact | State |
|---|---|
| PR #405 (R2.2 main) | ✅ MERGED (`ec05765ed`) |
| PR #410 (R2.2 hotfix) | ✅ MERGED (`e584d296c`) |
| PR #413 (patch script fix) | 🟡 OPEN — pending admin-merge |
| BFF deploy | ✅ LIVE (46.07 MB, /healthz green) |
| `sprk_dailyupdate` code page | ✅ LIVE |
| `sprk_spaarkeai` code page | ✅ LIVE |

### Phase A live channel state (post-resume)

All 5 channel nodes patched LIVE. Next playbook tick exercises them.

### Decisions Made (this session, after resume)

- **2026-06-22**: Channel-by-channel patches via reusable `Patch-PlaybookQueryFetchXml.ps1` script (extension of the field-fix-only `Patch-NotificationPlaybookDueDate.ps1`). Operates one node at a time so each channel is reviewed + applied independently.
- **2026-06-22**: For each channel, "My" filter stays as `ownerid eq-userid` (interim) — Phase C will design richer resolver. User confirmed.
- **2026-06-22**: For sprk_event and sprk_communication, use per-type regarding lookup (`sprk_RegardingMatter`) with outer-link + cross-entity OR pattern (Option C). User clarified that Spaarke has per-entity-type regarding lookups + a unified resolver, NOT a polymorphic regardingobjectid.
- **2026-06-22**: For sprk_document (Ch.3) — direct sprk_matter lookup is the model (no per-type regarding fields). Option C still applies via outer link + OR.
- **2026-06-22**: Channel 4 fetchXml type filter set to {Action, Deadline, Meeting, Milestone, Reminder} — user opted for broader event coverage than just Meeting.
- **2026-06-22**: Channel 5 ownership filter known to miss inbound emails owned by service principal. Documented as interim limitation; Phase C resolves via `sprk_to` UPN match or post-receive ownership-assignment flow.
- **2026-06-22**: Channels 6+7 deferred. Channel 7 design captured in `notes/channel-7-work-assignments-design.md` for future implementation (requires creating a new sprk_playbooknode record, not just patching).

### MCP visibility limitation (unchanged)

MCP service principal sees only 4 of (presumably 36+) appnotification records. Don't trust MCP appnotification counts as definitive — verify with user-side queries.

### Repo paths

- This file: `projects/spaarke-daily-update-service-r2/current-task.md`
- Ch.7 design notes: `projects/spaarke-daily-update-service-r2/notes/channel-7-work-assignments-design.md`
- Producer patch script (NEW): `scripts/Patch-PlaybookQueryFetchXml.ps1`
- Original due-date patch script: `scripts/Patch-NotificationPlaybookDueDate.ps1` (PR #413)
- BFF query executor: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/QueryDataverseNodeExecutor.cs`
- BFF notification creator: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs`
- Client notification service: `src/client/shared/Spaarke.DailyBriefing.Components/src/services/notificationService.ts` (Phase B target)

---

*Updated 2026-06-22 — Phase A complete; ready for commit + Phase B.*
