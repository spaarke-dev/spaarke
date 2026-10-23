using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Text;

namespace Sprk.Bff.Api.Services.Signals.Actions;

/// <summary>What the inquiry email says about its core record, decided at send time (task 160).</summary>
/// <param name="RecordLabel">"{name} ({number})" of the core record, or null when it could not be read. Becomes the service-request
/// association's <c>EntityName</c>: the footer's record reference and <c>sprk_communication.sprk_regardingrecordname</c>.</param>
/// <param name="LinkLines">The record link line(s) appended to the body, one per audience the recipients include.</param>
public sealed record InquiryEmailExtras(string? RecordLabel, IReadOnlyList<string> LinkLines);

/// <summary>
/// Task 160 (D-181; owner 2026-10-23): the inquiry email's record link chosen by recipient, and the record's name for the footer. Read
/// as the BFF at send time, for the core record the commit route has already authorised (the core has no name at commit).
/// </summary>
/// <remarks>
/// <para><b>Who is internal (owner 2026-10-23).</b> A recipient whose address is an ENABLED systemuser's <c>internalemailaddress</c> or
/// <c>domainname</c> gets the Spaarke app link, INCLUDING a systemuser flagged <c>sprk_isexternal</c>; anyone else (a contact, an unknown
/// address) gets the portal link. A contact and a systemuser that share an address are therefore internal. The read is the A9 licensed-user
/// read (<see cref="IContactIdentityStore.FindEnabledSystemUsersByEmailAsync"/>, decided by
/// <see cref="ContactBindingDecision.DecideLicensedUserEmail"/>); a recipient it cannot answer for is treated as external (Warning).</para>
/// <para><b>Links.</b> Built ONLY from configured base URLs (<c>Dataverse:EnvironmentUrl</c>, <c>ExternalAccess:PortalUrl</c>), this
/// stamp's customer key, a type token and a GUID; never from a recipient or any user text. The portal link is
/// <see cref="GrantAccessNotifier.BuildPortalLink(string?, string?, string, Guid)"/>; a core type the portal does not parse, no customer key
/// or an unusable portal URL gives no portal line (Warning), and the email still goes.</para>
/// <para><b>Best effort.</b> Nothing here throws: a failed read leaves the label out or the recipient external, and the email is sent.</para>
/// </remarks>
public class InquiryRecordFacts
{
    /// <summary><c>sprk_communication.sprk_regardingrecordname</c> holds 100 characters (CommunicationService truncates to it).</summary>
    internal const int RecordLabelMax = 100;

    private static readonly Regex LogicalName = new("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant);

    /// <summary>The portal token and the noun for each core type the link can name. A type not listed gets no portal link.</summary>
    private static readonly IReadOnlyDictionary<string, (string Token, string Noun)> CoreTypes =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["sprk_matter"] = ("matter", "matter"),
            ["sprk_project"] = ("project", "project"),
            ["sprk_workassignment"] = ("workassignment", "work assignment"),
            ["sprk_invoice"] = ("invoice", "invoice"),
        };

