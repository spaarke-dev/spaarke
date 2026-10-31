using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Text;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Identity;

namespace Sprk.Bff.Api.Services.Signals.Actions;

/// <summary>What the inquiry email says about its core record, decided at send time (task 160).</summary>
/// <param name="RecordLabel">"{name} ({number})" of the core record, or null when it could not be read OR may not be shown to these
/// recipients (see <see cref="InquiryRecordFacts"/>). Becomes the service-request association's <c>EntityName</c>: the footer's record
/// reference and <c>sprk_communication.sprk_regardingrecordname</c>.</param>
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
/// <para><b>Who may see the record's name (uac-r2 review F1).</b> The footer names the record to EVERY recipient, so the label is
/// internal-only: it is returned only when the record is positively Standard (<c>sprk_accesspermission</c> is exactly Standard; a
/// Restricted, Limited, blank or unreadable value gets no label) AND every recipient is internal. For the label a recipient is internal
/// only when an enabled systemuser matches the address and is not flagged <c>sprk_isexternal</c>
/// (<see cref="ISystemUserIdentityResolver.IsExternalAsync"/>); a contact, an unknown address, a flagged systemuser, no recipients, or a
/// check that failed or ran past the deadline means no label, and the caller-approved subject names the inquiry instead. This test is
/// stricter than the link choice above, which is unchanged.</para>
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

    /// <summary>At most this many recipient checks run at once.</summary>
    internal const int MaxConcurrentChecks = 10;

    /// <summary>One deadline for the whole read (the label and every recipient). Settable by the tests only.</summary>
    internal TimeSpan ReadDeadline { get; init; } = TimeSpan.FromSeconds(10);

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
    private readonly ISystemUserIdentityResolver _externality;
    private readonly CustomerIdentity _customer;
    private readonly IConfiguration _configuration;
    private readonly string? _environmentUrl;
    private readonly ILogger<InquiryRecordFacts> _logger;

    public InquiryRecordFacts(
        IFieldMappingDataverseService appOnly,
        IContactIdentityStore identities,
        ISystemUserIdentityResolver externality,
        CustomerIdentity customer,
        IConfiguration configuration,
        IOptions<DataverseOptions> dataverse,
        ILogger<InquiryRecordFacts> logger)
    {
        _appOnly = appOnly ?? throw new ArgumentNullException(nameof(appOnly));
        _identities = identities ?? throw new ArgumentNullException(nameof(identities));
        _externality = externality ?? throw new ArgumentNullException(nameof(externality));
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
        var addresses = recipients.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var (label, outcomes) = await ReadAsync(coreLogicalName, coreId, addresses, ct).ConfigureAwait(false);

        var anyInternal = outcomes.Contains(LicensedUserEmailOutcome.LicensedUser);
        var unknown = outcomes.Count(o => o == LicensedUserEmailOutcome.Unknown);
        var anyExternal = outcomes.Any(o => o != LicensedUserEmailOutcome.LicensedUser);

        if (unknown > 0)
        {
            _logger.LogWarning(
                "Budget inquiry: {Count} of {Total} recipient(s) could not be checked against the enabled users (read failed or timed out) " +
                "and are given the portal link.", unknown, addresses.Length);
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
                ? $"External recipients, open this {noun} in the Spaarke portal (if you have portal access): {portalLink}"
                : $"Open this {noun} in the Spaarke portal (if you have portal access): {portalLink}");
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

    /// <summary>
    /// The record label and every recipient's classification, read CONCURRENTLY (at most <see cref="MaxConcurrentChecks"/> in flight)
    /// under ONE deadline (<see cref="ReadDeadline"/>), so a slow read never holds the send for a recipient-count of timeouts. A
    /// recipient not classified by the deadline stays <see cref="LicensedUserEmailOutcome.Unknown"/> (external, the portal link), as
    /// a failed read already does; the label stays null. The label is also null unless EVERY recipient was positively found internal
    /// (an enabled systemuser, not flagged external) within the same gate and deadline; see the class remarks.
    /// </summary>
    private async Task<(string? Label, LicensedUserEmailOutcome[] Outcomes)> ReadAsync(
        string coreLogicalName, Guid coreId, string[] addresses, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(ReadDeadline);
        var token = deadline.Token;

        var outcomes = new LicensedUserEmailOutcome[addresses.Length];
        Array.Fill(outcomes, LicensedUserEmailOutcome.Unknown);
        var internalOnly = new bool[addresses.Length]; // false until the recipient is positively internal (the label's test)
        string? label = null;
        var gate = new SemaphoreSlim(MaxConcurrentChecks);

        async Task LabelAsync() => label = await ReadLabelAsync(coreLogicalName, coreId, token).ConfigureAwait(false);

        async Task CheckAsync(int index)
        {
            try
            {
                await gate.WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                var (outcome, matched) = await ClassifyAsync(addresses[index], token).ConfigureAwait(false);
                // The link outcome is final here: the externality check below only ever decides the label.
                outcomes[index] = outcome;
                internalOnly[index] = await AllInternalAsync(matched, token).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        var all = Task.WhenAll(addresses.Select((_, i) => CheckAsync(i)).Append(LabelAsync()));
        try
        {
            // WaitAsync abandons the wait at the deadline even for a read that ignores its token.
            await all.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Budget inquiry: the record and recipient reads did not finish within {Seconds:0.#} s; the email goes with what was read.",
                ReadDeadline.TotalSeconds);
            // Whatever finished late must not fault unobserved.
            _ = all.ContinueWith(static t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
        catch (OperationCanceledException)
        {
            // The REQUEST was cancelled, not the deadline: no timeout warning; the caller sees the cancellation.
            _ = all.ContinueWith(static t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            throw;
        }

        // Copies: a read that finishes after the deadline must not change what this email was decided on.
        var decided = outcomes.ToArray();
        var allInternal = addresses.Length > 0 && internalOnly.ToArray().All(i => i);
        var shown = allInternal ? label : null;
        if (label is not null && shown is null)
        {
            _logger.LogInformation("Budget inquiry: the record's name is left out of the footer (a recipient is not confirmed internal).");
        }

        return (shown, decided);
    }

    private async Task<string?> ReadLabelAsync(string coreLogicalName, Guid coreId, CancellationToken ct)
    {
        if (!LabelColumns.TryGetValue(coreLogicalName, out var columns))
            return null;

        try
        {
            // The access permission is read in the same call as the name, so the name is never read without it.
            var row = await _appOnly.RetrieveRecordFieldsAsync(
                coreLogicalName, coreId, [columns.Name, columns.Number, InheritedAccessPermission.Column], ct).ConfigureAwait(false);
            if (!IsStandard(row.GetValueOrDefault(InheritedAccessPermission.Column)))
            {
                // Fail closed: only a positively Standard record is named in the footer.
                return null;
            }

            return RecordLabel(row.GetValueOrDefault(columns.Name) as string, row.GetValueOrDefault(columns.Number) as string);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Budget inquiry: the name of {CoreType} {CoreId} could not be read; the footer names the inquiry instead.",
                coreLogicalName, coreId);
            return null;
        }
    }

    /// <summary>True only for exactly Standard. A null, blank, Limited, Restricted or unrecognised value is not (the record-access
    /// rules elsewhere read a null as Standard; the footer's label does not).</summary>
    private static bool IsStandard(object? value) => value switch
    {
        int i => i == InheritedAccessPermission.Standard,
        long l => l == InheritedAccessPermission.Standard,
        _ => false,
    };

    /// <summary>The link outcome, and the ids of the enabled systemusers the address matches (for the label's externality test).</summary>
    private async Task<(LicensedUserEmailOutcome Outcome, IReadOnlyList<Guid> Matched)> ClassifyAsync(string address, CancellationToken ct)
    {
        try
        {
            var lookup = await _identities.FindEnabledSystemUsersByEmailAsync(address, ct).ConfigureAwait(false);
            var outcome = ContactBindingDecision.DecideLicensedUserEmail(lookup, address);
            return (outcome, ContactBindingDecision.MatchingEnabledSystemUserIds(lookup, address));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The deadline: ReadAsync logs it once, with counts.
            return (LicensedUserEmailOutcome.Unknown, []);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Budget inquiry: a recipient could not be checked against the enabled users.");
            return (LicensedUserEmailOutcome.Unknown, []);
        }
    }

    /// <summary>
    /// True only when the address matched at least one enabled systemuser and NONE of them is flagged <c>sprk_isexternal</c>.
    /// <see cref="ISystemUserIdentityResolver.IsExternalAsync"/> answers true for a flagged user, an empty id or a missing row, and
    /// throws when its read fails; a throw (or the deadline) is "not internal", never an exception.
    /// </summary>
    private async Task<bool> AllInternalAsync(IReadOnlyList<Guid> matched, CancellationToken ct)
    {
        if (matched.Count == 0)
            return false;

        try
        {
            foreach (var id in matched)
            {
                if (await _externality.IsExternalAsync(id, ct).ConfigureAwait(false))
                    return false;
            }

            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Budget inquiry: a recipient's externality could not be checked; the footer leaves the record's name out.");
            return false;
        }
    }
}
