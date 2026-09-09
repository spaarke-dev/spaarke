# Task 068 — Create Project wizard Secure step: copy rework (FR-31)

**Date**: 2026-09-09 · **Task**: `068-create-project-wizard-secure-step.poml` · **Spec**: FR-31 · **Design**: §5.1, §6.1

This note exists for two reasons: to preserve the retired copy verbatim (the component deliberately
does **not** reproduce it, so the FR-31 grep gate stays meaningful), and to record the
CloseProjectDialog verdict step 4 asked for either way.

---

## 1. The component has exactly ONE implementation

The task warning about `SecureProjectSection.tsx` existing in two copies — a shared-library one and a
LegalWorkspace one — **does not hold for this component**. Verified three ways:

| Check | Result |
|---|---|
| `find src -iname "SecureProjectSection*"` | one hit: `src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/SecureProjectSection.tsx` |
| `grep -rln "Power Pages workspace is activated\|SecureProjectSection" src/` | 3 files, all inside that same shared-lib directory (the component, `CreateProjectStep.tsx`, `index.ts`) |
| Built bundle of `src/solutions/CreateProjectWizard` after the edit | `Power Pages` → **0 occurrences**; each new string → exactly 1 |

`src/solutions/CreateProjectWizard` is a thin Vite host: its `vite.config.ts` aliases
`@spaarke/ui-components` straight at the shared library's `src/`, so the shared file **is** the
shipped file. The bundle grep is the load-bearing evidence — it proves the edit reaches the artifact,
which a passing `tsc` would not have.

---

## 2. The retired copy, verbatim

Preserved here and nowhere else in the tree.

**`SecureProjectSection.tsx:162`** — third provisioning item, titled *"External Access Portal"*:

> A Power Pages workspace is activated so invited external users can access project documents and events.

**`SecureProjectSection.tsx:244–247`** — the warning `MessageBar`:

> **This designation is permanent.** Once a project is marked as Secure and created, the secure
> designation cannot be removed. Please confirm this is correct before proceeding.

**`SecureProjectSection.tsx:151`** — first provisioning item, *"Dedicated Business Unit"*:

> A Dataverse Business Unit is created to scope security roles and data access for this project.

**`CreateProjectWizard.tsx:800`** — appended to every provisioning failure:

> The project record was created but the Business Unit, SPE container, and External Access Account
> may need to be provisioned manually.

**`CreateProjectWizard.tsx:836`** — the secure success line:

> with its Business Unit, document container, and external access account provisioned.

### Why each was false

1. **The portal claim** — Power Pages is retired (SWA + CIAM). More to the point, provisioning never
   activated a portal even when it wasn't: nothing in `ProvisionProjectEndpoint` touches one.
   External participants reach a secure project through `sprk_externalrecordaccess` grants.
2. **The permanence warning** — `UnsecureProjectEndpoint` (`POST /api/v1/external-access/unsecure-project`,
   registered in `ExternalAccessEndpoints.cs:159`) reassigns the record to a named owner, revokes
   every share, and clears `sprk_issecure`. design.md §5.1 calls the designation reversible.
3. **"A Business Unit is created" / "External Access Account"** — BFF task 021 (2026-08-25) stopped
   creating both. There is ONE canonical `Secure Project` BU, **resolved by name**, never created;
   the per-project account was removed entirely. The wizard copy was never updated to match.

The fourth item — appending "the Business Unit… may need to be provisioned manually" to *every*
failure — was wrong in a second way beyond naming retired artifacts: the endpoint fails **closed**,
so most refusals mean nothing was attempted at all. The sentence told operators to go reconcile
things that had never been created.

---

## 3. What the copy says now, and what each sentence is traceable to

| Rendered copy | Server behaviour it describes |
|---|---|
| "Moved into the Secure Project business unit" / "That team has no members, so nobody reaches the project by owning it or by sharing its business unit." | `ProvisionProjectAsync` steps 2, 3, 5 — resolve the canonical BU by name from `SecureProject:BusinessUnitName`, resolve its default owner team, assign, verify |
| "Shared with you, and only with people you add" / "…needs an explicit grant before they can see it." | `ShareToCreatorAndPrincipalsAsync` (step 5.5) — creator identified from their own token via WhoAmI, granted `CreatorAccessRights`; provisioning FAILS rather than finish without it |
| "Given its own document container" | steps 6–7 — create the SPE container, record it on `sprk_containerid`, fail if unverifiable |
| "This can be undone later… returns the project to a named owner and revokes the access that securing it granted" | `UnsecureProjectEndpoint` — reassign owner → revoke every POA share → clear `sprk_issecure` |

---

## 4. Two stale premises in the POML

Recorded because both would have produced wrong code if followed literally.

