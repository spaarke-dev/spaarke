# Assessment — Eliminate the BFF client secret via Managed Identity (OBO → MI Federated Credentials)

> **Status**: INVESTIGATION SEED (pre-research) · **Date**: 2026-08-17
> **Origin**: surfaced during `code-quality-and-assurance-r3` task 011/#3b (the app-only Dataverse MI migration).
> **Author note**: this writeup captures what was found live on `spaarke-bff-dev`; it is a **seed for a proper
> research + scoping pass**, not a finished design. The central claim ("OBO can be secret-free via MI-FIC") must be
> validated against Microsoft's current guidance and our tenant/app-registration reality before any code is written.

---

## 1. What #3b actually delivered, and the wall it hit

`#3b` (task 011) migrated the **two primary Dataverse implementations** — `DataverseServiceClientImpl` (SDK
`ServiceClient`) and `DataverseWebApiService` (REST) — from **client-secret** auth to **Managed Identity**,
flag-gated on `Graph:ManagedIdentity:Enabled`, ClientSecret retained as fallback. **Proven live on dev** (SDK →
`/api/dataverse/metadata/*` = 200; WebApi → `/api/v1/field-mappings/profiles` = 200). ✅

Then the intended "final step — remove the client secret" was investigated and found to be **impossible as framed**.

## 2. The finding (verified live on `spaarke-bff-dev`, 2026-08-17)

The three secret app-settings are **the same secret value** — all reference `BFF-API-ClientSecret`:

| App setting | Value (dev) | Consumed by |
|---|---|---|
| `AzureAd__ClientSecret` | `l8b8Q~…_a6-` | **OBO** confidential client (Graph + Dataverse delegated) |
| `API_CLIENT_SECRET` | `l8b8Q~…_a6-` (same) | Graph OBO fallback, `DataverseAccessDataSource`, `DataverseUserClient`, `DataverseWebApiClient` |
| `Dataverse__ClientSecret` | `l8b8Q~…_a6-` (same) | what #3b migrated (now MI); still `[Required]` in `DataverseOptions.cs:32` + `ValidateOnStart` |

**Consumers beyond the two impls #3b migrated** (i.e., still on the secret):
- `Infrastructure/Graph/GraphClientFactory.cs` — the **OBO** path builds `IConfidentialClientApplication` with
  `.WithClientSecret(clientSecret)` and calls `AcquireTokenOnBehalfOf(...)`.
- `Spaarke.Dataverse/DataverseAccessDataSource.cs` — **OBO** for Dataverse row-level access
  (`_cca.AcquireTokenOnBehalfOf`); literally comments `_cca = null; // No OBO support with managed identity`.
- `Services/Ai/Handlers/Dataverse/DataverseUserClient.cs` — user-delegated Dataverse.
- `Spaarke.Dataverse/DataverseWebApiClient.cs` — reads `API_CLIENT_SECRET`.

**Consequence:** removing the KV secret / config keys would break OBO (all delegated user auth), three other
Dataverse callers, AND crash startup (the `[Required]` validator). The secret is **not removable** in the current
architecture. The standing "never remove `BFF-API-ClientSecret`" rule is correct.

## 3. The core architectural distinction (app-only vs OBO)

