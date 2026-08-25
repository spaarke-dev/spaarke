# Task 024 — relaxing the three validators that mandated the secret

> Implemented 2026-08-21. FR-B5.

---

## 1. What was actually there

The task described "three validators that mandate the secret". Reading them first — rather than
relaxing what the task named — turned up something that changes the risk assessment: **two of the three
mandated a secret that no code path reads.**

| Validator | Mandated | Consumers of that property, verified by grep across `src/` |
|---|---|---|
| `DataverseOptions.ClientSecret` `[Required]` | `Dataverse:ClientSecret` | **none.** Only `EnvironmentUrl` is ever read. `ClientId` and `TenantId` are equally unread |
| `GraphOptionsValidator` (MI disabled ⇒ required) | `Graph:ClientSecret` | **none.** The two `.WithClientSecret(_options.ClientSecret)` sites that look like consumers — `ReportingEmbedService:80`, `ReportingProfileManager:77` — take `IOptions<PowerBiOptions>`, a different type with its own secret |
| `AgentTokenOptionsValidator` | `AgentToken:ClientSecret` | **`AgentTokenService.cs:105`** — a real consumer |

So the startup-crash dependency on the client secret was, in two of three cases, a mandate with no
runtime purpose whatsoever: settings that existed only to prevent a secret-free boot.

That materially lowers the risk of relaxing them. It also means the honest description of this task is
not "weaken validation" but "stop requiring values nothing uses, and check the thing that actually
matters instead".

## 2. What changed

| File | Change |
|---|---|
| `DataverseOptions.cs` | `[Required]` removed from `ClientSecret`. `EnvironmentUrl` stays required — it **is** consumed |
| `GraphOptionsValidator.cs` | The "MI disabled ⇒ `Graph:ClientSecret` required" rule removed. The MI-enabled ⇒ `ManagedIdentity:ClientId` rule **retained** — that key *is* read, by `GraphClientFactory` and `ManagedIdentityCredentialFactory` |
| `AgentTokenOptions.cs` | `[Required]` removed from `ClientSecret`; the same check removed from `AgentTokenOptionsValidator`. `TenantId` / `ClientId` / `AgentAppId` / `DataverseEnvironmentUrl` **kept** — they identify *who* the exchange is between, which is required regardless of which credential proves it |
| `IdentityConfigurationValidator.cs` | **Rule 4 added** — the backstop (below) |

## 3. Validation moved, it did not disappear

The constraint is explicit: *"relax means the secret is no longer unconditionally required. It does NOT
mean removing validation — no credential of any kind must still fail fast at startup."*

Whether a usable credential exists is a question about the **ordered credential list**, which none of
these three options types can see. Re-deriving the answer in each of them would be exactly the
per-call-site credential handling ADR-028 A4 exists to end. So the check lives once, in
`IdentityConfigurationValidator` **rule 4**, under the same `ValidateOnStart` as the credential order.

**"Definitely unavailable" is judged conservatively, and the conservatism is the design:**

| Credential | When it counts as provably absent |
|---|---|
| `ClientSecret` | no secret in `AzureAd:ClientSecret`, `API_CLIENT_SECRET` or `AZURE_CLIENT_SECRET` |
| `KeyVaultCertificate` | no certificate name configured |
| `ManagedIdentityFederated` | **never.** An unset UAMI clientId means *system-assigned*, a legitimate shape this validator cannot rule out without a network call |

Rule 4 fires only when **every** configured credential is provably absent — e.g. an order of
`[ClientSecret]` with no secret anywhere. It therefore cannot produce a false positive that breaks a
developer workstation or a fixture, which is the AP-7 constraint every fail-fast change in this project
has to satisfy. Task 023's **rule 3** covers the one MI-FIC case that *is* unambiguously fatal (MI-FIC
with nothing beneath it to fall through to).

## 4. ⚠️ Deviation from step 3 — AgentTokenOptions was NOT moved under `ValidateOnStart`

The POML's step 3 says *"align AgentTokenOptions with the same rule and move it under ValidateOnStart for
consistency."* The steps are `mode="directional"`, so the binding contract is the goal, constraints and
acceptance criteria — and moving it would **violate acceptance criterion 3**.

**Verified, not assumed**: no test fixture seeds an `AgentToken` section. The only file in the repo
containing one is `appsettings.template.json`, which is a template and is not loaded as `appsettings.json`
at runtime. `AgentTokenOptionsValidator` still requires `TenantId`, `ClientId`, `AgentAppId`,
`DataverseEnvironmentUrl` and a non-empty `GraphScopes`, so switching it to `ValidateOnStart` would fail
startup for **every fixture that boots `Program.cs`** — the opposite of *"the 46 fixtures still start and
pass unchanged."*

`AgentModule.cs:19-21` already records the original reason for deferring it: *"AgentToken config may not
exist until Entra app registration is complete."* That reason still holds.

The FR-B5 goal is met without the move: the secret is no longer required, which is what made a
secret-free deployment impossible.

## 5. Escalation trigger — did not fire

The trigger was *"relaxing `DataverseOptions` cascades into unrelated startup validation failures — STOP;
that is the `ValidateOnBuild` hazard that took dev down in `#3b` attempt 1."*

No cascade. The full suite is green, including every fixture that boots the real `Program.cs`. This is
the expected result given §1 — removing a requirement on a property with no consumers cannot cascade into
consumers.

## 6. Verification

| Criterion | Evidence |
|---|---|
| Starts with no secret when a higher-priority credential is available | `Validate_WithNoSecretButManagedIdentityInTheOrder_Succeeds` — this configuration could not boot before |
| **Negative**: no credential of any kind fails fast, naming what is missing | `Validate_WhenNoConfiguredCredentialCanBeObtained_FailsFastNamingWhatIsMissing` |
| **Negative**: the fixtures seeding dummy secrets still start and pass unchanged | Full suite **10,586 / 0**, twice consecutively |
| **Negative**: no eager-connect crash (`#3b` SIGABRT) in any permutation | No cascade; suite green. Permutations covered by the seam tests rather than by booting four hosts |
| Full test suite green | **10,586 / 0** (97 skipped), two consecutive runs · ArchTests **36 / 36** |

## 7. Booked onward

- **`DataverseOptions.ClientId` and `.TenantId` are also unread.** Not removed — task 024's scope is the
  credential mandate, and deleting unused-but-harmless validation is a separate decision. Worth a sweep
  at task 090.
- **`Graph:ClientSecret` is now referenced by nothing at all.** It is a candidate for deletion at task
  033 alongside the other secret settings, and should be included in that reconciliation.
