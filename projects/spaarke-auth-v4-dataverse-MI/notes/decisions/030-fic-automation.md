# Decision record — task 030: FIC provisioning automation (spec FR-C4)

> **Date**: 2026-08-21 · **Task**: `030-fic-provisioning-automation.poml` · **Rigor**: FULL
> **Output**: `scripts/Register-EntraAppRegistrations.ps1` (+835 / −3, additive)

---

## 1. What was built

Idempotent managed-identity federated-credential (MI-FIC) creation, added to the **existing**
app-registration script per POML constraint 5. Seven functions plus one execution section, all inert
unless `-CreateFederatedCredential` / `-FicOnly` / `-ExportFunctionsOnly` is passed.

| Function | Role |
|---|---|
| `Resolve-SpaarkeUserAssignedIdentity` | UAMI lookup **by ARM resource ID**, never by name |
| `Assert-SpaarkeFicTenancy` | Refuses a cross-tenant pair (see §4) |
| `Get-SpaarkeFederatedCredential` | Name lookup |
| `Find-SpaarkeEquivalentFederatedCredential` | **Triple** lookup — the real idempotency key (§3) |
| `Test-SpaarkeFederatedCredentialShape` | Structural verification (§2) |
| `Get-SpaarkeManagedIdentityAssertion` | Mints the assertion; `$null` off-Azure (§5) |
| `Test-SpaarkeFicTokenExchange` | The real exchange, with bounded 70021 retry (§2) |
| `New-SpaarkeFederatedCredential` | Orchestrates 1→5 |

Provisioning consumes it either as `-FicOnly` (exit-coded) or by dot-sourcing with
`-ExportFunctionsOnly` and calling `New-SpaarkeFederatedCredential` directly.

---

## 2. The central design decision: structural check **before** the retry loop

Two acceptance criteria look independent and are not:

- **Criterion 3** — a FIC whose subject is the UAMI's *clientId* must be **detected**
- **Criterion 4** — `AADSTS70021` immediately after creation must be **retried**

**Both produce the same `AADSTS70021` at exchange.** A retry-on-70021 loop therefore cannot, on its
own, distinguish *wrong forever* from *right in thirty seconds*. Implemented naively, a permanently
misconfigured credential looks like slow propagation until the timeout expires, and then reports a
timeout — the least useful of the available diagnoses.

**Resolution**: `Test-SpaarkeFederatedCredentialShape` runs first, comparing the subject against the
`principalId` read from the identity resource itself. Once it passes, `AADSTS70021` has exactly one
remaining explanation, and retrying it is correct rather than merely hopeful. The timeout message says
so explicitly and lists what is left to check.

This is why `$script:PropagationErrorCodes` contains exactly one code and carries a comment telling
future maintainers not to widen it. Every other AADSTS code here is a configuration fault that retrying
only delays.

**Verified**: config-layer fault fails in **1 attempt / 0 s** against a 600 s budget; the propagation
path retries 5 s → 10 s and respects a 20 s budget (3 attempts, 16 s).

### 2a. Credential layer vs authorization layer

The exchange asks *"did Entra accept this assertion as this app's credential?"* — not *"does this app
have permissions?"*. Conflating them would fail verification on every freshly provisioned app
registration, before any grants exist. So `AADSTS500011` (resource principal not found) is treated as
**PASS**: Entra evaluates the resource only after accepting the client credential.

---

## 3. Idempotency is keyed on the triple, not the name — found by running it, not by reasoning

The first live run derived the default name `mi-mi-bff-api-dev-assertion` (the UAMI is itself named
`mi-bff-api-dev`), missed the existing `mi-bff-api-dev-assertion`, and proceeded to create.

**What actually happens then — measured, not assumed.** Entra enforces `(issuer, subject)` uniqueness
per application itself and rejects the create:

```
ERROR: The combination of issuer and subject must be unique for the application.
```

