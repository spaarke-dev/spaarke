# Deferrals & Issues — Assistant Enhancements R1

> Source of truth for deferred work + issues. Each entry names a **concrete failing behavior/contract**
> (CLAUDE.md §11). `push-to-github` Step 1.6 blocks push on entries without a GitHub Issue URL — file via
> `/project-defer-issue-tracking` (`/defer`) at push time.

| ID | Title | Type | Origin | GitHub Issue |
|----|-------|------|--------|--------------|
| D-032-01 | FR-E5 BU/team enrichment in the User fragment | Deferral | task 032 (owner sign-off 2026-07-15) | ✅ RESOLVED 2026-07-16 (built) |
| D-043-01 | No client-accessible preference source for the SNS chip reorder | Gap | task 043 (FR-G1) | {URL} |
| D-042-01 | User-scope MemoryItem seed deferred (no client memory-write endpoint) | Deferral | task 042 (FR-F3) | ✅ RESOLVED 2026-07-16 (built) |
| D-042-02 | Profile-write authZ depends on `sprk_userprofile` Dataverse row-security config | Security follow-up | task 042 (052 write-side hand-off) | ✅ RESOLVED 2026-07-16 |
| D-013-01 | Smart pre-seed **resolvedLookups server enrichment** (wire the 010 resolver into dispatch) | Deferral (part 3 of 013) | task 013 (2026-07-16) | {URL} |
| D-013-02 | create-todo OOB pre-fill needs **logical-name draftValues** (title→sprk_name, description→sprk_description, priority_suggestion→sprk_priority) | Gap | task 013b (2026-07-16) | {URL} |
| D-013-03 | create-task Event-subtype **display name** for the dropdown (only the Task GUID is client-side) | Gap | task 013b (2026-07-16) | {URL} |
| D-013-04 | **completeHandoff(recordId)** not wired into wizard `onFinish` → launches read back as "cancelled" (P5 honest-ack on the launch path) | Gap | task 013b (2026-07-16) | {URL} |

---

## D-013-01 — Smart pre-seed resolvedLookups server enrichment (part 3 of task 013)

- **Concrete behavior deferred**: the create-matter draft's `practice_area_suggestion` / `matter_type_suggestion` LABELS are NOT yet resolved to GUIDs server-side, so the launched Create Matter wizard opens with the name/description pre-filled but the **practice-area / matter-type dropdowns are NOT pre-selected** (the user picks them). The 010 constrained-field resolver is built + tested but **not yet wired into the dispatch path** — it has no live consumer until this lands.
- **Why deferred**: the enrichment injects a dependency into the core `SessionDispatchOrchestrator` (highest-blast-radius dispatch class) + needs a seam test (ADR-043 DoD). Doing it at the tail of a long session risked breaking the core dispatch class / its test harness. Design fully captured (below) — it's a focused pass, not open-ended.
- **Design (ready to implement)**: a new `ISurfaceLaunchEnricher` (Services/Ai), injected OPTIONALLY (nullable, last ctor param `= null`, or via `GetService`) into the orchestrator so existing test constructions don't break. Before `RouteAsync` (SessionDispatchOrchestrator.cs ~line 501), when `binding.Disposition == SurfaceLaunch`, call `output = await enricher.EnrichAsync(binding, output, ct)`. The enricher holds an R1 field-map keyed by `sprk_consumertype`: `create-matter → [(practice_area_suggestion → sprk_matter.sprk_practicearea), (matter_type_suggestion → sprk_matter.sprk_mattertype)]`; for each, call `IConstrainedFieldResolver.ResolveAsync` → build a `resolvedLookups` object (keyed by the ATTRIBUTE, value `{recordId, confidence, candidates?}` matching the 012 envelope `ResolvedLookup`) → merge into the payload JSON (JsonNode) → return. The router still stores an opaque payload it never parses. Register unconditionally (deps unconditional). Seam test: surface_launch create-matter dispatch → stored payload carries `resolvedLookups.sprk_practicearea.recordId`.
- **Client is ready**: 013b's wizard mappers already pre-select a dropdown when a `resolvedLookups` entry has `confidence === 'high'` — so this enrichment lights up the smart pre-select with no further client change.
- **Trigger to revisit**: next focused dispatch-spine pass (pairs naturally with 021).

## D-013-02/03/04 — 013b launch-path follow-ons

