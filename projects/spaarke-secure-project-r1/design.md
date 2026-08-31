# design.md — `spaarke-secure-project-r1`

> **Status**: DRAFT for owner review. Not yet a spec; no tasks created.
> **Created**: 2026-08-25
> **Origin**: split out of `unified-access-control-r2` after an owner-requested review of the secure
> project workflow. Evidence:
> [`projects/unified-access-control-r2/notes/secure-project-workflow-review-2026-08-24.md`](../unified-access-control-r2/notes/secure-project-workflow-review-2026-08-24.md)

---

## 1. Why this project exists

`unified-access-control-r2` is unifying two authorization systems into one evaluator. Reviewing the
secure-project workflow against its own design turned up something adjacent but distinct: **a secure
project's documents are not stored anywhere isolated, and nothing in the codebase reads the container
the project is supposed to own.**

That is document-storage routing with a security consequence, not evaluator work. It spans seven
client call sites, a server-side ingest path, a documented invariant, and wizard UX — none of which r2
owns. Folding it into r2 would roughly double that project's blast radius and put its authorization
deliverables behind a client-side refactor. Hence a separate project.

**What r2 keeps**: provisioning correctness (task 021 — stamps the container) and the FR-28 access
mechanism (explicit share via access teams). **What this project owns**: making the stamped container
actually the place secure documents live, plus the wizard surface that declares it.

---

## 2. The finding this project is built on

The question asked was *"does SPE read the container id from the record, or from the associated
business unit?"* The answer is **neither** — and there are **three** resolution strategies in the
codebase, not one.

| # | Strategy | Where | Secure-project correctness |
|---|---|---|---|
| **1** | Acting **user's** BU → `businessunit.sprk_containerid` | 7 client sites. Canonical resolver `xrmProvider.getSpeContainerIdFromBusinessUnit` (`userId → systemuser.businessunitid → businessunit.sprk_containerid`). The BFF deliberately does **not** resolve this server-side — documented invariant **INV-7**, "the resolver stays in the wizards" (`IComposeService.cs:743-751`) | ❌ **broken** |
| **2** | A single global `ArchiveContainerId` from config | Server-side communication/email ingest (`IncomingCommunicationProcessor.cs:868, 991`) | ❌ **broken** |
| **3** | The document's own `GraphDriveId` / `GraphItemId` | Every read/download path once a document exists | ✅ correct — already per-document |

**Zero server-side paths read `sprk_containerid` from a `sprk_project` row** other than provisioning's
own idempotency check.

### Why each break matters

**Strategy 1** — users live in the Operations subtree (r2 design §5.2) while secure records are owned
in `Secure Projects`. So the acting user's BU is an *Operations* BU, and a secure project's documents
are written into the **general Operations container**, reachable by anyone with access to it. Stamping
a per-project container on the project changes nothing, because no upload path reads it.

**Strategy 2** — attachments and `.eml` archives ingested against a secure project's communications
land in the **single global archive container**, shared across every matter. This is the easier one to
miss: no client is involved and there is no wizard to host a resolver.

**Strategy 3** needs no change, and that is worth stating: once a document exists its location is
recorded on the document row, so *reads* are already correctly per-document. The defect is entirely in
**where new bytes are placed**.

### The owner's decision (2026-08-25)

> *"Can we just have a special case for secure project since the general approach is the correct
> approach? So if `issecure=yes`, then use the Project `sprk_containerid`."*

**Accepted, and it is the right call.** The BU-based cascade is correct for ordinary records and should
stay. Secure projects are the exception. This is far smaller than making resolution record-based
everywhere — but it must cover **strategies 1 and 2**, not just the wizard.

---

## 3. Design

### 3.1 One record-aware resolver, every call site through it

```
resolveContainerForContext(contextRecord) →
    if contextRecord is a project with sprk_issecure = true → contextRecord.sprk_containerid
    else                                                    → existing BU cascade
```

**Rule: do not add an `issecure` test at seven client sites.** That is seven places to drift, and this
codebase has already demonstrated what drift costs (seven stale-column instances, one of which caused
a Critical finding). One resolver, one behaviour, every caller routed through it.

Known call sites to route (non-exhaustive until discovery completes):

| Surface | File |
|---|---|
| Canonical client resolver | `LegalWorkspace/src/services/xrmProvider.ts:97` |
| Wizard bootstrap | `Spaarke.UI.Components/src/utils/useWizardPageBootstrap.ts:184` |
| Create-entity cascade | `Spaarke.UI.Components/src/services/EntityCreationService.ts` |
| Matter wizard | `CreateMatterWizard/src/main.tsx:101` |
| Legacy web resource | `client/webresources/js/sprk_wizard_commands.js:115` |
| Workspace grid | `LegalWorkspace/src/components/Shell/WorkspaceGrid.tsx:535` |
| Semantic search nav | `SemanticSearchControl/services/NavigationService.ts:361` |
| Wizard dialogs (×5) | `LegalWorkspace/src/components/Create*/…WizardDialog.tsx` |

### 3.2 Strategy 2 needs a server-side equivalent

Server ingest has no client to ask. It must resolve the container from the **communication's parent
record**: if that parent is a secure project, use the project's container; otherwise the configured
`ArchiveContainerId`.

This is the one place where a **server-side** record→container resolution is unavoidable, which means
**INV-7 must be amended** rather than worked around. The amendment is narrow and should say so
explicitly: the wizards remain the resolver for interactive paths; background ingest resolves from the
record because there is no client in the loop.

⚠️ **Open question for the owner** (§6, Q1): does a secure project's email/communication archive belong
in the project's container at all, or should archival be *refused* for secure projects until a policy
exists? Refusing is arguably safer than routing, and is a smaller change. This needs a decision before
implementation.

