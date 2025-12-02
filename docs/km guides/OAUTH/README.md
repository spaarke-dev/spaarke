# SDAP Knowledge Base - Index

Quick reference for AI agents during task execution.

## How to Use

1. Task instructions reference specific knowledge files
2. Load only the file(s) needed for current task
3. Cross-references point to related topics

## OAuth 2.0 / Authentication

| File | Use When | Size |
|------|----------|------|
| `oauth/oauth-obo-quick-reference.md` | Deciding if OBO needed | 30 lines |
| `oauth/oauth-obo-implementation.md` | Implementing OBO flow | 80 lines |
| `oauth/oauth-obo-anti-patterns.md` | Code review | 50 lines |
| `oauth/oauth-obo-errors.md` | Debugging OBO failures | 60 lines |
| `oauth/oauth-obo-testing.md` | Writing tests | 40 lines |

## Dataverse

| File | Use When | Size |
|------|----------|------|
| `dataverse/dataverse-serviceclient.md` | Implementing Dataverse access | 70 lines |
| `dataverse/dataverse-errors.md` | Debugging Dataverse calls | 50 lines |

## Microsoft Graph API

| File | Use When | Size |
|------|----------|------|
| `graph-api/graph-spe-operations.md` | SPE file operations | 90 lines |
| `graph-api/graph-errors.md` | Debugging Graph calls | 60 lines |

## Caching

| File | Use When | Size |
|------|----------|------|
| `caching/redis-patterns.md` | Implementing distributed cache | 60 lines |
| `caching/token-caching.md` | Caching access tokens | 50 lines |

## Decision Trees

**Need to call downstream API?**
→ `oauth/oauth-obo-quick-reference.md`

**Dataverse connection failing?**
→ `dataverse/dataverse-errors.md`

**SPE file operation failing?**
→ `graph-api/graph-errors.md`

**Cache not working?**
→ `caching/redis-patterns.md`