So the duplicate is **not** silently created; the platform is a backstop. The defect is that a re-run
against an already-correct, already-working credential **fails instead of being a no-op** — precisely
what criterion 2 rules out ("*a no-op, not an error or a duplicate*").

> ⚠️ **Correction on the record.** An earlier version of this analysis — and of the code comment —
> claimed the create would have succeeded silently and was stopped only by a missing Application
> Administrator role. That was **wrong**. The observed rejection is a *validation* error, not an
> *authorization* one. Both the comment and this record now state the verified mechanism. Recorded
> rather than quietly amended, because a confidently-wrong sentence about auth mechanics is exactly
> the failure mode this project exists to correct (`.claude/constraints/auth.md:108`).

**Fix**: two parts. The name derivation drops the redundant `mi-` prefix, and — the part that matters —
`Find-SpaarkeEquivalentFederatedCredential` checks for the `(issuer, subject, audience)` triple under
*any* name before deciding to create. That converts the platform's error into the correct answer,
"already satisfied, nothing to do".

---

## 4. Cross-tenant: refused, not parameterised (escalation trigger **not** fired)

The POML carries an escalation trigger: *stop if PROVISIONING-CHANGE-REQUEST §9.2 is unanswered when
**parameterising for cross-tenant use***. §9.2 is still unanswered.

The trigger did **not** fire, because the code does not parameterise for cross-tenant use — it
**refuses** it. `Assert-SpaarkeFicTenancy` throws when the app-registration tenant and the UAMI tenant
differ, and the message names §9.2 and both remedies (own-stamp UAMI, or the ADR-028 A4 certificate
alternative).

Refusing beats a flag here: a cross-tenant FIC **creates successfully** and fails only at exchange, so
an option to attempt it is an option to produce a silent failure. If §9.2 resolves as reading (b) —
customer stamps must trust the shared Spaarke UAMI — MI-FIC is structurally impossible for that shape
and no parameter would have helped.

---

## 5. Verification is honest about what it cannot prove

A UAMI assertion can only be minted from inside Azure on compute carrying the identity; a workstation
cannot produce one at all (PHASE-0 §4). Rather than let that degrade into "created OK", the run reports
three distinct states and **three distinct exit codes**:

| Exit | State | Meaning |
|---|---|---|
| `0` | `Verified` | A real token exchange succeeded (or `-AllowUnverified` was passed) |
| `1` | `Failed` | Create failed, drift refused, structurally invalid, or the exchange was rejected |
| `2` | `Unverified` | Structurally correct, but **not exchange-provable from this host** |

`2` is distinct from `1` on purpose: provisioning needs to tell *not proven* from *proven broken* —
they call for different follow-ups. Default is to exit non-zero rather than report success;
`-AllowUnverified` is the deliberate opt-out. The unverified message prints the exact two commands to
mint an assertion on the right compute.

**Drift is reported, never silently overwritten.** A credential in this position may be in active use;
replacing one is an availability event, not a repair. `-ForceFederatedCredentialUpdate` is the explicit
opt-in.

---

## 6. What was verified, and what was not

**Verified live** (dev tenant, `1e40baad-…`, UAMI `mi-bff-api-dev`):

| # | Check | Result |
|---|---|---|
| 1 | Existing dev FIC detected, no-op (default name) | ✅ exit 2, `Created: False` |
| 2 | Different name, same triple → still a no-op | ✅ refused the duplicate by name |
| 3 | Subject = clientId → detected, named as the conflation | ✅ offline |
| 4 | Wrong issuer / wrong audience → detected | ✅ offline |
| 5 | Cross-tenant → throws, cites §9.2 | ✅ offline |
| 6 | Bad resource-ID shape → throws | ✅ offline |
| 7 | Off-Azure mint returns `$null` in <1 s (no hang) | ✅ offline |
| 8 | Config fault: 1 attempt, 0 s of a 600 s budget | ✅ live Entra |
| 9 | Propagation: retries 5→10 s, respects budget, disambiguating timeout | ✅ live Entra |
| 10 | Create payload shape accepted by `az` / Graph | ✅ reached Entra validation |
| 11 | Existing behaviour unchanged without the new flags | ✅ **byte-identical** `-DryRun` output |

