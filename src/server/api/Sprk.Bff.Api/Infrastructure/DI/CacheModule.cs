namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for distributed cache services (ADR-009).
/// Configures Redis for production or in-memory cache for local development.
/// </summary>
public static class CacheModule
{
    /// <summary>
    /// Adds distributed cache (Redis or in-memory) and memory cache services.
    /// </summary>
    public static IServiceCollection AddCacheModule(
        this IServiceCollection services,
        IConfiguration configuration,
        ILoggingBuilder logging)
    {
        var redisEnabled = configuration.GetValue<bool>("Redis:Enabled");
        if (redisEnabled)
        {
            var redisConnectionString = configuration.GetConnectionString("Redis")
                ?? configuration["Redis:ConnectionString"];

            if (string.IsNullOrWhiteSpace(redisConnectionString))
            {
                throw new InvalidOperationException(
                    "Redis is enabled but no connection string found. " +
                    "Set 'ConnectionStrings:Redis' or 'Redis:ConnectionString' in configuration.");
            }

            var configOptions = StackExchange.Redis.ConfigurationOptions.Parse(redisConnectionString);
            configOptions.AbortOnConnectFail = false;
            configOptions.ConnectTimeout = 5000;
            configOptions.SyncTimeout = 5000;
            configOptions.ConnectRetry = 3;
            configOptions.ReconnectRetryPolicy = new StackExchange.Redis.ExponentialRetry(1000);

            // Register IConnectionMultiplexer for JobStatusService pub/sub
            var connectionMultiplexer = StackExchange.Redis.ConnectionMultiplexer.Connect(configOptions);
            services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(connectionMultiplexer);

            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = configuration["Redis:InstanceName"] ?? "sdap:";
                options.ConfigurationOptions = configOptions;
            });

            logging.AddSimpleConsole().Services.Configure<Microsoft.Extensions.Logging.Console.SimpleConsoleFormatterOptions>(options =>
            {
                options.TimestampFormat = "HH:mm:ss ";
            });

            var logger = LoggerFactory.Create(config => config.AddConsole()).CreateLogger("Program");
            logger.LogInformation(
                "Distributed cache: Redis enabled with instance name '{InstanceName}'",
                configuration["Redis:InstanceName"] ?? "sdap:");
        }
        else
        {
            services.AddDistributedMemoryCache();

            var logger = LoggerFactory.Create(config => config.AddConsole()).CreateLogger("Program");
            logger.LogWarning(
                "Distributed cache: Using in-memory cache (not distributed). " +
                "This should ONLY be used in local development.");
        }

        services.AddMemoryCache();

        return services;
    }
}
