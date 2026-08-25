# 07 — Workforce-Plane Removal Impact: Teams App, Tier-1, Migration, Blast Radius

> **Status**: Investigation findings, 2026-08-20. Code read of `c:/code_files/spaarke-wt-unified-access-control-r2` (head). Cross-worktree claims are labeled. Code is ground truth; claims cited `path:line`.
> **Question under evaluation**: the operator's decision that the access model splits by SURFACE — MDA = Dataverse-native `systemuser` security; SPA + Teams app = **contact-only**, with the systemuser branch of the workforce plane (`ComposeForSystemUserAsync`) **removed** and internal users covered by an Organization grant.
> **Companion notes**: [01-spa-plane.md](01-spa-plane.md) (runtime mechanics), [06-adversarial-critique.md](06-adversarial-critique.md) (F2/F6 — the over-inclusion findings motivating this decision).

---

## Summary (read this first)

1. **Contact-only as stated breaks the Teams app for internal staff.** The Teams app's primary, live-verified scenario is *"a systemuser opens the Teams tab, signs in via workforce SSO with no second login, and sees exactly their membership records"* — operator-verified live 2026-08-06 (`projects/teams-app-r1/README.md:46`). Teams auth **cannot** be CIAM (ADR-028 A2 MUST NOT: `.claude/adr/ADR-028-spaarke-auth-architecture.md:77`), so Teams users always arrive on the workforce plane; removing the systemuser branch makes every internal user without a contact record **403** (`WorkforcePrincipalResolver.cs:162-170`) and every internal user *with* a contact but no grants see an **empty workspace** (silent, looks like data loss).

