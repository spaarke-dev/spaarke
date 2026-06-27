---
name: r6-destination-routing-2026-06-19
description: R6 destination-metadata routing investigation; Q5 RE-SHAPED design is per-node not per-action; NodeRoutingConfig already implemented additively
metadata:
  type: project
---

R6 Q5 RE-SHAPED design (per `projects/spaarke-ai-platform-unification-r6/CLAUDE.md:227-231`) puts `outputSchema` on the **action** (intrinsic) and `destination` + `widgetType` on the **node config** (per-playbook routing). `NodeRoutingConfig.cs` already implements this additively without modifying the 11 production node executors — it parses fields from `sprk_playbooknode.sprk_configjson` independently of `DeliveryNodeConfig` (the existing executor's parse type). `sprk_outputformat` field on action is unrelated — it's text-formatting (max length, include metadata) consumed by `DeliverOutputNodeExecutor`, NOT destination routing.

**Why:** R6 had to converge two divergent destination mechanisms (`PlaybookOutputHandler` for matched-dispatch outputs vs streaming-code emergent behavior for `summarize-document-for-chat@v1`) without modifying NFR-08-bound executors. Per-node config wins because the same action (SUM-CHAT@v1) can route to chat OR workspace in different playbooks — already realized in `summarize-document-for-workspace.playbook.json`.

**How to apply:** When asked about R6 destination routing: schema location is already decided (per-node `sprk_configjson`); the open question is `PlaybookOutputHandler` integration with `NodeRoutingConfig` (it currently uses `OutputType` enum text/dialog/navigation/download/insert; needs to learn about `NodeDestination` workspace/chat/form-prefill/side-effect). Streaming workspace flow is already wired via `sseToPaneEventBridge.ts` → `workspace.field_delta` → `StructuredOutputStreamWidget`. The "both" destination (chat ack + workspace artifact) is unresolved — likely needs CapabilityRouter dedup (task 042) + a separate ack-emit path, NOT a new ADR.

**Sources:**
- `src/server/api/Sprk.Bff.Api/Models/Ai/NodeRoutingConfig.cs:30-274`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookOutputHandler.cs:88-117`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Playbooks/summarize-document-for-workspace.playbook.json`
- `src/solutions/SpaarkeAi/src/components/conversation/sseToPaneEventBridge.ts:174-256`
- `projects/spaarke-ai-platform-unification-r6/CLAUDE.md:226-231` (Pillar 5 binding)
- `projects/spaarke-ai-platform-unification-r6/spec.md:136-141` (FR-27/30)

Related: [[r6-decisions]] in user memory.
