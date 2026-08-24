# Task 033 — remove the secret and reconcile the estate

> **Status**: ✅ **COMPLETE — all 7 steps.** The BFF identity is secret-free: nothing in app settings,
> nothing in Key Vault, nothing beneath MI-FIC in the credential order. **ADR-028 exception E-3 is CLOSED.**
> **Date**: 2026-08-24

---

## 0. Two things surfaced before any deletion

### 0.1 🔴 STEP 1 FINDING — the stated reason for caution about the lowercase alias is FALSE

The project has carried this claim since the spec, in the 033 POML `<background>`, in spec success
criterion 7, and in `config/spaarke-resources.yaml:123`:

> *"a SIXTH lowercase Key Vault alias `bff-api-client-secret` **used by the Office add-in deploy** — any
> removal that ignores it breaks the add-in."*

**It is not true.** Traced exhaustively:

| Consumer | Uses the alias? | Evidence |
|---|---|---|
| `.github/workflows/deploy-office-addins.yml` | **NO** | consumes `BFF_API_CLIENT_ID` (a client **id**), `AZURE_STATIC_WEB_APPS_API_TOKEN`, `GITHUB_TOKEN`. No client secret of any kind |
| `scripts/Deploy-OfficeAddins.ps1` | **NO** | zero matches for `ClientSecret` / `client-secret` / `CLIENT_SECRET` / `KeyVault` |
| `config/spaarke-resources.yaml` | yes — `:289`, `:313`, `:476`, `:494` | but it is a **manifest**, not executable |
| `scripts/naming-conformance-check.ps1` | mentions it | to **flag** the duplicate as *"a rotation hazard"* — it complains about the alias, it does not consume it |

**The real consumer is different, and so is the real risk.** `config/spaarke-resources.yaml` is read by
`scripts/Sync-LocalConfig.ps1` — which resolves `kv:bff-api-client-secret` to sync secrets into a **local
development** config file. So deleting the alias threatens **local `dotnet run`**, i.e. spec success
criterion **9**, and *not* the Office add-in deploy of criterion **7**.

**Why this matters beyond the immediate fix.** This is the same failure this whole project exists to
correct: a **false sentence in text** driving a decision, unexamined, across multiple documents. The
original was `.claude/constraints/auth.md:108` — *"OBO flow (OAuth spec requires confidential client +
secret)"* — which made three prior audits conclude the secret was permanent. This one would have sent 033
to protect the wrong surface and leave the actually-affected one broken.

**Consequences for the remaining steps:**

- Step 3's re-verification target changes: re-verify **`Sync-LocalConfig.ps1` / local `dotnet run`**, not
  the add-in deploy.
- The claim must be corrected in all four places it appears (POML background, spec criterion 7,
  `spaarke-resources.yaml:123`, and any doc repeating it) — corrected, not silently dropped.
- Success criterion 7's wording ("Office add-in deploy succeeds") should be **kept as a check** — it costs
  nothing and the add-in deploy is worth confirming — but its stated *rationale* is wrong and must not be
  cited as the reason the alias is load-bearing.

### 0.2 ✅ RESOLVED 2026-08-24 — the predicted conflict was the wrong one

**Outcome first**: `git merge-tree HEAD origin/work/unified-access-control-r2` → **clean merge, zero
conflicts in any file**. Merge order between this branch and PR #812 no longer matters.

Two things were wrong with the prediction below, and both are worth keeping:

1. **`.claude/constraints/auth.md` never conflicted.** Their hunk is at ~line 196 (the access-control
   note); mine are at ~5 and ~111-145. Disjoint. The `/conflict-check` skill compares **file paths**, which
   is the right cheap first pass — but a path overlap is a *signal to look*, not a conflict. Asserting one
   without testing would have been the same error as asserting an outcome from a status code.
2. **The real conflict was in a file I never anticipated** — `docs/architecture/DATAVERSE-ACCESS-LAYER-ROUTING.md`,
   and **I caused it**, by inserting a step-5 banner adjacent to a header line #812 also edits. Fixed by
   relocating the banner into the lines-18-88 window their two hunks don't touch, with a comment at the
   insertion point saying why it sits there so it is not "tidied" back to the top later.

**A content problem the merge check surfaced, which merge mechanics would have hidden.** PR #812's new
text asserts:

> *"the client-secret path is the local-dev fallback and is retained (**do NOT remove
> `Dataverse:ClientSecret` / `API_CLIENT_SECRET`**) per the migration's own guard comments."*

