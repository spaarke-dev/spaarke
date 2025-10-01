# Task 2.1: Thin Plugin Implementation

**PHASE:** Service Bus Integration (Days 6-10)
**STATUS:** ✅ COMPLETED
**DEPENDENCIES:** Task 1.1 (Entity Creation), Task 1.3 (Document CRUD API)
**ESTIMATED TIME:** 6-8 hours
**ACTUAL TIME:** 8 hours
**PRIORITY:** HIGH - Critical for async processing architecture
**COMPLETED:** 2025-09-30

---

## 📋 TASK OVERVIEW

### **Objective**
Create a thin Dataverse plugin that follows ADR-002 principles for minimal plugin logic. The plugin captures Document entity events and queues them to Service Bus for background processing, maintaining the async-first architecture.

### **Business Context**
- Need to capture Document entity events (Create, Update, Delete) in Dataverse
- Plugin should ONLY queue events to Service Bus, no business logic
- Must be ultra-fast (target < 200ms execution) and reliable
- Should not fail main Dataverse operation if queuing fails
- Follows existing plugin patterns in the solution

### **Architecture Impact**
This task establishes the event-driven foundation that enables:
- Async processing of complex business logic outside Dataverse
- Reliable event capture with retry mechanisms
- Scalable background job processing
- Separation of concerns between data operations and business logic

---

## 🔍 PRIOR TASK REVIEW AND VALIDATION

### **Task 1.1 and 1.3 Results Review**
Before starting this task, verify the following from previous tasks:

#### **Entity Validation Checklist**
- [ ] **sprk_document entity exists** and is accessible
- [ ] **Entity events are triggerable** (Create, Update, Delete operations work)
- [ ] **Field structure confirmed** for event payload capture
- [ ] **Primary key field accessible** for event identification

#### **API Integration Confirmation**
- [ ] **Document CRUD endpoints operational** and tested
- [ ] **DataverseService integration confirmed** with real entities
- [ ] **Background job integration points identified** for event processing
- [ ] **Service Bus configuration available** for plugin connection

#### **Performance Baseline**
- [ ] **Document operations response times measured** for baseline
- [ ] **Expected event volume estimated** for load planning
- [ ] **Plugin execution time targets established** (< 200ms)

### **Gaps and Corrections**
If any issues found in prior tasks:

1. **Entity Operation Failures**: Ensure all CRUD operations work before implementing plugin
2. **Service Bus Unavailable**: Confirm Service Bus infrastructure is ready
3. **Authentication Issues**: Verify plugin can authenticate to Service Bus
4. **Performance Problems**: Address any slow entity operations that would impact plugin

---

## 🎯 AI AGENT INSTRUCTIONS

### **CONTEXT FOR AI AGENT**
You are implementing a critical component of the async processing architecture. The plugin must be extremely lightweight and reliable, as it executes within Dataverse transaction context and any failures will impact user operations.

### **ARCHITECTURAL PRINCIPLES**

#### **Thin Plugin Principles (ADR-002)**
- **Minimal Processing**: Only event capture and queuing, no business logic
- **Fast Execution**: Target < 200ms execution time (p95)
- **Fault Tolerant**: Plugin failures must not break Dataverse operations
- **Async Focus**: Queue events for background processing
- **Stateless**: No plugin state management or complex dependencies

#### **Event-Driven Design**
- **Event Capture**: Extract minimal required data from plugin context
- **Message Queuing**: Send structured messages to Service Bus
- **Correlation Tracking**: Include correlation IDs for distributed tracing
- **Error Handling**: Graceful degradation when Service Bus unavailable

### **TECHNICAL REQUIREMENTS**

#### **Plugin Registration Configuration**

| Property | Value | Description |
|----------|-------|-------------|
| **Entity** | sprk_document | Target entity for event capture |
| **Events** | Create, Update, Delete | Operations to monitor |
| **Stage** | Post-operation | After successful Dataverse operation |
| **Mode** | Asynchronous | Non-blocking execution |
| **Deployment** | Database | Server-side plugin deployment |

