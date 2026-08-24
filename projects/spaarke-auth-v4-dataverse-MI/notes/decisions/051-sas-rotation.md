# Task 051 — Service Bus: SAS rotation, and the credential census that was wrong four ways

> 2026-08-23. FR-E2. Outcome: **rotation complete and irreversible; code migrated to namespace + DI
> managed identity; cutover and SAS removal booked to 031/033.** The rotation half was done in the
> prior session and caused a ~40-minute dev outage — recorded here in full, because the way I
> misdiagnosed it is the most transferable thing in this task.

---

## 1. The rotation (completed prior session — do not repeat)

| Fact | Evidence |
|---|---|
| The leaked key was the namespace **PRIMARY** of `RootManageSharedAccessKey` (Manage+Send+Listen, whole namespace) | fingerprint match against `C:/code_files/spaarke/src/server/api/Sprk.Bff.Api/appsettings.Development.json` — gitignored, **never committed** |
| That key is **rotated and dead** — fp `348e57a64503` no longer exists | `az servicebus namespace authorization-rule keys renew --key PrimaryKey` |
| **Both** current keys are valid — proven at the data plane, not inferred | hand-built SAS token → `POST /sdap-jobs/messages/head` → **HTTP 204** for primary and secondary |
| Both slots of `spaarke-bff-dev` (default + `staging`) run the **secondary** connection string | fp `f6d0dfd1ac9f` on both |
| KV secret `ServiceBus-ConnectionString` (`spaarke-spekvcert`) holds the **new primary** | fp `3db62606e51e` |
| Dev job processing healthy — 0 `InvalidSignature` since 20:36Z | App Insights |

**A live SAS value was displayed in terminal output** (`ConnectionStrings__ServiceBus` was a plaintext
app setting, not a Key Vault reference, so `az` echoed it). It was never written to any file, commit,
or record — only fingerprints were — and that key is the one now rotated, so it is dead. Same
handling rule as the client secret from task 022.

## 2. The outage, and the tell I misread

After rotating the key I fixed the **default slot's** app settings six times — Key Vault refresh,
version-pinned references, literal values, the secondary key, four restarts, each one
fingerprint-verified correct — while a **`staging` slot** kept looping on the dead key. It reports
the same `cloud_RoleName=spaarke-bff-dev` to App Insights, so every diagnostic pointed at the app I
was already fixing.

**The tell**: the failure rate never dipped at *any* default-slot restart. A process that restarts and
keeps failing looks identical in aggregate to one that never restarted — unless you look for the
gap. *Check for slots the second time a restart changes nothing, not the sixth.*

This project's own notes said the plan had *"slots supported, 0 exist"*. I trusted that note instead
of checking. It is now corrected in `CLAUDE.md` and §0 of `current-task.md`, with the consequences
spelled out for 031 (the slot exists — do not create it) and 033 (purge **both** slots).

## 3. The census was wrong four ways — and my own handoff was one of them

The POML says to modify `ServiceBusJobProcessor.cs`. **It constructs nothing** — the client is
injected (`:23`). It is a consumer, exactly like task 056's `WebSearchHandler.cs:504`. My own §0
handoff then listed three construction sites and missed the only one that mattered.

What is actually there:

| # | Site | Config key read | Registered at |
|---|---|---|---|
| 1 | `Infrastructure/DI/WorkersModule.cs:18` | `ServiceBus:ConnectionString` | Program.cs:75 |
| 2 | `Workers/Office/OfficeWorkersModule.cs:95` | `ServiceBusOptions.ConnectionString` | Program.cs:124 |
| 3 | `Infrastructure/DI/JobProcessingModule.cs:64` | `ConnectionStrings:ServiceBus` | Program.cs:196 |
| 4 | `Services/Ai/Membership/MembershipJunctionUpdaterHost.cs:120` | namespace + inline `DefaultAzureCredential` | — |

**Three singleton registrations of the same type. .NET DI resolves last-registration-wins, so every
consumer received #3 and #1–#2 were dead code** — including #2's `"ConnectionString is required for
Office workers"` guard, which could never fire. The dead sites read a *different config key* than the
live one, which is how the credential came to live under two spellings at once:

- `ConnectionStrings__ServiceBus` — Bicep (`model1-shared.bicep:187`, `model2-full.bicep:199`), and the key actually live on `spaarke-bff-dev`
- `ServiceBus__ConnectionString` — `scripts/Configure-ProductionAppSettings.ps1:85`

