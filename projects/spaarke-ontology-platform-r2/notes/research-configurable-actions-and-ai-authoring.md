# Research: configurable action catalogs and AI-authored configuration

> **Date**: 2026-10-22. Prepared for [`../design.md`](../design.md) by the `researcher` agent; edited by the main session.
> **Scope**: (1) how comparable platforms split action capability between code and data; (2) how to let an AI agent author configuration safely; (3) risks and guardrails; (4) design principles for R2.
> **Source quality**: Microsoft sources are primary (Learn, MSRC), with dates where given. ServiceNow and Salesforce rest on search summaries, because their help sites blocked direct fetch, so verify them before quoting externally. The repo's `knowledge/` folder had nothing on this topic beyond MCP and Foundry approval basics.

## Summary

Every comparable product splits the work the same way:
- **Code** owns the typed action contract and its implementation.
- **Data** owns the instances and compositions.
- **Publishing** freezes a versioned snapshot.

For an AI author, Microsoft and OWASP agree on one rule: the AI proposes, and deterministic code plus a human decide. Structured outputs guarantee shape, not meaning. Microsoft says it does not rely on blocking every prompt injection, so the design must limit what a successful injection can do.

## 1. Patterns for configurable action catalogs

### Dataverse Custom API — the closest analogue
[Learn, 2026-08](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/custom-api)

- **Data side:** definition rows hold request parameters, response properties, binding type, function vs action, and an execute-privilege name.
- **Code side:** the logic is a plug-in.
- **Locking:** shipped definitions are locked with `IsCustomizable=false`. Later changes may only add optional parameters, never required ones.
- **Privileges:** you cannot create a new privilege for an API; you reuse an existing one.
- **Workflow-enabled APIs:** limited to scalar parameter types plus EntityReference.
- **Lesson:** lock the contract, and allow only additive, optional extension. Spaarke has no plugins, so the BFF executor plays the plug-in role.

