# SDAP V2 Pattern Library

**Purpose**: Quick-reference patterns for vibe coding
**Usage**: Open pattern file → Copy code → Apply to your file → Done

---

## 🎯 Pattern Index

### Endpoints (API Layer)

| Pattern | File | Use When | Time |
|---------|------|----------|------|
| File Upload | [endpoint-file-upload.md](endpoint-file-upload.md) | Creating PUT endpoint for file upload | 15 min |
| File Download | [endpoint-file-download.md](endpoint-file-download.md) | Creating GET endpoint for file download | 10 min |
| Error Handling | [error-handling-standard.md](error-handling-standard.md) | Adding error handling to any endpoint | 5 min |

### DTOs (Data Transfer Objects)

| Pattern | File | Use When | Time |
|---------|------|----------|------|
| File Upload Result | [dto-file-upload-result.md](dto-file-upload-result.md) | Returning file metadata from upload | 5 min |

### Services (Business Logic)

| Pattern | File | Use When | Time |
|---------|------|----------|------|
| Graph Client Factory | [service-graph-client-factory.md](service-graph-client-factory.md) | Setting up OBO token exchange with caching | 30 min |
| Graph Token Cache | [service-graph-token-cache.md](service-graph-token-cache.md) | Implementing Redis token caching | 20 min |
| Dataverse Connection | [service-dataverse-connection.md](service-dataverse-connection.md) | Connecting to Dataverse with client secret | 15 min |

### DI (Dependency Injection)

| Pattern | File | Use When | Time |
|---------|------|----------|------|
| Feature Module | [di-feature-module.md](di-feature-module.md) | Organizing DI registrations | 10 min |

### ⚠️ Anti-Patterns (What NOT to Do)

| Anti-Pattern | File | Avoid When | Why | ADR |
|--------------|------|------------|-----|-----|
| Interface Proliferation | [anti-pattern-interface-proliferation.md](anti-pattern-interface-proliferation.md) | Creating unnecessary interfaces | Adds complexity, no benefit | ADR-010 |
| Leaking SDK Types | [anti-pattern-leaking-sdk-types.md](anti-pattern-leaking-sdk-types.md) | Returning DriveItem, Entity from services | Tight coupling to SDK versions | ADR-007 |
| Captive Dependency | [anti-pattern-captive-dependency.md](anti-pattern-captive-dependency.md) | Injecting Scoped into Singleton | Memory leaks, stale data, security issues | ADR-010 |

---

## 🚀 Quick Start: Vibe Coding Workflow

### Example: Add File Upload Endpoint

```bash
# 1. Open pattern files
Open: endpoint-file-upload.md
Open: dto-file-upload-result.md
Open: error-handling-standard.md

# 2. Split screen
Left: Your code file (OBOEndpoints.cs)
Right: Pattern file

# 3. Copy pattern
Copy from: endpoint-file-upload.md (lines 10-70)

# 4. Paste and adapt
Paste into: OBOEndpoints.cs
Change: containerId → your parameter name

# 5. Run checklist
✓ Authorization policy added
✓ Rate limiting applied
✓ Error handling complete
✓ Logging added

# 6. Done!
Time: 15 minutes
```

---

## 📂 File Organization

```
patterns/
├── README.md                           ← You are here
├── PATTERNS-SUMMARY.md                 ← Overview and statistics
├── QUICK-CARD.md                       ← 30-second lookup card
│
├── endpoint-file-upload.md             ← PUT /files (upload)
├── endpoint-file-download.md           ← GET /files (download)
├── dto-file-upload-result.md           ← FileUploadResult
├── service-graph-client-factory.md     ← OBO flow + caching
├── service-graph-token-cache.md        ← Redis caching
├── service-dataverse-connection.md     ← Dataverse S2S auth
├── di-feature-module.md                ← DI organization
├── error-handling-standard.md          ← ServiceException mapping
│
└── Anti-Patterns (What NOT to Do)
    ├── anti-pattern-interface-proliferation.md
    ├── anti-pattern-leaking-sdk-types.md
    └── anti-pattern-captive-dependency.md
```

---

## 🎨 Vibe Coding Tips

### 1. Use Split Screen
- Left: Your code
- Right: Pattern file
- Copy → Adapt → Done

### 2. Start with Checklist
Every pattern has a checklist at the bottom:
```markdown
## Checklist
- [ ] Authorization policy added
- [ ] Rate limiting applied
- [ ] Error handling complete
```

