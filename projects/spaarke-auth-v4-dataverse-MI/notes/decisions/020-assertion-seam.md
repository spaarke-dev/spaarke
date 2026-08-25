# Decision record — 020: the client-assertion seam, and what it deliberately is NOT

> **Task**: `tasks/020-client-assertion-provider-seam.poml` · **Completed**: 2026-08-21 · FULL rigor · opus/xhigh · FR-B1
>
> ## The required explicit statement (task 020 acceptance criterion, verbatim ask)
>
> *"State explicitly in the PR description whether the provider owns the cache or task 022 adds it —
> silence here is what turns task 011's time-boxed A4 exception into a permanent one."*
>
> ## **`IClientAssertionProvider` does NOT own the confidential-client cache, and it must not.**
>
> **Neither does task 022 "add it to the provider".** A *second, client-level* contract is required, and
> **task 021 is where it must be authored** — not 022. §3 explains why, and both POMLs have been amended.

---

## 1. What was built

| | |
|---|---|
| **Contract** | `IClientAssertionProvider` in `Spaarke.Dataverse` — `Task<string> GetAssertionAsync(CancellationToken)` |
| **Implementation** | `ManagedIdentityAssertionProvider` in `Sprk.Bff.Api/Infrastructure/Auth/`, holding one `ManagedIdentityClientAssertion` for the process |
| **Registration** | `AuthorizationModule`, singleton |
| **Package** | `Microsoft.Identity.Web.Certificateless` 4.14.2 — BFF only |
| **Shared-lib wiring** | `DataverseAccessDataSource` takes `IClientAssertionProvider? assertion = null` — accepted, **not yet used** |

The layer constraint dictated the shape and there was no alternative: `Spaarke.Dataverse` is the base
layer, cannot reference `Sprk.Bff.Api` (direction) and cannot reference `Spaarke.Core` (cycle —
`Spaarke.Core.csproj:32` already references it), with `LayerDependencyTests` (FR-14) failing the build
on either. Dependency inversion is the only legal seam.

## 2. Why the contract exposes no MSAL types — and the honest limit of that

`Spaarke.Dataverse` *does* reference `Microsoft.Identity.Client` 4.87.0 and could have leaked
`AssertionRequestOptions`. Keeping it out keeps MSAL out of the base layer. Consumers adapt at the call
site: `.WithClientAssertion(opts => provider.GetAssertionAsync(opts.CancellationToken))`.

**⚠️ Corrected after code review (Q4).** This section first claimed the narrowness meant a **Key Vault
certificate** implementation would need *no contract change*. **That is not safe.** A certificate
assertion is a self-signed JWT whose `aud` must be the token endpoint and whose `iss`/`sub` must be the
client id — precisely the `TokenEndpoint` and `ClientID` this contract drops, and precisely why
Identity.Web's own `ClientAssertionProviderBase` takes `AssertionRequestOptions`. Worse, that base class
caches **options-blind** (measured at the gate): correct for MI-FIC, whose assertion is
app-registration-agnostic, but a *correctness* bug for a certificate provider whose audience varies.

Not a problem today — MI-FIC is the adopted credential and A4 records that the certificate path was
explicitly not taken. The widening, if ever needed, is a **Spaarke-owned request record**
(`ClientId`, `TokenEndpoint`, `TenantId` — still no MSAL types). **Booked onto task 021** as a decision
to make before six call sites bind at 022.

## 3. The finding: ordered selection cannot live behind this contract, and neither can the cache

Task 020's constraint said to design the provider as *"the home for a single shared confidential-client
cache"*. **Working that through shows the assertion contract cannot be that home — and that the problem
surfaces one task earlier than expected, at 021 rather than 022.**

Task 021's ordered credential selection is **MI-FIC → KV certificate → dev secret**. Those are three
*different MSAL builder calls*, not three sources of the same value:

