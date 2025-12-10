# Tagging System Examples

> **Purpose**: Show how the lightweight tagging system helps discover related resources.

---

## Overview

Skills and knowledge docs include YAML frontmatter with tags. During project initialization, task creation, and design-to-project workflows, agents search for matching tags to discover relevant resources.

**Key Benefits:**
- ✅ Low maintenance - just add tags when creating/updating files
- ✅ Human-readable - developers can scan by tags too
- ✅ Self-documenting - tags are visible in file headers
- ✅ No complex scripts - agents search naturally

---

## Example 1: Creating a PCF Control Project

**Scenario:** User provides spec for new PCF control with Dataverse integration.

### Discovery Flow

1. **Agent runs `/design-to-project`**
2. **Phase 2: Discover Related Resources** (new step in design-to-project skill)
3. **Extract keywords from spec.md:**
   - "PCF control"
   - "Dataverse"
   - "React"
   - "TypeScript"

4. **Search `.claude/skills/INDEX.md`:**
   - `dataverse-deploy` - tags: `[deploy, dataverse, power-platform, pac-cli, pcf]` ✅ MATCH
   - `adr-aware` - tags: `[adr, architecture]` ✅ MATCH (always-apply)
   - `spaarke-conventions` - tags: `[conventions, standards]` ✅ MATCH (always-apply)

5. **Search `docs/ai-knowledge/`:**
   - `architecture/sdap-pcf-patterns.md` - tags: `[pcf, dataverse, react, frontend]` ✅ MATCH
   - `standards/oauth-flows.md` - tags: `[auth, oauth, security]` ✅ MATCH

6. **Result:** Agent knows to:
   - Load `dataverse-deploy` skill (will need it later)
   - Reference `sdap-pcf-patterns.md` for existing patterns
   - Follow ADRs via `adr-aware` skill
   - Apply coding standards via `spaarke-conventions`

---

## Example 2: Creating Tasks with Resource Discovery

**Scenario:** Agent runs `/task-create` to decompose plan.md.

### Task: "Implement file upload endpoint"

**Step 3.4: Discover Related Resources** (new step in task-create skill)

1. **Identify task characteristics:**
   - Building: API endpoint
   - Technologies: ASP.NET Core, Microsoft Graph
   - Operations: File upload, authentication

2. **Search skills:**
   - `adr-aware` - techStack: `[all]` ✅ MATCH (always-apply)
   - No specific "API endpoint" skill found (could create one later)

3. **Search ai-knowledge:**
   - `architecture/sdap-bff-api-patterns.md` - tags: `[api, aspnet-core, backend]` ✅ MATCH
   - `standards/oauth-flows.md` - tags: `[auth, oauth]` ✅ MATCH

4. **Result:** Task's `<knowledge><files>` includes:
   ```xml
   <knowledge>
     <files>
       <file>docs/ai-knowledge/architecture/sdap-bff-api-patterns.md</file>
       <file>docs/ai-knowledge/standards/oauth-flows.md</file>
       <file>docs/reference/adr/ADR-001-minimal-api-endpoints.md</file>
       <file>docs/reference/adr/ADR-008-authentication-authorization.md</file>
     </files>
   </knowledge>
   ```

---

## Example 3: Manual Discovery During Development

**Scenario:** Developer working on Azure OpenAI integration, needs guidance.

### Developer Action

```bash
# Search skills by tag
grep -r "tags:.*openai" .claude/skills/

# Search knowledge docs by tag  
grep -r "tags:.*openai" docs/ai-knowledge/
```

**Results:**
- No existing skill for Azure OpenAI (opportunity to create one)
- Knowledge docs:
  - `docs/ai-knowledge/architecture/SPAARKE-AI-STRATEGY.md` - tags: `[ai, azure-openai, foundry]`
  - Future: `docs/ai-knowledge/guides/azure-openai-integration.md`

---

## Standard Tag Vocabulary

**See `.claude/skills/INDEX.md` for complete vocabulary.**

**Most Common Tags:**
- **Project lifecycle:** `project-init`, `tasks`, `planning`
- **Development:** `api`, `pcf`, `plugin`, `frontend`, `backend`
- **Azure/AI:** `azure`, `openai`, `ai`, `embeddings`
- **Dataverse:** `dataverse`, `dynamics`, `power-platform`
- **Operations:** `deploy`, `git`, `auth`, `security`
- **Quality:** `testing`, `code-review`, `troubleshooting`

**Tech Stack Values:**
- `aspnet-core`, `csharp`, `react`, `typescript`, `powershell`
- `azure-openai`, `semantic-kernel`, `azure-ai-search`
- `dataverse`, `power-platform`, `pcf-framework`
- `sharepoint`, `microsoft-graph`

---

## Maintenance

### When Creating New Skills
1. Follow template in `.claude/skills/_templates/SKILL-TEMPLATE.md`
2. Add `tags`, `techStack`, `appliesTo` in YAML frontmatter
3. Use standard vocabulary from `.claude/skills/INDEX.md`
4. Update skills INDEX.md with new skill entry

### When Creating Knowledge Docs
1. Add YAML frontmatter (see `docs/ai-knowledge/CLAUDE.md`)
2. Include `title`, `category`, `tags`, `techStack`, `appliesTo`
3. Use same tag vocabulary as skills
4. Update knowledge INDEX if needed

### When Working on Tasks
- Check task's `<knowledge><files>` section for pre-discovered resources
- If you find resources not included, consider updating task-create skill's discovery logic
- Document useful patterns as new knowledge docs with appropriate tags

---

## What This System Does NOT Do

❌ **No automated script execution** - No pre-task hooks, no context-loader.js
❌ **No complex scoring algorithms** - Simple tag matching only
❌ **No mandatory metadata** - Tags are recommended but optional
❌ **No centralized curation requirement** - Add tags organically as files are created/updated

## What This System DOES Do

✅ **Makes resources discoverable** - Humans and agents can find related content
✅ **Self-documenting** - Tags visible in file headers
✅ **Low maintenance** - Just add tags when creating/updating files
✅ **Incremental adoption** - Add tags to files as you touch them
✅ **Supports manual workflows** - Grep/search by tags works great
