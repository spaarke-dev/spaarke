# Task 118 — D-1 option C: the Manage Access gate now asks the server's question, and fails closed

> **Date**: 2026-09-21 · **Spec**: FR-07 · **Owner decision**: D-1 (2026-09-19), option C
> **Siblings, both shipped the same day**: task 107 = option A (a grant with no expiry confers nothing,
> `b0e59bd86`) · task 117 = option B (the scheduled reconciliation job, `30a486f6e`)

---

## 1. The defect, stated precisely

The UI and the server asked **different questions about the same action**, with **opposite fail
directions**.

| | The server | The client (through v1.0.30) |
|---|---|---|
| Question | "Do you hold **Write** on **THIS record**?" | "May you create rows in the **`sprk_externalrecordaccess` TABLE**, anywhere?" |
| Mechanism | `DelegationRuleFilter` → `CallerRecordAccessProbe` (`RetrievePrincipalAccess`, OBO) | `context.utils.hasEntityPrivilege(…, Create, Global)` |
| Evaluated as | the caller | the caller |
| When unanswerable | **DENY** (`CallerRecordAccessProbe`: every degraded path returns `None`) | **ALLOW** (`return true`) |

Two concrete failures followed, and they are different failures:

1. **Wrong question.** A user with table-level `Create` but no `Write` on a confidential matter was
   offered Manage Access, clicked through, and was refused by the server — the affordance promised an
   action the caller could not perform, on exactly the records where that matters most.
2. **Wrong fail direction.** Whenever `hasEntityPrivilege` was unavailable or threw, the affordance
   appeared **for everyone**.

## 2. 🔴 The comment that kept it alive

Two comments asserted that `AccessGrantModal` applied "a **SECOND, defense-in-depth** gate", which is
what made the fail-open look safe. **It is not a second gate.** Verified at HEAD:

```
AccessGrantModal.tsx:501   canGrantAccess = true        ← the modal's default
AccessGrantModal.tsx:672   if (open && canGrantAccess)  ← reads the SAME prop
AccessGrantModal.tsx:1111  {!canGrantAccess ? …}        ← reads the SAME prop
TrackingFieldTrio.tsx:244  canGrantAccess !== false     ← the icon, same value, undefined ⇒ enabled
```

One value, read in three places, supplied by one host. **Two gates reading one value are one gate**, and
neither can catch the other being wrong. The conclusion the comment supported ("a fail-open here does not
bypass real authorization") happened to be true for a *different* reason — the BFF enforces regardless —
which is precisely why nobody checked. A comment that vouches for a guarantee its neighbour does not
provide teaches the next reader to discount its true neighbours too.

**All three sites are corrected** (§4). What the modal's check actually buys is stated honestly and it is
narrower: a direct component mount that bypasses the icon's disabled state still renders the
not-authorized surface instead of the grant UI. The real backstop is named: `DelegationRuleFilter`.

## 3. What shipped — the server

**`GET /api/v1/external-access/can-manage-access?recordType=&recordId=`** →
`200 {"recordId": "…", "canManageAccess": true}` or **403** with a `sdap.access.deny.delegation_*` code.

The route is registered on the `/api/v1/external-access` management group, **so
`DelegationRuleFilter` — the same filter that gates every grant, revoke, share and expiry change — runs
before the handler.** A caller without Write never reaches the handler.

**The status code IS the answer, deliberately.** The client is not handed a *mirror* of the rule that
could drift from the original; it is handed **the outcome of the rule itself**. Two gates that cannot
disagree, because there is still only one gate. The handler contains no rights logic at all — it returns
`canManageAccess: true` unconditionally, because reaching it is the proof.

Three consequences worth naming, since they are the whole safety argument:

- **Detaching the group filter** makes it answer "yes" to everyone → every client gate fails OPEN.
- **Deleting its `case` from `ResolveTargetAsync`** sends it to the default DENY branch → the affordance
  disappears for everyone.
- Both directions are pinned by `RecordAccessGateTests` (§6).

### §11 Component Justification (three questions)

1. **Existing.** `CallerRecordAccessProbe` already answers this server-side over OBO, and
   `DelegationRuleFilter` already enforces it — but **no endpoint returned the answer to a client**.
   Verified by enumerating the probe's consumers: four endpoint filters (`EntityAccessFilter`,
   `RecordRouteAccessAuthorizationFilter`, `DelegationRuleFilter`, and `ContainerDocumentAuthorizationFilter`
   which deliberately does not call it) and three endpoints (`UnsecureProjectEndpoint`,
   `ProvisionProjectEndpoint`, `InternalShareEndpoints`). None reports rights to a caller.
