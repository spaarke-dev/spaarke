# Task 054 — Document Intelligence: 1 already done, 1 made MI-capable, 1 escalated

> 2026-08-22. FR-E5. Outcome: **all three keys resolved — one migrated by 053, one now
> key-or-MI with the key retained under E-2, one blocked by an irreversible infra change.**
> Also closed a gap task 053 left open.

---

## 1. The three keys, mapped to resources first

| Key | Consumer | Target resource | Entra possible? | Disposition |
|---|---|---|---|---|
| `AiSearchKey` (:303) | 3 sites | `spaarke-search-dev` | ✅ (as of 053) | **Already migrated by task 053** — no double work, no duplicate RBAC |
| `OpenAiKey` (:42) | `OpenAiClient.cs:69` | `spaarke-openai-dev` (AIServices) | ⚠️ blocked by **ADR-028 E-2** | **Made key-or-MI; key RETAINED** with documented reason |
| `DocIntelKey` (:152) | `TextExtractorService.cs:778, 1005` | `spaarke-docintel-dev` | ❌ **no custom subdomain** | **Made key-or-MI; key RETAINED**; infra change ESCALATED |

## 2. `AiSearchKey` — criterion 3 satisfied by not repeating work

The POML anticipated this: *"check whether AiSearchKey targets the same Search resource as task 053.
If so, coordinate — do not grant duplicate RBAC or migrate twice."*

It does. Task 053 migrated all three consumers (`AnalysisServicesModule`, `DataverseIndexSyncService`,
`RecordMatchService`) onto `SearchClientFactory`, and the UAMI's `Search Index Data Contributor` on
`spaarke-search-dev` was already in place. **No code and no RBAC touched here.**

## 3. 🔴 The gap task 053 left open — and this task found

`DocumentIntelligenceOptionsValidator:56-59` **required `AiSearchKey` to be non-empty** whenever
`RecordMatchingEnabled=true`.

So after all of 053's work, clearing `AiSearch--AdminKey` would still have **failed startup** — not
at the DI gate 053 fixed, but one layer up at options validation, with a message naming the key
rather than the migration. **A key-presence check in a validator is the same defect as a
key-presence check in a DI gate, just easier to miss.** 053 fixed one and left the other.

Fixed here, and covered by five new tests
(`tests/unit/Sprk.Bff.Api.Tests/OptionsValidation/DocumentIntelligenceOptionsValidatorTests.cs`)
that assert both halves: the key is no longer required, and **the endpoint and index name still
are** — so the relaxation does not quietly become "validate nothing".

The same check existed for `OpenAiKey` (:27) and was relaxed for the same reason.

## 4. `OpenAiKey` — key-or-MI, key retained under E-2

`OpenAiClient.cs` built its client unconditionally from `new AzureKeyCredential(_options.OpenAiKey)`,
so clearing the key would have thrown at construction rather than falling forward.

It now mirrors **the branch already in `AiModule`** for the parallel `AzureOpenAI:ApiKey` path —
which carries its own ADR-028 E-2 comment, so this is adopting the in-repo canonical pattern rather
than inventing one.

**The key is retained, deliberately.** ADR-028 **E-2** records that this AIServices-kind account
returns HTTP 401 `PermissionDenied` to managed-identity tokens while accepting user tokens, and task
052 re-affirmed E-2 with dated evidence on 2026-08-21 — including eliminating the hoped-for
missing-custom-subdomain cause (the subdomain *is* configured on `spaarke-openai-dev`). Criterion 1
explicitly allows this: *"or a retained key is documented with its reason."*

**What changed is the cost of resolving E-2**: it is now a config change (clear the key) instead of
a code change.

## 5. 🔔 `DocIntelKey` — ESCALATED: the dev resource cannot do Entra at all

Measured 2026-08-22:

| Resource | Endpoint | `customSubDomainName` | Entra? |
|---|---|---|---|
| `spaarke-docintel-dev` | `https://westus2.api.cognitive.microsoft.com/` | **null** | ❌ |
| `spaarke-docintel-prod` | `https://spaarke-docintel-prod.cognitiveservices.azure.com/` | `spaarke-docintel-prod` | ✅ |

