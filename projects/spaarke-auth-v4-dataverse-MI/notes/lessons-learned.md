# Lessons learned — spaarke-auth-v4-dataverse-MI

> **Written**: 2026-08-24, at task 090. **The project is NOT closed** — build / deploy / UAT remain.
> This is the engineering retrospective, not the close-out.

---

## 1. The root cause was a sentence, and the sentence is the lesson

Three prior audits inventoried every secret consumer in this codebase **correctly**, at `file:line`, and
concluded **"NEVER-REMOVE"**. They were not sloppy. They were defeated by one clause in
`.claude/constraints/auth.md:108`:

> *"OBO flow (OAuth spec requires confidential client + secret)."*

OAuth requires a confidential **credential**. A secret is one of three ways to satisfy it, and Microsoft
ranks it last. The sentence foreclosed the question before any inventory could matter — which is why
better inventories never helped.

**What makes this worth generalising** is that the same shape kept appearing, in different files, all the
way through execution — and every instance was *true when written* and *never refreshed*:

| Where | The stale claim | Invalidated by |
|---|---|---|
| `constraints/auth.md:108` | "OAuth spec requires confidential client + secret" | never true |
| `oauth-obo-patterns.md:13` | "Requires confidential client **(has secret)**" | never true — **and it survived the 2026-08-17 A4 correction pass** |
| `appsettings.template.json` | *"DO NOT remove … removing it from Key Vault CRASHES the BFF at startup"* | tasks 022 + 024 |
| `DataverseServiceClientImpl.cs:18,61` | *"do NOT remove `API_CLIENT_SECRET`"* | task 022 |
| `auth-deployment-setup.md` ×3 | "Still required for OBO" | task 022 |
| `SPE.BFF.API-SECRETS-SETUP.md` | set `Graph:ClientSecret` / `Dataverse:ClientSecret` | task 022 — **both keys had zero consumers**, so following the doc produced a local env that silently could not authenticate |
| PR #812 (caught in flight) | *"do NOT remove … per the migration's own guard comments"* | it faithfully copied the stale comment above |

That last row is the important one. **A stale code comment propagated into an architecture doc in another
project, five days before we deleted the thing it described.** The mechanism is not carelessness — it is
that a comment written as a *warning* reads as authoritative long after its premise dies.

### What to do differently

1. **When you correct a falsehood, quote the old text in the replacement.** Every correction in this
   project does. A silently-fixed falsehood teaches nobody why it survived, and cannot be searched for.
2. **Date-stamp review metadata, and treat it as a control.** `auth.md`'s header still read
   `Last Updated: 2026-05-19` on 2026-08-24, *after* its rules had been materially corrected on 08-17. A
   reader checking currency was told it hadn't changed since May. Fixed, with a comment saying why the
   date is load-bearing.
3. **Guard comments ("do NOT remove X") need an owner and an expiry**, because they are the highest-
   authority, lowest-maintenance text in a codebase. Every one that bit us was a guard comment.

---

## 2. A status code never establishes an outcome on this codebase

This cost more time than anything else, and it appeared in **eight distinct shapes**. Recording all of
them, because the pattern is what generalises:

| # | Shape | Where | What it looks like |
|---|---|---|---|
| 1 | **fail-closed** | 031 §5.3/5.4 | a broken lookup and a legitimate denial both return 403 |
| 2 | **fall-through** | 031 §5.6 | the ordered provider silently uses the next credential; a broken one still returns 200 |
| 3 | **error-open** | 031 §5.5 | `GET /api/obo/containers/{id}/children` turns a Graph **404** into `200 {"items":[]}` |
| 4 | **swap-warm-worker** | 032 §3 | a slot swap does not restart the process, so no cache-miss log line appears |
| 5 | **build-time URL** | 032 | the add-in's BFF URL is baked at build time, so a pre-swap test proves nothing |
| 6 | **false text premise** | 033 §0.1 | a false sentence in docs drove protection of the wrong surface |
| 7 | **health races the drain** | 033 §2.6 | `/healthz` returned 200 from the **old container still draining** 27s after a restart |
| 8 | **wrong probe** | 033 §2.6 | `/api/me` returns 200 but performs **no OBO exchange**, so it builds no client and logs no credential |