| Credential | MSAL call | Produces an assertion? |
|---|---|---|
| MI-FIC | `.WithClientAssertion(...)` | ✅ yes |
| Key Vault certificate | `.WithCertificate(x509)` | ❌ **no** |
| Client secret (E-3, transitional) | `.WithClientSecret(...)` | ❌ **no** |

A contract shaped `Task<string> GetAssertionAsync(...)` therefore **cannot express the fallback**: two of
the three branches have no assertion to return. Widening `IClientAssertionProvider` to cover them would
produce a type whose name is a lie — an "assertion provider" that selects certificates and secrets.

The same reasoning settles the cache. A confidential client is built from *whichever* credential the
ordered selection chose, so the cache belongs to the component that performs the selection, keyed by
`(tenant | client | credential-kind)`. That component is the client-level seam, not this one.

### What this means for the three per-class CCA caches

Task 011 left three static caches (`DataverseUserClient`, `DataverseAccessDataSource`,
`AgentTokenService`) as an explicitly **time-boxed ADR-028 A4 path-A exception expiring at task 022**.
That obligation is unchanged and still owned. What changes is the *mechanism*:

- **Three of the four sites are inside the BFF** (`GraphClientFactory`, `DataverseUserClient`,
  `AgentTokenService`) and can consume a BFF **concrete** type — which ADR-010 actively prefers over an
  interface.
- **Only `DataverseAccessDataSource` is in `Spaarke.Dataverse`**, and it can see nothing but contracts
  declared there. It is the sole reason a *second* contract is needed at all.

So the consolidation is: one cache, owned by the selection component in the BFF; the three BFF sites
inject it concretely; the one shared-lib site reaches it through a client-level contract declared in
`Spaarke.Dataverse`.

## 4. Why this was not simply built now (the path chosen, per CLAUDE.md §6.5)

**Path A — project-scoped, with the design named and both downstream POMLs amended in this task.**
Not path C (widen the contract now), and explicitly not silence.

Reasons:

1. **It would be the wrong shape.** Per §3, the member task 022 needs is a *credential-selection* concern.
   Authoring it inside `IClientAssertionProvider` misnames it; authoring the correct second contract
   requires 021's selection design, which does not exist yet.
2. **021 lands before 022.** Building 022's seam now means building it before the task that determines
   its shape, then reworking it — the specific churn task 011 declined for the same reason.
3. **The reason path C is normally right does not apply.** "Change it now while it has zero consumers"
   assumes the contract needs changing. It does not — `IClientAssertionProvider` is correct and complete
   for what it does. The *additional* contract is a new artifact whose cost is the same whenever written.

**What makes this Path A rather than deferral-by-silence** — all four done in this task, not promised:

| Action | Where |
|---|---|
| The explicit statement the criterion demanded | this record, header + §3 |
| Task **021** now carries the obligation to author the client-level seam, with the assertion-vs-cert-vs-secret reasoning | `021-*.poml` constraint + criterion |
| Task **022**'s criterion made mechanism-agnostic — it required `grep` to return "only the provider", which §3 shows is unsatisfiable as written | `022-*.poml` |
| The two stale forward-references saying "task 022 relocates it onto `IClientAssertionProvider`" corrected | `DataverseAccessDataSource.cs`, `AgentTokenService.cs` |

## 5. The ADR-010 ceiling: a prescriptive step refused on evidence

Step 6 instructed raising `ADR010_DITests` 153 → 154, stating *"without it the build fails."*
**The premise is false.** Verified rather than assumed:

| Check | Result |
|---|---|
| ArchTests at the **unraised** ceiling of 153 | ✅ pass |
| Real 1:1 interface count | **151** — two slack already |
| `IClientAssertionProvider` in the counted list | **absent, 0 occurrences** |