Azure Cognitive Services requires a **custom subdomain** for Entra token auth; an account reached
through a **regional** endpoint accepts API keys only. **Dev is the outlier — prod is already
configured correctly.** The dev UAMI also holds **no role** on that account.

This fires the task's escalation trigger (*"Any target resource does not support Entra auth — STOP
for that key and document why it is retained"*). Two reasons not to just fix it:

1. **Adding a custom subdomain changes the endpoint URL** that `DocumentIntelligence:DocIntelEndpoint`
   and any other consumer depends on. That is the same trigger task 052 respected.
2. **It is irreversible** — a custom subdomain name is permanent for the life of the account.

**Recommended (owner):**

```bash
az cognitiveservices account update -g spe-infrastructure-westus2 -n spaarke-docintel-dev \
  --custom-domain spaarke-docintel-dev
# then: grant the UAMI "Cognitive Services User" on that account
# then: update DocumentIntelligence__DocIntelEndpoint to https://spaarke-docintel-dev.cognitiveservices.azure.com/
# then: clear DocumentIntelligence__DocIntelKey
```

**The code is already ready for it.** `TextExtractorService.CreateDocIntelClient()` selects Entra
when no key is configured, so once the subdomain and role exist this becomes a config change. The
branch is **not** dead code — prod's account supports it today.

## 6. Constructor shape: nullable with a default, per CLAUDE.md

Both `OpenAiClient` and `TextExtractorService` take `Azure.Core.TokenCredential? managedIdentityCredential = null`
as a **trailing optional**. First attempt made it a required parameter and broke ~15 existing test
fixtures; the project's own prescription is the nullable-with-default shape that *"keeps all 46 test
fixtures compiling"* (CLAUDE.md, on the `IClientAssertionProvider? assertion = null` seam). DI always
supplies it in the app.

A `null` credential on the MI branch throws an **actionable** `InvalidOperationException` naming both
remedies, rather than a `NullReferenceException` from inside the SDK — criterion 4.

## 7. A namespace collision I introduced and had to back out

The validator tests were first placed at `tests/unit/Sprk.Bff.Api.Tests/Configuration/`. Creating a
`Sprk.Bff.Api.Tests.Configuration` namespace **shadowed `Sprk.Bff.Api.Configuration`** for every test
that refers to production options by the short name `Configuration.X`, breaking
`AssociationStatusMapperTests` with *"AutoFileTenantOverride does not exist in the namespace"*.

Moved to `OptionsValidation/` rather than editing the unrelated test to work around my own file.
Recorded because the error message points at the innocent file, not the cause.

## 8. Verification

| Criterion | Status | Evidence |
|---|---|---|
| All three paths authenticate via MI, **or** a retained key is documented with its reason | ✅ | `AiSearchKey` migrated (053) · `OpenAiKey` retained under **E-2** (§4) · `DocIntelKey` retained pending an irreversible infra change (§5). All three code paths are now MI-capable |
| Document analysis output unchanged on a known document set | ✅ **by construction; not re-run** | Both keys are still configured, so both clients take the identical key branch they took before. Behaviour is byte-identical until an operator clears a key |
| Negative: no duplicate RBAC grant or double migration of the shared Search resource | ✅ | §2 — zero code and zero RBAC touched for `AiSearchKey` |
| Negative: RBAC absent → actionable message, not silent degradation | ✅ | §6 — the MI branch throws naming both remedies; §3's validator still fails loudly on a missing endpoint |
| Build + suites | ✅ | build 0 errors · unit **10,603 / 0** (97 skipped, **+5 new**) · auth seams 60/60 · ArchTests 49/49 |
| Live environment changes | ✅ **none** | All Azure calls read-only |

## 9. Carried forward

- **Task 031 / owner** — the `spaarke-docintel-dev` custom-subdomain decision (§5). Irreversible;
  needs a person.
- **Task 033** — `DocumentIntelligence__DocIntelKey` and `DocumentIntelligence__OpenAiKey` stay for
  now; both are documented retentions, not oversights. `DocumentIntelligence__AiSearchKey` goes with
  the 053 cutover.
- **Task 090** — the validator defect class (§3) is worth a forcing function: *a required-field check
  on a credential that has a managed-identity alternative is a latent migration blocker.* Same family
  as the DI-gate anti-pattern ADR-032 already covers, and neither the census (061) nor the credential
  ban (060) would catch it.
