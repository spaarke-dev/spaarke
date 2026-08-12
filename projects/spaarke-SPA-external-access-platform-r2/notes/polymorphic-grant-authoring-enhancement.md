# Enhancement: Polymorphic external grant authoring (write-side companion to task 028)

> 2026-08-10 · Owner-steered across the task-028 review. This is the WRITE/admin half of the
> polymorphic external-access model whose READ half shipped in task 028. Grounded in the teams-app-r1
> code inventory (closed project; R2 now owns its shared components) + live schema verification.

## Why

Task 028 made the external SPA **read** grants polymorphically (a contact sees records across accessible
Project **OR** Matter **OR** Work-Assignment roots, with documents/invoices rolling up). But the
**grant-authoring** path built by teams-app-r1 (TrackingFieldTrio PCF + AccessGrantModal + BFF
`/api/v1/external-access/grant`) is **project-only** — hardcoded `sprk_projectid`. So today you cannot
grant a Matter or Work Assignment to an external contact, and "Manage Access" errors on a Matter form
(`retrieveRecord('sprk_project', <matterId>)` fails). teams-app-r1 deferred this to R2 by name
(design.md:114/131/181/194; spec.md:33). **This IS the R2 project** → it's in-charter, not scope creep.

## Locked decisions (owner)

1. **`sprk_accesspermission`** global choice (Standard/Limited/Restricted) is now on **all three roots**
   — Matter (pre-existing), Project + Work Assignment (added by owner 2026-08-10). Sharing gate active
   on all three. **[W3 schema — DONE by owner.]**
