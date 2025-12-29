# Phase 3: Systematic Review & Revision Plan

> **Created**: December 18, 2025
> **Purpose**: Organize Phase 3 work by topic with cross-referencing strategy
> **Goal**: Create efficient AI context library with clear links to full documentation

---

## Cross-Reference Strategy

### Bidirectional Linking Convention

**In AI Context Files** (`.claude/`):
```markdown
## Source Documentation

This content is extracted/condensed from:
- [ADR-001 Full Version](../../docs/adr/ADR-001-minimal-api-and-workers.md)
- [BFF API Guide](../../docs/guides/BFF-API-DEVELOPMENT.md)

For detailed context, historical decisions, and examples, see full documentation above.
```

**In Full Documentation Files** (`docs/`):
```markdown
## Related AI Context

**AI-Optimized Versions**:
- [Concise ADR-001](../../.claude/adr/ADR-001-minimal-api.md) - 120 lines
- [API Constraints](../../.claude/constraints/api.md) - MUST/MUST NOT rules
- [API Patterns](../../.claude/patterns/api/endpoint-definition.md) - Code examples

AI agents should load these concise versions for context efficiency.
```

### Cross-Reference Map

Create `CROSS-REFERENCE-MAP.md` in root to show all relationships:
```markdown
| Topic | AI Context | Full Documentation |
|-------|-----------|-------------------|
| Minimal API | .claude/adr/ADR-001.md | docs/adr/ADR-001-minimal-api-and-workers.md |
| API Constraints | .claude/constraints/api.md | docs/adr/ADR-001, ADR-008, ADR-010, docs/guides/BFF-API-*.md |
```

---

## Phase 3 Subtasks by Topic

### Topic 1: Architecture Decision Records (ADRs)

**Scope**: 22 ADRs covering all architectural decisions

**Tasks**:
- [ ] **3.1.1** Create concise versions in `.claude/adr/` (100-150 lines each)
  - Focus on: Decision, Constraints, Key Patterns, Brief Rationale
  - Omit: Verbose context, historical discussion, long alternatives analysis
  - Add: Cross-reference to full ADR in `docs/adr/`

- [ ] **3.1.2** Update full ADRs in `docs/adr/` with:
  - "Related AI Context" section at top
  - Links to concise version, constraints, patterns

- [ ] **3.1.3** Update `docs/adr/INDEX.md` with brief descriptions

**Output**:
- 22 concise ADRs in `.claude/adr/`
- 22 updated full ADRs in `docs/adr/` with cross-references
- Updated INDEX.md files

**Priority**: High (ADRs are core to AI decision-making)

---

### Topic 2: API/BFF Patterns & Constraints

**Scope**: Minimal API, endpoint filters, DI, resilience, error handling

**Source Documents**:
- ADR-001, ADR-004, ADR-008, ADR-010, ADR-017
- Guides: BFF-API-DEVELOPMENT.md (if exists)
- Architecture: sdap-bff-api-patterns.md

**Tasks**:
- [ ] **3.2.1** Create `.claude/constraints/api.md` (150-200 lines)
  - MUST use Minimal API pattern
  - MUST use endpoint filters for authorization
  - MUST NOT use global middleware for resource auth
  - MUST keep DI registrations ≤15 non-framework
  - MUST use Polly for resilience
  - MUST return ProblemDetails for errors

- [ ] **3.2.2** Create `.claude/patterns/api/` files:
  - `endpoint-definition.md` - Minimal API endpoint patterns
  - `endpoint-filters.md` - Authorization filter patterns
  - `service-registration.md` - DI patterns
  - `error-handling.md` - ProblemDetails patterns
  - `resilience.md` - Polly retry patterns

- [ ] **3.2.3** Add cross-references in source ADRs and guides

**Output**:
- 1 constraint file: `.claude/constraints/api.md`
- 5 pattern files in `.claude/patterns/api/`
- Cross-references added to 5 ADRs and relevant guides

**Priority**: High (frequently used in development)

---

### Topic 3: PCF Controls

**Scope**: PCF development patterns, React hooks, Dataverse integration, error handling

**Source Documents**:
- ADR-006, ADR-012, ADR-014, ADR-015, ADR-018
- Guides: PCF-*.md (4 files), HOW-TO-CREATE-NEW-PCF-CONTROL.md, HOW-TO-DEPLOY-PCF-CONTROL.md
- Architecture: sdap-pcf-patterns.md

**Tasks**:
- [ ] **3.3.1** Create `.claude/constraints/pcf.md` (150-200 lines)
  - MUST use PCF over legacy web resources
  - MUST use shared component library
  - MUST use Fluent UI v9 exclusively
  - MUST implement proper error handling
  - MUST cache metadata appropriately
  - MUST follow module structure pattern

- [ ] **3.3.2** Create `.claude/patterns/pcf/` files:
  - `control-initialization.md` - PCF init and updateView
  - `react-hooks.md` - React hook patterns for PCF
  - `dataverse-queries.md` - WebAPI query patterns
  - `error-display.md` - User-friendly error UI
  - `metadata-caching.md` - Metadata retrieval and caching

