using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// The <b>Association Engine</b> (ADR-045 / FR-09). Resolves regarding associations for a
/// communication by evaluating an ordered set of <see cref="IAssociationRung"/> rungs over the
/// channel-neutral <see cref="NormalizedMessage"/> envelope — NEVER over
/// <c>Microsoft.Graph.Message</c>. The first rung (ascending <see cref="IAssociationRung.Order"/>)
/// that yields any <see cref="RungMatch"/> wins; its matches become the regarding writes.
///
/// <para>
/// Cascade (preserved from the pre-011 resolver): thread continuity (1) → participant correlation (2)
/// → subject reference (3). No match → <c>sprk_associationstatus = Pending Review</c>; a match →
/// <c>Resolved</c> plus the ADR-024 polymorphic resolver fields.
/// </para>
///
/// <para>
/// Non-fatal: association failure never prevents communication record creation (NFR-06). Each rung is
/// evaluated defensively — a rung that throws is treated as a non-match. Registered as a concrete type
/// in AddCommunicationModule() per ADR-010. The class name is retained (rather than "Engine") to keep
/// the DI registration + the inbound processor's dependency stable through task 011.
/// </para>
/// </summary>
public sealed class IncomingAssociationResolver
{
    private readonly IGenericEntityService _genericEntityService;
    private readonly ICommunicationDataverseService _communicationService;
    private readonly ILogger<IncomingAssociationResolver> _logger;

    /// <summary>Ordered rungs (ascending Order) evaluated over the envelope.</summary>
    private readonly IReadOnlyList<IAssociationRung> _rungs;

    /// <summary>Association status: Resolved.</summary>
    private const int AssociationStatusResolved = 100000000;

    /// <summary>Association status: Pending Review.</summary>
    private const int AssociationStatusPendingReview = 100000001;

    public IncomingAssociationResolver(
        ICommunicationDataverseService communicationService,
        IGenericEntityService genericEntityService,
        ILogger<IncomingAssociationResolver> logger)
    {
        _communicationService = communicationService;
        _genericEntityService = genericEntityService;
        _logger = logger;

        // Task-011 rung adapters wrapping the current inline logic. Composed internally to keep DI
        // minimal (ADR-010); tasks 012–014 supply real rung content against IAssociationRung.
        _rungs =
        [
            new ThreadContinuityRung(communicationService),
            new ParticipantCorrelationRung(communicationService),
            new SubjectReferenceRung(communicationService),
        ];
        _rungs = _rungs.OrderBy(r => r.Order).ToArray();
    }