2. **The Organization-grant "systemwide solve" cannot reproduce per-user scoping.** An org grant is a per-ROOT-RECORD row (contact empty + org set) unioned for active members of that org (`ExternalParticipationService.cs:445-476, 499-583`). There is no "grant org access to everything" primitive — systemwide coverage means an org-grant row on **every** project/matter/WA, and every internal contact then sees the **same** set. "Sees exactly their membership records" (NFR-08's per-user scope, the Teams graduation criterion) is unreproducible this way.

3. **The deeper blocker: the contact plane structurally cannot see systemuser-anchored assignments.** `ResolveByContactAsync` builds a `PersonIdentity(SystemUserId: Guid.Empty, ContactId)` so only **Contact-typed** lookups can bind (`MembershipResolverService.cs:373-384`), further constrained to the `sprk_assigned*` contact allow-list (`:403-410`). Internal staff's record associations — owner, systemuser-typed assigned fields, teams, BUs — are **invisible** on the contact plane. Reproducing per-user internal scoping contact-only would require re-anchoring every internal assignment to contact lookups: a data-model migration, not a code deletion.

4. **The motivating finding is real and should be fixed — by amendment, not amputation.** `ComposeForSystemUserAsync` feeds RAW ADR-034 membership (no access-conferring allow-list, `AccessibleRecordSetService.cs:192-194` with `options: null`) into an app-only boundary, and `WorkforcePrincipalStrategy` stamps **every** composed project `Collaborate` — including a contact's **grant-derived** projects, silently elevating a ViewOnly grant to Read|Create|Write on the workforce realm (`CallerPrincipalResolver.cs:360, 429-431`). Both are fixable in place (Alternative A below) at a fraction of removal's blast radius.

**Verdict**: the decision is **right in spirit** (grant-governed SPA plane; the raw-membership Collaborate-stamped systemuser branch is over-permissive) but **wrong as stated**. Recommend Alternative A (allow-list the systemuser membership term + honor grant levels), optionally combined with a config gate (Alternative B). Full reasoning in §8/§9.

---

## 1. Workforce-plane consumer map (who authenticates as workforce, where)

### Server: the resolution spine

| Component | File | Delivered by |
|---|---|---|
| `WorkforcePrincipalResolver` (token → systemuser/contact/deny) | `Infrastructure/ExternalAccess/WorkforcePrincipalResolver.cs:59-171` | teams-app-r1 task 020 |
| `AccessibleRecordSetService` (`ComposeForSystemUserAsync` `:184-241`, `ComposeForContactAsync` `:244-301`) | `Infrastructure/ExternalAccess/AccessibleRecordSetService.cs` | teams-app-r1 task 022; systemuser∪own-contact-grants union added by owner directive 2026-08-07 (`projects/spaarke-SPA-external-access-platform-r2/notes/access-model-systemuser-contact-grant-union.md`) |
| `WorkforcePrincipalStrategy` (wraps both; 3× `ComposeAsync` for project/matter/WA; Collaborate stamp `:360`) | `Infrastructure/ExternalAccess/CallerPrincipalResolver.cs:352-451` | teams-app-r1 task 025 (R2 spec FR-22, Option A) |
| Plane selection (`DeterminePlane`: CIAM iff `iss` ~ ciamlogin.com or `tid == Ciam:TenantId`; **else Workforce**) | `CallerPrincipalResolver.cs:234-254` | teams-app-r1 task 025 |
| DI registrations | `Infrastructure/DI/ExternalAccessModule.cs:101` (resolver), `:107` (standing-grant reader), `:118-120` (strategies + resolver), `:255` (accessible-set) | teams-app-r1 / R2 task 015 |

### Routes serving `PrincipalKind == SystemUser` today

All under the **dual-scheme** `/api/v1/external` group (`ExternalAccessEndpoints.cs:54-57`, `ExternalCollaboration` policy = Ciam + workforce default JwtBearer):

- `GET /api/v1/external/me` — project-access context (`ExternalAccessEndpoints.cs:60-71`)
- `GET /api/v1/external/me/entitlements` — Tier-1 modules (`:77-88`)
- `/api/v1/external/projects/*` incl. **document download** `GET .../documents/{documentId}/content` (authz-before-stream, app-only `SpeFileStore.DownloadFileAsync`, `ExternalProjectDataEndpoints.cs:64-238`) and the unscoped `PATCH /todos/{id}` (note 01 §6)
- `/api/v1/external/api/dataverse/*` — the module-host read seam feeding every grid widget (`ExternalAccessEndpoints.cs:100`; descriptors at `ExternalAccessModule.cs:146-246`)

The transitional `/api/v1/collab` workforce group was already removed (R2 task 018, `ExternalAccessEndpoints.cs:21-23`). `AccessibleRecordSetAuthorizationFilter` is **orphaned** — attached to no route (grep: self-references only; note 01 finding 4).

### Clients arriving on the workforce plane

1. **Teams personal app** — silent workforce SSO/NAA, never shows a realm chooser (`src/client/external-spa/src/host/TeamsHostAdapter.ts:213-238` → `acquireTeamsWorkforceBffToken`; `realm.ts:20` "The Teams host does its own silent workforce SSO and never reads this"). Delivered by teams-app-r1 (shipped, PR #723, live-verified).
2. **The browser module-host SPA, "My organization" realm** — the SPA serves BOTH planes from one URL with an explicit chooser (`realm.ts:10-11, 24`: `'workforce' | 'ciam'`; `msal-config.ts:37-47` workforce-multitenant authority). Delivered by SPA-external-access-platform-r2 (ADR-028 A3). So workforce is a first-class **browser** plane too, not just Teams.
3. **Internal management PCF surface** (grant modal on `TrackingFieldTrio`) hits `/api/v1/external-access/*` (workforce default scheme, `ExternalAccessEndpoints.cs:107-141`) — a different group, but note the grant write **requires** a caller systemuser for the `sprk_GrantedBy` audit (`GrantExternalAccessEndpoint.cs:92, 195-206`). Systemuser identity remains structurally required for admin actions regardless of this decision.

### Governing ADR

ADR-028 **Amendment A2** is a MUST: *"MUST resolve the workforce-authenticated caller to a principal — a `systemuser` (→ ADR-034 membership) or, for a non-systemuser, a `contact` …"* (`.claude/adr/ADR-028-spaarke-auth-architecture.md:73`). Removing the systemuser branch contradicts A2 as written → §6.5 **Path B amendment required**, not a silent change.

---

## 2. Teams app assessment (the sensitive one)

**Audience**: BOTH internal staff and (via contact-only workforce fallback) enterprise-customer users. The project's problem statement is explicit: *"Internal staff and enterprise customers expect that same directed collaboration surface inside Microsoft Teams, using their workforce identity … governed by the records they are members of, not by ad-hoc grants"* (`projects/teams-app-r1/README.md:36`). It is **not** an external-partner app; external partners use the CIAM SPA.

**Authentication**: workforce Entra SSO/NAA, multitenant app `1e40baad-…`; CIAM in Teams is an ADR-028 A2 **MUST NOT** (`ADR-028:70, 77`). So under ANY variant of the decision, Teams tokens keep hitting `DeterminePlane` → Workforce → `WorkforcePrincipalResolver`.

**Does the Teams app depend on the systemuser branch to function?** For its verified primary scenario, **yes**:

- Graduation criterion 1 (operator-verified live in Teams web, 2026-08-06): systemuser → workforce SSO → *sees exactly their membership records* (`README.md:46`). That record set comes from `ComposeForSystemUserAsync` → `_membership.ResolveAsync(systemUserId, …)` (`AccessibleRecordSetService.cs:192-194`). Remove it and:
  - internal user **without** a contact record → resolver branch (c) explicit deny 403 `sdap.access.deny.principal_not_resolved` (`WorkforcePrincipalResolver.cs:162-170`) — Teams tab errors (**breaks loudly**);
  - internal user **with** a contact → `ContactOnly` → grants ∪ standing-grant contact membership (`AccessibleRecordSetService.cs:244-301`), which for a typical internal user (no grants, no flag, no contact-typed assignments) is **empty** → workspace renders 0 records (**silently empty** — the worse failure, it looks like data loss).
- Criterion 2 (contact-only workforce user sees contact-anchored membership) survives — it *is* the contact plane.

**Conclusion**: contact-only does not merely degrade the Teams app; it inverts its verified core scenario for exactly the population it was built for, unless every internal user is first provisioned a contact + grants/org-membership (see §4 for why that still can't reproduce per-user scoping).

---

## 3. Tier-1 entitlement coupling

**Salvageable — with one hard constraint.** `ModuleEntitlementResolver.ResolveAsync` branches on **`principal.Plane`**, not `PrincipalKind`:

- `Plane == CiamContact` → blanket outside-counsel set (`ModuleEntitlementResolver.cs:94-98`, set = `{ "assigned-work" }` `:44`);
- otherwise (Workforce plane) → Entra **App-Role claims from the token** ∩ active `sprk_approlemodulemap` (`:100-115`, extraction `:142-153`, map read `:161-233`).

A workforce-authenticated internal user who resolves to a **ContactOnly principal still keeps App-Role entitlements**, because the roles come from the workforce token, not from the principal kind. So Tier-1 survives contact-only **iff the auth plane stays workforce**.

The failure mode to avoid: if "contact-only" were misread as "internal users authenticate as CIAM contacts", Tier-1 internal modules die — CIAM tokens are handled by the blanket branch, roles are never read (`:94-98`), and internal users would be entitled to the outside-counsel set only. Teams can't do CIAM anyway (A2 MUST NOT), so the only coherent reading is *workforce auth + contact principal* — which keeps Tier-1 intact.

One residual: no App-Roles on the token → 0 internal modules (fail-closed, `:104-108`). That behavior is unchanged by this decision.

---

## 4. Migration feasibility (contact-only for internal users)

**(a) systemuser→contact link — exists, three mechanisms:**
- Primary: `systemuser.sprk_primarycontact` derivation via `IIdentityNormalizationService.ResolveAsync` (`WorkforcePrincipalResolver.cs:115-124`).
- oid cross-ref: `contact.azureactivedirectoryobjectid == oid` (`IdentityNormalizationService.cs:285-312`).
- Verified-email fallback: `contact.emailaddress1` match (`:264-278`).
- teams-app-r1 lists `sprk_primarycontact` linkage for internal systemusers as an **admin prerequisite "assumed complete 2026-08-03"** on dev, with go-live verification a checklist item (`projects/teams-app-r1/README.md:85-88`). Production-population state is unverified.

**(b) Provisioning path**: none for internal contacts. `CiamUserProvisioningService` provisions **CIAM external accounts** (`ExternalAccessModule.cs:263-267`), not internal contact records. Headcount needing contacts = live-data unknown; there is no automation to create them.

**(c) Would an internal Entra token resolve to a contact?** Mechanically **yes** — `TryResolveContactByWorkforceIdentityAsync` matches any workforce oid against `contact.azureactivedirectoryobjectid`, then verified email against `emailaddress1` (`IdentityNormalizationService.cs:242-281`); nothing in it is external-specific. But it is only **reached** when systemuser resolution fails (systemuser-first order, `WorkforcePrincipalResolver.cs:108-146`), so contact-only requires removing/reordering branch (a) — the exact removal under evaluation.

**(d) Does the Organization-grant path cover an internal org?** Built for external firms, and **per-record**:
- An org grant = one `sprk_externalrecordaccess` row per ROOT record with `sprk_Contact` empty + `sprk_Organization` set (`task-073-org-grant-design.md:94-97`); membership via the `sprk_contactorganization` junction, `statecode eq 0` (`ExternalParticipationService.cs:540-583`).
- Internal-org coverage therefore requires: a contact per internal user + a junction row per contact + **an org-grant row on every root record they should see** — and grant-row creation automation for new records, which does not exist.
- Even then, every internal contact sees the **same** org-granted set. Per-user scoping ("their membership records") is gone.
- The only per-user contact mechanism, standing-grant contact membership, binds **only `sprk_assigned*` Contact-typed lookups** (`MembershipResolverService.cs:373-384, 403-410`); internal assignments held as owner/systemuser lookups/teams/BUs contribute **nothing**. Reproducing internal per-user scoping contact-only means re-anchoring internal record assignments to contact lookups — a Dataverse data-model migration that would also fight the MDA plane (where those same assignments must stay systemuser/team-based for native security). This is the decisive infeasibility.

Also note the level-semantics loss: workforce-plane composition stamps ALL projects `Collaborate` regardless of source (`CallerPrincipalResolver.cs:429-431`), so under contact-only-on-workforce, org-grant/grant `sprk_accesslevel` values (e.g. ViewOnly) are **discarded and elevated** for internal users — the same person gets ViewOnly via CIAM and Read|Create|Write via the workforce realm. Any migration inherits this until fixed.

---

## 5. Blast radius of removal

**Breaks loudly (403 / compile errors):**
- Internal users without contact records: 403 `sdap.access.deny.principal_not_resolved` on every `/api/v1/external/*` call (`WorkforcePrincipalResolver.cs:162-170`) — Teams tab + SPA "My organization" realm both dead for them.
- Type/enum cascade if actually deleted: `WorkforcePrincipalKind.SystemUser` (`ExternalCallerContext.cs:145`), `WorkforcePrincipal.SystemUserId`/`IsSystemUser` (`:167, 188`), `ForSystemUser` (`:236-246`), `AccessibleRecordSetSources.SystemUserMembership` (`AccessibleRecordSetService.cs:85-88`), `CallerPrincipal.SystemUserId` (`CallerPrincipalResolver.cs:80`), the `ComposeAsync` switch arm (`AccessibleRecordSetService.cs:153-161`).
- **Tests**: 68 references across 8 files break or lose their subject — `CallerPrincipalResolverTests` (29), `AccessibleRecordSetServiceTests` (13), `WorkforcePrincipalResolverTests` (9), `AccessibleRecordSetAuthorizationFilterTests` (8), `CallerPrincipalTests` (4), `ExternalModuleRegistryTests` (2), `ModuleEntitlementResolverTests` (1), and the ADR-038 KEEP-path seam test `tests/integration/seam/ExternalAccess/StandingGrantRuntimeUnionSeamTests.cs` (2).
- ADR-028 A2 MUST violated (`ADR-028:73`) → Path B amendment mandatory; A3 (dual-plane module host) also assumes both principal kinds.

**Silently empty (the dangerous bucket):**
- Internal users WITH contacts but no grants/standing flag/org membership: resolved principal, three empty root sets → every grid short-circuits to 0 rows without querying (`ExternalModuleDataEndpoints.cs:184-191` per note 01), `/projects` empty, downloads 403. No error anywhere; looks like data loss. This inverts Teams graduation criterion 1 silently.
- `service-requests` module keeps working (predicate needs only `Plane == Workforce && ContactId != Guid.Empty`, `ExternalAccessModule.cs:221-230`) — mixed signals for users: service requests visible, everything else gone.

**Fail-OPEN check: none found.** Unregistered strategy → 401 fail-closed (`CallerPrincipalResolver.cs:211-222`); unresolved principal → explicit deny; empty sets → 0 rows. Removal fails closed everywhere. The fail-open-shaped risk is not in the code path but in the **access model** of the mitigation: blanket internal-org grants on all records = everyone-sees-everything-granted, plus the Collaborate stamp elevating granted levels (§4d).

**Deploy/client artifacts affected**: the shipped Teams package (`deploy-teams-app.yml`, org catalog) and `deploy-external-spa.yml` don't break mechanically, but the live Teams app regresses behaviorally; the SPA's realm chooser (`RealmChooser.tsx`, `realm.ts`) would keep offering a "My organization" door into an empty room.

---

## 6. Coordination cost

Per `projects/INDEX.md` (rows quoted 2026-08-20):

| Project | State | Relevance |
|---|---|---|
| `teams-app-r1` (worktree `c:/code_files/spaarke-wt-teams-app-r1`) | **Complete/shipped** — merged PR #723, live-verified 2026-08-06 (`README.md:7`); INDEX row stale ("INITIALIZED") | Owns tasks 020/022/025 lineage; its shipped Teams app is the artifact this decision regresses. Not mid-flight, but its A2 amendment + graduation criteria are the contract being broken. |
| `spaarke-SPA-external-access-platform-r2` (worktree `…-wt-SPA-external-access-platform-r2`) | Shipped through task-073 UAT incl. org grants deployed 2026-08-11 (`task-073-org-grant-design.md:79-86`); BFF=Y, CI=Y | Owns `Api/ExternalAccess/**` + `Infrastructure/ExternalAccess/**` + the module framework; the 2026-08-07 owner directive (systemuser ∪ contact grants) lives here — **contact-only reverses half of the owner's own recent directive**; surface it explicitly. |
| `spaarke-SPA-external-access-platform-r3` (worktree `…-r3`) | **DRAFT design only** — "do not start execution" (`design.md:3` in the r3 worktree) | Its G1–G5 (record detail, Legal Front Door, notifications, Teams parity) assume the dual-plane surface as shipped; a UAC-r2 removal invalidates those assumptions before r3 specs. |
| `dataverse-access-unification-r1` | **PAUSED 2026-08-19** | Its validation note flagged the ExternalAccess `DataverseWebApiClient` stack (16 files) as out-of-scope-to-retire; UAC-r2 edits land in that same fenced stack. |
| `code-quality-and-assurance-r3` + ~19 active BFF worktrees | Active | `/conflict-check` before every PR; external-access corner is isolated from the AI/Compose cluster but shared with the two SPA projects above. |

**Collision files** (any UAC-r2 change): `AccessibleRecordSetService.cs`, `CallerPrincipalResolver.cs`, `WorkforcePrincipalResolver.cs`, `ExternalCallerContext.cs`, `ExternalAccessModule.cs`, `ExternalAccessEndpoints.cs`, `ExternalParticipationService.cs`, the 8 test files in §5, `.claude/adr/ADR-028-*` (main-session-only), and client-side `external-spa/src/auth/*` + `host/TeamsHostAdapter.ts` if the realm model changes.

---

## 7. Alternatives compared

| # | Option | What changes | Fixes the motivating finding? | Teams app | Risk | Effort |
|---|---|---|---|---|---|---|
| **A** | **Keep the branch; apply the access-conferring allow-list to the systemuser membership term + honor grant levels** | `ComposeForSystemUserAsync` filters membership to access-conferring roles (generalize `FilterToAccessConferringContactRoles` to systemuser/team-typed `sprk_assigned*` + owner, with exclusions — a design decision, cf. note 06 F5's convention critique); `WorkforcePrincipalStrategy` carries grant-sourced levels instead of blanket `Collaborate` (`CallerPrincipalResolver.cs:429-431`) | **Yes — both halves** (raw membership F2/F6 + level elevation) | Preserved (scoped, not removed) | Low-medium: behavior narrows (some records users see today disappear — communicate); allow-list definition for systemuser roles is the one real design task | ~2–4 tasks in `AccessibleRecordSetService`/`MembershipResolverService`/`WorkforcePrincipalStrategy` + tests |
| **B** | **Config-gate the systemuser membership term** (ADR-032 null-object kill-switch) | Flag chooses membership-term on/off per environment; off ⇒ systemusers behave as contact-only | Only where flag off; over-inclusion stays live where on | Preserved where on; degraded where off | Low code risk; asymmetric-registration rules apply (root CLAUDE.md §10 F.1) | Small |
| **C** | **Map workforce→contact at resolution time** (resolver returns ContactOnly for everyone with a contact) | `WorkforcePrincipalResolver` branch (a) skipped; downstream untouched | Superficially — but access-model consequences are **identical to removal** (§4d: internal per-user scoping lost) | **Broken** (same as removal, minus compile errors) | High (silent-empty failure mode) | Small code, huge data/ops |
| **D** | **Outright removal** (the decision as stated) | §5 in full + ADR-028 A2 Path-B amendment + internal contact/junction/org-grant provisioning buildout | Yes, by destroying the plane | **Broken** for internal staff | High | Large (code + data + ADR + client + comms) |

**Recommendation: A, optionally A+B.** A directly repairs both defects the operator found, keeps the shipped Teams contract and the owner's 2026-08-07 parallel-access directive intact, and aligns with note 06's verdict ("the two-plane *enforcement* split survives; the two-plane *membership-definition* split does not — apply the access-conferring filter to systemusers too"). B on top gives an environment-level escape hatch if a future org-grant-based contact-only model matures. C and D are the same access model with different code shapes, and both fail on §4d.

---

## 8. Verdict

**"SPA is contact-only" is correct with amendments — wrong as an outright removal.**

- **What the decision gets right**: the SPA/Teams boundary should be deliberately conferred, not raw-membership-derived. `ComposeForSystemUserAsync`'s unfiltered ADR-034 term (`AccessibleRecordSetService.cs:192-194`) read app-only, stamped Collaborate (`CallerPrincipalResolver.cs:360`), is a genuine over-permission (note 06 F2/F6) and additionally discards granted levels for workforce-realm contacts (`:429-431`).
- **What it gets wrong**: (1) it breaks the Teams app's verified core scenario for internal staff, who cannot authenticate any other way (A2 MUST NOT CIAM-in-Teams); (2) the Organization grant is per-record and per-org — it flattens access to "everyone in the org sees the granted set", abandoning per-user scoping rather than reproducing it; (3) the contact plane structurally cannot express systemuser-anchored assignments (`MembershipResolverService.cs:373-384`), so per-user internal scoping contact-only requires a record-assignment data-model migration nobody has scoped; (4) it reverses the owner's own 2026-08-07 UAT directive that created the systemuser∪contact-grants union; (5) it violates ADR-028 A2's MUST as written — a Path-B amendment conversation, not a refactor.
- **The legitimate reason the workforce plane exists, plainly**: Teams-hosted internal collaboration with per-user, membership-derived scope and zero per-user grant administration. Contact-only destroys that unless internal membership is re-modeled onto contacts — at which point you have rebuilt the systemuser plane under another name, with worse data.

**What evidence would change this verdict:**
1. An operator statement that the Teams app is being retired, or its audience narrowed to contact-holding users only (that reopens D honestly — the graduation criteria would need formal retraction).
2. Live-tenant data showing all internal SPA/Teams users already have contacts + `sprk_contactorganization` rows AND that internal record assignments are (or will be) contact-anchored — closing §4d.
3. An explicit product decision that internal users on SPA/Teams should see a shared org-wide set rather than "their" records (i.e., per-user scoping on these surfaces intentionally abandoned) — that makes the org-grant solve sufficient by definition.

---

## 9. Suggested next step for UAC-r2

Escalate per root CLAUDE.md §6.5 with this note attached: **ADR-028 A2 conflict — proposed path A/C hybrid**: keep the workforce plane (comply with A2), amend the *composition rule* (allow-listed membership + level-honoring) as a documented revision of teams-app-r1 design §5 — the same amendment channel the owner already used on 2026-08-07 (`access-model-systemuser-contact-grant-union.md:3-4`). Coordinate the edit with `spaarke-SPA-external-access-platform-r2` (file owner) and re-verify the two Teams graduation scenarios (positive: systemuser sees allow-listed membership; negative: ViewOnly grant no longer elevates) before merge.