**Shape 3 is the one that got past me into the written record.** I published "SPE over OBO PROVEN" on the
strength of a 200 that was a swallowed 404, then retracted it. The replacement rests on **55 bytes going
in and the same 55 bytes coming out** — the one thing none of these shapes can fake.

### The rule that came out of it

> Find the log line, the Graph status, or a byte-level artifact. If you cannot, **remove the fallback and
> let success be the proof.**

The second half turned out to be stronger than the first, and it became the method for every cutover:

- **033**: narrow `Graph:Credentials:Order` to `[ManagedIdentityFederated]` **while the secret was still
  present**. With nothing beneath it, a working OBO exchange is MI-FIC *by construction*.
- **051 / 053**: neither factory logs its choice — so delete the SAS strings and the keys. Success is the
  proof.

This is also why the ordering mattered: the decisive evidence in 033 was gathered while rollback was still
a single `az ... appsettings delete` of one key, **before** anything was destroyed.

---

## 3. Do the forcing functions actually hold? — Yes, demonstrated, with one gap

**Graduation criterion 12 was exercised, not asserted** (090). A deliberate ninth secret-bearing
confidential client was seeded on a scratch branch:

```
FR-F1: no secret-bearing confidential credential under src/server/** outside the allowlist   [FAIL]
   ADR-028 A4 violation: ... Offending sites:
   src\...\Infrastructure\Auth\DeliberateViolationProbe.cs:17: MSAL confidential client bound
   to a client secret -- .WithClientSecret(secret)

FR-F2: every confidential-client construction site is in the census, with the expected count  [FAIL]
   The credential census does not match the source.
   "A failure here is NOT a prompt to update the number."
```

Both fired, named the exact `file:line`, and told the reader what to do instead. Scratch branch deleted;
56/56 ArchTests green again on the work branch.

Note the precise claim: **`dotnet build` succeeds; the ArchTests fail.** "The build must fail" means the
CI gate fails, via `Spaarke.ArchTests` in the `code-quality` job — not the compiler. Worth stating exactly,
because someone will otherwise test it with `dotnet build` and conclude the guard is broken.

### ✅ The gap — found, and CLOSED (2026-08-24)

Every one of these forcing functions lives in `tests/Spaarke.ArchTests/**`, which ADR-038 **did not list as
a KEEP path** — while §7's bans B1–B5 simultaneously delegated their lost discovery *to that very category*.
The ADR prescribed the mechanism and declined to protect it, so `/test-diet` (a **mandatory** close gate)
recommended deleting it.

Task 063 fixed the **symptom** in 2026-06: heuristic 0 in the skill, plus a note in `tests/CLAUDE.md`. That
worked — which is exactly why it persisted. **A workaround that removes the pain also removes the pressure
to fix the cause.** The protection sat in a skill file and a module directive for two months, neither of
which is the ADR, and both of which demonstrably drift (the same task found the skill had *also* been
missing `tests/integration/seam/**` since 2026-07-09, silently making every seam test in the repo a delete
candidate).

**Ratified as ADR-038 Amendment A1** at task 090, moving all four surfaces together. This is the general
lesson worth keeping: *when you patch a skill to route around an ADR, you have not fixed the ADR — and the
next reader will trust the ADR.*

---

## 4. The task list was wrong in a specific, predictable direction: it under-counted

Every enumeration in the spec and POMLs was too small, and never too large:

| Claimed | Actual |
|---|---|
| 2 scripts reference the secret | **15** |
| ~25 docs | **33** |
| five config keys | **four** on the live app |
| 5 confidential-client sites (origin assessment) | **8** |
| 2 AI Search key sites (053 POML) | **7** |
| `Graph-API-ClientSecret` is an "alias" | a **different secret**, measured by fingerprint |
| "the only irreversible step" | **recoverable for 90 days** — the vault has soft-delete |
| the lowercase alias is used by the Office add-in deploy | **false**; its consumer was local dev |

**Re-derive counts; never inherit them.** `CredentialCensusTests` exists precisely because of this — and
its own failure message says *"A failure here is NOT a prompt to update the number."*

