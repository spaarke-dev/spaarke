using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Tests.Integration.Workspace;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Test host for the <c>/api/v1/external</c> collaboration group.
///
/// Why this exists (fixture-config-FIRST per bff-extensions.md § F.2): the group is gated by
/// <see cref="AuthPolicies.ExternalCollaboration"/>, which pins TWO named schemes —
/// <c>AuthSchemes.Ciam</c> and <c>JwtBearerDefaults.AuthenticationScheme</c>
/// (AuthorizationModule.cs:278-286). Naming schemes explicitly bypasses the default scheme, so
/// <see cref="WorkspaceTestFixture"/>'s <c>FakeAuthHandler</c> is never consulted on this group.
/// The real <c>Ciam</c> JwtBearer handler is then asked to validate, and its authority is built from
/// <c>Ciam:Instance</c> / <c>Ciam:TenantId</c> — config no fixture supplies — yielding a malformed
/// authority and an OIDC-metadata fetch that throws. The observable result is <c>500</c> where
/// production returns a clean <c>401</c>.
///
/// That 500 is a TEST-HOST artifact, not a production finding, and it is actively harmful to leave
/// in place: an authorization characterization asserting "not 403" passes VACUOUSLY when the request
/// never reaches the handler. This fixture removes the artifact by re-registering the policy against
/// the fake scheme (<c>AuthorizationOptions.AddPolicy</c> overwrites by name, and
/// <c>ConfigureTestServices</c> runs after <c>Program.cs</c>), so authentication behaves offline the
/// way it does in production: no credential → 401, credential → authenticated.
///
/// What this fixture deliberately does NOT do: stub <c>CallerPrincipalAuthorizationFilter</c>'s
/// principal resolution. Requests through THIS fixture therefore stop at the filter, which is the
/// right default for characterizing "an unresolvable caller must not reach a write handler".
///
/// <para><b>UPDATE 2026-08-24 (task 009).</b> The original note here said handler-level record-scope
/// behavior on this group (finding A-7) "stays out of reach offline" because principal resolution
/// "needs real Dataverse participation data". That is no longer accurate: the filter resolves through
/// <c>ICallerPrincipalResolver</c>, which is an interface registered <c>AddScoped</c>
/// (ExternalAccessModule.cs:141) and can be substituted in <c>ConfigureTestServices</c> — no
/// Dataverse required. <c>ExternalTodoScopeTestFixture</c> does exactly that. This class is now
/// unsealed so that fixture can inherit the policy fix above instead of duplicating it.</para>
/// </summary>
public class ExternalCollaborationTestFixture : WorkspaceTestFixture
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.Configure<AuthorizationOptions>(options =>
            {
                options.AddPolicy(AuthPolicies.ExternalCollaboration, policy =>
                {
                    policy.AuthenticationSchemes = new[] { "FakeAuth" };
                    policy.RequireAuthenticatedUser();
                });
            });
        });
    }
}
