using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Xrm.Sdk;
using Spaarke.Plugins.Models;

namespace Spaarke.Plugins
{
    /// <summary>
    /// Thin plugin that captures Document entity events and queues them to Service Bus.
    /// Follows ADR-002 principles: minimal processing, fast execution, fault-tolerant.
    /// Target execution time: < 200ms
    /// </summary>
    public class DocumentEventPlugin : IPlugin
    {
        private readonly string _serviceBusConnectionString;
        private readonly string _queueName;
        private const int MaxExecutionTimeMs = 5000; // 5 second timeout for Service Bus

        /// <summary>
        /// Constructor - receives secure configuration from plugin registration
        /// </summary>
        public DocumentEventPlugin(string unsecure, string secure)
        {
            // Parse secure configuration
            // Expected format: {"ServiceBusConnectionString":"...", "QueueName":"..."}
            if (!string.IsNullOrEmpty(secure))
            {
                try
                {
                    var config = JsonSerializer.Deserialize<PluginConfiguration>(secure);
                    _serviceBusConnectionString = config?.ServiceBusConnectionString ?? string.Empty;
                    _queueName = config?.QueueName ?? "document-events";
                }
                catch
                {
                    // Use default queue name if config parsing fails
                    _queueName = "document-events";
                }
            }
            else
            {
                // Fallback to environment variable or default
                _queueName = "document-events";
            }
        }

        public void Execute(IServiceProvider serviceProvider)
        {
            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

            tracingService.Trace($"DocumentEventPlugin: Starting execution for {context.MessageName} on {context.PrimaryEntityName}");

            try
            {
                // Fast validation - skip if not a document event we care about
                if (!ShouldProcessEvent(context))
                {
                    tracingService.Trace("DocumentEventPlugin: Event filtered out, skipping");
                    return;
                }

                // Extract event data
                var documentEvent = CreateDocumentEvent(context);
                tracingService.Trace($"DocumentEventPlugin: Created event {documentEvent.EventId} for document {documentEvent.DocumentId}");

                // Queue to Service Bus
                QueueEvent(documentEvent, tracingService);

                tracingService.Trace($"DocumentEventPlugin: Successfully queued event {documentEvent.EventId}");
            }
            catch (Exception ex)
            {
                // Log error but don't fail the main Dataverse operation
                // This follows the fault-tolerant principle
                tracingService.Trace($"DocumentEventPlugin ERROR (non-blocking): {ex.Message}");
                tracingService.Trace($"DocumentEventPlugin Stack: {ex.StackTrace}");

                // Note: We intentionally don't throw here to avoid failing the user's operation
                // The event will be lost but the document operation succeeds
                // TODO: Consider dead-letter queue for lost events in production
            }
        }

        private bool ShouldProcessEvent(IPluginExecutionContext context)
        {
            // Only process sprk_document entity
            if (context.PrimaryEntityName != "sprk_document")
                return false;

            // Only process Post-operation stage (Stage 40)
            if (context.Stage != 40)
                return false;

            // Only process Create, Update, Delete operations
            var validOperations = new[] { "Create", "Update", "Delete" };
            if (!validOperations.Contains(context.MessageName))
                return false;

            return true;
        }

        private DocumentEvent CreateDocumentEvent(IPluginExecutionContext context)
        {
            var documentEvent = new DocumentEvent
            {
                EventId = Guid.NewGuid().ToString(),
                Operation = context.MessageName,
                DocumentId = context.PrimaryEntityId.ToString(),
                UserId = context.UserId.ToString(),
                OrganizationId = context.OrganizationId.ToString(),
                CorrelationId = context.CorrelationId.ToString(),
                Timestamp = DateTime.UtcNow
            };

            // Extract post-operation entity data (what the record looks like after the operation)
            if (context.PostEntityImages.Contains("PostImage"))
            {
                var postImage = (Entity)context.PostEntityImages["PostImage"];
                documentEvent.EntityData = ExtractEntityData(postImage);
            }
            else if (context.MessageName == "Create" && context.InputParameters.Contains("Target"))
            {
                // For Create, fallback to Target if PostImage not configured
                var target = (Entity)context.InputParameters["Target"];
                documentEvent.EntityData = ExtractEntityData(target);
            }

            // Extract pre-operation entity data for Update operations (for change tracking)
            if (context.MessageName == "Update" && context.PreEntityImages.Contains("PreImage"))
            {
                var preImage = (Entity)context.PreEntityImages["PreImage"];
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
                "sprk_graphitemid",
                "sprk_graphdriveid",
                "statuscode",
                "statecode"
            };

            foreach (var field in relevantFields)
            {
                if (entity.Contains(field) && entity[field] != null)
                {
                    var value = entity[field];

                    // Convert complex types to serializable formats
                    if (value is EntityReference entityRef)
                    {
                        data[field] = new Dictionary<string, string>
                        {
                            ["LogicalName"] = entityRef.LogicalName,
                            ["Id"] = entityRef.Id.ToString(),
                            ["Name"] = entityRef.Name ?? string.Empty
                        };
                    }
                    else if (value is OptionSetValue optionSet)
                    {
                        data[field] = optionSet.Value;
                    }
                    else if (value is Money money)
                    {
                        data[field] = money.Value;
                    }
                    else
                    {
                        // Primitive types (string, int, bool, DateTime, etc.)
                        data[field] = value;
                    }
                }
            }

            return data;
        }

        private void QueueEvent(DocumentEvent documentEvent, ITracingService tracingService)
        {
            if (string.IsNullOrEmpty(_serviceBusConnectionString))
            {
                tracingService.Trace("DocumentEventPlugin: No Service Bus connection string configured, event not queued");
                return;
            }

            try
            {
                // Serialize event
                var messageBody = JsonSerializer.Serialize(documentEvent);
                tracingService.Trace($"DocumentEventPlugin: Serialized event, size: {messageBody.Length} bytes");

                // Create Service Bus client and sender
                var client = new ServiceBusClient(_serviceBusConnectionString);
                var sender = client.CreateSender(_queueName);

                try
                {
                    // Create message
                    var message = new ServiceBusMessage(messageBody)
                    {
                        MessageId = documentEvent.EventId,
                        CorrelationId = documentEvent.CorrelationId,
                        Subject = $"{documentEvent.Operation}Document",
                        ContentType = "application/json"
                    };

                    // Add custom properties for routing/filtering
                    message.ApplicationProperties["Operation"] = documentEvent.Operation;
                    message.ApplicationProperties["DocumentId"] = documentEvent.DocumentId;
                    message.ApplicationProperties["Source"] = documentEvent.Source;

                    // Send message synchronously (plugin context requires sync)
                    sender.SendMessageAsync(message).GetAwaiter().GetResult();

                    tracingService.Trace($"DocumentEventPlugin: Message sent to queue '{_queueName}'");
                }
                finally
                {
                    // Dispose resources
                    if (sender != null)
                        sender.DisposeAsync().GetAwaiter().GetResult();
                    if (client != null)
                        client.DisposeAsync().GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace($"DocumentEventPlugin: Service Bus error: {ex.Message}");
                // Don't throw - let the operation succeed even if queuing fails
            }
        }

        /// <summary>
        /// Configuration class for plugin settings
        /// </summary>
        private class PluginConfiguration
        {
            public string ServiceBusConnectionString { get; set; } = string.Empty;
            public string QueueName { get; set; } = "document-events";
        }
    }
}