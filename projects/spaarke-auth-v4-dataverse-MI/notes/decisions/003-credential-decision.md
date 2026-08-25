# Decision record — 003: the BFF's confidential-client credential

> **Task**: `tasks/003-record-credential-decision.poml` · **Recorded**: 2026-08-20 · Phase 0 decision gate
>
> ## DECISION: **MI-FIC is ADOPTED.** Managed-Identity-issued federated client assertion.
>
> Not a client secret. Not a Key Vault certificate. The Option B pivot is **not** taken.

---

## 1. Why this record exists at all

The last three audits closed this same question — and closed it **wrongly** — because the answer was
never written down with evidence attached. `.claude/constraints/auth.md:108` asserted *"OBO flow
(OAuth spec requires confidential client + secret)"*, and because nothing recorded *how* that
conclusion had been reached, every subsequent audit inherited it as settled fact rather than
re-deriving it.

This record exists so that **a future reader cannot re-open this question without new evidence** —
and so that if they do have new evidence, they can see exactly which claim it contradicts.

**The standard applied here**: every claim below links to a reproducible observation, not to an
argument.

---

## 2. The decision

**The BFF authenticates its confidential clients — including all OBO / delegated exchanges — with a
Managed-Identity-issued federated client assertion (MI-FIC).**

- **Mechanism**: `.WithClientAssertion(Func<AssertionRequestOptions, Task<string>>)` backed by
  `ManagedIdentityClientAssertion` (`Microsoft.Identity.Web.Certificateless`), instance reused so
  the signed assertion caches until expiry.
- **Identities**: the assertion is minted by the **UAMI** `mi-bff-api-dev`
  (clientId `5967251e-…`, principalId `9fd47efb-…`); the confidential client is built for the **app
  registration** `1e40baad-…`. These are deliberately different identities.
- **Trust**: FIC `mi-bff-api-dev-assertion` on the app registration — issuer
  `https://login.microsoftonline.com/a221a95e-…/v2.0`, subject the UAMI **principalId**, audience
  `api://AzureADTokenExchange`.
- **Fallback during migration**: the client secret remains as the ordered fallback under transitional
  exception **E-3**, removed at task 033 after a soak.

---

## 3. Evidence, item by item

Every "Remaining" item from `design.md` §6 Phase 0, and §5 item 1.

