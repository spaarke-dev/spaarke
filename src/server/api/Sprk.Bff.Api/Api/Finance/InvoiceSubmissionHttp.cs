using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// The HTTP shape both invoice submission channels share (ebilling-r1 task 044): the multipart form reader, the 202 / ProblemDetails mapping,
/// the OpenAPI contract and the body cap. It is a file of its own for the route-authorization scan (task 036 C): the external route file names
/// these members, and the scan follows every file a route file names, so they cannot live in <see cref="InvoiceSubmissionEndpoints"/>, whose
/// internal routes carry the Finance authorization filter the external surface must not reach. This file maps no route and names no filter.
/// </summary>
internal static class InvoiceSubmissionHttp
{
    /// <summary>
    /// The whole-request body cap: the largest configurable file (<see cref="EbillingOptions.MaxSubmissionFileSizeMbCeiling"/>) plus 1 MB of
    /// multipart envelope. Kestrel's default (about 30 MB) would otherwise refuse a configured 50 MB file before the handler; the configured
    /// limit itself is enforced by the handler (413 <see cref="InvoiceSubmissionErrors.TooLarge"/>).
    /// </summary>
    internal const long MaxRequestBodyBytes = (EbillingOptions.MaxSubmissionFileSizeMbCeiling + 1L) * 1024 * 1024;

    /// <summary>A read fault of the service (<see cref="InvoiceSubmissionService.ReadUnavailable"/>, 503) in this feature's problem shape.</summary>
    internal static IResult ReadFault(SdapProblemException ex) => Refused(ex.StatusCode, ex.Code, ex.Detail ?? ex.Title);

    /// <summary>The parsed multipart body, or the refusal to return instead.</summary>
    internal sealed record SubmissionForm(InvoiceSubmissionFile? File, string? FirmInvoiceNumber, string? PoNumber, IResult? Refusal);

    /// <summary>
    /// Reads the multipart body: <c>file</c>, <c>firmInvoiceNumber</c>, <c>poNumber</c>. A body larger than the configured limit is a 413
    /// before it is parsed; a malformed body is a 400 (the parser's message can echo request content, so it is not returned). Nothing is
    /// validated beyond size and shape here: the service validates the number (D-91), the PDF and the firm, in that order, before any write.
    /// <paramref name="statusUrl"/> is the calling channel's status route; the size refusals never reach it (they are not a 202), so it only keeps
    /// <see cref="ToResult"/>'s one contract for both channels.
    /// </summary>
    internal static async Task<SubmissionForm> ReadSubmissionFormAsync(
        HttpRequest request, InvoiceSubmissionService service, Func<Guid, string> statusUrl, CancellationToken ct)
    {
        var maxBytes = service.MaxFileBytes;
        if (request.ContentLength > maxBytes + (1024 * 1024)) // the multipart envelope adds a little; the file itself is checked below
        {
            return new SubmissionForm(null, null, null, ToResult(service.TooLarge(), statusUrl, request.HttpContext.TraceIdentifier));
        }

        if (!request.HasFormContentType)
        {
            return new SubmissionForm(null, null, null,
                Refused(StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.FileRequired, "The request must be multipart/form-data with a PDF file."));
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
        {
            return new SubmissionForm(null, null, null,
                Refused(StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.FileRequired, "The request body is not valid multipart/form-data."));
        }

        var number = form["firmInvoiceNumber"].ToString();
        var po = form["poNumber"].ToString();
        var part = form.Files.GetFile("file");
        if (part is null || part.Length == 0)
        {
            return new SubmissionForm(null, number, po, null);
        }

        if (part.Length > maxBytes)
        {
            return new SubmissionForm(null, number, po, ToResult(service.TooLarge(), statusUrl, request.HttpContext.TraceIdentifier));
        }

        byte[] bytes;
        await using (var stream = part.OpenReadStream())
        using (var buffer = new MemoryStream())
        {
            await stream.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }

        return new SubmissionForm(new InvoiceSubmissionFile(part.FileName, part.ContentType, bytes), number, po, null);
    }

    /// <summary>The OpenAPI contract of a submit route (documentation only; authorization is on each route's own chain).</summary>
    internal static RouteHandlerBuilder WithSubmitContract(this RouteHandlerBuilder builder, string coreNoun) => builder
        .Accepts<IFormFile>("multipart/form-data")
        .WithSummary($"Submit an invoice PDF against a {coreNoun}")
        .WithDescription(
            "Stores the PDF in the record's container, creates the document (type Invoice) and the submission (Received, with its " +
            "tracking number) and enqueues processing. Form: file (PDF), firmInvoiceNumber (required), poNumber. Query: firmId. " +
            "Refusals are ProblemDetails with errorCode: 400 not_pdf, file_required, firm_invoice_number_required, firm_invalid; " +
            "403 without the rights the route needs; 413 too_large; 503 ebilling.disabled.")
        .Produces<InvoiceSubmissionAccepted>(StatusCodes.Status202Accepted)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    /// <summary>202 with <c>Location</c> = the channel's status URL, or the refusal as ProblemDetails (ADR-019).</summary>
    internal static IResult ToResult(InvoiceSubmissionOutcome outcome, Func<Guid, string> statusUrl, string traceId) => outcome.Kind switch
    {
        InvoiceSubmissionOutcomeKind.Received => Accepted(outcome.Receipt!, statusUrl(outcome.Receipt!.SubmissionId)),
        InvoiceSubmissionOutcomeKind.OwnerRefused => ProblemDetailsHelper.RecordOwnerRefused(outcome.OwnerRefusal!, "invoice submission", traceId),
        _ => Refused(outcome.StatusCode, outcome.ErrorCode!, outcome.Detail!),
    };

    private static IResult Accepted(InvoiceSubmissionReceipt receipt, string statusUrl) =>
        Results.Accepted(statusUrl, new InvoiceSubmissionAccepted(receipt.SubmissionId, receipt.JobId, receipt.TrackingNumber, statusUrl));

    internal static IResult Refused(int statusCode, string errorCode, string detail) =>
        Results.Problem(
            statusCode: statusCode, detail: detail,
            extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode });
}
