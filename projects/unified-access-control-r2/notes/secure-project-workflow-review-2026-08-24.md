# Secure Project workflow review — 2026-08-24

> Owner-requested ("we need to review the full secure project workflow to ensure we are supporting
> what we defined in the design"), triggered by the task-021 findings.
> Scope: end-to-end, wizard → BFF → Dataverse, compared against [`design.md` §5](../design.md) and
> spec FR-18 / FR-28 / NFR-05.

---

## Headline

**`sprk_issecure = true` currently confers no isolation on either surface.** It gates provisioning
and is displayed in the SPA. None of the three mechanisms design §5.1 specifies exist in code, and
the infrastructure provisioning *does* build is not wired into the security model at all.

Separately: **secure-project provisioning is very likely failing outright today** on a field
collision between the wizard and an idempotency guard added 2026-08-23 — see §4. That one is mine.

---

## 1. What design §5.1 requires

> - One **`Secure Projects` business unit**; secure records live there.
> - A **service account in that BU owns the records**, so they stay there naturally — no
>   matrix-data-access dependency, no BU-per-project proliferation.
> - **All human access is by explicit Dataverse share**, including the creating attorney's. Nobody
>   gets access by ownership; nobody by business unit.
> - For types 2 and 3, the §4.5 veto suppresses derived and org terms — explicit
>   `sprk_externalrecordaccess` grants only.

Plus **FR-28** (owned by a service account in `Secure Projects`; all human access by explicit share)
and **NFR-05** (standing assertion that no security role reaches the `Secure Projects` BU).

## 2. What the workflow actually does

| # | Step | Where |
|---|---|---|
| 1 | Create `sprk_project` with `sprk_issecure = true` | `CreateProjectWizard/projectService.ts:280-284` |
| 2 | Cascade `sprk_containerid` + `sprk_searchindexname` **from the creating user's BU** | same file, `applyUserBuDefaults` |
| 3 | `POST /api/v1/external-access/provision-project` | `provisioningService.ts` |
| 4 | Create a **new BU per project** under the **root** BU | `ProvisionProjectEndpoint` Step 2 |
| 5 | Create an SPE container | Step 3 |
| 6 | Create an OOB `account` owned by that BU | Step 4 |
| 7 | Stamp 3 references onto the project | Step 5 — **broken since 2026-03** |

The record is created by, and remains owned by, **the calling user**, in **the calling user's business
unit**. Nothing anywhere moves it.

## 3. Gap analysis — 3 of 3 core mechanisms absent

| Design requirement | Reality | Verified by |
|---|---|---|
| Secure records live in the `Secure Projects` BU | Project stays in the **creating user's BU**. **No code anywhere resolves or references a `Secure Projects` BU** | repo-wide grep: zero matches for a Secure-Projects BU lookup |
| A **service account** owns them | Owner is the **creating user** (default Dataverse ownership; the create payload sets no `ownerid`) | `projectService.ts` payload; no `ownerid` in `ProvisionProjectEndpoint` |
| **All human access by explicit share** | **No share is ever created.** `ProvisionProjectEndpoint` appears in none of the 14 files that perform POA/sharing | grep for `GrantAccess`/`PrincipalObjectAccess`/`AddUserToRecordTeam` |
| One `Secure Projects` BU, no per-project proliferation | Creates one BU per project, parented to **root** | `ProvisionProjectEndpoint` Step 2 |
| §4.5 Secure veto suppresses derived + org terms | Not implemented — Phase 1, task 037 | expected; not a defect |

**Consequences, stated plainly:**

- On the **MDA**, a secure project is an ordinary record owned by an ordinary user in an ordinary BU.
  Anyone whose role reaches that BU at Deep depth reads it. The design's isolation is entirely absent.
- The creating attorney has access **by ownership** — the one thing §5.1 explicitly forbids
  ("Nobody gets access by ownership").
- On the **SPA/Teams** plane, `sprk_issecure` is projected to the client for display
  (`ExternalDataService` `$select`) and drives no decision.
- **NFR-05's standing assertion would not protect the per-project BUs even if they were used**,
  because they are parented to root rather than under `Secure Projects`.
- The per-project BU is **inert**: nothing is owned by it, no record is moved into it, no role is
  scoped to it. It is created, stamped (or not), and never consulted.

## 4. 🚨 Live regression — provisioning likely 409s for every secure project

**This one is mine**, introduced 2026-08-23 as a task-008 follow-up.

The idempotency guard (`ProvisionProjectEndpoint` Step 1b) treats a project as already-provisioned when:

```csharp
(_sprk_securitybu_value is { } bu && bu != Guid.Empty)
|| !string.IsNullOrWhiteSpace(sprk_containerid);
```

But the wizard writes `sprk_containerid` **at creation time**, cascaded from the creating user's BU,
for every project including secure ones (`EntityCreationService.applyUserBuDefaults`, FR-WIZ-01..05).

So for any user whose BU carries a container id — and the cascade is live; a real
`sprk_project` row read today holds `sprk_containerid = "b!vzGDfDpd7km…"` — the sequence is:

1. Wizard creates the project **with `sprk_containerid` already populated** from the user's BU.
2. Wizard calls `/provision-project`.
3. The guard sees a non-empty `sprk_containerid` → **409 Conflict, "already provisioned"**.
4. No BU, no isolated container, no account. The secure project keeps pointing at the **shared BU
   container** that other users can reach.

Before the guard, provisioning ran and returned 200 having created orphaned infrastructure. After it,
provisioning refuses. The 409 is the more honest answer, but the net effect is that the workflow is
blocked rather than merely ineffective.

**Root cause of my error**: I chose `sprk_containerid` as a provisioning marker without checking who
else writes that field. That is the same mistake class as everything else in this review — assuming a
field's meaning instead of verifying its writers. `_sprk_securitybu_value` alone would have been a
sound marker; `sprk_containerid` is shared state.

## 5. Owner decisions taken 2026-08-24

| # | Decision |
|---|---|
| 1 | **Stop creating business units.** Use the single `Secure Projects` BU |
| 2 | **Stop creating accounts.** Firms are `sprk_organization`; use **`sprk_assignedlawfirm1`** (lookup → `sprk_organization`) |
| 3 | **Leave `sprk_externalaccount` alone.** The `account` table may serve other purposes, but it is not part of the secure-projects workflow |

## 6. Recommended work, in dependency order

### Immediate (re-scoped task 021)
1. Reduce the Step 5 payload to **`sprk_containerid` only** — a plain string, no navigation property.
   This also dissolves the `@odata.bind` blocker entirely.
2. **Delete Step 2 (BU create) and Step 4 (account create)**, plus their rollback paths.
3. Make the stamp **fail loudly** — non-2xx with a reason code and the container id that was created,
   so an operator can reconcile.
4. **Fix the idempotency marker** to `_sprk_securitybu_value` — or, once BUs are gone, to a dedicated
   marker that only provisioning writes. Do **not** key it on `sprk_containerid`.
5. Regression test: provisioning **never writes** `sprk_externalaccount`.
6. Fix `secure-project-fields-schema.md` (task 026 scope) — it is the source of the wrong names.

### Needs a task of its own — the actual security mechanism
None of design §5.1 is built. This is not part of 021 and should not be smuggled into it:

- Move / create secure projects **in the `Secure Projects` BU**
- **Service-account ownership** (needs the account provisioned + configured; FR-28)
- **Explicit share to the creating attorney** at creation (FR-28; the POA/teams plumbing already
  exists in `PlaybookSharingService` and `IDataverseAccessGrantService` — CLAUDE.md §11 says
  consolidate, do not write a third client)
- The wizard's BU cascade should **not** apply `sprk_containerid` to secure projects at all

### Already scheduled
- §4.5 Secure veto (suppress derived + org for types 2/3) — task **037**
- NFR-05 standing role-depth assertion — task **034** family
- BU restructure itself is **UAT/environment work**, per spec § UAT & Environment Setup

---

# Addendum — investigations requested 2026-08-25

## A. 🚨 How SPE actually resolves a container — the answer is neither of the two options

The question was "does it read the container id on the record, or the associated business unit's?"
**It does a third thing, and that third thing breaks the per-project model.**

**The CLIENT resolves the container from the *acting user's* business unit and passes it to the BFF.**
The BFF deliberately does not resolve it at all. From `IComposeService.cs:743-751`:

> *CLIENT-SUPPLIED SPE container (or drive) id … The client resolves this via the existing wizard
> cascade (`resolveBusinessUnitContainerId` → `businessunit.sprk_containerid`) and passes it in.
> Required when `DocumentSpeId` is absent; **the BFF does NOT resolve a business-unit → container
> mapping server-side** (multi-container INV-7 — the resolver stays in the wizards).*

The canonical client resolver, `getSpeContainerIdFromBusinessUnit` (`xrmProvider.ts:97`), is
explicitly `userId → systemuser.businessunitid → businessunit.sprk_containerid`. The same
user-BU→container chain is repeated in at least seven places: `CreateMatterWizard/main.tsx:101`,
`sprk_wizard_commands.js:115`, `useWizardPageBootstrap.ts:184`, `WorkspaceGrid.tsx:535`,
`SemanticSearchControl/NavigationService.ts:361`, `EntityCreationService.applyUserBuDefaults`, and the
five `*WizardDialog.tsx` callers.

**Zero server-side paths read `sprk_containerid` off a `sprk_project` row** other than provisioning's
own idempotency check. The field is written by the cascade and read by essentially nobody.

### Why this breaks secure projects three ways

1. **A per-project container stamped on the project would not be used.** Uploads resolve from the
   acting user's BU, so documents land wherever that user's BU points — not in the project's container.
2. **Users stay in the Operations subtree** (design §5.2) while secure records are owned in
   `Secure Projects`. So the acting user's BU is an *Operations* BU, and secure-project documents
   would be written into the **general Operations container**, reachable by anyone with access to it.
3. **The single `Secure Projects` BU has one container id**, so a BU-based resolution cannot give
   per-project isolation even if the user were somehow in that BU.

### What this implies

Per-project containers require the container to be resolved **from the record** (the project or matter
the document belongs to), not from the acting user. That contradicts the documented invariant INV-7
("the resolver stays in the wizards") and touches every upload path — Office save, email attachment
ingest, Compose create-on-save, wizard document add. **This is the largest single piece of work the
secure-project model needs, and it is not in any current task.** It is also not
unified-access-control-r2 scope as written; it needs its own project or a spec amendment.

## B. Business unit resolution must be by NAME, not GUID — confirmed, and it is a small change

Current code resolves the **root** BU via `parentbusinessunitid eq null`
(`ResolveRootBusinessUnitIdAsync`). Replacing that with a name lookup is straightforward:

```
GET businessunits?$filter=name eq '{configuredName}'&$select=businessunitid&$top=2
```

Notes for implementation:
- **Business unit names are unique per organisation in Dataverse**, so a name lookup is deterministic.
  Select `$top=2` and fail if more than one comes back rather than silently taking the first.
- Put the name in configuration (e.g. `SecureProjects:BusinessUnitName`, default `"Secure Projects"`)
  rather than hard-coding it, so a customer rename does not require a deploy.
- **Fail closed and loudly** if the BU is not found — provisioning must not fall back to the root BU
  or the caller's BU. A missing `Secure Projects` BU is an environment-setup error.

## C. Owner = team. A service account is NOT necessary — the owner's instinct is right

Dataverse facts that settle this:

- **Every business unit automatically gets a default owner team on creation**, named after the BU. So
  "the team that corresponds to the Secure Projects business unit" **already exists** and needs no
  provisioning.
- `sprk_project` is user-or-team owned (`ownerid` is an `OWNER` field, confirmed in live metadata), so
  a team is a valid owner.
- **Ownership is independent of privileges.** A team can own records with no security roles at all.

So team ownership parks the record in `Secure Projects` with **no licence cost, no credential to
manage, and no service-account identity to audit**. It is strictly better than a service account here.
The only requirement is that the BFF application user holds `Assign` + `Write` on `sprk_project` to
set the owner.

**Recommendation: use the BU's default owner team. Do not introduce a service account.**

## D. What security role does the secure-project team need? **None — and definitely NOT System Administrator**

This is the most important thing to get right, because the intuitive answer is dangerous.

- A team's security roles determine what **team members** can do. They do **not** grant or restrict the
  team's ability to *own* records.
- **System Administrator on that team would be catastrophic**: every member would gain org-wide admin,
  and it would nullify NFR-05 completely — sysadmin reaches every business unit by definition.
- Any role granting Read on `sprk_project` at Business-Unit depth would also break §5.1, because team
  members would then read **every** secure project by membership rather than by explicit share.

**Recommendation: the owner team carries no security roles, and ideally no members.** It exists solely
to hold ownership so records sit in the `Secure Projects` BU. All human access then comes from the
explicit share, exactly as §5.1 specifies.

⚠️ **One mechanism detail that must not be missed.** A POA share is only effective if the user also
holds the entity-level privilege at *some* depth. A user with **zero** Read privilege on
`sprk_project` cannot see a shared secure project. So the normal user roles must retain Read on
`sprk_project` at **Basic/User depth** — which is harmless (it grants nothing without ownership or a
share) and is what makes sharing work. If roles were stripped of `sprk_project` Read entirely to
"secure" things, sharing would silently stop working.

## E. Licensed-user access: named shares vs a per-record access team

Both satisfy §5.1's "explicit Dataverse share". The trade:

| | Direct POA share per user | **Access team per record** |
|---|---|---|
| POA rows | N per record | **1** per record |
| Add/remove a person | write/delete a POA row | `AddUserToRecordTeam` / `RemoveUserFromRecordTeam` |
| Dataverse guidance | fine at low volume | **the purpose-built mechanism** for per-record sharing at scale |
| Extra dependency | none | a team template |

**Recommendation: access teams**, for three reasons — Dataverse's own recommended pattern for exactly
this shape; revocation becomes one membership delete instead of hunting POA rows; and the codebase
**already has POA-with-teams** in `PlaybookSharingService.cs:302-350`. Per project CLAUDE.md that code
is to be *consolidated* with `IDataverseAccessGrantService`, **not forked a third time** — so building
this on access teams reuses an existing seam rather than adding one.

Do **not** create a static per-project owner team; that is BU-per-project proliferation wearing a
different hat.

## F. External (non-licensed) contact access — needs live verification, not code review

The mechanism exists and has been worked on across tasks 007/010/016/017: `CallerPrincipalResolver` →
`AccessibleRecordSetService` → `sprk_externalrecordaccess` grants, with expiry (007), idempotent
upsert + full-key revoke (010), and closure cascade (016/017). What is **not** verified is any of it
against a live tenant — that is task **034**, which already owns RPA live verification.

Specific cases to test, which existing notes flag as unproven:
- Expiry: past-expiry gone, today's expiry still works, **null expiry unaffected** — test null FIRST,
  because if that predicate is wrong external access is down for nearly everyone (task 007 note)
- A contact grant on a secure project confers access on the SPA and **not** on the MDA
- Closure revokes both the Dataverse rows and the SPE container membership (016/017)
- The §4.5 Secure veto suppresses derived + org terms — **unbuilt**, task 037

## G. Create Project Wizard — promote Secure Project to its own step (owner request, 2026-08-25)

Today it is a **section inside** the single form step: `CreateProjectStep.tsx:402` renders
`<SecureProjectSection>` beneath the ordinary fields, driven by one toggle
(`SecureProjectSection.tsx`), and the wizard branches on `formValues.isSecure` in three places
(`CreateProjectWizard.tsx:453, 686, 805-814`).

Making it a discrete step is worth doing for reasons beyond layout:

- **The decision is irreversible.** `projectService.ts:280-284` notes `sprk_issecure` is only ever set
  to `true` — "once a project is secure, the designation is irreversible". A one-way choice buried
  under a form is the wrong weight of UI for the consequence.
- **A dedicated step is where the access model gets captured.** Once §5.1 is implemented, creating a
  secure project needs *who gets the initial explicit share* — the creating attorney is not enough,
  because ownership no longer grants them anything. That input has nowhere to live in the current
  layout, and it is precisely what the FR-28 task will need to collect.
- **It gives the container decision a home.** Per §A, a secure project needs its own SPE container
  rather than the cascade from the creator's BU. A step can state that explicitly instead of the
  cascade silently applying.
- **It separates provisioning failure from field validation.** `ProvisioningProgressStep` already
  exists as its own step; a secure-project step in front of it makes the sequence legible
  (choose secure → confirm access → provision).

Sequencing note: the step should be authored **with** the FR-28 access-model work, not before it.
Shipping an empty step that only relocates one toggle adds a click and no value; shipping it as the
place where initial shares and the container are declared is the version that earns its place. Filed
accordingly in §H.

## H. Summary of what the secure-project model still needs

| Gap | Owner |
|---|---|
| Stop creating BUs; resolve `Secure Projects` **by name**; own via its default team | **re-scoped 021** |
| Per-project SPE container **provisioned and actually used** | ⚠️ **new project/spec amendment** — the resolver is client-side and user-BU-based (§A) |
| Explicit share to the creating attorney; access-team mechanism | **new task** (FR-28) |
| Owner-team role posture (none) + keep Basic Read on `sprk_project` in user roles | environment setup + NFR-05 assertion |
| Secure veto on derived/org terms | task **037** |
| Live verification of external contact access | task **034** |
| Create Project Wizard: Secure Project as its own step | **with the FR-28 task**, not before it (§G) |

## 7. What this review did NOT cover

Closure (`ProjectClosureEndpoint`) was reviewed in tasks 016/017 and is not re-examined here. The
external grant/revoke lifecycle is covered by tasks 007/010/023. The MDA-side wizard UX and the
irreversibility of `sprk_issecure` were not assessed.
