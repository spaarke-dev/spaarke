# Redis: shared vs dedicated — correcting my overclaim, and the real numbers

> **Written 2026-09-28** after the owner challenged the dedication argument: *"the reason this was put into
> the 'shared' resources was that it is expensive and did not add value as a dedicated resource."*
> **Status**: ⚠️ **the Redis row of D-12 §3 is REOPENED for the owner.** Everything below is evidence.

---

## 1. 🔴 Correction — I overstated the collision

**What I wrote** (D-12 §3, ADR-009 amendment, the synopsis): *"a shared instance **collides** across Model 1
customers — Customer A's cached value served to Customer B."* Stated as a general property of a shared Redis.

**That is too strong.** The key is:

```
spaarke:tenant:{tenantId}:{resource}:{id}:v{version}
```

Under Model 1 `{tenantId}` is identical for every customer — correct. But a collision also needs
**`{resource}:{id}` to repeat across customers**. For the large majority of call sites `{id}` is a **GUID or
a hash** — a conversation id, a session id, a document id, `SHA256(user-token)`, a `userId:resourceId`
pair — and those are globally unique. **Those keys do not collide on a shared instance, Model 1 or not.**

The collision is real but **narrow and enumerable**: it occurs only where `{id}` is not globally unique.

## 2. The actual exposure — three sites, verified

| # | Site | Key shape | Why it collides |
|---|---|---|---|
| R1 | `AgentServiceClient.cs:52-56` | `…:agent-thread:thread:v1` | **both** parts compile-time constants |
| R2 | `AgentConfigurationService.cs:25-27` | `…:agent-config:exposed-playbooks:v1` and `…:agent-config:capabilities:v1` | id constants (`ExposedPlaybooksId`, `CapabilitiesId`) |
| R3 | `ModuleEntitlementResolver.cs:49-50` | `…:approle-module-map:all:v1` | id constant `"all"` |

R2 and R3 are **legitimately tenant-scoped** caches — an app-role→module map, the tenant's exposed
playbooks. Under Model 2 that reasoning is sound. Under **Model 1** "tenant-scoped" silently means
"shared by every customer", so one customer's configuration is served to another.

R1 is worse and is **not a tenancy problem at all**: it collides **across users inside one tenant**, so
every user resumes the same Foundry thread. Latent — `AgentServiceOptions.Enabled` defaults to `false`.

✅ **Three sites. Not "every key".** That is the honest scope, and it is what ADR-009's new
subject-discrimination MUST is for.

## 3. Cost — from in-repo research, not recall

Source: `projects/customer-provisioning-orchestration-r1/notes/pricing-research-2026-08-12.md` (cited to
Azure's pricing page; **re-verify before it carries budget** — these are 2026-08 figures).

| SKU | Size | Cost | Notes |
|---|---|---|---|
| **Basic C0** | 250 MB | **~$16/mo** | ⚠️ **no SLA, no replication — dev/test only** |
| **Premium P1** | 6 GB | **~$405/mo** | needed for VNet injection / clustering / HA |

**This is the whole argument.** Dedicating at C0 costs ~$16/customer/mo — immaterial. Dedicating at **P1
costs ~$405/customer/mo**, which is the same order as the App Service Plan and would be the **second
largest** per-customer fixed cost in the stack. The owner's original "shared" call was made against the P1
number, and on that basis it was reasonable.

⚠️ **Also on the record**: *"Azure Cache for Redis retirement: **2028-04-30**. Migration target is Azure
Managed Redis (different SKU codes, different pricing shape)."* Any multi-year per-customer Redis cost model
has to absorb that migration.

## 4. The two arguments that survive the correction

Dropping the overstated collision claim, dedication still has two real supports — and one of them is weaker
than it looks.

**(a) 🔴 Blast radius on credentials — the strong one.** Redis holds **OBO access tokens**
(`agent-graph-token`, `agent-dataverse-token`) and the **authorization cache**
(`uac-access:{userId}:{resourceId}`). On a shared instance every customer's BFF holds a connection to the
**whole** instance — Redis auth is per-instance, not per-keyspace. A key-construction bug, a `KEYS`/`SCAN`
in a diagnostic path, or a mis-scoped eviction in one customer's BFF can read or destroy another customer's
tokens. Key prefixing is a *convention enforced in our code*; it is not a boundary Redis enforces.

**(b) ⚠️ Noisy neighbour / capacity — weaker than it appears.** C0 is 250 MB total. That is small once many
customers share it, and eviction is global. But this argues for a *bigger shared* instance, not necessarily
a dedicated one.

## 5. The three options, honestly

| | Cost / customer | Closes the collision? | Closes credential blast radius? |
|---|---|---|---|
| **A. Shared + fix the 3 sites** | **$0** | ✅ yes, completely | ❌ no |
| **B. Dedicated C0** | ~$16/mo | ✅ | ✅ | 
| **C. Dedicated P1** | ~$405/mo | ✅ | ✅ (+ HA, VNet) |

⚠️ **B is not a free lunch**: C0 has **no SLA and no replication**. A per-customer C0 trades a shared,
better-resourced instance for many small ones with no availability guarantee. For a cache that fails open
that may be acceptable — ADR-009's fail-closed startup rule means it is **not** currently treated as
optional, so check that before choosing B.

## 6. Recommendation

**Do (A) unconditionally and immediately — it is free and it is required regardless.** ADR-009's new
subject-discrimination MUST already mandates it, and R1 is a live cross-user defect that dedication would
**not** fix (dedicating per customer still leaves every *user* in that customer sharing one thread).

**Then decide dedication on the credential-blast-radius argument alone, not on collision.** My
recommendation: **B (dedicated C0)** for Model 1 if the SLA question checks out, because it is ~$16 and
removes the shared-credential-store exposure; **not C**, since $405/customer buys HA the cache design does
not currently require.

🔴 **What I got wrong and why it matters**: I justified a ~$405/customer/mo decision with a correctness
claim that does not hold in general. Had that gone unchallenged it would have bought a real recurring cost
to fix three enumerable bugs that a free key change fixes properly.
