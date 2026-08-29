# Current Task State — `spaarkeai-compose-r8`

> **Last Updated**: 2026-08-29
> **Recovery**: read Quick Recovery. **#863 code-complete. Master synced. All 10 prior failures fixed.**
> Everything below "Full State" is preserved history from earlier checkpoints.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Active work** | **070 decomposition** — clusters **7 · 6 · 5b · 8** extracted & verified. **Cluster 1 is next available** (2b/2a are HELD). |
| **Next Action** | **Cluster 1 (re-anchor / stale-base)** — the executable spec is in the seam map. Treat it with more care than the first four: **76.6% branch** (they were 87–96%) and ~470 LOC over five members, so seed **several** mutations across different members, not one. Then 3 → 4 → 5a; 2b/2a only after UAC-r2 replies on #858. |
| **Branch** | `work/spaarkeai-compose-r8` · **synced with master** · `ComposeService.cs` 4,427 → **3,975** |
| **Suite** | ALL GREEN — BFF **11,619/0** · ArchTests **150/150** · `Sprk.Bff.Api.IntegrationTests` **103/0** · `Spe.Integration.Tests` **409/0** |
| **Verify with** | **`dotnet build`** at the SOLUTION root — not one project (see §A2 for why that distinction cost real time) |

---

## A1. RESOLVED — the ten failures were two causes, not three

**The 5 ArchTests**: branch staleness, as predicted. `git merge origin/master` — zero conflicts.
Master's census guard then fired correctly on task 070's split of `ComposeEndpoints.cs` into eight
files; all eight are now classified in `GovernedFiles` (one entry each, deliberately not a
`StartsWith("Api/Compose")` prefix rule, which would absorb the ninth file silently too).

**The other 5 were ONE defect** — and not the one recorded here yesterday. Every failure lasted
**~100s**, which is `HttpClient`'s default timeout, i.e. a hang. Test hosts held the REAL
`DefaultAzureCredential` from `Program.cs`, so the first request to an outbound-authenticating path
probed IMDS and blocked. The credential caches its answer, so only the FIRST caller paid — hence
rotating victims, uniform ~100s, and a test that passes in the suite but fails alone.

The recorded `SessionOwnershipFilter` hypothesis was **wrong** and could never have explained
`ScopePersonas`, a route with no session. Fixed at the fixture (§F.2), all 52 factories, guarded by
`TestHostCredentialGuardTests`. Full write-up: [`notes/test-host-credential-hang.md`](notes/test-host-credential-hang.md).

## A2. CLOSED — `Spe.Integration.Tests`: 23 → 0

That project **had not compiled since the #863 sweep**, so it had not run either; the 23 surfaced
when the compile break was fixed (shared helpers are now LINKED into the three projects that needed
them, not copied). Three causes, all fixture defects, none requiring an assertion to be relaxed:

- **18** — the caller had a RANDOM identity. `CreateAuthenticatedClient(…, userId = null)` defaulted
  to `Guid.NewGuid()`, so every request arrived as a different user. These suites had always been
  running "created by one user, read by another"; nothing noticed until #863 checked.
- **4** — `ai-upload` is a fixed **5 req/min partitioned BY USER** and the upload suite issues ~10.
  Those tests had only ever passed *because* the identity was broken — each got its own partition.
  Fixed by a per-test session registry: `CreateTestSession()` mints BOTH a session id and an owner
  per test. Both halves are required — `ChatSessionManager` caches by `tenant + sessionId` and NOT
  by user, so per-test oids alone made it worse (4 → 7, all 404).
- **1** — the credential hang again, this time with the stack trace naming it outright
  (`managed_identity_unreachable_network`, `169.254.169.254:80`).

Full record incl. the rejected option: [`notes/test-host-credential-hang.md`](notes/test-host-credential-hang.md).

**Practice change that outlives the bug**: verify with a SOLUTION-level `dotnet build`. A green
`dotnet test tests/unit/Sprk.Bff.Api.Tests/` said nothing about the other projects, and that is how
a non-compiling project stayed invisible.

---

> Sections A/B/C (the 2026-08-28 fix plan for the ten failures) were DELETED on 2026-08-29:
> A1/A2 above supersede them, and two of their conclusions are now known wrong — the
> `SessionOwnershipFilter` timeout hypothesis (§B) and "pre-existing, unrelated" (§C) were
> both the credential defect. Leaving them would let a future reader act on a refuted
> diagnosis. The reasoning is preserved in `notes/test-host-credential-hang.md`.

## D. Where #863 stands

**Complete**: `OwnerOid` on `ChatSession` + `StoredSession` (mapped both ways) · required positional
`ownerOid` on `CreateSessionAsync` · `AddSessionOwnershipFilter` on all 28 `{sessionId}` routes · 4
body-scoped routes checked in-handler and enumerated in the guard · History list owner-filtered · one
stable `session.not-found-or-not-owned` code + `auth.tid-missing` at 401 · unowned sessions fail
closed (cost accepted + documented).

**Two production defects the suite found (review did not)**: the Compose *document* session was
minted unowned — so the next dispatch 404'd for the user who had just registered the document — and
`POST /api/compose/active-document` had no ownership check although it mutates the named session and
its child inherits that owner.

**Tests**: `SessionOwnershipGuardTests` 5/5 · `SessionOwnershipTests` 8/8, both **proven
non-vacuous** (removing the ownership comparison turns 2 denial tests red; removing one
`.AddSessionOwnershipFilter()` line turns guard Rule 1 red).

Full record: `notes/863-session-ownership.md`. **Nothing on #863 awaits a decision.**

## E. Then: task 070 cluster 7

Extract **cluster 7 (memory capture)** first, NOT cluster 1 — coverage measured 2026-08-28 inverts
the structural order: cluster 1 is cleanest but only **76.6% branch**, while 7/6/5b/8 sit at 87–96%.
Evidence order: **7 → 6 → 5b → 8 → 2b → 2a → 1 → 3 → 4 → 5a**. Build + run the Compose seam/op-log
suites after EACH extraction (POML step 3), not once at the end.

> ⚠️ **Do NOT extract cluster 2 until `unified-access-control-r2` replies on #858.** They own a
> security fix inside `ComposeService.cs`; I proposed holding cluster 2 so their patch lands against
> today's line numbers.

The three standing 070 warnings (POML criteria unreachable · SaveAsync stays whole · 074 closed
do-not-delete) are UNCHANGED and still bind — preserved in full below.

## F. Task status