- [ ] **3.3.3** Consolidate PCF guides (4 guides may have overlap)
  - Review for duplication
  - Consolidate if appropriate
  - Add cross-references

- [ ] **3.3.4** Add cross-references in source ADRs and guides

**Output**:
- 1 constraint file: `.claude/constraints/pcf.md`
- 5 pattern files in `.claude/patterns/pcf/`
- Consolidated PCF guides in `docs/guides/`
- Cross-references added to 5 ADRs and 6+ guides

**Priority**: High (frequently used in development)

---

### Topic 4: Authentication & Authorization

**Scope**: OAuth, OBO, Dataverse auth, token management

**Source Documents**:
- ADR-004, ADR-016
- Standards: oauth-obo-*.md (3 files), dataverse-oauth-authentication.md
- Architecture: auth-boundaries.md, sdap-auth-patterns.md

**Tasks**:
- [ ] **3.4.1** Create `.claude/constraints/auth.md` (150-200 lines)
  - MUST use correct OAuth scope format (api://{guid}/scope)
  - MUST implement OBO flow correctly
  - MUST NOT use friendly names in scopes
  - MUST use endpoint filters for authorization
  - MUST follow Dataverse auth patterns

- [ ] **3.4.2** Create `.claude/patterns/auth/` files:
  - `oauth-scopes.md` - Correct OAuth scope patterns
  - `token-acquisition.md` - MSAL token patterns
  - `obo-flow.md` - On-Behalf-Of implementation
  - `service-principal.md` - Service-to-service auth

- [ ] **3.4.3** Review and consolidate OAuth standards (3 files may overlap)

- [ ] **3.4.4** Add cross-references in source documents

**Output**:
- 1 constraint file: `.claude/constraints/auth.md`
- 4 pattern files in `.claude/patterns/auth/`
- Consolidated standards in `docs/standards/`
- Cross-references added to 2 ADRs, 4 standards, 2 architecture docs

**Priority**: High (security-critical, frequently referenced)

---

### Topic 5: Dataverse (Plugins, Web API, Relationships)

**Scope**: Plugin development, Dataverse queries, navigation, metadata

**Source Documents**:
- ADR-002
- Guides: DATAVERSE-*.md (3 files)
- Architecture: sdap-overview.md, sdap-troubleshooting.md

**Tasks**:
- [ ] **3.5.1** Create `.claude/constraints/plugins.md` (150-200 lines)
  - MUST keep plugins thin (<200 LoC, <50ms p95)
  - MUST NOT make HTTP/Graph calls from plugins
  - MUST NOT implement orchestration in plugins
  - MUST use early-bound types when possible

- [ ] **3.5.2** Create `.claude/patterns/dataverse/` files:
  - `plugin-structure.md` - Thin plugin patterns
  - `early-binding.md` - Strongly-typed entity patterns
  - `web-api-calls.md` - Dataverse Web API patterns
  - `relationship-navigation.md` - N:1, 1:N navigation

- [ ] **3.5.3** Add cross-references in source documents

**Output**:
- 1 constraint file: `.claude/constraints/plugins.md`
- 4 pattern files in `.claude/patterns/dataverse/`
- Cross-references added to 1 ADR, 3 guides, 2 architecture docs

**Priority**: Medium-High

---

### Topic 6: Data Access & Caching

**Scope**: SPE file storage, caching strategy, Graph API usage

**Source Documents**:
- ADR-007, ADR-009, ADR-019, ADR-005
- Architecture: INFRASTRUCTURE-PACKAGING-STRATEGY.md

**Tasks**:
- [ ] **3.6.1** Create `.claude/constraints/data.md` (150-200 lines)
  - MUST use SpeFileStore facade
  - MUST NOT leak Graph SDK types above facade
  - MUST use Redis-first caching
  - MUST NOT use hybrid L1 cache without profiling
  - MUST follow SPE container lifecycle

- [ ] **3.6.2** Create `.claude/patterns/data/` files (if needed)
  - `spefilestore-usage.md` - Facade pattern usage
  - `redis-caching.md` - Caching patterns

- [ ] **3.6.3** Add cross-references in source documents

**Output**:
- 1 constraint file: `.claude/constraints/data.md`
- 2 pattern files (optional)
- Cross-references added to 4 ADRs

**Priority**: Medium

---

### Topic 7: AI & Document Intelligence

**Scope**: AI tool framework, streaming, SSE, prompt engineering

**Source Documents**:
- ADR-013
- Guides: AI-*.md (4 files)
- Architecture: SPAARKE-AI-STRATEGY.md

**Tasks**:
- [ ] **3.7.1** Review AI guides for consolidation opportunities

- [ ] **3.7.2** Create `.claude/constraints/ai.md` (if needed)
  - AI Tool Framework patterns
  - Dual pipeline requirements

- [ ] **3.7.3** Create `.claude/patterns/ai/` files (if needed)

- [ ] **3.7.4** Add cross-references in source documents

**Output**:
- Consolidated AI guides (if needed)
- Optional constraint/pattern files
- Cross-references added

**Priority**: Medium (less frequently used in current phase)

---

### Topic 8: Testing

**Scope**: Unit tests, integration tests, test patterns

**Source Documents**:
- ADR-022
- Architecture: (testing info may be scattered)

**Tasks**:
- [ ] **3.8.1** Create `.claude/constraints/testing.md` (150-200 lines)
  - MUST write tests for all code changes
  - MUST mirror src/ structure in tests/unit/
  - Testing requirements from ADR-022

- [ ] **3.8.2** Create `.claude/patterns/testing/` files:
  - `unit-test-structure.md` - xUnit patterns
  - `mocking.md` - NSubstitute patterns
  - `integration-tests.md` - WebApplicationFactory patterns
  - `test-data-builders.md` - Test data patterns

- [ ] **3.8.3** Add cross-references in source documents

**Output**:
- 1 constraint file: `.claude/constraints/testing.md`
- 4 pattern files in `.claude/patterns/testing/`
- Cross-references added to ADR-022

**Priority**: Medium

---

### Topic 9: Configuration & Deployment

**Scope**: Configuration management, telemetry, infrastructure

**Source Documents**:
- ADR-020, ADR-021
- Architecture: AZURE-RESOURCE-NAMING-CONVENTION.md, INFRASTRUCTURE-PACKAGING-STRATEGY.md

**Tasks**:
- [ ] **3.9.1** Create `.claude/constraints/config.md` (if needed)
  - Configuration patterns from ADR-021
  - Telemetry requirements from ADR-020

- [ ] **3.9.2** Review architecture docs for consolidation

- [ ] **3.9.3** Add cross-references in source documents

**Output**:
- Optional constraint file
- Reviewed architecture docs
- Cross-references added

**Priority**: Low (less frequently changed)

---

### Topic 10: Repository Architecture & General Patterns

**Scope**: Repository structure, UX management, general architecture

**Source Documents**:
- Architecture: SPAARKE-REPOSITORY-ARCHITECTURE.md, SPAARKE-UX-MANAGEMENT.md, SPAARKE-AI-STRATEGY.md

**Tasks**:
- [ ] **3.10.1** Review for accuracy and updates needed

- [ ] **3.10.2** Add cross-references to related AI context

- [ ] **3.10.3** Update with new `.claude/` and `docs/` structure

**Output**:
- Updated architecture docs
- Cross-references added

**Priority**: Low (mostly reference material)

---

## Additional Phase 3 Tasks

### Create Master Cross-Reference Map

- [ ] **3.11** Create `CROSS-REFERENCE-MAP.md` in root
  - Table showing all AI context files and their source docs
  - Reverse mapping showing docs and their AI versions
  - Quick lookup for developers and AI agents

### Update Root Documentation

- [ ] **3.12** Update `CLAUDE.md` with new structure
  - Reference `.claude/` loading strategy
  - Reference `docs/` for deep dives
  - Update documentation hierarchy section

- [ ] **3.13** Update `docs/CLAUDE.md` (traffic controller)
  - New directory structure
  - Cross-reference guidance

### Validate Context Efficiency

- [ ] **3.14** Test AI loading scenarios
  - "Create PCF Control" - measure context loaded
  - "Create BFF Endpoint" - measure context loaded
  - "Implement Auth" - measure context loaded
  - Validate 80-90% reduction vs old structure

---

## Execution Order

**Week 1: High Priority Topics**
1. Topic 1: ADRs (3.1.x)
2. Topic 2: API/BFF (3.2.x)
3. Topic 3: PCF (3.3.x)
4. Topic 4: Auth (3.4.x)

**Week 2: Medium Priority Topics**
5. Topic 5: Dataverse (3.5.x)
6. Topic 6: Data Access (3.6.x)
7. Topic 8: Testing (3.8.x)

**Week 3: Remaining Topics & Validation**
8. Topic 7: AI (3.7.x)
9. Topic 9: Config (3.9.x)
10. Topic 10: General (3.10.x)
11. Cross-Reference Map (3.11)
12. Root Docs Update (3.12-3.13)
13. Validation (3.14)

---

## Success Criteria

- [ ] All 22 ADRs have concise versions in `.claude/adr/`
- [ ] All domain constraint files created in `.claude/constraints/`
- [ ] All pattern files created in `.claude/patterns/`
- [ ] All documents have bidirectional cross-references
- [ ] CROSS-REFERENCE-MAP.md exists and is complete
- [ ] Context loading is 80-90% more efficient than old structure
- [ ] No broken links or missing references
- [ ] INDEX.md files are complete and accurate

---

## Notes

- **Consistency**: Use the cross-reference format consistently
- **Maintenance**: When updating a full doc, check CROSS-REFERENCE-MAP.md for AI versions that need updating
- **Validation**: Test AI context loading after each topic is complete
- **Flexibility**: Adjust priorities based on immediate development needs