That phrase is exactly right about its provenance — and **those guard comments were already stale when
#812 read them** (§2.1 / §4.4). It is the false-premise propagation this project exists to stop, caught in
flight: stale code comment → architecture doc → future readers. Raised on the PR
([comment](https://github.com/spaarke-dev/spaarke/pull/812#issuecomment-5399340721)) with suggested
replacement wording, rather than silently fixing it in this branch — it is their file and their call.

**Reusable lesson**: a file-path overlap warning is a hypothesis. `git merge-tree --write-tree` tests it
non-destructively in seconds and tells you the truth — including about conflicts you did not predict.

<details><summary>Original hard-warn, kept for the record</summary>

#### 🛑 CONFLICT-CHECK HARD WARN — `.claude/constraints/auth.md` is contended

Step 6 must edit `.claude/constraints/auth.md` (to close ADR-028 exception **E-3**).
**PR #812 (`work/unified-access-control-r2`) modifies the same file.**

Per the `/conflict-check` decision table this is the *hard warn* case: watchlist hot-path (skill
directives) + another active worktree + **file overlap**. Surfaced for coordination before step 6, not
silently merged into.

Also on that PR: `.claude/agent-memory/researcher/**` (no collision — append-only memory).
PRs #806 and #779 touch `.claude/` but **not** `constraints/auth.md`; #806 and #779 both touch root
`CLAUDE.md`, which 033 does not.

</details>

---

## 1. Step 1 — COMPLETE

Verified the Office add-in deploy path's dependency on the lowercase alias: **there is none** (§0.1).
The dependency that does exist is `Sync-LocalConfig.ps1` → local dev.

---

## 2. Step 2 — COMPLETE. The secret is gone from the running app

### 2.0 The POML's "irreversible" framing is wrong, and the correction changes how this was run

The POML calls 033 *"the only irreversible step in the project."* Measured rather than assumed:

| Vault property | Value |
|---|---|
| `enableSoftDelete` | **true** |
| `softDeleteRetentionInDays` | **90** |
| `enablePurgeProtection` | null (off) |

So a deleted Key Vault secret is recoverable with `az keyvault secret recover` for 90 days. **Step 3 is
reversible provided it deletes without `--purge`** — and it will. The genuinely irreversible act would be
a purge, which this task does not perform.

Nothing here licenses carelessness; it means the risk was *measured* instead of inherited from a sentence.

### 2.1 Pre-flight — what actually reads these keys

`grep` across `src/` for all five config keys returned 37 hits. **Exactly three are executable code**;
every other hit is a comment or XML doc:

| Site | What it does |
|---|---|
| `OrderedCredentialClientProvider.cs:516-517` | the `ClientSecret` branch — resolves `AzureAd:ClientSecret` → `API_CLIENT_SECRET` → `AZURE_CLIENT_SECRET`. **The intended consumer** |
| `IdentityConfigurationValidator.cs:185-186` | rule 2a — `LogError` only, non-fatal since task 022 |
| `IdentityConfigurationValidator.cs:223-226` | rule 5 — `AgentToken:ClientSecret` divergence, by fingerprint |

Confirmed zero consumers, as the carried-forward obligation predicted:
- **`Dataverse:ClientSecret`** — 2 hits, both comments
- **`Graph:ClientSecret`** — 2 hits, both comments. *Not present as an app setting at all* — so the POML's
  "five config keys" is **four** on the live app. Recorded rather than silently reconciled.

⚠️ **Two stale comments found that assert the opposite of the truth** — the exact failure mode this
project exists to eliminate. Both are in `DataverseServiceClientImpl.cs`:

```
:18-20  "The secret path is retained as a local-dev fallback and MUST NOT be removed until MI
         attribution is proven live per env."
:61     "The secret path is retained (do NOT remove API_CLIENT_SECRET) until MI attribution is
         proven live."
```

The class does **not** read `API_CLIENT_SECRET` — task 022 moved it onto `IConfidentialClientProvider`
(`:46`, ctor param `confidentialClients`). The comments were true before task 022 and were never
refreshed. **Booked into step 4's sweep.**

### 2.2 Rollback integrity — established BEFORE deleting anything

SHA-256 fingerprints, 12 hex chars, values never printed or written:

| Location | Key | len | fingerprint |
|---|---|---|---|
| app setting | `API_CLIENT_SECRET` | 40 | `b09a140a603e` |
| app setting | `AzureAd__ClientSecret` | 40 | `b09a140a603e` |
| app setting | `Dataverse__ClientSecret` | 40 | `b09a140a603e` |
| app setting | `AgentToken__ClientSecret` | 40 | `b09a140a603e` |
| Key Vault | `BFF-API-ClientSecret` | 40 | `b09a140a603e` |
| Key Vault | `bff-api-client-secret` | 40 | `b09a140a603e` |
| Key Vault | **`Graph-API-ClientSecret`** | 40 | **`34f4d5234fb7`** ← DIFFERENT |

Two things follow, and both change the plan:

1. **All four app settings and both KV aliases are the same value.** So while Key Vault still holds it,
   deleting the app settings is *reversible* — restore from KV. That is what made it safe to run step 2
   before step 3, in that order, as the POML's own constraint requires.
2. 🔴 **`Graph-API-ClientSecret` is NOT an alias of `BFF-API-ClientSecret` — it is a different secret.**
   Step 7 calls it *"the orphaned `Graph-API-ClientSecret` alias"*. It is not an alias. Deleting it needs
   its own investigation of which app registration it belongs to and whether anything still resolves it.
   **Step 7's premise is corrected; the step is not skipped.**

### 2.3 A verification harness, because a status code proves nothing here

Every rung below was verified with the same harness, which ends in the one check none of this codebase's
three misleading shapes (fail-closed / fall-through / error-open) can fake: a **byte-exact SPE round-trip
over OBO** — upload N bytes, download, compare, delete, and confirm the delete.

Two harness-level corrections were needed first, both instances of the same trap:

- **`/api/spe/containers` needs `?configId={guid}`** — a `sprk_specontainertypeconfig` id
  (`c3a25b9a-e81f-f111-88b2-7c1e525abd8b`, "Spaarke PAYGO 1"). Without it the endpoint 400s and a naive
  harness silently skips the only decisive check.
- 🔴 **`/api/me` does NOT perform an OBO exchange.** A 200 there proves inbound token validation and
  nothing about the credential. This is why an early attempt saw no `built with credential` line and
  nearly read that absence as a finding. The OBO exchange had to be driven through SPE.

### 2.4 Execution — three rungs, each independently reversible

```
16:34–16:37Z  BASELINE           all green incl. byte-exact SPE round-trip (58 bytes)
16:38:58Z     RUNG 2a  set Graph__Credentials__Order__0=ManagedIdentityFederated   (212 -> 213)
16:39:20Z       LOG: "Ordered credential selection active: ManagedIdentityFederated."   <- no "> ClientSecret"
16:48:49Z       VERIFY  all green, 68 bytes byte-identical, 15/15 + 15/15
16:50:25Z     RUNG 2b  DELETE API_CLIENT_SECRET, AzureAd__, Dataverse__, AgentToken__  (213 -> 209, exactly -4)
16:52:29Z       VERIFY  all green, 71 bytes byte-identical, 15/15 + 15/15
16:53:16Z     RUNG 2c  set Graph__Credentials__RequireSecretFreeIdentity=true          (209 -> 210)
16:55:18Z       VERIFY  app BOOTED; all green, 74 bytes byte-identical, 15/15 + 15/15
```

Every app-setting count delta is fully attributable — the obligation carried from 032 §4, where a
213→212 drop could not be explained. Baseline of names: [`notes/appsettings-baseline-pre-033.md`](../appsettings-baseline-pre-033.md).

### 2.5 Why rung 2a is the proof, and why it came FIRST

Rung 2a narrows the order to `[ManagedIdentityFederated]` **while the secret is still present**. That
ordering is deliberate:

> With `ClientSecret` absent from the order there is nothing beneath MI-FIC to fall through to, so a
> working OBO exchange is MI-FIC **by construction** — not by inference from a status code.

This is the same argument `IdentityConfigurationValidator` makes about itself (§rule 6 comment): the
configuration form is *"STRICTLY STRONGER. Observing one resolution says the secret was not used this
time. Asserting the order says it CANNOT be used at all."*

Doing 2a first also means the decisive evidence was collected while rollback was still a single
`az ... appsettings delete` of one key — before anything was destroyed.

The order line was captured three times, on three separate process starts:
`16:39:20.860Z`, `16:42:30.628Z`, `16:46:12.118Z` — each reading
`Ordered credential selection active: ManagedIdentityFederated.` with **no `> ClientSecret`**.
`A4 DEVIATION`: absent throughout.

**The mechanism that makes this binding** (`AuthorizationModule.AddCredentialSelection`, re-read at
`:395-398` before relying on it): the canonical default `[MI-FIC, ClientSecret]` is applied **only when
the section is absent**, precisely because the config binder MERGES into existing collections — so
narrowing the order cannot silently get `ClientSecret` back. The comment there names this exact edit as
the reason the code is written that way.

### 2.6 The trap, a seventh and eighth time

Both caught before they reached a written claim:

| # | Shape | What happened |
|---|---|---|
| 7 | **health-check races the drain** | `/healthz` returned 200 on its *first* try 27 s after a restart — from the **old container still draining**. The docker log showed the old container stopped at `16:39:53`, *after* the verification began. Two later probes mismatched for the same reason. Fixed by requiring a 90 s settle **plus 3 consecutive 200s** before probing |
| 8 | **`/api/me` isn't OBO** | its 200 involves no OBO exchange, so it builds no confidential client and logs no credential line. Reading the missing line as a finding would have been the §5.5 error over again |

A third, smaller one: an inline `head -n -1` body-parse produced a spurious "MISMATCH". Re-running the
**already-proven harness** distinguished a parsing bug from a regression in one step — which is why the
harness is a file and not re-typed each time.

### 2.7 The forcing function is armed AND proven to fire

`RequireSecretFreeIdentity=true` is now set. Two independent facts make that meaningful rather than
decorative:

- **It evaluated and passed live** — rule 6 runs under `ValidateOnStart`; the app booted, so the rule ran
  against the real configuration and found no `ClientSecret` in the order.
- **It provably fails when it should** — `tests/integration/seam/Auth/IdentityConflationSeamTests.cs`
  carries the negative control: `Rule6_WhenEnabledOutsideDevelopment_AndTheSecretIsStillListed_FailsFast()`,
  alongside `Rule6_WhenEnabledOutsideDevelopment_AndTheSecretIsGone_Succeeds()` — which is exactly the
  live state now in place.

So the mechanism was demonstrated **without** deliberately breaking dev to watch it fail. A live negative
control would have meant a self-inflicted outage on a fail-closed path for evidence the test suite
already provides.

### 2.8 🔻 Rollback after step 2

| Rung | Mechanism | Status |
|---|---|---|
| 1 | Swap back to the pre-migration build | ❌ retired 2026-08-24 15:37:45Z (slot deleted, 032 §6) |
| 2 | Reorder credentials to secret-first | ❌ **RETIRED 16:50:25Z** — the secret is no longer in app settings |
| 2′ | **Restore the app settings from Key Vault** (`b09a140a603e`, still present) + remove the order override | ✅ **live today**, ~2 min |
| 3 | Redeploy | available, slow |

Rung 2′ exists only until step 3 deletes the KV secret — after which recovery is
`az keyvault secret recover` (90-day window) followed by rung 2′.

### 2.9 Found, not fixed here — `AZURE_CLIENT_ID` is still set

Startup logs an **`ERROR`** every boot (`16:42:30.625Z`):

> `IDENTITY CONFLATION SIGNAL (FR-B4), now INERT: AZURE_CLIENT_ID holds the MANAGED IDENTITY clientId
> 5967251e-…, while the app registration is 1e40baad-… . Since auth-v4 task 022 no code reads
> AZURE_CLIENT_ID … but the setting is wrong for what it appears to mean and should be cleared (task 031).`

**031's carried obligation *"clear `AZURE_CLIENT_ID` on both slots (now hygiene)"* was never done.**

It was **deliberately not bundled into rung 2b**. No *Spaarke* code reads it, but the **Azure Identity
SDK does** — `EnvironmentCredential` and `ManagedIdentityCredential` both read `AZURE_CLIENT_ID` from the
environment, and `DefaultAzureCredential` is live in `DataverseServiceClientImpl`. Deleting it is
probably safe (that call site sets `ManagedIdentityClientId` explicitly, and the app has exactly one
UAMI attached) — but "probably safe" is not the standard on a fail-closed path, and mixing an unvetted
change into the secret deletion would have made both unattributable. **Booked as its own change with its
own verification.**

---

---

## 3. Step 3 — COMPLETE. The secret is out of Key Vault

### 3.1 The consumer was fixed BEFORE the deletion, not after

The POML's step 3 reads *"Remove the Key Vault secret and the lowercase alias; re-verify the add-in
deploy."* Two changes, both following from §0.1:

- The surface to re-verify is **`Sync-LocalConfig.ps1` → local dev**, not the add-in deploy.
- **Order reversed.** A "delete, then check what broke" sequence is only defensible when you don't know
  the consumer. Here it was known, so the manifest it reads was corrected *first* and the deletion made
  second — which turns the verification into a precondition rather than a post-mortem.

`config/spaarke-resources.yaml` edits (it is the file `Sync-LocalConfig.ps1` walks for `kv:` refs):

| Line | Was | Now |
|---|---|---|
| `:123` | *"KV alias … (+ duplicate lowercase … **used by the Office add-in deploy**)"* | retirement note + an explicit **CORRECTION** paragraph naming the false claim and its real consumer |
| `:288-290` | `api_client_secret: kv:bff-api-client-secret`, `graph_api_client_secret: kv:Graph-API-ClientSecret` | removed, with a do-NOT-re-add note citing `RequireSecretFreeIdentity` |
| `:311-313` | `app_user_client_secret: kv:bff-api-client-secret` | `secrets: {}` + note that the Dataverse app user shares the BFF app registration and therefore inherits the MI migration |
| `:476-477` | both names in `referenced:` | removed |
| `:494` | `- [bff-api-client-secret, BFF-API-ClientSecret]` in `duplicates:` | removed, with the §2.2 correction that `Graph-API-ClientSecret` was never a duplicate |

**Verified statically rather than by running the script.** Parsed the manifest and walked every `kv:`
reference: **14 total, 0 pointing at any of the three doomed secrets.** Since `Sync-LocalConfig.ps1`
fetches exactly the refs it finds, it cannot fail on a secret nothing references. Running it would have
dumped 14 live secret values to disk to prove the same thing — strictly worse.

### 3.2 Execution

```
17:14:33Z  precondition: 26 secrets in spaarke-spekvcert
17:14:37Z  az keyvault secret delete BFF-API-ClientSecret     (no --purge)
17:14:39Z  az keyvault secret delete bff-api-client-secret    (no --purge)
17:14:40Z  post-state: 24 secrets — exactly -2
17:15:16Z  VERIFY  all green, 66 bytes byte-identical, 15/15 + 15/15
```

| Soft-deleted, recoverable until | |
|---|---|
| `BFF-API-ClientSecret` | **2026-11-22T17:14:37Z** |
| `bff-api-client-secret` | **2026-11-22T17:14:39Z** |

Negative cases held: **`spe-owning-app-secret` untouched** (ADR-028 E-1, per-customer owning apps) and
`PowerBi__ClientSecret` untouched (task 042, deferred). `Graph-API-ClientSecret` deliberately left for
step 7 rather than folded in here — the POML is `mode="prescriptive"`, so its step order is binding, and
bundling it would have been convenience, not necessity.

### 3.3 🔔 Consequence the owner should see: local-dev OBO has no credential path left

This is the one genuine cost of the migration, and it is a **design consequence, not a defect**.
`IdentityConfigurationValidator` states it in its own words:

> *"Development is exempt from rule 6: a developer workstation has no route to IMDS, so MI-FIC cannot be
> minted there and **the user-secret fallback is the legitimate — and only — way to run OBO locally**."*

So local `dotnet run` OBO needs a client secret, and the KV copy was the only **readable** copy — Entra
will not disclose a secret value after creation. Measured impact, narrower than it first appears:

| | |
|---|---|
| Runtime source for local dev | `dotnet user-secrets` — **not** `config/secrets.local.json`, which `Program.cs` never reads. It is a reference dump |
| Developer who already has the user-secret set | **unaffected** — the app-registration secret itself is alive in Entra until 2027-12-19 |
| Developer setting up fresh, or after a machine rebuild | **cannot obtain the value** → no local OBO |
| Recovery if this bites | `az keyvault secret recover --name bff-api-client-secret` (until 2026-11-22), or mint a dev-only secret |

**Not escalated as a blocker** because it is recoverable in one command, affects no deployed environment,
and criterion 3 ("gone from Key Vault") is the task's stated deliverable. **Surfaced** because it is a
real change to the developer inner loop that no criterion states plainly, and the owner may want a
deliberate replacement (a dev-only app registration, or `AzureCliCredential` for local OBO) rather than
inheriting this by default. Booked to 090.

⚠️ `src/server/api/Sprk.Bff.Api/docs/SPE.BFF.API-SECRETS-SETUP.md` is **doubly stale**: it instructs
developers to set `Graph:ClientSecret` and `Dataverse:ClientSecret`, and per §2.1 **both keys have zero
consumers** — the provider resolves `AzureAd:ClientSecret` → `API_CLIENT_SECRET` → `AZURE_CLIENT_SECRET`.
Following that doc today produces a local dev environment that silently cannot authenticate. Step 5.

---

---

## 4. Step 4 — COMPLETE. The script sweep

**15 scripts reference a client secret, not the POML's 11** — the corrected count, re-derived rather than
trusted. Classified before editing, because most of them are *not* about this credential:

### 4.1 Modified — 7

| Script | Change |
|---|---|
| `Configure-ProductionAppSettings.ps1` | removed `Graph__ClientSecret`, `AzureAd__ClientSecret`, `Dataverse__ClientSecret` (all three were KV refs to a secret that no longer exists) and **added** `Graph__Credentials__Order__0` + `RequireSecretFreeIdentity` so the script provisions the end state rather than just omitting the old one |
| `Reconcile-DemoEnvironment.ps1` | removed `AgentToken__ClientSecret`; added the two credential-selection keys **plus the FIC prerequisite** the demo environment now needs (subject = UAMI principalId, not clientId) |
| `Seed-ProductionKeyVault.ps1` | **no longer seeds a placeholder `BFF-API-ClientSecret`.** A placeholder is worse than nothing here: it looks like the credential, so an operator "fixes" auth by populating it and silently restores a secret path |
| `Rotate-Secrets.ps1` | the EntraId rotation is now an explicit **audited no-op**, not a deletion. `-SecretType All` is a documented entry point; silently dropping a branch would make `All` quietly narrower than its name while still reporting success |
| `Register-EntraAppRegistrations.ps1` | **`-SkipClientSecret` added** — the acceptance criterion (task 030 carry-forward W-1) |
| `Test-EntraAppRegistrations.ps1` | dropped `BFF-API-ClientSecret` from `$expectedSecrets`, **added the inverse assertion** that it must stay absent, and rewrote the skip message |
| `README.md` | documented `-SkipClientSecret` as required for new registrations, and that a SKIPPED token test is now the correct result |

### 4.2 Left alone, with the reason recorded — 8

| Script(s) | Why it stays |
|---|---|
| `Create-NewContainerType.ps1`, `Register-BffApiWithContainerType.ps1`, `Test-SharePointToken.ps1` | **ADR-028 E-1** — the SPE *owning-app* credential. Container-type registration is a client-credentials call a managed identity cannot make, so a secret is correct here. Their dead *"retrieve it from Key Vault"* instruction was corrected: in dev the owning app **is** the BFF app registration, so its secret went with the migration |
| `Deploy-DataverseSolutions.ps1`, `Deploy-Release.ps1`, `Provision-Customer.ps1` | a **different** credential — the service principal for Dataverse solution import (`SPAARKE_SP_CLIENT_SECRET`) and per-customer provisioning. Out of scope |
| `naming-conformance-check.ps1` | it **flags** the duplicate alias as a rotation hazard; it never consumed it. Its `BFF-API-ClientSecret` occurrences are **synthetic self-test fixtures** exercising rule R2. Editing them would break the checker's own tests to no purpose. Added a note that the live instance is resolved while R2 itself is unchanged |
| `_archive/Get-ContainerMetadata-PCFApp.ps1` | archived |

### 4.3 The `-SkipClientSecret` design decision

**Opt-in, not the default.** Flipping the default would silently change behaviour for every existing
caller of an identity-provisioning script — including `customer-provisioning-orchestration-r1`, whose
Wave G-3 consumes this exact file. A silent behaviour change in provisioning is how you get an
environment that is subtly different from the one you tested. Opt-in keeps the change visible at the
call site; `scripts/README.md` now states it is required for new registrations.

The switch suppresses the secret mint **and** its Key Vault write, but still stores `BFF-API-ClientId`,
`BFF-API-Audience` and `TenantId` — those are identifiers, not credentials, and downstream config
resolves them by name.

### 4.4 The two stale C# comments (§2.1) — corrected, not deleted

`DataverseServiceClientImpl.cs:18-20` and `:61` both instructed readers **not** to remove
`API_CLIENT_SECRET`. Both were true before task 022 and were never refreshed. Rewritten to state what is
true now **and to quote what they used to say and why it was wrong** — the same treatment given to
`auth.md:108`, and for the same reason: a silently-corrected falsehood teaches nobody why it survived.

### 4.5 Verification

- All 10 modified `.ps1` files parse (`System.Management.Automation.Language.Parser`) — a brace mismatch
  in `Register-EntraAppRegistrations.ps1` would have shipped a broken provisioning script.
- `-SkipClientSecret` confirmed exposed and bound as `SwitchParameter` (18 params, 7 switches).
- `dotnet build src/server/api/Sprk.Bff.Api/` — **Build succeeded**, 0 errors, 7 pre-existing unrelated
  `CS0618` warnings.

---

## 5. Steps 5–7 — COMPLETE

### 5.1 Step 5 — the doc sweep: **33 files, not the POML's ~25 / the notes' 13**

Classified before editing. **Historical records were deliberately NOT rewritten** —
`.claude/CHANGELOG.md`, `docs/assessments/*`, `.claude/AUDIT-FINDINGS-AUTH-SYSTEM.md` are point-in-time,
and rewriting them would destroy the audit trail this project's whole argument depends on.

**The false premise was hunted as a sentence-shape, not a string**, and two live instances had survived the
2026-08-17 A4 pass:
- `docs/standards/oauth-obo-patterns.md:13` — *"Requires confidential client (**has secret**)"*. The
  canonical OBO standard doc, still asserting the thing that made three audits conclude the secret was
  permanent.
- `docs/guides/auth-deployment-setup.md` ×3 — *"Still required for OBO (OAuth spec mandates middle-tier
  confidential credential)"*.

Also corrected: *"A 200 confirms … OBO token exchange to Graph works (`BFF-API-ClientSecret` valid)"* — a
200 identifies no credential while anything sits beneath MI-FIC in the order. The same trap, one more place.

**Method, stated plainly**: 16 operational guides received a prominent banner at the top; 5 files received
targeted line edits where the text instructs an *action* rather than describing state. A banner is a real
correction at the point of use, but it is **not** the same as rewriting all 33 files line by line — where a
page has many descriptive mentions, the banner supersedes them collectively and says so.

**The most dangerous stale text in the estate** was `appsettings.template.json`, a live artifact:

> *"WARNING: DO NOT remove the secret backing this setting — the shared-lib Dataverse path STILL
> hard-requires it; removing it from Key Vault CRASHES the BFF at startup."*

True when written (2026-08-13), invalidated by task 022 (migrated the call sites) and 024 (relaxed
`[Required]`+`ValidateOnStart`), never refreshed — a direct instruction not to do what this task just did,
**disproven empirically the same afternoon**. Old text quoted in the replacement so it cannot quietly return.

### 5.2 Step 6 — ADR-028 E-3 CLOSED

Four edits to ADR-028 (closure banner, the `Remediation TODO: OPEN` line discharged, and both
forward-referencing MUST-NOTs, so the exception cannot be cited by someone who reads only the rules).

**`.claude/skills/adr-check/SKILL.md` was the load-bearing one.** Its ADR-028 A4 row told agents to *cite
E-3 and NOT report those sites as violations*. With E-3 closed and its enumeration empty, that directive
would have gone on excusing precisely what this project removed. It now reports them as violations and adds
two checks: a secret listed **beneath** MI-FIC in the order, and `RequireSecretFreeIdentity` absent/false
outside Development.

`.claude/constraints/auth.md` — both halves of the task-030 carry-forward (ADR-check W-6) discharged:
the stale `Last Updated: 2026-05-19` header (with a comment explaining that stale review metadata **on this
file** is itself part of how the false sentence survived three audits), and the **FIC provisioning shape**
the file never carried: subject = **principalId, not clientId** (`AADSTS700213`), issuer, audience,
same-tenant requirement, the ~2-minute `AADSTS70025` propagation flap, and resolve-by-resource-ID.

> ⚠️ Merged into `.claude/constraints/auth.md` while **PR #812** also modifies it (§0.2). Edits were kept
> additive and localised to reduce conflict surface, but a merge conflict there is expected and should be
> resolved in favour of keeping **both** changes.

### 5.3 Step 7 — `Graph-API-ClientSecret` deleted

Provenance confirmed the orphan diagnosis: created **2025-09-29**, **never once updated**, zero code/config
consumers (my grep and `code-quality-and-assurance-r3` HYGIENE-2 agree independently). Deleted 17:34:10Z,
24 → 23, recoverable to 2026-11-22. It was **not** an alias of `BFF-API-ClientSecret` despite five documents
calling it one — different fingerprint, measured (§2.2).

---

## 6. 🔴 The near-miss: the task's own carried-forward obligation was unsafe

The obligation read: *"also delete `ClientSecret` from the default order in `AddCredentialSelection`."*

**Executing it literally breaks every unconfigured environment**, and it was caught only because a test was
watching. `CredentialSelectionOptionsValidator` fails fast when `ManagedIdentityFederated` is the *only*
credential and no UAMI clientId is set — correctly, since there would be nothing to fall through to. But
**every test fixture in this repo and every local `dotnet run` has neither** a `Graph:Credentials` section
nor a UAMI (a workstation has no route to IMDS). A MI-FIC-only *default* stops all of them booting.

`CredentialOrderingSeamTests.Startup_WithNoCredentialSectionAtAll_BootsOnTheCanonicalOrder` failed
immediately. Its own comment had already predicted this — *"task 010 already shipped that exact regression
once in this project"* — as had the `AddCredentialSelection` comment (FAILURE-MODES **AP-7**: converting a
silent fallback into fail-fast has unbounded blast radius; **the default is what bounds it**).