**44 ✅ · 4 🔲 (070, 071, 072, 090) · 1 ⊘ (043) · 1 ⛔ (074).** 059 closed this session (owner
sign-off; the directed cross-user fix became #863).

## G. Files modified this session

`Api/Filters/SessionOwnershipFilter.cs` **NEW** · `Models/Ai/Chat/ChatSession.cs` ·
`Services/Ai/Sessions/StoredSession.cs` · `Services/Ai/Chat/ChatSessionManager.cs` ·
`Services/Ai/Sessions/{I,}SessionPersistenceService.cs` · `Api/ComposeActiveDocumentEndpoints.cs` ·
`Api/Ai/{ChatEndpoints,AnalysisEndpoints}.cs` · `Api/Agent/AgentEndpoints.cs` ·
`Services/Compose/ComposeService.cs` · `Api/Filters/AiAuthorizationFilter.cs` (corrected the false
"handlers check ownership" comment) · `tests/Spaarke.ArchTests/SessionOwnershipGuardTests.cs` **NEW**
· `tests/integration/auth/Ai/SessionOwnershipTests.cs` **NEW** ·
`tests/integration/Shared/{TestSessionOwner,TestHttpContexts}.cs` **NEW** (wired into the csproj) ·
~60 test files (fixture repairs) · `notes/863-session-ownership.md` **NEW** · `tasks/TASK-INDEX.md`.

---

# Full State (preserved history — earlier checkpoints)

### 📋 Owner decisions taken 2026-08-28 — all recorded, none pending

| Item | Decision |
|---|---|
| **059** (security — tenant self-naming) | ✅ **SIGNED OFF, may merge.** Recorded in `notes/059-tenant-header-decisions.md` §9. |
| **059 cross-user DELETE gap** | ❌ owner **overrode** the "accept residual" recommendation → **fix it**. Filed as **#863** (schema change: `ChatSession` gains `OwnerOid`, persisted across Redis+Cosmos+Dataverse; the hard part is the migration policy for pre-existing unowned sessions, not the field). |
| **059 `RagEndpoints`** | 📄 **document + defer.** Evidence: the API-key principal carries NO tenant claim at all (`ApiKeyAuthenticationHandler.cs:92-96`), so nothing was bypassed. It is a machine credential that legitimately spans tenants — a different, lower class than 059. Correct fix is the key model. |
| **#853** (live-anchorless prompt vs retry) | ✅ **keep the prompt.** Closed on the issue. Tripwire noted: if `'live-anchorless'` fires often, the *anchor supply* has regressed — do not re-tune the copy. |
| **ADR-038 enforcement** | Filed **#864**. The 17 bans are documented and **nothing fails a build**; 24 touched files use the banned `Mock<HttpMessageHandler>` (4 added here, 20 pre-existing). Start with B4 + B13 — both at **zero** today, so a guard arms green. |

### 🍽️ `/test-diet` run early (report: `notes/test-diet-report.md`)

Run at owner request to answer *"do we have too many tests?"* with data. **Re-run at 090** — the skill is
a project-close gate and this project is still active.

**Answer: volume is not the problem, distribution is.** 187 test methods across 26 added files; the
project added **one** file outside a KEEP path and **deleted three**. The real finding is the unenforced
ban (#864) plus the pattern coverage exposed the same day: the `usePendingRedline` anchorless suite had
**29 tests, 23 of them on the same population** — the live path a user actually hits had **zero**. Test
count hides that; branch coverage finds it.

### 🔴 BEFORE EXTRACTING ANYTHING — coordination constraint from `unified-access-control-r2` (#858)

UAC-r2 owns a security fix in **`ComposeService.cs`**: create-on-save writes bytes into a CLIENT-NAMED
SPE container. They explicitly told compose-r8 **not** to implement it, and asked only to be told when
the file is stable enough to edit.

**They do not know 070 is about to restructure it.** I told them (#858 comment 2026-08-28) and proposed:

> **070 extracts every cluster EXCEPT cluster 2 (create-on-save / promotion).** `PromoteIfEphemeralAsync`
> (3169-3670) + record-resolution helpers (4036-4285) stay at their current line numbers until their fix
> lands. Costs us one deferred extraction; unblocks them completely.

⚠️ **Do NOT extract cluster 2 until UAC-r2 replies on #858.** Everything else is clear to proceed.

### ✅ #853 FIXED (`220ddd18e`) — live-anchorless is no longer called a replay

The discriminator was never missing: `MaterializeOrigin` was destructured at `usePendingRedline.ts:907`
and never read — invariant 7 breached in its purest form. New `AnchorlessSource` selected from
`origin` and **carried** (both proposal sites had hardcoded `'legacy-replay'`). Copy extracted to
`redlineFailureCopy.ts`; 19 new tests, non-vacuity proven. **Mechanics unchanged** — the confirmation
guard still applies to live-anchorless. 🔔 Owner question left open on #853: should a live-anchorless
edit *retry* instead of prompting? Not decided unilaterally.

### ✅ Issue #839 CLOSED OUT — PR #847 open, 131/131 ArchTests pass

All 6 ArchTest failures adjudicated. Do not re-open. Highlights worth keeping:

- **FR-27**: 5 of 8 findings were the regex matching a secret's NAME, not its value. Fixed with a
  name-vs-reference discriminator applied *after* the value regex — **never narrow the regex**, this is a
  CATASTROPHIC-severity detector. The real find: `PendingKvSecretWrite(VaultName, SecretName, Value)` — the
  guard reported the harmless `SecretName` and was blind to `Value`, which its own doc calls CLEARTEXT. New
  **secret-carrier rule** catches that pairing.
- **ServiceBusClientGuard**: demanded an architecturally forbidden fix (L2 has zero ProjectReferences and a
  MUST rule against referencing the BFF). Now one canonical construction site **per deployable**.
- **ADR-010**: ceiling 153 → 156. Net looked like +2; the diff was **7 added / 5 removed** — removals hid
  five additions from the ratchet. Evidence posted to #809.

### 📋 Everything else the repaired Tier 2 aggregator exposed is FILED, not carried

| Issue | Finding | Owner |
|---|---|---|
| **#848** | 5 unit-test failures; 4 are real-clock timing tests (`Spaarke.Scheduling.Tests`: 9s local vs 5m14s CI) | unclaimed; pairs with #795 |
| **#849** | 1212 broken markdown links, but **86% of the scanned corpus is historical `projects/**` docs** | unclaimed |
| **#850** | Prettier: **CI says 1907 files, local says 46** — not developer-reproducible. `npx prettier` is the pattern PR #393 already fixed for ESLint | `ci-cd-unit-test-remediation-r1` |
| **#853** | The Compose classifier bug above | **this project** |

Two genuine Prettier fixes landed here (`442fa904d`). 17 of 19 flagged files were **CRLF-only** —
`.gitattributes` doesn't cover `.ts`/`.tsx` and `core.autocrlf=true`, so they're already LF in CI and
`--write` produces a diff git normalizes away. Don't chase them.

### ✅ The authorization emergency is OVER — merged and deployed, owner-confirmed

| PR | Merge | Deployed | What |
|---|---|---|---|
| **#832** | `3e6fbd4d7` | dev, 45.07 MB | 38 broken caller-identity sites + 2 disclosures + `WorkspaceLayoutService` (3 breaks) |
| **#840** | `30e6fd9cf` | dev, 45.08 MB | remaining 41 `NameIdentifier` fallbacks · `CallerIdentityGuardTests` · Tier-2 aggregator repair |

Owner confirmed **"files are now showing"**. `/healthz` 200. **Do not re-investigate the oid/sub defect.**

### Worktrees

| Worktree | Branch | State |
|---|---|---|
| `c:\code_files\spaarke-wt-spaarkeai-compose-r8` | `work/spaarkeai-compose-r8` | PR **#806**. Synced with master, **11,462/0/95**. Clean, 0 unpushed, 0 behind. |
| `c:\tmp\spaarke-auth-oid` | `fix/caller-identity-sweep-clean` → now `fix/archtest-guard-adjudication` | Active work. Clean, 0 unpushed. |

---

## Active work — issue #839 detail

**Fixed and pushed (3 commits):**

1. `ed7fd7629` — **the Cosmos guard now actually RUNS.** It was still dead: the loader was repaired by
   `spe-admin-app-r2`, but nothing built the L2 DLLs it inspects, so it threw `FileNotFoundException`
   every CI run. The csproj claimed "CI's full-sln build satisfies this" — false; Tier 2 builds only the
   ArchTests project. Fixed with a `BuildL2ForCosmosGuard` MSBuild target (no `ProjectReference`, so the
   two original design reasons still hold). Proof it works: its **positive control now passes**.
2. `acd2b873a` — **FR-F1/FR-F2 closed.** `DataverseRegistryConcurrencyStore` was the one real ADR-028 A4
   violation (BFF's own app-reg + client secret). Its own FUTURE MIGRATION note gated the fix on the L2
   UAMI being a Dataverse Application User — **that was already true and the code never followed**
   (verified live: `sprk-controlplane-dev-uami`, app id `965a4a01-…`, enabled, `Spaarke Provisioning
   Registry` role). Migrated to `DefaultAzureCredential`, identical to the sibling
   `DataverseEnvironmentRegistryClient`. The other 3 sites are genuine E-1 (customer registrations,
   per-request) → allowlist + census entries. Bicep + KV reference removed end-to-end.
3. `46fe89d7d` — self-registered in `projects/INDEX.md`. **⚠️ Overlaps PR #845** (provisional row for the
   same project); whichever merges second takes mine, it is a superset.

**Remaining 3 — with the trap in each:**

- **FR-27** — 8 secret-shaped properties. Only 3 look like real secret VALUES
  (`SharedSecretResolution.Secret` ×2, `SolutionVerificationRequest.ClientSecret`); 5 look like the regex
  matching a property NAME (`PerEnvSettingEntry.Key`, `TrapVerificationRequest.KeyVaultName`,
  `PendingKvSecretWrite.SecretName`, …). **Do NOT narrow the regex** — it is a CATASTROPHIC-severity
  detector. Adjudicate per-property with evidence. NOTE: the rule is about **Cosmos-persisted** POCOs, and
  `SolutionVerificationRequest` is a transient request record never written to Cosmos — check persistence
  before classifying.
- **ADR-010** — ceiling 153 → 155. Identify the 2 added 1:1 interfaces; either justify + raise with docs,
  or register concrete.
- **ServiceBusClientGuard** — `ServiceBusModule.cs:144` `return new ServiceBusClient(fqn, credential);`
  Route through `ServiceBusClientFactory.CreateForNamespace`.

---

## ⚠️ Cross-project constraints — READ BEFORE TOUCHING CI OR AUTH

### CI shadow window (`ci-cd-unit-test-remediation-r1` owns `.github/workflows/**`)

**FROZEN**: `ci-router.yml`, `ci-tier1-blocking.yml`, `ci-tier2-advisory.yml`. My #840 edited two of them
and merged 5 minutes into the window; **disclosed and adjudicated ACCEPTED — no violation** (the freeze was
an unmerged PR at the time). The freeze now carries a **GATE REPAIR carve-out**: if a gate is silently not
enforcing, fix it and disclose. **I committed to disclosing before using it rather than self-authorising.**
Window is being re-baselined after #825; my branch adds no workflow changes.

### `unified-access-control-r2` owns parent→child access (their Amendment 1)

**R8 must NOT implement the parent-fallback, even as an interim.** Their §5 closed our Q6 (term 5 grants
the SAME right — no read/write fork). Vocabulary: **"Parental cascade"** = the Dataverse feature (rejected);
**"parent-fallback"** = their computed term 5. Docs: `notes/coordination-from-unified-access-control-r2-*.md`
+ `notes/response-to-unified-access-control-r2-*.md`.

### Sync #806 with `git merge origin/master`, NEVER rebase

It is a shared branch under review carrying merge commits. Also: a clean `mergeable` status is **not** a
clean build — the last sync returned MERGEABLE and then failed to compile (both sides had added the same
`using`, CS0105 ×4). Deduplicate after every sync.

---

## Session gotchas worth keeping

1. **The shell cwd resets between Bash calls.** Several greps/builds silently ran in the WRONG worktree and
   reported stale results. **`cd` explicitly at the start of every command.**
2. **`io.open(p,'w')` truncates before the write.** A Python edit that hit an encoding error left a
   committed doc at **0 bytes**. Recovered via `git checkout --`. Prefer the Edit tool for structured edits.
3. **Classify by SINK, not by expression shape.** Three sites written `oid`-first with early returns READ
   as correct and were cleared twice; two of them fed authorization and were broken.
4. **A guard not in the Tier 1 filter cannot fail the build.** `CredentialGuardTests` shipped red and CI
   reported green for 6 days. Arm a guard in the same PR that adds it.
5. **Verify a comment before repeating it as fact.** Three comment blocks said the L2 UAMI was not a
   Dataverse Application User. It had been for some time. One query settled it.
6. **Prove non-vacuity.** Every guard/test added this session was verified to FAIL against the pre-fix code
   (re-broken sites, probe files) before being accepted.

---

## Full State — PRIOR checkpoint (2026-08-26, Compose R8 UAT + deploy)

> Superseded as the ACTIVE task by the P1 work above, but still the record for the Compose R8
> project itself, which is not finished. Retained verbatim.

> **Last Updated**: 2026-08-26 (by `context-handoff`) · **Committed through**: `670d31db2` · **pushed, 0 unpushed**
> **Branch**: `work/spaarkeai-compose-r8` · **Recovery**: read "Quick Recovery" first.
> Everything below is recoverable from files alone.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **DEPLOYED TO DEV — owner is mid-UAT.** No task in progress. |
| **Status** | 47 of 51 resolved. Tree clean, everything pushed, merged with `origin/master` (12 behind again — other projects merging). BFF **11,931 / 0 / 96** · integration **103 / 6** · ArchTests **9 failed / 101 passed — ALL 9 PRE-EXISTING ON MASTER** (verified against a clean `origin/master` checkout: 9 failed / 95 passed). |
| **Next Action** | **WAIT for UAT results.** While waiting: fix the misleading *"came from an earlier session"* copy (see 🔴 OPEN BUG below). Then Track D **070/071/072** (worktrees staged at `C:\code_files\spaarke-wt-07{0,1,2}`, **agents NOT dispatched**), then **090**. **059 still needs owner sign-off.** |

---

## 🚀 DEPLOYED TO DEV 2026-08-26 — first deploy of Tracks A/B/C

| Component | Result |
|---|---|
| **BFF** `spaarke-bff-dev` | 45.14 MB · SHA-256 hash-verified · `/healthz` 200 · 2/2 CORS origins |
| **SpaarkeAi** `sprk_spaarkeai` | rebuilt + published, 5,725 KB. ⚠️ the previous `dist` was **Aug 21** — deploying it would have shipped a 5-day-old client against the new BFF. **ALWAYS rebuild before deploying.** |
| **Action mirrors** (4 Track C rows) | `target_para_id` **False → True** in `outputSchema` + `systemPrompt`. Verified 3 ways: PATCH result, independent re-read, second dry run = 0 changes (idempotent). |
| **Route-surface proof** | **All 17 authenticated Compose routes → 401, zero 404s** against the DEPLOYED app. Stronger than 073's two local oracles. |

**Track B is still DISARMED** — `SessionFileStore:BlobEndpoint` empty; dev has no storage account, UAMI has no storage role.

---

## 🔴 THE BIG FINDING: Track C was UN-DEPLOYABLE, not un-deployed

UAT hit two symptoms — a refusal banner on *make more concise*, and a *"Where should this suggestion go?"*
confirm dialog on *draft alternative*. **One root cause**: the deployed Action rows were the **2026-07-28**
versions asking the model for `target_text` and never for `target_para_id`. No anchor ⇒ every LIVE edit fell
into task 053's **replay** population ⇒ banner (prose didn't match) or confirm dialog (prose matched).

**The recorded prerequisite was never executable.** `Deploy-AnalysisAction.ps1` cannot deploy
`infra/dataverse/actions/*.action.json` for three independent, individually-fatal reasons:
1. it reads a `{actions:[...]}` wrapper — the mirrors are **bare objects**;
2. it hard-requires `actionTypeName` and skips without it — **0 of 17** mirrors carry it;
3. it writes `sprk_ActionTypeId@odata.bind` — **that column does not exist on the entity**.

**The column is missing BY DESIGN — verified, not assumed.** R7 task 028 / FR-07 removed the ActionTypeId
expand (*"Action is no longer the dispatch axis — orchestrator reads `node.sprk_executortype` directly"*,
`AnalysisActionService.cs:235`/`:343`); live metadata shows **65 attributes with no action-type lookup**;
the only surviving `sprk_ActionTypeId` reference in the whole BFF is a **stale comment**
(`InsightsActionRouter.cs:290` — the 6th stale-comment defect this project has hit). **Re-adding that column
would restore a retired dispatch axis — a regression against FR-07, not a fix.** `seed-data/manifest.yaml`
already recorded the gap: step `actions-r7`, **`deployer: null`**.

**Closed by NEW `scripts/Deploy-ActionMirrors.ps1`** — deploys all 17 mirrors, binds no action type, `-DryRun`
shows a per-field before/after, idempotent, and refuses to invent a row when no `sprk_actioncode` matches.

> **Lesson worth keeping**: the model contract lives in **Dataverse DATA** (`sprk_outputschemajson` +
> `sprk_systemprompt`), read at runtime. Shipping BFF + client code cannot move it. Any task that changes an
> Action's schema/prompt is **not deployed** until the mirror is pushed.

---

## 🔴 OPEN BUG — misleading copy on a live anchorless edit (NEXT THING TO FIX)

Both UAT symptoms rendered *"This suggestion came from an earlier session, before suggestions carried a
paragraph reference"* — to a user who had just selected the text a second earlier. That copy is **literally
true of the payload** (it genuinely had no anchor) and **completely wrong about the user's action**.

Root: everything anchorless is classified `legacy-replay`. The classifier must distinguish
**"no anchor because it predates anchors"** (replay — ask, don't place) from **"no anchor on a LIVE edit"**
(a model-contract failure — different words, and arguably a retry rather than a prompt). Sites:
`ComposeBannerStack.tsx:937-942` (banner) and `ComposeWorkspace.tsx:5340-5341` (dialog); classification in
`usePendingRedline.ts`. Note the fallback bound is structural and CORRECT — an anchored edit cannot reach
that dialog; only the wording and the live-vs-replay split are wrong.

---

### 058 — nested/conditional merge fields now carry (merged 2026-08-26)

Task 049 flattened these for a real structural reason, and that reasoning **survives intact**: a nested
field's recoverable instruction is a *concatenation* of both code phases, so re-emitting it authors a
different field. What 049 established is that a nested field cannot be **reconstructed** — not that it
cannot be **carried**. The third mechanism was never on the table: **carry the span's OOXML and never
parse it.** The tree survives because nothing reads it. Headline test asserts the saved span
**character-for-character** against the source — the one assertion a reconstruction cannot pass.

**It surfaced a second defect, which is the more valuable half**: `ComposeBlockMerge.InheritRunProperties`
donates the base paragraph's *dominant* run properties to every rendered run. In a conditional the
dominant run is the outer `IF` result — **bold** — so all 17 carried runs came back bold, silently bolding
both inner `MERGEFIELD` values. A fidelity loss introduced by the fix for a fidelity loss, and one that
would have shipped looking correct. Rule now stated where it lives: *inheritance repairs a re-authored
run; a carried run has nothing to repair.* Scoped to nested spans only.

Residual list: the nested half leaves §2; only the **unterminated** field (`TOC`/`INDEX`, which spans
paragraph marks) remains. [`notes/058-nested-field-carry.md`](notes/058-nested-field-carry.md).

✅ **Owner-signed 2026-08-26**: *"follow the established pattern."* A user who deletes a conditional chip
is indistinguishable from a client that never sent it, so the construct is **restored** — the same trade
already taken for bookmarks, SDT shells and objects. This is now the **fourth** construct behaving that
way and the pattern is explicitly sanctioned, so a future carry should adopt it without re-asking.

Still true and NOT covered by that sign-off: no browser/UAT run, and the document was never opened in
Word. Fidelity is asserted through the SDK, the schema validator and the relationship gate.

### 🔒 059 — what it actually turned out to be (read before signing off)

Filed as *"remove the spoofable `X-Tenant-Id` fallback from four handlers plus the auth path."*
The mandated enumeration found **21 sites across three mechanisms**, and **the filed one was the least
severe**:

| Mechanism | Sites | Status before 059 |
|---|---|---|
| `X-Tenant-Id` header, last tier of a `??` chain | 16 | **LATENT** — only reachable by a principal with **no `tid` claim at all**, since tier 1 short-circuits. One such principal exists (`RagApiKey`) but never touched this tier. |
| `X-Spaarke-Tenant-Id`, no claim consulted | 1 | Live, admin-gated, **zero senders** anywhere in the repo |
| **`?tenantId=` query string** | **4** | **LIVE for any authenticated user.** Three consult **no claim at all**; the fourth let the query string OUTRANK the claim. |

**Two of those four are Compose's own**: `GET /api/compose/documents/{documentSpeId}` (the document
**open/resume** path) and `GET /api/compose/sessions/{sessionId}/annotations`. Both took the tenant
from the URL, so a caller could open another tenant's Compose session and resume its anchored
annotations, defined terms and action history. Two of them rejected a missing value with *"tenantId
query parameter is required for multi-tenant isolation"* — isolation the caller chose.

All 21 are closed. The guarantee is **structural, not a rule**: `TenantResolution.ResolveTenantId`
takes a `ClaimsPrincipal`, **not** an `HttpContext`, so it cannot reach a header, query string or
body — the same idiom as `ComposeEditAnchorPass` (no document text) and post-064 offsets. A
two-armed tripwire (`Headers[…Tenant…]` | `[FromQuery … tenantId`) matches by **shape, not name**;
its regex is verified in both directions, and its query arm is what found the two Compose sites
*after* the header sweep was believed complete.

**Four test fixtures minted principals with no `tid`** — a shape Entra never issues — and the tests
compensated with the header. That fixture gap was holding the hole open: it made the spoofable
fallback the only tenant path those tests ever exercised. Repaired the fixture, not the symptom
(`bff-extensions.md` §F.2). Two further tests were passing **vacuously** and now assert something
real. Full record: [`notes/059-tenant-header-decisions.md`](notes/059-tenant-header-decisions.md).

### Landed across the last two sessions — all committed AND pushed (PR #806)
`052` demote text-search · `053` bounded confirmable fallback · `053b` null-identifier edits reach the
document · `061` lazy re-index · `062` retention + availability · `063` durable erasure · `064` retire the
orphaned edit-batch surface · `047b` never-silent hole · `052b` stale-detection durability · **`059`
tenant-selection security (awaiting sign-off)** · **`058` nested-field carry** · **`073` endpoint
decomposition** · **`074` CLOSED do-not-delete** · **deploy + `Deploy-ActionMirrors.ps1`**.

### Critical context in one paragraph
**Track C (AI edit placement) and Track B (durable session files) are both COMPLETE.** Text search is no
longer a placement mechanism — and that is now enforced by the TYPE SYSTEM in two places rather than by
rule: `ComposeEditAnchorPass.Validate` takes no document text, and after 064 no type in `Services/Compose/`
can express a character offset at all. The client fallback survives only as a bounded, confirmable proposal
for replayed entries, in a module that **has no `applied` outcome**. Three defects found along the way were
worse than filed: an anchored edit replaced the ENTIRE paragraph; a stale target was not detected at all
(silent overwrite of the user's newer text); and 047b was not merely under-reporting — it was cloning an
UNTOUCHED block from the wrong base, an outright breach of ADR-049 invariant 2, in a real signed NDA.

---

## ⚠️ Publish-size: the ~1.3 MB divergence is the SHELL — settled 2026-08-25

**Current: 45.03 MB compressed incl. PDBs under `pwsh` 7** (215 files, 4 `.pdb`, **raw dir sum 137.41 MB**)
— **+0.07 MB** vs the 44.96 MB net10 baseline; ceiling 60 MB.

This project has carried two conflicting clusters (43.68–43.74 vs 45.00–45.04) for months. Zipping the
*same directory twice in the same minute* settled it:

| Shell | `Compress-Archive -CompressionLevel Optimal` |
|---|---|
| Windows PowerShell **5.1** (what `powershell` resolves to from Git Bash) | **43.73 MB** |
| **pwsh 7.6.3** (what the `PowerShell` tool and CI use) | **45.03 MB** |

Neither is an artifact — different `System.IO.Compression` implementations. **Canonical: `pwsh` 7**, because
CI uses it and it reconciles with the 44.96 MB baseline at +0.07 MB; PS 5.1 would imply a −1.23 MB drop no
code-only change could produce, which is itself the evidence the baseline was taken under pwsh 7.

**Method — pin the shell:**
```
rm -rf <out>
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o <out>
pwsh -Command "Compress-Archive -Path '<out>\*' -DestinationPath '<out>.zip' -CompressionLevel Optimal -Force"
```
**Always report the raw dir sum (~137 MB) + file count (215 / 4 `.pdb`) next to the zip.** Those are
shell-independent, so a mismatch there is a real content change while a zip-only mismatch is tooling. That
invariant is exactly what made this diagnosable.

## Owner decisions still in force (do not re-ask)

| Q | Decision |
|---|---|
| **Q1** Which bicep stack? | The question was wrong — dev is not stack-deployed. See "Track B is blocked". |
| **Q2** Sign off the residual list? | **YES — signed 2026-08-25.** Task 045 CLOSED. (Note: the field and object rows were *declined and fixed*, not accepted.) |
| **Q3** Conditional merge fields? | **Fix it** → task **058**. |
| **Q4** `X-Tenant-Id` fallback? | **Separate task, fix in R8** → task **059**. |
| **Q5** Silent-loss hole? | **Fix in R8** → task **047b**. |
| **052** `match_mode: 'all'` | **Retired in full.** Asymmetric failure modes; document-wide sweeps route to user-invoked find/replace. Reasoning: `notes/052-…-decisions.md` §2. |

### 🚨 ARMING WARNING — the code gate is closed, but do NOT set `BlobEndpoint` yet

Tasks 060–063 are done: durable store, lazy re-index, retention, erasure. ADR-015's precondition
(retention AND erasure before a persisted store is armed) is **satisfied in code**.

**Two pre-existing AUTHORIZATION defects sat on the same DELETE route.** One is now CLOSED; one remains.

1. ~~**The spoofable `X-Tenant-Id` fallback**~~ — **CLOSED by task 059** (2026-08-26), along with 20
   sibling sites it turned out to have. ⚠️ **Correction to what this warning previously said**: the
   header was described here as live on that route. It was **not**, for any caller holding a normal
   token — it sat at the END of a `??` chain, so it was only ever reached by a principal carrying **no
   `tid` claim at all**. The defect was **latent** (one route-registration away from live), not live.
   I wrote the earlier claim; it was wrong, and a test I wrote to prove it passed **vacuously** before
   the fix, which is how it was caught. See `notes/059-tenant-header-decisions.md` §3.
2. **No owner check — STILL OPEN.** `ChatSessionManager.DeleteSessionAsync(tenantId, sessionId, …)` is
   keyed on tenant + session only, and `ChatSession` has **no owner field at all** — so a check is not
   implementable without a persisted-schema change (Redis + Cosmos + Dataverse) and a policy for
   pre-existing sessions. 059 narrows it from **cross-tenant** to **within-tenant**; session ids are
   `Guid.NewGuid().ToString("N")`, so exploitation needs a leaked id, not a guess. **Owner decision
   pending** — `notes/059-tenant-header-decisions.md` §6a and §8.

What arming changes is **blast radius**: today these delete a 24-hour AI-Search index entry; armed, they
delete **90-day durable bytes**, and 063 confirms Azure soft-delete and versioning are OFF, so a
completed delete is final. A store that is armed and later disarmed also cannot be erased from.

**Arming is now gated on: (a) human sign-off of 059, and (b) the cross-user decision.** Not on further
code.

### The four operator steps (all still required, and still not done)
Provision/pick a storage account → create the container → grant **`mi-bff-api-dev`**
(the UAMI — **not** the system-assigned identity `model2-full.bicep` currently targets, which does not
exist on `spaarke-bff-dev`) *Storage Blob Data Contributor* → set `SessionFileStore:BlobEndpoint`.
063 also notes the role assignment is missing from `customer.bicep` and `model1-shared.bicep`.
Dev has **no storage account**, and the UAMI holds **no storage role of any kind**.

---

## Remaining queue (6 open, 1 blocked)

| # | Task | Gate |
|---|---|---|
| **058** | Nested / conditional merge fields | 049 ✅ 057 ✅ |
| **059** | SECURITY — `X-Tenant-Id` spoofable fallback + the cross-user DELETE gap; human sign-off required | 060 ✅ — **dispatch next; gates arming** |
| **070–073** | Track D decomposition | ready; same files as Track A/C — sequence carefully |
| **074** ⛔ | Retire `ComposeShadowPatchEngine` | gate-confirm before deleting 3,000 lines |
| **090** | Wrap-up (incl. `/test-diet`) | all |

---

## 🔔 The ONE decision waiting

**`ComposeEditAnchorPass` + `ComposeAnchorResolver` now have ZERO production callers.** Verified
independently after 064: only comment references remain in `src/`; all 15 `Validate` call sites are in
tests. `POST /api/compose/edit-batch/validate` was their only caller and 064 deleted it.

They are the same orphan category 064 just retired — but task **052 kept the anchor pass deliberately**, and
the ADR-043/041 assessment (§7, C-7) names it the designated home for closed-set validation. So retiring it
is an owner decision, not a cleanup. Three options, in `notes/064-orphan-retirement-decisions.md` §4:

- **(a) Keep** as the designated home — accept it is currently dark.
- **(b) Wire it** — the obvious candidate is server-side validation of whole-document `target_para_id`s
  (today the closed-set check is client-side only).
- **(c) Retire it too** and amend the assessment.

> Owner decisions A and B (2026-08-25) are DONE — A → task 053b, B → task 064. Do not re-ask them.
> One sub-decision inside 064 has a revert point: three always-default fossils (`MatchCount`,
> `EditErrorKind.Overlap`, `BatchValidationResult.BatchErrors`) were removed beyond the task's list.
> Rationale + blast radius: `notes/064-orphan-retirement-decisions.md` §3.4.

### Superseded — decision #1 is CLOSED
### 🔔 Decision waiting #1 — a false `applied` that contradicts what we tell the model (surfaced by 053 §5)

A **post-052** payload can carry `target_para_id: null` — Structured Outputs requires the key to be present,
so "no identifier" arrives as an explicit null, not an absent field. Such an edit has no anchor **and no
prose**, so 053's fallback cannot serve it; it falls through to the insertion-at-cursor branch and reports
**`applied`**. Meanwhile the catalog prompt tells the model, verbatim:

> *"Set target_para_id to null ONLY when you genuinely cannot identify the paragraph. An EDIT with a null
> identifier is **REFUSED rather than placed** — there is no prose fallback — so a missing identifier costs
> you the edit."*

So the system currently lies to the model and gives the user a stray insertion reported as success. It is
**not** a UAT-21 mis-placement (nothing is struck; it is a pending insertion at the user's own caret), which
is why 053 surfaced it instead of changing it — the same branch also serves `compose-draft-document` and
`compose_context_insert`, which are *legitimately* anchorless.

**The discriminator that separates them cleanly**: `hasOwnProperty(payload, 'target_para_id')` — key present
and null ⇒ an edit that failed to identify its target ⇒ **refuse**; key absent ⇒ a genuine insertion ⇒ insert
as today. **Fix it, or change the catalog promise to match the code?** Recommend fixing the code: the promise
is the correct behavior and R8's charter is no false `applied`.

### Superseded — decision #2 is CLOSED (task 064 executed it)
`ComposeEditBatch` + `ComposeEditTransaction` are now orphaned — the text-offset APPLY half of the
mechanism 052 retired, with no producer and no production consumer, so they can never apply anything. They
do **not** violate I-7 (they apply spans, they do not search), so 052 left them rather than delete ~500
lines outside its list. **Retire them (with `/edit-batch/validate` and the models serving only them)
alongside task 074?** Evidence: `notes/052-…-decisions.md` §1.4.

---

## How to run the next wave (this keeps working — reuse it)

**Parallelism.** The blanket `parallel-safe: false` on the Compose spine is too coarse. Judge **file AND
toolchain disjointness per pair**. Task 052 split cleanly into `src/server/**`+`tests/**/*.cs` (dotnet) ∥
`src/client/**`+`infra/dataverse/**` (jest) — but give each agent an explicit "you MUST NOT touch X"
boundary naming the *other* agent's paths, or they collide. **052 ∥ 047b/058 would collide** (all
`Services/Compose`).

⚠️ **Do NOT trust the POML `parallel-safe` flag — read the file sets.** 061/062/063 are all marked
`parallel-safe: ✅`, but **all three declare `Services/Ai/Sessions/` as `primary-edit`**, and 062
additionally touches the Compose client. They are safe *relative to other tracks*, not *to each other*.
Running them concurrently would collide on `SessionFileBlobStore` / `SessionFilesCleanupJob` /
`SessionRestoreService`. **Sequence them: 061 → 062 → 063.** The genuinely disjoint pair is
**053 (Compose client / jest) ∥ 061 (Ai Sessions server / dotnet)**, which is what was dispatched.

**Main session reserves** `TASK-INDEX.md`, `current-task.md`, `.claude/**` and ALL git operations. Tell
agents explicitly they cannot write `.claude/` (root §3) and should report proposed CHANGELOG text instead.

**Never build/test while an agent is mid-edit in the same tree** — you will read half-written work as a
regression. Note the cross-toolchain case: a C# test that reads `infra/dataverse/**` JSON at runtime is
affected by the *client* agent's edits.

**Run `dotnet format` before committing.** Task 052's files had whitespace/EOL violations; CI auto-formats
and pushes, which rejects your next push. Use `dotnet format whitespace --include <your paths>` — a
project-wide `dotnet format` also "fixes" ~22 pre-existing IDE1006 naming violations in unrelated files and
produces a huge diff.

**Beware `grep -i compose` in this worktree** — the path is `spaarke-wt-spaarkeai-compose-r8`, so it
matches EVERY line. Scope to `Services\\Compose\\` or a filename.

**Verify every agent report.** What that caught this time:
- a **wrong publish number already committed to a project note** (see the box above);
- a **stale test fixture neither agent owned** — `golden-utterances.json` still documented `match_mode` as
  a live payload field and carried a whole case for the retired `all` sweep. One agent fixed only the `.cs`
  half; the other flagged the file as out-of-boundary, and its flag was itself stale. **When two agents
  share a contract, check the seam neither one owns.**

---

## Standing constraints (unchanged)

### ✅ DEPLOYED TO DEV — 2026-08-26 (BFF + `sprk_spaarkeai` together, NFR-05 satisfied)

First deploy of Tracks A / B / C. Commit `cfc118fe4` (merged with `origin/master`, 0 behind).

| | |
|---|---|
| **BFF** | `spaarke-bff-dev` · package **45.14 MB** · SHA-256 hash-verified on 4 critical files · `/healthz` 200 · 2/2 CORS origins present |
| **SpaarkeAi** | web resource `sprk_spaarkeai` (`5206a442-…`) updated + customizations published · bundle **5,725 KB**, rebuilt today (the previous `dist` was **Aug 21** — five days stale) |
| **Route-surface proof** | **All 17 authenticated Compose routes return 401, zero 404s** — task 073's decomposition verified against the DEPLOYED app, which is stronger than the two local oracles. |

⚠️ **Still NOT observable: Track C.** `Deploy-AnalysisAction.ps1` has **not** been run. Task 052 changed
the four compose Action output schemas, so until those `sprk_analysisaction` rows are upserted, dev still
asks the model for `target_text` and the anchored-placement work cannot be exercised. **This is the next
deploy step, and it was not part of the requested deploy.**

⚠️ **Track B remains DISARMED** — `SessionFileStore:BlobEndpoint` empty; dev has no storage account and the
UAMI holds no storage role. Unchanged by this deploy.

- **Deploy prerequisite (CORRECTED 2026-08-26 — the old instruction was NOT EXECUTABLE)**: Track C needs
  the Action mirrors in `infra/dataverse/actions/` deployed to `sprk_analysisaction`, via the NEW
  `scripts/Deploy-ActionMirrors.ps1`. The previously recorded instruction — *run `Deploy-AnalysisAction.ps1`* —
  **could never have worked**: that script reads a `{actions:[...]}` wrapper (mirrors are bare objects),
  hard-requires `actionTypeName` (all 17 mirrors omit it), and writes `sprk_ActionTypeId@odata.bind` — a
  lookup that **does not exist** on the entity. The ActionType axis was retired ON PURPOSE by R7 task 028 /
  FR-07; `seed-data/manifest.yaml` already recorded `deployer: null` for this source. **DONE 2026-08-26** —
  the four Track C actions now carry `target_para_id` in both schema and prompt.
  **052 raises the stakes** — it changed the four compose Action output schemas, so until that script runs,
  dev still asks the model for `target_text`. Deploy BFF + `sprk_spaarkeai` together (NFR-05).
  **Nothing from Phase 3 onward is deployed.**
- Publish ceiling 60 MB **compressed**; current **43.73 MB**. No new NuGet on Track A.
- **NEVER delete `docxBridge.ts`.** Confirmed unmodified through 052.
- Pre-existing CI red, NOT ours: **Compose Client Gate** (timeout flake since `7069717bd`) and **Trivy**
  (HIGH CVE on master). PR **#806** open.
- **C-4 still unmeasured against a real model response.** Anchors add 3.50% at realistic payload size.
- **Nothing in Track B has run against real Azure** — no storage account, no MI, no RBAC.
- **No bicep file has been changed by this project at any point.**

---

## Hard-won gotchas (this session) — do not rediscover these

- **Publish size: PIN THE SHELL.** `Compress-Archive` gives **43.73 MB under Windows PowerShell 5.1** and
  **45.03 MB under pwsh 7** for the SAME directory. Canonical is **pwsh 7** (CI uses it; reconciles with the
  44.96 MB baseline at +0.07 MB). Always report the **raw dir sum (~137.41 MB) + file count (215 / 4 `.pdb`)**
  alongside the zip — those are shell-independent and are the only reason this was diagnosable.
- **Line endings**: `.gitattributes` sets `*.cs text eol=crlf`, and edits can silently produce pure LF.
  **`grep -c $'\r$'` reports those files as CRLF and is WRONG.** The reliable check needs the `tr`:
  `od -An -tx1 <file> | tr ' ' '\n' | grep -c '^0d$'` — non-zero means CRLF.
  ⚠️ **Without `| tr ' ' '\n'` it returns 0 for CORRECT files too** (od prints 16 bytes per line, so no line
  ever equals `0d`) — i.e. it silently reports every file as broken. Task 047b caught exactly that error in a
  brief written from this note; the note is now correct.
- **`dotnet format` before committing**, scoped: `dotnet format whitespace <csproj> --no-restore --include
  <your paths>`. CI auto-formats and pushes, which rejects the next push. A project-wide run also "fixes"
  ~22 pre-existing IDE1006 violations in unrelated files.
- **`grep -i compose` matches EVERY line** — the worktree path is `spaarke-wt-spaarkeai-compose-r8`. Scope to
  `Services\Compose\` or a filename.
- **Don't trust the POML `parallel-safe` flag — read the file sets.** 061/062/063 are all marked ✅ but all
  three declare `Services/Ai/Sessions/` as `primary-edit`.
- **Give each agent an explicit "you MUST NOT touch X" naming the OTHER agent's paths.** Both parallel waves
  this session stayed clean because of that; the one cross-agent seam that broke was a file *neither* owned.
- **When two agents share a contract, check the seam neither one owns.** Task 052: one agent fixed the `.cs`
  eval test, the other flagged the file as out-of-boundary (and its flag was itself stale) — the JSON fixture
  went stale and only main-session verification caught it.
- **Don't `dotnet build` while a `dotnet test` run is live** — the test host holds the output assembly and
  the build reports a phantom error. Same family as the mid-edit hazard. Re-run after it finishes.
- **The mid-run hazard includes FIXTURES, not just code.** Re-running a corpus generator
  (`tests/fixtures/compose-corpus/generators/*.py`) rewrites its `.docx` **in place**. Doing that during a
  live suite produced **2 corpus-theory failures at `< 1 ms`** that looked like real 058 regressions and were
  purely self-inflicted; a clean re-run gave **11,391 / 0**, exactly the predicted count. The `< 1 ms`
  duration is the tell — that is a file-read failure, not a logic failure.
- **A regenerated corpus `.docx` is NOT a no-op diff.** `zipfile.ZipFile(path, 'w')` stamps the current
  mtime into every entry, so the bytes differ on every run while the content is identical. `git status`
  cannot tell that apart from a real content change — unzip and `diff -r` before committing one.
- **Run the two client suites SEQUENTIALLY, not concurrently.** 052b saw 2 and 12 spurious failures
  running `Spaarke.Compose.Components` and `SpaarkeAi` at the same time; both green run one after the other.
- **Verify every agent report.** Caught so far: a wrong publish number already committed to a note, a stale
  test fixture, a misleading `parallel-safe` flag, two of an agent's own tests passing vacuously, and a
  "regenerates byte-identically" claim that was false (ZIP mtimes).
- **An agent whose worktree you remove will report DATA LOSS.** After collecting + committing 073 the
  worktree was removed; the agent then re-notified with an urgent "the deliverable is no longer on disk".
  Nothing was lost — verify with `git ls-tree -r --name-only HEAD | grep <artifact>` and move on. **Collect,
  commit, THEN remove** — and expect the alarm.
- **`gh`/OData/metadata queries that ERROR can print a false negative.** A `contains()` filter is unsupported
  on Metadata Entities; the failed call left `$m` null and the script printed "NONE — no attribute exists",
  which would have become evidence for re-adding a column. **A failed query is not a negative result.**
- **Model-contract changes live in Dataverse DATA, not code.** `sprk_outputschemajson` + `sprk_systemprompt`
  on `sprk_analysisaction` are read at RUNTIME. Deploying BFF + client cannot move them. Use
  `scripts/Deploy-ActionMirrors.ps1`.

---

## 🚨 047b found more than a reporting bug — read this before touching the merge

Task 047b was filed as "an edited block with no base counterpart reports no loss". It was **not only** that.
On `interior-text-boxes.docx`, blocks 1 and 2 project to **byte-identical** models (the text box's prose is
accept-flattened; the shape is not carried), so `ComposeBlockMerge.Plan`'s LCS was **ambiguous** — and the
traceback's tie-break skipped the *posted* block, producing:

```
posted 1 -> Render base=-1   <- the EDITED block, no counterpart -> nothing reported
posted 2 -> Clone  base=1    <- the UNTOUCHED twin, cloned from the WRONG base
              base 2 stranded, never written
```

The saved package held block 1's `v:shape` at position 2 and block 2's not at all. **ADR-049 invariant 2
("untouched blocks are preserved") was being breached by a clone.** The remark on `Plan` asserted this could
not happen — equality there is over the *projected model*, not the OOXML — and that comment is why nobody
looked. Fourth stale-comment defect this project has hit.

Corpus sweep, 24 docs × every block position = **294 single-block edits: unpaired blocks 5 → 0.** Four of the
five were in a **real signed NDA** (`AppligentNDA_Signed.docx`), on consecutive empty paragraphs.

**Why the fidelity gate never caught it**: the gate edits block 0 of that document. Every other parity row
sits in a document whose blocks all read differently. 047b added a `pictTextBoxTwin` parity row so the
published list is now measured **at a duplicate-key block position** — that is the gap that let this survive
four runs of a check built to catch it.

`COMPOSE-WRITE-RESIDUAL-LOSS.md` changed but **no row changed** — the signed five losses are identical. What
changed is that §2's promise ("reported by name … none is silent") is now *true* where it wasn't.

### Recorded by 047b, not fixed (deliberate)
- `BaselineUnavailable` / `BaselineUnaligned` fall back to R6's whole-document rebuild with no base side — a
  different failure CLASS (document-level, not per-edited-block), whose honest signal needs a new degradation
  code + client copy + banner state, which this project's CLAUDE.md forbids adding here. Reachability
  measured: **0 of 24** corpus documents. Both already on `ComposeMergeStats`; only a consumer is missing.
- LCS cannot see a MOVED block (matches never cross) — 0 of 294 after the fix.

## Doc drift to fix (not urgent, main-session only — hot path)
Root `CLAUDE.md`'s ADR-049 pointer says the save "pairs blocks by **document order**". It has paired by
**LCS** since task 040 — loosely true (matches are monotone) but imprecise, and 047b showed the imprecision
is where a real defect hid. Touching root CLAUDE.md needs `/conflict-check` + a `.claude/CHANGELOG.md` entry.