**(a) "the project is owned by a secure service account".** It is **not**. design.md §5.1a
(*"Ownership: an owner TEAM, not a service account"*, decided 2026-08-25) supersedes this, and the
merged endpoint assigns to the BU's **default owner team**, which is memberless. The copy says owner
team. Following the POML would have reintroduced a mechanism the project explicitly rejected.

**(b) "The unconfigured-environment **4xx** from 061".** The environment-setup refusals are returned
as **HTTP 500**, not 4xx — the server treats a missing BU as a server-side configuration fault
(`ProvisionProjectEndpoint.cs:309, 322, 371, 382`). A client that classified on status code would
have mapped every one of them to a generic error. Classification therefore reads the `reasonCode`
ProblemDetails extension, which is the only reliable discriminator. This is pinned by a test.

---

## 4b. Finding: the client classified 8 of the endpoint's 11 reason codes, and one gap lied

Diffing `grep -o 'sdap\.provision\.[a-z_]*'` across `ProvisionProjectEndpoint.cs` against the client's
sets turned up three codes with no branch. Two are harmless; one was not.

| Reason code | Falls through to | Honest? |
|---|---|---|
| `owner_assignment_failed` | `'error'` | **Yes.** The endpoint's own detail says *"Nothing has been provisioned."* — which is what the generic copy says. |
| `owner_assignment_not_applied` | `'error'` | **Yes**, same detail, same wording. |
| `container_not_recorded` | `'error'` | **No — wrong in both halves.** |

By the time `container_not_recorded` is returned, endpoint steps 5 and 5.5 have already succeeded:
ownership moved to the secure owner team and the creator share was issued. Only step 7 failed — the
SPE container exists but is not linked. The generic copy ("The project record was created as a normal
project") would have told the user (a) the project was *not* secured, when it was, and (b) that
nothing was created, when an **orphaned container** was — the exact artifact the endpoint's detail
tells an operator to go reconcile.

Fixed by adding a `'container-not-recorded'` failure kind with copy that says the project *is*
secured but has no document storage yet. Pinned by
`provisioningService.test.ts` → *"classifies every reason code ProvisionProjectEndpoint can emit"*,
which transcribes the endpoint's `Reason*` constants and fails if a code lands on `'error'` without
having been deliberately placed there.

## 4c. Also fixed: secure requested with no BFF configured was silent

`CreateProjectWizard.tsx` guarded provisioning on `isSecure && authFetch && bffBaseUrl`. When the host
supplied no authenticated fetch or base URL, the call was skipped with no branch — and the success
screen then announced *"Secure Project created!"*, naming a designation the record did not have. Now
it emits the same class of designed message as an unconfigured environment, and the secure title is
keyed on `provisioningSucceeded` rather than on the toggle.

## 5. CloseProjectDialog verdict (step 4) — NOT wrong, no change made

Checked `CloseProjectDialog.tsx` copy against `ProjectClosureEndpoint` (FR-15) as instructed.

| Copy | Verdict |
|---|---|
| `:405` "Closing this project will permanently revoke all external access." | **Correct.** Closure deactivates active `sprk_externalrecordaccess` rows and removes external SPE members. Nothing reverses it; re-access requires re-invitation. |
| `:277` "Permanent — cannot be undone" | **Correct, and not about the designation.** This is closure, not the secure designation — FR-31's permanence gate does not reach it. The item's own body already states the nuance precisely: *"To re-enable external access, each user would need to be invited again."* |
| `:265` "All external access revoked — Every external user's participation record will be deactivated" | **Correct but narrow.** The endpoint deactivates contact grants **and organization grants** (the org half is exactly what FR-15 fixed). Organization grants carry no contact, so "every external user's participation record" under-describes the blast radius: a firm-wide grant is also deactivated. |

**No edit made.** The third row is an omission, not a misstatement, and `CloseProjectDialog.tsx` is
outside this task's declared `<outputs>`. Flagged here for the orchestrator: a one-line tightening of
that description ("…including grants issued to a whole organization") would be accurate and cheap,
but it belongs to whoever owns FR-15's surface, not to FR-31.

---

## 4d. Step 9.5 quality gates — what they found, and what was done

Both gates ran (`code-review`, `adr-check`). Neither found an ADR-021 or ADR-028 violation in the
task's own work: the auth contract is clean (injected `authenticatedFetch`, no Bearer literal, no
token prop) and `SecureProjectSection.tsx` is token-pure.

