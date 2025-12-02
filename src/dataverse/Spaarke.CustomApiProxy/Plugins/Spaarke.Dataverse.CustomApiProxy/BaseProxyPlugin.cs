using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json;

namespace Spaarke.Dataverse.CustomApiProxy
{
    /// <summary>
    /// Base plugin class providing common functionality for all Custom API Proxy operations.
    /// Handles authentication, configuration retrieval, audit logging, and error handling.
    /// </summary>
    public abstract class BaseProxyPlugin : IPlugin
    {
        protected ITracingService TracingService { get; private set; }
        protected IOrganizationService OrganizationService { get; private set; }
        protected IPluginExecutionContext ExecutionContext { get; private set; }

        private readonly string _pluginName;

        protected BaseProxyPlugin(string pluginName)
        {
            if (string.IsNullOrWhiteSpace(pluginName))
                throw new ArgumentNullException(nameof(pluginName));

            _pluginName = pluginName;
        }

        public void Execute(IServiceProvider serviceProvider)
        {
            // Get services from service provider
            TracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            ExecutionContext = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            OrganizationService = serviceFactory.CreateOrganizationService(ExecutionContext.UserId);

            TracingService.Trace($"{_pluginName}: Starting execution");

            string correlationId = null;
            var startTime = DateTime.UtcNow;

            try
            {
                // Validate request
                ValidateRequest();

                // Log request
                correlationId = LogRequest();

                // Execute derived class logic
                ExecuteProxy(serviceProvider, correlationId);

                // Log successful response
                var duration = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
                LogResponse(correlationId, true, null, duration);

                TracingService.Trace($"{_pluginName}: Completed successfully (Duration: {duration}ms)");
            }
            catch (Exception ex)
            {
                var duration = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
                TracingService.Trace($"{_pluginName}: Error - {ex.Message}");

                if (!string.IsNullOrEmpty(correlationId))
                {
                    LogResponse(correlationId, false, ex, duration);
                }

                throw new InvalidPluginExecutionException($"{_pluginName} failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Derived classes implement proxy logic here.
        /// </summary>
        protected abstract void ExecuteProxy(IServiceProvider serviceProvider, string correlationId);

        /// <summary>
        /// Validate request parameters. Can be overridden by derived classes.
        /// </summary>
        protected virtual void ValidateRequest()
        {
            if (ExecutionContext == null)
                throw new InvalidPluginExecutionException("Execution context is null");

            if (OrganizationService == null)
                throw new InvalidPluginExecutionException("Organization service is null");
        }

        /// <summary>
        /// Retrieve external service configuration from Dataverse.
        /// </summary>
        protected ExternalServiceConfig GetServiceConfig(string serviceName)
        {
            TracingService.Trace($"Retrieving config for service: {serviceName}");

            var query = new QueryExpression("sprk_externalserviceconfig")
            {
                ColumnSet = new ColumnSet(true)
            };
            query.Criteria.AddCondition("sprk_name", ConditionOperator.Equal, serviceName);
            query.Criteria.AddCondition("sprk_isenabled", ConditionOperator.Equal, true);

            var results = OrganizationService.RetrieveMultiple(query);

            if (results.Entities.Count == 0)
                throw new InvalidPluginExecutionException($"External service config not found or disabled: {serviceName}");

            var entity = results.Entities[0];

            var config = new ExternalServiceConfig
            {
                Name = entity.GetAttributeValue<string>("sprk_name"),
                BaseUrl = entity.GetAttributeValue<string>("sprk_baseurl"),
                AuthType = entity.GetAttributeValue<OptionSetValue>("sprk_authtype")?.Value ?? 0,
                TenantId = entity.GetAttributeValue<string>("sprk_tenantid"),
                ClientId = entity.GetAttributeValue<string>("sprk_clientid"),
                ClientSecret = entity.GetAttributeValue<string>("sprk_clientsecret"),
                Scope = entity.GetAttributeValue<string>("sprk_scope"),
                ApiKey = entity.GetAttributeValue<string>("sprk_apikey"),
                ApiKeyHeader = entity.GetAttributeValue<string>("sprk_apikeyheader"),
                Timeout = entity.GetAttributeValue<int>("sprk_timeout"),
                RetryCount = entity.GetAttributeValue<int>("sprk_retrycount"),
                RetryDelay = entity.GetAttributeValue<int>("sprk_retrydelay")
            };

            TracingService.Trace($"Config loaded: {config.BaseUrl}, AuthType: {config.AuthType}");

            return config;
        }

        /// <summary>
        /// Create HttpClient with authentication configured based on service config.
        /// </summary>
        protected HttpClient CreateAuthenticatedHttpClient(ExternalServiceConfig config)
        {
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(config.BaseUrl),
                Timeout = TimeSpan.FromSeconds(config.Timeout > 0 ? config.Timeout : 300)
            };

            // Get access token based on auth type
            string accessToken = null;

            switch (config.AuthType)
            {
                case 1: // ClientCredentials
                    accessToken = GetClientCredentialsToken(config);
                    break;
                case 2: // ManagedIdentity
                    accessToken = GetManagedIdentityToken(config);
                    break;
                case 3: // ApiKey
                    httpClient.DefaultRequestHeaders.Add(config.ApiKeyHeader ?? "X-API-Key", config.ApiKey);
                    TracingService.Trace("API Key authentication configured");
                    return httpClient;
                default:
                    // No authentication
                    TracingService.Trace("No authentication configured");
                    return httpClient;
            }

            if (!string.IsNullOrEmpty(accessToken))
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                TracingService.Trace("Bearer token authentication configured");
            }

            return httpClient;
        }

        private string GetClientCredentialsToken(ExternalServiceConfig config)
        {
            TracingService.Trace("Acquiring token using client credentials flow (OAuth2)");

            try
            {
                var token = SimpleAuthHelper.GetClientCredentialsToken(
                    config.TenantId,
                    config.ClientId,
                    config.ClientSecret,
                    config.Scope
                );

                TracingService.Trace("Token acquired successfully");
                return token;
            }
            catch (Exception ex)
            {
                TracingService.Trace($"Token acquisition failed: {ex.Message}");
                throw new InvalidPluginExecutionException($"Failed to acquire access token: {ex.Message}", ex);
            }
        }

        private string GetManagedIdentityToken(ExternalServiceConfig config)
        {
            TracingService.Trace("Managed Identity authentication not supported in this version");
            throw new InvalidPluginExecutionException(
                "Managed Identity authentication is not supported. Please use Client Credentials (AuthType=1) instead.");
        }

        private string LogRequest()
        {
            var correlationId = Guid.NewGuid().ToString();

            try
            {
                var auditLog = new Entity("sprk_proxyauditlog");
                auditLog["sprk_operation"] = _pluginName;
                auditLog["sprk_correlationid"] = correlationId;
                auditLog["sprk_executiontime"] = DateTime.UtcNow;
                auditLog["sprk_userid"] = new EntityReference("systemuser", ExecutionContext.UserId);

                // Serialize input parameters (redact sensitive data)
                var sanitizedParams = RedactSensitiveData(ExecutionContext.InputParameters);
                auditLog["sprk_requestpayload"] = JsonConvert.SerializeObject(sanitizedParams);

                OrganizationService.Create(auditLog);

                TracingService.Trace($"Request logged with correlation ID: {correlationId}");
            }
            catch (Exception ex)
            {
                TracingService.Trace($"Failed to log request: {ex.Message}");
                // Don't fail operation if logging fails
            }

            return correlationId;
        }

        private void LogResponse(string correlationId, bool success, Exception error, int duration)
        {
            try
            {
                var query = new QueryExpression("sprk_proxyauditlog");
                query.ColumnSet = new ColumnSet(true);
                query.Criteria.AddCondition("sprk_correlationid", ConditionOperator.Equal, correlationId);

                var results = OrganizationService.RetrieveMultiple(query);

                if (results.Entities.Count > 0)
                {
                    var auditLog = results.Entities[0];
                    auditLog["sprk_success"] = success;
                    auditLog["sprk_duration"] = duration;

                    if (success && ExecutionContext.OutputParameters != null)
                    {
                        var sanitizedOutput = RedactSensitiveData(ExecutionContext.OutputParameters);
                        auditLog["sprk_responsepayload"] = JsonConvert.SerializeObject(sanitizedOutput);
                        auditLog["sprk_statuscode"] = ExecutionContext.OutputParameters.Contains("StatusCode")
                            ? ExecutionContext.OutputParameters["StatusCode"]
                            : 200;
                    }

                    if (error != null)
                    {
                        auditLog["sprk_errormessage"] = error.Message;
                        auditLog["sprk_statuscode"] = 500;
                    }

                    OrganizationService.Update(auditLog);

                    TracingService.Trace("Response logged successfully");
                }
            }
            catch (Exception ex)
            {
                TracingService.Trace($"Failed to log response: {ex.Message}");
                // Don't fail operation if logging fails
            }
        }

        /// <summary>
        /// Redact sensitive data from parameters before logging.
        /// </summary>
        private ParameterCollection RedactSensitiveData(ParameterCollection parameters)
        {
            var sanitized = new ParameterCollection();

            foreach (var key in parameters.Keys)
            {
                var keyLower = key.ToLower();

                // Redact sensitive fields
                if (keyLower.Contains("secret") || keyLower.Contains("password") ||
                    keyLower.Contains("token") || keyLower.Contains("filecontent"))
                {
                    sanitized[key] = "[REDACTED]";
                }
                else
                {
                    sanitized[key] = parameters[key];
                }
            }

            return sanitized;
        }

        /// <summary>
        /// Execute action with retry logic and exponential backoff.
        /// </summary>
        protected T ExecuteWithRetry<T>(Func<T> action, ExternalServiceConfig config)
        {
            int retryCount = config.RetryCount > 0 ? config.RetryCount : 3;
            int retryDelay = config.RetryDelay > 0 ? config.RetryDelay : 1000;

            for (int i = 0; i < retryCount; i++)
            {
                try
                {
                    return action();
                }
                catch (Exception ex)
                {
                    if (i == retryCount - 1)
                    {
                        // Last attempt failed
                        TracingService.Trace($"All {retryCount} retry attempts failed");
                        throw;
                    }

                    // Check if error is retriable (transient error)
                    if (!IsTransientError(ex))
                    {
                        TracingService.Trace($"Non-transient error, not retrying: {ex.Message}");
                        throw;
                    }

                    TracingService.Trace($"Retry {i + 1}/{retryCount} after error: {ex.Message}");

                    // Exponential backoff
                    var delay = retryDelay * (i + 1);
                    System.Threading.Thread.Sleep(delay);
                }
            }

            throw new InvalidPluginExecutionException("Max retries exceeded");
        }

        /// <summary>
        /// Determine if an error is transient and should be retried.
        /// </summary>
        private bool IsTransientError(Exception ex)
        {
            // Check for HTTP errors
            if (ex is HttpRequestException)
            {
                return true; // Network errors are generally transient
            }

            // Check for timeout
            if (ex.Message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            // Check for specific HTTP status codes (if exception message contains them)
            var message = ex.Message.ToLower();
            if (message.Contains("500") || message.Contains("502") ||
                message.Contains("503") || message.Contains("504"))
            {
                return true; // Server errors are potentially transient
            }

            return false;
        }
    }

    /// <summary>
    /// External service configuration model.
    /// </summary>
    public class ExternalServiceConfig
    {
        public string Name { get; set; }
        public string BaseUrl { get; set; }
        public int AuthType { get; set; } // 0=None, 1=ClientCredentials, 2=ManagedIdentity, 3=ApiKey
        public string TenantId { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string Scope { get; set; }
        public string ApiKey { get; set; }
        public string ApiKeyHeader { get; set; }
        public int Timeout { get; set; } // Seconds
        public int RetryCount { get; set; }
        public int RetryDelay { get; set; } // Milliseconds
    }
}