#### **Event Message Structure**

Create a comprehensive event message that includes all necessary context:

```csharp
public class DocumentEvent
{
    // Event Identification
    public string EventId { get; set; } = Guid.NewGuid().ToString();
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Operation Context
    public string Operation { get; set; } = string.Empty; // Create, Update, Delete
    public string DocumentId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;

    // Entity Data
    public Dictionary<string, object> EntityData { get; set; } = new();
    public Dictionary<string, object>? PreEntityData { get; set; } // For Update operations

    // Processing Instructions
    public int Priority { get; set; } = 1; // 1=Normal, 2=High, 3=Critical
    public TimeSpan ProcessingDelay { get; set; } = TimeSpan.Zero;
    public int MaxRetryAttempts { get; set; } = 3;

    // Metadata
    public string Source { get; set; } = "DocumentEventPlugin";
    public string Version { get; set; } = "1.0";
}
```

#### **Plugin Implementation Pattern**

```csharp
public class DocumentEventPlugin : IPlugin
{
    private readonly string _serviceBusConnectionString;
    private readonly string _queueName;
    private readonly ITracingService _tracingService;

    public DocumentEventPlugin(string unsecure, string secure)
    {
        // Initialize from secure configuration
        var config = JsonSerializer.Deserialize<PluginConfiguration>(secure);
        _serviceBusConnectionString = config.ServiceBusConnectionString;
        _queueName = config.QueueName;
    }

    public void Execute(IServiceProvider serviceProvider)
    {
        var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

        try
        {
            // Fast validation
            if (!ShouldProcessEvent(context))
                return;

            // Extract event data
            var documentEvent = CreateDocumentEvent(context);

            // Queue to Service Bus
            QueueEventAsync(documentEvent, tracingService);

            tracingService.Trace($"Document event {documentEvent.EventId} queued successfully");
        }
        catch (Exception ex)
        {
            // Log error but don't fail the main operation
            tracingService.Trace($"Plugin error (non-blocking): {ex.Message}");

            // Optional: Queue to dead letter for investigation
            TryQueueErrorEvent(context, ex, tracingService);
        }
    }

    private bool ShouldProcessEvent(IPluginExecutionContext context)
    {
        // Quick validation to avoid unnecessary processing
        return context.PrimaryEntityName == "sprk_document" &&
               context.Stage == 40 && // Post-operation
               (context.MessageName == "Create" ||
                context.MessageName == "Update" ||
                context.MessageName == "Delete");
    }

    private DocumentEvent CreateDocumentEvent(IPluginExecutionContext context)
    {
        var documentEvent = new DocumentEvent
        {
            Operation = context.MessageName,
            DocumentId = context.PrimaryEntityId.ToString(),
            UserId = context.UserId.ToString(),
            OrganizationId = context.OrganizationId.ToString(),
            CorrelationId = context.CorrelationId?.ToString() ?? Guid.NewGuid().ToString()
        };

        // Extract entity data
        if (context.PostEntityImages.Contains("PostImage"))
        {
            var postImage = context.PostEntityImages["PostImage"];
            documentEvent.EntityData = ExtractEntityData(postImage);
        }

        // Extract pre-entity data for updates
        if (context.MessageName == "Update" && context.PreEntityImages.Contains("PreImage"))
        {
            var preImage = context.PreEntityImages["PreImage"];
            documentEvent.PreEntityData = ExtractEntityData(preImage);
        }

        return documentEvent;
    }

    private Dictionary<string, object> ExtractEntityData(Entity entity)
    {
        var data = new Dictionary<string, object>();

        // Extract only relevant fields for performance
        var relevantFields = new[]
        {
            "sprk_documentname",
            "sprk_containerid",
            "sprk_documentdescription",
            "sprk_hasfile",
            "sprk_filename",
            "sprk_filesize",
            "sprk_mimetype",
            "statuscode",
            "statecode"
        };

        foreach (var field in relevantFields)
        {
            if (entity.Contains(field))
            {
                data[field] = entity[field];
            }
        }

        return data;
    }

    private void QueueEventAsync(DocumentEvent documentEvent, ITracingService tracingService)
    {
        try
        {
            using var serviceBusClient = new ServiceBusClient(_serviceBusConnectionString);
            using var sender = serviceBusClient.CreateSender(_queueName);

            var messageBody = JsonSerializer.Serialize(documentEvent);
            var message = new ServiceBusMessage(messageBody)
            {
                MessageId = documentEvent.EventId,
                CorrelationId = documentEvent.CorrelationId,
                Subject = $"Document{documentEvent.Operation}",
                TimeToLive = TimeSpan.FromHours(24)
            };

            // Add custom properties for routing and filtering
            message.ApplicationProperties["DocumentId"] = documentEvent.DocumentId;
            message.ApplicationProperties["Operation"] = documentEvent.Operation;
            message.ApplicationProperties["Priority"] = documentEvent.Priority;

            // Synchronous send (required in plugin context)
            sender.SendMessageAsync(message).GetAwaiter().GetResult();

            tracingService.Trace($"Event queued: {documentEvent.EventId}");
        }
        catch (Exception ex)
        {
            tracingService.Trace($"Service Bus error: {ex.Message}");
            // Don't rethrow - allows main operation to continue
        }
    }

    private void TryQueueErrorEvent(IPluginExecutionContext context, Exception exception, ITracingService tracingService)
    {
        try
        {
            // Create error event for investigation
            var errorEvent = new
            {
                EventId = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow,
                Source = "DocumentEventPlugin",
                Operation = context.MessageName,
                DocumentId = context.PrimaryEntityId.ToString(),
                Error = exception.Message,
                StackTrace = exception.StackTrace
            };

            // Queue to error handling system (optional)
            tracingService.Trace($"Plugin error logged: {errorEvent.EventId}");
        }
        catch
        {
            // Ignore errors in error handling to prevent cascading failures
        }
    }
}
```