---

## 5. The obligation that would have broken everything

033's own carried-forward obligation said: *"also delete `ClientSecret` from the default order in
`AddCredentialSelection`."* Executed literally, it **breaks every unconfigured environment** — the
validator fails fast when MI-FIC is the only credential and no UAMI is set, and every test fixture and
every local `dotnet run` has neither.

`CredentialOrderingSeamTests` caught it in seconds. Its own comment had predicted it (*"task 010 already
shipped that exact regression once in this project"*), as had the `AddCredentialSelection` comment
(FAILURE-MODES **AP-7**: converting a silent fallback into fail-fast has unbounded blast radius; **the
default is what bounds it**).

Two lessons:

1. **A carried-forward obligation is a hypothesis from a past context, not an instruction.** It was
   written before the validator rule existed. Re-validate obligations against present code.
2. **Deliver the guarantee where it belongs.** The secret-free property is enforced by *configuration* on
   deployed environments — explicit, auditable, per-environment, and unable to disable local development —
   not by narrowing a default that also governs workstations. The same reasoning kept the `ClientSecret`
   branch and the Service Bus SAS branch: **the credential is unreachable either way; only the recovery
   cost differs**, and removing them converts config-rollback into redeploy on a fail-closed path (NFR-06).

---

## 6. Smaller things worth keeping

- **A file-path overlap from `/conflict-check` is a hypothesis.** The predicted `auth.md` conflict with PR
  #812 did not exist (disjoint hunks); the real conflict was in a file I never anticipated and **had caused
  myself**. `git merge-tree --write-tree` tests it non-destructively in seconds.
- **Fingerprint before you delete.** SHA-256 prefixes proved all four app settings and both KV aliases
  were the same value — which is what made "delete app settings first, Key Vault second" a *reversible*
  sequence rather than a hopeful one. Same technique proved `Graph-API-ClientSecret` was **not** an alias.
- **Measure recoverability instead of inheriting a risk label.** The POML called 033 "the only irreversible
  step". The vault has soft-delete, 90-day retention, no purge protection. Deleting *without* `--purge`
  kept a 90-day undo. Nothing was purged.
- **Never write any part of a secret down.** Found partial values (7 and 12 chars of live 40-char Entra
  secrets) committed under a column captioned *"First 5 chars"*. Redacted; **history untouched**; rotation
  recommended and booked. Use the SHA-256 prefix — the code already does exactly this.
- **A skill directive can encode the bug.** `/test-diet`'s heuristic 1 omitted `tests/integration/seam/**`,
  making **every seam test in the repo** a delete candidate. The classifier was the defect, not the tests.
- **Verify a harness before trusting its silence.** The first OBO harness silently skipped its only
  decisive check because `/api/spe/containers` needs a `configId`. A skipped check reads like a passing one.

---

## 7. What this project did *not* resolve

Booked, not hidden:

1. **Partial secret values in git history** (`c1803e99a`, 2026-03-09). Redacted in the working tree only.
   `Dataverse-Checkout-20251218` is still a valid credential until 2027-12-18. **Rotating it is cheap
   precisely because nothing reads it any more** — recommended.
2. **Local-dev OBO has no credential path for a fresh setup.** The code calls the user-secret fallback
   *"the legitimate — and only — way to run OBO locally"*, and the Key Vault copy was the only **readable**
   copy. Needs a deliberate replacement, not an inherited gap.
3. **`AZURE_CLIENT_ID` is still set** and logs an ERROR every boot — 031 hygiene. Deliberately not bundled
   into the secret removal, because the Azure Identity SDK reads that variable itself.
4. **ADR-038 amendment for `tests/Spaarke.ArchTests/**`** — §3 above.
5. **Key Vault `ServiceBus-ConnectionString` and `AiSearch--AdminKey`** retained as UAT rollback sources;
   deletion booked to project close.
6. **Power BI (040/041/042)** — deferred by owner decision (DEF-001).
7. **`/api/containers`** — two endpoints that 403 every caller, always. Pre-existing; a decision, not a
   credential project's call.