**Reverted.** `ClientSecret` stays in the canonical default, with the reasoning recorded at the code so it
is not "fixed" again. This is not a weakening:

| | |
|---|---|
| Where the guarantee is delivered | **Configuration on deployed environments** — `Order=[ManagedIdentityFederated]` + `RequireSecretFreeIdentity=true` |
| Why that is *better* than a narrower default | explicit, auditable, per-environment, and it cannot silently disable local development |
| Why it is not weaker | the secret is deleted from app settings **and** Key Vault — on a deployed environment the default has nothing left to resolve even if reached |

The ClientSecret **branch** in `OrderedCredentialClientProvider` is likewise kept deliberately: removing it
would make rollback a redeploy, violating **NFR-06** ("rollback is config-only at every phase") on the
highest-blast-radius auth surface in the system.

Two tests now pin this: the canonical-default assertion (with the reasoning), and a new
`Startup_WithTheDeployedSecretFreeConfiguration_BootsAndCannotReachAClientSecret` pinning the post-033
deployed shape — including that narrowing the order **actually takes effect** rather than having the binder
merge a trailing `ClientSecret` back in.

---

## 7. Final state and verification

```
17:41:26Z   slots ................ default only (staging deleted at 032)
            app settings ......... NO BFF-identity secret. Only PowerBi__ClientSecret (task 042, deferred)
            Graph__Credentials__Order__0 .................. ManagedIdentityFederated
            Graph__Credentials__RequireSecretFreeIdentity . true
            Key Vault ............ zero BFF-identity secrets; spe-owning-app-secret INTACT (E-1)
17:40:36Z   OBO .................. all green, 68 bytes BYTE-IDENTICAL, 15/15 + 15/15
            dotnet build ......... 0 errors
            BFF test suite ....... 10,615 passed / 0 failed / 97 skipped
            ArchTests ............ 56 passed / 0 failed
            PowerShell ........... all 10 modified scripts parse
```

