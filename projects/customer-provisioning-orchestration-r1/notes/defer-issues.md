# Defer / Issue Tracking — customer-provisioning-orchestration-r1

> **Source of truth** for deferred work + newly-discovered issues in this project.
> Each entry has a paired GitHub Issue. See `/project-defer-issue-tracking` skill for the protocol.
>
> **Rollup view**: `gh issue list --label customer-provisioning-orchestration-r1` (visible to whole team via portfolio board)
> **CLAUDE.md §11 rule**: every entry MUST name a concrete behavior or contract that fails without it.

---

## Open (in priority order)

### ISS-042 — Stamp users cannot read seeded event types (user-owned table, Deep read)

| Field | Value |
|---|---|
| **Status** | In Progress — owner ruled Global read 2026-11-25; implementer PR pending |
| **Urgency** | now (blocks the event-type picker on every stamp once ISS-039 seeds rows) |
| **Filed** | 2026-11-25 |
| **Source** | Adversarial review of PR #3088; confirmed read-only on ACME 2026-11-25 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/3105 |

**Description**

`sprk_eventtype_ref` is user-owned; Spaarke Basic, Core and Office Add In User read it at Deep. Rows seeded by H12b are
owned by the control-plane app user (`sprk-controlplane-dev-uami`), which on ACME sits in the root unit `spaarkedemo`;
ACME's licensed users sit in the child unit "ACME Corporation". Deep never reaches a parent unit, so the Create Event
wizard's event-type picker comes back empty with no error. Record types, agreement types and triage categories are
org-owned and read at Global, so they are unaffected.

**Entry-points**

- `src/dataverse/solutions/SpaarkeMaster/Roles/Spaarke {Basic,Core,Office Add In} User.xml` (`prvReadsprk_EventType_Ref`)
- `config/feature-role-privileges.json`, `config/core-user-wizard-linking-role.json`

**Fix (owner ruling 2026-11-25)**