### 3.3 Stop cascading `sprk_containerid` onto secure projects

`EntityCreationService.applyUserBuDefaults` writes `sprk_containerid` from the creating user's BU at
create time, for every project including secure ones. That must not happen for a secure project — it
both defeats isolation and collides with provisioning's idempotency marker (r2 review §4; r2 task 021
fixes the marker, this project fixes the cascade).

### 3.4 Create Project Wizard — Secure Project as its own step

Today it is a section inside the single form step (`CreateProjectStep.tsx:402` renders
`SecureProjectSection`), with the wizard branching on `formValues.isSecure` in three places.

Promoting it to a discrete step earns its place for reasons beyond layout:

- **The choice is irreversible.** `projectService.ts:280-284` records that `sprk_issecure` is only ever
  set to `true`. A one-way decision with this consequence should not be buried under a form.
- **It is where the container decision becomes visible** rather than a silent cascade.
- **It is where the initial access declaration lives** — once r2's FR-28 lands, ownership no longer
  grants the creating attorney anything, so *someone must be named*. There is nowhere for that input
  today.
- **It separates provisioning failure from field validation.** `ProvisioningProgressStep` already
  exists as its own step; a secure step in front of it makes the sequence legible.

**Dependency**: this step must *consume* r2's FR-28 access model. It should not ship as a relocation of
one toggle — that adds a click and no value. It should also absorb r2 **FR-31**'s copy fixes (the
retired Power Pages claim, the permanence warning).

---

## 4. Scope

### In scope
1. One shared record-aware container resolver; all client call sites routed through it (§3.1)
2. Server-side ingest resolution for strategy 2, subject to Q1 (§3.2)
3. Suppress the `sprk_containerid` BU cascade for secure projects (§3.3)
4. Amend **INV-7** to sanction server-side resolution for background paths only (§3.2)
5. Create Project Wizard: Secure Project as its own step, absorbing FR-31's copy fixes (§3.4)
6. Verification that a secure project's documents actually land in its own container — the acceptance
   test this whole project exists for

### Out of scope
- **Provisioning correctness** — `unified-access-control-r2` task 021 (creates and stamps the container)
- **The explicit share / access teams** — r2 FR-28
- **The `Secure Project Owner` security role, the BU, the owner team's membership posture** — environment
  setup, per r2 spec § UAT & Environment Setup
- **The §4.5 Secure veto** (suppressing derived + org terms) — r2 task 037
- **Migrating documents already stored in the wrong container** — see Q2
- Retro-fitting per-record containers to non-secure projects. The BU cascade is correct for them; this
  project does not change ordinary behaviour

---

## 5. Dependencies and sequencing

```
r2 task 021 ──────────────► this project
(provisions + stamps         (makes the container
 the container)               actually used)
        │
        └── r2 FR-28 ───────► this project §3.4
            (access model)     (wizard step consumes it)
```

**r2 task 021 is a hard prerequisite** for anything to read: there is no per-project container to
resolve until provisioning creates one. **r2 FR-28 gates only §3.4**, not §3.1–3.3.

**Honest statement of the gap**: between 021 shipping and this project shipping, provisioning writes a
container that nothing reads, and secure documents continue to land in shared containers. 021 is still
worth shipping first — it closes a live provisioning failure and is the prerequisite — but **document
isolation is not achieved until this project lands.** That gap should be an explicit, accepted risk,
not a surprise.

---

## 6. Open questions for the owner

| # | Question | Why it matters |
|---|---|---|
| **Q1** | For a secure project, should email/communication archival route to the project's container, or be **refused** until a policy exists? | Refusing is safer and smaller. Routing means secure archives share a container's lifecycle with the project. §3.2 cannot be implemented without this. |
| **Q2** | What happens to documents **already stored** in shared containers for existing secure projects? Migrate, leave, or flag? | If there are existing secure projects in dev/UAT, their documents are in the Operations or global archive container today. Migration is a data-movement exercise with its own risk. |
| **Q3** | Should the wizard's Secure step let the creator **name the initial shared users**, or is that a post-create action? | Determines whether §3.4 depends on FR-28's API surface being complete, or can ship with a link to a separate management surface. |
| **Q4** | Do **matters** need the same treatment as projects? `sprk_matter` carries the same `sprk_externalaccount` client field and the same live-fact resolver shape. There is no `sprk_issecure` on matter per live metadata — is a secure *matter* a concept? | Decides whether the resolver keys on "secure project" or on a more general "secure record" notion. Affects the resolver's signature, so it is cheap now and expensive later. |

---

## 7. Risks

| Risk | Mitigation |
|---|---|
| Seven client call sites drift back to BU-only resolution | One resolver, and a lint/test forcing function that fails if `businessunit` + `sprk_containerid` are read together outside it |
| INV-7 amendment is read as "server-side resolution is now fine everywhere" | Amend narrowly and state the boundary: background paths only, because no client exists |
| A secure project with no provisioned container silently falls back to the BU container | **Fail closed.** A secure project with an empty `sprk_containerid` must refuse the upload, not fall back — falling back is the disclosure |
| The wizard step ships as a relocated toggle and adds friction with no benefit | Gate it on FR-28 (§3.4 dependency); do not schedule it first |
| Scope creep into r2's authorization work | The out-of-scope list in §4 is explicit; every item there names its owner |

---

## 8. What would make this project done

A secure project's documents — created through the wizard, uploaded from the grid, saved from Office,
and ingested from email — all land in **that project's own SPE container**, and an attempt to place
them anywhere else fails loudly rather than falling back. Ordinary projects are unchanged.