That is the same fan-out ADR-028 A4 blames for making `BFF-API-ClientSecret` unfixable ("seven call
sites each rolling their own credential handling"). **Fourth consecutive task whose POML site count
did not survive a grep** (053: 2 claimed / 7 real; 054: 3 claimed / 1 already done; 056: 2 / 1).

## 4. What was built

**New `Infrastructure/Auth/ServiceBusClientFactory.cs`** — one place for the credential decision,
deliberately mirroring `SearchClientFactory` from task 053 so the platform has one shape.
`FullyQualifiedNamespace` set → namespace + DI-injected `TokenCredential`; otherwise the connection
string; neither → a throw that names both settings and the two required roles.

**Namespace wins when both are configured.** That is what makes the cutover reversible with one
setting (NFR-06) instead of requiring the SAS string to be deleted before it can be stopped from
being used.

**Registration consolidated to one unconditional site** (`JobProcessingModule`). #1 and #2 deleted
with comments pointing at the canonical site. The old shape gated registration on the credential's
presence — the **ADR-032 asymmetric-registration anti-pattern** (CLAUDE.md §10 F.1), third instance
this workstream — where clearing the credential silently un-registers the client and every hosted
service injecting it. Registration is now unconditional and failure surfaces where an operator can
read it.

**`ServiceBusOptions.ConnectionString` `[Required]` removed.** It made the SAS string mandatory at
`ValidateOnStart`, so the app could not boot on the managed-identity path — a validator that
outlived its credential and silently blocked its own removal. Same latent-blocker class as
`DocumentIntelligenceOptionsValidator` (task 054).

**The two config keys reconciled** via `PostConfigure` on `ServiceBusOptions`: when
`ServiceBus:ConnectionString` is unset, back-fill from `ConnectionStrings:ServiceBus`. **Without
this the task would have caused a second outage** — moving client construction onto
`ServiceBusOptions` would have read an empty string on the deployed app, which sets only the legacy
key, with no namespace configured yet and therefore no credential at all.

**`MembershipJunctionUpdaterHost` migrated too.** The POML says its inline `new
DefaultAzureCredential()` is a deviation "not to propagate"; it now takes the DI credential
(nullable-with-default, so fixtures compile) and routes through the factory. Not cosmetic — five
UAMIs exist in the dev subscription and one is named like the BFF's without being attached to it, so
an unpinned `DefaultAzureCredential` can authenticate as the wrong principal, which presents as a
permissions bug rather than a credential bug. Migrating it also means the guard below needs **no
allowlist**: an allowlist with one entry is a census waiting to regrow.

Four stale comments describing the old inline-credential behavior were corrected rather than left to
be re-derived later.

## 5. Criterion 4 needed more than a loud throw

> *"With RBAC absent, the processor fails loudly at startup rather than silently not processing jobs."*

The factory covers *credential absent*. **RBAC absent is a different failure**: the client constructs
fine, `StartProcessingAsync` succeeds, and the rejection arrives later through `ProcessErrorAsync` —
which logged at `Error` and returned, so the processor retried forever. That is not hypothetical; it
is precisely the observed outage, in which **both slots returned `/healthz` 200 for ~40 minutes while
draining zero messages.**

So: auth-class failures now log at **Critical** with text naming the two roles, the namespace-scope
requirement, and the check-every-slot instruction — and `ServiceBusJobProcessor` implements
`IHealthCheck`, reporting **Degraded** after one auth failure and **Unhealthy** after three
consecutive with no message processed in between.

Deliberately **not** killing the host: a single 401 can be MI token-propagation lag, and flapping the
host on that would be worse than the disease (FAILURE-MODES **AP-7** — converting a silent fallback
into fail-fast has unbounded blast radius). Three consecutive failures with no successful receive is
not transient.

**One instance, not two.** `AddHostedService<T>()` registers only `IHostedService→T`, so `AddCheck<T>()`
would build a *second* T via `ActivatorUtilities` that can never observe the processor's state. The
existing `RoutingConsumerTypeHealthCheck` has exactly that shape and gets away with it only because
its check re-derives everything from scratch. Here the singleton is registered first and handed to
both pipelines.

## 6. Two findings worth keeping

**The Service Bus SDK has no authorization failure reason.** `ServiceBusFailureReason` in
`Azure.Messaging.ServiceBus 7.18.1` has thirteen members — verified by reflecting the enum, after
guessing `Unauthorized` and then `UnauthorizedAccess` and being wrong both times — and **none of them
denotes "credential rejected"**. Authorization failures arrive as `UnauthorizedAccessException`, or as
a `GeneralError` whose *message* is the signal. A rotated SAS key produces the latter with the text
`InvalidSignature`. Matching on message text is normally a smell; here it is the only thing that
catches the case this exists for, and a false positive is bounded (it logs and can degrade health, but
changes no message handling and drops nothing).

**My first version of the anti-gating guard was evadable, and its own negative control caught it.**
It matched a config key on the `if` line. The violation I seeded to test it read:

```csharp
var probe = configuration.GetValue<string>("ServiceBus:ConnectionString");
if (!string.IsNullOrWhiteSpace(probe)) { services.AddSingleton(...); }
```

Key and condition on different lines, so nothing on the `if` line named Service Bus — the guard
passed a textbook instance of the thing it exists to prevent. Rewritten to look *backward* from each
registration for an enclosing null/empty guard. **A check that can be evaded by renaming a local is
not a check**, and I would have shipped it as one had I not run the control.

## 7. A hazard this project should know about

Midway through verification, `az` was defaulted to the **"Spaarke Model 1 Production"** subscription,
not dev. The first RBAC query therefore returned "no Service Bus role assignments exist" and listed a
namespace named `sprksharedprod-servicebus` — briefly making it look as though the prior session's
grants had failed, or worse, been made against a shared production namespace. Both were artifacts of
the wrong context.

Given owner decision #1 is **dev only**, every `az` command in this project should carry an explicit
`--subscription`. The dev subscription is `484bc857-3802-427f-9ea5-ca47b43db0f0`.

## 8. Verified live state (dev)

| | |
|---|---|
| Namespace | **`spaarke-servicebus-dev.servicebus.windows.net`** · RG `SharePointEmbedded` · sub `484bc857-…` |
| Job queue | `sdap-jobs` present (with `sdap-communication`, `office-*`, `document-events`, `sprk-provisioning-jobs`) |
| UAMI `mi-bff-api-dev` (principal `9fd47efb-…`) | **`Azure Service Bus Data Sender` + `Data Receiver`, both at NAMESPACE scope** ✅ |
| Second principal `38f7693f-…` | **`sprk-controlplane-dev-uami`** (`rg-spaarke-platform-dev`) — also holds both at namespace scope. **Expected, not an anomaly**: the namespace carries a `sprk-provisioning-jobs` queue, and this is `customer-provisioning-orchestration-r1`'s control-plane identity. **No action.** I flagged it as unidentified before cross-referencing §2.3 of this project's own `PHASE-0-LIVE-VERIFICATION.md`, which already inventories all five dev UAMIs by principalId |

The escalation trigger *"Service Bus RBAC cannot be granted — STOP"* did **not** fire: it is granted.

**The exact setting 031 must apply**, on **both slots of `spaarke-bff-dev`** (the default slot and `staging`):

```
ServiceBus__FullyQualifiedNamespace = spaarke-servicebus-dev.servicebus.windows.net
```

Not applied in this session: the deployed code does not read it yet, and applying it would make the
first 031 deploy flip the credential as a side effect rather than as a verified step. Per the
project's "no in-session flips" non-negotiable, it is a controlled step for 031.

## 9. Verification

| Criterion | Status | Evidence |
|---|---|---|
| The leaked SAS key is rotated and the rotation recorded | ✅ | §1; irreversible; this document is the record |
| Job processing runs on namespace + managed identity | 🔄 **code-complete, cutover booked to 031** | Factory + single registration + RBAC verified present. Requires the app setting in §8 and a deploy; cannot be proven in-session without one |
| The SAS connection string is removed from config and Key Vault | ⏭️ **booked to 031/033 by design** | Removing it before the new code is deployed and verified would take dev down. Same disposition as 053's cutover; the project's own non-negotiable is that the credential stays until 033 |
| Negative case: RBAC absent fails loudly, not silently | ✅ **and the real gap was worse** | It was silent *and* `/healthz` said 200. Now: Critical log naming the roles + Degraded/Unhealthy health check (§5) |
| Publish size reported; no new HIGH CVE | ✅ | see below |
| Build + suites | ✅ | build 0 errors · unit **10,615 / 0** · ArchTests **55 / 55** (+3) · seam **12 / 12** new |
| Live environment changes | ✅ **none this session** | Read-only Azure queries only. The rotation was the prior session |

**Controls for the new guard** (per `tests/CLAUDE.md` "Structural fitness functions"):

- **Negative control — demonstrated.** Seeding a gated duplicate registration in `WorkersModule`
  failed **both** rules (2 of 3 tests), then reverted, 55/55 green.
- **Positive control — demonstrated.** All three pass against the sanctioned single-site shape, and
  the third asserts the factory still offers *both* credential paths, so a future change that
  deletes the SAS branch while config still depends on it fails here rather than at startup.

## 10. Carried forward

- **Task 031** — apply the §8 setting to prod **and the `staging` slot**; verify `/healthz` reports
  the new `servicebus-job-processing` check Healthy and that `sdap-jobs` drains. The slot **already
  exists**; do not create it.
- **Task 033** — remove `ConnectionStrings__ServiceBus` **and** `ServiceBus__ConnectionString` from
  both slots, then the KV secret. Two keys, not one.
- **Owner** — should the `staging` slot be Running? Its creator is not recoverable (activity log
  retains 90 days and has no entry, so it predates 2026-05-26). This matters most for **032**: a slot
  swap promotes whatever is in `staging` into the default slot. (The `38f7693f-…` principal is
  resolved — see §8 — and needed a lookup in this project's own notes, not the owner.)
- **Owner decision, closed 2026-08-23: the `Cognitive Services User` grant on the owner's account
  STAYS.** It is on the owner's *user* account, not on the BFF's identity — the platform authenticates
  to Content Safety / OpenAI as `mi-bff-api-dev` and is unaffected either way. Retained for future
  latency measurements. Do not re-raise this.
- **Task 090** — `WorkersModule` and `OfficeWorkersModule` each carried a shadowed registration that
  had been dead long enough to drift onto a different config key. Nothing looks for duplicate
  singleton registrations of the same type; that is a general BFF hygiene gap, not a Service Bus one.