| Must prove | Result | Evidence |
|---|---|---|
| Create a dev slot | ✅ | [`001-slot-creation.md`](001-slot-creation.md) — `staging` slot, UAMI assigned, healthy, never swapped |
| Deploy the assertion spike | ✅ | branch `spike/002-obo-mi-fic` (`397a5f306`), slot-only, since removed |
| **OBO → Graph / SPE** | ✅ | [`002-spike-results.md`](002-spike-results.md) T1 — full SPE scope set, `IdentityProvider` source |
| **OBO → Dataverse `user_impersonation`** | ✅ | T2 — `scp=user_impersonation`, **`upn` preserved** |
| **Long-running OBO** | ✅ | T3 — init `IdentityProvider` → retrieval from `Cache` |
| **The built ordered fallback** (per E4′) | ✅ | §4 of the spike record — MI unreachable → falls through to secret → real OBO succeeds |
| §5 item 1 — decide the credential on prototype evidence | ✅ | this record |
| **Power BI** | ⏭️ **Deferred, not proven** | Owner decision 2026-08-20 — [DEF-001 / #804](https://github.com/spaarke-dev/spaarke/issues/804) |
| **Model 2 cross-tenant resource shape** | ❌ **Still open** | [`PROVISIONING-CHANGE-REQUEST.md`](../PROVISIONING-CHANGE-REQUEST.md) §9.2 — owed by `customer-provisioning-orchestration-r1` |

### The negative evidence matters as much as the positive

A decision recorded only from successes is how the original false premise survived. Two controls:

- **T4 (secret control)** ran the identical OBO through a client secret and produced identical
  scopes. So a T1/T2 failure could not have been blamed on the harness — and equally, T1/T2's
  success is not an artefact of a lenient test.
- **T5 (negative control)** minted the assertion for the **wrong identity** (the app registration's
  clientId instead of the UAMI's) and **failed**, loudly, at assertion-minting time:
  *"No User Assigned or Delegated Managed Identity found for specified ClientId."* The mechanism is
  therefore discriminating, not permissive.

---

## 4. The two items NOT proven, and why neither blocks this decision

Stated plainly rather than folded into a pass.

**Power BI (deferred).** Workstream D was deferred by owner decision on 2026-08-20 because Power BI
is not yet in use at Spaarke. `PowerBi:ClientSecret` is a *separate* credential from
`BFF-API-ClientSecret` and no OBO path reads it, so it neither participates in nor constrains this
decision. When Power BI is adopted, task 040 must first answer whether service-principal **profiles**
work under a managed-identity principal — that question is deferred, **not answered**.

**Model 2 cross-tenant shape (open).** Whether a per-customer Model 2 app registration can trust a
FIC issued in the *hosting* tenant is unresolved, and A4's own platform constraints say the UAMI and
app registration must be **same-tenant**. If Model 2 requires a cross-tenant FIC, that shape is
structurally impossible and Model 2 needs the sanctioned alternative (KV certificate) — which is
precisely why the question was raised back to provisioning in §9.2.

**Why this does not gate the decision**: this project's rollout is **dev-only, Model 1**, where the
UAMI and app registration are verified same-tenant (`a221a95e-…`). The Model 2 answer changes what
*provisioning* builds for customer tenants; it does not change what the BFF does here. Recording it
as open is the point — a future reader must not read this decision as having settled Model 2.

---

## 5. What would legitimately re-open this

Not opinion. New evidence of one of these:

1. A reproducible OBO failure under the assertion that is **not** propagation delay (`AADSTS70021`)
   and **not** a misconfigured FIC subject — retry first; a fresh FIC takes minutes to propagate.
2. Microsoft withdrawing or changing MI-FIC's support for the OBO grant.
3. A hosting-model change that breaks the same-tenant rule for Model 1 (the Model 2 question above
   is already tracked separately and does **not** count).
4. The UAMI ceasing to be user-assigned — Entra supports **only** UAMI as a FIC issuer.

**Absent one of these, "OBO needs a secret" is a settled question. Do not re-derive it from a stale
document; correct the document instead.**

---

## 6. Consequences accepted with this decision

- `Microsoft.Identity.Web.Certificateless` becomes a BFF dependency. Measured cost: **~0.01 MB**
  compressed. NFR-01 is not a constraint on this design.
- `IConfidentialClientApplication` instances **must** be singleton/process-scoped and keyed
  `(tenant|client)` — client assertions require shared clients, and per-request construction discards
  MSAL's token cache. That is what task 011 fixes.
- The ordered fallback must be **built**, not inherited from `Microsoft.Identity.Web`'s declarative
  `ClientCredentials` list — see §7.
- Local development keeps the secret path. The MI assertion fails cleanly on a workstation
  (`managed_identity_unreachable_network`), which is what makes the fall-through reliable.

---

## 7. Correction to ADR-028 A4's "Preferred wiring" — the declarative list is unusable here

A4 presents `Microsoft.Identity.Web`'s ordered `ClientCredentials` JSON as the *preferred wiring*.
**That is correct as general Microsoft guidance and wrong for this codebase**, per finding **E4′**:

- Zero occurrences of `EnableTokenAcquisition`, `ITokenAcquisition`, `IDownstreamApi`, or
  `ClientCredentials` in any `.cs` file.
- `AddMicrosoftIdentityWebApi` (`AuthorizationModule.cs:36`) performs **inbound validation only**.
- `Spaarke.Dataverse` has no `Microsoft.Identity.Web` reference at all.

The declarative list only takes effect through Identity.Web's token-acquisition surface, which this
codebase does not use. Every confidential client here is constructed directly through
`ConfidentialClientApplicationBuilder`.

**Consequence, and it is not cosmetic**: the ordered fallback that the entire rollback story depends
on has to be **implemented** (task 021). A reader who follows A4 literally will configure the JSON,
observe no effect, and reasonably conclude MI-FIC does not work. A4 has been annotated accordingly.

---

## 8. Empirical details worth carrying forward

Small things that cost time if unknown:

- The minted assertion's `aud` claim appears as the **GUID** `fb60f99c-7a34-4190-8149-302f77469936`,
  not the literal string `api://AzureADTokenExchange`. Decoding an assertion and expecting the URI
  will look like a misconfiguration. It is not.
- Distinguishable failure modes, all clean and catchable:
  | Condition | Error |
  |---|---|
  | No MI present (workstation) | `managed_identity_unreachable_network` |
  | Wrong identity requested | `managed_identity_request_failed` — "No User Assigned … found" |
  | Wrong/stale **secret** | `AADSTS7000215` — opaque, no indication the value is simply wrong |
  | Fresh FIC not yet propagated | `AADSTS70021` — **retry before concluding anything** |
- The last two rows are why the advice to `customer-provisioning-orchestration-r1` (§9.1) was to
  **omit** the secret rather than write a sentinel value: an absent secret gives a clean
  fall-through, a wrong one gives an opaque `AADSTS7000215`.

---

## 9. Status

| | |
|---|---|
| **Decision** | MI-FIC adopted |
| **Option B (KV certificate)** | **Not taken.** Remains the sanctioned alternative if the same-tenant rule ever fails (e.g. Model 2) |
| **Certificate provisioning work** | Correctly remains **dropped** — `TENANCY-AND-CREDENTIALS.md` recorded it as such and this decision does not re-open it |
| **ADR-028 A4** | Adoption status + the E4′ correction recorded in the ADR |
| **design.md §6 Phase 0** | Marked DONE with this outcome |
| **Escalation triggers** | None fired. Evidence was consistent and non-flaky across repeated runs |
