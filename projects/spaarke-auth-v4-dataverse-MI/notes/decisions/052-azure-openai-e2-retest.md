# Task 052 — E-2 re-tested: two causes eliminated, the exception re-affirmed

> 2026-08-21. FR-E3. Outcome: **E-2 RE-AFFIRMED with current dated evidence.** The key is not cleared.

---

## 1. The near-miss worth recording first

The task's premise is that Microsoft documents a **missing custom subdomain** as the usual cause of
Azure OpenAI returning 401 under managed identity, so this "may be a one-config-change elimination".

Checking it produced the opposite: the subdomain **is** configured. I then ran a chat completion against
the endpoint with an Entra token and got **HTTP 200**, on both endpoint hosts — and was one step from
concluding that E-2's premise was disproven and the key could be cleared.

**It was not disproven.** Re-reading E-2's own "Why" first:

> *"Direct curl with the same audience using **my own bearer token** returned HTTP 200 to the same URL."*

The prior investigator had already run exactly that test and already got 200. My result **reproduces**
their finding; it does not contradict it. The distinguishing fact in E-2 has always been the *pair*:

| Principal | Result |
|---|---|
| A **user** token | HTTP 200 |
| The **managed identity** token, with verified-correct `oid` / `appid` / `aud` / `idtyp=app` | HTTP 401 `PermissionDenied` |

Testing only the half already known to pass, and reading it as a refutation, is precisely the failure
mode this project exists to eliminate — a conclusion drawn from a test that shares the premise. It is the
same shape as the false sentence in `constraints/auth.md` that three audits inherited, and the same shape
as task 021's defect, where the seam tests passed because the stub threw the type the implementation
assumed.

## 2. What today's measurements actually settled

| Hypothesis | Measured 2026-08-21 | Verdict |
|---|---|---|
| Missing custom subdomain | `spaarke-openai-dev` → `customSubDomainName: spaarke-openai-dev`, endpoint `https://spaarke-openai-dev.cognitiveservices.azure.com/` | **ELIMINATED** — configured; never the cause. The hoped-for one-config fix does not exist |
| Missing / unpropagated RBAC | UAMI `9fd47efb-…` holds **`Cognitive Services OpenAI User`** *and* **`Cognitive Services User`** at account scope | **ELIMINATED** — re-confirms E-2's original claim, still true |
| Wrong endpoint host | Chat completion with a user Entra token: **200 on `…openai.azure.com` AND 200 on `…cognitiveservices.azure.com`** | Not the cause **for user tokens**. Untested for `idtyp=app` |

That is real progress: the task's headline hypothesis is closed off permanently, so no future audit needs
to re-open it, and the remaining search space is narrower than it was this morning.

## 3. What could not be tested here, precisely

The decisive test is a **managed-identity** token, and it needs IMDS inside the app container:

- A developer workstation has no route to IMDS at all.
- The **Kudu SCM container does not receive the managed-identity environment variables** — verified, not
  assumed: a Kudu `api/command` run returned `has_endpoint=no`, `IMDS_HTTP=000`. An earlier run in that
  container appeared to produce `MI_INFERENCE_HTTP=401`, which would have looked like a confirmation of
  E-2 — but `token_len=0` showed it was a 401 from sending an **empty** bearer. Reporting that as
  "MI still fails" would have been a fabricated confirmation.

So the honest state is: **not re-measured**, and E-2's original App Insights capture
(`LoggingTokenCredential`, correct claims, 401) remains the only direct measurement. Nothing found today
contradicts it.

## 4. Why the key was NOT cleared

Step 4 of the (prescriptive) sequence says *"if it works, clear the key"*. It did not "work" in the sense
that matters — the managed-identity half was never exercised. Clearing `AzureOpenAI__ApiKey` on live dev
on the strength of a user-token 200 would have:

- broken every AI feature on a shared dev environment if E-2's original finding still holds, and
- done so on the basis of the exact reasoning error described in §1.

Step 5 is therefore the applicable branch: *"if it fails, capture the exact error and re-affirm E-2 with
dated current evidence."* Re-affirmed, with the two eliminations recorded.

## 5. The one cheap test that could still resolve E-2

The app is configured with `AzureOpenAI__Endpoint = https://spaarke-openai-dev.openai.azure.com/`, while
this account is `kind=AIServices` whose own endpoint is `https://spaarke-openai-dev.cognitiveservices.azure.com/`.
Both accept **user** Entra tokens (proven today). Whether both accept an `idtyp=app` token is exactly the
variable E-2 never isolated, and it is plausible precisely for an `AIServices` account where
`…openai.azure.com` is an alias rather than the resource's endpoint.

**Recommended next step** — two app settings on the dev slot, instantly reversible:

```
AzureOpenAI__Endpoint = https://spaarke-openai-dev.cognitiveservices.azure.com/
AzureOpenAI__ApiKey   = (cleared)
```

then exercise one chat completion. `AiModule` selects `ApiKeyCredential` when the key is present and the
DI `TokenCredential` otherwise, so clearing the key *is* the switch to MI.

Not done in this task: it is a live app-setting change on a shared environment, and slot settings interact
with tasks 031/032's swap discipline. **Booked onto 031**, where slot settings are being verified anyway
and where an operator is present.

## 6. Escalation trigger — did not fire

*"Adding a custom subdomain would change the endpoint URL consumers depend on — STOP."* No subdomain was
added; it already existed. Nothing was changed in any environment by this task.

## 7. Verification

| Criterion | Evidence |
|---|---|
| Custom subdomain checked FIRST, as the constraint demands | §2 row 1 — the first command run |
| UAMI holds `Cognitive Services OpenAI User` | §2 row 2 |
| Either the key is cleared and MI works, **or** E-2 is re-affirmed with current evidence rather than inherited assumption | **Re-affirmed**, with two causes eliminated and one new negative result (host alias, user tokens) |
| Exact error captured | §3 — including the false 401 and why it was not reportable |
| ADR-028 E-2 updated with a dated note | Yes — re-affirmation block appended; the exception text itself is unchanged |
| Nothing changed in a live environment | Confirmed — read-only checks plus two inference calls |

## 8. Side findings for the rest of Group F (free, from the RBAC sweep)

The subscription-wide role listing for the UAMI turned up prerequisites already in place for two other
tasks in this group:

- **Task 053 (AI Search)** — UAMI already holds **`Search Index Data Contributor`** on
  `spaarke-search-dev`. The RBAC half is done.
- **Task 051 (Service Bus)** — UAMI already holds **`Azure Service Bus Data Sender`** and
  **`Data Receiver`**, but **scoped to the `sprk-membership-changes` topic only**, not the namespace. Any
  other topic or queue the BFF uses will need its own grant — worth knowing before that task assumes
  namespace-level access.
- The Key Vault in use is **`spaarke-spekvcert`** (resource group `SharePointEmbedded`), where the UAMI
  holds `Key Vault Secrets User`. That answers the vault-name question task 055 could not resolve, and
  it is where tasks 033 and 055 must purge secrets.

Also worth noting for the project's own records: **the UAMI does not live in `rg-spaarke-dev`.** It is in
`spe-infrastructure-westus2`. It was resolved by reading the App Service's `userAssignedIdentities` — by
resource ID, per this project's own rule — after a name-and-guessed-resource-group lookup returned
`ResourceNotFound`. A small live demonstration of why that rule exists.