    /// <summary>The name and number columns of a core type whose label the footer reads (the commit route supplies a matter only).</summary>
    private static readonly IReadOnlyDictionary<string, (string Name, string Number)> LabelColumns =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["sprk_matter"] = ("sprk_mattername", "sprk_matternumber"),
        };

    private readonly IFieldMappingDataverseService _appOnly;
    private readonly IContactIdentityStore _identities;
    private readonly CustomerIdentity _customer;
    private readonly IConfiguration _configuration;
    private readonly string? _environmentUrl;
    private readonly ILogger<InquiryRecordFacts> _logger;

    public InquiryRecordFacts(
        IFieldMappingDataverseService appOnly,
        IContactIdentityStore identities,
        CustomerIdentity customer,
        IConfiguration configuration,
        IOptions<DataverseOptions> dataverse,
        ILogger<InquiryRecordFacts> logger)
    {
        _appOnly = appOnly ?? throw new ArgumentNullException(nameof(appOnly));
        _identities = identities ?? throw new ArgumentNullException(nameof(identities));
        _customer = customer ?? throw new ArgumentNullException(nameof(customer));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _environmentUrl = dataverse?.Value.EnvironmentUrl;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The label and link lines for an inquiry about <paramref name="coreLogicalName"/> <paramref name="coreId"/> to
    /// <paramref name="recipients"/>. Never throws.</summary>
    public virtual async Task<InquiryEmailExtras> ResolveAsync(
        string coreLogicalName, Guid coreId, IReadOnlyList<string> recipients, CancellationToken ct)
    {
        var label = await ReadLabelAsync(coreLogicalName, coreId, ct).ConfigureAwait(false);

        var anyInternal = false;
        var anyExternal = false;
        var unknown = 0;
        foreach (var address in recipients.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            switch (await ClassifyAsync(address, ct).ConfigureAwait(false))
            {
                case LicensedUserEmailOutcome.LicensedUser:
                    anyInternal = true;
                    break;
                case LicensedUserEmailOutcome.Unknown:
                    unknown++;
                    anyExternal = true;
                    break;
                default:
                    anyExternal = true;
                    break;
            }
        }

        if (unknown > 0)
        {
            _logger.LogWarning(
                "Budget inquiry: {Count} recipient(s) could not be checked against the enabled users and are given the portal link.", unknown);
        }

        var known = CoreTypes.TryGetValue(coreLogicalName, out var type);
        var noun = known ? type.Noun : "record";
        var appLink = anyInternal ? AppLink(_environmentUrl, coreLogicalName, coreId) : null;
        var portalLink = anyExternal && known
            ? GrantAccessNotifier.BuildPortalLink(_configuration["ExternalAccess:PortalUrl"], _customer.IsResolved ? _customer.Id : null, type.Token, coreId)
            : null;

        if (anyInternal && appLink is null)
        {
            _logger.LogWarning(
                "Budget inquiry: no app link for {CoreType} {CoreId}: Dataverse:EnvironmentUrl must be an https URL. The email is sent without it.",
                coreLogicalName, coreId);
        }

        if (anyExternal && portalLink is null)
        {
            _logger.LogWarning(
                "Budget inquiry: no portal link for {CoreType} {CoreId}: the portal must parse the record type, ExternalAccess:PortalUrl must be " +
                "an https URL and this stamp's customer id (Customer:Id) must be resolved. The email is sent without it.",
                coreLogicalName, coreId);
        }

        return new InquiryEmailExtras(label, LinkLines(noun, anyInternal && anyExternal, appLink, portalLink));
    }

    /// <summary>One line per audience; both are labelled when the recipients are mixed.</summary>
    internal static IReadOnlyList<string> LinkLines(string noun, bool mixed, string? appLink, string? portalLink)
    {
        var lines = new List<string>(2);
        if (appLink is not null)
            lines.Add(mixed ? $"Spaarke users, open this {noun} in Spaarke: {appLink}" : $"Open this {noun} in Spaarke: {appLink}");
        if (portalLink is not null)
            lines.Add(mixed
                ? $"External recipients, open this {noun} in the Spaarke portal: {portalLink}"
                : $"Open this {noun} in the Spaarke portal: {portalLink}");
        return lines;
    }

    /// <summary>
    /// The model-driven app's record URL: the environment URL (absolute https, no credentials) plus the relative shape
    /// <c>/main.aspx?etn={logical}&amp;id={id}&amp;pagetype=entityrecord</c> (GrantAccessNotifier). Null when either is unusable.
    /// </summary>
    internal static string? AppLink(string? environmentUrl, string logicalName, Guid id)
    {
        if (id == Guid.Empty || string.IsNullOrEmpty(logicalName) || !LogicalName.IsMatch(logicalName)
            || !Uri.TryCreate(environmentUrl?.Trim(), UriKind.Absolute, out var env)
            || env.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(env.UserInfo))
        {
            return null;
        }

        return $"{env.GetLeftPart(UriPartial.Path).TrimEnd('/')}/main.aspx?etn={logicalName}&id={id:D}&pagetype=entityrecord";
    }

    /// <summary>"{name} ({number})", the name alone, or the number alone; cut to <see cref="RecordLabelMax"/>. Null when neither is set.</summary>
    internal static string? RecordLabel(string? name, string? number)
    {
        var n = name?.Trim();
        var no = number?.Trim();
        var label = (string.IsNullOrEmpty(n), string.IsNullOrEmpty(no)) switch
        {
            (false, false) => $"{n} ({no})",
            (false, true) => n,
            (true, false) => no,
            _ => null,
        };
        // The cut adds an ellipsis, so it cuts one short to stay inside the column.
        return label is null ? null : TextTruncation.TruncateSurrogateSafe(label, RecordLabelMax - 1);
    }

    private async Task<string?> ReadLabelAsync(string coreLogicalName, Guid coreId, CancellationToken ct)
    {
        if (!LabelColumns.TryGetValue(coreLogicalName, out var columns))
            return null;

        try
        {
            var row = await _appOnly.RetrieveRecordFieldsAsync(coreLogicalName, coreId, [columns.Name, columns.Number], ct).ConfigureAwait(false);
            return RecordLabel(row.GetValueOrDefault(columns.Name) as string, row.GetValueOrDefault(columns.Number) as string);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Budget inquiry: the name of {CoreType} {CoreId} could not be read; the footer names the inquiry instead.",
                coreLogicalName, coreId);
            return null;
        }
    }

    private async Task<LicensedUserEmailOutcome> ClassifyAsync(string address, CancellationToken ct)
    {
        try
        {
            var lookup = await _identities.FindEnabledSystemUsersByEmailAsync(address, ct).ConfigureAwait(false);
            return ContactBindingDecision.DecideLicensedUserEmail(lookup, address);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Budget inquiry: a recipient could not be checked against the enabled users.");
            return LicensedUserEmailOutcome.Unknown;
        }
    }
}