| Flow | Meaning | Auth today | MI-capable? |
|---|---|---|---|
| **App-only** (`DataverseServiceClientImpl`, `DataverseWebApiService`) | BFF calls Dataverse **as itself** (system/background) | ✅ **MI** (#3b) | Yes — simple, done |
| **OBO / delegated** (`DataverseAccessDataSource`, `DataverseUserClient`, Graph OBO) | BFF exchanges the **user's** token to act **as the user** | KV client secret | Yes — but via a *different* mechanism (§4) |

## 4. Is OBO fundamentally incompatible with MI? **No.**

The OAuth 2.0 On-Behalf-Of flow requires the middle tier to authenticate with a **confidential-client credential**.
That credential can be: (a) a **client secret** (today), (b) a **certificate**, or (c) a **client assertion from a
Managed Identity configured as a Federated Identity Credential (FIC)**. Option (c) — "workload identity federation
with a managed identity" — is Microsoft's supported path to eliminate the secret **including for OBO**.

**Mechanism (to be validated in research):**
1. **App registration**: add a *Federated Identity Credential* on the BFF app registration trusting the UAMI
   `mi-bff-api-dev` (audience `api://AzureADTokenExchange`). Per environment.
2. **Code**: swap every confidential client from `.WithClientSecret(secret)` →
   `.WithClientAssertion(async () => <MI token for api://AzureADTokenExchange>)` (MSAL.NET supported). Touches
   `GraphClientFactory`, `DataverseAccessDataSource`, and any other `IConfidentialClientApplication`.
3. **Then** `BFF-API-ClientSecret` can be removed from Key Vault (true zero-secret).

So **(b) "Dataverse/OBO cannot use MI" is FALSE**; **(a) "the OBO components are wired to the KV secret and need
more extensive surgery" is TRUE.** The current `// No OBO support with managed identity` comment describes today's
wiring, not a platform limitation.

## 5. Why was this not raised in the prior auth audits? (the honest answer)

This is the important question, and the answer is nuanced — it is **not** a hardening miss:

1. **The prior audits got the current state RIGHT.** ADR-028 §24, AUTHV2-042 Phase C, and the BFF Auth Surface Map
   (r3 task 019) all correctly identified that **OBO retains `BFF-API-ClientSecret`** and documented it as
   *never-remove* ("retained ONLY for OBO — OAuth spec mandates a middle-tier confidential credential"). They did
   **not** miss that the secret is needed for OBO; they deliberately kept it.
2. **The unexamined assumption** is the framing that OBO **requires a *secret*** ("OAuth spec mandates"). That is
   true for *a confidential credential* — but a secret is only **one** of three ways to satisfy it. The **MI-FIC /
   client-assertion** alternative (newer, less common) was simply never on the table in those audits, so
   "eliminate the OBO secret too" was never scoped. That is a **scope/knowledge boundary, not a defect** in the
   hardening work.
3. **The actual error was recent and mine.** During #3b I framed "remove `API_CLIENT_SECRET` / `Dataverse-ClientSecret`"
   as the migration's final step **without recognizing they are the same shared OBO secret**. The prior audits had
   this correct (never remove); my framing conflated "the two app-only impls now use MI" with "the secret can go."
   Catching that is what surfaced this whole question. The retraction is documented in
   `code-quality-and-assurance-r3/notes/task-011-ng1-3b-mi-migration.md`.

**Net:** nothing was "missed." What is *new* is the **possibility** of going further than ADR-028 currently
commits to — eliminating even the OBO secret via MI-FIC. Whether that is worth doing (vs. accepting an
OAuth-standard confidential secret for OBO) is exactly the question this project must research before scoping.

## 6. Open questions for the research/scoping phase (do NOT pre-decide)

1. **Is the goal even "zero secrets"?** ADR-028 currently accepts the OBO secret as standard/acceptable. Is full
   elimination a real requirement, or is a well-managed, rotated KV secret + certificate option sufficient? (A
   **certificate** is the more conventional secret-free confidential credential and may be lower-risk than MI-FIC.)
2. **Does MI-as-FIC actually work for our OBO exchange** against both Graph and Dataverse in our tenant, on the
   App Service MI source? (Prototype in a non-prod slot; MI-FIC has platform prerequisites and regional caveats.)
3. **Scope of blast radius**: OBO is the highest-risk auth surface — breaking it breaks *all* delegated user auth
   (SPE documents, chat, Office add-ins, Dataverse row-level access). What is the staged rollout + rollback?
4. **Per-env app-registration work**: the FIC must be configured on the app registration in each environment
   (dev live now; demo/prod when re-provisioned). Operator + Azure AD admin task.
5. **Certificate vs MI-FIC**: compare the two secret-free options on operational cost, rotation, portability, and
   risk. MI-FIC removes rotation entirely but is newer; certificates are conventional but need rotation.
6. **ADR outcome**: this likely amends ADR-028 (§24 confidential-credential position). What does the amended ADR say?
7. **Interaction with `dataverse-access-unification-r1`** (RED-4 C) and #3b: those touch the app-only paths; this
   touches OBO. Sequence to avoid churn on the same files (`GraphClientFactory`, `Spaarke.Dataverse`).

## 7. Recommendation

- Treat this as a **research-first project**: a proper investigation/spike to (1) confirm the correct problem
  (is zero-secret actually required?), and (2) choose the correct solution (MI-FIC vs certificate vs status-quo),
  **before** any OBO code changes. OBO is not a surface to "wing" in-session.
- If it proceeds, it is an **ADR-028 amendment + staged OBO credential migration**, per env, with slot-based
  rollout and explicit rollback — not a quick change.

## 8. Evidence pointers

- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` (OBO CCA + `WithClientSecret`, lines ~20-21, 55, 83-88)
- `src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs:22,49-75,105-130` (OBO CCA; `// No OBO support with managed identity`)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/Dataverse/DataverseUserClient.cs:85` · `Spaarke.Dataverse/DataverseWebApiClient.cs:39`
- `src/server/api/Sprk.Bff.Api/Configuration/DataverseOptions.cs:32` (`[Required]` + ValidateOnStart)
- `code-quality-and-assurance-r3/notes/task-011-ng1-3b-mi-migration.md` (the #3b journey + the "remove secret" retraction)
- ADR-028 (`.claude/adr/ADR-028-spaarke-auth-architecture.md`) §24 (MI mandate + OBO-secret retention)
- Microsoft: "Workload identity federation" / "Configure an app to trust a managed identity" (MI-as-FIC) — **verify current guidance in research**.
