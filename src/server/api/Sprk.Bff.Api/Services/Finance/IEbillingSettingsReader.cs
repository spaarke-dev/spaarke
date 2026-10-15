using Sprk.Bff.Api.Services.Finance.Models;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// Reads the eBilling business settings (FR-48, D-59): the one <c>sprk_ebillingsetting</c> row, cached for a few seconds so a changed
/// value takes effect on the next submission without a deployment.
/// </summary>
public interface IEbillingSettingsReader
{
    /// <summary>
    /// Returns the current settings. When no active settings row exists the typed defaults (equal to the seed) are returned and a
    /// warning is logged. A Dataverse read failure is NOT swallowed: it propagates, so a caller never acts on made-up settings
    /// because of an outage.
    /// </summary>
    Task<EbillingSettings> GetAsync(CancellationToken ct = default);
}