**Acted on (all inside this task's declared outputs):**

| Finding | Action |
|---|---|
| Generic failure copy asserts *"created as a normal project"* and advises *"try again"* — **false for the three SPE-container failures**, which carry **no `reasonCode` at all** and fire *after* ownership moved and the creator share was issued | Rewrote the generic and `share-failed` copy to assert no state the client cannot observe, and removed "try again" from both — a retry past the owner assignment returns 409 `already_provisioned`, a second wrong message on the same record |
| `sprk_issecure` is written `true` before provisioning and never cleared on refusal, so "normal project" is wrong about the flag too | Same rewrite; rationale recorded on `classifyProvisioningFailure` |
| `already_provisioned` copy is untrue on a retry after `creator_share_failed` (claimed, but the creator still cannot open it) | Hedged to cover both states and to tell a locked-out user to get an administrator |
| A 2xx whose body lacks `sharedToCreatorSystemUserId` was accepted as success | Fails closed now — otherwise a contract change ships as "shared with you" on no evidence |
| **WCAG 2.5.3 Label in Name**: visible label "Enabled" vs `aria-label` "Mark this project as a Secure Project" — no shared words, so speech input cannot address it. The first draft of the test *pinned* this failing shape under a heading named ADR-021 | Accessible name is now `Secure Project: Enabled/Disabled`; the test asserts the WCAG property instead of the old string |
| Disclosure was silent — no `aria-expanded` / `aria-controls`, so the corrected copy was corrected for sighted users only | Both added, with a test that also fails on a dangling `aria-controls` |
| `PROVISIONING_STEPS` has **zero production consumers** (renderer deleted 2026-09-04); this task grew it *and* added a test pinning it — an ADR-038 B6 mirror test over dead code | Test removed, with a comment saying why; constant annotated as having no consumer |
| Copy said *"That team has no members"* as fact — provisioning never asserts it; it is an environment invariant | Now *"by design has no people in it"*, with the reasoning in-code |
| *"This can be undone later"* implied self-service, but **no client surface calls `/unsecure-project`** | Now *"An administrator can remove the secure designation later"*, pinned by a test |
| Panel title *"What happens when this project is created:"* promised certainty; refusal is the normal case pre-UAT | Now *"What securing this project does:"* |
| `not.toContain('workspace')` would fail on any truthful future rewrite | Narrowed to `portal` only |

**Deliberately NOT done — and why.** Each is real; none is FR-31, and all sit outside this task's
declared `<outputs>`:

- **ADR-044 raw GUIDs in `@odata.bind` / key predicates** — `CreateProjectWizard.tsx:61, 64, 114`
  interpolate `matterId` / `projectId` / `accountId` without `cleanGuid`, which is exported from this
  very library and imported nowhere in `CreateProjectWizard/`. **This is the highest-severity finding
  in either gate**, it is pre-existing, and ADR-044's own rationale cites AP-6 — *braces in an
  `@odata.bind` broke Create Matter/Project* — i.e. two prior outages in this exact wizard. Recommend
  a follow-on task; the reviewer's judgment was that the legitimate choice is *when*, not *whether*.
- **ADR-012 platform coupling** — `CreateProjectWizard.tsx:60-78, 93-104` call the Dataverse Web API
  by raw same-origin `fetch`, bypassing the injected `IDataService`, which breaks the file's own
  "no solution-specific imports" claim for any non-MDA host. Likely a legitimate §6.5 **path A**
  exception (nav-prop metadata discovery has no `IDataService` equivalent) rather than a fix — but it
  should be *documented* as one, not left silent.
- **Missing `// Auth v2 (D-AUTH-7):` markers** on those two Dataverse-direct fetches (documentation
  half of the rule; the security half is satisfied — no Bearer literal).
- **Reciprocal server-side guard** — both reason-code parity checks (the client's set and the test's
  `emittedByEndpoint` array) are hand transcriptions and cannot observe the server. A new `Reason*`
  constant in `ProvisionProjectEndpoint.cs` still ships green here. The repo already has the right
  pattern (`tests/fixtures/compose-citation-parity/cases.json`, executed by both runtimes).
- **A server reason code for SPE-container failures** — the real fix for the C1 class. §6.5 **path B**;
  the client-side hedge above is the mitigation, not the cure.
- **Wizard-wiring test (reviewer's S14)** — two of the five retired strings lived in
  `CreateProjectWizard.tsx`, and it still has no test. `onFinish` is an inline closure inside a
  ~340-line function; covering it needs a full wizard harness that would dwarf this task. Recorded as
  a gap rather than met with a shallow test.
- **`onFinish` decomposition** — now ~340 lines with 10+ responsibilities; this task added ~35. The
  provisioning block is a clean extraction candidate per `COMPONENT-COMPLEXITY.md`. Direction is
  declining; flagged, not acted on.

## 6. Escalation trigger — evaluated, did NOT fire

The POML's trigger: *stop if the step MISSES an input 061 requires (initial share principals) and
adding it changes the wizard's step flow materially.*

`SharePrincipalIds` is **optional** on `ProvisionProjectRequest` (defaulted `= null`), and the creator
share — the mandatory half — is taken server-side from the caller's token, never from the request. So
061 requires no new wizard input, the flow is unchanged, and no new step was invented. The optional
field is mirrored in `IProvisionProjectRequest` and left unsent by the wizard, with a comment saying
so; colleagues are added afterwards through the FR-29 "+ User" surface.