`ServicesShouldBeConcreteUnlessSeamRequired` scans `typeof(Program).Assembly` — the BFF only — and the
interface is declared in `Spaarke.Dataverse`. **A cross-assembly 1:1 seam is structurally invisible to
this ratchet.** Raising the ceiling would have widened the slack from 2 to 3, letting a future
*in-assembly* interface land without the justify-or-concrete review the gate exists to force.

Confirmed independently at the quality gate, which re-ran the counting logic in a throwaway probe and
reproduced 151 / absent. Deviation from a prescriptive step, escalated per CLAUDE.md §6 rather than
applied silently.

**Two consequences, one booked and one open:**

- **Booked** — task **061**'s census must scan all server assemblies, with a negative control that adds a
  scratch confidential-client site in `Spaarke.Dataverse` (an assembly-scoped detector passes that only
  by accident).
- **Open, owner decision** — the ADR-010 ratchet itself stays blind to cross-assembly seams forever, and
  its ceiling is 153 against a real count of 151. Both are one-line detector fixes with repo-wide blast
  radius, and neither is this project's to take unilaterally.

## 6. Verification

| Check | Result |
|---|---|
| Full BFF suite | ✅ **10,553 / 0** (97 skipped) — NFR-04: all 46 fixtures compile and pass unchanged |
| Seam tests | ✅ **17 / 17** (4 new) |
| ArchTests | ✅ **36 / 36**, ceiling untouched at 153 |
| `LayerDependencyTests` FR-14 | ✅ `Spaarke.Dataverse.csproj` **byte-identical** — no ProjectReference, no PackageReference |
| Publish size | **43.68 MB** compressed incl. PDBs, via `Compress-Archive` over 215 files / 137.25 MB raw. **Δ 0.00 MB vs this worktree's pre-change publish** (task 011 measured the same 43.68). **Ceiling 60.** See the method note below |
| Certificateless cost | **provably zero** — `project.assets.json` shows `Microsoft.Identity.Web 4.14.2 → Certificateless 4.14.2` already resolving transitively for both `net10.0` and `net10.0/linux-x64`. The explicit reference pins what we now use directly rather than inheriting it |
| CVE | ✅ clean |

### Method note on publish size (code review S-8)

