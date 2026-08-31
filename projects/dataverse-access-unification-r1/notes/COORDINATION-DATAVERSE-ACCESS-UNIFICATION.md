# Coordination → `dataverse-access-unification-r1`

## What `spaarke-auth-v4-dataverse-MI` is doing to `Spaarke.Dataverse`, and the interlock we propose

> **From**: `spaarke-auth-v4-dataverse-MI` · **Date**: 2026-08-19 · **Status**: FOR REVIEW
> **Their status**: INITIALIZED (design only — no spec, no tasks, no worktree)
> **Expected relationship**: **parallel execution, no dependency in either direction**
> **Evidence**: [`PHASE-0-LIVE-VERIFICATION.md`](PHASE-0-LIVE-VERIFICATION.md) ·
> [`CREDENTIAL-INVENTORY.md`](CREDENTIAL-INVENTORY.md) · [`RESEARCH-FINDINGS.md`](RESEARCH-FINDINGS.md)

---

## 0. Read this first

**Please run your own independent verification.** Our reading of your scope comes from
[`projects/dataverse-access-unification-r1/design.md`](../../dataverse-access-unification-r1/design.md) as it
stands on 2026-08-19 — a 58-line design-only document. If your spec has since scoped things differently,
particularly around `DataverseAccessDataSource` or the `Spaarke.Dataverse` DI surface, the file-level interlock in
§4 needs renegotiating and we'd rather hear it early.

Where we describe *your* project we are inferring from a design doc. Where we describe the credential surface,
`Spaarke.Dataverse`'s current auth wiring, or the live Azure state, we have `file:line` or live `az` evidence.

## 1. TL;DR

We are changing **how the BFF identity authenticates** — replacing the client secret with a Managed-Identity-issued
federated credential (MI-FIC) across every BFF-identity confidential client, including the OBO paths in
`Spaarke.Dataverse`.

You are changing **which implementation serves `IDataverseService`** — collapsing two stacks into one and
decomposing the god-classes.

**Different axis, minimal overlap: four files, all of which we can sequence around.** We are explicitly *not*
proposing to merge the projects (§3), and we are explicitly *not* treating your project as a prerequisite (§2).

## 2. We are not blocking on you — correcting our own earlier framing

Our design and research notes both said *"let `dataverse-access-unification-r1` land first where possible."* On
review that phrasing over-stated the dependency and we are retracting it.

**What it actually buys**: if you land first, auth-v4 has two fewer credential sites to migrate
(`DataverseWebApiService`, `DataverseWebApiClient` both get deleted) and skips one gating-defect fix. That is
convenience worth perhaps a day, not a prerequisite.

**Why it is not a prerequisite**: the entire point of auth-v4 is the **OBO** surface, and your design says plainly
*"It does not touch `GraphClientFactory`."* The two files we'd inherit from you are **app-only**, and one of them
(`DataverseWebApiService`) was already migrated to Managed Identity by `#3b`. None of our OBO work — which is all
the risk and most of the effort — depends on anything you ship.

**So: either order works.** Whoever touches the four shared files second inherits the other's shape. §4 makes that
concrete.

## 3. Why we recommend against folding your scope in as our "Phase 0"

This was raised and considered seriously. The argument for merging — *two projects on the same shared library is
semantic overhead, and merging forces close coordination* — is real. We still think it's the wrong call, for three
reasons:

1. **Risk profiles that shouldn't be coupled.** Auth-v4 is a **fail-closed** migration: if OBO breaks, every user
   is locked out immediately and totally across SPE documents, chat tool calls, Office add-ins, the Copilot agent,
   send-as-user email, and row-level authorization on every document and AI endpoint. Its safety story is
   *staged rollout with config-only rollback and a soak before deletion*. Your project is a **~5,600-LOC refactor**
   of impersonation and POA code that carries row-level security semantics (`NFR-06`), whose safety story is
   *characterisation tests proving parity before switching*. Both are sound. Neither is improved by sharing a
   rollback boundary — a single revert would undo a security refactor and a credential migration together.

