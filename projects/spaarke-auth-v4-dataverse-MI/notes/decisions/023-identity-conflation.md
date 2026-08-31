# Task 023 — the two identities, the decoy, and the live trap

> Implemented 2026-08-21. Every fact below was verified against the live tenant or the repo, not recalled.

---

## 1. The two identities

MI-FIC requires the BFF to hold **two different identities at once**, and confusing them is this
project's designated silent-failure mode (FR-B4).

| Identity | What it does | Where it comes from | Live dev value |
|---|---|---|---|
| **User-assigned managed identity** | **MINTS** the client assertion | `Graph:ManagedIdentity:ClientId` (canonical) → `ManagedIdentity:ClientId` (legacy) | `5967251e-…` (`mi-bff-api-dev`) |
| **App registration** | What the assertion authenticates **AS** | `AzureAd:ClientId` → `API_APP_ID` | `1e40baad-…` (`SDAP-BFF-SPE-API`) |

The federated credential ties them together: its **subject is the UAMI's `principalId`**
(`9fd47efb-…`) — *not* its clientId, which is the commonest silent error, and which task 030 verified
live returns `AADSTS700213`.

Swap the two and nothing complains at deploy time. The credential is created cleanly, the app boots, and
the only symptom is a token exchange that fails later — on the OBO path, for every user simultaneously.

## 2. The decoy

The dev subscription holds **five** user-assigned managed identities. One is named
**`spaarke-bff-identity`** — as though it were the BFF's — and is **not attached to `spaarke-bff-dev`**.
The BFF's actual identity is **`mi-bff-api-dev`**.

Anything that resolves an identity *by name* will pick the wrong one and fail in the way described above.

**Verified: the runtime cannot do this.** A grep across `src/` for either identity name returns exactly
one hit — a doc comment in `ManagedIdentityCredentialFactory` warning about the decoy. There is no
name-based managed-identity lookup anywhere in the runtime; resolution is always from a configured
clientId, and when none is configured `ResolveUamiClientId` returns **null** rather than searching. That
is asserted in `IdentityConflationSeamTests`, and the structural half (no name-based lookup exists) is
booked onto task 060 where this project's other source-analysis guards live.

## 3. ⚠️ The live trap: `AZURE_CLIENT_ID` holds a managed identity

Verified against `spaarke-bff-dev` on 2026-08-21:

```
API_APP_ID                        = 1e40baad-…   ← app registration
Graph__ManagedIdentity__ClientId  = 5967251e-…   ← UAMI  ✓
ManagedIdentity__ClientId         = 5967251e-…   ← UAMI  ✓
UAMI_CLIENT_ID                    = 5967251e-…   ← UAMI  ✓
AZURE_CLIENT_ID                   = 5967251e-…   ← UAMI  ⚠️
```

