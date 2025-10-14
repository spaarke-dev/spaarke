# 🎯 SDAP Pattern Quick Card

**Bookmark this file** → Instant pattern lookup

---

## ⚡ 30-Second Lookup

| I Need To... | Open This Pattern | Time |
|--------------|-------------------|------|
| **Upload file** | [endpoint-file-upload.md](endpoint-file-upload.md) | 15m |
| **Download file** | [endpoint-file-download.md](endpoint-file-download.md) | 10m |
| **Return file data** | [dto-file-upload-result.md](dto-file-upload-result.md) | 5m |
| **Handle errors** | [error-handling-standard.md](error-handling-standard.md) | 5m |
| **Connect Graph API** | [service-graph-client-factory.md](service-graph-client-factory.md) | 30m |
| **Cache tokens** | [service-graph-token-cache.md](service-graph-token-cache.md) | 20m |
| **Connect Dataverse** | [service-dataverse-connection.md](service-dataverse-connection.md) | 15m |
| **Organize DI** | [di-feature-module.md](di-feature-module.md) | 10m |

### ⚠️ Anti-Patterns (What to Avoid)

| Don't Do This... | Read This | Risk |
|------------------|-----------|------|
| **Create unnecessary interfaces** | [anti-pattern-interface-proliferation.md](anti-pattern-interface-proliferation.md) | Complexity |
| **Return DriveItem/Entity** | [anti-pattern-leaking-sdk-types.md](anti-pattern-leaking-sdk-types.md) | Tight coupling |
| **Inject Scoped into Singleton** | [anti-pattern-captive-dependency.md](anti-pattern-captive-dependency.md) | Memory leaks |

---

## 🔥 Most Used (80% of Tasks)

```
1. error-handling-standard.md    ← Every endpoint needs this
2. endpoint-file-upload.md        ← Common operation
3. dto-file-upload-result.md      ← Every endpoint returns data
```

---

## 📋 3-Step Workflow

```
1. Find pattern above ↑
2. Copy code from pattern
3. Paste & adapt to your file
```

**Average time**: 5-15 minutes per feature

---

## 🎨 Split Screen Setup

```
┌─────────────────┬─────────────────┐
│                 │                 │
│  Your Code      │  Pattern File   │
│  (Left)         │  (Right)        │
│                 │                 │
│  OBOEndpoints   │  endpoint-file- │
│  .cs            │  upload.md      │
│                 │                 │
│  ← Copy ← ← ← ← │  [code here]    │
│                 │                 │
└─────────────────┴─────────────────┘
```

---

## 💡 Checklist Usage

Every pattern file ends with:
```markdown
## Checklist
- [ ] Item 1
- [ ] Item 2
- [ ] Item 3
```

Use it to verify your implementation ✅

---

## 🚨 Emergency Lookup

### Error 401 (Unauthorized)
→ Check: [service-graph-client-factory.md](service-graph-client-factory.md)
→ Section: OBO token exchange

### Error 403 (Forbidden)
→ Check: [service-dataverse-connection.md](service-dataverse-connection.md)
→ Section: Application User setup

### Error 500 (Internal)
→ Check: [error-handling-standard.md](error-handling-standard.md)
→ Section: Exception mapping

### Slow Performance
→ Check: [service-graph-token-cache.md](service-graph-token-cache.md)
→ Section: 97% latency reduction

---

## 📱 Mobile-Friendly View

Can't remember pattern names? Use symbols:

- 📤 Upload → `endpoint-file-upload.md`
- 📥 Download → `endpoint-file-download.md`
- 📦 DTO → `dto-file-upload-result.md`
- 🔑 Auth → `service-graph-client-factory.md`
- 💾 Cache → `service-graph-token-cache.md`
- 🗄️ Database → `service-dataverse-connection.md`
- 🔌 DI → `di-feature-module.md`
- ⚠️ Errors → `error-handling-standard.md`

**Anti-Patterns**:
- 🚫 No interfaces → `anti-pattern-interface-proliferation.md`
- 🚫 No SDK types → `anti-pattern-leaking-sdk-types.md`
- 🚫 No captive deps → `anti-pattern-captive-dependency.md`

---

## 🎯 Task Shortcuts

**Phase 1 Tasks**:
- Fix Dataverse → `service-dataverse-connection.md`
- Fix GraphFactory → `service-graph-client-factory.md`
- Organize DI → `di-feature-module.md`

**Common Dev**:
- New endpoint → `endpoint-file-*.md` + `error-handling-*.md`
- New DTO → `dto-file-upload-result.md` (template)

---

## 🏆 Time Savings

| Task | Without Patterns | With Patterns | Savings |
|------|-----------------|---------------|---------|
| Upload endpoint | 60 min | 15 min | 75% ⚡ |
| Error handling | 30 min | 5 min | 83% ⚡ |
| Graph setup | 120 min | 30 min | 75% ⚡ |

**Average savings**: 80% less time

---

## 🔗 Quick Links

- **Browse all patterns**: [README.md](README.md)
- **Find by task**: [../TASK-PATTERN-MAP.md](../TASK-PATTERN-MAP.md)
- **Full reference**: [../CODE-PATTERNS.md](../CODE-PATTERNS.md)
- **Anti-patterns**: [../ANTI-PATTERNS.md](../ANTI-PATTERNS.md)

---

**Last Updated**: 2025-10-13
**Pattern Count**: 11 patterns (8 correct + 3 anti-patterns)

**Print this card** → Keep on desk → Save hours 🎯
