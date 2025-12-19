# AI Knowledge Article Template

> **Last Updated**: December 3, 2025
>
> **Purpose**: Template for creating structured AI knowledge articles optimized for Claude Code consumption.
>
> **Format**: Markdown (optimized for AI parsing)

---

## How to Use This Template

When creating a new AI knowledge article:

1. Copy this template to `docs/ai-knowledge/summaries/{topic-name}.md`
2. Use the generation prompt below with Claude Code
3. Provide source material (docs, code examples, your expertise)
4. Review and verify the generated article
5. Update `docs/ai-knowledge/INDEX.md` with the new article

---

## Generation Prompt

Use this prompt to generate a new knowledge article:

```
Create an AI knowledge article following the template structure below.

**Role**: You are a Senior Developer and Microsoft MVP creating an AI knowledge 
article for use by Claude Code. Your focus is:
- Conciseness: Every line must earn its place; no filler or history
- Precision: Exact patterns, exact syntax, exact constraints  
- Actionability: Code that compiles, patterns that work
- AI-Optimization: Structure for quick parsing and extraction

Write as if context window space costs $1 per token. Be dense but clear.

**Constraints**:
- Total article length: 50-150 lines (excluding comments)
- TL;DR section: Maximum 10 lines
- Each pattern: Maximum 20 lines including code
- No historical context or "why it was designed this way"
- No links to external tutorials (link to official docs only)
- All code examples must be syntactically correct
- Must include at least 3 common mistakes

**Topic**: {TOPIC_NAME}

**Source Material**:
{PASTE RELEVANT DOCS, CODE EXAMPLES, OR DESCRIBE YOUR REQUIREMENTS}

**Output**: Create the article using the template structure below.
```

---

## Article Template

Copy everything below this line for new articles:

---

````markdown
# {Topic Title}

> **Last Verified**: {YYYY-MM-DD}
> **Applies To**: {Components/scenarios this knowledge covers}
> **Owner**: {Team or person responsible for accuracy}
> **Related ADRs**: {ADR-NNN, ADR-NNN}

## TL;DR

- **What**: {One sentence description}
- **When to use**: {Trigger conditions}
- **Key constraint**: {Most important rule}
- **Primary pattern**: {The default approach}
- **Gotcha**: {Most common mistake}

## Applies When

| Condition | This Article Applies |
|-----------|---------------------|
| {Scenario 1} | ✅ Yes |
| {Scenario 2} | ✅ Yes |
| {Scenario 3} | ❌ No - see {other article} |

## Key Patterns

### Pattern 1: {Pattern Name}

**Use when**: {Specific trigger condition}

```{language}
// Minimal, working code example
```

**Critical details**:
- {Detail 1}
- {Detail 2}

---

### Pattern 2: {Pattern Name}

**Use when**: {Specific trigger condition}

```{language}
// Minimal, working code example
```

---

### Pattern 3: {Pattern Name}

**Use when**: {Specific trigger condition}

```{language}
// Minimal, working code example
```

## Common Mistakes

| ❌ Mistake | Why It's Wrong | ✅ Correct Approach |
|-----------|----------------|---------------------|
| {Bad code/practice} | {Consequence} | {Good code/practice} |
| {Bad code/practice} | {Consequence} | {Good code/practice} |
| {Bad code/practice} | {Consequence} | {Good code/practice} |

## Configuration / Setup

<!-- Skip if not applicable -->

```{language}
// Required configuration or DI setup
```

## Error Handling

| Error Scenario | Handling Pattern |
|----------------|------------------|
| {Error type 1} | {How to handle} |
| {Error type 2} | {How to handle} |

## Testing Patterns

<!-- Skip if not applicable -->

```{language}
// Test setup or mock pattern
```

## Related Resources

| Resource | Purpose |
|----------|---------|
| {ADR-NNN} | {Why relevant} |
| {Official doc URL} | {What it covers} |
| {Code example path} | {What it demonstrates} |

## Verification Checklist

- [ ] Code examples compile without errors
- [ ] Patterns align with current ADRs
- [ ] No deprecated APIs referenced
- [ ] Tested against current version of dependencies
- [ ] Common mistakes verified from actual incidents/PRs

---

*Article version: 1.0 | Last verified by: {Name} on {Date}*
````

---

## Section Guidelines

| Section | Purpose | Rules |
|---------|---------|-------|
| **TL;DR** | 80% value from 10% content | Max 10 lines, bullets only, no code |
| **Applies When** | Help AI decide relevance | Table format, include negative matches |
| **Key Patterns** | Copy-paste-ready code | 3-5 patterns, ≤20 lines each, must compile |
| **Common Mistakes** | Prevent bad code | Min 3, table format, from real code reviews |
| **Optional sections** | Skip if not applicable | Config, Error Handling, Testing, Performance |

## Naming Convention

```
docs/ai-knowledge/summaries/{category}-{topic}.md

Examples:
- graph-sharepoint-embedded.md
- pcf-fluent-ui-components.md  
- dataverse-plugin-patterns.md
- auth-msal-obo-flow.md
- dotnet-minimal-api-endpoints.md
```

## Quality Checklist

Before publishing:

- [ ] TL;DR is ≤10 lines and captures essentials
- [ ] Total length is 50-150 lines
- [ ] All code examples are syntactically correct
- [ ] At least 3 common mistakes documented
- [ ] "Applies When" table helps AI decide relevance
- [ ] No historical/background content
- [ ] Related ADRs are linked
- [ ] Added to INDEX.md

---

*Template version: 1.1 | Markdown format*