**NOT verified — stated plainly:**

- **Creating a genuinely new FIC end-to-end.** Every candidate `(issuer, subject)` on the dev app
  registration already exists, and creating a throwaway app registration in the shared Entra tenant is
  a directory write beyond this task's scope. The *payload* is proven good (it reached Entra's
  uniqueness validation, not a schema error); the create *call* is exercised only to that point.
- **A successful token exchange.** Requires an assertion minted on compute carrying the UAMI. This is
  scheduled work, not a gap: **task 031** deploys to the slot, where the assertion is mintable. The
  `-AssertionToken` parameter exists for exactly that hand-off.

---

## 7. Conflict check (POML criterion 6)

🛑→⚠️ **Hard warn, downgraded after inspection.** `customer-provisioning-orchestration-r1` (PR **#779**,
open) has already rewritten this same file: **+707 / −257**, an idempotency-contract rewrite of the
app-registration path.

Three-way merge simulation (`base` = master, `ours` = task 030, `theirs` = #779):

- **1 conflict hunk** — both sides append to `param()`. Resolution: keep both (their
  `$SecretExpiryMonths` + the FIC parameters). Mechanically obvious.
- All seven functions, the execution section, and both mode guards survive **exactly once**.
- The `-FicOnly` Key Vault skip lands correctly on their restructured pre-flight — checked
  specifically, because a clean *textual* merge can still be semantically wrong.
- All three insertion anchors still exist in their version.

**Their branch contains zero federated-credential code**, so the duplicate-work risk flagged in
PROVISIONING-CHANGE-REQUEST §9.3 has not yet materialised. Landing task 030 first still avoids it.

---

## 8. Follow-ups

1. **Notify `customer-provisioning-orchestration-r1`** that the extension has landed, with the invocation
   contract and the one-hunk merge note (POML step 6).
2. **§9.2 remains unanswered** and must be resolved **before Wave G-3 task 130 executes**. Until then
   cross-tenant is refused at runtime — the failure is loud, but the question is still open.
3. **Exchange verification completes in task 031**, on the slot, via `-AssertionToken`. Task 031 should also
   **re-exercise criteria 3 and 4 there** — it will have a real mintable assertion, so closing that loop is
   nearly free, and there is no Pester harness to catch a regression otherwise (ADR-check W-7).
4. **Combined mode still mints a client secret** — deliberate deferral, recorded rather than fixed here.
   `-CreateFederatedCredential` *without* `-FicOnly` runs the full flow, which unconditionally creates a
   24-month secret and writes it to Key Vault. There is no `-SkipClientSecret`. Post-task-033 that would mint
   a per-customer secret on every onboarding, which ADR-028 **E-3** ("does not license expansion") arguably
   reaches. Not fixed in task 030 because spec **FR-C3 / task 033 explicitly owns this file**, the secret is
   still the ordered fallback and rollback mechanism, and changing that block would both break criterion 5's
   spirit and collide head-on with PR #779's rewrite of exactly that code. `-FicOnly` — the path provisioning
   actually invokes — never reaches it. (ADR-check W-1, Path A.)
5. **Fold into task 033's `auth.md` update**: the file still carries `Last Updated: 2026-05-19` despite the
   A4 correction applied 2026-08-17, and it states the *runtime* rule but nothing about FIC **provisioning**
   shape (same-tenant; subject = principalId, not clientId). FR-C3 already requires `auth.md` to reflect the
   end state — this just names what to include. (ADR-check W-6.)

## 9. Invocation gotcha

Passing `-UamiResourceId` from **Git Bash / MSYS** mangles the leading `/subscriptions/...` into a
Windows path (`C:/Program Files/Git/subscriptions/...`). Invoke from PowerShell, or set
`MSYS_NO_PATHCONV=1`. The error message echoes the received value, so the cause is visible immediately —
but it costs a minute if unexpected.
