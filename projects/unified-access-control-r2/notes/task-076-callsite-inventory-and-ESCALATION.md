# Task 076 — call-site inventory (complete) + ESCALATION

> **Status**: 🔔 **ESCALATED — not implemented.** Step 0 (the inventory) is done and is the acceptance
> evidence the POML asked for. Steps 1–6 are blocked on one modelling decision.
> **Date**: 2026-08-26 · Depends on task 075 (shipped, commit `6153049`).

---

## 🔴 READ FIRST — F-9 is a live fail-open, traced end-to-end

**The decision below is not "which resolution point is cleaner". It is "which resolution point closes
a fail-open that is armed in shipped code today".** Options (A) and (C) read differently under that
framing than under a cleanliness framing.

> **F-9 · CRITICAL · Creating a secure project with files puts those bytes in the acting user's
> business-unit container while the row advertises the project's own.**
>
> Traced end-to-end from source (§3b) — not inferred. The row ends up **correct** because provisioning
> overwrites the stamp; the bytes end up **wrong** because the client never learns the new container.
> Because SPE permissions are additive-only, **that placement cannot be retracted** by any later
> permission change.
>
> **Armed, not yet fired.** The code path is live in shipped code; the trigger — creating a secure
> project with files attached in the wizard — has never been exercised, because zero secure projects
> exist in any environment (build plan §2). The first real secure project with an attachment fires it,
> irreversibly. This is the strongest available argument for the build plan's "build it right now"
> position: the window closes on first use, not on a date.

**Scope, so this is not mis-filed**: F-9 does **not** reopen the task 075 gate. 075 built the seam and
the gate closed on the seam; 076 routes the call sites, and F-9 is a call-site defect. It is also
**pre-existing** — not introduced by 075 or 076 — and is in fact *the original defect this wave exists
to close*, finally traced end-to-end rather than asserted. Both facts argue for naming it plainly.

Full detail: **§4 F-9**. Verified sequence: **§3b**.

---

## 1. Why this is escalated rather than half-done

The POML carries this trigger, and it fires:

> *"If a call site resolves a container in a context where the owning record is not known — so the
> resolver cannot be asked — STOP and surface it. That is a modelling gap; guessing the container in
> an unknown context is exactly how shared-container co-mingling happened."*

