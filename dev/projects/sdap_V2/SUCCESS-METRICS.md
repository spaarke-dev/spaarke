# Refactoring Success Metrics

## Code Complexity Metrics

### Service Layer Simplification
| Metric | Baseline | Target | Formula |
|--------|----------|--------|---------|
| Service Registrations | 20+ | ≤15 | Count lines in Program.cs DI section |
| Interface Count | 10+ | 2 | Count `public interface I*` in Services/ |
| Service Layer Depth | 6 | 3 | Max chain: Endpoint → Service → ... → SDK |
| Concrete Classes | ~10 | ~5 | Total service classes |

### DI Configuration
| Metric | Baseline | Target | Formula |
|--------|----------|--------|---------|
| Program.cs DI Lines | 80+ | ≤20 | Lines between `var builder = ...` and `var app = builder.Build()` |
| Feature Modules | 0 | 3 | `AddSpaarkeCore`, `AddDocumentsModule`, `AddWorkersModule` |

## Performance Metrics

### Request Latency
| Endpoint | Baseline | Target | Test Method |
|----------|----------|--------|-------------|
| File Upload (small) | 700ms | 200ms | Upload 1MB file, measure P50 |
| File Download | 500ms | 100ms | Download 1MB file, measure P50 |
| Dataverse Health Check | 300ms | 50ms | GET /healthz/dataverse, measure P50 |

### Caching Effectiveness
| Metric | Target | Test Method |
|--------|--------|-------------|
| Cache Hit Rate (after warmup) | >90% | 100 requests, same user token |
| OBO Exchange Rate | <10% | Count MSAL token acquisitions / total requests |
| Average Token Age | ~25min | Sample 100 cached tokens, measure TTL |

## Test Coverage

### Unit Tests
| Component | Coverage Target | Current | After |
|-----------|----------------|---------|-------|
| SpeFileStore | 80% | ___% | ___% |
| GraphTokenCache | 90% | ___% | ___% |
| Authorization Rules | 85% | ___% | ___% |
| Endpoints | 75% | ___% | ___% |

### Integration Tests
- [ ] File upload E2E
- [ ] File download E2E
- [ ] Token caching E2E
- [ ] Dataverse operations E2E
- [ ] Background job processing

## Code Quality Gates

### Build Quality
- [ ] Zero compiler warnings
- [ ] Zero nullable reference warnings
- [ ] All tests pass (100% pass rate)
- [ ] No deprecated API usage

### Static Analysis
- [ ] SonarQube Quality Gate: Pass
- [ ] Cyclomatic Complexity: <10 per method
- [ ] Code Duplication: <5%

## ADR Compliance Checklist

### ADR-007: Storage Seam Minimalism
- [ ] `SpeFileStore` is a concrete class (no `ISpeFileStore` interface)
- [ ] All Graph SDK calls encapsulated in `SpeFileStore`
- [ ] Endpoints only inject `SpeFileStore`, never Graph SDK types
- [ ] All methods return SDAP DTOs, never Graph SDK types

### ADR-009: Redis-First Caching
- [ ] Only `IDistributedCache` used (no `IMemoryCache`)
- [ ] Graph tokens cached with 55-minute TTL
- [ ] Cache keys versioned (e.g., `sdap:graph:token:{hash}`)
- [ ] No authorization decisions cached

### ADR-010: DI Minimalism
- [ ] Feature module pattern implemented
- [ ] Program.cs DI section ≤20 lines
- [ ] Only 2 interfaces: `IGraphClientFactory`, `IAccessDataSource`
- [ ] All services registered as concrete classes (except above)

## Documentation Completeness

- [ ] Architecture diagrams updated
- [ ] API documentation current
- [ ] Deployment guide updated
- [ ] Troubleshooting guide complete
- [ ] ADR references added to code comments

## Validation Sign-Off

### Phase 1: Configuration
- [ ] Tests pass
- [ ] Manual validation complete
- [ ] Performance baseline captured

### Phase 2: Storage Layer
- [ ] Tests pass
- [ ] No Graph SDK types in endpoints
- [ ] Upload/download working

### Phase 3: DI Simplification
- [ ] Tests pass
- [ ] Program.cs simplified
- [ ] Feature modules working

### Phase 4: Token Caching
- [ ] Tests pass
- [ ] Cache hit rate >90%
- [ ] Latency improved 70%+

### Final Validation
- [ ] All integration tests pass
- [ ] Performance targets met
- [ ] Code coverage targets met
- [ ] ADR compliance verified
- [ ] Documentation updated
- [ ] PR approved and merged

