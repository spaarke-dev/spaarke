# Task 055 — `Analysis:PromptFlowKey` disposition: DEAD, deleted

> Implemented 2026-08-21. FR-E6. The task's goal was *"a decided, recorded disposition — migrated,
> deleted, or retained with a reason. No third state."* The decision is **deleted**.

---

## 1. Establishing the consumer first, as the constraint required

The constraint is explicit: *"establish the consumer FIRST by grep. Do not delete configuration whose
consumer is unknown."* Four independent checks, all negative:

| Check | Result |
|---|---|
| Readers of `AnalysisOptions.PromptFlowKey` / `.PromptFlowEndpoint` in `src/` | **none** — both properties are bound and never read |
| Any Prompt Flow HTTP client, endpoint call or SDK client in the BFF | **none** |
| Readers of the `sprk_PromptFlowEndpoint` Dataverse environment variable the doc comment claimed to map to | **none** — the only hit in the repository is the doc comment asserting the mapping |
| `Analysis__PromptFlowKey` on `spaarke-bff-dev` app settings | **absent** — never deployed |

And the detail that settles it: `Seed-ProductionKeyVault.ps1` seeded both secrets as
`-IsPlaceholder $true`, with the values `placeholder-promptflow-key` and
`https://placeholder-promptflow.azurewebsites.net`. They were scaffolded for a Prompt Flow integration
that was never built, and never updated because nothing ever read them.

## 2. The escalation trigger did NOT fire

The trigger is *"the consumer is outside this repository — STOP; deleting a credential another system
depends on is not recoverable from here."*

The one genuine Prompt Flow artifact in the repo is
`infrastructure/ai-foundry/evaluation/metrics/citation_accuracy.py`, which does
`from promptflow.core import tool` and exposes a `@tool` entry point. That is the **Azure AI Foundry
evaluation SDK's authoring model** — a decorator that makes the Python function callable as a Foundry
evaluation metric. It runs inside Foundry, is not invoked by the BFF, and does **not** read
`Analysis:PromptFlowKey`. Checked precisely because a superficial grep for "PromptFlow" makes it look
like a consumer.

So the trigger does not fire, and not because the consumer is inside the repo — because **there is no
consumer at all.**

## 3. What was removed

| File | Change |
|---|---|
| `Configuration/AnalysisOptions.cs` | `PromptFlowEndpoint` + `PromptFlowKey` properties deleted, replaced by a comment recording the evidence |
| `appsettings.template.json` | both Key Vault reference lines deleted |
| `scripts/Configure-ProductionAppSettings.ps1` | `Analysis__PromptFlowEndpoint` / `__PromptFlowKey` settings deleted |
| `scripts/Seed-ProductionKeyVault.ps1` | both placeholder seedings deleted |

**Why the endpoint went too, though the task named only the key.** An endpoint with no key and no client
is not neutral leftovers — it tells every future reader, and every provisioning run, that Prompt Flow is
configured. Removing the credential while leaving its endpoint would have produced a *more* misleading
configuration than before. Stated here rather than done quietly.

**Why `ExecuteFlowName` / `ContinueFlowName` did NOT go.** They are equally unread, but they are not
credentials and they carry safe literal defaults. Removing them would widen a credential task into a
general dead-configuration sweep, which is scope creep in a project whose whole thesis is that unexamined
scope is how the secret survived three audits. Noted in the code and booked to task 090.

## 4. The Key Vault entries are NOT deleted here

`PromptFlow-Key` and `PromptFlow-Endpoint` may still exist in whichever vault a given environment uses.
They are not deleted in this task for two reasons:

1. **Key Vault deletions are task 033's territory**, where the secret-removal runbook and the both-slots
   discipline live.
2. **This session could not enumerate the vault** — `az keyvault list -g rg-spaarke-dev` returns nothing
   and the guessed name fails DNS resolution, which suggests a private endpoint or a different resource
   group. Deleting a secret I cannot first list would be guessing, and the scripts that seeded these are
   now the only remaining pointer to their names.

**Booked onto 033**: purge `PromptFlow-Key` and `PromptFlow-Endpoint` alongside the BFF secret. They are
placeholders, so the blast radius is nil — but leaving them means the next inventory finds two more
unexplained credentials, which is exactly the condition this task closed.

## 5. Verification

| Criterion | Evidence |
|---|---|
| Consumer established before deletion | Four negative checks (§1), including the Dataverse env-var claim and the Foundry SDK false positive |
| Disposition decided and recorded — no third state | **Deleted**, this document |
| Dead configuration removed from appsettings and provisioning | 4 files (§3) |
| Key Vault entry handled | Explicitly deferred to 033 with the reason, not silently skipped (§4) |
| Build + suite | `dotnet build` clean · full suite **10,596 / 0** (97 skipped) · ArchTests **49 / 49** |
| No residual references | `grep` for all four names across `.cs` / `.json` / `.ps1` returns only the explanatory comments |

## 6. The finding worth carrying

This key was **not in the origin seed's inventory**. It was found by the independent sweep, and its
disposition turned out to be "nothing ever used it" — a placeholder credential provisioned into Key
Vault, wired into two deployment scripts and one options class, and read by nothing, for as long as it
has existed.

That is the same failure mode as the project's headline finding, in miniature: configuration that
*asserts* a capability the code does not have. The census (task 061) and the credential ban (060) now
catch the confidential-client version of this. The configuration version — a bound options property with
zero readers — has no forcing function. Worth considering at 090.
