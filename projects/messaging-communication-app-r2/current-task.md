# Current State — messaging-communication-app-r2 (Communication Workspace)

> **Last Updated**: 2026-07-19 (context-handoff before /compact)
> **Recovery**: Read "Quick Recovery" first. Project is **CODE-COMPLETE + BFF DEPLOYED TO DEV**. Owner is now hand-creating the Dataverse schema + placing PCFs.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | ✅ **Code-complete** (all 20 work tasks + 090 wrap) · **BFF deployed to `spaarke-bff-dev` + verified live** (2026-07-19). Now in **owner manual-deploy** of Dataverse schema + PCFs. |
| **Branch** | `work/messaging-communication-app-r2` @ `0eb17bd4f` — pushed, **synced to latest master (0 behind)**, **NOT merged to master** (owner's pending decision). |
| **BFF deploy** | ✅ Live on `spaarke-bff-dev` — hash-verified, `/healthz` 200; new endpoints `by-regarding` / `query` / `participant=` return **401** (route live, behind auth). They return **DATA only after the Dataverse schema is applied**. |
| **Next Action** | **Owner is hand-creating the schema + placing PCFs** (details below + README "Owner deploy gates"). When done: (1) live-verify the reads; (2) offer the **page + widget client deploys** (not yet packaged); (3) offer **merge-to-master**. |
| **Tests / gates** | 8654 pass / 0 fail; publish ~46.24 MB (<60); 0 new CVE; 0 ADR violations. |

### ⏳ What the owner is doing right now (manual, in the maker portal)

1. **Schema — modify `sprk_communicationthread`**: add 11 typed `sprk_Regarding{...}` lookups (mirror `RegardingFieldMap.All`; `account`→`sprk_RegardingAccount`, `contact`→`sprk_RegardingPerson`) + `sprk_RegardingRecordType_Ref` (Lookup→`sprk_recordtype_ref`) + `sprk_NameIsAutoDerived` (Yes/No, default Yes) + `sprk_IsDefaultThread` (Yes/No, default No). **Keep the existing Text `sprk_regardingrecordtype` — do NOT retype.** Spec: `notes/002-thread-regarding-schema.md`.
2. **Schema — create `sprk_communicationparticipant`** (Org-owned): `sprk_Communication` (Lookup→sprk_communication, Required, Cascade), `sprk_SystemUser` (Lookup→systemuser), `sprk_Contact` (Lookup→contact), `sprk_Role` (Choice: From/To/Cc/Bcc = **100000000/1/2/3 — exact**), `sprk_AddressText` (Text/Email), `sprk_IsResolved` (Yes/No). Grant BFF app-user Create/Read/Append. Spec: `notes/003-communicationparticipant-schema.md`.
3. **PCF ZIP to upload**: `src/client/pcf/CommunicationTimelineRegarding/Solution/bin/CommunicationTimelineRegardingSolution_v1.0.0.zip`.
4. **PCF placement — `CommunicationTimelineRegarding`** on 11 forms, bound to each entity's primary-name field (matter→`sprk_mattername`, project→`sprk_projectname`, event→`sprk_eventname`, account→`name`, contact→`fullname`, rest→`sprk_name`); input props copied from R1's `CommunicationTimeline` placement (`apiBaseUrl`/`tenantId`/`clientAppId`/`bffAppId`). Spec: `notes/022-pcf-form-placement.md`.
5. **PCF placement — `RegardingResolver`** (already in env, no ZIP) on the `sprk_communicationthread` form: `entity=sprk_communicationthread`, `regardingRecordType`→`sprk_regardingrecordtype_ref` (bound), `regardingRecordNameField`→`sprk_regardingrecordname`, `regardingTargets`=the 11-list. Spec: `notes/071-regarding-resolver-thread-placement.md`.

### 🚨 The one gotcha to watch
`sprk_Role` choice **values** must be exactly **100000000 (From) / 100000001 (To) / 100000002 (Cc) / 100000003 (Bcc)** — the BFF is coded to these. If Dataverse auto-assigns different numbers, either override them or tell Claude the actual values → update the BFF constants + redeploy.

### Not yet packaged (offer when schema/PCFs are done)
- **Standalone page** deploy: `sprk_communicationspage` web resource (`scripts/Deploy-AllDataGridConsumers.ps1 -Only sprk_communicationspage`).
- **Widget** dual-redeploy (LegalWorkspace + SpaarkeAi) — ⚠️ pre-existing Compose-dep gap (mammoth/tiptap) blocks the full prod bundle on both shells (not R2).
- **Grid chips**: paste `filterChips` block from `notes/041-grid-curation.md` into config `e1826c4c-…`.

### Open findings (see README "Open findings")
- **[MED]** Thread name re-derive method exists but **trigger not wired** — thread edits are client-side `Xrm.WebApi`, bypass BFF → needs a Dataverse plugin on Update (task 071).
- **[MED]** VisualHost "unread" has no backing field (count-only) (task 023).
- **[MED, pre-existing, not R2]** Compose-dep prod-bundle gap on both shells (task 030).

---

## Locked decisions (spec §10 + resolved)
Q1 build participant junction · Q2 no category/tags · Q3 upgrade grid/widget in place · Q4 ship standalone page · Q5 all 11 entities · Q-C two typed lookups (ADR-034 path-C / ADR-048) · Q-D write unresolved-address rows · Q-E stay BFF-polling (no notification-spine dependency).

## Commits (all pushed to `work/messaging-communication-app-r2`)
- `0eb17bd4f` merge origin/master (pre-deploy worktree update)
- `2be760697` 090 wrap-up (code-complete)
- `72baa8132` W8 seam tests + arch doc
- `41d5e355d` W2/W3/W5/W6/W7 (surfaces, participant index, auto-threading)
- `54952bbcd` W1 reads + grid curation
- `e1e9835b1` W0 foundation + standalone page
- `c0edd4a88` / `b75696303` / `7e2883710` project init + POMLs + portfolio

## Key artifacts
`spec.md` · `plan.md` · `README.md` (status + Owner deploy gates + Open findings) · `tasks/TASK-INDEX.md` (all ✅) · `notes/lessons-learned.md` · `notes/test-diet-report.md` · `.claude/adr/ADR-048-communication-participant-index.md` · schema scripts `scripts/Deploy-ThreadRegardingSchema.ps1` + `scripts/Deploy-CommunicationParticipantSchema.ps1` + `projects/.../scripts/Verify-CommunicationTimelineRegardingPlacement.ps1`.

## Portfolio
Project #662 (Epic #431), Board #2, Status=Active, Task Count 21 / Completed 21.

## R1 status
Complete + deployed + archived (Project #654).
