# ArchTest findings in ControlPlane code — raised by `sdap-SPE-admin-app-r2`

> **Filed 2026-08-27** by `sdap-SPE-admin-app-r2` (task 042 follow-up, ISS-002).
> **No code in this project was changed.** This is a findings handoff — the repairs, if any, are yours
> to scope. Nothing here blocks your current work.
> **No secret value appears in this file.** Property and type names are identifiers, not credentials.

---

## Why you are getting this

While retiring scaffolding tests, `sdap-SPE-admin-app-r2` had to get
`tests/Spaarke.ArchTests` to a known state. It went from 102/108 to **106/111**. The 5 that remain red
are all in **this project's** code, and they were **deliberately not forced green** — three of them
cannot be silenced without weakening a security detector, and one of them says so in its own failure
message.

Run them yourself with:

```bash
dotnet test tests/Spaarke.ArchTests/
```

⚠️ **None of these block CI.** Tier-1's blocking subset is 7 named ArchTests and none of these are in
it; they live in Tier 2 / `adr-audit.yml`. So this is a real-findings report, not an outage.

---

## 1. 🔴 The Cosmos secret guard was DEAD, not passing — and now reports 8 findings

**This is the most important item here, and it is the reason this note exists.**

`CosmosProvisioningSecretGuardTests` (FR-27) enforces a **CATASTROPHIC**-severity invariant: no
cleartext secret may be persisted to Cosmos, which is a queryable audit log — a secret written there
leaks to any Reader.

It had not run since the ControlPlane split. Its loader pointed at
`src/server/services/Sprk.Provisioning.ControlPlane/`, a directory that **no longer exists** (L2 is now
`.Api` / `.Core` / `.Sidecar` / `.Worker` / `.Tests`). Both Facts threw `FileNotFoundException` on
every run.

**Why that was worse than an ordinary broken test**: it did not report *"I cannot check this."* It
failed under the DisplayName **"types have no string-typed secret-shape properties"** — so anybody
reading CI would reasonably conclude the secret rule had been evaluated and had an opinion. It never
ran once after the split.

**Repaired by `sdap-SPE-admin-app-r2`** (loader now enumerates every `Sprk.Provisioning.ControlPlane*`
assembly — the scan comment always said `*`, only the loader was singular). It now reports **8
secret-shaped properties**:

| Property | Our read — needs your judgement |
|---|---|
| `SolutionVerificationRequest.ClientSecret` | 🔴 **Looks like a real secret VALUE** — resolved from Key Vault and used to construct a `ClientSecretCredential`. Start here. |
| `ExchangePolicySidecarClient+SharedSecretResolution.Secret` | 🔴 Likely a real value |
| `ExchangePolicySidecarReadClient+SharedSecretResolution.Secret` | 🔴 Likely a real value |
| `PendingKvSecretWrite.SecretName` | Probably a NAME — allowed (root CLAUDE.md §9: names are fine, values are not) |
| `TrapVerificationRequest.KeyVaultName` | Probably a NAME |
| `SlotKeyVaultRefSnapshot.KeyVaultReferenceIdentity` | Probably an identity reference, not a value |
| `PerEnvYamlEntry.Key` / `PerEnvSettingEntry.Key` | Probably a settings key name |

**We did not refine the regex to silence the last five.** Loosening a CATASTROPHIC detector in another
team's code, on our inference about which properties hold names versus values, risks silently removing
protection if we are wrong once — and we would not find out. That judgement is yours: you know which of
these are names.

If the bottom five are false positives, the fix is a narrower shape rule (or a `KeyVaultSecretRef`
type), **not** an allowlist of property names.

---

## 2. FR-F1 / FR-F2 — 4 `ClientSecretCredential` sites not in the census

| Site |
|---|
| `DataverseWebApiSolutionVerifier.cs:55` |
| `DataverseWebApiSolutionImporter.cs:185` |
| `DataverseWebApiEnvVarValuesWriter.cs:84` |
| `DataverseRegistryConcurrencyStore.cs:298` |

FR-F2's own failure message says:

> **"A failure here is NOT a prompt to update the number."**

So we did not update the number. Either these are legitimate and belong in the allowlist **with a
written reason and an ADR citation** (per `tests/CLAUDE.md`'s rule for this path), or they are the
ADR-028 A4 drift the census exists to catch.

---

## 3. `ServiceBusClientGuardTests` — one real second construction site

`ServiceBusModule.cs:144` constructs a `ServiceBusClient` outside `ServiceBusClientFactory`.

The guard's class doc explains why it has **no allowlist**, deliberately: *"an allowlist with one entry
is a census waiting to regrow."* The sanctioned fix is to route through
`ServiceBusClientFactory.Create` / `.CreateForNamespace`, as `MembershipJunctionUpdaterHost` already
does.

**One change we did make** (2026-08-27): the guard now skips `*.Tests` projects. It was reporting three
test doubles in `Sprk.Provisioning.ControlPlane.Tests/**` alongside the real finding, which is how a
guard gets read as noisy and then disabled. This is a **scope narrowing, not an allowlist** — every
file under `src/server/**`, including every ControlPlane service project, is still scanned, and no
named production site is exempt. `ServiceBusModule.cs:144` is the one real hit that remains.

---

## 4. ADR-010 — 1:1 interface ceiling drifted 153 → 155

Two new interfaces with a single implementation. We did not identify which; the test names the count,
not the pair. Either they need a seam justification or they should be registered concretely.

---

## What we are asking for

Nothing urgent. In priority order:

1. **Adjudicate `SolutionVerificationRequest.ClientSecret`** (§1). If it holds a resolved secret value
   that reaches Cosmos, that is the CATASTROPHIC case the guard exists for.
2. Decide the other seven §1 properties — name vs value.
3. Decide the 4 credential sites in §2 (allowlist-with-reason, or fix).
4. `ServiceBusModule.cs:144` (§3) and the ADR-010 pair (§4) at your convenience.

If you conclude any of these are non-issues, the useful output is a **written reason in the test's
allowlist**, so the next person does not re-derive it.

---

## What we changed in shared test code (FYI, already merged)

| File | Change |
|---|---|
| `tests/Spaarke.ArchTests/CosmosProvisioningSecretGuardTests.cs` | Loader repaired to enumerate all `…ControlPlane*` assemblies; added `SafeGetTypes` for `ReflectionTypeLoadException` |
| `tests/Spaarke.ArchTests/ServiceBusClientGuardTests.cs` | `*.Tests` projects out of scope (see §3) |

Both landed in `work/sdap-SPE-admin-app-r2` commit `1b1d03b23`.
