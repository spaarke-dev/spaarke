using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Services.Dataverse.Privileges;
using Sprk.Bff.Api.Tests.Integration.Workspace;
using AccessRights = Spaarke.Dataverse.AccessRights;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Test host for the <c>/api/v1/external</c> collaboration group.
///
/// Why this exists (fixture-config-FIRST per bff-extensions.md § F.2): the group is gated by
/// <see cref="AuthPolicies.ExternalCollaboration"/>, which pins named schemes —
/// <c>AuthSchemes.Ciam</c>, <c>JwtBearerDefaults.AuthenticationScheme</c> and (r3 task 045)
/// <c>AuthSchemes.WorkforceHome</c> (AuthorizationModule.cs). Naming schemes explicitly bypasses the default scheme, so
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
    /// <summary>
    /// The Workforce-role ceiling (unified-access-control-r2 task 186) this host answers with. Defaults to every privilege
    /// the <c>/api/v1/external</c> routes need (<see cref="WorkforceCeilingDouble.OwnerPrerequisite"/>), so a workforce-contact
    /// test written before the ceiling keeps its meaning; a ceiling test sets <see cref="WorkforceCeilingDouble.Ceiling"/>.
    /// </summary>
    internal WorkforceCeilingDouble WorkforceCeiling { get; } = new();

    /// <summary>
    /// ebilling-r1 task 098: the (a') invoice predicate this host answers with. Defaults to hiding nothing, so a test written
    /// before the predicate keeps its meaning; an (a') route test sets one that hides (the predicate itself is tested over
    /// the real reads in <c>ExternalInvoiceVisibilityTests</c>).
    /// </summary>
    internal Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceHidingStub InvoiceHiding { get; set; } =
        Sprk.Bff.Api.Tests.Api.ExternalAccess.InvoiceHidingStub.NoneHidden();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            // The real checker still answers the per-user Read questions; only the Workforce-role read is scripted (it
            // needs a live ServiceClient).
            services.RemoveAll<IDataversePrivilegeChecker>();
            services.AddSingleton<IDataversePrivilegeChecker>(sp =>
            {
                WorkforceCeiling.Inner = ActivatorUtilities.CreateInstance<UserPrivilegeChecker>(sp);
                return WorkforceCeiling;
            });

            services.RemoveAll<Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalInvoiceHiding>();
            services.AddScoped<Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalInvoiceHiding>(
                _ => (caller, invoiceIds, ct) => InvoiceHiding.Seam(caller, invoiceIds, ct));

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

/// <summary>
/// The Workforce-role ceiling as a test double (task 186): <see cref="GetWorkforceRoleCeilingAsync"/> answers
/// <see cref="Ceiling"/> and counts the reads; the per-user Read questions go to the real checker (<see cref="Inner"/>).
/// </summary>
internal sealed class WorkforceCeilingDouble : IDataversePrivilegeChecker
{
    private int _reads;

    /// <summary>
    /// Every privilege the Workforce role needs for workforce contacts to keep every <c>/api/v1/external</c> route working
    /// (review round 1 F1): the union of the routes' requirement sets, the message and contact-grant roots, and Read on every
    /// module the module-data seam serves. It is the table the owner is given; <c>TheFixtureDefault_IsEveryRoutesPrivilege</c>
    /// pins it to the routes, so a route that needs more fails there first.
    /// </summary>
    public static IReadOnlyDictionary<string, AccessRights> OwnerPrerequisite { get; } = new Dictionary<string, AccessRights>
    {
        ["sprk_servicerequest"] = AccessRights.Create | AccessRights.Read | AccessRights.AppendTo | AccessRights.Delete,
        ["sprk_document"] = AccessRights.Create | AccessRights.Read | AccessRights.AppendTo | AccessRights.Delete,
        ["sprk_project"] = AccessRights.Read | AccessRights.AppendTo,
        ["sprk_matter"] = AccessRights.Read | AccessRights.AppendTo,
        ["sprk_workassignment"] = AccessRights.Read | AccessRights.AppendTo,
        ["sprk_invoice"] = AccessRights.Read | AccessRights.AppendTo,
        ["sprk_invoicesubmission"] = AccessRights.Create | AccessRights.Read,
        ["sprk_todo"] = AccessRights.Create | AccessRights.Read | AccessRights.Write | AccessRights.Delete,
        ["sprk_event"] = AccessRights.Create | AccessRights.Read | AccessRights.Delete,
        ["contact"] = AccessRights.Read,
        ["account"] = AccessRights.Read,
        ["sprk_communication"] = AccessRights.Create | AccessRights.Read,
        ["sprk_externalrecordaccess"] = AccessRights.Create | AccessRights.Read | AccessRights.Write,
        ["sprk_gridconfiguration"] = AccessRights.Read,
    };

    public WorkforceRoleCeiling Ceiling { get; set; } = WorkforceRoleCeiling.Available(OwnerPrerequisite);

    /// <summary>How many times the ceiling was asked for.</summary>
    public int Reads => Volatile.Read(ref _reads);

    public IDataversePrivilegeChecker? Inner { get; set; }

    /// <summary>
    /// Task 099b: answers the owner-team question (<see cref="TeamHasReadPrivilegeAsync"/>) when set; otherwise it goes to
    /// <see cref="Inner"/>. A host whose real checker has no ServiceClient sets a team double here.
    /// </summary>
    public IDataversePrivilegeChecker? OwnerTeams { get; set; }

    public void Reset()
    {
        Ceiling = WorkforceRoleCeiling.Available(OwnerPrerequisite);
        Interlocked.Exchange(ref _reads, 0);
    }

    public Task<bool> HasReadPrivilegeAsync(Guid userOid, string entityLogicalName, CancellationToken ct) =>
        (Inner ?? throw new InvalidOperationException("No inner checker.")).HasReadPrivilegeAsync(userOid, entityLogicalName, ct);

    public Task<IReadOnlySet<string>> GetReadableEntitiesAsync(Guid userOid, CancellationToken ct) =>
        (Inner ?? throw new InvalidOperationException("No inner checker.")).GetReadableEntitiesAsync(userOid, ct);

    public Task<bool> TeamHasReadPrivilegeAsync(Guid teamId, string entityLogicalName, bool fresh, CancellationToken ct) =>
        (OwnerTeams ?? Inner ?? throw new InvalidOperationException("No inner checker."))
            .TeamHasReadPrivilegeAsync(teamId, entityLogicalName, fresh, ct);

    public Task<WorkforceRoleCeiling> GetWorkforceRoleCeilingAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _reads);
        return Task.FromResult(Ceiling);
    }
}
