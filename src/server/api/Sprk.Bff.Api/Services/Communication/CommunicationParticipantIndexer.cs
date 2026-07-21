using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Models;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Writes the queryable participant index (FR-08 / ADR-048): for every persisted <c>sprk_communication</c>
/// message — outbound send AND inbound capture — it writes ONE <c>sprk_communicationparticipant</c> junction
/// row per (message × person/address × role {From,To,Cc,Bcc}) at MESSAGE grain (parent =
/// <c>sprk_communication</c>; there are no thread-grain rows — thread participation is derived by rollup).
/// </summary>
/// <remarks>
/// <para>
/// <b>Identity (ADR-048 rule 2 / ADR-034 path C):</b> a resolved person carries EXACTLY ONE of
/// <c>sprk_systemuser</c> XOR <c>sprk_contact</c> plus <c>sprk_isresolved=true</c>; the raw address is always
/// retained in <c>sprk_addresstext</c> as provenance. An address that cannot be resolved writes a first-class
/// row with BOTH person lookups null, <c>sprk_isresolved=false</c>, and <c>sprk_addresstext</c> = the raw
/// address (Q-D) — never silently skipped, so <c>participant=</c> (task 051) still surfaces external parties
/// and the row is back-fillable when a contact is later created.
/// </para>
/// <para>
/// <b>Reuse, not a new resolver (ADR-048 rule 5 / §11):</b> email→person resolution REUSES the existing
/// <see cref="ICommunicationDataverseService.QueryContactByEmailAsync"/> that <c>ParticipantCorrelationRung</c>
/// already uses. No second resolver, no new AI dependency, no new NuGet.
/// </para>
/// <para>
/// <b>Best-effort / non-fatal (NFR-02 / ADR-048 rule 5):</b> the <c>sprk_communication</c> record is the
/// source of truth; participant rows are a derived index. <see cref="WriteParticipantsAsync"/> NEVER throws —
/// a write (or the schema not yet being applied live) is logged and swallowed, so it can never fail an
/// outbound send or drop an inbound-captured message.
/// </para>
/// <para>
/// <b>Idempotent per message:</b> before creating rows it loads the existing (role, address) keys for the
/// message and skips any already present, so re-processing the same message (inbound echo/retry) creates NO
/// duplicate rows. A partial write self-heals on the next pass (already-written rows are skipped).
/// </para>
/// </remarks>
public sealed class CommunicationParticipantIndexer
{
    private const string EntityName = "sprk_communicationparticipant";

    // sprk_role choice integers (task 003 schema / ADR-048 rule 4).
    private const int RoleFrom = 100000000;
    private const int RoleTo = 100000001;
    private const int RoleCc = 100000002;
    private const int RoleBcc = 100000003;

    private const string SystemUserLogicalName = "systemuser";
    private const string ContactLogicalName = "contact";

    private readonly ICommunicationDataverseService _resolver;
    private readonly IGenericEntityService _genericEntityService;
    private readonly ILogger<CommunicationParticipantIndexer> _logger;

    public CommunicationParticipantIndexer(
        ICommunicationDataverseService resolver,
        IGenericEntityService genericEntityService,
        ILogger<CommunicationParticipantIndexer> logger)
    {
        _resolver = resolver;
        _genericEntityService = genericEntityService;
        _logger = logger;
    }

    /// <summary>
    /// Writes the participant rows for a persisted message. Best-effort / non-fatal (never throws) and
    /// idempotent per message.
    /// </summary>
    public async Task WriteParticipantsAsync(
        Guid communicationId, CommunicationParticipantSet participants, CancellationToken ct)
    {
        try
        {
            // 1. Build the desired (role, address) set, deduped within this call, in a stable order.
            var desired = BuildDesiredRows(participants);
            if (desired.Count == 0)
                return;

            // 2. Idempotency: load the (role, address) keys already present for this message. A partial prior
            //    write self-heals — already-written rows are skipped, missing ones are added.
            var seen = await LoadExistingKeysAsync(communicationId, ct);

            var written = 0;
            foreach (var (role, address, preResolved) in desired)
            {
                if (!seen.Add(NormalizeKey(role, address)))
                    continue; // already present (an existing row, or a duplicate within this call)

                // 3. Resolve identity. Prefer a caller-supplied typed reference (e.g. the ACS message sender
                //    already resolved to a systemuser); otherwise REUSE the email→contact resolver. An address
                //    with no '@' (e.g. an ACS MRI) cannot match a contact by email — skip the lookup and write
                //    an unresolved row (Q-D).
                var resolved = preResolved;
                if (resolved is null && LooksLikeEmail(address))
                {
                    var contact = await _resolver.QueryContactByEmailAsync(address, ct);
                    if (contact is not null)
                        resolved = ParticipantReference.Contact(contact.Id);
                }

                await _genericEntityService.CreateAsync(BuildRow(communicationId, role, address, resolved), ct);
                written++;
            }

            if (written > 0)
                _logger.LogInformation(
                    "Wrote {Written} communication participant row(s) | CommunicationId: {CommunicationId}",
                    written, communicationId);
        }
        catch (Exception ex)
        {
            // Best-effort / non-fatal (NFR-02 / ADR-048): the sprk_communication record is the source of truth;
            // a participant-index write failure (incl. the junction schema not yet applied live) MUST NOT fail
            // the send or drop the captured message. Logged and swallowed.
            _logger.LogWarning(
                ex,
                "Participant-index write failed (non-fatal) | CommunicationId: {CommunicationId}",
                communicationId);
        }
    }