2. **You are the larger and less-ready project.** You are design-only, dependent on an interim
   `dataverse-access-hardening` effort, and you need your own ADR before implementation can start. Auth-v4's ADR
   work is **done** (ADR-028 Amendment A4 + exception E-3, applied 2026-08-17) and its platform prerequisites are
   **verified and provisioned** (the dev MI-FIC was created 2026-08-19). Folding you in would make a ready project
   wait on an unwritten ADR.

3. **The overlap is genuinely small** — four files, of which two are scheduled for deletion by you anyway (§4).

**If the goal is portfolio-level visibility rather than execution coupling**, the right instrument is a shared
Epic on the board, not a merged project. Auth-v4 already sits under Epic *Auth / Code Quality* (#427).

## 4. The interlock — four files, explicit contract

| File | What **auth-v4** does | What **unification** does (per your design) | Contract |
|---|---|---|---|
| [`DataverseAccessDataSource.cs`](../../../src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs) | **Heavy.** Replace the OBO CCA credential (`:59-63`, exchange `:118-121`); fix the MI-flag gating defect (`:53`); change DI lifetime from **transient** → singleton-cached (`SpaarkeCore.cs:39`) | Not named in your design — it implements `IAccessDataSource`, not `IDataverseService` | **Ours.** Please tell us if your spec pulls it in — this is the highest-blast-radius file in the library and we'd want to serialize hard |
| [`DataverseWebApiClient.cs`](../../../src/server/shared/Spaarke.Dataverse/DataverseWebApiClient.cs) | Fix MI-flag gating defect (`:42`); remove `ClientSecretCredential` (`:44`) | **DELETE** | **Yours wins.** If you land first we skip it entirely. If we land first, our change is ~6 lines you then delete — no merge pain either way |
| [`DataverseWebApiService.cs`](../../../src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs) | Remove the residual `ClientSecretCredential` fallback (`:83`); `#3b` already MI-gated the primary path (`:65`) | **DELETE** | **Yours wins.** Same reasoning |
| [`DataverseServiceClientImpl.cs`](../../../src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs) | Remove the residual `ClientSecretCredential` / `AuthType=ClientSecret` connection-string fallback (`:114-118`); `#3b` already MI-gated the primary path (`:73`, `tokenProviderFunction` `:87-97`) | **DECOMPOSE** into per-concern services below the 2,000-line ratchet | ⚠ **The one that needs real sequencing.** A decomposition moving the credential block to a new file while we edit it in place is a guaranteed conflict. See §4.1 |

Everything else in auth-v4's migration surface — `GraphClientFactory`, `DataverseUserClient`, `AgentTokenService`,
`ReportingEmbedService`, `ReportingProfileManager`, plus the new credential provider under
`Sprk.Bff.Api/Infrastructure/Auth/` — is outside `Spaarke.Dataverse` and outside your scope entirely.

### 4.1 Proposed rule for `DataverseServiceClientImpl.cs`

**Whoever starts second on this file inherits the other's shape.** Concretely:

- If **auth-v4 goes first**, we will confine our edit to the credential-construction block (`:60-125`) and touch
  nothing else, so your decomposition moves a small, self-contained region. We will tell you the exact line range
  when the PR opens.
- If **you go first**, tell us which new file the credential construction lands in and we'll retarget. Our change
  is a deletion of a fallback branch, not new logic — it relocates cleanly.
- **Either way**: whoever is second re-runs `/conflict-check` before opening the PR, and neither of us rebases the
  other's work silently.

## 5. Findings from our investigation that are relevant to your project

These came out of the auth-v4 codebase sweep. They are yours to use or discard; several will affect your parity
testing.

### 5.1 A live gating defect in the library you're consolidating

**`DataverseAccessDataSource.cs:53` and `DataverseWebApiClient.cs:42` never read `Graph:ManagedIdentity:Enabled`.**
Secret *presence* alone selects the secret path. On dev — where `API_CLIENT_SECRET` is set because OBO needs it —
**both run on the client secret today, despite MI being enabled.**

Consequence for you: **if you characterisation-test these paths on dev right now, you are testing the secret code
path, not the MI code path.** Your design's step 1 ("consume #3b MI outcome — target impl is MI-only") assumes MI
is what's live for these two. For `DataverseServiceClientImpl` and `DataverseWebApiService` that is true. For
these two it is not. Auth-v4 fixes this in its prerequisite phase; until then, don't infer MI behaviour from dev
observation on these files.

### 5.2 `DataverseAccessDataSource` is transient, and that is load-bearing for both of us

It is registered as a **transient typed HttpClient** ([`SpaarkeCore.cs:39`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/SpaarkeCore.cs)),
so every resolution builds a **fresh MSAL confidential client** and discards its token cache. `AgentTokenService`
has the same problem (scoped, `AgentModule.cs:24`).

For auth-v4 this is a hard blocker — client assertions require shared/cached clients — so we're fixing it. For you
it's relevant because it means the current per-request token behaviour of the authorization path is *not* what a
consolidated implementation should reproduce. The pattern to copy is the process-wide static CCA cache keyed
`(tenant|client)` at `DataverseUserClient.cs:55-56,91`.

### 5.3 The OBO half of `DataverseAccessDataSource` is the highest-blast-radius code in the library

Worth knowing before any refactor touches it. Its OBO exchange (`:118-121`) backs `AuthorizationService`,
`CachedAccessDataSource`, `AiAuthorizationService`, `AiAuthorizationFilter`, `VisualizationAuthorizationFilter`
and `PermissionsEndpoints.cs:56,116` — i.e. **row-level authorization for every document and AI endpoint running
an authorization filter.** It fails **closed** (`AccessRights.None`), which is the safe direction but means a
regression is an immediate, total lockout rather than a degraded experience.

### 5.4 `prvActOnBehalfOfAnotherUser` — please verify before relying on it

Your design step 1 says to grant `prvActOnBehalfOfAnotherUser` to the MI app-user for impersonated writes. We did
**not** verify that this grant exists today, and it's outside our scope. Flagging it because it is the kind of
prerequisite that reads as done in a design and isn't — and because a missing privilege here fails at runtime on
the impersonation path, which is exactly your NFR-06 risk.

### 5.5 The god-class ratchet already moved under you

Per root `CLAUDE.md`, `DataverseWebApiService` **graduated below 2,000 lines** on 2026-08-16 via RED-4 B dead-code
deletion, and its waiver was removed. Your design (written earlier) describes "two ~2,800-LOC god-classes". Worth
re-baselining before you size the decomposition — you may have one, not two.

### 5.6 A latent footgun in the credential wiring, for awareness

[`GraphClientFactory.cs:54`](../../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs)
resolves `_clientId = AZURE_CLIENT_ID ?? API_APP_ID`, and in Azure `AZURE_CLIENT_ID` is deliberately set to the
**UAMI's** clientId. The dev subscription has **five** UAMIs, one of which (`spaarke-bff-identity`) is named as
though it were the BFF's but is **not attached** to the BFF App Service. Anything resolving an identity by name,
or conflating a UAMI clientId with an app-registration clientId, is a silent-failure generator. Outside your
scope, but the same trap exists anywhere `Spaarke.Dataverse` reads identity config.

## 6. Timeline and what we need

Auth-v4 is heading into `/design-to-spec` now. Its prerequisite phase (DI lifetimes + gating defect) touches
`DataverseAccessDataSource` and `DataverseWebApiClient` early — those are the first edits to land in
`Spaarke.Dataverse`.

Asks, in priority order:

1. **Confirm or correct §4** — especially whether your spec pulls in `DataverseAccessDataSource`. This is the one
   answer that would change our plan.
2. **Confirm §4.1** — the "second one inherits" rule for `DataverseServiceClientImpl.cs`.
3. **Tell us if you object to §2/§3** — i.e. if you think the projects genuinely should merge. We'd rather have
   that argument now than at the first conflicting PR.
4. **Take or leave §5.** §5.1 is the one we'd actively encourage you to verify yourself, because it changes what
   dev observation means for your parity tests.

Both projects run `/conflict-check` per PR and keep `Spaarke.Dataverse` PRs small and serialized, per your own
design's stated mitigation for "highest-contention shared lib".