#### **Plugin Configuration Model**

```csharp
public class PluginConfiguration
{
    public string ServiceBusConnectionString { get; set; } = string.Empty;
    public string QueueName { get; set; } = "document-events";
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool EnableErrorLogging { get; set; } = true;
}
```

### **PERFORMANCE OPTIMIZATION**

#### **Fast Path Implementation**
```csharp
// Optimize for common scenarios
public void Execute(IServiceProvider serviceProvider)
{
    // Get services once
    var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
    var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

    // Fast exit for irrelevant events
    if (context.PrimaryEntityName != "sprk_document" || context.Stage != 40)
        return;

    // Pre-allocate objects to reduce GC pressure
    var eventData = new Dictionary<string, object>(8);

    // Continue with minimal allocations...
}
```

#### **Connection Pooling**
```csharp
// Use static connection pool for Service Bus clients
private static readonly ConcurrentDictionary<string, ServiceBusClient> _clientPool = new();

private ServiceBusClient GetServiceBusClient(string connectionString)
{
    return _clientPool.GetOrAdd(connectionString, cs => new ServiceBusClient(cs));
}
```

### **ERROR HANDLING AND RESILIENCE**

#### **Circuit Breaker Pattern**
```csharp
private static readonly object _circuitBreakerLock = new object();
private static DateTime _lastFailureTime = DateTime.MinValue;
private static int _consecutiveFailures = 0;
private static bool _circuitOpen = false;

private bool IsCircuitOpen()
{
    lock (_circuitBreakerLock)
    {
        if (_circuitOpen && DateTime.UtcNow - _lastFailureTime > TimeSpan.FromMinutes(5))
        {
            _circuitOpen = false;
            _consecutiveFailures = 0;
        }
        return _circuitOpen;
    }
}

private void RecordFailure()
{
    lock (_circuitBreakerLock)
    {
        _consecutiveFailures++;
        _lastFailureTime = DateTime.UtcNow;

        if (_consecutiveFailures >= 5)
        {
            _circuitOpen = true;
        }
    }
}
```