### Business process flows
[Learn, 2026-08](https://learn.microsoft.com/en-us/power-automate/business-process-flows-overview)

- Stages and steps are data, within limits: 30 stages per process, 10 active processes per table.
- Role-filtered availability and required-step gates are declarative.
- **Lesson:** a BPF holds no logic of its own. Data defines the guided UX; logic lives elsewhere.

### Copilot Studio tools
[Learn, 2026-10](https://learn.microsoft.com/en-us/microsoft-copilot-studio/add-tools-custom-agent) · [orchestration](https://learn.microsoft.com/en-us/microsoft-copilot-studio/advanced-generative-actions)

- **Inputs:** each input has "Fill using" (dynamic AI or a custom value), display text, retry and validation.
- **Selection:** tool descriptions drive which tool is chosen. The cap is 128 tools; 25–30 are recommended.
- **Confirmation:** "Ask the end user before running" defaults to **No**. That permissive default is exactly what a closed catalog should not copy.

### ServiceNow (search summaries)

- [Action Designer](https://www.servicenow.com/docs/r/build-workflows/workflow-studio/actions.html):
  - Actions have typed inputs and outputs.
  - Script steps are the escape hatch and need a dedicated role.
  - Publish keeps an immutable snapshot; spoke actions are read-only and can be copied.
- [Decision tables](https://docs.servicenow.com/bundle/vancouver-build-workflows/page/administer/decision-table/task/edit-decision-tables-using-draft-authoring.html):
  - Draft authoring keeps the published version running and includes a Test feature.
  - Older versions are **not** kept, so there is no rollback.

### Salesforce (search summaries)

- [`@InvocableVariable`](https://developer.salesforce.com/docs/atlas.en-us.apexcode.meta/api/apex_classes_annotation_InvocableVariable.htm) declares typed inputs in code, and Flow Builder renders them.
- [Next Best Action](https://help.salesforce.com/s/articleView?language=en_US&id=platform.einstein_next_best_action.htm&type=5):
  - Recommendations are data records, each linked to one flow.
  - An inactive or invalid linked flow means the recommendation is not shown. **That is fail-closed.**

### Camunda element templates — the nearest match to preset rows
[Camunda docs](https://docs.camunda.io/docs/components/modeler/desktop-modeler/element-templates/defining-templates)

- Templates are JSON describing properties, bindings and constraints such as `notEmpty` and `pattern`.
- They carry `version`, `$schema` and an `engines` compatibility range.
- Tooling ignores templates whose schema is newer than it supports.

### Typical split

| In code | In data |
|---|---|
| Parameter types, side effects, permission checks, the executor | Label, defaults, which inputs to expose, ordering, availability by role |
| Allowed binding and target kinds | Which tables and fields, from an allow-list |
| Schema version | Preset version and publish state |

## 2. AI agents authoring configuration safely

### Constrained generation
[Learn, 2026-08-24](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/structured-outputs)

- **Where it works:** Azure OpenAI strict `json_schema` works in Chat Completions, and in Responses via `text.format`.
- **Required schema rules:**
  - all fields are required (use null unions for optional ones);
  - `additionalProperties:false`;
  - ≤100 properties and 5 nesting levels.
- **Unsupported keywords:** `pattern`, `minLength`, `minimum`/`maximum`, `minItems`/`maxItems`.
- **Other constraints:** `anyOf` is allowed but not at the root. Strict mode needs `parallel_tool_calls=false`.
- **Not supported with Foundry Agent Service or bring-your-own-data.** Calling the model directly from the BFF avoids that.
- **Technique:** Microsoft's own example puts `enum`s on table and column names. Generate the schema per request from the allow-list, so out-of-list values cannot be emitted.
- **Validator still needed:** pattern and range checks must live in the validator.

### The LLM proposes; the validator disposes

- [OWASP LLM05](https://genai.owasp.org/llmrisk/llm052025-improper-output-handling/): treat model output like user input and validate it before backend use.
- [OWASP LLM06](https://genai.owasp.org/llmrisk/llm062025-excessive-agency/): enforce authorization downstream; do not trust the LLM.
- [MCP spec 2025-06-18](https://modelcontextprotocol.io/specification/2025-06-18/server/tools): servers MUST validate inputs, apply access controls and rate-limit. Tool annotations are untrusted.

### Human review and publish

- Copilot Studio shows a review step for an agent-flow plan before you confirm it ([Learn](https://learn.microsoft.com/en-us/microsoft-copilot-studio/flow-nl)).
- Microsoft says AI-generated flows should be reviewed and tested before production ([FAQ](https://learn.microsoft.com/en-us/microsoft-365/copilot/responsible-ai/flow-builder-responsible-ai-faq)).
- **Direct precedent:** the Conditional Access Optimization Agent creates policies in **report-only** mode, and a human turns them on ([Learn, 2026-06](https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-conditional-access-report-only)).

### Dry run and simulation

- Precedents: report-only evaluation, the policy-impact view, and ServiceNow's decision-table Test feature.
- **Not found:** a Microsoft source for diff-based AI config proposals. The diff, replay and shadow-mode design is our synthesis.

### Provenance and audit

- Microsoft: log each tool call with its inputs, outputs, identity and rationale ([shared responsibility, 2026-08-26](https://learn.microsoft.com/en-us/azure/security/fundamentals/shared-responsibility-ai-agent)).
- Entra Agent ID and Dataverse agent users ([Learn](https://learn.microsoft.com/en-us/power-platform/admin/create-users)) would give the agent its own principal. **Preview as of 2026-09-21; re-verify before depending on it.**

### Prompt injection when the agent reads record data

- **Microsoft's defence layers** ([MSRC](https://www.microsoft.com/en-us/msrc/blog/2025/07/how-microsoft-defends-against-indirect-prompt-injection-attacks)):
  - prevention (spotlighting);
  - detection (Prompt Shields);
  - impact mitigation (deterministic blocking, governance, human-in-the-loop).

  The post says Microsoft "does not rely on our ability to block all prompt injections."
- **Prompt Shields limits** ([Learn](https://learn.microsoft.com/en-us/ai-services/content-safety/concepts/jailbreak-detection)):
  - document-attack detection is off by default in Foundry;
  - tool-response scanning and Spotlighting are preview.

  Treat Prompt Shields as a detector, not a boundary.
- **Structural patterns** ([Beurer-Kellner et al. 2025](https://arxiv.org/abs/2506.08837)): action-selector, plan-then-execute, and dual-LLM. Once untrusted input is in the context, it must not be able to trigger consequential actions.
- **The "lethal trifecta"** ([Willison](https://simonwillison.net/2025/Jun/16/the-lethal-trifecta/)): private data, untrusted content and external communication in one agent. Avoid combining all three.

### Least-privilege tool design

- Foundry's MCP tool supports `allowed_tools` and `require_approval`. Its guidance is to treat tool descriptions and results as untrusted, require approval for tools that write, and log approvals ([Learn, 2026-10-21](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/tools/model-context-protocol)).
- The built-in Dataverse MCP (`/api/mcp`) is delegated-only. No app-only flow is documented.

## 3. Risks and guardrails

| Risk | Guardrail | Basis |
|---|---|---|
| Agent or preset widens capability | Closed executor set. Presets only narrow, and cannot name executors or side-effect classes | OWASP LLM06 |
| Writes to unintended tables or columns | `(executor, table) → columns` allow-list, checked at author time (schema enums), at publish and at commit. Writes run as the deciding user | Per-action authorization |
| Template or parameter injection | Inert `{field}` substitution, no expressions, no re-expansion, destination encoding, fixed URL hosts. Record text never becomes a recipient, field or table | OWASP LLM05 (phishing via email templates) |
| Injection through record data read by the agent | Plan-then-execute; a quarantined step reduces records to stats or enums | Beurer-Kellner et al. |
| Persistent poisoned config | Free text is prominent in the review diff; provenance is stamped | Shared-responsibility page |
| External side effects | The side-effect class belongs to the executor. Above "internal reversible", an elevated publisher is needed. External communication keeps a human gate at publish and at decision | Microsoft: gate writes and external sends |
| Runaway agent | Caps on steps, cost and proposals; rate limits | Shared-responsibility page; MCP spec |

## 4. Recommended principles for R2

1. **Capability lives in code, configuration in data, and data may only narrow** (Custom API, Camunda).
2. **One validator, three call sites, fail closed.** The same code builds the strict schema, gates publish, and re-checks at commit (Next Best Action).
3. **The agent writes drafts only; publishing needs a separate human privilege.** There is no publish tool. Published versions are immutable, and a change is a new version shown as a diff.
4. **Simulate in "report-only" mode before publishing.** Replay against historical Signals with side-effect-free previews.
5. **Treat all record text as hostile, and record provenance.** Stamp rule, preset and validator versions and the author type on what is published and on each Decision Record.

## Caveats

- **Preview or likely to change:**
  - Entra Agent ID and Dataverse agent users;
  - Prompt Shields tool-response scanning and Spotlighting;
  - Dataverse MCP custom tools;
  - the structured-outputs exclusion for Foundry Agent Service.
- **Check against our code:** whether R1's executors already escape email templates. R1 sends plain text, and task 160 builds links only from configured URLs. Column allow-lists exist for rule bodies (`PolicyVersionValidator.AuthorableColumns`) but not yet for action writes.