    /// <summary>
    /// Resolves associations for a communication over the normalized envelope. Iterates rungs in
    /// order and applies the matches of the first rung that yields any; otherwise flags Pending Review.
    /// </summary>
    /// <param name="communicationId">The <c>sprk_communication</c> record to update.</param>
    /// <param name="message">The normalized envelope (channel-neutral).</param>
    /// <param name="context">Ambient association context (mailbox account, etc.).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ResolveAsync(
        Guid communicationId,
        NormalizedMessage message,
        AssociationContext context,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Starting association resolution for communication {CommunicationId} | Direction: {Direction}",
            communicationId, message.Direction);

        foreach (var rung in _rungs)
        {
            IReadOnlyList<RungMatch> matches;
            try
            {
                matches = await rung.EvaluateAsync(message, context, ct);
            }
            catch (Exception ex)
            {
                // A rung failure is non-fatal (NFR-06): treat as a non-match and continue the cascade.
                _logger.LogWarning(ex,
                    "Association rung {Rung} (order {Order}) failed for communication {CommunicationId}; skipping",
                    rung.Kind, rung.Order, communicationId);
                continue;
            }

            if (matches.Count == 0)
                continue;

            var fields = new Dictionary<string, object>();
            foreach (var match in matches)
            {
                fields[match.RegardingFieldName] = match.Target;
            }

            _logger.LogInformation(
                "Association resolved via rung {Rung} (order {Order}) for communication {CommunicationId} | Fields: {Fields}",
                matches[0].Rung, rung.Order, communicationId, string.Join(", ", fields.Keys));

            await ApplyAssociationAsync(communicationId, fields, AssociationStatusResolved, ct);
            return;
        }

        // No rung matched: flag as Pending Review (surfaces for manual review; no default-matter fallback).
        _logger.LogInformation(
            "No association found for communication {CommunicationId}. Setting status to Pending Review.",
            communicationId);

        await ApplyAssociationAsync(communicationId, new Dictionary<string, object>(), AssociationStatusPendingReview, ct);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Apply resolved associations to the communication record
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Updates the sprk_communication record with resolved association fields and status.
    /// Also populates the Polymorphic Resolver fields (ADR-024) for cross-entity views.
    /// </summary>
    private async Task ApplyAssociationAsync(
        Guid communicationId,
        Dictionary<string, object> fields,
        int associationStatus,
        CancellationToken ct)
    {
        fields["sprk_associationstatus"] = new OptionSetValue(associationStatus);

        // Populate polymorphic resolver fields from the primary regarding entity
        if (associationStatus == AssociationStatusResolved)
        {
            await PopulateResolverFieldsAsync(fields, ct);
        }

        await _genericEntityService.UpdateAsync("sprk_communication", communicationId, fields, ct);

        _logger.LogDebug(
            "Applied association to communication {CommunicationId} | Status: {Status}, FieldCount: {FieldCount}",
            communicationId, associationStatus == AssociationStatusResolved ? "Resolved" : "PendingReview",
            fields.Count);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Polymorphic Resolver (ADR-024)
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Entity-specific regarding fields in priority order for determining the primary record.
    /// Business entities first (matter, project, etc.), then people/orgs.
    /// </summary>
    private static readonly (string FieldName, string EntityLogicalName)[] RegardingFieldPriority =
    [
        ("sprk_regardingmatter", "sprk_matter"),
        ("sprk_regardingproject", "sprk_project"),
        ("sprk_regardinginvoice", "sprk_invoice"),
        ("sprk_regardingservicerequest", "sprk_servicerequest"),
        ("sprk_regardingworkassignment", "sprk_workassignment"),
        ("sprk_regardingevent", "sprk_event"),
        ("sprk_regardingbudget", "sprk_budget"),
        ("sprk_regardinganalysis", "sprk_analysis"),
        ("sprk_regardingorganization", "sprk_organization"),
        ("sprk_regardingaccount", "account"),
        ("sprk_regardingperson", "contact"),
    ];

    /// <summary>
    /// In-memory cache for sprk_recordtype_ref lookups (entity logical name → GUID + display name).
    /// Populated lazily, lives for the lifetime of the singleton service.
    /// </summary>
    private readonly Dictionary<string, (Guid Id, string DisplayName)?> _recordTypeRefCache = new();

    /// <summary>
    /// Populates the 4 denormalized resolver fields based on the highest-priority
    /// entity-specific regarding field that was set.
    ///
    /// Fields set:
    ///   - sprk_regardingrecordtype  (Lookup → sprk_recordtype_ref)
    ///   - sprk_regardingrecordid    (Text — parent GUID)
    ///   - sprk_regardingrecordname  (Text — parent display name)
    ///   - sprk_regardingrecordurl   (URL — clickable link to parent record)
    /// </summary>
    private async Task PopulateResolverFieldsAsync(
        Dictionary<string, object> fields,
        CancellationToken ct)
    {
        // Find the primary regarding entity (highest priority field that was set)
        EntityReference? primaryRef = null;
        string? primaryEntityLogicalName = null;

        foreach (var (fieldName, entityLogicalName) in RegardingFieldPriority)
        {
            if (fields.TryGetValue(fieldName, out var value) && value is EntityReference entityRef)
            {
                primaryRef = entityRef;
                primaryEntityLogicalName = entityLogicalName;
                break;
            }
        }

        if (primaryRef is null || primaryEntityLogicalName is null)
            return;

        try
        {
            // Set sprk_regardingrecordid (GUID as text)
            var cleanId = primaryRef.Id.ToString("D").ToLowerInvariant();
            fields["sprk_regardingrecordid"] = cleanId;

            // Set sprk_regardingrecordname (display name from the EntityReference or retrieve)
            var recordName = primaryRef.Name;
            if (string.IsNullOrEmpty(recordName))
            {
                // EntityReference.Name may not always be populated; try to retrieve it
                try
                {
                    var nameField = GetPrimaryNameField(primaryEntityLogicalName);
                    if (nameField is not null)
                    {
                        var record = await _genericEntityService.RetrieveAsync(
                            primaryEntityLogicalName, primaryRef.Id, [nameField], ct);
                        recordName = record.GetAttributeValue<string>(nameField) ?? "";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not retrieve name for {Entity} {Id}",
                        primaryEntityLogicalName, primaryRef.Id);
                }
            }
            fields["sprk_regardingrecordname"] = recordName ?? "";

            // Set sprk_regardingrecordurl
            fields["sprk_regardingrecordurl"] = BuildRecordUrl(primaryEntityLogicalName, cleanId);

            // Set sprk_regardingrecordtype (Lookup to sprk_recordtype_ref)
            var recordTypeRef = await ResolveRecordTypeRefAsync(primaryEntityLogicalName, ct);
            if (recordTypeRef.HasValue)
            {
                fields["sprk_regardingrecordtype"] = new EntityReference(
                    "sprk_recordtype_ref", recordTypeRef.Value.Id)
                {
                    Name = recordTypeRef.Value.DisplayName
                };
            }

            _logger.LogDebug(
                "Populated resolver fields: Entity={Entity}, Name={Name}, RecordType={RecordType}",
                primaryEntityLogicalName, recordName ?? "(unknown)",
                recordTypeRef?.DisplayName ?? "(not found)");
        }
        catch (Exception ex)
        {
            // Non-fatal — resolver fields are for display, not critical data
            _logger.LogWarning(ex, "Failed to populate resolver fields for {Entity}", primaryEntityLogicalName);
        }
    }

    /// <summary>
    /// Resolve the sprk_recordtype_ref GUID for an entity logical name. Cached.
    /// </summary>
    private async Task<(Guid Id, string DisplayName)?> ResolveRecordTypeRefAsync(
        string entityLogicalName, CancellationToken ct)
    {
        if (_recordTypeRefCache.TryGetValue(entityLogicalName, out var cached))
            return cached;

        var record = await _communicationService.QueryRecordTypeRefAsync(entityLogicalName, ct);
        if (record is not null)
        {
            var entry = (
                Id: record.Id,
                DisplayName: record.GetAttributeValue<string>("sprk_recorddisplayname") ?? entityLogicalName
            );
            _recordTypeRefCache[entityLogicalName] = entry;
            return entry;
        }

        _recordTypeRefCache[entityLogicalName] = null;
        return null;
    }

    /// <summary>
    /// Build a Dataverse record URL for the resolver.
    /// Uses the Dataverse environment base URL from the service client connection.
    /// </summary>
    private static string BuildRecordUrl(string entityLogicalName, string recordId)
    {
        // On the server side we don't have Xrm context, but we know the org URL
        // from the service client. Use a relative URL that works in model-driven apps.
        return $"/main.aspx?pagetype=entityrecord&etn={entityLogicalName}&id={recordId}";
    }

    /// <summary>
    /// Map entity logical name to its primary name attribute.
    /// </summary>
    private static string? GetPrimaryNameField(string entityLogicalName) => entityLogicalName switch
    {
        "sprk_matter" => "sprk_mattername",
        "sprk_project" => "sprk_projectname",
        "sprk_invoice" => "sprk_name",
        "sprk_event" => "sprk_eventname",
        "sprk_workassignment" => "sprk_name",
        "sprk_servicerequest" => "sprk_name",
        "sprk_budget" => "sprk_name",
        "sprk_analysis" => "sprk_name",
        "sprk_organization" => "sprk_name",
        "contact" => "fullname",
        "account" => "name",
        _ => null,
    };
}