Raise the read to Global on the three roles (precedent #2474). Ships in the next SpaarkeMaster release; dev gets it via
`Set-FeatureRolePrivileges.ps1 -Gap ISS-042` before the next export (owner OK); a stamp gets it at its next upgrade run
(Remove-EbillingRateCardCoreLookups.ps1 report mode first, past 1.3.2.1).

**Estimated effort**: 1–2 h code + the release
**Blockers**: next SpaarkeMaster release
**Related**: ISS-039 #3075, PR #3088, #2474, #2401

---

### ISS-040 — EventsPage config names a "Communication" event type id that exists on no environment

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-11-24 |
| **Source** | PR #3088 (ISS-039 D1+D2): the export of dev's `sprk_eventtype_ref` has no row with this id |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/3089 |

**Description**

`src/solutions/EventsPage/src/config/eventConfig.ts` lists "Communication" with id `348f8195-99ff-f011-8406-7c1e525abd8b`.
Dev has no such row; it has Email, Phone Call, Teams Message, Text Message and Postal Mail instead. On every environment
that entry matches nothing, so it is dead configuration. PR #3088's ArchTest baselines the id.

**Entry-points**

- `src/solutions/EventsPage/src/config/eventConfig.ts` (search `348f8195`)
- `tests/Spaarke.ArchTests/ReferenceRowIdLiteral.baseline.txt`

**Suggested fix**

Owner decision: retire the entry (and map the five communication types), or add the row on dev, re-export with
`scripts/seed-data/reference/Export-ReferenceData.ps1`, and drop the baseline line.

**Estimated effort**: 1–2 h after the decision
**Blockers**: owner decision
**Related**: ISS-039 #3075, PR #3088

---

### ISS-041 — 86 hard-coded dev row ids outside the seeded set are invisible to the reference-row guard

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-11-24 |
| **Source** | PR #3088 (ISS-039 D1+D2): widening the guard's pattern from `-f011-8406-` to `-f011-`/`-f111-` |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/3090 |

**Description**

The guard added by PR #3088 sees only the `-f011-8406-` id shape. A wider pattern finds 86 distinct row-id literals in
product code that no provisioning step seeds, e.g. the External Access SPA widgets' grid-configuration ids
(`3af4102c-1092-f111-…`). On a stamp the feature reading each one finds no row.

**Entry-points**

- `git grep -nE "[0-9a-f]{8}-[0-9a-f]{4}-f[01]11-[0-9a-f]{4}-[0-9a-f]{12}" -- src/server src/solutions src/client`
- `tests/Spaarke.ArchTests/ReferenceRowIdLiteralGuardTests.cs`

**Suggested fix**

Classify the ids by table: product configuration → seed file (ISS-039 pattern) or name/alternate-key lookup; solution
components → allow-list; the rest are bugs. Then widen the pattern with a ratchet baseline.

**Estimated effort**: 1–2 days
**Blockers**: none
**Related**: ISS-039 #3075, PR #3088, `notes/data-setup-plan.md`

---

### ISS-039 — New stamps get no product-configuration reference rows, while product code hard-codes dev event-type row ids

| Field | Value |
|---|---|
| **Status** | In Progress — PR #3088 (D1+D2: seed files, H12b seeder, ArchTest guard) open, in review 2026-11-24; ACME gets the rows only at the D7 live run. `KNW-011`/`KNW-012` knowledge-pack refs in `sprk_agreementtype` resolve only once L56 seeds knowledge |
| **Urgency** | now (ACME, the demo stamp, has no event types; Events page, Daily Briefing and Portfolio depend on them) |
| **Filed** | 2026-11-24 |
| **Source** | Data-setup review 2026-11-24 (read-only row counts, spaarkedev1 vs spaarke-acme); plan `notes/data-setup-plan.md` |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/3075 |

**Description**

Provisioning seeds `sprk_recordtype_ref` (H12b, 14 rows) and the AI type lookups (H12a), but no other reference table.
Three tables are product configuration, not customer taxonomy: `sprk_eventtype_ref` (17 rows on dev: per-type field
configuration JSON, side-pane form id, colour, due-date rules), `sprk_agreementtype` (10: AI classification cues,
confidence thresholds, knowledge-pack refs) and `sprk_triagecategory` (10: classifier guidance, priority weights). ACME
has 0 rows in each (read 2026-11-24). Product code also hard-codes dev row ids of `sprk_eventtype_ref`: the "Task" type
`124f5fc9-98ff-f011-8406-7c1e525abd8b` in `DailyBriefingCollector.cs:128` and `PortfolioService.cs:99`, and 11 type ids
in `src/solutions/EventsPage/src/config/eventConfig.ts:34-96` and `src/solutions/EventDetailSidePane/src/config/eventTypeConfigs.ts:282-286`.
So on any stamp, events cannot be typed, and the Daily Briefing / Portfolio task counts and the Events page form routing
match nothing — even after event types are added by hand, unless they carry the dev ids.

**Entry-points**

- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/AppConfigSeed/DataverseWebApiRecordTypeSeeder.cs` (the create-if-missing pattern to extend)
- `git grep -nE "[0-9a-f]{8}-[0-9a-f]{4}-f011-8406-" -- src/server src/solutions src/client` (hard-coded dev row ids)
- Read-only check: `GET https://spaarke-acme.crm.dynamics.com/api/data/v9.2/sprk_eventtype_refs?$select=sprk_name` → 0 rows

**Suggested fix**

Export the three tables from spaarkedev1 into source-controlled seed files that keep each row's primary key, and seed
them in H12b create-if-missing with the same ids (report drift on changed rows; never overwrite). Add an ArchTest that
every Dataverse row id hard-coded in product code exists in a seed file. Same pattern as L56 (AI content).

**Estimated effort**: 1–2 days with tests
**Blockers**: none (applying it to ACME is a live run, owner OK)
**Related**: L56 (H12a AI content, `notes/t186-acme-run-log.md`); `notes/data-setup-plan.md`; ISS-037 (H12b workspaces)

---

### ISS-038 — Handler upgrade mode (`provisionedOn`) is unusable

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | before any upgrade must run H2a's drift check or H0's compatibility check (today every upgrade runs in fresh mode, as ACME's did) |
| **Filed** | 2026-11-23 |
| **Source** | Adversarial review of the ISS-031 fix (#3017) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/3018 |

**Description**

`provisionedOn` switches H0/H2a/H4/H4b to upgrade mode. H0 then requires `currentBffVersion`, `currentSolutionVersion`,
`targetBffVersion` and `targetSolutionVersion` (else Resumable `upgrade-compat-missing-versions`, and intake cannot change
after creation); the skill sends none. Even with them, `version-compat-matrix.json` has one cell (`1.0.0-net10|S2026.08`)
while H13 records an H9 build id and `"<UniqueName> Managed x.y.z"`, so every real pair is Red. H2a's drift check counts
every what-if Modify/Deploy/Unsupported as drift and has never run on a real stamp, so it would likely quarantine.

**Fix**: one version vocabulary for H13 / matrix / H0 inputs and a populated matrix; the skill derives current + target
versions; a what-if dry run against ACME to decide which change kinds are drift. Until then #3017 acknowledges an existing
stamp with `upgradeOfProvisionedOn` and never sends `provisionedOn`.

### ISS-032 — H6 misjudges a long SpaarkeMaster upgrade: 60-minute timeout, no in-flight check, async-op status trusted over the import result

| Field | Value |
|---|---|
| **Status** | Fixed by PR #3077 (merged `42ed4abffc`, 2026-11-24): the import job + solution history decide the outcome (async op advisory), in-flight check before starting, 120-min timeout recording job ids; queries checked read-only on ACME; awaiting CI + owner merge, then a Worker deploy (owner OK) |
| **Urgency** | now (every SpaarkeMaster upgrade of a stamp) |
| **Filed** | 2026-11-22 |
| **Source** | ACME upgrade to SpaarkeMaster 1.3.2.1, run `3b6f7b65`: H6 failed `import-timeout`; the import then succeeded |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2942 |

**Description**

On 2026-11-22 H6 started a managed StageAndUpgrade of SpaarkeMaster 1.3.0.0 → 1.3.2.1 on ACME at 15:24Z. What happened:
- **16:24:38Z:** H6's `ImportTimeout` (60 min) expired and the run failed `import-timeout`.
- **16:24:41Z:** Dataverse suspended the StageAndUpgrade async operation (`428c1ad0`) and retried it.
- **16:26:47Z:** the retry failed with "Cannot start another [Import] … previous [Import] running". The import blocking it was its own first attempt. The async operation ended statecode 3 / statuscode 31 (Failed).
- **16:37:31Z:** the first attempt finished. Solution history shows `Import/InlineUpgrade Completed Success`, and 1.3.2.1 was installed with no import failures. The whole import took 73 minutes.

Three defects, all in the import wait:
1. **The timeout is too short.** 60 min is less than a real SpaarkeMaster upgrade takes on a stamp (73 min here).
2. **No check for an import in flight.** H6 starts StageAndUpgrade without checking for an import already in progress (an open solution-history entry or an incomplete import job). A resume after a timeout would have collided with the running import.
3. **The wrong completion signal.** H6 trusts the async operation's status. Here the async operation reported Failed while the import succeeded. Had H6 still been polling at 16:29Z, it would have returned `ReportedByDataverse` and quarantined a successful upgrade.

**Entry-points**

- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/SolutionImport/DataverseWebApiSolutionImporter.cs` — `PollImportAsync` (~line 603) and the import start (~line 383)
- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/SolutionImport/SolutionImportOptions.cs:33` (`ImportTimeout` = 60 min)

**Suggested fix**

Make the import job and solution history the completion signal: the import job's `completedon` and result XML, and the `msdyn_solutionhistory` entry's status and result. Treat the async operation's status as advisory.

Before starting an import, look for an open SpaarkeMaster solution-history entry or an incomplete import job. If one exists, wait for it instead of starting a second import.

Raise the default `ImportTimeout` to at least 120 min. On a timeout, record the import-job id, so that a resume can find that import and keep waiting on it.

**Estimated effort**: 4–6 h with tests
**Blockers**: none
**Related**: ISS-031; ebilling PR #2935 (the 1.3.2.0 package defect that preceded this); `notes/acme-1.3.2.0-upgrade-plan.md`

### ISS-028 — Operator scripts excuse the System Administrator field-security profile by name (#2725)

| Field | Value |
|---|---|
| Found | 2026-11-11, task 268 review (H7b had the same defect; fixed there by the fixed id `572329c1-a042-4e22-be47-367c6374ea45`) |
| Issue | https://github.com/spaarke-dev/spaarke/issues/2725 |
| Fails without it | A profile NAMED "System Administrator" with create/update on a locked column is excused by 19 scripts' `-Verify` (list in the issue), so the script reports the lock whole while an ordinary member can write the column. |
| Owner | unified-access-control-r2 / external-access r3 (most scripts), ebilling-r1 (four). Not ours to fix in 268. |

### ISS-003 — A second environment for the same customer overwrites the first one's BFF app registration

| Field | Value |
|---|---|
| **Status** | Open — not needed while owner D6 holds (one environment per customer) |
| **Urgency** | later (before per-customer staging/dev) |
| **Filed** | 2026-10-07 |
| **Source** | T240a review, verifier pass 2 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1376 |

**Description**

The registration is per customer (D-13) but H3 sets its SPA redirect, FIC (`spaarke-uami-trust`) and pre-authorizations
per run, so a second environment for the same customer breaks the first's code-page sign-in and BFF credential.

**Suggested fix**

A per-stamp registration (`spaarke-bff-api-{customerId}-{env}`), or intake refusing a second environment until then.

### ISS-004 — Any Spaarke-tenant user can get any customer BFF token (no stamp-level token gate)

| Field | Value |
|---|---|
| **Status** | Owner approved the fix 2026-10-14 (plan D37): `appRoleAssignmentRequired` on each stamp BFF service principal, assigned to `sprk-{customerId}-users`. Built after the A9 design settles home-directory workforce admission |
| **Urgency** | before the first external customer |
| **Filed** | 2026-10-07 |
| **Source** | T240a review, verifier pass 2 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1377 |

**Description**

All Model 1 BFF registrations are in Spaarke's tenant; any member or guest can get a token for any customer's BFF. Record
access is still bounded by the stamp's Dataverse/SPE authorization, but record-free endpoints (AI chat) may run on that
customer's OpenAI quota.

**Suggested fix**

`appRoleAssignmentRequired` per BFF service principal with `sprk-{customerId}-users` assigned — after checking the Type-2
external workforce plane (UAC-r2 task 141) and the External Access SPA (T240d).

### ISS-002 — Demo self-registration SPE grant: marker keyed to the demo Dataverse; expiry leaves it behind (UAC-r2 code)

| Field | Value |
|---|---|
| **Status** | Open — owned by unified-access-control-r2 (task 171 code) |
| **Urgency** | next-round (latent: "Demo 1" has no `sprk_specontainerid` today) |
| **Filed** | 2026-10-07 |
| **Source** | Adversarial verifier finding F8 on the 2026-10-07 master merge (`f442915e6`) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1363 |

**Description**

Step 8 records a standing-writer marker keyed by a systemuserid from the demo environment's Dataverse, which the
membership sync cannot resolve. Demo expiry deletes the grant directly (first page only, no `Prefer` header) and
leaves the marker. When the BFF's Dataverse and the demo's are the same, the standing pass sees a stale marker and can
re-grant writer to an expired demo user whose systemuser is still enabled.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Registration/DemoProvisioningService.cs:146`, `:169-185`, `:257-261`
- `src/server/api/Sprk.Bff.Api/Services/Registration/DemoExpirationService.cs:226`, `:308-350`
- `src/server/api/Sprk.Bff.Api/Services/Access/SpeContainerMembershipSync.cs:95-210`

**Suggested fix**

UAC-r2 decides the marker identity (or a demo prefix the standing pass ignores) and expires via
`RemoveMarkedGrantAsync`. Provisioning side (ours): configuring a demo container also needs it owned by the hosting
BFF (`SharePointEmbedded__OwnedContainerIds` or the `spaarkeCustomerId` marker), or Step 8 is skipped (T227d).

### ISS-006 — RAG `DefaultRagModel = Dedicated` reads an index nothing creates

| Field | Value |
|---|---|
| **Status** | Open — BFF code (not this project's surface) |
| **Urgency** | before anyone sets `Analysis__DefaultRagModel` on a stamp |
| **Filed** | 2026-10-08 (found in T235) |
| **Source** | T235 doc sweep — the BYOK guide told operators to set `Dedicated` |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1432 |

**Description**

`Shared` (the default) reads `AiSearch:KnowledgeIndexName` (`spaarke-files-index`, created by H2b in the stamp's own
AI Search). `Dedicated` reads `{tenantId}-knowledge`, which nothing creates, so setting it breaks every RAG search that
does not name its index. No stamp sets it today. The two docs that advised it were corrected in T235.

**Suggested fix**

Remove `Dedicated` (needed → build, else remove) or have H2b create its index; optionally rename `Shared`.
`AnalysisOptions.cs` (enum), `KnowledgeDeploymentService.cs:288-320`.

---

### ISS-008 — L2 CustomerRunGuard (I5 / FR-32) is off in every environment

| Field | Value |
|---|---|
| **Status** | Fix applied live — dev Api `CustomerRunGuard__Enabled=True` (deployed 2026-10-13 from `67c80623f`). Open only for the live proof at T186: a second concurrent POST for the same customer is refused |
| **Urgency** | before T186 |
| **Filed** | 2026-10-09 (punch-list re-verification, 203b row A27) |
| **Source** | `infrastructure/bicep/platform-controlplane.bicep` `customerRunGuardEnabled` default false; no `.bicepparam` sets it; live dev Api `CustomerRunGuard__Enabled = False` |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1484 |

**Description**

Two runs for the same customer can overlap: the guard that serializes them (spec §4D I5, FR-32 — required ON in
production) is disabled. It authenticates as the L2 UAMI against the admin Dataverse environment and fails fast at host
start when enabled without that access, which is why the default is off.

**Suggested fix**

Owner OK → ensure the UAMI is an Application User on the admin environment (`Grant-ControlPlaneIdentity.ps1`); set
`customerRunGuardEnabled = true` in `platform-controlplane-dev.bicepparam` (and the prod parameter file when created);
redeploy; prove a second concurrent run for one customer is refused.

---

### ISS-009 — External-contact bind-by-email (E11) binds contacts this stamp never invited

| Field | Value |
|---|---|
| **Status** | Open — UAC-r2 code; before any stamp accepts CIAM tokens (T240d step 2) |
| **Urgency** | before external contacts are enabled on a stamp |
| **Filed** | 2026-10-09 (T240d design) |
| **Source** | `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ContactBindingDecision.cs` `// E11` (~line 659) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1485 |

**Description**

A CIAM sign-in whose verified email matches an unbound contact is bound to it whether or not this stamp invited the
person. With one shared CIAM tenant, an account created for customer X can be bound to customer Y's same-email contact.

**Suggested fix**

Pending-invite marker written by this stamp's invitation; bind by email only when it exists; consume on bind. Also fixes
"second customer invites a person who already has a CIAM account" (fails today). Design: `notes/t240d-ciam-external-contacts-design.md` §5–§6.

---

### ISS-011 — BFF asymmetric registrations: services that cannot be constructed when a feature gate is off (204e F1–F4)

| Field | Value |
|---|---|
| **Status** | Open — BFF code (ADR-032 decision per service); latent for customer stamps |
| **Urgency** | before any environment runs with a gate off (DocIntel / AI Search) |
| **Filed** | 2026-10-09 (task 204e) |
| **Source** | `tests/Spaarke.ArchTests/Adr032/*` ledgers; `notes/task-202-punch-list.md` rows 204e-F1..F4 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1487 |

**Description**

Finance job handlers (F1), the app-only analysis job handlers + `EmbeddingMigrationService` (F2 — the BFF cannot be
constructed with `DocumentIntelligence:Enabled=false`, the code default), AI Search / chat-client consumers registered
unconditionally (F3), and ~35 services when DocIntel is on without an AI Search endpoint (F4). Stamps set DocIntel on and
the AI Search endpoint, so none of this hits T186.

**Suggested fix**

Per service: register on the dependency's gate, or give the dependency a Null-object peer (ADR-032); delete the ledger
row as each is fixed.

---

### ISS-012 — `Graph:Scopes` is required at BFF startup but nothing reads it

| Field | Value |
|---|---|
| **Status** | Open — not blocking (T258 supplies the value on every stamp) |
| **Urgency** | later (maintainability) |
| **Filed** | 2026-10-09 (T258) |
| **Source** | T258 — reading the validators behind 204e-F7 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1499 |

**Description**

`GraphOptions.Scopes` carries `[Required]` + `[MinLength(1)]` with an empty default, so a BFF without
`Graph__Scopes__0` does not start in any environment. No code reads `GraphOptions.Scopes`: every Graph client builds its
own scopes. T258 writes the literal `https://graph.microsoft.com/.default` through the manifest so stamps start. This is
the same latent-blocker class auth-v4 task 024 removed for `Graph:ClientSecret` (a rule that only prevents a boot).

**Suggested fix**

Owner decision (it changes a validator): drop the property and its two attributes, then the manifest entry
`Graph__Scopes__0` and the `IOptionsDriftTests` census row. Until then the manifest literal is correct and harmless.

### ISS-013 — The H0.5 consent callback cannot work as built (Model 2, out of scope)

| Field | Value |
|---|---|
| **Status** | Open — dormant: T258 gates the endpoint off (`Onboarding:Enabled`, default false) |
| **Urgency** | when Model 2 returns (plan D3) |
| **Filed** | 2026-10-09 (T258) |
| **Source** | T258 — deciding whether a stamp needs the Onboarding endpoint (204e-F5) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1500 |

**Description**

`POST /api/onboarding/consent-callback` (BFF, task 042) cannot carry a Model 2 consent into L2 on any current host:
(1) it enqueues to `sprk-provisioning-jobs` through the host BFF's own Service Bus client — on a customer stamp that is
the stamp's namespace, which has no such queue and which L2 does not drain; (2) Microsoft's admin-consent redirect is a
browser GET carrying `admin_consent`, `tenant` and `state` in the query, not an HMAC-signed POST, so something else
would have to sign and forward it; (3) no host was ever given an `Onboarding:HmacSigningKey` (spaarke-bff-dev ran with
`Onboarding__EnableDevBypass=true`, so the route answered 401 to every call). T258 maps the route and registers its
services only when `Onboarding:Enabled=true`; no stamp channel sets it, and `IOptionsDriftTests` fails if one does
without supplying the key.

**Suggested fix**

When Model 2 is reopened, decide the consent-capture host (L2 itself is the natural one — it owns the queue and the
run) and either rebuild the callback there or remove the BFF endpoint, `HmacSignatureVerifier`,
`ServiceBusProvisioningEnqueuer` and their tests. Needed → build, else remove.

---

### ISS-017 — `AgentToken:*` / `AgentTokenService` are registered but on no request path; a comment claims token validation that nothing does

| Field | Value |
|---|---|
| **Status** | Open (latent; nothing breaks today) |
| **Urgency** | low. Decide before the first per-customer Copilot agent live test (T257), so nobody configures `AgentToken__*` on a stamp believing it gates the agent |
| **Filed** | 2026-10-09 (T257) |
| **Source** | T257 step 2, the alignment check (design note §5.2) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1523 |

**Description**

`AgentModule` registers `AgentTokenService` (a singleton OBO client for "M365 Copilot → Graph/Dataverse") and binds
`AgentTokenOptions`. No endpoint or handler resolves either of them:
- `SpaarkeAgentHandler` has `// TODO: Inject AgentTokenService when MCI-014 is implemented`;
- the `/api/agent/*` endpoints and every endpoint the Copilot agent's OpenAPI calls use the normal OBO path.

`AgentTokenOptions.AgentAppId` is documented as "used to validate that incoming tokens were issued to the expected
agent", but no code validates `azp`/`appid` against it. It is only logged. `AgentTokenOptionsValidator` requires
`TenantId`, `ClientId`, `AgentAppId` and `DataverseEnvironmentUrl`. There is no `ValidateOnStart` and no resolver, so
the validator never runs. The canonical secret catalog still emits `AgentToken__ClientId` / `AgentToken__TenantId` app
settings.

Concrete risk: an operator or a later task reads the comment, sets `AgentToken__AgentAppId` to the "Spaarke Copilot
Agent" client, and believes the BFF now only accepts that client. It does not. Per-customer isolation comes from the
token audience and the stamp's Dataverse roles (T257 design §3), not from this setting.

**Suggested fix**

Needed → build, else remove. T257 does not need an agent-specific token path, so remove these:
- `AgentTokenService`;
- `AgentTokenOptions` and its validator;
- the `AgentToken__*` catalog app settings;
- the stale `SpaarkeAgentHandler` TODO.
If MCI-014 is ever revived, it re-adds them with a real consumer. This is BFF work (§10 hygiene, publish-size delta) for
the BFF's owning project.

---

### ISS-018 — Graph identity grant gaps found by T261: the L2 Worker lacks the roles H3/H10 call with; H3's delegated catalog has a wrong id; app-only SPE search

| Field | Value |
|---|---|
| **Status** | Open; F3 resolved, F6 code-fixed 2026-10-14 |
| **Urgency** | F3 and F6 before T186 (F3 blocks H3 and H10 outright); F2/F4/F5 low |
| **Filed** | 2026-10-09 (T261) |
| **Source** | T261 inventory — `notes/t261-stamp-graph-least-privilege.md` §8 (call sites, Learn citations, live reads) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1543 |

**Description**

- **F3 (blocks T186).** The L2 Worker identity (`sprk-controlplane-dev-uami`, read live 2026-10-09) holds neither
  `AppRoleAssignment.ReadWrite.All` nor `Application.ReadWrite.OwnedBy`. H10 (`POST/DELETE
  /servicePrincipals/{stamp}/appRoleAssignments`) and H3 (`POST /applications`, FICs, `appRoleAssignedTo`) need them;
  no prerequisite granted them (`Grant-ControlPlaneIdentity.ps1` granted the BFF stamp catalog). T261 gave the Worker
  its own catalog (`Handlers/ControlPlaneGraphAppRoles.cs`, PRQ-E-07 manifest 11). Owed: run
  `Grant-ControlPlaneIdentity.ps1` for `sprk-controlplane-dev-uami` (live, owner OK); then, optionally, remove the
  Worker roles the note lists as no longer needed (`Directory.ReadWrite.All`, `User.ReadWrite.All`, `Files.*`,
  `Sites.*`, `FileStorageContainer.Selected`, `Group.Read.All`, `User.Read.All`, `Mail.ReadWrite`, `Mail.Send`,
  `MailboxSettings.Read`).
- **F6 (risk for T186, delegated).** `EntraAppReg/EntraAppRegPermissionCatalog.cs` requests delegated
  `Files.ReadWrite.All` with id `75359482-…` — the APPLICATION role id; the delegated scope is
  `863451e7-0667-486c-a5d6-d135439485f0` (read live). It also lacks delegated `FileStorageContainer.Selected`
  (`085ca537-6565-41c2-aca7-db852babc212`), which OBO SPE calls need and the dev BFF registration carries. A stamp's OBO
  SharePoint Embedded calls may fail. Fix in H3's catalog + `scripts/Register-EntraAppRegistrations.ps1:288` (same id).
- **F2.** `POST /api/spe/search/items` calls `/search/query` app-only (`SpeAdminGraphService.cs:7432`). Microsoft
  documents SharePoint Embedded search as delegated-only, so it cannot return container content; the old catalog's
  `Files.Read.All` would have let it search all of Spaarke's SharePoint instead. Needs OBO or `$filter` enumeration
  (SPE admin owner — sdap-SPE-admin-app-r2).
- **F4 (dev).** `mi-bff-api-dev` lacks `Mail.ReadWrite` → inbound mark-as-read 403 (3/3, 2026-10-06); security pages
  `GET /security/alerts_v2` 403 7/8 (Learn least: `SecurityAlert.Read.All`; dev holds `SecurityEvents.Read.All`).
- **F5 (dev).** `createLink` 403 app-only and delegated — a sharing setting, not a Graph role; not investigated.

**Concrete failure without a fix:** H3 and H10 403 on the first live run (F3); OBO SPE calls on a new stamp may 403
(F6).

**Update 2026-10-14.** F3 resolved: read live, `sprk-controlplane-dev-uami` (principal `38f7693f…`) holds
`Application.ReadWrite.OwnedBy` and `AppRoleAssignment.ReadWrite.All`. The optional removal of the roles it no longer
needs is still open; it holds `Mail.ReadWrite`, `Mail.Send` and `MailboxSettings.Read` by owner decision D33. F6 is
fixed in code (e3f13d6b4; live proof at T186 H3): the delegated Files.ReadWrite.All id becomes `863451e7-0667-486c-a5d6-d135439485f0`, and
delegated FileStorageContainer.Selected (`085ca537-…`) is added. Both ids were read live from the Graph service
principal. F2, F4 and F5 are unchanged.

### ISS-022 — Three BFF secrets absent from the canonical catalog: `PowerBi__ClientSecret`, `Rag__ApiKey`, `Notifications__SignalR__ConnectionString`

| Field | Value |
|---|---|
| **Status** | Fixed by PR #3076 (merged `46ef2fb0e6`, 2026-11-24): H2a carries the SignalR host, H4b writes the keyless MI string + Enabled only when SignalR is deployed; manifest records PowerBI/RAG as not on stamps; awaiting CI + owner merge. Live check of the string when a stamp first enables SignalR |
| **Urgency** | soon (a stamp starts without them; nothing is broken at boot) |
| **Filed** | 2026-10-09 (from the auth-system-of-record-r1 note §3, analysis only) |
| **Source** | `notes/coordination/2026-10-09-from-auth-system-of-record-r1-webhooks.md` §3; `scripts/canonical-secret-catalog/manifest.yaml` has none of the three keys |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1570 |

**Description and recommendation (stamps are keyless, D13)**

- **`PowerBi__ClientSecret`** — Power BI embed for the Reporting module. `ReportingModule.cs:30-37,57-62` registers
  `ReportingEmbedService` and maps `/api/reporting/*` only when `PowerBi:TenantId` is set; without it the module is
  absent (404) and nothing fails at boot. `ReportingEmbedService.cs:80-84` authenticates with MSAL
  `WithClientSecret(PowerBiOptions.ClientSecret)` (`PowerBiOptions.cs:42-44`, `[Required]`), so setting TenantId without the
  secret would fail options validation. Keyless: MSAL accepts a client assertion and the BFF already has one for the UAMI
  (`Infrastructure/Auth/ManagedIdentityAssertionProvider.cs`), so a federated credential on a Power BI service-principal
  app registration removes the secret. Not available yet: that registration, the Power BI tenant setting allowing it, and
  a Power BI/Fabric embed capacity and workspace per stamp. **Recommendation: gate the feature off on stamps** (supply
  nothing; the module self-gates) until a stamp reporting design exists; then use a UAMI-federated registration, not a
  secret.
- **`Rag__ApiKey`** — the `RagApiKey` scheme (`AuthorizationModule.cs:81-92`) guards only `POST
  /api/ai/rag/enqueue-indexing` (`RagEndpoints.cs:147-152`), for non-JWT callers (scripts, bulk jobs, tests). The product
  indexing path is the Service Bus `RagIndexingJobHandler` and the JWT `send-to-index` endpoint, so no stamp feature uses
  it. Without the key, `ApiKeyAuthenticationHandler.cs:70-77` fails closed (401). The only in-repo caller is
  `scripts/Reconcile-DemoEnvironment.ps1:104`, which mints a throwaway value for the demo. **Recommendation: not needed
  on stamps.** Leave it absent from the catalog (record that decision in the manifest's notes); the endpoint stays closed.
- **`Notifications__SignalR__ConnectionString`** — Layer-C live push. `NotificationsModule.cs:39-58` registers the real
  `SignalRDeliveryService` only when `Enabled` and a connection string exist, else the Null-Object (`/negotiate` answers
  503, clients use the poll endpoint, ADR-032). `customer.bicep:549-560` deploys SignalR only when `signalrEnabled` (default
  false) and `signalr.bicep:83` sets `disableLocalAuth: true`, so the resource has no access key and a key-bearing string
  can never work on a stamp. The Management SDK (`SignalRDeliveryService.cs:246-252`, `Microsoft.Azure.SignalR.Management`
  1.33.1) accepts a keyless managed-identity string:
  `Endpoint=https://<name>.service.signalr.net;AuthType=azure.msi;ClientId=<uami client id>;Version=1.0;`
  ([Microsoft Learn](https://learn.microsoft.com/en-us/azure/azure-signalr/signalr-howto-authorize-managed-identity)).
  The UAMI already holds "SignalR App Server" (`customer.bicep:540-543`). **Recommendation: supply via managed identity,
  but only when `signalrEnabled`**: H4b writes the string as a plain per-env setting built from the Bicep outputs
  `signalrHostName` and the UAMI client id (no Key Vault secret). Default stamps (signalrEnabled=false) keep the Null-Object.
  Needs a live check that the resource accepts the string before the setting is turned on.

**Concrete failure today:** no reporting embed, no live notifications (poll fallback only) and no `enqueue-indexing`
caller on a stamp; none is a boot failure.

---

### ISS-023 — The skill's customer pass stops a NativeAccount run on PRQ-C-10

| Field | Value |
|---|---|
| **Status** | Open — low; Model 2 only (out of scope for r1) |
| **Filed** | 2026-10-09 (PRQ-C-14 work) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1590 |

`$mustResolveTokens` (Step 1e-ter) includes `environmentSecurityGroupId`, which only B2BGuest fills; a NativeAccount run stops
with "unresolved placeholder" on PRQ-C-10. Fix when Model 2 returns: resolve to `notApplicable` for non-B2BGuest runs and let
PRQ-C-10's recipe SKIP (the PRQ-C-14 pattern).

### ISS-025 — H7b does not check the `contact.sprk_externalinvitedon` (and, with UAC 185, `sprk_externalinvitecodehash`) field locks on stamps

| Field | Value |
|---|---|
| **Status** | Code complete; invitedon + codehash locks verified live on ACME by H7b (run 3b6f7b65). Follow-up (audit / Advanced Find off on the codehash column): PR #3079 merged `1609245ab0` 2026-11-24, H7b procedure 12; live once the Worker is deployed |
| **Filed** | 2026-10-13 (filed by unified-access-control-r2 for this project) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1803 |

UAC-r2 task 184 adds `contact.sprk_externalinvitedon`, the pending-invite marker E11 trusts on the CIAM plane. It is
field-secured through the existing `Spaarke BFF-Managed Field Readers` (Read) and `Writers` (Read/Create/Update) profiles and
ships in SpaarkeMaster. A user who could write it could open E11's email bind, so a stamp where the lock did not arrive is
a tenant-isolation gap. H7b (`SecureRecordSetupProcedure`) already verifies the other BFF-managed column locks but not this
one. **Fix:** add the column to H7b's lock verification (reuse `scripts/Set-ExternalInviteMarkerSchema.ps1 -Verify`'s
checks); H7b never secures a column itself. If a check ever writes field-security rows, allow for `0x80041103`
(MetadataCache lag: UAC's first `-Apply` on dev failed there although the column was readable; a re-run passed).
**Widened 2026-10-13 (UAC task 185 design):** UAC task 185 adds `contact.sprk_externalinvitecodehash`, the invite-code
hash, through `scripts/Set-ExternalInviteCodeHashSchema.ps1`. H7b must verify it too, with different grants: Readers
have **no** permission row for it (UAC's script, `48db7adb3`, never creates one; a missing row and a 0/0/0 row both
mean no access), and Writers 4/4/4. Any Readers access is a FAIL. The script refuses to run without 184's marker column.

Follow-up (2026-10-14 review): H7b does not check auditing-off / Advanced-Find-off on `sprk_externalinvitecodehash` (the Dataverse seam reads only IsSecured); add with the release that carries the column.

### ISS-026 — Publisher verification for Spaarke's multitenant apps (ADR-028 A9)

| Field | Value |
|---|---|
| **Status** | Open — owner/business action; **before the first production customer** |
| **Filed** | 2026-10-13 (ADR-028 A9, owner decision relayed by SPA-r3) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1811 |

Under A9, workforce contacts consent to, and sign in through, Spaarke's multitenant workforce SPA client and each
stamp's multitenant BFF app. Unverified multitenant apps meet risk-based step-up consent and show "unverified" to
customer admins. **Needs:** a Microsoft AI Cloud Partner Program account, and a verified publisher domain (not
`*.onmicrosoft.com`) set on those apps. Provisioning's A9 work (H3 v2 tokens + `knownClientApplications`, intake
`customerWorkforceTenantIds`, admin-consent link): `notes/coordination/2026-10-13-from-spa-r3-adr028-a9-workforce-signin.md`.

**Update 2026-10-14 (owner + research).** Spaarke has a Partner Program account: Partner ID `7011236`, publisher name
"Spaarke Legal Collaboration Platform". Research on Microsoft Learn (researcher note
`.claude/agent-memory/researcher/publisher-verification-2026-10-14.md`):
- The ID must be the partner **global** account (`PartnerGlobal`); a location ID is rejected.
- The Spaarke tenant must be associated with that account in Partner Center (Settings → Account settings → Tenants).
- The Partner Center primary-contact email domain must be `spaarke.com`; it is the tenant's verified default domain and
  the apps' `publisherDomain`.
- `setVerifiedPublisher` is **delegated-only**: a person with Cloud Application Administrator in Entra and Accounts
  Admin (or Cloud Partner Program Admin) in Partner Center, with fresh MFA. H3's app-only identity cannot set it, so
  each customer's BFF app needs an operator step after provisioning.
- Admin consent is **not** blocked for unverified apps (the prompt shows "Unverified"). Risk-based step-up affects user
  consent only. So verification does not gate onboarding or T186.

**Plan:** (1) An operator confirms the three Partner Center facts above. (2) One-time: set the Partner ID on the shared
workforce SPA client, the Office add-in apps and the Teams app (portal: App registrations → Branding & properties →
Partner ID). (3) Per customer: an operator step in the hand-off checklist (`POST /applications/{id}/setVerifiedPublisher`
`{"verifiedPublisherId":"7011236"}`), verified with `GET ?$select=verifiedPublisher`. (4) Test whether an unverified
per-customer API app (a resource, not the client) changes the consent prompt at all. If it does not, step 3 is optional.

### ISS-027 — SpaarkeMaster ships an orphan web resource `sprk_DocumentDelete.js` with hard-coded deleted BFF hosts

| Field | Value |
|---|---|
| **Status** | Fixed by PR #3074 (merged `276f5b4743`, 2026-11-24): removed from SpaarkeMaster source + legacy copy deleted; spaarkedev1 dependency check none; release owner removes it from the dev solution before the next export (live, owner OK) |
| **Filed** | 2026-10-14 (found during the old-demo retirement, D35) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1866 |

`sprk_DocumentDelete.js` ships in SpaarkeMaster, but no ribbon, form or other web resource references it. It maps org
URLs to BFF hosts and defaults to the deleted `spe-api-dev-67e2xz`. The non-shipped
`src/dataverse/webresources/spaarke_documents/DocumentOperations.js` keeps the same stale mapping. The shipped
`sprk_DocumentOperations.js` is correct, because it reads `sprk_BffApiBaseUrl`. **Fix:**
1. Run a dependency check in spaarkedev1.
2. Remove the file from SpaarkeMaster, and list it as a removed component in the release note.
3. Delete the stale repo copy.

## Resolved

### ISS-029 — New stamps lack the invoice-family (#2799) and message-access field locks; H7b does not check them (#2859)

| Field | Value |
|---|---|
| Found | 2026-11-15, master review (`9fef284de0`..`ce6c241964`) |
| Issue | https://github.com/spaarke-dev/spaarke/issues/2859 |
| Fails without it | On a new stamp, users can write the 24 invoice-family lookups (#2799: `Set-InvoiceFamilyFieldSecurity.ps1`; not secured in SpaarkeMaster 1.3.1.0) and so re-file invoice records past the BFF's checks. The message-access lock (`Set-ThreadFlagFieldSecurity.ps1`) is described in guide §7.4 as run by provisioning, but nothing runs it. H7b checks only S14 and S26. |
| Owner | This project: task 270 (owner go 2026-11-16). Owner chose path A on 2026-11-16: uac-r2 packages the #2799 lock in SpaarkeMaster (branch `fix/2859-invoice-lock-in-spaarkemaster`, planned 1.3.2.0), and H7b verifies it (S27) and the message-access lock (S28, procedure version 10). Open until uac-r2's release is published and S27's package-row check is added to `BffManagedLinkColumnLockListTests`. |
| Resolved | 2026-11-16. Task 270 (PR #2862 → `4256166205`): H7b procedure 10 verifies the invoice-family lock (S27) and the message-access lock (S28); remedies name only routes that work on a stamp. uac-r2 #2865 → `57431c8e6a`: SpaarkeMaster 1.3.2.0 carries the #2799 lock (pinned by `InvoiceFamilyFieldLockSourceGuardTests`); S27's remedy names 1.3.2.0. #2859 closed. Not yet published or imported anywhere (uac-r2, owner go); no L2 Worker with procedure 10 before 1.3.2.0 is published. |

<!-- Resolved entries move here with the resolution date and commit/PR. -->

### ISS-037 — H12b never seeded the Worklist, Decision Records, Reconciliation and Compose workspaces

| Field | Value |
|---|---|
| **Status** | Fixed by PR #2993 (merged `5e4cf470d6`, 2026-11-23); ACME seeded live 2026-11-23; dev L2 Worker deployed from master `ca1684dd2f` 2026-11-23 (owner OK), healthy, package carries all 16 layouts |
| **Urgency** | before the next stamp run (every new stamp lacks the four workspaces) |
| **Filed** | 2026-11-23 |
| **Source** | ACME guest-test prep after run `3b6f7b65`: the Console's Manage Workspaces showed 12 system workspaces, dev 16 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2991 |

**Description**

H12b (`DataverseWebApiWorkspaceLayoutSeeder`) seeds only `scripts/system-layouts.json`. Worklist, Decision Records and
Reconciliation were seeded on dev by standalone `scripts/seed-*-workspace-layout.ps1` scripts, and Compose by hand
(2026-06-29), so no provisioned stamp had them.

**Done**
- ACME (owner OK "Fix both"): the three scripts ran after a `-WhatIf`, and Compose was created from dev's row (sort 5, section `compose-editor`). Read-back: 16 system rows, Daily Briefing the only default.
- PR #2993: the four layouts are added to `system-layouts.json`, plus a guard test (every standalone seed script's layout is listed with the same section; it fails on master's list).

**Remaining**: none on dev. Any other L2 environment (demo, prod) picks the list up at its next Worker deploy from master; move this entry to Resolved at the next defer-issues review.

### ISS-036 — intake.schema.json lacks `buildId`, so an upgrade run cannot pin the BFF build from a valid intake

| Field | Value |
|---|---|
| **Status** | Fixed by PR #3012 (merged `b9a4d6b5a7`, 2026-11-23): schema `buildId` + parity test + skill Step 1.0/4.0 send it; no deploy needed (skill / prereq files only) |
| **Urgency** | before the next upgrade run that must not move the BFF |
| **Filed** | 2026-11-23 |
| **Source** | ACME upgrade run `3b6f7b65` (2026-11-20): `buildId = 2026.10.16-2` was added to the POST outside the schema-valid intake — a recorded deviation |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2987 |

**Description**

`IntakeParameterCatalog.cs:200` accepts `buildId` ("BFF build to deploy (H9 optional — defaults to latest.json)"), but
`scripts/provisioning-prereqs/intake.schema.json` does not list it. Pinning the build is the only guard on an upgrade that
changes the solution but must not change the BFF (ACME: the BFF upgrade waits on A9 home sign-in). Without it in the schema
every such run deviates from the skill, or deploys whatever `latest.json` names.

**Suggested fix**: add `buildId` (optional string, H9's pattern) to the schema; mention it in the skill's upgrade-mode
intake step; add a test that every catalog key has a schema property or is listed as server-only.

**Estimated effort**: 1–2 h with the parity test
**Related**: ISS-031; `notes/acme-1.3.2.0-upgrade-plan.md` step 2

### ISS-035 — A completed run keeps its handlers' failure-marker gates `Pending`

| Field | Value |
|---|---|
| **Status** | Fixed by PR #3013 (merged `b76078668c`, 2026-11-23): `RecordFailureGate` tags markers, `MarkPhaseSucceeded` clears them, H13 records its own success; dev L2 Worker deployed from master `7dc36ac0f5` 2026-11-23 (owner OK), healthy |
| **Urgency** | before any tooling reads `gateStates` (fleet view, provisioning web UI) |
| **Filed** | 2026-11-23 |
| **Source** | ACME upgrade run `3b6f7b65` ended `Completed` with five `Pending` gates and H13 in `failedHandlers` |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2986 |

**Description**

Failure paths write `run.GateStates[$"{h}-{rejectionCode}"]` as `Pending` (H2a, H2b `H2bAiSearchIndexHandler.cs:516`,
H4b, H9 `H9BffDeployHandler.cs:884`, H10, H12a, H12b, and H13/H6 alike); a later success never clears them. Run
`3b6f7b65` shows `h2b-index-provisioning-failed`, `h6-import-timeout`, `h9-slot-swap-infra-fault`,
`h9-scheduled-jobs-slot-guard-failed` and `h13-h13-extended-validation-inconclusive` (H13's codes already carry the
`h13-` prefix the key adds). A reader cannot tell from the record whether anything is owed.

**Suggested fix**: on a handler's success, set its own failure-marker gates to `Cleared` (verifier + time), keeping them as
history; clear or rename `failedHandlers`.

**Estimated effort**: 2–3 h with tests
**Related**: ISS-034

### ISS-034 — H13 keyless-proof times out against a BFF still warming up after H9's swap

| Field | Value |
|---|---|
| **Status** | Fixed — PR #2980 merged `c4d7da8d57` 2026-11-23 (H13 repeats a no-verdict keyless proof with 30 s → 5 min waits for up to 40 min; never a failure, an earlier failed check or a 404); dev L2 Worker deployed; ACME H13 passed on resume (first attempt, 5 s). Stamp sizing (second bullet below) remains an owner decision |
| **Urgency** | now (every H13 within ~30–45 min of a BFF deploy on S1 fails; workaround: wait for the plan CPU to settle, then resume) |
| **Filed** | 2026-11-22 |
| **Source** | ACME upgrade run `3b6f7b65`: H13 failed `h13-extended-validation-inconclusive` (keyless-proof: no answer within 90 s) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2970 |

**Description**

H9 finished the swap at 19:22:07Z, and the new production BFF process had started at 19:20:36Z. H13 called `POST /api/platform/keyless-proof` at 19:23:02Z. ACME's App Insights shows the call took 98.5 s in all:
- the request waited 33 s before the endpoint ran (19:23:35Z);
- the `openai-chat` probe timed out after 36 s, over its 30 s cap, although the OpenAI call itself returned 200 in 1.5 s at 19:24:17Z;
- the BFF answered at 19:24:40Z, logged as 499.

H13 had given up at `KeylessProofTimeout` (90 s), so the run failed. The isolation census that followed took 45 s and returned Isolated. The owner chose to resume (2026-11-22).

**Second attempt and corrected cause.** The resume at 19:33:53Z failed the same way: the BFF took 119 s, and `openai-chat` sent its request only 42 ms before its 30 s limit. Redis GETs took 19–20 s and the BFF logged `RedisTimeoutException` with nothing in flight. The process was not cold; the plan was saturated. `sprk-acme-prod-plan` is S1 with 1 worker, running both BFF slots. Its CPU:
- 24–30% average 15:00–16:30Z;
- 89–93% for 16:45–17:15Z after the first H9 deploy and swap, back to 32–40% from 17:30Z;
- 97% at 19:15Z and 85% at 19:30Z after the second, with memory peaking at 97%.

So for about 30–45 minutes after each deploy and swap, every call to the BFF is slow, while `/healthz` still answers in about 2 s. A `/healthz` warm-up check would not catch it.

**Entry-points**

- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/E2EAcceptance/E2EValidationRunner.cs:413-457`
- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/E2EAcceptance/H13AcceptanceOptions.cs:50`
- `src/server/api/Sprk.Bff.Api/Infrastructure/Diagnostics/KeylessProbeRunner.cs:29`

**Suggested fix**

- H13: treat a keyless-proof timeout as transient, and retry with backoff over up to about 45 minutes before reporting Inconclusive. Keep the 90 s per-call limit, so a BFF that is really stuck is still reported.
- Stamp sizing: an S1 plan with one worker running both BFF slots saturates for about 30–45 minutes after every deploy. Decide whether stamps need a larger plan, or whether the staging slot should stop after a successful swap. This needs an owner decision; it affects the NFR-04 cost envelope.

**Estimated effort**: 2–3 h with tests.

### ISS-033 — H9 gives up on a slot swap after 10 min while ACME's swap takes 11, then retries into the still-running swap

| Field | Value |
|---|---|
| **Status** | Fixed — PR #2956 merged `c39114e7c6` 2026-11-22 (30-min swap wait; slot guard waits out a swap in flight); verified on ACME (H9 passed on resume) |
| **Urgency** | now (every BFF deploy to a stamp on S1) |
| **Filed** | 2026-11-22 |
| **Source** | ACME upgrade run `3b6f7b65`: H9 failed `scheduled-jobs-slot-guard-failed` (409 "prepared for swap") after `slot-swap-infra-fault` |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2949 |

**Description**

On 2026-11-22 H9 deployed the BFF to `sprk-acme-prod-api` staging (push deploy ended 16:48:58Z) and called the staging → production swap at 16:53:49Z. Azure applied production's settings to staging and started warmup.

`ArmSlotSwapper` waits `SlotSwapTimeout` (10 min), then reports `slot-swap-infra-fault`. The run auto-retried H9 at about 17:03:51Z, while the swap was still running. H9's first step (the slot-sticky `Scheduling__RunScheduledJobs=false` write on staging) got 409 "The slot cannot be changed because its configuration settings have been prepared for swap". The run failed.

The swap completed at 17:04:53Z (about 11 min; the BFF needs about 220 s to start on S1, T186 L48). Production then served H9's deployment, and both slots were healthy. So H9's work had succeeded, yet the run recorded it as failed. A resume reruns the deploy and swap, and hits the same 10-minute ceiling.

Same shape as ISS-032 (H6): a fixed wait shorter than the Azure operation, then a retry that collides with the operation still in flight.

**Entry-points**

- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BffDeploy/BffDeployOptions.cs:84` (`SlotSwapTimeout` = 10 min; its doc says swaps take 30–90 s)
- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BffDeploy/ArmSlotSwapper.cs:94-130`
- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/BffDeploy/ArmSlotStickyAppSettingWriter.cs` (the 409 on the retry)

**Suggested fix**

On a swap timeout, do not report a failure while Azure is still swapping. Poll the site's `slotSwapStatus.timestampUtc` (and the activity log) until the swap completes or fails, within a ceiling that covers `WEBSITES_CONTAINER_START_TIME_LIMIT` plus warmup (≥ 30 min).

At the start of H9, if a swap is in flight on the site (the staging config is "prepared for swap"), wait for it instead of writing to the slot.

When the swap completes with production healthy and serving the expected deployment, H9 succeeds without redeploying.

**Estimated effort**: 3–5 h with tests
**Blockers**: none
**Related**: ISS-032; T186 L46/L48

### ISS-031 — No upgrade run can start: CreateRun (REG-07) refuses a Ready registry row and nothing moves it back

| Field | Value |
|---|---|
| **Status** | Fixed by PR #3017 (merged `7dc36ac0f5`, 2026-11-23): admitted with intake `upgradeOfProvisionedOn` = the row's `sprk_provisionedon`, row left as it is (no InProgress move); skill sends it. Review found sending `provisionedOn` would break H0 → ISS-038. dev L2 API deployed from master `7dc36ac0f5` and swapped to production 2026-11-23 (owner OK), healthy |
| **Urgency** | now (every upgrade of an existing stamp) |
| **Filed** | 2026-11-20 |
| **Source** | ACME upgrade to SpaarkeMaster 1.3.2.0: the first `POST /api/runs` returned 400 `registry-setup-status-not-in-progress` |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2926 |

**Description**

`CreateRun` accepts a run only when the customer's `sprk_dataverseenvironment` row has `sprk_setupstatus = InProgress`.
After a stamp's first run, H13 sets it to Ready. The skill's Step 1a detects an upgrade (`sprk_provisionedon` set) and
asks the operator to confirm. But neither the skill nor L2 sets the row back to InProgress, so every upgrade run
fails with 400 before it starts. On 2026-11-20 the owner approved a manual PATCH (2 → 1) for ACME; run `3b6f7b65` then
started.

**Entry-points**

- `src/server/services/Sprk.Provisioning.ControlPlane.Api/Api/RunsEndpoints.cs:686-698` (REG-07 setupStatus check)
- `.claude/skills/provision-environment/SKILL.md` Step 1a, the upgrade branch (`$script:IsUpgradeRun = $true`), around line 739

**Suggested fix**

Pick one owner for the transition. Either Step 1a's upgrade branch PATCHes the row to InProgress after the operator
confirms the upgrade (registry write, like Step 1f), or CreateRun accepts Ready when the row has `sprk_provisionedon`
and the intake carries an upgrade acknowledgement, and moves the row itself. The second keeps the rule in one place
on the server.

**Estimated effort**: 2–4 h with tests
**Blockers**: none
**Related**: REG-07 (pre-dispatch punch list 2026-08-27); ISS-030; `notes/acme-1.3.2.0-upgrade-plan.md`

### ISS-030 — PRQ-C-03 (global name availability) fails on every upgrade run

| Field | Value |
|---|---|
| **Status** | Fixed by PR #3015 (merged `22c42efad6`, 2026-11-23): an unavailable name that exists in the stamp RG passes as OWNED (H2a rule); no deploy needed (skill / prereq files only) |
| **Urgency** | next-round |
| **Filed** | 2026-11-20 |
| **Source** | ACME upgrade prechecks (skill Step 1e-ter), 2026-11-20 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2925 |

**Description**

PRQ-C-03 calls `checkNameAvailability` / `checkDomainAvailability`, which report every existing name as taken. On an
upgrade run the stamp's Service Bus namespace, storage account and Cognitive Services subdomains already exist in the
customer's own resource group, so the check fails. ACME failed it for `spaarke-acme-prod-sbus`, `sprkacmeprodsa`,
`sprk-acme-prod-docintel` and `sprk-acme-prod-contentsafety`, all four in `rg-spaarke-acme-prod`. The operator has to
recognise and override a hard-stop check. H2a had the same defect and was fixed on 2026-10-15 (T186 L23); the
precheck recipe was not.

**Entry-points**

- `scripts/provisioning-prereqs/prereqs.yaml:800-829` (PRQ-C-03 recipe; its `expect` already says "run this before the first attempt only")
- `src/server/services/Sprk.Provisioning.ControlPlane.Core/.../ArmResourceNameAvailabilityProbe.cs` (H2a's fix to copy)

**Suggested fix**

When a name is unavailable, GET it at its expected ID in `rg-spaarke-{customerId}-{stampEnvironment}` on the stamp
subscription: 200 means it is the stamp's own (pass), 404 means a real collision (fail), anything else fails with the
error. This matches H2a.

**Estimated effort**: 1–2 h
**Blockers**: none
**Related**: T186 L23; ISS-031

### ISS-024 — H14m (customer shared mailbox) needs two Exchange roles the owner has not approved yet — CLOSED 2026-10-13

| Field | Value |
|---|---|
| **Status** | **Closed 2026-10-13.** D32 approved 2026-10-10 and applied 2026-10-13 (M1 PASS). Live checks as the app (Worker UAMI → MI-FIC → Spaarke Exchange Admin) the same day: M2, M3, M4a/b, M5, M7 PASS; M6 not run (optional). Results: `notes/t263-customer-shared-mailbox.md` §5. The full ensure route (M4 `Success` → `AlreadyCompliant`, `Test-ServicePrincipalAuthorization` for the 3 roles) is proven by T186 at H14m |
| **Filed** | 2026-10-10 (T263; number provisional — the main session assigns ISS numbers) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1562 (the mail-model issue; T263 implements it) |

T263 built H14m: `sprk-{customerId}-mail` is created as a shared mailbox, added to `Spaarke-AppAccess-{customerId}`,
authorization-tested, and recorded as the stamp's verified `sprk_communicationaccount` row. H13 re-checks all of it.
`Spaarke Exchange Admin` holds only `Spaarke App RBAC Admin` plus `View-Only Recipients`, so it cannot run `New-Mailbox`
or `Add-DistributionGroupMember`. Widening it is an owner decision (D25; `provisioning.md`).

The sidecar detects the missing cmdlets before any write, so every run fails Resumable at H14m with
`h14m-mailbox-ensure-failed` ("PRQ-E-16 … not applied") and creates nothing.

**Needs**:
1. The owner approves D32: the two custom roles plus one scope in `notes/t263-customer-shared-mailbox.md` §5, with
   the exact cmdlets and parameters.
2. An Organization Management admin applies them (deployment guide §4.2.1 step 5).
3. Run live checks M1–M7, notably M5: whether `Get-Recipient -Filter "Members -eq '<DN>'"` works. If it does not, use
   the one-line fallback in the note.

Until then, merge T263 only together with the approval, or accept that T186 stops at H14m.

### ISS-020 — H7b leaves a new stamp unable to read standing grants (and did not check the task-154 role split)

| Field | Value |
|---|---|
| **Status** | Resolved 2026-10-09 — H7b `secure-setup-procedure=4`, steps S19–S21 (this branch's commit; tests in `SecureRecordSetupProcedureTests`) |
| **Urgency** | before T186 |
| **Filed** | 2026-10-09 |
| **Source** | `notes/coordination/2026-10-09-from-auth-system-of-record-r1-webhooks.md` §3 (M-17) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1565 |

**Description**

`contact.sprk_standinggrant` is field-secured. Without Read the platform silently strips it from the BFF's app-only
retrieve, so `SubjectStandingGrantReader.cs:171-183` fails closed: every contact reads as "no standing grant" (a WARNING
per read, no error). The documented fix (teams-app-r1 `notes/050-standing-grant-field-schema.md` §5.4; UAC-r2
`spa-external-access-model-briefing.md:182-186`) is membership of the BFF application user in the `Standing Grant
Administrators` profile, which SpaarkeMaster ships (`Other/FieldSecurityProfiles.xml:675`) with no members. H7b had no step
for it (`SecureRecordSetupProcedure.cs` ended at S18). The note's claim about "Access Administrator / Core User privilege
edits" is only half right: both roles' final state ships in SpaarkeMaster (`Roles/Spaarke Access Administrator.xml` holds
Read/Write/Create/Append/AppendTo on `sprk_noaccessentry` at Global; `Roles/Spaarke Core User.xml` holds none), so a stamp
imports the right state and nothing needs editing; the script `Set-NoAccessEntryRolePrivileges.ps1` repairs older
environments.

**Resolution (evidence)**

S19 resolves the profile (exactly one) and verifies the column is secured and the profile grants Read; S20 adds the BFF's
two application users as members (read first, only the missing ones, dry-run aware, other members never touched); S21
verifies (never repairs) the role split and refuses `secure_setup.no_access_entry_roles_incomplete` naming the repair
script; S9 verification re-reads the S20 membership. `ProcedureVersion` 3 to 4 so completed runs re-run. Tests: 11 new
refusal rows plus 5 facts (profile gains users and keeps a foreign administrator, partial membership, pre-S19 environment
with dry run, membership that does not stick, role split never repaired). Note: stamp BFF users are System
Administrator (H10), which may already carry the platform's System Administrator FLS profile; membership is still the
documented mechanism and is harmless, so it is added regardless. A live check that a stamp reads a `true` standing grant
remains part of T186.

### ISS-021 — The T7 Spaarke-tenant guard was a silent no-op when `AzureAd__TenantId` was a Key Vault reference

| Field | Value |
|---|---|
| **Status** | Resolved 2026-10-09 — `CustomerIdentityT7Probe.CheckWorkforceTenants` fails closed (this branch's commit) |
| **Urgency** | before T186 |
| **Filed** | 2026-10-09 |
| **Source** | coordination note §3 (L-10) |
| **GitHub Issue** | (none; under #1560-family note) |

**Description**

`CustomerIdentityT7Probe.cs:262-269` guarded a Model 1 workforce list against Spaarke's own tenant only when the slot's
`AzureAd__TenantId` parsed as a GUID (`Guid.TryParse` false for `@Microsoft.KeyVault(...)`), so a reference, blank, zero
or missing value skipped the guard and H13 passed.

**Resolution (evidence)**

H4b writes `AzureAd__TenantId` as a plain GUID (the manifest's per-env entry overrides the TenantId secret's reference), and
ARM app settings cannot resolve a reference without Key Vault data-plane access the probe does not have, so resolving is
the wrong fix. On Model 1 an unreadable value (reference, blank, all-zero, missing, or case-variant ambiguous) now returns
`[UNRESOLVABLE ...]` and the probe Fails with an instruction to re-run H4b. Model 2 is unchanged. Tests: 5 new Model 1 cases
plus a Model 2 reference case in `CustomerIdentityT7ProbeTests`.

---

### ISS-019 — H14b and H14c wire webhooks to routes the BFF does not serve (a run would stop at H14b)

| Field | Value |
|---|---|
| **Status** | Resolved 2026-10-09 — H14b and H14c removed from L2 (owner directive "needed -> build, else remove"); H14a kept |
| **Urgency** | before the first run reaches H14 |
| **Filed** | 2026-10-09 (incoming note from auth-system-of-record-r1 via external-access-r3, `notes/coordination/2026-10-09-from-auth-system-of-record-r1-webhooks.md` §1) |
| **Source** | auth-system-of-record-r1 `x09a-mail-and-webhooks.md` Q2-Q3 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1560 |

**Description**

H14b registered Graph subscriptions at `{stamp}/api/webhooks/graph/{module}` and H14c a Dataverse `serviceendpoint` at
`{stamp}/api/webhooks/dataverse/communication`. Verified in code on this branch (every claim re-checked, none taken from the note):

| Claim | Result | Evidence |
|---|---|---|
| H14b notificationUrl is `{base}/api/webhooks/graph/{module}`, `clientState` = the `Communication-Webhook-SigningKey` secret | True | `H14bGraphWebhookSubHandler.cs:55,159-163,196-197` (before this change) |
| H14c endpoint URL is `{base}/api/webhooks/dataverse/communication` | True | `H14IntegrationWiringHandler.cs:290`, `H14cDataverseWebhookSubHandler.cs:45,135-139` (before this change) |
| The BFF maps neither route | True | the only webhook routes in `src/server/api/Sprk.Bff.Api` are `POST /api/communications/incoming-webhook` (`Api/CommunicationEndpoints.cs:470`) and `POST /api/compose/webhooks/spe-doc-changed` (`Api/ComposeSyncEndpoints.cs:43`); no `/api/webhooks/*` route exists |
| H14c registers no `sdkmessageprocessingstep` | True | the registrar wrote only the `serviceendpoints` row (`DataverseWebApiServiceEndpointWebhookRegistrar.cs`, before this change); `sdkmessageprocessingstep` occurs in L2 only as a privilege-name string in `Tests/Handlers/SecureRecordSetup/FakeSecureRecordSetupDataverse.cs:25-26` |
| The BFF creates/renews its own per-mailbox subscriptions with `customer.bicep`'s URL | True | `Services/Communication/GraphSubscriptionManager.cs:58,431-434` use `CommunicationOptions.WebhookNotificationUrl`; `infrastructure/bicep/customer.bicep:793` sets `Communication-WebhookUrl` = `{bffApi appServiceUrl}/api/communications/incoming-webhook` |

A Graph create handshake to a 404 route fails, H14b turns any failure into `HandlerResult.Failure`, so a run stopped at H14b.
Mail was never at risk: the BFF subscribes itself and polls every 5 minutes.

**Resolution (2026-10-09)**

Removed, not rewired (nothing needs a provisioning-time subscription; the BFF owns subscribe/renew/self-heal):
- Deleted `H14bGraphWebhookSubHandler`, `H14cDataverseWebhookSubHandler`, `IGraphSubscriptionCreator` + `GraphRestSubscriptionCreator`,
  `IServiceEndpointWebhookRegistrar` + `DataverseWebApiServiceEndpointWebhookRegistrar`, their tests and `H14SecretRedactionTests`.
- `H14IntegrationWiringHandler` now drives H14a only (`ExpectedSubStepCount` 3 -> 1); it no longer requires `BffApiUrl`, the customer
  `keyVaultName`, `dataverseEnvUrl` or `subscriptionId`. Removed `HandlerIds.H14b/H14c`, the options only they used
  (`GraphRequestTimeout`, `GraphSubscriptionExpirationMinutes`, `DataverseRequestTimeout`, `ServiceEndpoint*`), the rejection codes
  (`H14bRejections`, `H14cRejections`, four H14 parent codes) and the gates `GraphWebhooksWired` / `DataverseWebhookWired`.
- DAG: `H14 <- H12c` (the `H9` edge existed only for the webhook base URL; H9 still precedes H14 transitively via H11 <- H7 <- H9).
- Intake: removed `communicationGraphResource` / `emailGraphResource` (catalog, `HandlerRunInputs`, POST /api/runs rule
  `h14b-no-webhook-targets-configured`, `intake.schema.json`, `provisioning-runs/_templates/intake.md`, load-test payloads).
- The Worker's `Mail.Read` Graph role existed only for H14b: removed from `ControlPlaneGraphAppRoles.cs` and the T261 note. **Live revocation
  of `Mail.Read` from the Worker identity is an operator step (not done here).**
- Handler count: the dispatchable count stays **21** (H14b/H14c were never in `HandlerIds.Dispatchable`); the in-process H14 sub-steps
  went from 3 (H14a/b/c) to 1 (H14a); handler ids in the catalog 24 -> 22. No Bicep or app setting existed only for H14b/H14c.
- Signing key: a stamp still needs one - the BFF's `Communication:WebhookSigningKey` is `[Required]` and its mapped receiver verifies
  `X-Hub-Signature-256` with it. The BFF reads config key `Communication:WebhookSigningKey` (app setting `Communication__WebhookSigningKey`),
  not a Key Vault name; on a stamp that setting is a Key Vault reference to `Communication-Webhook-SigningKey`, which is what
  `scripts/canonical-secret-catalog/manifest.yaml`, `customer.bicep` and H4 already use, so the manifest needed no change. The other
  two spellings are outside provisioning: `Communication-WebhookSigningKey` (dev platform, `config/spaarke-resources.yaml:349`) and
  the BFF's own error-message text (`communication-webhook-signing-key`, `CommunicationOptions.cs:65`).
- Not fixed here (BFF owner / other issues): #1561 (the mapped receiver 401s without `X-Hub-Signature-256`, which Graph never sends),
  #1562 (nothing creates the stamp's `sprk_communicationaccount` rows).

**Entry-points**

`src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/IntegrationWiring/H14IntegrationWiringHandler.cs`, `.../Reconciler/DagAdvancer.cs`,
`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §5, §7.9.

### ISS-014 — H13 cannot run the BFF's secure-record isolation census (no identity can call it)

| Field | Value |
|---|---|
| **Status** | Resolved 2026-10-09 — task 260 (owner approved the recommended fix 2026-10-09): H13 requires the stamp BFF's census to answer `isolated` |
| **Urgency** | before T186 Ready (the census is run by hand until then) |
| **Filed** | 2026-10-09 (T259) |
| **Source** | T259 step 5 (ISS-010's H13 half) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1527 |

**Description**

The census exists only as a BFF scheduled job: `SecureRecordIsolationCensusJob` (`secure-record-isolation-census`, every
15 min, read-only; its run's `ResultJson.status` is `isolated` | `findings` | `inert` | `error`). It is reachable only
through the generic admin routes `POST /api/admin/jobs/{jobId}/trigger` (202, async) and `GET /api/admin/jobs/{jobId}/status`
(`recentRuns[].resultJson`), both behind `RequireAuthorization("SystemAdmin")` — an `Admin` or `SystemAdmin` app role
(`AuthorizationModule`). A stamp's BFF app registration (H3) defines exactly one app role, `Provisioning.KeylessProof`,
assigned to the L2 Worker identity only; it defines no `Admin`/`SystemAdmin` role. So H13, signing in as the L2 Worker,
gets 403, and no run can prove isolation before Ready — the owner's 2026-10-09 goal ("a run cannot reach Ready unless the
census says isolated") is unmet. Granting L2 an `Admin` role would be far wider than needed: the same policy guards job
enable/disable and trigger of every job, RAG index writes/deletes, membership admin and record-matching admin.

**Suggested fix (one recommendation)**

A read-only, synchronous BFF route beside the keyless proof: `POST /api/platform/secure-record-isolation-census`, behind
the existing `KeylessProofAuthorizationFilter` (app-only token of this tenant for this API holding the L2-only
`Provisioning.KeylessProof` role — no new role, no H3 change), returning `{status, verdict, findings}` from the SAME code
the job runs (move the job's census read + `SecureBuRoleDepthAssertion.Evaluate` into one shared method; the job keeps its
schedule). L2: H13 calls it with the keyless proof's token acquisition and fails unless `status == "isolated"` (new code,
e.g. `h13-secure-isolation-not-isolated`, carrying the findings). §11: existing = the job + admin routes (async, admin-only;
widening them to L2 grants every job's controls); cost of doing nothing = H13 cannot gate Ready on isolation. Needs the
owner's OK (BFF surface + authorization, CLAUDE.md §6/§10) and a BFF publish-size check.

**Resolution (task 260, 2026-10-09)**

Built as recommended — no new app role, no H3 change, no new package.
- BFF: `POST /api/platform/secure-record-isolation-census` in `Api/Platform/KeylessProofEndpoints.cs`, behind
  `RequireAuthorization` + the existing `AddKeylessProofAuthorizationFilter` (app-only token of this tenant, this API's
  audience, `Provisioning.KeylessProof`) + the `job-submission` rate limit. Synchronous, read-only, bounded at 60 s; a read
  failure or timeout answers 200 `status=error` (no exception text). Body = the job's result: `{status, verdict,
  findings[{verdict, message}]}`.
- One census: the job's read + grading moved into `Services/ExternalAccess/SecureRecordIsolationCensus.cs` (static —
  no DI registration); the job calls it and keeps its schedule, CRITICAL lines, heartbeat, throw-on-read-failure and
  `ResultJson`. A test pins that the job and the route agree (status, verdict, findings) on isolated / findings / inert.
- Contract: route + the four statuses in `KeylessProofContract.SecureRecordIsolationCensus` (source-linked into both).
- L2: `IE2EValidationRunner.RunSecureIsolationCensusAsync` — the keyless proof's call path extracted into
  `PostAsL2IdentityAsync` and shared (same credential, `api://{BffAppRegId}/.default`, https only, role-less-token
  diagnostic, one transient retry, 401/403/500 fail, 404 inconclusive). H13 step 7b calls it after the existing checks:
  `isolated` → gate `h13-secure-isolation` Verified; `findings` → QuarantineRequired `h13-secure-isolation-not-isolated`
  (findings in the diagnostic, ≤ 20, sanitised); `inert` → QuarantineRequired `h13-secure-isolation-inert`; a failed
  call or an answer this build cannot read → QuarantineRequired `h13-secure-isolation-census-failed`; `error` /
  transport / timeout / 404 / a throw → Resumable `h13-secure-isolation-inconclusive`.
- `inert` on a fresh stamp: the census grades the security TOPOLOGY (role depth into the Secure Record unit, users in it,
  the owner team and role), not records, so a new environment with no secure record yet is graded in full and can be
  `isolated`. `inert` means the BFF finds no Secure Record unit at all; H7b creates it before H13 (H13 ← H7b), so inert
  proves nothing and is a broken stamp (or a BFF `SecureRecord:BusinessUnitName` that differs from H7b's) — it fails.
- Tests: BFF route contract (401 / 403 ×3 / 200 shape / error without exception text / reads only), job↔route parity;
  L2 runner (13 census cases) and H13 (AC-28..35). The deployment guide's "run the census by hand" interim text is
  replaced by the H13 rule (§7.10, §7.11).

**Rollout:** deploy the BFF build carrying task 260 before (or with) the Worker build: a Worker that calls an older BFF
gets 404 → Resumable `h13-secure-isolation-inconclusive` (redeploy the BFF via H9, then resume). Live proof owed at T186
(the first H13 run against a real stamp).

---

### ISS-015 — H6, H7 and H7b cannot sign in as the customer BFF app registration under either Worker credential chain

| Field | Value |
|---|---|
| **Status** | Resolved 2026-10-09 — H3 keeps a second FIC `spaarke-l2-worker` (subject = L2 Worker UAMI principalId) on every Spaarke-tenant customer BFF registration; branch `worktree-agent-ae886473c4b5b7444` (merged to the work branch by the main session) |
| **Urgency** | now — blocks T186 at H6 (first live run) |
| **Filed** | 2026-10-09 (T252; ISS-014 left free for a parallel agent) |
| **Source** | T252 step 1 inventory of the dev control plane's Worker credential chain |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1524 |

**Description**

H6 (solution import), H7 (environment-variable values) and H7b (Secure Record setup) sign in to the customer's
Dataverse environment as `InterStepState.BffAppRegId` — the per-customer BFF app registration H3 creates (D-13). The
Worker builds that credential from the FR-39 chain (`WorkerDataverseCredentialFactory.Create`):

- **Secret-free chain `[ManagedIdentityFederated]`** (T252 makes it the default and dev's setting): the Worker presents
  its own UAMI (`sprk-controlplane-{env}-uami`) as a federated assertion for that app. But H3 gives the app exactly one
  federated credential, whose subject is the **stamp BFF's** UAMI (`GraphAppRegistrationProvisioner`, FIC recipe:
  `Subject = request.UamiPrincipalId`). Nothing trusts the L2 Worker UAMI, so Entra refuses the assertion
  (AADSTS70021 / 700213: no matching federated identity record) at H6's first token request.
- **Legacy chain `[ClientSecret]`** (dev until T252): the secret slot holds `BFF-API-ClientSecret` from the platform
  vault — on dev the sentinel `pending-oob-population`, and in any case the secret of the old shared BFF app
  (`1e40baad`), not of the per-customer app. It cannot authenticate as the per-customer app (AADSTS7000215), and the
  binding rule forbids creating a real one.

So no configured chain can complete H6/H7/H7b on a Model 1 run today. Unit tests do not catch it: they stop at
credential construction, and the boot tests deliberately do not exchange tokens (`WorkerSecretFreeBootTests` header).
H3's adoption check also refuses an existing app carrying a federated credential it did not create (`foreignFics`), so a
hand-added Worker FIC would block re-runs.

**Suggested fix**

H3 adds a second federated credential on every per-customer BFF app registration, subject = the L2 Worker UAMI
(`ControlPlaneIdentityOptions.PrincipalObjectId`, already a validated Worker option), issuer = Spaarke's tenant (Model 1),
audience `api://AzureADTokenExchange`; triple-idempotent like the first; the adoption check accepts exactly these two
names. Then H6's first live call (T186) is the exchange proof. Owner check before building: confirm the per-customer
app should trust L2's identity (it already holds Dataverse System Administrator through H10's app user, and L2 already
owns the app it created), versus a different sign-in identity for H6/H7/H7b. Needed → build, else remove.

**Entry-points**

- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/EntraAppReg/GraphAppRegistrationProvisioner.cs` (FIC recipe ~L1100–1215; adoption check ~L410–440)
- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/Credentials/WorkerDataverseCredentialFactory.cs`
- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/{SolutionImport/H6SolutionImportHandler.cs, EnvVarValues/H7DataverseEnvVarValuesHandler.cs, SecureRecordSetup/H7bSecureRecordSetupHandler.cs}`

**Resolution (2026-10-09)**

Built as suggested — a gap against D-13, not a design change. `GraphAppRegistrationProvisioner`:
- `BuildRequiredFicSpecs` (pure) plans two credentials before any Graph write: `spaarke-uami-trust` (stamp BFF UAMI,
  unchanged) and `EntraAppRegOptions:WorkerFicName` = `spaarke-l2-worker` — issuer
  `https://login.microsoftonline.com/{SpaarkeTenantId}/v2.0`, subject = `ControlPlaneIdentityOptions.PrincipalObjectId`
  canonicalised (the Worker UAMI's **object id**, the value `WorkerDataverseCredentialFactory`'s MI assertion carries as
  `sub`; never its clientId), audience `api://AzureADTokenExchange`. Refused before any write
  (`appreg-worker-fic-identity-missing`) when the Worker principal id is blank / not a GUID / empty, equals the stamp UAMI,
  or the two names are blank or equal. `customer-owned-model2`: stamp credential only (cross-tenant; Model 2 out of scope).
- `PlanFederatedCredentials` (pure) reconciles both by triple (SF-7) — create, no-op re-run, delete-then-recreate a
  drifted or misnamed one (a stamp triple under the worker name is moved, never dropped) — and the re-GET verification
  runs the same planner. The adoption check accepts exactly the planned names.
- No new setting: `ControlPlaneIdentity__PrincipalObjectId` (Worker, ValidateOnStart; `platform-controlplane.bicep`
  passes `uami.outputs.principalId` — the same UAMI whose clientId is the Worker's `ManagedIdentity__ClientId`).
- Tests: `WorkerFicTrustTests` (24 cases) + `AppRegistrationAdoptionTests` (+3).

**Rollout:** deploy the Worker; then, for a stamp whose H3 already ran (e.g. the T186 run), resume H3 (it adds the
credential to the adopted registration) before H6. The exchange proof is H6's first token request (T186); a fresh FIC
flaps with AADSTS70025 for ~2 minutes (auth.md). Both FICs stay for the registration's lifetime (2 of Entra's 20).

---

### ISS-010 — Production business-unit topology: the customer's own unit, the BFF application users and guests in it (INCOMING-145 §6 T1/T3/T5)

| Field | Value |
|---|---|
| **Status** | Resolved 2026-10-09 — task 259 (owner decision 2026-10-09): T1/T3/T5 built and H7b checks T1/T3; the H13 census step is split out as **ISS-014** (H13 cannot call the census with the stamp's identity) |
| **Urgency** | before T186 (server-side creates of secure children; #1081) |
| **Filed** | 2026-10-08 (T256) |
| **Source** | unified-access-control-r2 INCOMING-145 §6 (owner 2026-10-02, binding) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1486 |

**Description**

§6 makes the production topology binding: users and the BFF's application users sit in the customer's NAMED child
business unit, never in the root; the Secure Record unit is a sibling of it under the root. T256's H7b enforces the two
parts it owns — **T2** (it creates the Secure Record unit under the root and refuses, quarantined, one under any other
parent) and **T4** (it refuses, quarantined, a root default team holding Deep/Global Read on a codified table). Not
built, because each needs a decision or an input that does not exist:
- **T1** the customer unit — its name "comes from the run", but no intake key carries a customer display name
  (`IntakeParameterCatalog` has none; the schema's `displayName` never reaches the run).
- **T3** `DataverseWebApiAppUserCreator` (H10) creates both application users in the ROOT unit. Server-side creates then
  fall back to the root default team, which holds no privileges, and Dataverse refuses it as owner (#1081). H10 runs
  before H6/H7b and its users must keep System Administrator (H6/H7/H7b sign in as the BFF app user), so moving them later
  (a business-unit change removes a user's roles) is not a safe repair.
- **T5** H11 makes each guest a Dataverse user — today in the ROOT unit, with `Spaarke Basic User`
  (`H11UserProvisioningOptions.DefaultGuestSecurityRoleName`), which ships holding `prvReadsprk_Project`,
  `prvReadsprk_Matter` and `prvReadsprk_WorkAssignment` at **Deep** (`SpaarkeMaster/Roles/Spaarke Basic User.xml`).
  Deep at the root reaches every child unit, the Secure Record unit included: **on a provisioned environment every
  guest reads every secure project, matter and work assignment by depth** (NFR-05 clause 1 — exactly §6's "why it
  matters"). Nothing in the pipeline catches it: H7b runs before H11 (T4 checks only the root default team), and H13
  does not run the BFF's isolation census (INCOMING-145 §3 leaves that to "H13 or the operator"). A real-path
  cross-record exposure (F1) on the first live run (T186) unless the operator census (`secure-record-isolation-census`)
  is run and acted on.

**Suggested fix (one recommendation)**

Add intake `customerDisplayName` (required, validated at POST /api/runs); **H10** creates the customer unit (T1) under the
root before it creates the two application users IN it with System Administrator (T3), and records the unit id
(`InterStepState.CustomerBusinessUnitId`, `[ProducedBy(H10)]`); **H11** creates guests in that unit (T5); H7b then also
checks T1/T3 (unit present, application users' `businessunitid`); and **H13** triggers the BFF's read-only
`secure-record-isolation-census` and requires `isolated` (INCOMING-145 §3), so no run reaches `Ready` with isolation
void. Needs the owner's OK on the placement and the new key. Until then, T186's runbook must run the census by hand
after H11 and treat any human reaching the Secure Record unit as a stop.

**Resolution (task 259, 2026-10-09)**

Owner decision 2026-10-09 (binding): "for secure records, only users (systemusers, guest systemusers or contact users)
explicitly granted access should have access; guest users are added to the root customer business unit NOT added to the
secure business unit (no users are added to the secure business unit)." The "root customer business unit" is the
customer's own unit directly under the Dataverse root (not the root itself — Deep at the root reaches Secure Record).
- Intake: the existing `displayName` (T237) is carried into the run — required at POST /api/runs
  (`CustomerBusinessUnitIntake`: 1–160 chars, no control character, no leading/trailing whitespace, never `Secure Record`;
  `h10-customer-display-name-required` / `-invalid`). No new key (coordinator correction, §11 reuse).
- H10 (T1/T3): finds or creates the customer unit directly under the root (`h10-customer-bu-wrong-parent` Quarantine,
  `h10-customer-bu-ambiguous`), records `InterStepState.CustomerBusinessUnitId` (`[ProducedBy(H10)]`), creates both App
  Users IN it with the unit's System Administrator copy; an App User elsewhere → Quarantine
  `h10-app-user-in-foreign-business-unit`, never moved.
- H11 (T5): resolves the guest roles in the customer unit; moves a guest Dataverse added to the ROOT into the customer
  unit (PATCH + read-back) before any role; a guest in any other unit → Quarantine `userprov-guest-in-foreign-business-unit`.
- H7b: `secure_setup.customer_bu_missing` / `secure_setup.customer_bu_wrong_parent` / `secure_setup.app_user_outside_customer_bu`
  (all Quarantine); S2 already refuses any user in the Secure Record unit. Procedure version 3.
- H7 (found in review): links the customer unit to H8's container beside the root — records are owned in the customer
  unit and the BFF resolves a non-secure record's container from its owning unit only.
- H11 also refuses (Quarantine) a guest holding a role of another unit (`userprov-guest-holds-role-outside-customer-unit`).
- Not done here: H13 requiring the census → ISS-014, resolved by task 260 (H13 now refuses Ready unless the census is
  `isolated`; no hand-run census).

---

### ISS-005 — Deploy-Release Phase 3 imports a 9-solution list that does not exist

| Field | Value |
|---|---|
| **Status** | Resolved 2026-10-08 — task 218f: Deploy-Release Phase 3 imports the CI-published SpaarkeMaster (Import-SpaarkeMasterPackage.ps1, H6 rules, typed by config/environments.json); Deploy-DataverseSolutions.ps1 deleted; first publish 1.2.0.0 (run 37864623352); PR #1365 |
| **Urgency** | before the next release to demo |
| **Filed** | 2026-10-07 (found in T218b) |
| **Source** | T218b — H6 left `Deploy-DataverseSolutions.ps1`; `Deploy-Release.ps1` still calls it |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1401 |

**Description**

`Deploy-Release.ps1` Phase 3 (the `deploy-new-release` skill, Spaarke's own environments) runs
`Deploy-DataverseSolutions.ps1`, whose list names 9 solutions — 6 exist nowhere — and whose zip filter
(`:443`) accepts every zip. A release to demo imports nothing useful or fails at the first missing solution. The legacy
`Provision-Customer.ps1` (step 7, `:915`) calls it too; `Load-DemoSampleData.ps1` and `scripts/README.md` point operators
to it.

**Suggested fix**

Package type per Spaarke environment (`config/environments.json`), then point the script at SpaarkeMaster with an
explicit type and fix the filter — or retire Phase 3 in favour of the CI-published SpaarkeMaster zips (T218d).

### ISS-007 — CI identity trusts the `pull_request` OIDC subject

| Field | Value |
|---|---|
| **Status** | Resolved 2026-10-08 — owner OK; credential deleted (4 remaining: master, environment dev/staging/production); OIDC guide no longer creates it |
| **Urgency** | soon (security exposure; nothing uses the subject) |
| **Filed** | 2026-10-08 (T218d review) |
| **Source** | T218d — the new publish workflow uses the same CI identity |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1446 |

**Description**

`github-actions-spe-infrastructure` (`8c85a481-…`) has federated credential `gh-pull_request`
(`repo:spaarke-dev/spaarke:pull_request`). Unused since task 249. A same-repo pull request that edits a workflow can sign
in to Azure as the app, with all its roles, before review.

**Suggested fix**

Delete the credential; drop `pull_request` from the OIDC guide's setup loop (guide row already corrected).

### ISS-001 — Hand-off owed by UAC-r2: how a new environment gets `sprk_noaccessentry`

| Field | Value |
|---|---|
| **Status** | Resolved — answered and built by T256 (2026-10-08, update below); #1364 closed by unified-access-control-r2 |
| **Urgency** | now (blocks T256 / T218 / T186) |
| **Filed** | 2026-10-07 |
| **Source** | 2026-10-06 conversion review; owner 2026-10-07 asked for an issue + message to UAC-r2 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1364 |

**Description**

An environment without `sprk_noaccessentry` denies every read, so a freshly provisioned environment fails closed and
T186 cannot pass. UAC-r2 documents the table only in its own notes; nothing tells provisioning which solution carries
it, when it must exist, what to seed, or how H13 verifies it.

**Entry-points**

- `projects/unified-access-control-r2/` notes `DEPLOY-CHECKLIST.md`, `batch4-integration-steps.md`; `docs/data-model/INDEX.md`
- This project: T256 (INCOMING-145), T218 (solution package), H13 checks

**Suggested fix**

UAC-r2 answers the four questions in the issue (packaging, ordering, verification, upgrade); provisioning turns the
answer into T256 handler work and T218 package content.

**Update 2026-10-08 (T256).** What provisioning needs is now answered by the repository, and built:
1. *Packaging*: the table ships in SpaarkeMaster 1.2.0.0 with every column the BFF reads (`src/dataverse/solutions/SpaarkeMaster/Entities/sprk_noaccessentry`).
2. *Ordering*: H7b probes `sprk_noaccessentries` with the BFF reader's own `$select` (`NoAccessListReader.RowSelect`,
   pinned by `SecureRecordOwnerRoleSetParityTests`) and refuses Resumable `secure_setup.noaccessentry_missing`; H9 waits
   for H7b, so no BFF is deployed to an environment without it. Nothing is seeded: an empty deny list denies nothing.
3. *Verification*: that probe, on every run.
4. *Upgrades*: SpaarkeMaster upgrades carry it.
Left open only for UAC-r2's confirmation on #1364 (and to hear if the schema changes — we re-export, never hand-edit).