### 3. Follow Time Estimates
- ⚡ 5 min = Quick copy-paste
- ⏱️ 15 min = Copy + minor adaptations
- 🕐 30 min = Copy + understand + customize

### 4. Keep Index Open
Keep this README.md open in a tab for quick navigation

### 5. Use IDE Bookmarks
Bookmark pattern files you use frequently

---

## 🎯 Task → Pattern Mapping

### Phase 1 Tasks

| Task | Patterns Needed |
|------|-----------------|
| Fix Dataverse connection | `service-dataverse-connection.md` |
| Remove UAMI_CLIENT_ID | `service-graph-client-factory.md` |
| Organize DI registrations | `di-feature-module.md` |

### Adding New Upload Endpoint

| Step | Pattern |
|------|---------|
| 1. Create endpoint | `endpoint-file-upload.md` |
| 2. Create DTO | `dto-file-upload-result.md` |
| 3. Add error handling | `error-handling-standard.md` |

### Adding Token Caching

| Step | Pattern |
|------|---------|
| 1. Create cache service | `service-graph-token-cache.md` |
| 2. Integrate with factory | `service-graph-client-factory.md` |
| 3. Register in DI | `di-feature-module.md` |

---

## 📖 Pattern Features

Each pattern file includes:

✅ **Quick Copy-Paste** - Full working code at top
✅ **Time Estimate** - How long to implement
✅ **Checklist** - Verify completeness
✅ **Key Points** - Important concepts
✅ **Related Files** - Where to apply the pattern
✅ **DI Registration** - How to register services

---

## 🔍 Finding Patterns

### By Feature
- **File Operations**: `endpoint-file-*.md`
- **Authentication**: `service-graph-*.md`
- **Data Access**: `service-dataverse-*.md`
- **DI Setup**: `di-*.md`
- **Data Transfer**: `dto-*.md`

### By Layer
- **API Layer**: `endpoint-*.md`, `error-handling-*.md`
- **Service Layer**: `service-*.md`
- **Data Layer**: `dto-*.md`
- **Infrastructure**: `di-*.md`

### By ADR
- **ADR-007** (SPE Storage Seam): `endpoint-file-*.md`, `service-spe-*.md`, ⚠️ `anti-pattern-leaking-sdk-types.md`
- **ADR-009** (Caching): `service-graph-token-cache.md`
- **ADR-010** (DI Minimalism): `di-feature-module.md`, ⚠️ `anti-pattern-interface-proliferation.md`, ⚠️ `anti-pattern-captive-dependency.md`

---

## 💡 Pro Tips

### When Stuck
1. Open relevant pattern file
2. Read "Key Points" section
3. Check checklist
4. Copy code and adapt

### When Starting New Feature
1. Find pattern in index above
2. Open pattern file
3. Follow time estimate
4. Use checklist to verify

### When Reviewing Code
1. Check against pattern checklist
2. Verify error handling
3. Verify logging
4. Verify DI lifetime
5. Check for anti-patterns (interface proliferation, SDK type leakage, captive dependencies)

---

## 📚 Related Documentation

- **ADRs**: [../ARCHITECTURAL-DECISIONS.md](../ARCHITECTURAL-DECISIONS.md)
- **Full Patterns**: [../CODE-PATTERNS.md](../CODE-PATTERNS.md) (1500+ lines reference)
- **Anti-Patterns**: [../ANTI-PATTERNS.md](../ANTI-PATTERNS.md) (Complete anti-pattern reference)
- **Target Architecture**: [../TARGET-ARCHITECTURE.md](../TARGET-ARCHITECTURE.md)
- **Task-Pattern Map**: [../TASK-PATTERN-MAP.md](../TASK-PATTERN-MAP.md)
- **Architecture Overview**: [../../SDAP-ARCHITECTURE-OVERVIEW-V2-2025-10-13-2213.md](../../SDAP-ARCHITECTURE-OVERVIEW-V2-2025-10-13-2213.md)

---

## 🤝 Contributing Patterns

When you create a useful pattern:

1. Create new `.md` file in `patterns/`
2. Follow existing pattern format:
   - Title with "Use For" / "Task" / "Time"
   - Quick Copy-Paste section at top
   - Key Points
   - Checklist
   - Related Files
3. Add to index in this README.md
4. Update task mappings

---

**Last Updated**: 2025-10-13
**Pattern Count**: 11 patterns (8 correct patterns + 3 anti-patterns)
**Status**: Active - Ready for Phase 1 refactoring
