// -----------------------------------------------------------------------------
// SwaggerModule.cs
//
// L2 CONTROL-PLANE OpenAPI / Swagger composition.
//
// SPEC / ADR references:
//   - spec.md FR-20:  OpenAPI at /swagger.
//   - spec.md FR-21:  L2 endpoints must export via OpenAPI schema (task 037+
//                     onward populate the surface; this module makes the
//                     surface visible).
//   - ADR-010:        Extension-method module keeps Program.cs at ~15 lines.
//
// Swagger is enabled UNCONDITIONALLY (no IsDevelopment gate) because the L2
// surface is Spaarke-internal and OpenAPI export is an acceptance criterion.
// Bearer security scheme is declared so operators can paste an
// `az account get-access-token --resource api://spaarke-provisioning-controlplane-{env}`
// token into the Swagger UI to exercise Operator/Reader-protected endpoints.
// -----------------------------------------------------------------------------

using Microsoft.OpenApi.Models;

namespace Sprk.Provisioning.ControlPlane.Modules;

/// <summary>
/// DI registration for OpenAPI (Swashbuckle) + a Bearer security scheme so
/// the Swagger UI can authorize requests using a JWT acquired via
/// <c>az account get-access-token</c> (FR-20 acceptance).
/// </summary>
public static class SwaggerModule
{
    /// <summary>Registers Swashbuckle with a single "Bearer" JWT security scheme.</summary>
    public static IServiceCollection AddSwaggerModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Sprk.Provisioning.ControlPlane",
                Version = "v1",
                Description =
                    "L2 fleet-scoped provisioning orchestration API. Sequences the 19 IJobHandler " +
                    "handlers that provision a customer environment. See " +
                    "projects/customer-provisioning-orchestration-r1/spec.md FR-20..FR-24.",
            });

            // Bearer JWT security scheme — operators paste a token from
            //   az account get-access-token --resource api://spaarke-provisioning-controlplane-{env}
            // into the Swagger UI's Authorize dialog.
            var bearerScheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = "Paste a bearer token: 'Bearer {token}'.",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer",
                },
            };
            options.AddSecurityDefinition("Bearer", bearerScheme);
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [bearerScheme] = Array.Empty<string>(),
            });
        });

        return services;
    }
}