- **D-013-02** (create-todo OOB pre-fill): `launchSurface` passes `draftValues` to the OOB `sprk_todo` form as `defaultValues`, which require Dataverse **logical column names**. The create-todo Action emits `{title, description, priority_suggestion}` — not logical names. Fix: normalize to `{sprk_name, sprk_description, sprk_priority}` (server enrichment for create-todo, OR a client todo key-normalizer at the call-site). Small.
- **D-013-03** (event-subtype display): the create-task registry preset injects the Task-subtype GUID as a bare `sprk_eventtype_ref`; the event wizard mapper deliberately does NOT bind it to the dropdown (the display NAME is unknown client-side → would blank the label). Fix: server enrichment supplies the name, or an on-mount name resolve in the Event wizard.
- **D-013-04** (honest-ack on launch path): the launched wizards close via their existing `onClose`/`closeDialog` and do NOT write the committed `SurfaceHandoffResult` (record id) that `launchSurface` reads back — so a successful launch currently reads back as "cancelled". Fix: wire `completeHandoff(recordId)` (from `useWizardPageBootstrap`, shipped in 012) into each wizard's `onFinish`/success path. This is the P5 honest-ack (task 020 invariant) for the *launch* path — 020 shipped the ack contract but predates the 012 `completeHandoff` seam, so this specific wiring is new. Small but needed for a truthful "✅ Created" claim on launches.

---

## D-032-01 — FR-E5 role/BU/team enrichment (BU/team portion)

- **Decision**: Descoped from R1 by owner sign-off (2026-07-15). Recorded in `notes/032-budget-and-caching-decision.md` §4.
- **What ships in R1**: FR-E5's acceptance criterion — *"role reflected in the profile fragment"* — is **met**: task 030 renders the stated `sprk_primaryrole` label in the stated-profile block. Only the **BU/team** enrichment is deferred.
- **Concrete behavior deferred**: the assistant's one biased turn does NOT carry the caller's **business unit / team** membership. A user whose org-unit context would sharpen the assistant's framing gets no BU/team signal in the User fragment.
- **Why deferred** (not dropped):
  1. The carried-forward note named the wrong service — `IMembershipResolverService` resolves **record-membership** (which `sprk_matter`/document/event rows a user is on, keyed by target entityType), **not** org BU/team. BU/team live on `systemuser.businessunitid` + `teammembership`, carried by `IdentityNormalizationService.PersonIdentity`.
  2. It is **organizational scope** — the design explicitly defers `IOrganizationalContextProvider` and treats the org graph as "reference, not copy"; folding BU/team into the *User* slice back-doors deferred org-scope.
  3. It adds a second per-turn hot-path read for unproven bias value.
