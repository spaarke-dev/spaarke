namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Which customer this BFF instance serves (D-14, unified-access-control-r2 task 123).
///
/// <para>
/// Bound from the <c>Customer</c> section; on App Service the value arrives as the application setting
/// <c>Customer__Id</c>, emitted by <c>infrastructure/bicep/customer.bicep</c> and
/// <c>infrastructure/bicep/stacks/model2-full.bicep</c> from the <c>customerId</c> those stacks already
/// hold. When the setting is absent the value is DERIVED from <c>WEBSITE_RESOURCE_GROUP</c> — see
/// <see cref="CustomerIdResolver"/>, which owns both paths and the rule that neither may invent a value.
/// </para>
///
/// <para>
/// NO <c>[Required]</c> DATA ANNOTATION — deliberately, and for the reason
/// <see cref="PublicConfigOptions"/> records: a bare <c>[Required]</c> evaluates eagerly on every option
/// read and would crash the Development / Testing boot path.
/// <see cref="CustomerOptionsValidator"/> is the single source of truth for requiredness, and
/// <see cref="CustomerIdentity"/> is what makes an unresolved value fail at the point of USE rather
/// than silently reading as an empty string.
/// </para>
///
/// <para>
/// 🔴 Do NOT read <see cref="Id"/> directly to key a cache, a log scope or a metric — inject
/// <see cref="CustomerIdentity"/> instead. This type can legitimately hold an empty string (in
/// Development, and in Testing fixtures that do not set it); <see cref="CustomerIdentity.Id"/> throws
/// in that state, which is the behaviour every consumer wants.
/// </para>
/// </summary>
public class CustomerOptions
{
    public const string SectionName = "Customer";

    /// <summary>
    /// The customerId — 3–8 characters, lowercase letters and digits, starting with a letter
    /// (<see cref="CustomerIdResolver.CustomerIdPattern"/>). Assigned at provisioning intake and stored
    /// on <c>sprk_dataverseenvironment.sprk_customerid</c>; Bicep CONSUMES it and never mints one.
    ///
    /// <para>
    /// Empty means unresolved, never "the default customer". See the type remarks.
    /// </para>
    /// </summary>
    public string Id { get; set; } = string.Empty;
}