2. **Extension.** Considered and rejected, for a mechanical reason rather than an aesthetic one:
   - **`GET /user-shares`** (task 063) is *already* a read on this group with exactly these 200/403
     semantics, so it was the strongest candidate. Rejected because using it as the gate would run a full
     POA read plus batched `systemuser` name lookups **on every form load** to answer a boolean, and would
     tie the gate's meaning to a list endpoint whose 400/500 shapes mean other things.
   - **Every other route on the group is a WRITE**, so asking one "may I?" would mean performing the action.
   - **Host-context `RetrievePrincipalAccess`** via `context.webAPI` — note the repo already has
     `Spaarke.UI.Components/src/services/PrivilegeService.ts` doing exactly this, which the task POML's
     premise list missed. Rejected anyway, and the extra finding does not change the answer: it puts an
     authorization derivation in the client, which is the pattern this project exists to remove, and the
     BFF already holds the OBO credential. `PrivilegeService` is untouched and remains unused by this path.
   - **What IS reused, not rebuilt**: `CallerRecordAccessProbe`, `DelegationRuleFilter`,
     `GrantExternalAccessEndpoint.ResolveExplicitRoot`, `ExternalGrantRoot`. **No second
     `RetrievePrincipalAccess` caller was added** — grep evidence in §8.
3. **Cost of doing nothing.** The two concrete failures in §1, plus: without it, D-1 option C cannot be
   executed at all — removing `Create` would hide Manage Access for every user.

**Deliberately NOT returned: the rights mask.** The client needs a decision; handing it raw rights would
invite re-deriving authorization rules client-side, and would cost a **second** probe per form load (the
filter has already run one). This is a documented, narrower reading of the POML's acceptance criterion 1
("returns the CALLER's rights"): the criterion's *intent* — one record, the caller, over OBO, reusing the
probe, no second `RetrievePrincipalAccess` caller — is met more strictly than its literal wording.

### §10 Placement Justification (per `.claude/constraints/bff-extensions.md`)

**In the BFF, on the existing external-access surface.** §A/§D: it is BFF domain code over a BFF-owned
authorization rule; it requires the **caller's OBO token**, which only the BFF holds; it has no AI
coupling, adds **no package**, **no DI registration**, and **no background work**. It joins an existing
route group rather than creating one, so it inherits that group's auth policy and filter rather than
repeating them — repetition being a second place to forget. ADR-052 does not engage: there is no workload
to place, only a route. ADR-008 is honored literally — resource authorization stays in the filter; this
route reports its verdict and **is never the thing that permits a write**.

## 4. What shipped — the client