- **Cheapest in-boundary path when picked up**: render `- Business Unit: …` / `- Team: …` from the **already-cached** `IdentityNormalizationService.PersonIdentity` (no new Dataverse read), included in the byte-stable render + the (now 700) User budget. This is a small, self-contained follow-up — not a new pipeline.
- **Trigger to revisit**: owner wants org-unit-aware assistant framing, OR the Organizational slice / Work IQ seam gets wired (natural home for BU/team).
- ✅ **RESOLVED 2026-07-16 (owner-requested build)**: BU/team **names** folded into the User fragment as a deterministic `### Your Organization` block, via a new `UserOrgContextReader` (reuses the caller's resolved systemuserid + `IIdentityNormalizationService`, Redis-cached 10-min TTL per-systemuserid; soft-fails to null). Preference-only (ADR-039 — never reaches `AgentToolFilterContext`, pinned by test); stays within the 700 User budget (~560 worst); byte-stable (no golden re-baseline needed). This is the profile-fragment *context* form (a prompting hint); record *visibility* remains the membership-resolver's job (the "my open tasks" filter). Note: `notes/fr-e5-bu-team-enrichment-decision.md`.

## D-043-01 — No client-accessible preference source for the SNS chip reorder

- **Decision**: the FR-G1 reorder mechanism shipped (task 043) but its preference input is **not yet wired** — surfaced, not fabricated (per the task's anti-guessing instruction).
- **Concrete behavior deferred**: the Suggested-Next-Steps chip reorder (`reorderChipsForDisplay` in `chipDisplayOrder.ts`) is deterministic + preference-keyed + ADR-039-clean, but **no client-accessible source of the user's chip-ordering preference exists**, so every call site passes none and the reorder deterministically falls back to the server-declared (`sprk_chiptransitions`) order. A user whose stated/learned preference should re-rank their suggestions gets the default order.
- **Why deferred**: the stated profile (`sprk_userprofile`) is read **server-side only** (`StatedProfileReader` → `ContextBinder.userFragment` as LLM prompt text); there is no GET endpoint / session-bootstrap field / SSE frame projecting it to the browser, and there is no structured "chip-ordering preference" field in the User Model yet (only free-text `sprk_assistantpreferences`).
- **What it needs when picked up** (two parts, both small, no sort-mechanics change): (1) a **structured chip-order preference** in the User Model (a `preferredBindingOrder`-shaped signal, stated via the questionnaire and/or learned via the "shape suggestions over time" capability, spec §5); (2) a **client projection** of it — either a session-bootstrap field or a client-side Dataverse read of `sprk_userprofile` — passed into `useConsumerChips` as `chipDisplayPreference`. The comparator already accepts it verbatim.
- **Trigger to revisit**: task 042 (My Assistant questionnaire) or the preference-update capability (spec §5) lands, OR a session-bootstrap profile projection is added. Natural pairing with 042.

## D-042-01 — User-scope MemoryItem seed deferred

- **Decision**: task 042 built the typed-profile write but **deferred the "seed a User-scope MemoryItem (source=user)"** acceptance criterion (FR-F3) — surfaced, not silently dropped.
- **Concrete behavior deferred**: the questionnaire does not write a `MemoryItem` on submit; the "learned-signal" seed of the User memory does not happen at profile-completion time.
- **Why deferred**: there is **no client-callable memory-write/seed endpoint**. `MemoryWriteHandler` (`memory.write`) is **AI-initiated only** — it requires a `ChatInvocationContext` + the confirmation gate and always writes `source=ai-derived` keyed by the server-resolved `context.UserId`; `MemoryGovernanceEndpoints` exposes only `GET/DELETE /api/memory/user` (no `POST` seed). A questionnaire submit is a user gesture, not an AI turn, so there is no natural entry. Adding a `POST` seed endpoint is new BFF hot-path surface (§10) — avoided per the task's data-access guidance.
- **Mitigation (why low-impact)**: the typed profile (primary deliverable) + the shipped stated-profile READ path (task 030 → `ContextBinder.userFragment`) already deliver the User Model to the assistant on the next turn. The seed is redundant-secondary.
- **What it needs when picked up**: either (a) a narrow user-initiated memory-seed path (justify the BFF surface per §10), or (b) accept that the typed profile is the sole stated-profile channel and formally drop the MemoryItem-seed clause from FR-F3.
- **Trigger to revisit**: an explicit user-initiated memory-write capability is designed, OR the owner rules the seed clause unnecessary.
- ✅ **RESOLVED 2026-07-16 (owner-requested build)**: added `POST /api/memory/user/seed` — writes ONE User-scope `MemoryItem` (`source=user`) keyed by the **server-resolved caller** systemuserid (never the body → can't seed another user; 052 discipline; 5 tests pin it), upsert-by-(factType,key) via the existing `IMemoryItemStore` (no second store, ADR-042). §10 Placement Justification embedded; publish-size ~0 delta; no new CVE. Questionnaire submit best-effort-calls it (`@spaarke/auth`), never blocking the save. **Verified: User-memory recall IS live on the interactive chat path** (`PlaybookChatContextProvider → ContextBinder → AppendUserMemoryFragment`), so a seed surfaces next turn.

## D-042-02 — Profile-write authZ depends on `sprk_userprofile` row-security config (SECURITY)

- **Concrete failure scenario**: task 042 writes the profile client-side via a keyed upsert `PATCH sprk_userprofiles(sprk_systemuser=<id>)` in the host session. The questionnaire UI only ever supplies the **caller's own** Xrm `systemuserid`, so normal use is self-scoped. BUT a user with devtools could call the same-origin Web API directly with a **forged `sprk_systemuser` key**; if `sprk_userprofile`'s Dataverse security role permits creating/updating a row whose `sprk_systemuser` lookup points at **another** user, that user's server-side stated-profile read (task 030, keyed by `sprk_systemuser`) would then fold **attacker-authored text** into the victim's prompt (cross-user profile seeding). This is the mirror, on the WRITE side, of the app-only READ isolation ratified in 052 F1.
- **Mitigations present**: client sources the caller's own id; the client write runs under the **caller's** Dataverse privileges (row-level security applies, unlike the server app-only read); free-text is length/newline-hygiened at write time; and 052 F3/F4 proved profile text can never flip grounding/dispatch — the blast radius is tone/selection bias in the victim's own turn, not control-flow.
- **What it needs**: confirm/lock the `sprk_userprofile` security role so a user can only create/update a row keyed to **their own** `systemuserid` (or move the write behind a server endpoint that re-derives the caller key server-side — heavier, new BFF surface). This is a **Dataverse security-role configuration** task, not a client-code defect.
- **Trigger to revisit**: before production enablement of the questionnaire write; owner/security review of the `sprk_userprofile` role.
- ✅ **RESOLVED 2026-07-16**: owner confirmed `sprk_userprofile` is **user-scoped** — a user cannot create/update a row keyed to another user's `sprk_systemuser`, so the forge vector is closed at the Dataverse role level. No code change needed.