Two measurements of the same tree disagreed: **43.68 MB** (mine, `Compress-Archive`) versus **44.97 MB**
(the code-review gate's). Investigated rather than reconciled by preference — the publish content is
**identical** (215 files, 137.25 MB raw, verified both by `find` and PowerShell), so the ~1.3 MB gap is
**compression method/level**, not content.

Two things follow, and only the second is actionable:

1. **It does not affect any decision here.** Both measurements agree this change's own delta is ≈0, and
   both are far under the 60 MB ceiling — which is unsurprising, since `project.assets.json` shows
   `Microsoft.Identity.Web 4.14.2 → Certificateless 4.14.2` already resolving transitively, so the
   explicit reference adds no bytes at all.
2. **CLAUDE.md §10's baseline is not method-qualified.** "44.96 MB incl. PDBs" says nothing about how it
   was compressed, so two honest reporters can differ by more than the +5 MB single-task escalation
   threshold while both believing they are comparing like with like. Worth fixing in §10 — the figure
   needs a stated method, not just a number and a PDB convention. Not this project's call to make
   unilaterally; raised here so it is on the record.

## 7. What the Step 9.5 quality gates changed

Both gates ran as parallel sub-agents on opus. **`adr-check`: 2 violations, 12 warnings.
`code-review`: 0 critical, 10 warnings, 9 suggestions.** Both independently reproduced the ADR-010
ceiling finding and endorsed the prescriptive-step refusal.

**The gates measured two of my claims against the real package and both were false.** That is the
headline: they were not style objections, they were empirical refutations.

| Finding | What was wrong | Fix |
|---|---|---|
| **CR W-4** | I claimed a per-instance `ManagedIdentityClientAssertion` would cost **an IMDS round trip per call**. Measured: five fresh instances → **zero** additional IMDS requests. MSAL's managed-identity token cache is **process-static and keyed by identity**, not per-assertion-object. The claim appeared in **five** places, including `DataverseAccessDataSource`'s CCA-cache doc where it was cited *forward into task 022* | Corrected in all five. The singleton is still right (A4 prescribes instance reuse; it avoids rebuilding the MI application) — the stated *cause* was wrong. Task 022 must not lean on the retracted premise |
| **CR W-5** | I skipped the deferred-failure test because *"proving it means waiting out a timeout — that is slow."* Measured: **~80 ms**, not a timeout. And that test covers the ONE property task 021's ordered selection rests on | Test added, asserting `MsalServiceException` + a **set** of error codes (a workstation cannot route to IMDS; GitHub runners are Azure VMs where it *is* routable — pinning one code passes locally and fails in CI) |
| **ADR V2** | `AuthorizationModule` claimed *"the ceiling is raised 153 → 154 in this same PR"*. It was not — written before the discovery, never revised. Two files in one commit asserting opposite facts, in the paragraph justifying the ADR-010 seam | Rewritten to the verified finding |
| **ADR V1 / CR W-7** | The shared-CCA-cache disposition was unstated, and the criterion demanding the statement named silence as its own failure mode | §3–§4 above; POMLs 021/022 amended; stale forward-refs corrected |
| **CR W-2** | A blank `Graph:ManagedIdentity:ClientId` built a **user-assigned** assertion for an empty identity while logging *"falling back to system-assigned"* — the wrong diagnostic in exactly the FR-B4 scenario the log exists for. Also diverged from `Create`'s guard, defeating the extraction's purpose | `ResolveUamiClientId` now normalises blank → `null`, so both consumers agree |
| **CR W-3** | I claimed the provider reads the UAMI *"through the same code path as every app-only consumer."* **False** — four shared-lib sites read the two keys in the OPPOSITE precedence and don't call the resolver. If both keys were ever set to different UAMIs they would resolve to different identities (FR-B4) | Claim replaced with the actual state + the four sites named; convergence booked onto 022 |
| **CR Q4 / S-9** | The contract doc claimed a certificate implementation needs no change. Refuted — see §2 | §2 corrected; decision booked onto 021 |
| **CR W-9** | Fall-through is not uniform: `managed_identity_request_failed` is the FR-B4 wrong-identity signature and must **fail loud**, not fall through to the secret — otherwise production runs on the secret looking healthy. MSAL has four relevant codes; the doc named two. Failures aren't negatively cached, so a broken MI costs ~80 ms *per request* | Booked onto 021: one `IsFallThroughEligible` predicate + short-TTL negative memo, with a criterion that the wrong-identity code fails loud |
| **CR W-10** | `tokenExchangeUrl` hard-coded; A4 flags that **sovereign clouds differ** | Made configurable (3 lines now vs a six-call-site change after 022) |
| **CR W-6 / W-8** | Three tests were `NotThrow`-only (ban B10); three auth files now sit in `seam/**`, which ADR-038 scopes to the AI convergence spine | W-5's test converts the first into a two-part behavioural claim. Category drift booked onto **090** for `/test-diet` — noting `tests/integration/auth/**` is empty **and not compiled**, so a test authored there would run never |
| **CR S-8** | Two measurements of one tree: 43.68 MB vs 44.97 MB | Investigated: content identical (215 files), so the gap is compression method. Recorded, plus the observation that CLAUDE.md §10's baseline is not method-qualified — two honest reporters can differ by more than the +5 MB escalation threshold |

Applied at discretion: S-1/S-4 (idiom consistency, usings), S-5 (obviously-fake secret literal).
Declined: S-7 (a separate `Spaarke.Auth.Abstractions` project — real option, but a new project + CI
surface for one interface; dependency inversion into the existing base layer is the cheaper correct
answer, and §11 says default to reuse). Noted for the record rather than silently omitted.
