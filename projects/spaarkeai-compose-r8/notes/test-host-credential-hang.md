# The 100-second test-host hang — one root cause behind ten "failures"

> **Date**: 2026-08-29 · **Branch**: `work/spaarkeai-compose-r8`
> **Status**: FIXED + guarded. BFF suite 11,619 pass / 0 fail. ArchTests 150/150.

---

## 1. What it looked like, and why that was misleading

Going into this, ten failures were on the board in three buckets:

| Bucket | Believed cause |
|---|---|
| 5 ArchTest failures | branch staleness (correct — fixed by the master sync) |
| 3 "Compose timeouts" | the new `SessionOwnershipFilter` adding a session lookup |
| 2 "pre-existing" failures | someone else's problem, unrelated to this branch |

The last two buckets were **one defect**, and neither description was right.

The tell was in the durations, not the assertions: every failure lasted **~100 seconds**. That is
not a slow test — it is `HttpClient`'s default timeout, which means a **hang**. Three different
subsystems do not independently hang for the same 100 seconds.

The decisive experiment was cheap and should have come first: **run a PASSING sibling test alone.**
`ScopePersonasEndpointTests.GetPersonas_AcceptsSortParameters` passes in the full suite and
**fails at 1m41 in isolation**. A test whose outcome depends on what ran *before* it is not a test
with a wrong assertion. That killed every subject-matter explanation at once, including my own
recorded hypothesis about the session filter (which also could not explain `ScopePersonas`, a route
that touches no session).

## 2. Root cause

`Program.cs` registers the credential once, for everything that authenticates outbound:

```csharp
builder.Services.AddSingleton<Azure.Core.TokenCredential>(sp =>
    ManagedIdentityCredentialFactory.Create(sp.GetRequiredService<IConfiguration>()));
```

`ManagedIdentityCredentialFactory.Create` returns a **`DefaultAzureCredential`**. No test fixture
replaced it. So every `WebApplicationFactory<Program>` host held the **real** credential, and the
first request to reach an outbound-authenticating path ran the real probe chain — environment,
workload identity, **IMDS (169.254.169.254)**, Azure CLI. Off Azure the IMDS leg does not fail
fast, and the request blocked until the client timed out at 100s.

`DefaultAzureCredential` caches which source answered. **Only the first caller in a host pays.**
That single fact produces every confusing symptom:

| Symptom | Because |
|---|---|
| The failing set rotated between runs | whichever test reached an outbound path first |
| All failures ~100s regardless of subject | it is one timeout, not N assertions |
| A test passed in the suite, failed alone | alone, it *is* the first caller |
| Config looked fine | `test.crm.dynamics.com` / `login.microsoftonline.com` **resolve** |

That last row is why the earlier **F-070-01** investigation cleared the network hypothesis and was
wrong to: it probed with an `.invalid` host, which fails DNS instantly. The real fixture values are
real, routable hosts. A refutation is only as good as the substitution it makes.

## 3. The fix

Fixed at the **fixture**, per `bff-extensions.md` §F.2 (Fixture-Config-FIRST): a real credential in
a test host is a non-contract fixture value, so the fixture is the defect. **No assertion was
relaxed and no production code changed.**

- `tests/integration/Shared/TestTokenCredential.cs` — a `TokenCredential` that answers instantly
  and never touches the network, plus `services.UseStubTokenCredential()`.
- Applied to **all 52** `WebApplicationFactory<Program>` fixtures.
- It returns a token rather than throwing: these hosts point at fake URLs and the tests assert on
  routing, binding and status codes. Throwing would convert a hang into an exception and still not
  let the request reach the handler under test.

Evidence, before → after, same isolated test: **1m41 → 5s, passing.**

## 4. The guard

`tests/Spaarke.ArchTests/TestHostCredentialGuardTests.cs` fails the build if a
`WebApplicationFactory<Program>` fixture omits the call. One line per fixture is exactly the shape
that decays, and here the symptom actively points away from the cause — a fixture that forgets is
invisible until it costs someone a day.

Proven non-vacuous by perturbation: removing one `UseStubTokenCredential()` call turns the rule
**red** and names the file; restoring it turns it **green**. It also carries a positive control (it
must not fire on the three files that merely *mention* the type in prose) and a floor check, so it
cannot pass by scanning nothing.

## 5. A separate defect this uncovered — mine, and previously invisible

`dotnet build` at the **solution** level failed with 10 pre-existing `CS0103: TestSessionOwner does
not exist` errors in `tests/integration/Spe.Integration.Tests`.

Those came from the earlier `#863` sweep in this project: the shared helpers were wired into
`Sprk.Bff.Api.Tests.csproj` only, while the sweep edited `tests/**`. **That project has not
compiled since — and I did not notice, because I was verifying with
`dotnet test tests/unit/Sprk.Bff.Api.Tests/` rather than a solution build.** A green run of one
project says nothing about the others.

Fixed by linking `tests/integration/Shared/**` into the three projects that needed it
(`Spe.Integration.Tests`, `Sprk.Bff.Api.IntegrationTests`,
`Sprk.Provisioning.ControlPlane.LoadTests`) rather than copying the helpers — a second copy is how
two projects drift into disagreeing about what a test caller's oid is.

**Practice change**: verify with a solution-level `dotnet build` before claiming a suite is green.

## 6. Worth checking separately (not claimed as diagnosed)

The stub removes the hang, but the underlying question stands: **should a BFF request block for
100 seconds when its credential source is unreachable?** In production the IMDS probe normally
succeeds, so this is not evidence of a live outage — but nothing observed here bounds that call. A
credential/HTTP timeout on the outbound auth path is worth sizing on its own merits.
