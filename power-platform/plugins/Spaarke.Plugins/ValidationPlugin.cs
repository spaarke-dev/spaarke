using System;
using Microsoft.Xrm.Sdk;

namespace Spaarke.Plugins
{
    /// <summary>
    /// Thin validation plugin following ADR-002.
    /// Performs only synchronous validation with no external I/O.
    /// Execution target: p95 < 50ms
    /// </summary>
    public class ValidationPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
                throw new ArgumentNullException(nameof(serviceProvider));

            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = serviceFactory.CreateOrganizationService(context.UserId);

            // Only process supported messages
            if (!IsSupportedMessage(context.MessageName))
                return;

            // Only process supported entities
            if (!IsSupportedEntity(context.PrimaryEntityName))
                return;

            switch (context.MessageName)
            {
                case "Create":
                case "Update":
                    ValidateEntity(context, service);
                    break;
            }
        }

        private static bool IsSupportedMessage(string messageName)
        {
            return messageName == "Create" || messageName == "Update";
        }

        private static bool IsSupportedEntity(string entityName)
        {
            return entityName == "spe_document" || entityName == "spe_container";
        }

        private static void ValidateEntity(IPluginExecutionContext context, IOrganizationService service)
        {
            if (!context.InputParameters.Contains("Target") ||
                !(context.InputParameters["Target"] is Entity target))
                return;

            switch (target.LogicalName)
            {
                case "spe_document":
                    ValidateDocument(target);
                    break;
                case "spe_container":
                    ValidateContainer(target);
                    break;
            }
        }

        private static void ValidateDocument(Entity document)
        {
            // Validate name is required and not empty
            if (document.Contains("spe_name"))
            {
                var name = document.GetAttributeValue<string>("spe_name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidPluginExecutionException("Document name is required and cannot be empty.");
                }
            }

            // Validate size is positive
            if (document.Contains("spe_size"))
            {
                var size = document.GetAttributeValue<int>("spe_size");
                if (size < 0)
                {
                    throw new InvalidPluginExecutionException("Document size must be positive.");
                }
            }

            // Validate mime type format
            if (document.Contains("spe_mimetype"))
            {
                var mimeType = document.GetAttributeValue<string>("spe_mimetype");
                if (!string.IsNullOrEmpty(mimeType) && !IsValidMimeType(mimeType))
                {
                    throw new InvalidPluginExecutionException("Invalid mime type format.");
                }
            }
        }

        private static void ValidateContainer(Entity container)
        {
            // Validate container name is required
            if (container.Contains("spe_name"))
            {
                var name = container.GetAttributeValue<string>("spe_name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidPluginExecutionException("Container name is required and cannot be empty.");
                }
            }

            // Validate document count is not negative
            if (container.Contains("spe_documentcount"))
            {
                var count = container.GetAttributeValue<int>("spe_documentcount");
                if (count < 0)
                {
                    throw new InvalidPluginExecutionException("Document count cannot be negative.");
                }
            }
        }

        private static bool IsValidMimeType(string mimeType)
        {
            // Simple validation for mime type format: type/subtype
            return mimeType.Contains("/") && !mimeType.StartsWith("/") && !mimeType.EndsWith("/");
        }
    }
}