2. **TrackingFieldTrio placed on Matter + Project + Work Assignment forms.** **[W4 — DONE by owner.]**
3. **Contact picker = the side-pane Advanced Lookup** (not the current Fluent Combobox). It is a
   REUSABLE feature wanted across other PCF surfaces → investigated separately (see "Open: side-pane
   research"). Modal must follow MODAL-DECISION-CRITERIA / MODAL-DESIGN-SYSTEM + shared UI components
   (already true: AccessGrantModal is built on the `SprkModal` base shell).
4. **Task 020 = Option B** — one Contact-free table `sprk_approlemodulemap` (App-Role→module code),
   created + seeded by owner. External tabs entitled to all CIAM contacts in code (no external
   entitlement table). **[020 schema — DONE by owner.]**
5. **Grant-table lookup names** (verified): read/filter `_sprk_project_value` / `_sprk_matter_value` /
   `_sprk_workassignment_value`; write `sprk_projectid@odata.bind`→`/sprk_projects` ·
   `sprk_matterid@odata.bind`→`/sprk_matters` · `sprk_workassignmentid@odata.bind`→`/sprk_workassignments`.
6. **Tab sets per plane** (owner):
   - **Internal (workforce):** Quick Start (pinned) · Service Requests · Policy Library
   - **External (outside counsel):** Work Assignments · Projects · Matters · Invoices · Documents

## Generalization surface (from the teams-app-r1 code inventory)

Already polymorphic (no change): the `sprk_externalrecordaccess` table, task-028 reads,
`AccessibleRecordSetAuthorizationFilter`, standing grants, revoke (by record id). **Work concentrates in
exactly three places** + one latent bug:

- **① BFF grant WRITE** — `GrantExternalAccessEndpoint.BuildGrantPayload` hardcodes `sprk_projectid@odata.bind`
  (line 175); DTO requires `ProjectId`. Latent bug: `ProjectClosureEndpoint.cs:162` filters
  `_sprk_projectid_value` (should be `_sprk_project_value`) → project-close cascade-revoke likely matches
  zero rows today.
- **② PCF host** `TrackingFieldTrio/index.ts` — `GRANT_RECORD_ENTITY='sprk_project'` (134), project-only
  `CANDIDATE_ROLE_FIELDS` (146-153; Matter + WA carry the SAME `sprk_assigned*` lookups — verified),
  grant-read filter `_sprk_projectid_value` (380), `retrieveRecord('sprk_project',…)` (347).
- **③ AccessGrantModal** — entity-agnostic EXCEPT 3 `projectId:` body keys (310/320/442) that track the
  BFF DTO. Contact picker (Combobox) → side-pane Advanced Lookup via an injected host callback (keeps the
  shared modal Xrm-free).

## Task breakdown

| Task | Scope | Status |
|---|---|---|
| **W3** schema — `sprk_accesspermission` on Project + WA | choice mirror of Matter | ✅ owner-done |
| **W4** form placement — trio on Matter/Project/WA forms | maker | ✅ owner-done |
| **020** Tier-1 schema — `sprk_approlemodulemap` (Option B) | table + key + seed | ✅ owner-done |
| **070** BFF polymorphic grant-write | DTO `{recordType,recordId}`; `BuildGrantPayload` switch to bind the right typed lookup; relax `ProjectId`-required on `/revoke`+`/invite`+`/invite-and-grant`; **fix close-project `_sprk_projectid_value` bug**; tests; publish-size; redeploy from worktree | POML to author |
| **071** PCF + modal polymorphism + side-pane lookup | derive host entity (drop `GRANT_RECORD_ENTITY` hardcode); per-entity role-field map; grant-read filter by root; modal 3 body keys → `recordType`/`recordId`; **reusable side-pane Advanced-Lookup helper** (per research); PCF version bump + solution import | POML after side-pane research returns |
| **072** Entitlement Option-B wiring | amends **021** (resolver: internal reads `sprk_approlemodulemap` App-Role→module; blanket-entitle CIAM plane) + **022** (`/me`) + `widgetRegistry.ts` tab config per the owner tab lists (internal defaults = service-requests+policy-library; ciam defaults = all 5; drop `requiredEntitlement:'assigned-work'` on ciam widgets) | POML to author (or fold into 021/022) |
| **073** Deploy + both-plane UAT | BFF (worktree) + PCF (solution) live; UAT: grant a Matter/WA to an external contact → visible in SPA with children rolling up | POML to author |

Coordination: touches shared `Spaarke.UI.Components` + the PCF (formerly teams-app-r1's, now R2-owned);
`/conflict-check` before the BFF + PCF PRs. Builds on task 028 (shipped).

## Side-pane Advanced Lookup — RESOLVED (reuse the existing helper, no new abstraction)

The reusable helper already exists in `@spaarke/ui-components` and is proven across surfaces:
- **`INavigationService.openLookup(options: LookupOptions): Promise<LookupResult[]>`**
  (`src/types/serviceInterfaces.ts:363`) — "In a Dataverse-hosted context delegates to
  `Xrm.Utility.lookupObjects`" (the modern **Advanced Lookup** UX); in SPA/BFF "returns an empty array
  as a graceful no-op."
- Adapters: **`createXrmNavigationService()`** (`utils/adapters/xrmNavigationServiceAdapter.ts:92`, calls
  `Xrm.Utility.lookupObjects` with `entityTypes`/`allowMultiSelect`/`defaultViewId` + `cleanGuid`
  normalization so picker GUIDs never 400 an `@odata.bind`), **`createBffNavigationService()`** (SPA
  fallback), **`createMockNavigationService()`** (tests).

**Design for task 071:** `AccessGrantModal` takes an injected `openLookup` (via `INavigationService`, the
same DI pattern as its other callbacks — modal stays Xrm-free); the **TrackingFieldTrio PCF** wires
`createXrmNavigationService()`. Replace section-2's Fluent Combobox with a "Select contact" button that
calls `openLookup({ entityTypes:['contact'], allowMultiSelect:false })`. **Cross-surface reuse is already
achieved** — any PCF injects `createXrmNavigationService`; the external SPA injects the BFF adapter. The
"reusable side-pane lookup across PCF surfaces" goal is met by ADOPTING this existing abstraction, not
building a new one (§11).

> Note: the external `researcher` pass on this was aborted by the account's monthly Anthropic spend cap
> (billing, not code) — moot, since the repo already carries the canonical helper.

## Companion-code clarification (for the record)
The owner creates ONLY schema (`sprk_accesspermission` on Project/WA ✅, `sprk_approlemodulemap` ✅). All
resolver/widgetRegistry/BFF/PCF wiring is CODE delivered by tasks 070/071/072 — nothing further for the
owner to create.