| File | Change |
|---|---|
| `src/client/pcf/TrackingFieldTrio/index.ts` | `computeCanGrantAccess()` (sync, table privilege, fail-OPEN) → `evaluateGrantGate()` (async, server's rule, fail-CLOSED) + `ensureGrantGate()` + `setGrantGate()`. Header comment rewritten. v1.0.30 → **v1.0.31**. |
| `…/Spaarke.UI.Components/src/components/TrackingFieldTrio/TrackingFieldTrio.tsx` | `canGrantAccess !== false` → **`=== true`** |
| `…/TrackingFieldTrio/types.ts` | prop doc: default inverted, and why |
| `…/AccessGrantModal/AccessGrantModal.tsx` | default `canGrantAccess = true` → **`false`**; header comment amended (§5) |
| `…/AccessGrantModal/types.ts` | 🔴 the false "SECOND, defense-in-depth" claim replaced |

**Async answer, synchronous `updateView`.** The gate resolves into control state and re-renders — the same
pattern this file already uses for `authInit` — rather than blocking first paint. The control renders
**disabled** immediately and enables only on a "yes".

**Every path that is not a 200 naming this record leaves the gate `false`**: no bound record, auth not
initialised, fetch throws, non-200 (including the filter's own 403), unparseable body, `canManageAccess`
not exactly `true`, or an answer about a record we are no longer bound to. Each logs.

**Three details that are not obvious:**

- `canManageAccess === true` **exactly**, not truthy. An answer the client does not understand is not a
  licence.
- The answer **must name the record asked about**. Closes two hazards: an in-flight answer arriving after
  the form rebound, and a proxy/cache returning another record's response.
- `updateView` re-asks **only when the bound record changed**, and **revokes the previous verdict first**.
  Without the guard, every field write would cost an OBO exchange plus two Dataverse calls; without the
  revoke, a rebind from a writable record to a read-only one would show the affordance for a render.

### The harness tension, and how it is resolved

The POML required the affordance **disabled when the rule cannot be evaluated**. A PCF harness has no
`Xrm`, no MSAL and no BFF, so it can never be evaluated there — disabling is correct, and the old comment
called this out as the reason for the fail-open.

**It does not make the component untestable, because the rendered UI is not in the PCF.** It is
`@spaarke/ui-components`' `TrackingFieldTrio`, which takes `canGrantAccess` as an **explicit prop**; its
tests pass the value they mean. The inversion changed only the **default**, and every test that needs an
enabled icon now says `canGrantAccess: true` — which is strictly better, because a test inheriting the
value from a default is exactly what let the retired fail-open pass review. The only thing a harness loses
is an *enabled* grant icon: an icon that, in a host with no BFF, could not have completed a grant anyway.

## 5. The third comment — a tension the POML did not list

`AccessGrantModal.tsx:82-93` said the modal **"MUST NOT try to predict that outcome client-side (no
privilege pre-check, no hiding the button) — server truth only."** Read literally that forbids this task.

It does not, and the comment now says so precisely: task 118 **sharpened** the rule rather than relaxing
it. *Asking* the server the same question ahead of time is server truth; *guessing* from a table-level
`hasEntityPrivilege` was not. The prohibition on re-deriving the rule client-side stands.

## 6. Tests

**`tests/integration/auth/UnifiedAccessControl/RecordAccessGateTests.cs` — 14 tests** (ADR-038 KEEP path
#1, authorization behaviour). Every negative has a positive twin differing **only in the caller's rights**
— without the pairing, a 403 assertion passes equally against a route that denies unconditionally, against
the filter being detached and something else refusing, and against the route not existing.

| Property | Test |
|---|---|
| Write ⇒ 200 + `canManageAccess` + the record named (×3 root types) | `…ForCallerWithWriteOnTarget_AnswersYesForThatRecord` |
| Read-only ⇒ 403 `delegation_write_required` | `…ForCallerWithoutWriteOnTarget_IsDenied` |
| **Every right EXCEPT Write** ⇒ still denied — the retired gate's exact defect | `…ForCallerHoldingEveryRightExceptWrite_IsStillDenied` |
| Probes the right record + entity set (×3) | `…ChecksWriteOnTheRecordTheClientNamed` |
| Rights check throws ⇒ 403, not 200 | `…WhenTheRightsCheckThrows_IsDeniedNotAllowed` |
| No bearer token ⇒ 403 | `…WithNoBearerToken_IsDenied` |
| No resolvable record (×3 shapes) ⇒ 403 `delegation_target_unresolved` | `…WithNoResolvableRecord_IsDeniedByAuthorization` |
| No credential ⇒ 401 | `…WithNoCredential_Is401` |

**Client (jest, `@spaarke/ui-components`)** — the task-114 lesson applied: the code and the tests changed
together, since a green suite after changing only one side proves nothing.

- `TrackingFieldTrio.toolbar.test.tsx:124` — *"defaults the person icon to enabled when `canGrantAccess`
  is omitted"* → **`disables the person icon when canGrantAccess is omitted (fail closed)`**, and it now
  also asserts no dead click. **Paired** with `enables the person icon only when canGrantAccess is
  explicitly true` — a disabled-by-default assertion alone would pass against a component that disabled
  the icon for everyone, which is total loss of the affordance, not a fix.
- `leaves the email icon enabled when canGrantAccess is omitted` — the email icon is gated independently
  and must not become collateral damage. The `false` case was already covered; **omitted** is the state
  every un-wired host now lands in.
- `AccessGrantModal.test.tsx` — new `renders the not-authorized state when canGrantAccess is omitted (fail
  closed)`; the `describe` renamed off "defense in depth" onto the property it actually has.
- Three `makeProps` factories now state `canGrantAccess: true` explicitly, each with the reason.

**Test scope justification** (acceptance criterion 15): no near-identical happy-path variants were added.
The three `[InlineData]` root types in the two server theories are not duplicates — each pins a *different*
entity-set mapping, which is the thing that would silently authorize the wrong record.

## 7. Perturbation (mandatory) — 6 properties, both directions

Files backed up to a scratchpad copy and restored from it; **never `git checkout` / `git stash`**. All five
restored files re-verified **byte-identical by md5**, and a repo-wide grep confirms no `PERTURBATION` marker
survived. Baselines: server gate **14/14**, jest **73/73**.

| # | Property broken | Result |
|---|---|---|
| 1 | `.AddDelegationRuleFilter()` **detached** from the group | **10 of 14 gate tests fail** (35 of 54 with the delegation suite). The 4 survivors are the three Write-positives and the 401 — correct: neither depends on the filter *denying* |
| 2 | `case RecordAccessGateQuery` **removed** from `ResolveTargetAsync` | **9 of 14 fail — including all three positives.** This is the direction that would hide the affordance from everyone, and it is caught |
| 3 | Handler returns `CanManageAccess: false` | **3 fail** — exactly the positives |
| 4 | Handler echoes `Guid.NewGuid()` instead of the record asked about | **3 fail** — the "names the record" assertion, which is what lets the client discard a stale answer |
| 5 | `grantEnabled` reverted to `canGrantAccess !== false` (the retired fail-open) | **1 fail** — `disables the person icon when canGrantAccess is omitted` |
| 6 | `AccessGrantModal` default reverted to `canGrantAccess = true` | **1 fail** — `renders the not-authorized state when canGrantAccess is omitted` |
| — | **All restored** | server **14/14**, delegation suite **54/54**, jest **73/73** |

**Why 1 and 2 are the pair that matters.** They are the two ways this endpoint's meaning can silently
invert, and they fail in *opposite directions* — #1 says yes to everyone (the defect task 118 closed), #2
says no to everyone (the defect the code-before-config ordering exists to prevent). A test suite that
caught only one of them would leave the other as a one-line change away from a silent regression.

## 8. 🔴 The operator step — removing `Create`, which this task did NOT perform

**`SDAP User (Core)` appears in exactly ONE file in the repo — `entity-schema.md` — which documents it.
There is no solution XML, role definition or export to edit.** Removing `Create` is a Dataverse
security-role change performed by an operator, and this task deliberately performs **no role change**.

### PRECONDITION — binding, and its failure is silent

> **The code in §3 and §4 must be DEPLOYED — BFF *and* PCF v1.0.31 — before `Create` is removed from any
> human role.**

Reversed, the deployed client still gates on the privilege, `computeCanGrantAccess` returns `false` for
every user, and **Manage Access disappears for everyone** — while the BFF's app-only writes keep
succeeding, so nothing fails loudly and no alert fires. This is why the owner's D-1 text put the ordering
first, and why it is not a preference.

### Runbook

1. **Verify the BFF is deployed** — `GET /api/v1/external-access/can-manage-access?recordType=project&recordId={a project you can write}` returns **200** `{"canManageAccess":true}`. A **404** means the BFF is not deployed yet: **STOP**.
2. **Verify the PCF is deployed** — open a record carrying `TrackingFieldTrio` and confirm the footer reads **v1.0.31** (hard-refresh, `Ctrl+Shift+R`; the version footer exists for exactly this check). Anything lower: **STOP**.
3. **Verify the gate is live, in both directions** — as a user **with** Write, the person icon is enabled; as a user with **Read only**, it is disabled with the tooltip "You do not have permission to grant access". If the icon is enabled for the read-only user, the old bundle is still cached: **STOP** and re-check step 2.
4. **Remove the privilege** — in each human security role holding it (starting with `SDAP User (Core)`), set **Create = None** on `sprk_externalrecordaccess`. Leave `Read` and `Write` as they are. **Do not touch `SDAP Admin`, `System Administrator` or `System Customizer`** — administrative repair of grant rows must stay possible.
5. **Verify nothing regressed** — as a non-admin with Write on a record: the person icon is still enabled, the modal opens, a grant still succeeds (the write is app-only and never used the caller's `Create`), and a revoke still succeeds.
6. **Verify the ingress actually closed** — as the same non-admin, attempt to create an `sprk_externalrecordaccess` row directly (Advanced Find / a personal view / the Web API). It must be refused. This is the *point* of the removal, not a side effect: it is the path by which undated rows — which confer **nothing** since task 107 — entered the table.

**Rollback**: re-grant `Create` on the role. No data change is involved, and the v1.0.31 client does not
consult the privilege either way, so rollback restores the prior state exactly.

### Escalation trigger 1 — swept, and it did NOT fire

> *"If anything OTHER than this UI gate reads `Create` on `sprk_externalrecordaccess` — another control, a
> form script, a flow, a view — removing it is no longer safe in one step."*

| Surface | Evidence (2026-09-21) |
|---|---|
| `hasEntityPrivilege` anywhere in `src/` | **Zero call sites** after this change (remaining hits are prose describing what was removed) |
| Client `createRecord` on the table | **Zero** (`grep "createRecord\s*\(\s*['\"]sprk_externalrecordaccess"` → no matches) |
| Dataverse plugins | `src/dataverse/**` → **zero** references to the table |
| Solution XML / ribbon rules / flow definitions | **Zero** `.xml` / `.json` in the repo reference the table |
| Remaining client references | All **reads** — `external-spa` OData filters (Power-Pages-era), and the PCF's own `retrieveMultipleRecords` |

**One non-code dependency, recorded rather than treated as a blocker**: business rule 4 in
`entity-schema.md` notes that rows *can* be created outside the BFF "because users hold Create on this
table" — by hand, via the Web API, a flow or an import. Removing `Create` closes that ingress. That is a
**benefit**, not a regression: it is the same hole task 107 had to close from the read side. No product
capability depends on it, since every grant the product writes is written app-only by the BFF.

## 9. Cross-project coordination — `code-quality-and-assurance-r3`

That project's `workstreams/pcf-controls/design.md:180` records:

> *"fail-open is the documented availability fallback with server-side BFF enforcement behind it. D7-01
> TESTS the behavior; switching to fail-closed is an owner-input behavior change outside this workstream."*

**This task IS that owner-input change** (D-1, 2026-09-19). **D7-01 as written is superseded.** If it is
authored later against fail-OPEN it will pin a retired rule — task 114's exact defect
(`InternalUserShareContractTests.cs:94` asserted a 422 the owner had retired).

That project is **initialized but not executing** (`projects/INDEX.md:53`, last commit 2026-08-06), so no
such test exists yet and there is no merge conflict today. `/conflict-check` will not surface this:
`src/client/pcf/**` is **not** on its hot-path watchlist (columns are BFF / SpaarkeAi / CI /
skill-directives), which is why the coordination is carried here rather than by tooling.

**Action for whoever executes that workstream**: D7-01 must be authored against **fail-CLOSED**, and the
canonical assertions already exist — reuse `TrackingFieldTrio.toolbar.test.tsx`'s paired cases rather than
writing a third opinion.

## 10. Exact numbers, and what could NOT be run

| Measurement | Before | After |
|---|---|---|
| `Sprk.Bff.Api` build | 0 errors, 0 warnings | **0 errors, 0 warnings** (`TreatWarningsAsErrors=true`) |
| `Sprk.Bff.Api.Tests` | 12,575 passed / 0 failed / 58 skipped / 12,633 | **12,589 / 0 / 58 / 12,647** (+14 = exactly this task's tests) |
| `Spaarke.ArchTests` | 323 / 0 | **323 / 0** — after updating the endpoint-file census (§10a) |
| `@spaarke/ui-components` `tsc --noEmit` | clean | **clean** |
| jest (TrackingFieldTrio + AccessGrantModal) | 70 passed / 70 | **73 / 73** (+3: 2 toolbar, 1 modal) |
| Vulnerable packages | — | **none** (`dotnet list package --vulnerable --include-transitive`) |
| New package references | — | **zero** |

### 10a. The endpoint-file census fired, and that is it working

`RouteAuthorizationGuardTests` pins the count of BFF files registering routes, so a new endpoint file
**fails the build until it is classified**. Adding `RecordAccessGateEndpoint.cs` took it 119 → 120 and
reddened ArchTests on the first run. Classified per the maintenance procedure, exactly as task 063 did:
the route serves neither document metadata nor file bytes, so **no `GovernedFiles` entry is needed** — the
count moves and the change log records why. This is the third time this census has caught a route surface
at review time rather than in production.

### 10b. What could NOT be run — named

- **PCF typecheck / `npm run build:prod` / `npm run lint` for `TrackingFieldTrio`.** The control has **no
  `node_modules`**, and `src/client/pcf/node_modules` (which exists) does not resolve
  `@fluentui/react-components`, `@spaarke/auth`, `@spaarke/ui-components` or the generated
  `ManifestTypes`. `npx tsc --noEmit -p tsconfig.json` from the PCF root yields **290 errors across every
  control** — 162 of them `TS2307 Cannot find module` — i.e. the check fails identically on files this
  task never touched, so it is evidence in neither direction. All 15 errors in `TrackingFieldTrio/index.ts`
  are of that class or implicit-`any` downstream of it. **The PCF host file is reviewed but not
  machine-verified. It must be built with `npm run build:prod` before deployment** (root CLAUDE.md §12 —
  never `npm run build`).
- **The four `<ui-tests>` in the POML.** They require a deployed control in a live MDA and a second
  read-only test user; no browser session is available here. The three functional ones are covered offline
  by the server pairs (§6) and the client pairs (§6); the **dark-mode** check is covered by the existing
  `adr-021-dark-mode` jest case, which renders the toolbar under `webDarkTheme`, and no color was
  hard-coded by this change (the disabled state is native Fluent `disabled`).
- **Publish size.** See the task report — same structural blocker as task 117 §11 (a `git worktree` is
  created from a *commit*, and this work is uncommitted; committing is outside this session's remit).

## 11. Deliberately NOT done

- **No role change.** §8 — operator action, gated on deployment.
- **No rights mask in the response.** §3 — decision, not rights.
- **No change to `PrivilegeService.ts`.** It is unused by this path; deleting it is out of scope and would
  be a separate §11 judgment.
- **No `sprk_name` composition.** §12 — a real finding, but a write-path change outside this task.
- **No second `RetrievePrincipalAccess` caller.** The filter's single probe is the only one.
- **No change to server enforcement.** `DelegationRuleFilter` still gates every external-access write; the
  new route can never permit one.

## 12. One finding this task surfaced and did not fix

`entity-schema.md` business rule 2 prescribed generating `sprk_name` as `"{Contact} → {Project}"` **via a
pre-create plugin**. Both halves are wrong:

- **The mechanism is banned** — D-1 ruled plugins out repo-wide. This was the last doc in the repo still
  prescribing one for this table, and a plugin is exactly the shape a reader reaches for on noticing a
  column something must populate.
- **Nothing composes the name.** `GrantExternalAccessEndpoint.BuildGrantPayload` sets the root
  `@odata.bind`, `sprk_accesslevel`, `sprk_granteddate` and optionally `sprk_Contact`, `sprk_GrantedBy`,
  `sprk_expiresdate` — **not `sprk_name`**. No other writer sets it. The rule has described an intention,
  not a behaviour, for as long as it has existed.

Impact is cosmetic — `sprk_name` is the primary name column, so it is what an MDA grid or lookup shows for
a grant row; no code path reads it. Recorded in the doc, **not fixed**: composing it is a write-path change
outside this task's scope. (Also corrected there from live metadata: `sprk_name` is `NVARCHAR(850)`, not
the 200 the document's field and index tables claim.)

## 12a. Step 9.5 quality-gate findings (both fixed in-session)

| # | Gate | Finding | Disposition |
|---|---|---|---|
| 1 | code-review (naming) | `RecordAccessGateEndpoint.HandleAsync` was **not async** — it returns `IResult`, not `Task<IResult>`. The sibling it was modelled on (`InternalShareEndpoints.ListAsync`) genuinely is async; copying the suffix without the signature tells the next reader to await something that never yields. | **Fixed** — renamed to `Handle`, matching `ExternalUserContextEndpoint.Handle`, the existing convention for a synchronous handler. |
| 2 | code-review (correctness — **fail-closed hazard in my own code**) | The client compared the record id it ASKED about with the one the server ANSWERED about using raw `toLowerCase()` equality. The two ends do not agree on GUID formatting: `Xrm`'s `getId()` yields `{ABC…}`, .NET's `ToString()` yields `abc…`. A braced id would have made the comparison false **for the right record** — and because this check fails CLOSED, the symptom would be "Manage Access is gone for everyone", which looks exactly like a broken gate rather than a formatting mismatch. | **Fixed** — added `normalizeRecordId()` (trim, strip braces, lowercase), applied to both sides, with the reasoning inline. Worth recording: this is the *same* class of silent, total-loss-of-affordance failure that the code-before-config ordering exists to prevent, arriving by a different route. |

**adr-check: clean.** Verified by grep on the changed files — ADR-001 (Minimal API, no controllers),
ADR-003 (fail-closed, which is the task), ADR-007 (no Graph), ADR-008 (authorization stays in the endpoint
filter; the new route is never an enforcement point), ADR-009 (no `IMemoryCache`), ADR-010 (zero new
interfaces, zero new DI registrations), ADR-012 (the modal still receives a boolean; no entity name, record
id or probe call entered the shared component), ADR-013 (no AI types), ADR-021 (Fluent v9, no hard-coded
colors; the two hex/`createRoot` grep hits are both prose), ADR-022 (`ReactDOM.render` untouched),
ADR-028 + A4 (`authenticatedFetch` only — no raw `fetch`, no bearer handling, no `accessToken` prop, no
`.WithClientSecret`), ADR-038 (KEEP path `tests/integration/auth/**`; no `Mock<HttpMessageHandler>`, no
DI-registration or ctor-null tests, no reflection, no `Stopwatch`/`DateTime.UtcNow`/`Task.Delay`;
`{Method}_{Scenario}_{ExpectedResult}` naming throughout), ADR-052 (no background work added).

**One §10 item is NOT satisfied and is reported as such: publish size** — see §10b. No CVE (`dotnet list
package --vulnerable --include-transitive`: none), zero new package references.

**Observation, out of scope, not fixed**: `Spaarke.UI.Components/src/services/PrivilegeService.ts` issues a
raw `window.fetch` to the Dataverse Web API with `credentials: 'same-origin'`. It is pre-existing, unused by
this path, and untouched by this task — but it is the client-side authorization derivation this project
exists to remove, and it is the natural thing for someone to reach for next time. Flagged for whoever owns
the PCF-controls cleanup.

## 13. Premise rot found at HEAD (this project's 20th instance — the code won again)

| POML premise | At HEAD, 2026-09-21 |
|---|---|
| gate at `index.ts:462-473`, `return true` at `:472` | **`:479-489`**, `return true` at `:489` — task 065 shifted it |
| comments at `index.ts:459-460` + `AccessGrantModal/types.ts:144` | **`index.ts:42-46` + `:472-478`** and **`types.ts:175-183`** |
| back-reference at `index.ts:869` | **`:937`** |
| "on the client side, `hasEntityPrivilege` is the only existing mechanism" | **False** — `Spaarke.UI.Components/src/services/PrivilegeService.ts` already calls `RetrievePrincipalAccess` host-context. Does not change the decision (§11 q2), but the premise was wrong |
| Step 5: "business rule 4 no longer says expiry is enforced NOWHERE" | **Already corrected** by task 107 earlier the same day. Only a forward-reference to the `Create` removal was added |
| `AccessGrantModal.tsx:232 / :313 / :630` | **`:501` / `:672` / `:1111`** — same mechanism, same conclusion |

The *mechanisms* were all confirmed; only line numbers and one "only existing mechanism" claim were stale.