`AZURE_CLIENT_ID` is **ambiguous by convention**. The Azure SDK reads it as a *managed identity's*
clientId — which is why it is set to the UAMI here, per
[`auth-deployment-setup.md`](../../../../docs/guides/auth-deployment-setup.md). But
[`GraphClientFactory.cs:54`](../../../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs#L54)
reads it as the *app registration's*:

```csharp
_clientId = configuration["AZURE_CLIENT_ID"] ?? configuration["API_APP_ID"];
```

So on live dev, `GraphClientFactory._clientId` resolves to a **managed identity** where an app
registration is required. Its only consumer is the legacy app-only branch at line 147:

```csharp
credential = new ClientSecretCredential(_tenantId, _clientId, _clientSecret);
```

**It is inert today** — and only because `Graph__ManagedIdentity__Enabled=true` makes that branch dead
code. Set the flag to `false`, which is a plausible move during an incident, and the BFF builds a
`ClientSecretCredential` from a managed identity's clientId paired with the app registration's secret.
The resulting `AADSTS` error names neither identity.

`AZURE_CLIENT_ID` has exactly **one** consumer in `src/` (that line), so the blast radius is bounded and
known.

### Why the escalation trigger did NOT fire

The trigger is *"an existing environment sets both keys to the same value — STOP and report."* The two
**identity keys** hold correct, distinct values: the UAMI keys hold the UAMI, `API_APP_ID` holds the app
registration. What exists instead is a *third* key that is read as the app-registration id while holding
the UAMI's — the same hazard arriving by a different route, and one the task's own `<background>` already
predicted. The task's instruction for it is explicit: *"add the guard and surface the ambiguity. Changing
app-only behaviour is out of scope."* That is what was done.

## 4. The guard

[`IdentityConfigurationValidator`](../../../../src/server/api/Sprk.Bff.Api/Configuration/IdentityConfigurationValidator.cs),
registered as a second `IValidateOptions<CredentialSelectionOptions>` so it runs under the **same
`ValidateOnStart`** as the credential order — the two identities are only meaningful together with that
order (rule 3 depends on it), and validating them separately would duplicate the order in two places.

| Rule | Condition | Outcome |
|---|---|---|
| **1** | UAMI clientId **==** app-registration clientId | **FAIL** — an app registration cannot mint its own managed-identity assertion, so this could never work |
| **2a** | `AZURE_CLIENT_ID` == UAMI, ≠ app registration, **MI disabled** | **FAIL** — this is the moment the trap springs |
| **2b** | same, but **MI enabled** (the live shape) | **LogError** — reported, not fatal |
| **3** | MI-FIC is the **only** credential and no UAMI is set | **FAIL** — nothing to fall through to |
| **4** | *(added by task 024)* no configured credential can be obtained at all | **FAIL** — see [`024-relax-config-validators.md`](024-relax-config-validators.md) |

### Why rule 2 warns instead of failing when MI is enabled

Because the live dev environment is in exactly that state. Failing startup on it would take dev down to
fix a defect that is **not firing** — the shape of the `#3b` incident this project keeps citing. Reporting
at error level puts it in front of an operator without that cost. An inert trap nobody is told about is
just a trap with a longer fuse.

### Why rule 3 is scoped to "no fallback beneath it"

This is the judgment call in the task, and the scoping is load-bearing rather than timid.

Criterion 5 asks that *"MI-FIC selected with either identity unset fails fast rather than at first token
exchange."* Applied unconditionally that would fail startup **everywhere**: the default credential order
is `[ManagedIdentityFederated, ClientSecret]`, and **no developer workstation and no test fixture in this
repo sets `Graph:ManagedIdentity:ClientId`**. Every one of them would stop booting.

With a fallback configured, an absent managed identity is a **designed fall-through condition** (task
021), not an error — it is the ordinary local-dev shape, and the whole reason ordered selection exists.
With **no** fallback there is nothing to fall through to, so the process can only fail at the first token
exchange, which is precisely the failure the criterion is about.

The scoping has a property worth noting: it **tightens automatically**. Once task 033 removes
`ClientSecret` from the order, MI-FIC becomes the only credential and rule 3 becomes strict — exactly
when the project reaches its end state, with no further code change.

## 5. What was NOT changed, and why

- **`GraphClientFactory.cs:54`** — untouched. Task 023's constraint puts changing app-only fallback
  semantics out of scope, and criterion 6 requires it unchanged. Verified: the file has no diff in this
  task.
- **`ManagedIdentityAssertionProvider`** — listed as a `modify` output, but the substantive change it
  implied does not exist. It already resolves the UAMI through the shared resolver and never reads
  `API_APP_ID`, so **no cross-defaulting is present to remove**. The provider also must not fail at
  construction (task 020's contract, on which task 021's fall-through depends), so the guard could not
  live there anyway. Only a cross-reference to the new guard was added. Stated plainly rather than
  manufacturing a change to match the output list.

## 6. Verification

| Check | Result |
|---|---|
| `IdentityConflationSeamTests` | **10 / 10** |
| All five auth seam files | **48 / 48** |
| Full BFF suite (after 023) | **10,582 / 0** (97 skipped) |
| ArchTests | **36 / 36** |
| `GraphClientFactory.cs` diff | **none** — criterion 6 |

## 6a. Side finding: chasing the "flake" found a shipped defect in task 021

While running this task's gates, the intermittent failure in
`ClientAssertionProviderSeamTests` recurred — final tally **2 failures in 4 full-suite runs**, always
passing in isolation. Task 021 had booked it onto task 060 rather than diagnosing it.

Re-running at normal verbosity produced the stack trace, and the stack trace produced a **real defect in
task 021's ordered credential selection**: its fall-through predicate and catch clause were typed to
`MsalServiceException`, but MSAL throws **`MsalClientException`** for
`managed_identity_all_sources_unavailable` — the "no IMDS on this host" case, which is the commonest
fall-through and the entire reason ordered selection exists. A developer workstation would have received
a failed request instead of a fall-through to the secret.

Fixed in both the production predicate and the test, with regression tests pinning the shape MSAL
actually throws. Full write-up:
[`021-credential-config-keys.md` §13a](021-credential-config-keys.md).

## 7. Recommendation for the owner

`AZURE_CLIENT_ID` on `spaarke-bff-dev` (and on the `staging` slot) can be **cleared**. Its single consumer
falls back to `API_APP_ID`, which is the correct value for that use. Clearing it removes the trap rather
than guarding it.

Not done in this task: app-setting changes to a live environment are task 031/032/033 territory, and this
task's constraint keeps app-only behaviour out of scope. **Booked onto task 031**, where slot settings are
being verified anyway.
