# Slot strategy — why slots were here at all, and why 032 is rescoped

> 2026-08-23. Owner decision. Affects tasks 031, 032, 033. Supersedes the "swap + soak" shape of
> `032-slot-swap-and-soak.poml` (renamed to `032-promote-and-retire-slot.poml`).

---

## 1. The question that prompted this

> *"Why does Dataverse MI have anything to do with slot swaps — I'm totally confused by what this is
> doing."*

It is a fair question and the answer is: **nothing about Dataverse or managed identity requires a
deployment slot.** Slots are not part of the auth design. They entered this plan as a
*deployment-safety device*, through one chain of reasoning:

1. OBO **fails closed** — a bad credential cutover locks out every user at once, totally.
2. `#3b` attempt 1 took dev down with a **SIGABRT at startup** (eager connect under
   `ValidateOnBuild`).
3. Therefore: do not flip the credential on the running app. Deploy to a second slot, verify OBO
   there, swap.

The load-bearing detail in (2) is that the failure was a **crash at boot**, not a rejected
credential. That distinction is the entire justification for a slot, and it is why the slot is not
redundant with the ordered credential fallback: **you cannot config-rollback an app that will not
start.**

## 2. What was over-built

The plan then added a separate, gated **soak** task: swap, re-verify, then wait out a soak window
before anything could proceed.

That is ceremony for this project, for two reasons:

- **This is one dev app.** `spaarke-bff-dev` is the only deploy target; `spaarke-bff-prod` is Stopped
  and explicitly out of scope (`001-create-dev-deployment-slot.poml:35` forbids touching it). Blast
  radius is the team, not customers.
- **Rollback is already config-only** (NFR-06). The project built `OrderedCredentialClientProvider`
  precisely so the credential order can be reversed without a deploy — and **task 031 already
  requires proving it**, by re-running its checklist with the secret first.

A multi-day gate adds delay, not safety, on top of a rehearsed instant rollback.

## 3. What the slot actually costs

The slot is not free, and this is the part that decided the rescope:

- **Duplicate secret surface.** Task 001 mirrored **all 213 app settings** into the staging slot
  (deliberately — see `001-slot-creation.md` Finding B). **16 plaintext secret values therefore exist
  in two places.** Task 033's job is removing secrets; doing it across two slots is twice the work
  and twice the chance of leaving one behind. Worse, an app setting left on the staging slot would
  **swap back into the default slot** on any future swap.
- **A permanent diagnostic blind spot.** The staging slot reports the **same `cloud_RoleName`** to
  App Insights as the default slot. On 2026-08-23 that turned a credential rotation into a
  ~40-minute outage: the default slot was fixed six times, each fix verified correct, while the
  staging slot looped on the dead key and every diagnostic pointed at the app already being fixed.
  Keeping the slot keeps that trap armed.

## 4. Decision

| | |
|---|---|
| **031** | **Unchanged.** Deploy to the slot and run the §6.1 OBO checklist there. This is the real value: it answers "does the app even start under MI-FIC, and does OBO work" without touching the serving instance |
| **032** | **Rescoped.** Keep the swap — it is the cheapest promotion available (atomic, and instantly reversible *while the slot still exists*). **Drop the gated soak.** **Add: delete the staging slot** once the swap is confirmed good |
| **033** | **Simplified as a consequence.** Only one slot left to purge. Its "remove from BOTH slots" constraints now apply only if 032's deletion did not happen |

**The swap is kept, not removed.** An earlier draft of this recommendation said "deploy, verify,
done", implying a direct deploy to the default slot instead. On inspection the swap is *lower* risk,
not higher: it promotes the exact artifact 031 verified, atomically, and can be reversed in one
command. What was actually objectionable was the soak gate, not the swap.

## 5. The ordering that is load-bearing

Rollback changes hands during 032:

```
before the swap      → rollback = don't swap
after the swap       → rollback = swap back          (staging holds the old build)
after slot deletion  → rollback = reorder credentials so the secret is first
```

So the slot may only be deleted **after** the swap is confirmed good, and only if **031's
secret-first re-verification passed** — because that is the evidence the third rollback mode works.
This is written into 032 as both a constraint and an escalation trigger.

## 6. What this does not change

- The secret still stays until **033**. It is the fallback throughout 031 and 032.
- OBO still fails closed. The staged rollout is still staged; only the waiting is removed.
- Nothing is deployed to a production environment, because there isn't one in scope.
