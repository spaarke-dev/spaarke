// Program.cs — Minimal pipeline + DI (condensed)
builder.Services.AddProblemDetails();
builder.Services.AddOptions<ServiceBusOptions>().Bind(Configuration.GetSection("ServiceBus")).ValidateOnStart();
builder.Services.AddOptions<DataverseOptions>().Bind(Configuration.GetSection("Dataverse")).ValidateOnStart();
builder.Services.AddOptions<GraphOptions>().Bind(Configuration.GetSection("Graph")).ValidateOnStart();

builder.Services.AddStackExchangeRedisCache(o => o.Configuration = Configuration.GetConnectionString("Redis"));
builder.Services.AddSingleton(new ServiceBusClient(Configuration.GetConnectionString("ServiceBus")));
builder.Services.AddSingleton(sp => GraphClientFactory.Create(sp.GetRequiredService<IOptions<GraphOptions>>().Value));
builder.Services.AddHttpClient<IDataverseClient, DataverseClient>().AddPolicyHandler(PollyPolicies.HttpRetry());

builder.Services.AddScoped<AuthorizationService>();
builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<SpeFileStore>();
builder.Services.AddScoped<IAccessDataSource, DataverseAccessDataSource>();
builder.Services.AddScoped<RequestCache>();

builder.Services.AddScoped<IAuthorizationRule, ExplicitDenyRule>();
builder.Services.AddScoped<IAuthorizationRule, ExplicitGrantRule>();
builder.Services.AddScoped<IAuthorizationRule, TeamMembershipRule>();
builder.Services.AddScoped<IAuthorizationRule, RoleScopeRule>();
builder.Services.AddScoped<IAuthorizationRule, LinkTokenRule>();

builder.Services.AddHostedService<OcrWorker>();
builder.Services.AddHostedService<IndexWorker>();

app.UseExceptionHandler(_ => { });
app.UseAuthentication();
app.UseMiddleware<SpaarkeContextMiddleware>();
app.UseAuthorization();
app.MapHealthChecks("/healthz");
