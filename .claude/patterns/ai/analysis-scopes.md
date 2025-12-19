# Analysis Scopes Pattern

> **Domain**: AI / Prompt Construction
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-013

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisContextBuilder.cs` | Prompt building |
| `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` | Scope loading |

---

## Three-Tier Scope System

```
┌─────────────────────────────────────────────────────────────┐
│ AnalysisAction (Required)                                   │
│ - Base system prompt                                        │
│ - Defines analysis objective (Summarize, Review, etc.)      │
└─────────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────────┐
│ AnalysisSkills (Optional, Multiple)                         │
│ - Behavioral instructions added to system prompt            │
│ - Example: "Identify legal risks", "Extract entities"       │
└─────────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────────┐
│ AnalysisKnowledge (Optional, Multiple)                      │
│ - Context/grounding added to user prompt                    │
│ - Types: Inline text, Document ref, RAG index              │
└─────────────────────────────────────────────────────────────┘
```

---

## Scope Models

```csharp
public class AnalysisAction
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string SystemPrompt { get; set; }  // Base instruction set
    public int SortOrder { get; set; }
}

public class AnalysisSkill
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string PromptFragment { get; set; }  // Added to system prompt
    public string Category { get; set; }
}

public class AnalysisKnowledge
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public KnowledgeType Type { get; set; }  // Inline, Document, RagIndex
    public string Content { get; set; }       // Text or reference ID
}
```

---

## Context Building

```csharp
public class AnalysisContextBuilder
{
    public AnalysisContext Build(
        AnalysisAction action,
        IEnumerable<AnalysisSkill> skills,
        IEnumerable<AnalysisKnowledge> knowledge,
        string documentText)
    {
        // 1. Build system prompt
        var systemPrompt = new StringBuilder(action.SystemPrompt);

        foreach (var skill in skills.OrderBy(s => s.Category))
        {
            systemPrompt.AppendLine();
            systemPrompt.AppendLine($"## {skill.Name}");
            systemPrompt.AppendLine(skill.PromptFragment);
        }

        // 2. Build user prompt
        var userPrompt = new StringBuilder();

        // Add knowledge materials
        foreach (var item in knowledge.Where(k => k.Type == KnowledgeType.Inline))
        {
            userPrompt.AppendLine($"### Reference: {item.Name}");
            userPrompt.AppendLine(item.Content);
            userPrompt.AppendLine();
        }

        // Add document content
        userPrompt.AppendLine("### Document to Analyze:");
        userPrompt.AppendLine(documentText);

        return new AnalysisContext
        {
            SystemPrompt = systemPrompt.ToString(),
            UserPrompt = userPrompt.ToString()
        };
    }
}
```

---

## Key Points

1. **Action required** - Base system prompt defines behavior
2. **Skills optional** - Add behavioral instructions
3. **Knowledge optional** - Add grounding context
4. **Order matters** - Skills sorted by category
5. **JSON output** - Structured responses when enabled

---

## Related Patterns

- [Streaming Endpoints](streaming-endpoints.md) - Consume built context
- [Text Extraction](text-extraction.md) - Document text source

---

**Lines**: ~90