#### **Graceful Degradation**
```csharp
private void QueueEventWithFallback(DocumentEvent documentEvent, ITracingService tracingService)
{
    if (IsCircuitOpen())
    {
        tracingService.Trace("Service Bus circuit breaker open - skipping event");
        return;
    }

    try
    {
        QueueEventAsync(documentEvent, tracingService);
        ResetFailureCount();
    }
    catch (Exception ex)
    {
        RecordFailure();

        // Optional: Store event locally for retry
        StoreEventForRetry(documentEvent, tracingService);

        tracingService.Trace($"Event queuing failed: {ex.Message}");
    }
}
```

### **MONITORING AND DIAGNOSTICS**

#### **Plugin Telemetry**
```csharp
public class PluginTelemetry
{
    private static readonly Counter<int> EventsProcessed =
        Meter.CreateCounter<int>("plugin.events.processed");

    private static readonly Counter<int> EventsQueued =
        Meter.CreateCounter<int>("plugin.events.queued");

    private static readonly Counter<int> EventsFailed =
        Meter.CreateCounter<int>("plugin.events.failed");

    private static readonly Histogram<double> ExecutionTime =
        Meter.CreateHistogram<double>("plugin.execution.duration");

    public static void RecordEventProcessed(string operation) =>
        EventsProcessed.Add(1, KeyValuePair.Create("operation", operation));

    public static void RecordEventQueued(string operation) =>
        EventsQueued.Add(1, KeyValuePair.Create("operation", operation));

    public static void RecordEventFailed(string operation, string error) =>
        EventsFailed.Add(1,
            KeyValuePair.Create("operation", operation),
            KeyValuePair.Create("error", error));

    public static void RecordExecutionTime(double milliseconds, string operation) =>
        ExecutionTime.Record(milliseconds, KeyValuePair.Create("operation", operation));
}
```

---

## ✅ VALIDATION STEPS

### **Development Environment Testing**

#### **1. Plugin Registration Validation**
```powershell
# Verify plugin assembly is registered correctly
pac plugin list --environment [ENV_URL]

# Check plugin step registration
pac plugin step list --environment [ENV_URL]

# Validate plugin configuration
pac plugin show --id [PLUGIN_ID] --environment [ENV_URL]
```

#### **2. Event Generation Testing**
```csharp
// Test document operations that should trigger plugin
var testDocument = new Entity("sprk_document");
testDocument["sprk_documentname"] = "Plugin Test Document";
testDocument["sprk_containerid"] = new EntityReference("sprk_container", containerId);

// Create operation should trigger plugin
var documentId = service.Create(testDocument);

// Update operation should trigger plugin
var updateDoc = new Entity("sprk_document", documentId);
updateDoc["sprk_documentname"] = "Updated Plugin Test Document";
service.Update(updateDoc);

// Delete operation should trigger plugin
service.Delete("sprk_document", documentId);
```

#### **3. Service Bus Message Validation**
```csharp
// Monitor Service Bus queue for messages
using var serviceBusClient = new ServiceBusClient(connectionString);
using var receiver = serviceBusClient.CreateReceiver(queueName);

var messages = await receiver.ReceiveMessagesAsync(maxMessages: 10);
foreach (var message in messages)
{
    var documentEvent = JsonSerializer.Deserialize<DocumentEvent>(message.Body);

    // Validate message structure
    Assert.NotNull(documentEvent.EventId);
    Assert.NotNull(documentEvent.DocumentId);
    Assert.Contains(documentEvent.Operation, new[] { "Create", "Update", "Delete" });

    await receiver.CompleteMessageAsync(message);
}
```

### **Performance Testing**

#### **1. Plugin Execution Time Testing**
```csharp
// Measure plugin execution time
var stopwatch = Stopwatch.StartNew();

// Perform document operation
var documentId = service.Create(testDocument);

stopwatch.Stop();

// Plugin should complete within transaction time
Assert.True(stopwatch.ElapsedMilliseconds < 200,
    $"Plugin execution took {stopwatch.ElapsedMilliseconds}ms");
```

