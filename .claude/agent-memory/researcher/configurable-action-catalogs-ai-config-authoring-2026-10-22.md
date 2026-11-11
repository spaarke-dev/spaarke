---
name: configurable-action-catalogs-ai-config-authoring-2026-10-22
description: 2026-10-22 research for the decision-wizard R2 (action presets as data + AI agent authoring wizard config) - catalog patterns in Dataverse/ServiceNow/Salesforce/Camunda, structured-outputs limits, MS guidance on injection/HITL, OWASP LLM05/LLM06, 5 design principles.
metadata:
  type: project
---

# Configurable action catalogs + AI-authored config (2026-10-22)

**Question**: Patterns for data-defined action presets over a closed code executor set; how to let an AI agent author wizard config safely (humans publish); risks of AI-configured writes/email.

**Findings**
- Universal split: typed contract + implementation = code; instances/composition = data; publish = immutable snapshot. Dataverse Custom API (params/response/privilege rows, IsCustomizable=false, additive-only OPTIONAL params, no new privileges creatable), ServiceNow Action Designer (publish snapshot; spoke actions read-only/copy; decision tables draft->publish, NO rollback of old versions, Test feature), Salesforce @InvocableVariable (label/description/required) + NBA Recommendation records link to one flow (inactive flow => not shown = fail closed), Camunda element templates (JSON props/bindings/constraints/version/$schema/engines).
- Azure OpenAI strict structured outputs (doc 2026-08-24): all fields required, additionalProperties:false, <=100 props/5 nesting levels, NO minLength/pattern/min/max/minItems, anyOf OK (not root), $defs/recursion OK, parallel_tool_calls=false, NOT supported with Foundry Agent Service or BYOD. Doc's own example enum-constrains table/column names (build schema from allow-list). Shape guaranteed, semantics not.
- MS guidance: MSRC (2025-07) "does not rely on blocking all prompt injections" - deterministic impact limits + HITL; Azure AI-agent shared-responsibility page (ms.date 2026-08-26): HITL for writes/deletes/payments/external sends, per-action authz, log inputs/outputs/identity/rationale, memory poisoning. Prompt Shields doc attacks OFF by default; tool-response scanning + Spotlighting = preview. MS precedent for AI proposing config in non-enforcing mode: Conditional Access Optimization Agent creates policies in report-only.
- OWASP LLM06 (excessive functionality/permissions/autonomy) and LLM05 (improper output handling: zero-trust model output, context-aware encoding, email-template phishing). Beurer-Kellner et al. 2025 patterns (action-selector, plan-then-execute, dual LLM...); Willison lethal trifecta. MCP spec 2025-06-18: annotations untrusted; servers MUST validate inputs/access-control/rate-limit.

**Sources**: learn.microsoft.com/azure/foundry/openai/how-to/structured-outputs; /power-apps/developer/data-platform/custom-api; /microsoft-copilot-studio/add-tools-custom-agent; /azure/security/fundamentals/shared-responsibility-ai-agent; /azure/ai-services/content-safety/concepts/jailbreak-detection; MSRC blog 2025/07 indirect prompt injection; genai.owasp.org LLM05/LLM06; arxiv 2506.08837; modelcontextprotocol.io spec 2025-06-18 tools; servicenow.com/docs build-workflows actions + decision-table draft authoring; help.salesforce.com einstein_next_best_action; docs.camunda.io element templates.

**Open questions**: ServiceNow/Salesforce primary pages were hard to fetch (search-summary only); whether Prompt Shields tool-response scanning applies to non-Foundry (BFF-direct) calls (use standalone Content Safety API); no Microsoft source on diff-based config proposals (design inference).
