# Task 062 — the startup assertion, and why it asserts the order rather than a resolution

> Implemented 2026-08-21. FR-F3. `IdentityConfigurationValidator` rule 6 + `CredentialSelectionOptions.RequireSecretFreeIdentity`.

---

## 1. The window this closes

Ordered selection falls through on purpose. That is correct **during** the migration and dangerous
**after** it: post-033, a broken MI-FIC with the secret still listed would resolve to the secret, serve
every request successfully, and pass every health check. The failure mode is not an outage — it is an
outage that never appears, and the project would quietly be back where it started while every signal
stayed green.

## 2. The design decision: assert the ORDER, not an observed resolution

The obvious implementation is "at startup, resolve a credential and check what won." I did not do that,
for three reasons, and the third is the one that makes the choice unambiguous.

1. **It requires a token acquisition on the startup path.** That is this task's own escalation trigger —
   *"cannot determine the resolved credential type without performing a token acquisition — STOP; that
   reintroduces the eager-connect crash risk"* — and it is the `#3b` SIGABRT shape.
2. **It would be wrong even if it were safe.** Entra federated-credential propagation was **measured at
   task 030** to flap for roughly two minutes (`AADSTS70025`). A startup probe landing inside a flap
   would resolve to the secret and refuse to boot, converting a transient Entra state into a hard
   outage — exactly the class of harm this rule exists to prevent.
3. **The configuration form is strictly stronger.** Observing one resolution says *the secret was not
   used this time*. Asserting the order says *the secret cannot be used at all*: with `ClientSecret`
   absent there is nothing beneath MI-FIC to fall through to, so a broken MI-FIC fails loudly **by
   construction**. That is the property FR-F3 actually wants, and it is determinable with zero I/O.

## 3. Config-gated, inert by default

`Graph:Credentials:RequireSecretFreeIdentity`, default **false**.

Until task 033 removes the secret, `ClientSecret` is the *intentional* lowest-priority fallback and the
rollback mechanism NFR-06 depends on. A guard that fired now would block the very rollout it protects:
task 031 deploys with the secret still listed, and 032 swaps with it still listed. The POML constraint
is explicit — *"gate it on configuration, not on a hardcoded date"* — and the default direction matters
as much as the gate: **forgetting to enable it leaves the guard silent rather than breaking a
deployment.**

That silence is itself a risk, so it is booked as a **binding constraint on task 033's POML** rather
than as prose here: 033 must set the flag in the same change that drops `ClientSecret` from the order.

## 4. ⚠️ Two deviations from the POML's `<outputs>`, both deliberate

The outputs list names `ManagedIdentityAssertionProvider.cs` and `Program.cs`. Neither was changed.

- **`ManagedIdentityAssertionProvider` must not fail at construction.** That is task 020's contract, and
  task 021's fall-through depends on it: the provider is constructed before anyone knows whether a
  managed identity exists, and a throwing constructor would turn "no MI on this host" into a boot
  failure on every developer workstation. A startup guard cannot live there.
- **`Program.cs` needs no change.** `AddCredentialSelection` already wires `ValidateOnStart`, and
  `IdentityConfigurationValidator` is already registered against `CredentialSelectionOptions`. Adding a
  second registration would duplicate the credential order in two places — the thing task 023 put this
  validator in exactly this position to avoid.

Rule 6 therefore sits with rules 1–5, under the same `ValidateOnStart`. Stated plainly rather than
manufacturing a change to match the output list — the same call task 023 and 024 made.

## 5. Not `ValidateOnBuild` — the constraint that keeps this safe

`ValidateOnStart` runs options validation during host start. It constructs no singletons, opens no
connections, and acquires no tokens. `ValidateOnBuild` is what constructed singletons on the startup
thread in `#3b` and aborted the process with SIGABRT. The distinction is the whole reason this guard can
be a startup check at all.

## 6. Development is exempt; an unknown environment is not

A developer workstation has no route to IMDS, so MI-FIC cannot be minted there — the user-secret
fallback is the legitimate and only way to run OBO locally. Failing there would make the guard's first
observable effect "nobody can run the BFF."

A **null** `IHostEnvironment` (direct construction, tooling) is treated as **non-Development**. Defaulting
an unknown environment to *exempt* would make a fail-fast guard silently inert precisely where its
absence matters most — which is the shape of the false premise that survived three audits.

## 7. Verification

| Criterion | Evidence |
|---|---|
| Enabled outside Development with a secret-backed credential ⇒ startup fails with an actionable message | `Rule6_WhenEnabledOutsideDevelopment_AndTheSecretIsStillListed_FailsFast` — message names the flag, the credential AND the environment |
| Development is unaffected | `Rule6_InDevelopment_IsExempt_EvenWithTheSecretListed` |
| **Negative**: inert while the secret is still the intentional fallback (pre-033) | `Rule6_WhileTheSecretIsStillTheIntentionalFallback_IsInert` — this is the control that lets 031/032 proceed |
| **Negative**: no eager connection / `ValidateOnBuild` introduced | Rule 6 is a pure configuration comparison inside an existing `IValidateOptions`; no I/O, no container construction (§5) |
| The end state actually passes | `Rule6_WhenEnabledOutsideDevelopment_AndTheSecretIsGone_Succeeds` |
| Unknown environment is conservative | `Rule6_WithNoHostEnvironment_TreatsTheEnvironmentAsNonDevelopment` |
| Suites | auth seams **60 / 60** · full suite **10,596 / 0** (97 skipped) · ArchTests **49 / 49** |

## 8. Booked onward

- **033** — booked as a **POML constraint**, not prose: set `Graph:Credentials:RequireSecretFreeIdentity=true`
  in the same change that drops `ClientSecret` from the order, and update the census entry's
  `CredentialSource` line to stop saying "transitional secret".
- **Operators** — during a deliberate emergency rollback, set the flag to `false` for its duration. That
  records the deviation instead of hiding it, and pairs with the provider's existing error-level A4
  deviation log.
