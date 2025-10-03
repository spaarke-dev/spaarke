using System;
using System.Collections.Generic;

#nullable enable

namespace Spaarke.Plugins.Models
{
    /// <summary>
    /// Event message for Document entity operations.
    /// Contains all context needed for background processing.
    /// </summary>
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
        public Dictionary<string, object> EntityData { get; set; } = new Dictionary<string, object>();
        public Dictionary<string, object>? PreEntityData { get; set; } // For Update operations

        // Processing Instructions
        public int Priority { get; set; } = 1; // 1=Normal, 2=High, 3=Critical
        public TimeSpan ProcessingDelay { get; set; } = TimeSpan.Zero;
        public int MaxRetryAttempts { get; set; } = 3;

        // Metadata
        public string Source { get; set; } = "DocumentEventPlugin";
        public string Version { get; set; } = "1.0";
    }
}
