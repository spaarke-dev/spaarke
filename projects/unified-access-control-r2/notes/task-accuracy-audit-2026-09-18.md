# Task-accuracy audit — 2026-09-18 (session 15, owner request)

> **Owner directive**: *"proceed with next tasks — BUT audit to ensure the tasks are accurate and match
> the current status."* Two read-only Opus agents, ~500K tokens of verification. Nothing was edited by
> either; every claim below was checked against code, commits or Microsoft Learn, and everything they
> could **not** determine is named as such rather than guessed.
>
> Two distinct failure modes were in scope, both of which have bitten this project:
> **premise rot** (a task's factual claims stop matching the code — 19 prior instances) and
> **scope rot** (work described as outstanding has since shipped — task 094 was ~80% delivered).

## Headline

| | |
|---|---|
| Status drift | **CLEAN** — all 26 open POML `<status>` values agree with their index markers; `check-task-status-drift.ps1` rc=0, 105/105 |
| Task files with a wrong load-bearing sentence | **8** |
| Runnable right now (all deps ✅) | **082, 093, 094, 095, 044, 065, 108** (+107 once rescoped) |
| Unnecessarily deferred | **044** and **065** — see §5 |
| Outright obsolete tasks | **none**. Obsolete *fragments* only, in 093 and 082 |

---

## 1. Task 107 — NEEDS RESCOPE (the gap is real; the mechanism menu is not)

**Still needed, and nothing has closed it.** `sprk_expiresdate` appears in zero schema/deployment scripts,
no solution XML, and no workflow artifact; `git log -- src/dataverse` shows nothing since task 083. This is
**not** a 094-style "already shipped" case — there is no partial implementation to reconcile. Deps ✅ (097
complete; its default, its 118/118 run and its live backfill 25 → 0 all landed).

### 🔴 Two premises are provably wrong

1. **Mechanism option (a) — an entity-scope business rule — cannot work.** Business rules "are run on
   clients when a form is opened… they aren't executed inside Dataverse", and date arithmetic (`DateAdd`)
   belongs to **formula columns**, not a Set-Field-Value action. A business rule can neither compute
   today+90 nor see a Web API create. So one of the POML's two named mechanisms covers **zero** API write
   paths.
2. **ISS-009's own suggested fix — "make the column required" — is insufficient alone.** *"Dataverse
   doesn't return an error when a column with `ApplicationRequired` applied doesn't have a value"*; only
   `SystemRequired` is enforced, and custom columns cannot use it. The POML's step-0 hypothesis that this
   might be "schema-only" is therefore a dead branch — now proven rather than assumed.

### 🔴 Its reference implementation is the repo's worst ADR-002 offender

The POML points at `BaseProxyPlugin.cs` as the pattern to copy. That file is
`[Obsolete("Violates ADR-002: makes HTTP calls, uses Thread.Sleep…")]`. `src/dataverse/plugins/` holds
**three `.cs` files total**, all part of one HTTP-proxying Custom API. **There is no thin pre-operation
plugin anywhere in this repo to copy.**

### 🔔 OWNER DECISION REQUIRED — the first ADR-002 exception in this repo

A pre-operation plugin step is now the **only** viable in-transaction mechanism. But ADR-002 states
*"use of plugins requires explicit ADR exception approval"* (+ "< 200 LOC"), and a repo-wide search found
**no precedent of such an exception ever being granted**. Under CLAUDE.md §6.5 this is a path-A/B decision
the POML never surfaces. 107 cannot start until it is answered.

### The test plan does not fit the build topology

The plugin project is **net462**. `ADR002_PluginTests` resorts to *source-file scanning* for exactly this
reason: the plugin assembly *"cannot be loaded as a ProjectReference in this net8.0 test project."* And
`ci-tier1-blocking.yml` states the plugin is *"deliberately NOT in Spaarke.sln… If a net4x project is ever
ADDED to the solution, this job will fail on ubuntu."* So `<output type="test">tests/ (KEEP path)</output>`
is unqualified and unbuildable as written. Either factor the date computation into a **netstandard2.0**
assembly the net10 tests can reference (then `tests/unit/domain/**` is legitimate), or state explicitly
that the plugin test project stays **out of the solution** and runs manually.

### A consequence the POML never mentions

Task 100's reminder job selects only grants whose expiry falls from today through 30 days out. So an
externally-created **undated** row is **unmonitored as well as unbounded** — invisible to reminders, not
merely unexpiring. Good material for the cost-of-doing-nothing justification.

### Smaller corrections

- **Doc staleness is at `entity-schema.md` :51 AND :220**, not only :220 as the POML says. Line 51 repeats
  the same falsehood ("expiry is NOT ENFORCED anywhere… filters on `statecode` only") and contains a
  self-nullifying correction: *"the live field is `sprk_expiresdate`, NOT the originally documented
  `sprk_expiresdate`"* — identical names. Fix both.
- **Pin "today" semantics**: the plugin must use the sandbox's UTC date and write midnight/`Unspecified`,
  matching `ExternalGrantLifecycle.ToSdkDateOnly`. The column is `TimeZoneIndependent`, so a `Local`-kind
  value can land on the neighbouring date.
- **Record the rejected alternative** the POML omits: a Dataverse **webhook/Service Endpoint** to the BFF
  (precedent exists — HMAC-signed webhooks via `WebhookSignatureFilter`). Reject it *for this purpose*
  because it is **out-of-transaction**: the row is live-and-unbounded for the gap, and the read filter
  treats an undated row as never-expiring.
- **Make the mechanisms additive, not alternatives**: `ApplicationRequired` for the MDA form/quick-create
  path **and** the plugin for API/flow/import paths. Requirement level alone isn't enforced; the plugin
  alone leaves form UX silently optional.
- Keep unchanged: the goal, the "`eq null` stays" constraint, the operator-step-only scope, the
  perturbation requirement, and the acceptance criteria — all sound.

### Could not determine (107)

- **Live requirement level of `sprk_expiresdate`** — the `dataverse` MCP server failed to connect
  (`CONNECTION_CLOSED`) this session, and the `Required = No` claim rests on a doc whose adjacent sentence
  is demonstrably stale. Step 0 must re-read live metadata.
- **Whether any user actually holds Create** on the table — role configuration, not code. (A privilege
  *check* does exist, `TrackingFieldTrio/index.ts:455-467`, and it is **fail-open** if the API is
  unavailable.)
- **Whether a pre-op step fires on every import path** (Import Data wizard, `ExecuteMultiple`/bulk,
  data-migration mode). This is precisely what 107's own escalation trigger names.

---

## 2. Open-task triage — 26 open, no status drift

Verdict key: **OK** = accurate as written · **RESCOPE** = wrong sentence(s) to fix first ·
**NEEDS-OWNER** = blocked on a human decision.

| Task | Deps met? | Spot-checked claim | Verdict |
|---|---|---|---|
| 036 | ❌ 034 blocked | cites `Sprk.Bff.Api/appsettings.json` — **no such file** | RESCOPE (minor) |
| 044 | ✅ **all met** | "alongside the existing seam suites" ✅ | **OK — runnable; index defers it behind 036 needlessly** |
| 047 | ❌ 093 | live-dev container assertions — undeterminable (MCP down) | OK (blocked) |
| 054 | ❌ 036 | 11/11 paths ✅, but goal still asserts the service-request 4th root | NEEDS-OWNER |
| 055 / 056 / 057 / 058 | ❌ | 9/9, 9/9, 7/7 ✅; 058 cites a downstream output as a reference | OK / NEEDS-OWNER (055 inherits 054) |
| 064 | ❌ 105 | "no report endpoint exists" ✅ confirmed absent | OK |
| 065 | ✅ **met (063)** | `TrackingFieldTrio/index.ts` ✅ | **OK — runnable; unblocks 066/067/069/099** |
| 066 / 067 | ❌ | cite "the frozen DTO" — 064's unbuilt output | OK (cosmetic) |
| 069 | ❌ 064 | 6/6 ✅ | OK |
| 082 | ✅ **met** | `CallerIdentityGuardTests.cs` ✅ exists with both rules | **RESCOPE — see §3** |
| 087 / 088 | ❌ | 10/10 and 8/8 ✅ **but** cite **080/081** for what is now 086/087 | RESCOPE (numbering) |
| 089 / 090 | ❌ | downstream-output references, benign | OK |
| 093 | ✅ **met (076)** | "orders upload → create → link" — **❌ FALSE** | **RESCOPE (major) — see §3** |
| 094 | ✅ **met (076)** | all 6 cited commits ✅; `ListChildrenAsync` ✅ | OK + one addition (§3) |
| 095 | ✅ none | alt key ✅ real; `sprk_todo` lookup claim **❌ half-wrong** | OK with fix (§3) |
| 099 | ❌ 065,066 | M8 still unfixed at `AccessGrantModal.tsx:334` ✅ | OK (well-maintained) |
| 101 | ❌ 107 | benign | OK |
| 105 | ❌ 036 | `:1034` region ✅ consistent (line drifted) | OK |
| 108 | ✅ **met (063)** | `UnsecureProjectEndpoint.cs` + reason codes ✅ | **OK — runnable now** |
| 034 | 🟡 blocked | tripwire scans `appsettings*.json` ✅ | NEEDS-OWNER (A vs B) |

---

## 3. Ranked rewrite list — the exact wrong sentence in each

1. **093 — four false premises; the first would send an implementer to redo shipped work.**
   - ❌ *"`EntityCreationService.ts` orders the flow **upload → create → link**. At upload time there is no
     record…"* — **inverted by task 076.** The file's own header now reads *"Create the record FIRST — the
     upload is keyed on it… There is no container parameter."* `applyDefaultContainerId` was **deleted**.
   - ❌ **Acceptance criterion 1 is already met** — all five uploading wizards pass `(entity, recordId)`.
   - ❌ **The isolation gap it exists to close is already closed for projects**:
     `CreateProjectWizard.tsx:690` provisions if secure, `:736` uploads, and `:726-731` says so, naming
     F-9 closed. *Today's order **is** the POML's target order.*
   - ❌ *"it exists in TWO implementations"* — only **one** `SecureProjectSection.tsx` exists. **Task 068
     already disproved this** by building the Vite bundle and grepping `dist`. So escalation trigger 1 is
     unfireable and the "open question to SURFACE" is answered. 068 is also `completed`, not pending.
   - ❌ Cites `tasks/076-record-keyed-upload-contract.poml` — real name
     `076-route-callsites-through-resolver.poml`.
   - ✅ **Genuine remaining scope**: move the IsSecure collection point ahead of the Info step; secure
     **matter** provisioning (owner deferred matters, `provision-project` is project-only); and the status
     of the three non-uploading wizards. Note "7 Create wizards" matches nothing verifiable — **8** dirs
     exist, **5** upload. Rescoping 093 also pulls **047** forward.
2. **082 — the header is right; the rest of the file was never updated.** `<step order="0">` still makes
   *"Confirm PR #832 and the ten worktree branches have merged… a hard gate, not a preference"* blocking —
   and #832/#840 **have** merged. Criteria 1/2/7 still demand the retired second ratchet, `<outputs>` lists
   `CallerIdentityCensusTests.cs`, and `<deps>` still lists `PR-832`. Its census is stale too: "71 files,
   ~40 unaudited, ~10 further filters" re-measures at HEAD to **59 files, 6 filters, and zero outside a
   5-entry allowlist**. Worth noting while deciding: the sibling's guard already asserts *"the BFF has
   exactly one identity primitive"* and allowlists two files as *"Owned by unified-access-control-r2"* — so
   082's remaining job is **ratification in writing**, not discovery.
3. **054 (and 055/056 by inheritance)** — `<goal>` still asserts *"CallerPrincipal exposes an accessible
   service-request set…"*, contradicting task 028's ruling. Only `<notes>` carries the amendment.
4. **087 / 088** — systematic pre-renumbering references: `<gate>` *"requires 080 (table)"* / *"requires 081
   merged"*, `<goal>` *"the 080 vocabulary"* / *"emitted in 081 events"*, and `<dependency task="080|081">`.
5. **036** — `<file role="modify">…/appsettings.json` and *"document both states in appsettings.json
   comments"*: that file does not exist (only `appsettings.Testing.json` + three `.template`s).
6. **095** — ❌ *"`sprk_document` has NO lookup for `sprk_todo`"*: `Models.cs:214` registers
   `sprk_relatedtodo → sprk_todo`. Truthful form: *no **bare** `sprk_todo` lookup, but `sprk_relatedtodo`
   exists.* This matters because the claim is the stated **argument for** the intersection entity's shape.
   Also minor: Document→WorkAssignment is a **pair**, not one.
7. **094** — add before execution: `DriveItemOperations.ListChildrenAsync` is **drive-keyed**, but under
   076/083 the container is server-resolved from `(entity, recordId)`. A drive-keyed probe would
   reintroduce the client-supplied-container shape 083 deleted, and
   `SpeWriteSinkContainerProvenanceGuardTests` counts `ClientSupplied = 0`. **The probe must be
   record-keyed.**
8. **058 / 065 / 066 / 067 / 089** — `role="canonical-reference"` notes files that are actually downstream
   *outputs*; 066 calls one *"the frozen DTO"*. Cosmetic, but each reads as an existing artifact.

---

## 4. Owner-decision sweep — where the stale surface actually is

| Decision (2026-09-18) | Where the work actually lives |
|---|---|
| **External licensed user = internal licensed user** (task 063) | **No open POML asserts the refusal.** The stale surface is *shipped code + an asserting test*: `InternalShareEndpoints.cs:80` emits `user_not_internal`, `SystemUserIdentityResolver.cs:57` carries a doc comment justifying it, and `InternalUserShareContractTests.cs:94` **asserts the 422**. 063 is complete → this is **new unfiled work**, not a POML edit. ⚠️ The test encodes the old rule, so it must change *with* the code, not after. Nearest open consumers: **065** (the "+ User" picker) and **069** (Phase-4 seam tests). |
| **Renewal keeps the previous level** (ISS-023 / #1002) | **No open task owns this.** Today `GrantExternalAccessEndpoint.cs:248-255` sets `levelChanged = survivor.AccessLevel != requestedLevel` and writes the **requested** level — a re-grant *sets*. ⚠️ **099 is the renewal UI** and its criteria say nothing about level, so as specified it would present a date picker while the server silently changes the level. Needs filing + a constraint on 099, and a look at **101**'s views. ⚠️ Also: the rule cannot be "always `max(requested, existing)`" — that would make **downgrades impossible**, and task 063's `share-user` path exists to downgrade (the pending real-Dataverse DOWNGRADE check is about exactly that). |
| **ADR-038 ban B8 amendment** (W5) | **Zero open POMLs cite B8 or `InternalsVisibleTo`** (all 25 checked). Citations live only in completed POMLs and one TASK-INDEX narrative line. The amendment creates **no open-task rewrite work**. |

---

## 5. Sequencing observations

- **Runnable right now**, all deps complete: **082, 093, 094, 095, 044, 065, 108** — plus 107 once rescoped
  and the ADR-002 exception is decided.
- **044 is unnecessarily deferred.** Its deps (039/041/042/043) are all ✅ and the index itself notes it
  "excludes FR-20 by design", yet wave 2 places it behind owner-blocked **036**.
- **065 is unnecessarily deferred.** Its only dep (063) is ✅, and pulling it forward unblocks the
  066 → 067 → 099 chain **plus 069**. 099's escalation has already fired *waiting on 065's M8*.
- No task is obsolete. The obsolete **fragments** are 093's step 1 + escalation trigger 1, and 082's
  criteria 1/2 + step 0.

## 6. Could not determine — named, not guessed

- **047's live assertions** (three projects carrying the root BU's container id; zero secure projects) — dev
  tenant only, and the `dataverse` MCP server failed to connect this session.
- **093's "7 Create wizards"** — 8 `Create*Wizard` directories exist, 5 upload; which 7 is meant is
  unresolvable from the repo.
- **095's live relationship cardinality** — corroborated in `Models.cs` pairs, but its step 0 demands live
  metadata.
- **105's `:1034`** — line numbers drifted; the region is consistent with the claim but not the same line.
- **036's tripwire vacuity** — whether the `appsettings*.json` glob actually matches the `.template` files,
  and so whether the flag default is pinned in a checked-in file at all.