#### **2. Load Testing**
```csharp
// Test concurrent document operations
var tasks = Enumerable.Range(0, 50).Select(async i =>
{
    var doc = new Entity("sprk_document");
    doc["sprk_documentname"] = $"Load Test Document {i}";
    doc["sprk_containerid"] = new EntityReference("sprk_container", containerId);

    return service.Create(doc);
});

await Task.WhenAll(tasks);

// Verify all events were queued
// Check Service Bus queue depth
```

#### **3. Circuit Breaker Testing**
```csharp
// Test behavior when Service Bus is unavailable
// Simulate Service Bus failures
// Verify plugin doesn't fail main operations
// Test recovery when Service Bus comes back online
```

### **Error Handling Testing**

#### **1. Service Bus Unavailable Testing**
```bash
# Stop Service Bus or block network access
# Perform document operations
# Verify main operations still succeed
# Verify appropriate error logging occurs
```

#### **2. Invalid Message Testing**
```csharp
// Test plugin with corrupted entity data
// Test plugin with missing required fields
// Verify plugin handles errors gracefully
// Confirm main Dataverse operation completes
```

#### **3. High Failure Rate Testing**
```csharp
// Simulate high Service Bus failure rate
// Verify circuit breaker activates
// Test recovery after failures decrease
// Confirm graceful degradation behavior
```

---

## 🔍 TROUBLESHOOTING GUIDE

### **Common Issues and Solutions**

#### **Issue: Plugin Not Executing**
**Symptoms**: Document operations complete but no events appear in Service Bus
**Diagnosis Steps**:
1. Check plugin registration and activation status
2. Verify plugin step configuration (stage, mode, entity)
3. Check plugin execution context and filters
4. Review Dataverse system job history for errors

**Solutions**:
```powershell
# Check plugin registration
pac plugin list --environment [ENV_URL]

# Verify plugin steps
pac plugin step list --environment [ENV_URL]

# Check system jobs for plugin errors
# Navigate to Settings > System Jobs in Dataverse
```

#### **Issue: Plugin Execution Timeouts**
**Symptoms**: Dataverse operations fail with timeout errors
**Diagnosis Steps**:
1. Check plugin execution time in system jobs
2. Analyze plugin code for performance bottlenecks
3. Review Service Bus connection and send times
4. Check for synchronous operations in async context

**Solutions**:
- Optimize plugin code for minimal processing
- Use asynchronous Service Bus operations correctly
- Implement circuit breaker for Service Bus failures
- Add performance monitoring and alerts

#### **Issue: Service Bus Connection Failures**
**Symptoms**: Plugin logs show Service Bus connection errors
**Diagnosis Steps**:
1. Verify Service Bus connection string and permissions
2. Check network connectivity from Dataverse to Service Bus
3. Review Service Bus namespace and queue configuration
4. Test connection from development environment

**Solutions**:
```csharp
// Add connection validation
private bool ValidateServiceBusConnection()
{
    try
    {
        using var client = new ServiceBusClient(_serviceBusConnectionString);
        return client.CanConnect();
    }
    catch
    {
        return false;
    }
}
```

#### **Issue: Message Serialization Errors**
**Symptoms**: Plugin executes but messages are malformed in Service Bus
**Diagnosis Steps**:
1. Check DocumentEvent serialization logic
2. Verify entity data extraction from plugin context
3. Review message format in Service Bus explorer
4. Test serialization with sample data

**Solutions**:
- Add JSON serialization error handling
- Validate entity data before serialization
- Include schema version in messages
- Add message format validation

#### **Issue: High Plugin Failure Rate**
**Symptoms**: Many plugin executions fail or timeout
**Diagnosis Steps**:
1. Review plugin execution statistics in system jobs
2. Analyze failure patterns and error messages
3. Check Service Bus performance and throttling
4. Monitor Dataverse performance impact

