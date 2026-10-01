using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Env-aware startup validator for <see cref="CustomerOptions"/> (unified-access-control-r2 task 123,
/// D-14 §8). Mirrors <see cref="PublicConfigOptionsValidator"/>, which is the established shape for
/// "Tier-1 fail-fast in deployed environments, short-circuit in Development / Testing".
///
/// <para>
/// FAIL CLOSED IN DEPLOYED ENVIRONMENTS. A stamp that cannot say which customer it serves is
/// misconfigured, and the failure must be a refusal to start rather than a value that quietly reads as
/// shared. D-14 §8 rejected "warn and continue" outright for exactly that reason.
/// </para>
///
/// <para>
/// SHORT-CIRCUIT IN DEVELOPMENT / TESTING. A developer workstation has no
/// <c>WEBSITE_RESOURCE_GROUP</c>, and requiring every one of the 30+ per-endpoint test fixtures to add
/// a <c>Customer:Id</c> entry would be a mechanical sweep with high review cost and no safety benefit
/// (per <c>.claude/constraints/bff-extensions.md</c> §F.2.1 Testing allow-list). This is NOT a default
/// value and NOT a softening: in those environments the identity simply stays unresolved, and
/// <see cref="CustomerIdentity.Id"/> throws if anything actually asks for it. A test that needs the
/// value sets <c>Customer:Id</c>; a test that does not, never notices.
/// </para>
/// </summary>
public sealed class CustomerOptionsValidator : IValidateOptions<CustomerOptions>
{
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public CustomerOptionsValidator(IHostEnvironment environment, IConfiguration configuration)
    {
        _environment = environment;
        _configuration = configuration;
    }

    public ValidateOptionsResult Validate(string? name, CustomerOptions options)
    {
        var isLocalLike = _environment.IsDevelopment() ||
            string.Equals(_environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase);

        if (isLocalLike)
        {
            return ValidateOptionsResult.Success;
        }

        var resourceGroupName = _configuration[CustomerIdResolver.ResourceGroupEnvironmentVariable];
        var (id, _) = CustomerIdResolver.Resolve(options.Id, resourceGroupName);

        return string.IsNullOrEmpty(id)
            ? ValidateOptionsResult.Fail(
                CustomerIdResolver.BuildUnresolvedMessage(options.Id, resourceGroupName))
            : ValidateOptionsResult.Success;
    }
}