### Acceptance criteria

| Criterion | |
|---|---|
| Secret removed from **both slots** | ✅ one slot exists (032 deleted staging); negative case satisfied by construction |
| No BFF-identity code path resolves a secret | ✅ order has no fallback; nothing left in KV or app settings to resolve |
| Secret + lowercase alias gone from Key Vault | ✅ both, soft-deleted (recoverable to 2026-11-22, **not purged**) |
| Office add-in deploy still succeeds | ✅ **never depended on it** (§0.1) — the premise was false; deploy path untouched |
| All scripts stop referencing it or document why | ✅ 15 scripts: 7 modified, 8 documented |
| ADR-028 E-3 closed + `auth.md` reflects end state | ✅ incl. both task-030 carry-forward specifics |
| `Register-EntraAppRegistrations.ps1` no longer mints unconditionally | ✅ `-SkipClientSecret` (opt-in — §4.3) |
| Negative: E-1 + PowerBi untouched | ✅ verified live |
| Negative: §6.1 OBO checklist still passes | ✅ green at every rung |

### Left for the owner (booked to 090)

1. 🔴 **Partial secret values are in git history** (§5, commit `c1803e99a`, 2026-03-09). Redacted in the
   working tree; history untouched. `Dataverse-Checkout-20251218` is still a valid credential until
   2027-12-18. **Recommended: rotate/delete it — cheap now precisely because nothing reads it.**
