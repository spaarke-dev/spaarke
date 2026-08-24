# Task 033 — remove the secret and reconcile the estate

> **Status**: 🔄 IN PROGRESS — steps 1–2 done. **The secret is GONE from the app; still present in Key Vault.**
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

### 0.2 🛑 CONFLICT-CHECK HARD WARN — `.claude/constraints/auth.md` is contended

Step 6 must edit `.claude/constraints/auth.md` (to close ADR-028 exception **E-3**).
**PR #812 (`work/unified-access-control-r2`) modifies the same file.**

Per the `/conflict-check` decision table this is the *hard warn* case: watchlist hot-path (skill
directives) + another active worktree + **file overlap**. Surfaced for coordination before step 6, not
silently merged into.

Also on that PR: `.claude/agent-memory/researcher/**` (no collision — append-only memory).
PRs #806 and #779 touch `.claude/` but **not** `constraints/auth.md`; #806 and #779 both touch root
`CLAUDE.md`, which 033 does not.

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

## 3. Steps 3–7 — NOT STARTED

| | |
|---|---|
| App settings | ✅ **4 BFF-identity secret keys DELETED.** `PowerBi__ClientSecret` remains (task 042, deferred — correct) |
| Key Vault `spaarke-spekvcert` | `BFF-API-ClientSecret` + `bff-api-client-secret` **both still present** |
| Credential order | `[ManagedIdentityFederated]` — explicit, no fallback |
| `RequireSecretFreeIdentity` | **true** |
| Rollback | rung 2′ (restore from KV) |

Remaining, with the corrections from §0.1 and §2.2 folded in:

3. Delete the KV secret + lowercase alias **without `--purge`**; re-verify **`Sync-LocalConfig.ps1` /
   local `dotnet run`** (criterion 9 — *not* the add-in deploy).
4. Sweep the **15** scripts (not 11). Includes the two stale `DataverseServiceClientImpl` comments (§2.1).
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
