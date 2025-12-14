# Context Engineering Quick Reference

> **Audience**: AI Agents  
> **Load when**: Context usage > 50%  
> **Part of**: [Spaarke Software Development Procedures](INDEX.md)

---

## Context Window

Claude Code has ~200,000 token context window encompassing:
- Conversation history
- File reads
- Tool interactions
- CLAUDE.md content
- AI outputs

---

## Context Thresholds

| Usage | Level | Action |
|-------|-------|--------|
| < 50% | ✅ Normal | Proceed normally |
| 50-70% | ⚠️ Warning | Monitor, wrap up current subtask |
| > 70% | 🛑 Critical | STOP, create handoff, new session |
| > 85% | 🚨 Emergency | Immediately create handoff |

---

## Context Commands

| Command | Purpose |
|---------|---------|
| `/context` | Display current context usage |
| `/clear` | Wipe conversation context |
| `/compact` | Compress conversation to reclaim space |
| `/resume` | Revisit previous session |

---

## Best Practices

| Practice | Description |
|----------|-------------|
| **Monitor usage** | Check context before memory-intensive operations |
| **Document and clear** | Write progress to file, clear context, continue in new session |
| **Working directory** | Launch from specific module, not repo root |
| **Be specific** | Clear instructions reduce iterations and wasted tokens |
| **Offload to files** | Write design docs, checklists to files rather than keeping in memory |
| **Read efficiently** | Load larger chunks at once rather than many small reads |

---

## Handoff Protocol

When context > 70%:

1. **Create handoff summary** at `notes/handoffs/handoff-{NNN}.md`
2. **Include**:
   - Task ID and title
   - Completed subtasks
   - Remaining subtasks
   - Files modified
   - Decisions made
   - Resources for next session
3. **Tell user**: "Context at {X}%. Please start new session with handoff."

---

## Handoff Template

```markdown
# Handoff Summary - Task {ID}

## Task
- **ID**: {task-id}
- **Title**: {task-title}

## Completed
- [x] Subtask 1
- [x] Subtask 2

## Remaining
- [ ] Subtask 3
- [ ] Subtask 4

## Files Modified
- `src/path/file.cs` - Description

## Decisions Made
- Choice A because {reason}

## Resources to Load
- `docs/reference/adr/ADR-XXX.md`
```

---

## Session Strategy

| Situation | Recommendation |
|-----------|----------------|
| Starting new phase | Fresh session |
| Context > 70% | Handoff → fresh session |
| Complex task | Break into subtasks, checkpoint between |
| Simple task | Complete in current session |

---

*Part of [Spaarke Software Development Procedures](INDEX.md) | v2.0 | December 2025*