**Solutions**:
- Implement robust error handling and retry logic
- Add circuit breaker pattern for Service Bus
- Optimize plugin code for minimal allocations
- Consider asynchronous plugin execution

---

## 📚 KNOWLEDGE REFERENCES

### **Existing Patterns to Reference**
- `power-platform/plugins/Spaarke.Plugins/ProjectionPlugin.cs` - Plugin structure and patterns
- `src/api/Spe.Bff.Api/Services/Jobs/JobContract.cs` - Event message patterns
- Service Bus integration patterns in the solution
- Existing plugin registration and deployment procedures

### **Technical Documentation**
- Dataverse Plugin Development Guide
- Azure Service Bus .NET SDK documentation
- Plugin registration tool documentation
- Dataverse security and configuration guides

### **Configuration References**
- Service Bus connection string and queue configuration
- Plugin security configuration and permissions
- Dataverse environment settings and limits
- Monitoring and alerting configuration

---

## 🎯 SUCCESS CRITERIA

This task is complete when:

### **Functional Criteria**
- ✅ Plugin executes successfully for all document operations (Create, Update, Delete)
- ✅ Events are properly queued to Service Bus with correct message structure
- ✅ Plugin failures do not break main Dataverse operations
- ✅ All required event data is captured and transmitted
- ✅ Circuit breaker prevents cascading failures

### **Performance Criteria**
- ✅ Plugin execution time consistently < 200ms (p95)
- ✅ No noticeable impact on document operation response times
- ✅ Service Bus message queuing completes within timeout limits
- ✅ Plugin handles high-frequency document operations without failure

### **Reliability Criteria**
- ✅ Plugin operates correctly under load (50+ concurrent operations)
- ✅ Graceful degradation when Service Bus is unavailable
- ✅ Automatic recovery when Service Bus becomes available
- ✅ Comprehensive error logging for troubleshooting

### **Security Criteria**
- ✅ Plugin configuration stored securely
- ✅ Service Bus authentication and authorization working
- ✅ No sensitive data exposed in error messages
- ✅ Plugin operates with minimum required permissions

---

## 🔄 CONCLUSION AND NEXT STEP

### **Impact of Completion**
Completing this task establishes:
1. **Event-driven architecture foundation** for async processing
2. **Reliable event capture** from Dataverse operations
3. **Scalable message queuing** for background job processing
4. **Separation of concerns** between data operations and business logic
5. **Monitoring framework** for plugin performance and health

### **Quality Validation**
Before moving to the next task:
1. Execute comprehensive plugin testing across all document operations
2. Validate event message structure and content accuracy
3. Confirm performance targets are consistently met
4. Test failure scenarios and recovery mechanisms
5. Verify Service Bus integration works correctly

### **Integration Validation**
Ensure the plugin foundation is solid:
1. Document operations trigger events reliably
2. Event messages contain all required context
3. Service Bus queue receives properly formatted messages
4. Plugin monitoring provides actionable insights
5. Error handling prevents operational disruption

### **Immediate Next Action**
Upon successful completion of this task:

**🎯 PROCEED TO: [Task-2.2-Background-Service-Implementation.md](./Task-2.2-Background-Service-Implementation.md)**

The event capture foundation is now complete and ready for background processing. The background service will consume the events queued by this plugin and execute the complex business logic asynchronously.

### **Handoff Information**
Provide this information to the next task:
- Service Bus queue name and message format for consumption
- Event types and data structure for processing logic
- Error handling patterns and retry requirements
- Performance expectations for background processing
- Monitoring and alerting requirements established

---

**📋 TASK COMPLETION CHECKLIST**
- [ ] Plugin registered and activated successfully
- [ ] All document operations trigger plugin execution
- [ ] Events queued to Service Bus with correct format
- [ ] Performance targets met consistently
- [ ] Error handling prevents operation failures
- [ ] Circuit breaker implemented and tested
- [ ] Monitoring and logging operational
- [ ] Next task team briefed on event processing requirements