    /// <summary>
    /// Flattens the address set into deduped (role, address, pre-resolved-identity) rows in From→To→Cc→Bcc
    /// order. Dedup key is (role, address-lowercased) so the same address in two roles yields two rows but the
    /// same address twice in one role yields one.
    /// </summary>
    private static List<(int Role, string Address, ParticipantReference? PreResolved)> BuildDesiredRows(
        CommunicationParticipantSet p)
    {
        var seen = new HashSet<(int, string)>();
        var rows = new List<(int, string, ParticipantReference?)>();

        void Add(int role, string? address, ParticipantReference? pre)
        {
            if (string.IsNullOrWhiteSpace(address))
                return;
            var trimmed = address.Trim();
            if (seen.Add((role, trimmed.ToLowerInvariant())))
                rows.Add((role, trimmed, pre));
        }

        Add(RoleFrom, p.FromAddress, p.FromResolved);
        foreach (var a in p.To ?? Array.Empty<string>()) Add(RoleTo, a, null);
        foreach (var a in p.Cc ?? Array.Empty<string>()) Add(RoleCc, a, null);
        foreach (var a in p.Bcc ?? Array.Empty<string>()) Add(RoleBcc, a, null);

        return rows;
    }

    /// <summary>
    /// Loads the (role, address) keys of the participant rows already written for this message, so the write
    /// is idempotent (re-processing creates no duplicates).
    /// </summary>
    private async Task<HashSet<(int, string)>> LoadExistingKeysAsync(Guid communicationId, CancellationToken ct)
    {
        var keys = new HashSet<(int, string)>();

        var query = new QueryExpression(EntityName)
        {
            ColumnSet = new ColumnSet("sprk_role", "sprk_addresstext"),
            Criteria =
            {
                Conditions = { new ConditionExpression("sprk_communication", ConditionOperator.Equal, communicationId) },
            },
        };

        var result = await _genericEntityService.RetrieveMultipleAsync(query, ct);
        foreach (var e in result.Entities)
        {
            var role = e.GetAttributeValue<OptionSetValue>("sprk_role")?.Value ?? -1;
            var address = e.GetAttributeValue<string>("sprk_addresstext");
            if (role >= 0 && !string.IsNullOrWhiteSpace(address))
                keys.Add(NormalizeKey(role, address));
        }

        return keys;
    }

    /// <summary>
    /// Builds one junction row. A resolved person sets EXACTLY ONE typed lookup (systemuser XOR contact) +
    /// <c>sprk_isresolved=true</c>; an unresolved address sets neither + <c>sprk_isresolved=false</c>. The raw
    /// address is always retained in <c>sprk_addresstext</c>.
    /// </summary>
    private static DataverseEntity BuildRow(
        Guid communicationId, int role, string address, ParticipantReference? resolved)
    {
        var row = new DataverseEntity(EntityName)
        {
            ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
            ["sprk_role"] = new OptionSetValue(role),
            ["sprk_addresstext"] = TruncateTo(address, 400),
            ["sprk_name"] = TruncateTo($"{address} — {RoleLabel(role)}", 400),
            ["sprk_isresolved"] = resolved is not null,
        };

        // XOR — set EXACTLY ONE typed person lookup for a resolved person (ADR-048 rule 2 / ADR-034 path C).
        if (resolved is not null)
        {
            if (string.Equals(resolved.EntityLogicalName, SystemUserLogicalName, StringComparison.OrdinalIgnoreCase))
                row["sprk_systemuser"] = new EntityReference(SystemUserLogicalName, resolved.RecordId);
            else if (string.Equals(resolved.EntityLogicalName, ContactLogicalName, StringComparison.OrdinalIgnoreCase))
                row["sprk_contact"] = new EntityReference(ContactLogicalName, resolved.RecordId);
        }

        return row;
    }

    private static (int, string) NormalizeKey(int role, string address)
        => (role, address.Trim().ToLowerInvariant());

    private static bool LooksLikeEmail(string address)
        => address.Contains('@');

    private static string RoleLabel(int role) => role switch
    {
        RoleFrom => "From",
        RoleTo => "To",
        RoleCc => "Cc",
        RoleBcc => "Bcc",
        _ => "Participant",
    };

    private static string TruncateTo(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