2. 🔔 **Local-dev OBO has no credential path for a fresh setup** (§3.3). Recoverable in one command; the
   owner may want a deliberate replacement rather than inheriting this.
3. `AZURE_CLIENT_ID` is still set and logs an ERROR every boot (§2.9) — 031 hygiene, never done,
   deliberately not bundled here because the Azure Identity SDK reads that variable itself.
5. Sweep the 13 docs/config/workflow files — including correcting the false add-in claim in 4 places.
6. Close ADR-028 E-3; update `.claude/constraints/auth.md` + `Sprk.Bff.Api/CLAUDE.md:110,221`.
   **CONTENDED with PR #812 — see §0.2.**
7. `Graph-API-ClientSecret` — **not an alias** (§2.2). Identify its owner before deciding.

Also outstanding: clear `AZURE_CLIENT_ID` (§2.9), and the code-side default order in
`AddCredentialSelection` still lists `ClientSecret` (carried-forward obligation) — a code change, so it
lands with the sweep, not with the live config.

## 4. Prep carried in from 032

- App-setting **name** baseline: [`notes/appsettings-baseline-pre-033.md`](../appsettings-baseline-pre-033.md).
  Every delta in §2.4 is attributable against it.
- Estate numbers corrected: **15** scripts, 13 docs/config/workflow files.
- **Key Vault name is `spaarke-spekvcert`**, not `spaarke-spekv-dev` (which does not resolve).
- ⚠️ **`spe-owning-app-secret` is in the same vault and is OUT OF SCOPE** — ADR-028 **E-1**, per-customer
  owning apps. Do not touch.
