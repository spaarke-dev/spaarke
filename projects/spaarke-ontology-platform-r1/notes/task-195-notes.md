# Task 195 notes: template render finished text; Email documents Summary once

**Status (2026-11-16):** PR #2861 merged to master `04f5c943f9`. Not deployed: the BFF part goes in uac-r2's next dev BFF build, and the client part in the next PCF/code-page deploy after the owner's go.

## What changed
- **Template render** (`EmailTemplateService.cs`, `CommunicationTemplateEndpoints.cs`)
  - Source columns: the route renders from the template's `subjectsafehtml` / `safehtml` columns. It never reads the XSL `subject` / `body` columns, which hold CDATA sections and caused the owner's C1 debris.
  - Merge fields: each one (a `dataslug` span's `data-value`, or a bare `{!…}`) becomes, in order of preference:
    - the regarding record's value, if the token's entity is the regarding type and the value is readable;
    - the token's fallback text;
    - empty.
  - Multi-field tokens: the first non-empty field wins.
  - Output: the body stays HTML with every substituted value HTML-encoded. The subject is plain text.
  - No safehtml: such a template is refused with the route's existing 400 ("Template Render Failed").
- **Merge values** (`ToMergeVariables`)
  - Annotated attributes: every attribute with a FormattedValue annotation uses it verbatim. That covers choice label, money with currency, date in the caller's zone, two-option label and lookup name.
  - Unannotated attributes keep their raw value, and `EmailTemplateService.FormatValue` formats them as the fallback.
  - The old parity mapping existed only for the retired `{{x}}` path.
- **Summary step** (`DocumentEmailWizard.tsx`, new `summaryStream.ts`)
  - Parsing: the `/api/ai/analysis/execute` stream sends the tokens and then the completed text. The step takes the last complete JSON object and shows TL;DR and summary once, through `normalizeResult`, now exported from `summarizeService.ts` rather than copied.
  - Non-JSON output is shown once. The BFF stream is unchanged.
- **Render warning:** the setState-in-render warning is fixed by `SummaryStepActivator`, which starts the run from an effect. The parse result is memoised.

## Evidence
- Live dev (read-only query, 2026-11-16): built-in templates ("Account Reconnect", "Bulk Deletion Task …") store merge fields as `<span class="dataslug" … data-value="{!account:name;Valued Customer}">…</span>`.
  - Lookups appear as `{!account:ownerid/@name;…}`, and empty fallbacks as `{!asyncoperation:createdon;}`.
  - Some templates wrap the slug in a plain outer `<span>`, which the regex skips correctly.
  - subjectsafehtml is either plain text or text with slugs.
- Only caller: `IEmailTemplateService` has one caller (this route), and `RenderFromContent` has no production caller.

## Reviews
- **Main session:** PASS.
- **Independent adversarial review (Opus)** found three fix-now issues, all fixed in `3c697d68d6` and re-checked by the main session:
  - **F2:** choice, money, date and two-option values rendered as raw integer, bare number, UTC date and fixed Yes/No. Fix: use the caller's FormattedValue.
  - **F1:** a multi-field token joined its values. Fix: first non-empty value.
  - **F3:** the "merge parity" test exercised the retired Handlebars path. It is replaced by a real-render test.
- The implementer's own review: memoising the summary parse (F, fixed).

## Known limits
- **K1:** a template with no `safehtml` returns 400. That happens for SDK/Web API-created or legacy templates never re-saved in the editor. The composer keeps the user's text and shows "Could not apply the template." A template with an empty `subjectsafehtml` beside an XSL subject gets a blank subject.
- **K2:** a bare `{!…}` inside an `href` could make a record value the link target. The editor never emits one there, and the composer filters unsafe schemes.
- **K3:** summary parser edge cases that the current playbook does not produce:
  - a stream that ends mid-object, with no done event, shows the fragment;
  - a `{"result":{…}}` wrapper shows "(no summary available)";
  - a `rawResponse` prose fallback repeats its first 200 characters.
- Sender placeholders (another entity than the regarding record) render their fallback or empty.

## Tests and measurements
- BFF, filtered:
  - first round: `Services.Communication` 1893 pass;
  - fix round: CommunicationTemplateEndpointTests, EmailTemplateServiceRenderTests, DataverseEmailTemplateWireShapeTests and CommunicationRecordAuthorizationContractTests, 220/220.
  - New tests fail on the old code: 4 of 30 fail against `7cfbc228a5`.
- ArchTests 2635/2635. Jest 55/55 (8 suites). The BFF build is clean, and `dotnet format` is clean on changed files.
- Publish size: master 42,336,768 → branch 42,338,495 bytes (+1,727), 191 vs 191 files. No new CVE, endpoint, DI registration or package.
- `client-lint` locally: 9 errors in untouched files, caused by missing `node_modules` in the temp worktree; none in changed files. CI's client-lint is authoritative.

## Owner UAT (after deploy)
- **Semantic Search → Email documents:** the Summary shows readable text once.
- **Compose:** apply a template with placeholders, e.g. "Account Reconnect" regarding an account. Check the subject and body for finished text, then send to yourself.
- **Console → Send Email:** apply the same template.

Merged: master `04f5c943f9` (2026-11-16).

rework: fix-round-2 task 195
