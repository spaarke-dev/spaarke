using System;
using System.IO;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace Spaarke.Plugins.Registration
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: dotnet run <connection-string>");
                return;
            }

            string connectionString = args[0];
            string assemblyPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "..", "Spaarke.Plugins", "bin", "Release", "net48", "Spaarke.Plugins.dll");
            assemblyPath = Path.GetFullPath(assemblyPath);

            if (!File.Exists(assemblyPath))
            {
                Console.WriteLine($"ERROR: Assembly not found at {assemblyPath}");
                return;
            }

            try
            {
                Console.WriteLine("Connecting to Dataverse...");
                using (var serviceClient = new ServiceClient(connectionString))
                {
                    if (!serviceClient.IsReady)
                    {
                        Console.WriteLine($"ERROR: Failed to connect: {serviceClient.LastError}");
                        return;
                    }

                    Console.WriteLine($"Connected to: {serviceClient.ConnectedOrgFriendlyName}");

                    // Load assembly
                    byte[] assemblyBytes = File.ReadAllBytes(assemblyPath);
                    var assembly = System.Reflection.Assembly.Load(assemblyBytes);
                    var assemblyName = assembly.GetName();

                    Console.WriteLine($"Assembly: {assemblyName.Name} v{assemblyName.Version}");

                    // Register or update assembly
                    Guid assemblyId = RegisterAssembly(serviceClient, assemblyName.Name, assemblyBytes, assemblyName.Version.ToString());
                    Console.WriteLine($"Assembly ID: {assemblyId}");

                    // Register plugin type
                    Guid pluginTypeId = RegisterPluginType(serviceClient, assemblyId);
                    Console.WriteLine($"Plugin Type ID: {pluginTypeId}");

                    // Get entity type code
                    int entityTypeCode = GetEntityTypeCode(serviceClient, "sprk_document");
                    Console.WriteLine($"Entity type code: {entityTypeCode}");

                    // Register steps
                    RegisterStep(serviceClient, pluginTypeId, entityTypeCode, "Create", 40, 1, null, null);
                    RegisterStep(serviceClient, pluginTypeId, entityTypeCode, "Update", 40, 1, "PreImage", "PostImage");
                    RegisterStep(serviceClient, pluginTypeId, entityTypeCode, "Delete", 40, 1, "PreImage", null);

                    Console.WriteLine("\nPlugin registration completed successfully!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        static Guid RegisterAssembly(IOrganizationService service, string name, byte[] content, string version)
        {
            Console.WriteLine($"Checking for existing assembly: {name}");
            var query = new QueryExpression("pluginassembly")
            {
                ColumnSet = new ColumnSet(true)
            };
            query.Criteria.AddCondition("name", ConditionOperator.Equal, name);

            var results = service.RetrieveMultiple(query);

            if (results.Entities.Count > 0)
            {
                Console.WriteLine("Updating existing assembly...");
                var existing = results.Entities[0];
                existing["content"] = Convert.ToBase64String(content);
                existing["version"] = version;
                service.Update(existing);
                return existing.Id;
            }
            else
            {
                Console.WriteLine("Creating new assembly...");
                var assembly = new Entity("pluginassembly");
                assembly["name"] = name;
                assembly["content"] = Convert.ToBase64String(content);
                assembly["isolationmode"] = new OptionSetValue(2); // Sandbox
                assembly["sourcetype"] = new OptionSetValue(0); // Database
                assembly["version"] = version;
                return service.Create(assembly);
            }
        }

        static Guid RegisterPluginType(IOrganizationService service, Guid assemblyId)
        {
            Console.WriteLine("Checking for existing plugin type...");
            var query = new QueryExpression("plugintype")
            {
                ColumnSet = new ColumnSet(true)
            };
            query.Criteria.AddCondition("pluginassemblyid", ConditionOperator.Equal, assemblyId);
            query.Criteria.AddCondition("typename", ConditionOperator.Equal, "Spaarke.Plugins.DocumentEventPlugin");

            var results = service.RetrieveMultiple(query);

            if (results.Entities.Count > 0)
            {
                Console.WriteLine("Plugin type already exists");
                return results.Entities[0].Id;
            }
            else
            {
                Console.WriteLine("Creating plugin type...");
                var pluginType = new Entity("plugintype");
                pluginType["pluginassemblyid"] = new EntityReference("pluginassembly", assemblyId);
                pluginType["typename"] = "Spaarke.Plugins.DocumentEventPlugin";
                pluginType["name"] = "DocumentEventPlugin";
                pluginType["friendlyname"] = "Document Event Plugin";
                return service.Create(pluginType);
            }
        }

        static int GetEntityTypeCode(IOrganizationService service, string logicalName)
        {
            var request = new RetrieveEntityRequest
            {
                LogicalName = logicalName,
                EntityFilters = EntityFilters.Entity
            };
            var response = (RetrieveEntityResponse)service.Execute(request);
            return response.EntityMetadata.ObjectTypeCode.Value;
        }

        static void RegisterStep(IOrganizationService service, Guid pluginTypeId, int entityTypeCode,
            string messageName, int stage, int mode, string preImageName, string postImageName)
        {
            Console.WriteLine($"Registering step: {messageName}");

            // Check if step already exists
            var query = new QueryExpression("sdkmessageprocessingstep")
            {
                ColumnSet = new ColumnSet(true)
            };
            query.Criteria.AddCondition("plugintypeid", ConditionOperator.Equal, pluginTypeId);

            var messageFilter = query.AddLink("sdkmessagefilter", "sdkmessagefilterid", "sdkmessagefilterid");
            messageFilter.LinkCriteria.AddCondition("primaryobjecttypecode", ConditionOperator.Equal, entityTypeCode);

            var message = query.AddLink("sdkmessage", "sdkmessageid", "sdkmessageid");
            message.LinkCriteria.AddCondition("name", ConditionOperator.Equal, messageName);

            var existingSteps = service.RetrieveMultiple(query);

            if (existingSteps.Entities.Count > 0)
            {
                Console.WriteLine($"  Step already exists for {messageName}");
                return;
            }

            // Get message ID
            var messageQuery = new QueryExpression("sdkmessage")
            {
                ColumnSet = new ColumnSet("sdkmessageid")
            };
            messageQuery.Criteria.AddCondition("name", ConditionOperator.Equal, messageName);
            var messages = service.RetrieveMultiple(messageQuery);
            var messageId = messages.Entities[0].Id;

            // Get message filter ID
            var filterQuery = new QueryExpression("sdkmessagefilter")
            {
                ColumnSet = new ColumnSet("sdkmessagefilterid")
            };
            filterQuery.Criteria.AddCondition("sdkmessageid", ConditionOperator.Equal, messageId);
            filterQuery.Criteria.AddCondition("primaryobjecttypecode", ConditionOperator.Equal, entityTypeCode);
            var filters = service.RetrieveMultiple(filterQuery);
            var filterId = filters.Entities[0].Id;

            // Create step
            var step = new Entity("sdkmessageprocessingstep");
            step["name"] = $"Spaarke.Plugins.DocumentEventPlugin: {messageName} of sprk_document";
            step["plugintypeid"] = new EntityReference("plugintype", pluginTypeId);
            step["sdkmessageid"] = new EntityReference("sdkmessage", messageId);
            step["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", filterId);
            step["stage"] = new OptionSetValue(stage);
            step["mode"] = new OptionSetValue(mode);
            step["rank"] = 1;
            step["asyncautodelete"] = true;

            var stepId = service.Create(step);
            Console.WriteLine($"  Step created: {stepId}");

            // Register images
            string imageAttributes = "sprk_documentname,sprk_containerid,sprk_documentdescription,sprk_hasfile,sprk_filename,sprk_filesize,sprk_mimetype,sprk_graphitemid,sprk_graphdriveid,statuscode,statecode";

            if (!string.IsNullOrEmpty(preImageName))
            {
                Console.WriteLine("  Registering PreImage...");
                var preImage = new Entity("sdkmessageprocessingstepimage");
                preImage["sdkmessageprocessingstepid"] = new EntityReference("sdkmessageprocessingstep", stepId);
                preImage["imagetype"] = new OptionSetValue(0); // PreImage
                preImage["name"] = preImageName;
                preImage["entityalias"] = preImageName;
                preImage["attributes"] = imageAttributes;
                service.Create(preImage);
            }

            if (!string.IsNullOrEmpty(postImageName))
            {
                Console.WriteLine("  Registering PostImage...");
                var postImage = new Entity("sdkmessageprocessingstepimage");
                postImage["sdkmessageprocessingstepid"] = new EntityReference("sdkmessageprocessingstep", stepId);
                postImage["imagetype"] = new OptionSetValue(1); // PostImage
                postImage["name"] = postImageName;
                postImage["entityalias"] = postImageName;
                postImage["attributes"] = imageAttributes;
                service.Create(postImage);
            }
        }
    }
}