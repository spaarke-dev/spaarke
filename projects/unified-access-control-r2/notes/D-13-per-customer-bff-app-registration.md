# D-13 — The BFF app registration is PER CUSTOMER. Settled. Do not re-open.

> **Decided**: by the owner, before D-12, and **re-affirmed 2026-09-28** after I wrongly re-opened it.
> **Status**: 🔴 **CLOSED — BINDING.** This is the decision the whole Model 1 / Model 2 analysis exists to
> support. It is not a cost trade-off and it is not an optimisation. **Do not re-litigate.**
> **Why this file exists**: the question has now been raised three times. It is written here in full —
> mechanism, consequences and the counter-arguments and why they fail — so the next person (or agent) finds
> the answer instead of re-deriving it.

---

## 1. The decision

**Every customer gets their own Entra app registration for the BFF API.** In both models.

- **Model 1** — the app registration lives in **Spaarke's** Entra tenant, one per customer.
- **Model 2** — it lives in the **customer's own** tenant.

**One app registration, one customer. No sharing, no multitenant "one BFF app reg for all Model 1
customers."** `SharedBffAppRegistrationId` is a retired artifact.

---

## 2. 🔴 The reason — the Dataverse ownership chain

This is the mechanism. It is the reason the deployment model was redefined at all.

```
Entra app registration  (Application ID)
        │  registered into a Dataverse environment as …
        ▼
Dataverse APPLICATION USER  (systemuser row, ApplicationId = the app registration)
        │  is assigned to exactly ONE …
        ▼
BUSINESS UNIT  (+ team memberships)
        │  and every record the BFF creates is OWNED by that application user, so it lands in …
        ▼
THAT BUSINESS UNIT
```

**The BFF writes to Dataverse app-only (S2S), as an application user.** It has no interactive user identity
of its own. So the business unit a BFF-created record lands in is **fully determined by which application
user wrote it**, and the application user is **fully determined by the app registration**.

**Therefore: to make a record land in customer X's business unit, the BFF must authenticate as an
application user that belongs to customer X's business unit — which requires an app registration dedicated
to customer X.**

### Why "the environment is per-customer, so one app reg could work" is not sufficient

The strongest counter-argument, and the one I raised: since D-12 gives every customer their **own Dataverse
environment**, a single shared app registration could be registered as an application user **in each
environment**, each assigned to that environment's business unit. Application users are per-environment, so
the BU assignment would still be per-customer.

**That is technically true and still rejected**, for four reasons:

1. 🔴 **One credential, every customer.** A shared app registration has **one** set of credentials. Whoever
   holds them can obtain a token that the application user in **every** customer environment will accept.
   The blast radius of a single credential compromise is the entire customer base. This alone is decisive
   for a product holding privileged legal material.
2. 🔴 **It re-creates the exact failure D-12 exists to eliminate** — one shared thing, relied on to behave
   differently per customer, with nothing structural enforcing the difference. The separation would depend
   on per-environment configuration being correct forever, which is the "filter, not a boundary" pattern
   D-12 §3 rejects everywhere else.
3. ⚠️ **It does not survive Model 2 anyway.** A Model 2 customer's app registration must live in the
   **customer's own tenant** — Spaarke cannot put its app object there and would not want to. So a shared
   design produces **two different onboarding paths**, one per model, for no benefit. Per-customer is the
   only shape that is uniform across both models.
4. ⚠️ **The 20-FIC cap makes sharing fail at 20 customers.** See §3 — and note it argues *for* this
   decision, not against it.

### The secure-record dimension

D-12 §2 puts the **Secure Record** BU as a **sibling** of the customer BU, so that `Deep` depth (which
traverses downward only) cannot reach it. That model only works if the writing identity's BU placement is
controlled per customer. A shared writer identity would have to be placed somewhere in *every* customer's
hierarchy, and any placement that lets it write secure records in one customer's environment is a placement
that must be re-justified in all of them.

---

## 3. ✅ The 20-FIC cap supports this decision — I had it backwards

On 2026-09-28 I raised [the 20-federated-identity-credential cap per application](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-considerations)
(*"no way to increase this quota, even through a support request"*) as an argument that sharing needed
careful design. **The implication runs the other way:**

| Design | FICs per app registration | Scales to |
|---|---|---|
| **Per-customer app registration** ✅ | **1–2** (one per stamp identity) | **unbounded** |
| One shared app registration | **1 per customer** | 🔴 **20 customers, hard stop** |

**A shared app registration hits an unraisable Entra limit at customer 21.** The per-customer design never
approaches it. ADR-028 already records the dev app registration at *"1 of 20 used"*, which is the shape this
decision produces.

*(Flexible FICs would relax the cap but are **preview**, and would only rescue a design that is rejected on
§2's grounds regardless.)*

---

## 4. What this costs — nothing that matters

An Entra app registration is **free**. The cost is **operational**: one more object to create, credential
to federate and lifecycle to manage per customer. That is provisioning automation (H3), which already
exists and already creates per-customer app registrations.

⚠️ **MI-FIC still works** and is unchanged (ADR-028 A4): each customer's stamp UAMI federates to that
customer's own app registration. Intra-tenant in both models, so **no client secrets and no certificates**
are introduced.

---

## 5. Scope — what this decision does NOT say

- It does **not** say every Azure *resource* must be dedicated. That is a separate question with its own
  criteria — `notes/D-12-resource-sharing-analysis.md`.
- It does **not** apply to app registrations that are genuinely fleet-level and touch no customer data
  (e.g. the provisioning control plane's own identity, CI/CD OIDC). Those are Spaarke infrastructure and are
  not Dataverse application users in a customer environment.

---

## 6. Record of re-openings — so the next one stops here

| When | What happened |
|---|---|
| pre-2026-09 | Decided by the owner. Recorded in ADR-028 as an *"Open (provisioning's call)"* question, which is why it kept looking unsettled. |
| 2026-09-28 | I wrote *"closed by D-12 — per-customer app registration"*. Right answer, but asserted rather than argued. |
| 2026-09-28 | I then **withdrew** it as an over-assertion after finding that FIC-sharing is technically possible. **That withdrawal was wrong** — technical possibility is not the question; §2 is. |
| 2026-09-28 | Owner: *"we discussed this already at length and resolved it… THIS IS CRITICAL."* Re-affirmed and written here. |

🔴 **If you are reading this because you are about to re-open it: don't.** The mechanism is §2. The
technical fact that a shared app registration *can* be made to work is already accounted for and rejected.
Nothing about it has changed unless Dataverse changes how application users bind to business units.
