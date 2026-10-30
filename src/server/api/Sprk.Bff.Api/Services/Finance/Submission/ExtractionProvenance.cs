using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// EB-D-170 (task 101 trigger): the AI provenance the submission persister stores so that a reviewer's correction (D-64) can name the analyzer
/// or Action version that produced the value it replaces. Without it the calibration report (task 161) cannot be queried per version.
/// <para><b>Where it lives (no new column).</b> Per line, a <c>provenance</c> member of the line's <c>sprk_extractionconfidence</c> JSON:
/// <c>{ analyzerId, apiVersion, actionCode?, actionVersion? }</c> (the Action pair only when the line was coded). For the header, a
/// <c>provenance</c> member of the submission's <c>sprk_findingsjson</c>: the same pair plus <c>header</c>, the header fields as they were
/// READ (<c>{ value, confidence, combined }</c> each). The header values are kept because the invoice columns are overwritten by a
/// correction and a later correction of the same field must still name what the AI read.</para>
/// <para>Values are invariant text and never logged (ADR-015). A line or submission stored before EB-D-170 has no <c>provenance</c> member;
/// the parsers return null for it, and the correction service refuses an extracted-field correction there with a stable 409.</para>
/// </summary>
internal static class ExtractionProvenance
{
    /// <summary>The JSON member name, on a line's extraction JSON and on the submission's findings JSON.</summary>
    internal const string Member = "provenance";

    internal const string AnalyzerIdMember = "analyzerId";
    internal const string ApiVersionMember = "apiVersion";
    internal const string ActionCodeMember = "actionCode";
    internal const string ActionVersionMember = "actionVersion";
    internal const string HeaderMember = "header";

    /// <summary>The header fields a reviewer may correct, under the <see cref="BillingFieldNames"/> / resolution spellings.</summary>
    internal static readonly string[] HeaderFieldNames =
    [
        BillingFieldNames.FirmInvoiceNumber,
        BillingFieldNames.InvoiceDate,
        BillingFieldNames.BillingPeriodStart,
        BillingFieldNames.BillingPeriodEnd,
        BillingFieldNames.Currency,
        ResolutionFieldNames.TotalDue,
    ];

    /// <summary>The line-level pair: the analyzer and API version that read the line; the Action and version that coded it (null when not coded).</summary>
    internal static JsonObject ForLine(ExtractedInvoice read, bool coded)
    {
        var node = Source(read);
        AddAction(node, coded);
        return node;
    }

    /// <summary>The submission-level object: the pair, the Action pair when coded, and the header fields as read.</summary>
    internal static JsonObject ForSubmission(ExtractedInvoice read, bool coded, InvoiceResolutionResult? resolution)
    {
        var node = Source(read);
        AddAction(node, coded);
        var header = read.Header;
        var fields = new JsonObject
        {
            [BillingFieldNames.FirmInvoiceNumber] = Field(header.FirmInvoiceNumber.Value, header.FirmInvoiceNumber.Confidence, resolution, BillingFieldNames.FirmInvoiceNumber),
            [BillingFieldNames.InvoiceDate] = Field(Text(header.InvoiceDate.Value), header.InvoiceDate.Confidence, resolution, BillingFieldNames.InvoiceDate),
            [BillingFieldNames.BillingPeriodStart] = Field(Text(header.BillingPeriodStart.Value), header.BillingPeriodStart.Confidence, resolution, BillingFieldNames.BillingPeriodStart),
            [BillingFieldNames.BillingPeriodEnd] = Field(Text(header.BillingPeriodEnd.Value), header.BillingPeriodEnd.Confidence, resolution, BillingFieldNames.BillingPeriodEnd),
            [BillingFieldNames.Currency] = Field(header.Currency.Value, header.Currency.Confidence, resolution, BillingFieldNames.Currency),
            [ResolutionFieldNames.TotalDue] = Field(Text(header.TotalDue.Value), header.TotalDue.Confidence, resolution, ResolutionFieldNames.TotalDue),
        };
        node[HeaderMember] = fields;
        return node;
    }

