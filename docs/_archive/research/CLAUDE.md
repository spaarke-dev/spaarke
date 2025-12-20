# Reference Documentation

> **Purpose**: Background material, historical records, and research archives.

---

## ⚠️ IMPORTANT: Do Not Load Unless Asked

**Do not load or reference documents from this directory unless explicitly asked.**

These documents are:
- Historical records (ADRs, past decisions)
- Verbose reference material (full architecture guides)
- Research articles (KM-* knowledge management files)
- Background context that is not relevant to coding tasks

**Why?** Loading these documents unnecessarily consumes context window and may introduce outdated or overly detailed information that conflicts with the condensed, actionable content in `/docs/ai-knowledge/`.

---

## Directory Contents

### `/adr/` - Architecture Decision Records
Historical decisions that shaped the codebase. Useful for understanding *why* something was decided, but the *what* is encoded in coding standards.

**When to reference:**
- User explicitly asks "why was X decided?"
- Need historical context for a major change
- Evaluating whether to supersede an ADR

### `/research/` - Knowledge Management Articles
Detailed reference articles (KM-* files) covering specific technologies. These are verbose and comprehensive.

**When to reference:**
- User asks for deep-dive on specific technology
- Troubleshooting complex issues not covered in guides
- Learning a new technology area

### `/articles/` - Archived Content
Historical articles and documentation that may be outdated.

**When to reference:**
- Almost never - ask the developer first

---

## If You Need Historical Context

Ask the developer before searching here:

```
"I see you're asking about [topic]. I have reference documentation on this in 
/docs/reference/. Would you like me to review that for historical context, or 
should I proceed with the current patterns in /docs/ai-knowledge/?"
```

---

*The actionable, coding-relevant content is in `/docs/ai-knowledge/`.*
