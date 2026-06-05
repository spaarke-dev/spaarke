using System.Globalization;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Insights.LiveFacts;

/// <summary>
/// Production <see cref="ILiveFactResolver"/> for the <c>invoice:</c> subject scheme. Reads
/// <c>sprk_invoice</c> via <see cref="IGenericEntityService"/> and returns a deterministic
/// <see cref="FactArtifact"/> for each invoice-scoped predicate.
/// </summary>
/// <remarks>
/// <para>
/// <b>r2 Wave D5 (task 034) — NEW</b> per design-a6 §6.3. One of three per-entity
/// resolvers registered in <c>IReadOnlyDictionary&lt;string, ILiveFactResolver&gt;</c>
/// keyed by entity-type name.
/// </para>
/// <para>
/// <b>Zone B placement</b> per SPEC §3.5 — lives under <c>Services/Insights/LiveFacts/</c>
/// and consumes <see cref="IGenericEntityService"/> only. ZERO AI-internal imports.
/// </para>
/// <para>
/// <b>Supported predicates (Phase 1.5 initial set per design-a6 §6.3)</b>. The actual
/// <c>sprk_invoice</c> attribute names below are placeholder mappings pending D5-Q2 SME
/// confirmation against the Spaarke Dev <c>sprk_invoice</c> schema; the resolver dispatch
/// logic + shape is the load-bearing part:
/// <list type="bullet">
///   <item><c>invoiceNumber</c> → <c>sprk_invoicenumber</c> (plain string)</item>
///   <item><c>invoiceTotal</c> → <c>sprk_total</c> (decimal/Money; USD by default)</item>
///   <item><c>invoiceStatus</c> → <c>sprk_status</c> (option set → display name string)</item>
///   <item><c>relatedMatter</c> → <c>sprk_matter</c> (LOOKUP → sprk_matter; <c>{id, name}</c>
///   per A6-D7 — NO recursion into matter resolver)</item>
///   <item><c>currentInvoiceFacts</c> → composite shape returning the above sub-values</item>
/// </list>
/// Any other predicate throws <see cref="LiveFactNotSupportedException"/>.
/// </para>
/// <para>
/// <b>Behavior parity with matter resolver</b>: confidence = 1.0; null-on-missing-row;
/// <see cref="LiveFactNotSupportedException"/> on unsupported predicate;
/// <c>producedBy.id = "dataverse://sprk_invoice"</c>.
/// </para>
/// <para>
/// <b>Inter-entity references</b> (A6-D7): <c>relatedMatter</c> returns an
/// <c>EntityReference</c>-shaped <c>{id, name}</c> only. If a playbook needs facts from
/// BOTH the invoice AND its related matter, it composes a second <c>LiveFactNode</c> with
/// <c>subject = "matter:&lt;related-matter-guid&gt;"</c>. This keeps each resolver simple
/// and aligned with the matter resolver's pattern.
/// </para>
/// </remarks>
public sealed class InvoiceLiveFactResolver : ILiveFactResolver
{
    /// <summary>Logical name of the Dataverse entity this resolver reads.</summary>
    internal const string InvoiceEntityName = "sprk_invoice";

    /// <summary>The subject scheme prefix this resolver supports.</summary>
    internal const string InvoiceSubjectScheme = "invoice:";

    /// <summary>ProducedBy.Id for all FactArtifacts emitted by this resolver.</summary>
    internal const string ProducerId = "dataverse://sprk_invoice";

    /// <summary>ProducedBy.Version for all FactArtifacts.</summary>
    internal const string ProducerVersion = "v1";

    /// <summary>
    /// Fields the resolver pulls from sprk_invoice to satisfy the Phase 1.5 initial predicate
    /// set. Pending D5-Q2 SME confirmation of attribute names.
    /// </summary>
    private static readonly string[] ReadColumns =
    [
        "sprk_invoiceid",
        "sprk_invoicenumber",
        "sprk_total",
        "sprk_status",
        "sprk_matter"
    ];

    // Predicate names — Phase 1.5 initial set per design-a6 §6.3.
    private const string PredicateInvoiceNumber = "invoiceNumber";
    private const string PredicateInvoiceTotal = "invoiceTotal";
    private const string PredicateInvoiceStatus = "invoiceStatus";
    private const string PredicateRelatedMatter = "relatedMatter";
    private const string PredicateCurrentInvoiceFacts = "currentInvoiceFacts";

    private readonly IGenericEntityService _entityService;
    private readonly ILogger<InvoiceLiveFactResolver> _logger;