    private static JsonObject Source(ExtractedInvoice read) => new()
    {
        [AnalyzerIdMember] = read.AnalyzerId,
        [ApiVersionMember] = read.ApiVersion,
    };

    private static void AddAction(JsonObject node, bool coded)
    {
        if (!coded)
        {
            return;
        }

        node[ActionCodeMember] = LineCodingService.ActionCode;
        node[ActionVersionMember] = LineCodingService.ActionVersion;
    }

    private static JsonObject Field(string? value, decimal? confidence, InvoiceResolutionResult? resolution, string name) => new()
    {
        ["value"] = value,
        ["confidence"] = confidence,
        ["combined"] = resolution is not null && resolution.HeaderFields.TryGetValue(name, out var combined) ? combined.Combined : null,
    };

    /// <summary>A date as <c>yyyy-MM-dd</c>, an amount as <c>0.00</c>; the one spelling the correction rows use for these.</summary>
    internal static string? Text(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Text(DateOnly?)"/>
    internal static string? Text(decimal? value) => value?.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>One line's stored provenance. Null members were not stored (a line that was not coded has no Action pair).</summary>
    internal sealed record LineProvenance(string? AnalyzerId, string? ApiVersion, string? ActionCode, int? ActionVersion)
    {
        /// <summary>True when the analyzer pair is there: an extracted-field correction can name its source version.</summary>
        internal bool HasExtraction => !string.IsNullOrWhiteSpace(AnalyzerId) && !string.IsNullOrWhiteSpace(ApiVersion);
    }

    /// <summary>One header field as read: the value, the analyzer's confidence and 046's combined confidence.</summary>
    internal sealed record HeaderField(string? Value, decimal? Confidence, decimal? Combined);

    /// <summary>The submission's stored provenance: the pair and the header fields as read.</summary>
    internal sealed record SubmissionProvenance(
        string? AnalyzerId, string? ApiVersion, string? ActionCode, int? ActionVersion, IReadOnlyDictionary<string, HeaderField> Header)
    {
        internal bool HasExtraction => !string.IsNullOrWhiteSpace(AnalyzerId) && !string.IsNullOrWhiteSpace(ApiVersion);
    }

    /// <summary>The <c>provenance</c> member of a line's extraction JSON, or null when the column is empty, unreadable or has none (a pre-EB-D-170 line).</summary>
    internal static LineProvenance? ParseLine(string? extractionJson)
    {
        if (ReadMember(extractionJson) is not { } element)
        {
            return null;
        }

        return new LineProvenance(Text(element, AnalyzerIdMember), Text(element, ApiVersionMember), Text(element, ActionCodeMember), Int(element, ActionVersionMember));
    }

    /// <summary>The <c>provenance</c> member of the submission's findings JSON, or null when absent or unreadable.</summary>
    internal static SubmissionProvenance? ParseSubmission(string? findingsJson)
    {
        if (ReadMember(findingsJson) is not { } element)
        {
            return null;
        }

        var header = new Dictionary<string, HeaderField>(StringComparer.Ordinal);
        if (element.TryGetProperty(HeaderMember, out var fields) && fields.ValueKind == JsonValueKind.Object)
        {
            foreach (var field in fields.EnumerateObject().Where(f => f.Value.ValueKind == JsonValueKind.Object))
            {
                header[field.Name] = new HeaderField(Text(field.Value, "value"), Decimal(field.Value, "confidence"), Decimal(field.Value, "combined"));
            }
        }

        return new SubmissionProvenance(
            Text(element, AnalyzerIdMember), Text(element, ApiVersionMember), Text(element, ActionCodeMember), Int(element, ActionVersionMember), header);
    }

    private static JsonElement? ReadMember(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            // The document is cloned: the caller keeps the element after the parse buffer is gone.
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(Member, out var member)
                && member.ValueKind == JsonValueKind.Object
                    ? member.Clone()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    private static decimal? Decimal(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) ? number : null;
}
