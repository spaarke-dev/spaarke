# ADR ArchTest violations — triage verdict + handoff charters

> **Date**: 2026-07-10 · **Source**: e2e completion audit F-6 (ADR ArchTest leg) + dedicated sizing assessment
> **Attribution**: all 5 violations are ACCUMULATED REPO DEBT — none introduced by redesign-r2 (violating classes trace to R3 membership, R7 narrators, datagrid-r1, office-integration, email-to-document, registration/demo, and ci-cd-unit-test-remediation-r1). The Code Quality job runs these tests with `continue-on-error: true` (advisory by design), which is how they accumulated.

## Disposition (operator rule: fix in r2 if efficient; hand off if extensive)

| # | Rule | Verdict |
|---|---|---|
| 2 | ADR-010 `new HttpClient` in RegistrationDataverseService | **FIXED IN R2** (IHttpClientFactory migration) |
| 4 | ADR-010 Options-pattern false positives (3 positional records) | **FIXED IN R2** (test refinement + negative control) |
| 5 | ADR-009 EndpointResponseCache IMemoryCache | **FIXED IN R2** (documented path-A exemption; deliberate L1 design per ci-cd-r1 FR-A06) |
| 1 | ADR-007 Graph SDK isolation (5 types) | **HANDOFF** → charter below |
| 3 | ADR-010 1:1 interface ceiling 76→124 | **HANDOFF** → charter below |

After the r2 fixes land, `dotnet test tests/Spaarke.ArchTests/` should show exactly 2 remaining failures — the two handoff items.

---

## HANDOFF-1 — ADR-007 Graph SDK isolation (5 types) — effort M–L (~8–14h incl. tests)

- **Failing test**: `Spaarke.ArchTests.ADR007_GraphIsolationTests.GraphTypesMustBeIsolatedToInfrastructure` ("ADR-007: Graph SDK types must be isolated to Infrastructure layer"). Rule: no type outside a namespace containing `Infrastructure.Graph` or `SpeFileStore` may have a field/property/method-return/param whose type namespace starts with `Microsoft.Graph`. `Assert.Empty` — no allowlist mechanism exists today.
- **Violators**:
  - `src/server/api/Sprk.Bff.Api/Services/Communication/GraphAttachmentAdapter.cs` — `ToAttachmentInfo(FileAttachment)` (:15), `ToAttachmentInfoList(IEnumerable<Attachment>)` (:34)
  - `.../Services/Communication/GraphMessageToEmlConverter.cs` — `ConvertToEml(Message)` (:18) + private Graph-typed helpers
  - `.../Services/Communication/IncomingAssociationResolver.cs` — `ResolveAsync(..., Message graphMessage, ...)` (:84); registered concrete service calling Graph live via `IGraphClientFactory`
  - `.../Infrastructure/Errors/ProblemDetailsHelper.cs` — `FromGraphException(ODataError)` (:12)
  - `.../Api/Office/Errors/OfficeProblemDetailsExtensions.cs` — `FromGraphException(ODataError)` (:314)
- **Root cause**: Graph SDK types leak into the Communication service layer (email mappers/resolver consume Graph `Message`/`FileAttachment` directly) and into the error-translation helpers (`ODataError`).
- **Remediation options / tradeoffs**:
  1. Move the two pure-transform mappers (`GraphAttachmentAdapter`, `GraphMessageToEmlConverter`) into `Infrastructure.Graph.*` — low risk; updates call sites in the EmailProcessor/Communication pipeline; passes the test cleanly.
  2. DTO at the boundary for `IncomingAssociationResolver` — extract a `{subject, sender, inReplyTo, headers, attachments[]}` POCO inside `Infrastructure.Graph` and pass that across. Correct isolation; more churn; needs Communication regression coverage.
  3. Error helpers: delegate to a Graph-namespace extractor (the pattern already exists — `Infrastructure/Graph/GraphErrorTranslator.cs`) that returns a POCO `{status, code, message, requestId}`; `FromGraphException` then takes the POCO, removing `ODataError` from both signatures. Modest effort.
  4. Weakening the rule (allowlisting `Services.Communication`) is NOT recommended for the mappers/resolver — defensible at most for the error-translation case.
- **Acceptance criteria**: test green with no rule weakening for the Communication mappers/resolver; email→document and Office save error paths keep behavior (contract tests under `tests/integration/contract/**`); per root CLAUDE.md §10.6 tests updated with the services.
- **Verify**: `dotnet test tests/Spaarke.ArchTests --filter "FullyQualifiedName~ADR007_GraphIsolationTests"`

## HANDOFF-3 — ADR-010 1:1 interface ceiling 76 → 124 — effort L (enumerate+categorize ~4h; each de-interface ~0.5–1h × N)

- **Failing test**: `Spaarke.ArchTests.ADR010_DITests.ServicesShouldConcreteUnlessSeamRequired` ("ADR-010: Services should be concrete unless seam required"). `const int knownOneToOneCeiling = 76`; fails when the count of app-namespace (`Sprk*`/`Spaarke*`) interfaces with EXACTLY one concrete impl exceeds it. Currently 124 (+48).
- **Root cause**: interface-per-service accumulation across R3 membership, R7 narrators, datagrid-r1, insights, redesign-r1. Note: the 35 `Null*` ADR-032 kill-switch impls create 2-impl seams and are EXCLUDED from the count — all 124 are genuine single-impl 1:1s.
- **Remediation options / tradeoffs**:
  1. Documented ceiling bump to 124 with a category-breakdown comment (the test explicitly invites this). Fast, honest, ratchets debt.
  2. Targeted de-interfacing of gratuitous 1:1s → concrete registration per ADR-010/BFF §10. ~0.5–1h each + call-site/test churn; highest value; multi-day if aggressive.
  3. **Hybrid (recommended)**: bump the ceiling to 124 to green the job now, then cull an agreed batch and RATCHET the ceiling DOWN to the new floor in the same project.
- **Required first step**: run the test to dump the full `Iface -> Impl` list of 124 (it prints on failure), then categorize: genuine seam (PublicContracts facade, real alternate impl pending, ADR-030/032 patterns) vs gratuitous (single impl, mock-only justification → candidate for concrete + WebApplicationFactory-style integration test instead of mock seam).
- **Acceptance criteria**: test green; category table documented in the owning project's design.md ADR-Tensions section; ceiling comment updated with date + rationale; if hybrid, the ratchet-down is enforced by the updated constant.
- **Verify**: `dotnet test tests/Spaarke.ArchTests --filter "FullyQualifiedName~ServicesShouldConcreteUnlessSeamRequired"`

## Suggested owner
Both charters fit the CI/test-hygiene lineage (`ci-cd-unit-test-remediation-r1` successor) — they are ArchTest-governance work items, not feature work. HANDOFF-1 alternatively fits a Communication-module hygiene pass if one is planned.