**It fires for the majority of the client sites, and not marginally.** Every `Create*Wizard` resolves
its container when the wizard **opens** — before the record it is about to create exists. At that
moment there is no record to ask the resolver about. Verified sequence
(`CreateProjectWizard.tsx` `onFinish`, from the POML's own end-to-end trace):

| # | Action | Line | Does the owning record exist? |
|---|---|---|---|
| — | Container resolved from the acting user's BU | `CreateRecordWizard.tsx:221-227` (wizard OPEN) | ❌ **no** |
| 1 | Resolve BU cascade defaults | 542-544 | ❌ no |
| 2 | `createProject(...)` — row created, `sprk_issecure` written | 561 | ✅ from here on |
| 4 | Secure provisioning — the record's OWN container is created | 688-698 | ✅ |
| 5 | **File upload to SPE** — uses the container resolved at OPEN | 708-712 | ✅ |
| 6 | `createDocumentRecords` stamps that container onto `sprk_document` | 721 | ✅ |

So the existing call sites are in the wrong *place* to ask the resolver, not merely calling the wrong
*function*. Task 075's seam takes `(entity, recordId)`; at wizard-open there is no `recordId`. Calling
it there with a blank id makes it refuse (correctly — 075 treats a securable entity with an empty id
as indeterminate) which would break every create flow, or, if softened, would silently return the BU
fallback — the co-mingling the trigger names.

### The POML contradicts itself on the mechanism, which is the decision needed

- Its **constraint** says: *"Route each site through 075's resolver"* and *"No code decides which
  container to use except through task 075's resolver."*
- Its **own worked example** says the CreateProjectWizard fix is: *"consume
  `provisionResult.data.speContainerId` at `:712` and `:726`"* — i.e. take the container from
  **provisioning's return value**, which is **not** 075's resolver.

Those are two different mechanisms with different guarantees. The provisioning-return-value fix is
two lines and repairs the create flow, but it only works on the create path: it does nothing for an
upload against an **already existing** secure record (DocumentUploadWizard, the subgrid command, the
analysis command, WorkspaceGrid, SemanticSearchControl). Routing through the resolver covers both but
requires **moving the resolution point to upload time** in 7 wizards.

I will not pick between them silently. Choosing wrong here is the difference between "secure content
is isolated" and "secure content is isolated on one of six paths", and because SPE permissions are
additive-only there is no later repair for whichever path is wrong.

### Why not implement the unambiguous subset anyway

Because this task's own note forbids exactly that:

> *"Completeness is the entire deliverable. Because SPE permissions are additive-only, one missed
> call site is a permanent isolation failure for whatever flows through it — there is no later fix.
> Partial completion here is not partial credit — it is the same failure as no completion, for
> whichever path was missed."*

A half-routed surface is also actively harmful in a second way: it would delete the `Pending` waivers
and satisfy the reviewer's mental model ("076 is done") while leaving paths open. Better to hand over
a complete map and one decision.

---

## 2. 🔔 Human Input Required

**Situation.** Task 075's resolver is shipped and proven. Task 076 must route every container call
site onto it. 12 of the client sites resolve their container **before the owning record exists**, so
the resolver cannot be asked where they currently sit. The POML prescribes two incompatible
mechanisms (route-through-resolver vs consume-provisioning-return-value).

**Options.**

- **(A) Move resolution to upload time — route-through-resolver, fully.**
  Wizards keep resolving the BU container at open (INV-7, unchanged) but treat it as the *fallback
  only*. Immediately before the first byte moves, each upload path calls
  `resolveContainerForRecord({ entityLogicalName, recordId, fallbackContainerId: <the BU value> })`
  and uses the answer. `provisionSecureProject`'s return value becomes unnecessary — the resolver
  reads the row provisioning just stamped.
  · *Covers*: create flows AND uploads against existing records. One mechanism, one decision point.
  · *Cost*: touches the upload path in 7 wizards + `CreateRecordWizard` + DocumentUploadWizard; the
  eagerly-resolved `speContainerId`/`speContainerIdRef` becomes a fallback rather than the answer.
  · *Risk*: an ordering bug in a wizard shows up as an upload failure, not a silent mis-route — the
  failure mode is loud, which is the right direction.
  · **My recommendation.** It is the only option that satisfies the POML's constraint as written, and
  it is the only one that also fixes uploads against pre-existing secure records.

- **(B) Two mechanisms: provisioning-return-value on create + resolver on existing-record uploads.**
  · *Cost*: smaller diff on the create path (the POML's two-line fix).
  · *Risk*: **two** mechanisms deciding one thing — the failure class every finding in this project
  came from, and the POML's own constraint ("ONE resolver … not a client copy and a server copy of
  the decision") argues against it. Also leaves `speContainerIdRef` authoritative on create, so a
  future reordering re-opens the hole silently.

- **(C) Record-keyed upload contract (the deeper fix, larger scope).**
  Change the upload routes to take `(entity, recordId)` instead of a caller-named container; the
  server resolves the container from the record it is already authorizing. The client stops deciding
  entirely and the C#/TS duplication from 075 §4 disappears.
  · *Cost*: spans task 073's authorization + this task's routing + the OBO upload trio's waivers.
  Not deliverable inside 076 as scoped.
  · *Note*: this is the right end state. Recommend filing it as a follow-on task regardless of
  whether A or B is chosen for now.

**Recommendation: (A)**, plus file (C) as a follow-on. (A) honours the POML's binding constraint,
needs no new mechanism, and makes the resolver the single decision point on both create and
existing-record paths.

### ⚠️ Check the chosen option against the two-hop child gap — do not decide it by accident

Task 075's review surfaced a gap that is **the same question as this escalation, one hop further
out**, so whichever option is chosen here should be checked against it deliberately:

> A communication regarding `sprk_invoice`, where that invoice belongs to a **secure matter**,
> resolves to the shared archive container. `sprk_invoice` is in `RegardingFieldMap.All` but is not
> securable, so the securable-regarding scan skips it. One hop out, the answer is wrong.

This is not a separate defect class — it is *"which record is the decision about?"*, which is exactly
what options A/B/C are choosing between. The connection matters because:

- **Under (A)**, the resolver is asked at upload time about a record the caller names. If that record
  is a child, the same gap appears on the client paths too, not just on ingest. (A) does not close the
  gap, but it puts every path through **one** place where closing it later is a single change.
- **Under (B)**, the resolver is never consulted on the create path, so the two-hop case cannot be
  fixed there without a third mechanism. **Two independent reasons, verified from source** — an
  earlier draft of this note gave only the second and stated it as current behaviour, which was
  wrong:
  1. **Scope**: the container is decided **client-side at create, before anything server-side runs.**
     `CreateProjectWizard/projectService.ts:283-285` sets `sprk_issecure`, then `:291-292` applies
     `EntityCreationService.applyUserBuDefaults` **unconditionally** — there is no secure-project
     suppression — so the row is stamped with the acting user's BU container either way. That is
     write **W1** in §3b, and removing it is in 076's scope under any option; but (B) leaves the
     *decision* client-side, so there is no point at which a resolver could be asked.
  2. **Architecture**: for the *bytes*, (B)'s fix routes the container from **provisioning's return
     value** — a different subsystem from the resolver. So even after (B) lands, nothing on the create
     path consults the seam.
- **Under (C)** (record-keyed upload), the server resolves from the record it is authorizing, so
  closing the gap is a server-side change in one place and the client is unaffected.

Closing the gap itself needs the **Phase 3 denormalized core-ancestor stamp** (tasks 050–055) — the
project's model already says children inherit one hop via that stamp; the container decision simply
does not follow it yet. So this is **not** a prerequisite for 076. It is a constraint on the choice:
pick the option that leaves the gap closable in one place rather than three.

Cross-references: `task-075-*.md` §6 finding F-4 (the ingest-side statement of the gap) and §10
"what the review found that the design genuinely missed" item 2 (the two-hop extension).

---

## 3. THE COMPLETE INVENTORY — grepped, not remembered

The POML says *"treat this list as a STARTING POINT and prove it complete, because prior counts in
this project have been wrong every time"*. They were wrong again.

> **POML said**: 7 client sites + `IncomingCommunicationProcessor`.
> **Actual**: **12 distinct client resolution sites, 2 client write-back sites, and 9 server sites
> across 3 files.** The POML itself had already found an 8th ("the wizard's own inline BU resolver at
> `CreateProjectWizard/src/main.tsx:88-98`"); there are four more beyond that.

### 3a. Client — business-unit container resolution (12 sites)

| # | File : line | Shape | In POML list? |
|---|---|---|---|
| 1 | `src/solutions/CreateProjectWizard/src/main.tsx:96-97` | inline BU read | ⚠️ POML flagged as "8th" |
| 2 | `src/solutions/CreateMatterWizard/src/main.tsx:101-102` | inline BU read | ✅ |
| 3 | `src/solutions/CreateWorkAssignmentWizard/src/main.tsx:70-71` | inline BU read | ❌ **NEW** |
| 4 | `src/solutions/CreateTodoWizard/src/main.tsx:170-171` | inline BU read | ❌ **NEW** |
| 5 | `src/client/shared/Spaarke.UI.Components/src/utils/useWizardPageBootstrap.ts:184-185` | inline BU read | ✅ |
| 6 | `src/solutions/LegalWorkspace/src/services/xrmProvider.ts:97` | `getSpeContainerIdFromBusinessUnit` — the canonical resolver | ✅ |
| 7 | `src/solutions/SmartTodo/src/services/xrmProvider.ts:97` | **a SECOND copy of the canonical resolver** | ❌ **NEW** |
| 8 | `src/solutions/DocumentUploadWizard/src/components/AssociateToStep.tsx:130` | `resolveBusinessUnitContainerId` | ✅ (as "5 `*WizardDialog.tsx` callers") |
| 9 | `src/solutions/DocumentUploadWizard/src/components/AssociateToStep.tsx:154-160` | **already record-aware** — reads the associated record's `sprk_containerid`, silently falls back to BU on ANY failure incl. "field not on entity" | ❌ **NEW — see F-5** |
| 10 | `src/client/webresources/js/sprk_wizard_commands.js:115-116` | inline BU read | ✅ |
| 11 | `src/client/pcf/SemanticSearchControl/.../services/NavigationService.ts:354-362` | inline user→BU→container chain, passed as `&containerId=` on a URL | ✅ |
| 12 | `src/solutions/LegalWorkspace/src/components/Shell/WorkspaceGrid.tsx:535-537` | inline BU read | ✅ |

Consumers of #6 (the canonical resolver) that do not themselves resolve:
`LegalWorkspace/components/CreateProject/ProjectWizardDialog.tsx:121`,
`CreateMatter/WizardDialog.tsx:135`, `CreateEvent/EventWizardDialog.tsx:103`,
`SmartTodo/src/SmartTodoApp.tsx:643`.

Consumers of `EntityCreationService.resolveUserBuDefaults` (the shared cascade):
`CreateProjectWizard/src/main.tsx:105`, `CreateMatterWizard/src/main.tsx:116`,
`LegalWorkspace/src/sections/composeEditor.registration.ts:182`,
`Spaarke.UI.Components/src/components/EmailComposer/createXrmEmailComposeHandlers.ts:242`.

### 3b. Client — `sprk_containerid` WRITES (2 sites, both harmful)

| # | File | What it does | POML |
|---|---|---|---|
| W1 | `Spaarke.UI.Components/src/services/EntityCreationService.ts:327` (`applyDefaultContainerId`, via `applyUserBuDefaults:374`) | stamps the acting user's BU container onto the new row — **including secure projects** | ✅ |
| W2 | `src/solutions/DocumentUploadWizard/sprk_subgrid_commands.js` | overwrites `sprk_project.sprk_containerid` with the BU container when the field is not on the form — **defeats a correct resolver after the fact** | ✅ |

**Verified current-state sequence for a SECURE project** (traced from source 2026-08-26, because the
option-B argument above depends on getting this right and an earlier draft did not):

| # | What | Where | Result |
|---|---|---|---|
| 1 | `sprk_issecure = true` set on the create payload | `projectService.ts:283-285` | flag written |
| 2 | BU cascade applied **unconditionally** — no secure suppression | `projectService.ts:291-292` | row stamped with the **acting user's BU container** (W1) |
| 3 | Provisioning creates the project's own container and **overwrites** the stamp | `ProvisionProjectEndpoint.cs:690-704` | row now correct |
| 4 | Upload uses `context.speContainerId` from wizard-open; the provisioning return value is **discarded** | `CreateProjectWizard.tsx:700-704`, `:712` | **bytes land in the BU container** |

So the row ends up *correct* and the bytes end up *wrong* — which is why "a container id is set on the
project" is precisely the false positive task 047 must not accept.

Corroborating evidence from the server side: provisioning's overwrite path already logs a **Warning**
stating that the previous value *"was cascaded from the creating user's business unit and is shared
storage, not this project's container"* (`ProvisionProjectEndpoint.cs:691-694`). The system has been
telling us W1 is harmful in its own logs.

### 3c. Client — form-context container read (1 site, different shape)

`src/client/webresources/js/sprk_analysis_commands.js:58` reads `sprk_containerid` off the **form
context** and forwards it as `containerId` to an analysis command (`:399`). Not a BU cascade and not a
write, but it *is* a path where a container id is chosen and handed onward. Needs classifying.

### 3d. Server — strategy 2 (9 sites in 3 files; 2 routed by task 075)

| File : line | Status |
|---|---|
| `Services/Communication/IncomingCommunicationProcessor.cs:868` (attachments) | ✅ **routed in 075** |
| `Services/Communication/IncomingCommunicationProcessor.cs:991` (`.eml`) | ✅ **routed in 075** |
| `Services/Communication/CommunicationService.cs:460` | 🔲 076 |
| `Services/Communication/CommunicationService.cs:1259` | 🔲 076 |
| `Services/Communication/CommunicationService.cs:1574` | 🔲 076 |
| `Services/Communication/CommunicationService.cs:2054` (throws if unconfigured) | 🔲 076 |
| `Services/Communication/CommunicationService.cs:2146` | 🔲 076 |
| `Services/Communication/MessageAttachmentMaterializer.cs:114` | 🔲 076 |
| `Services/Communication/CommunicationService.cs:2368` | ⚠️ comment claims the legacy path "is no longer used here" while 5 siblings still read it — **do not trust the comment** |

The mechanism for all 7 remaining is the one already built and proven in 075
(`CommunicationContainerResolver` + `ResolveContainerForContentAsync`); each needs its own regarding
context resolved, which that adapter already does.

---

## 4. Findings beyond the POML

### F-5 · `AssociateToStep.tsx:154-160` is a record-aware resolver that fails OPEN

The most consequential new finding. This code already asks the associated record for its
`sprk_containerid` — and on **any** failure falls through to the BU container:

```
// Record may not have sprk_containerid field — fall through to BU
```

For a secure record whose container read fails transiently, that is a silent redirect of secure
content into the shared BU container. It is the exact fail-open shape task 075 was built to remove,
already present and already record-aware — which makes it the single highest-value site to route, and
it is **not in the POML's list**.

### F-6 · `SmartTodo/src/services/xrmProvider.ts` is a full duplicate of the canonical resolver

`getSpeContainerIdFromBusinessUnit` exists twice, at the same line number (97), in
`LegalWorkspace/src/services/xrmProvider.ts` and `SmartTodo/src/services/xrmProvider.ts`. Routing one
and not the other leaves SmartTodo's upload path unrouted, and the identical name/line makes the
duplicate easy to mistake for the same file in a diff.

### F-7 · Strategy 2 is 4.5× the POML's count

See §3d — 9 sites, not 2. `CommunicationService.cs` alone has 5. A 076 that routes only
`IncomingCommunicationProcessor` (its stated scope) would leave the **outbound** archive path writing
secure correspondence to the shared archive container.

### F-9 · CRITICAL — secure-project creation with files writes the bytes to the shared BU container

**The sharpest live fail-open on this surface.** Promoted out of §2, where it had been sitting as a
rationale bullet supporting a design recommendation — and a defect documented as an argument gets read
as an argument.

**What happens.** A user creates a secure project through the wizard and attaches files. Per the
verified sequence in §3b: the row's `sprk_containerid` is stamped from the acting user's BU (W1),
provisioning creates the project's own container and overwrites the stamp, and then the upload uses
`context.speContainerId` — resolved at wizard-open, from the BU — because
`CreateProjectWizard.tsx:700-704` consumes only `success`/`errorMessage` from `provisionResult` and
nothing between the provisioning call (`:698`) and the upload guard (`:708`) reassigns it. The upload
at `:712` therefore targets the BU container, and `createDocumentRecords` (`:726`) stamps that BU
container onto the `sprk_document` rows.

**Why it is CRITICAL rather than a bug.** SPE permissions are additive-only — inheritance cannot be
broken on an individual file — so the bytes are readable by every member of the shared BU container
and **no later permission change can retract them**. There is no per-item remedy and no repair short
of migrating the content, which is precisely the migration the build plan says does not yet need to
exist.

**Armed, not fired.** The path is live in shipped code, but the trigger has never been exercised:
zero secure projects exist in any environment (build plan §2). The window is closed by *first use*,
not by a date — which is the strongest form of the build plan's "build it right now" argument.

**What closes it.** Nothing in 075 — the seam cannot fix a call site that never asks it. Under option
**(A)** the upload asks the resolver immediately before the first byte moves, so the stale
wizard-open value stops being authoritative and F-9 closes as a consequence of the routing rather
than as a special case. Under **(B)** it closes only if the two-line provisioning-return-value fix is
applied *and* W1 is removed — two changes, on one path, leaving the other paths untouched. Under
**(C)** it cannot occur, because the client stops naming a container at all.

**Not a 075 regression.** 075 built the seam; its gate closed on the seam. F-9 is a call-site defect,
pre-existing and owned by 076.

### F-8 · Two silent-skip paths in the create-project flow (from the POML, confirmed)

`CreateProjectWizard.tsx:700-704` treats a provisioning failure as non-fatal (success-screen warning
only), and an absent `authFetch`/`bffBaseUrl` skips provisioning with **no warning at all** — while
the success screen still claims (`:816-817`) the container was provisioned. Under option (A) this
becomes safe by construction: the resolver refuses at upload because the row has no container. Under
option (B) it stays a silent hole, because there is no return value to consume when provisioning
never ran. **This is an argument for (A).**

---

## 5. What is ready to go the moment a decision lands

- The seam exists, is proven, and is exported: C# `IRecordContainerResolver` +
  `SecureContainerDecision`; TS `resolveContainerForRecord` / `decideContainer` from
  `@spaarke/ui-components`.
- The complete site list is §3 — no further discovery needed.
- The two harmful writes (W1, W2) are unambiguous and need no decision; they were left untouched only
  because shipping them alone would read as "076 landed".
- The 7 remaining server sites need no decision either — same mechanism as 075.
- Under option (A) the work is: 12 sites → fallback-only; add a resolver call at each upload point;
  delete W1/W2; route the 7 server sites; re-grep to prove no survivors.