    public InvoiceLiveFactResolver(
        IGenericEntityService entityService,
        ILogger<InvoiceLiveFactResolver> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<FactArtifact?> ResolveAsync(
        string subject,
        string predicate,
        string tenantId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(predicate);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var invoiceId = ParseInvoiceSubject(subject);
        if (invoiceId is null)
        {
            throw new LiveFactNotSupportedException(subject, predicate);
        }

        Entity? invoice;
        try
        {
            invoice = await _entityService.RetrieveAsync(
                InvoiceEntityName,
                invoiceId.Value,
                ReadColumns,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            && (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogDebug(
                "InvoiceLiveFactResolver: invoice {InvoiceId} not found in Dataverse; returning null",
                invoiceId);
            return null;
        }

        if (invoice is null)
        {
            return null;
        }

        return predicate switch
        {
            PredicateInvoiceNumber => BuildStringFact(invoice, subject, predicate, "sprk_invoicenumber", tenantId),
            PredicateInvoiceTotal => BuildNumericFact(invoice, subject, predicate, "sprk_total", tenantId),
            PredicateInvoiceStatus => BuildOptionSetFact(invoice, subject, predicate, "sprk_status", tenantId),
            PredicateRelatedMatter => BuildLookupFact(invoice, subject, predicate, "sprk_matter", tenantId),
            PredicateCurrentInvoiceFacts => BuildCompositeFact(invoice, subject, predicate, tenantId),
            _ => throw new LiveFactNotSupportedException(subject, predicate)
        };
    }

    /// <summary>
    /// Parses <c>invoice:{guid}</c>. Returns null on any format violation.
    /// </summary>
    internal static Guid? ParseInvoiceSubject(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return null;
        if (!subject.StartsWith(InvoiceSubjectScheme, StringComparison.OrdinalIgnoreCase)) return null;

        var suffix = subject.AsSpan(InvoiceSubjectScheme.Length).Trim();
        return Guid.TryParse(suffix, out var id) && id != Guid.Empty ? id : null;
    }

    private FactArtifact? BuildStringFact(
        Entity invoice,
        string subject,
        string predicate,
        string fieldName,
        string tenantId)
    {
        var value = invoice.GetAttributeValue<string>(fieldName);
        if (string.IsNullOrWhiteSpace(value))
        {
            _logger.LogDebug(
                "InvoiceLiveFactResolver: invoice {InvoiceId} has no value for field {Field}; returning null for predicate {Predicate}",
                invoice.Id, fieldName, predicate);
            return null;
        }

        var valueRaw = JsonSerializer.SerializeToElement(value);
        return BuildFact(subject, predicate, valueRaw, "text", tenantId, invoice.Id);
    }

    /// <summary>
    /// Build a numeric (Money / decimal / double) fact. Returns the value as a JSON number;
    /// the synthesis prompt formats it according to invoice context (USD assumed per
    /// design-a6 §6.3; currency override is Phase 2).
    /// </summary>
    private FactArtifact? BuildNumericFact(
        Entity invoice,
        string subject,
        string predicate,
        string fieldName,
        string tenantId)
    {
        if (!invoice.Contains(fieldName))
        {
            _logger.LogDebug(
                "InvoiceLiveFactResolver: invoice {InvoiceId} has no value for numeric field {Field}; returning null for predicate {Predicate}",
                invoice.Id, fieldName, predicate);
            return null;
        }

        var raw = invoice[fieldName];
        decimal? numericValue = raw switch
        {
            Money money => money.Value,
            decimal dec => dec,
            double dbl => (decimal)dbl,
            float flt => (decimal)flt,
            int i => i,
            long l => l,
            null => null,
            _ => null
        };

        if (numericValue is null)
        {
            _logger.LogDebug(
                "InvoiceLiveFactResolver: invoice {InvoiceId} field {Field} has unexpected type {Type}; returning null",
                invoice.Id, fieldName, raw?.GetType().FullName ?? "null");
            return null;
        }

        // Serialize as a JSON number (no quotes). Use invariant culture so the value emits
        // as e.g. 12500.00, not 12500,00.
        var valueRaw = JsonSerializer.SerializeToElement(numericValue.Value);
        return BuildFact(subject, predicate, valueRaw, "currency-usd", tenantId, invoice.Id);
    }

    private FactArtifact? BuildOptionSetFact(
        Entity invoice,
        string subject,
        string predicate,
        string fieldName,
        string tenantId)
    {
        var optionSet = invoice.GetAttributeValue<OptionSetValue>(fieldName);
        if (optionSet is null)
        {
            _logger.LogDebug(
                "InvoiceLiveFactResolver: invoice {InvoiceId} has no value for option-set field {Field}; returning null for predicate {Predicate}",
                invoice.Id, fieldName, predicate);
            return null;
        }

        string displayValue;
        if (invoice.FormattedValues.TryGetValue(fieldName, out var formatted)
            && !string.IsNullOrWhiteSpace(formatted))
        {
            displayValue = formatted;
        }
        else
        {
            displayValue = optionSet.Value.ToString(CultureInfo.InvariantCulture);
        }

        var valueRaw = JsonSerializer.SerializeToElement(displayValue);
        return BuildFact(subject, predicate, valueRaw, "text", tenantId, invoice.Id);
    }

    /// <summary>
    /// Build a FactArtifact for a single lookup field. Returns the EntityReference shape
    /// <c>{id, name}</c> only — NO recursion into the referenced entity per A6-D7.
    /// </summary>
    private FactArtifact? BuildLookupFact(
        Entity invoice,
        string subject,
        string predicate,
        string fieldName,
        string tenantId)
    {
        var lookup = invoice.GetAttributeValue<EntityReference>(fieldName);
        if (lookup is null || lookup.Id == Guid.Empty)
        {
            _logger.LogDebug(
                "InvoiceLiveFactResolver: invoice {InvoiceId} has no value for field {Field}; returning null for predicate {Predicate}",
                invoice.Id, fieldName, predicate);
            return null;
        }

        var valueRaw = JsonSerializer.SerializeToElement(new
        {
            id = lookup.Id.ToString(),
            name = lookup.Name ?? string.Empty
        });
        return BuildFact(subject, predicate, valueRaw, "entity-reference", tenantId, invoice.Id);
    }

    private FactArtifact BuildCompositeFact(
        Entity invoice,
        string subject,
        string predicate,
        string tenantId)
    {
        var invoiceNumber = invoice.GetAttributeValue<string>("sprk_invoicenumber");

        decimal? invoiceTotal = null;
        if (invoice.Contains("sprk_total"))
        {
            invoiceTotal = invoice["sprk_total"] switch
            {
                Money money => money.Value,
                decimal dec => dec,
                double dbl => (decimal)dbl,
                int i => (decimal)i,
                long l => (decimal)l,
                _ => null
            };
        }

        var status = invoice.GetAttributeValue<OptionSetValue>("sprk_status");
        string? statusDisplay = null;
        if (status is not null)
        {
            statusDisplay = invoice.FormattedValues.TryGetValue("sprk_status", out var formatted)
                && !string.IsNullOrWhiteSpace(formatted)
                ? formatted
                : status.Value.ToString(CultureInfo.InvariantCulture);
        }

        var relatedMatter = invoice.GetAttributeValue<EntityReference>("sprk_matter");

        var composite = new
        {
            invoiceNumber = string.IsNullOrWhiteSpace(invoiceNumber) ? null : invoiceNumber,
            invoiceTotal = invoiceTotal,
            invoiceStatus = statusDisplay,
            relatedMatter = relatedMatter is null || relatedMatter.Id == Guid.Empty
                ? null
                : (object)new { id = relatedMatter.Id.ToString(), name = relatedMatter.Name ?? string.Empty }
        };

        var valueRaw = JsonSerializer.SerializeToElement(composite);
        return BuildFact(subject, predicate, valueRaw, "invoice-facts", tenantId, invoice.Id);
    }

    /// <summary>
    /// Compose the canonical FactArtifact envelope per design.md §2.1 + SPEC §3.4.1.
    /// </summary>
    /// <remarks>
    /// <b>Scope.MatterId is intentionally null</b> for invoice subjects per design-a6 §4.4.
    /// scope.entityType/entityId land in task 035 (Wave D6 index scope migration).
    /// </remarks>
    private static FactArtifact BuildFact(
        string subject,
        string predicate,
        JsonElement valueRaw,
        string displayHint,
        string tenantId,
        Guid invoiceId)
    {
        return new FactArtifact
        {
            Id = $"fact:{subject}:{predicate}",
            Subject = subject,
            Predicate = predicate,
            Value = new Value
            {
                Raw = valueRaw,
                DisplayHint = displayHint
            },
            Evidence = new[]
            {
                new EvidenceRef
                {
                    RefType = "fact-source",
                    Ref = $"dataverse://{InvoiceEntityName}/{invoiceId}#{predicate}"
                }
            },
            AsOf = DateTimeOffset.UtcNow,
            ProducedBy = new ProducedBy
            {
                Kind = "query",
                Id = ProducerId,
                Version = ProducerVersion
            },
            Scope = new Scope
            {
                TenantId = tenantId
                // MatterId intentionally null per design-a6 §4.4 (invoice subjects do not
                // populate scope.matterId). scope.entityType/entityId land in task 035.
            },
            TenantId = tenantId,
            Confidence = 1.0
        };
    }
}
