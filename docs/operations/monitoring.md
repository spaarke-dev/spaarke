# SDAP Monitoring and Observability

## Overview

This document outlines the monitoring, logging, and observability strategy for the SDAP (SharePoint Document Access Platform) system.

## Components to Monitor

### 1. API Service (Spe.Bff.Api)
- **Health Checks**: `/ping`, `/healthz`
- **Key Metrics**:
  - Request latency (p50, p95, p99)
  - Request rate and error rate
  - Authentication success/failure rates
  - Graph API call rates and latencies
  - Memory usage and GC pressure

### 2. Power Platform Plugins
- **Performance Metrics**:
  - Plugin execution time (target: p95 < 50ms per ADR-002)
  - Plugin failure rates
  - Transaction rollback rates
  - Dataverse service protection limit hits

### 3. External Dependencies
- **Microsoft Graph API**:
  - Rate limiting status
  - Response times
  - Error rates by endpoint
  - Throttling events
- **Azure Services**:
  - KeyVault access patterns
  - Redis cache hit/miss rates (when implemented)

## Logging Strategy

### Log Levels
- **Error**: Unhandled exceptions, authentication failures, critical business logic failures
- **Warning**: Retries, degraded performance, validation failures
- **Information**: Business events, successful operations, performance milestones
- **Debug**: Detailed execution traces (development only)

### Structured Logging
```json
{
  "timestamp": "2025-09-28T10:00:00Z",
  "level": "Information",
  "message": "Document uploaded successfully",
  "traceId": "abc123",
  "userId": "user@domain.com",
  "containerId": "container-guid",
  "documentId": "doc-guid",
  "sizeBytes": 1024,
  "duration": "00:00:01.234"
}
```

### Correlation IDs
- Every request includes a `traceId` for end-to-end tracing
- Plugin executions include correlation to triggering API requests
- All external API calls include correlation context

## Alerting Rules

### Critical Alerts (Immediate Response)
- API health check failures
- Error rate > 5% over 5 minutes
- Plugin execution time p95 > 50ms (ADR-002 violation)
- Authentication service unavailable
- Graph API rate limiting exhausted

### Warning Alerts (Monitor)
- API response time p95 > 2 seconds
- Plugin execution time p95 > 30ms
- Error rate > 1% over 15 minutes
- Unusual authentication patterns

### Informational Alerts (Trend Analysis)
- Daily usage reports
- Plugin performance trends
- Cache efficiency reports
- Resource utilization trends

## Dashboards

### 1. Service Health Dashboard
- Overall system status
- API availability and performance
- Plugin execution health
- External dependency status

### 2. Business Metrics Dashboard
- Document upload/download volumes
- User activity patterns
- Container usage statistics
- Feature adoption metrics

### 3. Technical Performance Dashboard
- Response time distributions
- Error rate trends
- Resource utilization
- Cache performance (when implemented)

## Implementation

### Application Insights Integration
```csharp
// In Program.cs
builder.Services.AddApplicationInsightsTelemetry();

// Custom telemetry
builder.Services.AddSingleton<ITelemetryInitializer, CustomTelemetryInitializer>();
```

### Plugin Telemetry
```csharp
// In ValidationPlugin.cs
public void Execute(IServiceProvider serviceProvider)
{
    var telemetry = new Dictionary<string, object>
    {
        ["PluginName"] = "ValidationPlugin",
        ["EntityName"] = context.PrimaryEntityName,
        ["MessageName"] = context.MessageName
    };

    var stopwatch = Stopwatch.StartNew();

    try
    {
        // Plugin logic here
        telemetry["Success"] = true;
    }
    catch (Exception ex)
    {
        telemetry["Success"] = false;
        telemetry["Error"] = ex.Message;
        throw;
    }
    finally
    {
        stopwatch.Stop();
        telemetry["DurationMs"] = stopwatch.ElapsedMilliseconds;

        // Log telemetry (implementation depends on available logging in plugins)
    }
}
```

### Health Check Configuration
```csharp
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy())
    .AddCheck("graph-api", new GraphApiHealthCheck())
    .AddCheck("keyvault", new KeyVaultHealthCheck())
    .AddCheck("redis", new RedisHealthCheck()); // When implemented
```

## Security Monitoring

### Authentication Events
- Failed login attempts
- Unusual access patterns
- Token expiration events
- Permission escalation attempts

### Data Access Monitoring
- Large file downloads
- Bulk data access patterns
- Cross-tenant access attempts
- Sensitive document access

### Plugin Security
- Plugin execution failures
- Unauthorized data access attempts
- Privilege escalation in plugin context

## Performance Baselines

### API Performance Targets
- Health checks: < 100ms p95
- Authentication: < 500ms p95
- Document operations: < 2s p95
- List operations: < 1s p95

### Plugin Performance Targets (ADR-002)
- ValidationPlugin: < 50ms p95
- ProjectionPlugin: < 50ms p95
- Total plugin execution: < 100ms p95

### Availability Targets
- API Service: 99.9% uptime
- Plugin execution: 99.95% success rate
- End-to-end workflows: 99.5% success rate

## Incident Response

### Severity Levels
- **P0 (Critical)**: Complete service outage, data corruption
- **P1 (High)**: Significant functionality impaired, security breach
- **P2 (Medium)**: Partial functionality impaired, performance degradation
- **P3 (Low)**: Minor issues, cosmetic problems

### Escalation Matrix
- P0: Immediate notification to on-call engineer and management
- P1: Notification within 15 minutes
- P2: Notification within 1 hour
- P3: Next business day notification

### Runbooks
- API service restart procedures
- Plugin deployment rollback
- Database connectivity issues
- External service outage response

## Continuous Improvement

### Weekly Reviews
- Performance trend analysis
- Error pattern identification
- Capacity planning updates
- Alert tuning

### Monthly Reports
- SLA compliance metrics
- Performance baseline updates
- Security incident summary
- Operational efficiency metrics

### Quarterly Assessments
- Monitoring strategy effectiveness
- Tool evaluation and updates
- Process improvements
- Training needs assessment