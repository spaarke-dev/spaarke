using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Office;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Office;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>The from-email request as sent (camelCase, contract section 2). Every field is nullable so a missing one is a field-level 400.</summary>
public sealed record FromEmailSubmissionRequest
{
    public FromEmailTargetEntity? TargetEntity { get; init; }
    public EmailMetadata? Email { get; init; }
    public List<FromEmailMark>? Marks { get; init; }
    public Guid? FirmId { get; init; }
    public string? FirmInvoiceNumber { get; init; }
    public string? PoNumber { get; init; }
    public string? Mode { get; init; }
    public Guid? InvoiceId { get; init; }
}

/// <summary>The core record the submission (and the <c>.eml</c>) is filed to. <c>displayName</c> is ignored.</summary>
public sealed record FromEmailTargetEntity
{
    public string? EntityType { get; init; }
    public Guid? EntityId { get; init; }
}

/// <summary>
/// The bound body of <c>POST /api/finance/invoice-submissions/from-email</c> (ebilling-r1 task 095). Binding never fails: a body that is not
/// JSON for this route, or misses a field, is carried as <see cref="Errors"/> and refused by the route's Finance filter as the contract's
/// body-shape 400 (a ValidationProblem), AFTER step 0 and before any record is read. The filters (Finance, EntityAccessFilter, idempotency)
/// read the SAME instance the handler receives.
/// </summary>
public sealed class FromEmailSubmissionBody
{
    private FromEmailSubmissionBody(FromEmailSubmissionRequest? request, Dictionary<string, string[]> errors)
    {
        Errors = errors;
        if (errors.Count == 0 && request is not null)
        {
            Input = InvoiceSubmissionFromEmailHttp.ToInput(request);
        }
    }

    /// <summary>The field errors; empty when the body is well formed.</summary>
    public Dictionary<string, string[]> Errors { get; }

    /// <summary>The validated request; null whenever <see cref="Errors"/> is not empty.</summary>
    internal FromEmailSubmissionInput? Input { get; }

    /// <summary>The record <see cref="EntityAccessFilter"/> gates (the record the <c>.eml</c> is filed to), in the filter's spellings; null
    /// when the body is not valid (the route's Finance filter refuses it first).</summary>
    public SaveEntityReference? EntityAccessTarget =>
        Input is { } input ? new SaveEntityReference { EntityType = input.SaveEntityType, EntityId = input.CoreRecord.RecordId } : null;

    /// <summary>Minimal API custom binding: the app's JSON options (the same as <c>/office/save</c>'s), the body left re-readable for the
    /// idempotency filter's body key. A 413 (Kestrel's limit) propagates.</summary>
    public static async ValueTask<FromEmailSubmissionBody?> BindAsync(HttpContext http, ParameterInfo parameter)
    {
        var options = http.RequestServices.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions
            ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
        http.Request.EnableBuffering();
        FromEmailSubmissionRequest? request = null;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        try
        {
            request = await JsonSerializer.DeserializeAsync<FromEmailSubmissionRequest>(http.Request.Body, options, http.RequestAborted);
        }
        catch (JsonException ex)
        {
            errors[string.IsNullOrEmpty(ex.Path) ? "$" : ex.Path] = new[] { "The value is missing or not valid for this field." };
        }
        finally
        {
            if (http.Request.Body.CanSeek)
            {
                http.Request.Body.Position = 0;
            }
        }

        if (errors.Count == 0)
        {
            InvoiceSubmissionFromEmailHttp.Validate(request, errors);
        }

        return new FromEmailSubmissionBody(request, errors);
    }
}

/// <summary>
/// The from-email route's HTTP shape (ebilling-r1 task 095): body validation, the Finance filter's targets, the response mapping, and the
/// firms-for-a-record route (contract section 8). The routes are mapped in <see cref="InvoiceSubmissionEndpoints"/> (the governed file).
/// </summary>
internal static class InvoiceSubmissionFromEmailHttp
{
    /// <summary>The core-record spellings the route accepts (contract section 2: any case, <c>sprk_*</c> too), to the save's friendly type.</summary>
    private static readonly IReadOnlyDictionary<string, (InvoiceCoreRecordKind Kind, string SaveEntityType, string EntitySet)> CoreTypes =
        new Dictionary<string, (InvoiceCoreRecordKind, string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["matter"] = (InvoiceCoreRecordKind.Matter, "Matter", FinanceAuthorizationFilter.MatterEntitySet),
            ["sprk_matter"] = (InvoiceCoreRecordKind.Matter, "Matter", FinanceAuthorizationFilter.MatterEntitySet),
            ["project"] = (InvoiceCoreRecordKind.Project, "Project", FinanceAuthorizationFilter.ProjectEntitySet),
            ["sprk_project"] = (InvoiceCoreRecordKind.Project, "Project", FinanceAuthorizationFilter.ProjectEntitySet),
            ["workassignment"] = (InvoiceCoreRecordKind.WorkAssignment, "WorkAssignment", FinanceAuthorizationFilter.WorkAssignmentEntitySet),
            ["sprk_workassignment"] = (InvoiceCoreRecordKind.WorkAssignment, "WorkAssignment", FinanceAuthorizationFilter.WorkAssignmentEntitySet),
        };

    /// <summary>The firms route's path segment (contract section 8: <c>Matter</c>, <c>Project</c>, <c>WorkAssignment</c>, any case).</summary>
    internal static bool TryCoreType(string? entityType, out InvoiceCoreRecordKind kind, out string entitySet)
    {
        if (entityType is not null && CoreTypes.TryGetValue(entityType.Trim(), out var found))
        {
            (kind, _, entitySet) = found;
            return true;
        }

        kind = default;
        entitySet = string.Empty;
        return false;
    }

    /// <summary>The body-shape rules (contract section 6, the ValidationProblem row). Field paths are the JSON paths.</summary>
    internal static void Validate(FromEmailSubmissionRequest? request, Dictionary<string, string[]> errors)
    {
        if (request is null)
        {
            errors["$"] = new[] { "A request body is required." };
            return;
        }

        if (request.TargetEntity is null)
        {
            errors["targetEntity"] = new[] { "targetEntity is required." };
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.TargetEntity.EntityType) || !CoreTypes.ContainsKey(request.TargetEntity.EntityType.Trim()))
            {
                errors["targetEntity.entityType"] = new[] { "entityType must be Matter, Project or WorkAssignment." };
            }

            if (request.TargetEntity.EntityId is not { } id || id == Guid.Empty)
            {
                errors["targetEntity.entityId"] = new[] { "entityId is required." };
            }
        }

        if (request.Email is null)
        {
            errors["email"] = new[] { "email is required." };
        }
        else
        {
            // OP-2: the RFC 5322 Message-ID is required; an Exchange item id is not one, and Graph is never asked.
            if (!OfficeEmailEnricher.IsRfcMessageId(request.Email.InternetMessageId))
            {
                errors["email.internetMessageId"] = new[] { "internetMessageId must be the email's RFC 5322 Message-ID." };
            }

            if (request.Email.Attachments is null)
            {
                errors["email.attachments"] = new[] { "attachments is required." };
            }
            else
            {
                for (var i = 0; i < request.Email.Attachments.Count; i++)
                {
                    if (request.Email.Attachments[i] is null)
                    {
                        // 095 review F4: a null entry is the body-shape 400, never a fault in the marks rules.
                        errors[$"email.attachments[{i}]"] = new[] { "An attachment entry is required." };
                    }
                }
            }
        }

        if (request.Marks is null)
        {
            errors["marks"] = new[] { "marks is required." };
        }
        else
        {
            for (var i = 0; i < request.Marks.Count; i++)
            {
                var mark = request.Marks[i];
                if (mark is null)
                {
                    errors[$"marks[{i}]"] = new[] { "A mark is required." };
                    continue;
                }

                if (mark.FileName is null)
                {
                    errors[$"marks[{i}].fileName"] = new[] { "fileName is required." };
                }

                if (mark.Size is null)
                {
                    errors[$"marks[{i}].size"] = new[] { "size is required." };
                }

                if (mark.ContentType is null)
                {
                    errors[$"marks[{i}].contentType"] = new[] { "contentType is required." };
                }

                if (mark.Mark is null)
                {
                    errors[$"marks[{i}].mark"] = new[] { "mark is required." };
                }

                // 095 review F1: an attached message (message/*) is not a file part of the .eml (MIME nests it, and its bytes are
                // re-serialized), so it cannot be kept byte-for-byte. Refused when it is to be saved; a DontSave mark may name one.
                if (mark.Mark is FromEmailMarks.Invoice or FromEmailMarks.Supporting
                    && mark.ContentType?.TrimStart().StartsWith("message/", StringComparison.OrdinalIgnoreCase) == true)
                {
                    errors[$"marks[{i}].contentType"] = new[] { "An attached email (message/*) cannot be marked Invoice or Supporting." };
                }
            }
        }

        if (request.FirmId is null)
        {
            errors["firmId"] = new[] { "firmId is required." };
        }

        if (request.FirmInvoiceNumber?.Trim() is { Length: > TrackingNumberAllocator.MaxFirmInvoiceNumberLength })
        {
            errors["firmInvoiceNumber"] = new[] { $"firmInvoiceNumber may be at most {TrackingNumberAllocator.MaxFirmInvoiceNumberLength} characters." };
        }

        if (request.PoNumber?.Trim() is { Length: > InvoiceSubmissionService.MaxPoNumberLength })
        {
            errors["poNumber"] = new[] { $"poNumber may be at most {InvoiceSubmissionService.MaxPoNumberLength} characters." };
        }

        switch (request.Mode)
        {
            case FromEmailModes.New when request.InvoiceId is not null:
                errors["invoiceId"] = new[] { "invoiceId is not sent for mode New." };
                break;
            case FromEmailModes.Correction when request.InvoiceId is not { } invoiceId || invoiceId == Guid.Empty:
                errors["invoiceId"] = new[] { "invoiceId is required for mode Correction." };
                break;
            case FromEmailModes.New or FromEmailModes.Correction:
                break;
            default:
                errors["mode"] = new[] { "mode must be New or Correction." };
                break;
        }
    }

    /// <summary>The validated request (call only when <see cref="Validate"/> found nothing).</summary>
    internal static FromEmailSubmissionInput ToInput(FromEmailSubmissionRequest request)
    {
        var (kind, saveEntityType, _) = CoreTypes[request.TargetEntity!.EntityType!.Trim()];
        return new FromEmailSubmissionInput
        {
            CoreRecord = new InvoiceCoreRecord(kind, request.TargetEntity.EntityId!.Value),
            SaveEntityType = saveEntityType,
            Email = request.Email!,
            Marks = request.Marks!,
            FirmId = request.FirmId!.Value,
            FirmInvoiceNumber = request.FirmInvoiceNumber,
            PoNumber = request.PoNumber,
            Mode = request.Mode!,
            InvoiceId = request.InvoiceId,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Authorization declarations — read the bound body the handler receives
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The from-email targets, after step 0: a body-shape error is the ValidationProblem 400 with no rights query made; otherwise Read and
    /// AppendTo on the core record (<c>finance.submit_invoice</c>, as the internal submit route, D-24) and AppendTo on the firm
    /// (<c>finance.attach_invoice</c>). An empty firm id adds no firm check: the handler answers 400 firm_invalid.
    /// </summary>
    internal static FinanceAuthorizationTargets ResolveFromEmailTargets(EndpointFilterInvocationContext context)
    {
        if (context.Arguments.OfType<FromEmailSubmissionBody>().FirstOrDefault() is not { } body)
        {
            return FinanceAuthorizationTargets.Reject(ProblemDetailsHelper.ValidationProblem(
                new Dictionary<string, string[]> { ["$"] = new[] { "A request body is required." } }));
        }

        if (body.Errors.Count > 0 || body.Input is not { } input)
        {
            return FinanceAuthorizationTargets.Reject(ProblemDetailsHelper.ValidationProblem(body.Errors));
        }

        TryCoreType(input.CoreRecord.EntityLogicalName, out _, out var entitySet);
        var checks = new List<FinanceAuthorizationCheck>
        {
            new()
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = entitySet,
                RecordId = input.CoreRecord.RecordId,
                Operation = "finance.submit_invoice",
                Source = "body.targetEntity",
            },
        };

        if (input.FirmId != Guid.Empty)
        {
            checks.Add(new FinanceAuthorizationCheck
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = FinanceAuthorizationFilter.VendorOrganizationEntitySet,
                RecordId = input.FirmId,
                Operation = "finance.attach_invoice",
                Source = "body.firmId",
            });
        }

        return FinanceAuthorizationTargets.Authorize(checks.ToArray());
    }

    /// <summary>The firms route: Read on the path's core record (uniform 404); an entity type outside the three is a field-level 400.</summary>
    internal static FinanceAuthorizationTargets ResolveFirmsTargets(EndpointFilterInvocationContext context)
    {
        var route = context.HttpContext.Request.RouteValues;
        if (!TryCoreType(route["entityType"]?.ToString(), out _, out var entitySet))
        {
            return FinanceAuthorizationTargets.Reject(ProblemDetailsHelper.ValidationProblem(
                new Dictionary<string, string[]> { ["entityType"] = new[] { "entityType must be Matter, Project or WorkAssignment." } }));
        }

        if (!Guid.TryParse(route["entityId"]?.ToString(), out var id) || id == Guid.Empty)
        {
            return FinanceAuthorizationTargets.Authorize(); // no check: the filter denies
        }

        return FinanceAuthorizationTargets.Authorize(new FinanceAuthorizationCheck
        {
            Path = FinanceCheckPath.Record,
            EntitySetName = entitySet,
            RecordId = id,
            Operation = "finance.read",
            Source = "route.entityId",
        });
    }

    /// <summary>The idempotency filter's binding: the response cache of an <c>X-Idempotency-Key</c> is bound to K plus the firm details
    /// (contract section 4).</summary>
    internal static string? IdempotencyBinding(EndpointFilterInvocationContext context) =>
        context.Arguments.OfType<FromEmailSubmissionBody>().FirstOrDefault()?.Input is { } input
            ? FromEmailSubmissionKey.CacheBinding(input)
            : null;

    /// <summary>
    /// Only a request that carries <c>X-Idempotency-Key</c> is answered from (or written to) the response cache (contract section 4: the
    /// header names the cache). Without the header the filter would key the cache on the body, so an identical retry after a Failed
    /// attempt would get the failed attempt's cached 202 for 24 hours instead of the new attempt EB-D-160 grants (section 5). The
    /// handler's own replay (ids derived from K) answers a repeat either way; the in-flight lock still applies.
    /// </summary>
    internal static bool MayReplayResponse(EndpointFilterInvocationContext context) =>
        !string.IsNullOrEmpty(context.HttpContext.Request.Headers[IdempotencyKeyHeader].FirstOrDefault());

    private const string IdempotencyKeyHeader = "X-Idempotency-Key";

    // ═══════════════════════════════════════════════════════════════════════════
    // Handlers
    // ═══════════════════════════════════════════════════════════════════════════

    internal static async Task<IResult> SubmitFromEmailAsync(
        FromEmailSubmissionBody body,
        HttpContext http,
        FromEmailSubmissionService service,
        Spaarke.Core.Auth.AuthorizationService authorization,
        Spaarke.Dataverse.IGenericEntityService entities,
        ILogger<FromEmailSubmissionService> logger,
        CancellationToken ct)
    {
        if (body.Input is not { } input)
        {
            // The Finance filter refuses a malformed body first; reaching here without one is a wiring defect.
            return ProblemDetailsHelper.ValidationProblem(body.Errors.Count > 0
                ? body.Errors
                : new Dictionary<string, string[]> { ["$"] = new[] { "A request body is required." } });
        }

        var userId = http.Items[OfficeAuthFilter.UserIdKey] as string ?? CallerResolution.ResolveObjectId(http.User);
        if (string.IsNullOrEmpty(userId))
        {
            return ProblemDetailsHelper.OfficeAccessDenied(http.TraceIdentifier);
        }

        FromEmailSubmissionOutcome outcome;
        try
        {
            outcome = await service.SubmitAsync(input, userId, http, ct);
        }
        catch (FromEmailRouteAnswerException ex)
        {
            // Task 094: another route's own answer (the communication re-file's refusal in step A, the owner refusal): nothing submitted.
            // A reasonCode-only answer (the owner refusal) after step A filed the communication keeps its code field and says the filing is kept.
            return ex.FilingKept ? InvoiceSubmissionHttp.WithFilingKept(ex.Answer) : ex.Answer;
        }

        if (outcome.Receipt is { } receipt)
        {
            var statusUrl = InvoiceSubmissionEndpoints.StatusUrl(receipt.SubmissionId);
            return Results.Accepted(statusUrl, new InvoiceSubmissionFromEmailAccepted(
                receipt.SubmissionId, receipt.JobId, receipt.TrackingNumber, statusUrl, receipt.EmlDocumentId));
        }

        if (outcome.OfficeError is { } officeError)
        {
            // Contract section 6, the last row: the in-process save's own refusal, rendered exactly as /office/save renders it.
            var error = await OfficeEndpoints.WithholdCollisionIdentityIfUnauthorizedAsync(officeError, userId, http, authorization, logger, ct);
            var office = OfficeEndpoints.MapSaveErrorToProblem(error, http.TraceIdentifier);
            return outcome.FilingKept ? InvoiceSubmissionHttp.WithFilingKept(office) : office;
        }

        var extensions = new Dictionary<string, object?> { ["errorCode"] = outcome.ErrorCode };
        if (outcome.FilingKept)
        {
            extensions[InvoiceSubmissionHttp.FilingKeptField] = true;
        }

        foreach (var (key, value) in outcome.Extensions ?? new Dictionary<string, object?>())
        {
            extensions[key] = value;
        }

        // Task 192: the optional `filedUnder` of the "filed under another core record" 409, only for a record the caller can open.
        if (outcome.FiledUnder is { } candidate
            && await FiledUnderAsync(candidate, http, entities, authorization, logger, ct) is { } filedUnder)
        {
            extensions[FiledUnderField] = filedUnder;
        }

        return Results.Problem(statusCode: outcome.Status, detail: outcome.Detail, extensions: extensions);
    }

    /// <summary>The top-level field of the "filed under another core record" 409 (contract section 6; additive and optional to read).</summary>
    internal const string FiledUnderField = "filedUnder";

    /// <summary>
    /// <c>filedUnder</c> of the "filed under another core record" 409 (task 192): the record's type in the intake vocabulary, id and primary
    /// name, only when the caller has Read on it, checked as the caller through the Finance filter's own rights decision
    /// (<see cref="FinanceAuthorizationFilter.CallerHoldsRightsAsync"/>, operation <c>finance.read</c>, as <c>replacedBy</c> does). The record
    /// is read app-only for its name AFTER that check passes, so one the caller cannot open is never named or read. DELIBERATE: the field is
    /// optional, so a refusal OR a fault (the rights, the name, an unmapped type) leaves it out, a Warning for a fault; it never changes the
    /// 409's status, code or detail and is never a 503. The candidate is already a non-secure record (<see cref="FromEmailSubmissionOutcome.FiledUnder"/>).
    /// </summary>
    private static async Task<FromEmailFiledUnder?> FiledUnderAsync(
        InvoiceCoreRecord record,
        HttpContext http,
        Spaarke.Dataverse.IGenericEntityService entities,
        Spaarke.Core.Auth.AuthorizationService authorization,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var typeName = record.Kind switch
            {
                InvoiceCoreRecordKind.Matter => "Matter",
                InvoiceCoreRecordKind.Project => "Project",
                InvoiceCoreRecordKind.WorkAssignment => "WorkAssignment",
                _ => null,
            };
            if (typeName is null || CallerResolution.ResolveObjectId(http.User) is not { Length: > 0 } userId)
            {
                return null;
            }

            var mayRead = await FinanceAuthorizationFilter.CallerHoldsRightsAsync(
                authorization, userId, InvoiceSubmissionEndpoints.CoreEntitySet(record.Kind), record.RecordId, "finance.read",
                TokenHelper.ExtractBearerTokenOrNull(http), ct);
            if (!mayRead)
            {
                return null;
            }

            var name = await ReadNameAsync(entities, record.EntityLogicalName, record.RecordId, logger, ct, "From-email 409");
            return name is null ? null : new FromEmailFiledUnder(typeName, record.RecordId, name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "From-email 409: the record the email is filed under could not be checked or read; filedUnder is left out");
            return null;
        }
    }

    /// <summary>
    /// Contract section 8: the active firms linked to the record (D-106, the one definition <see cref="InvoiceSubmissionService.CheckFirmLinkAsync"/>
    /// reads), sorted by name (ordinal, ignore case) then id; <c>[]</c> when none. A read fault is 503, never a partial or empty list.
    /// </summary>
    internal static async Task<IResult> GetFirmsAsync(
        string entityType,
        Guid entityId,
        ExternalParticipationService participations,
        InvoiceSubmissionService submissions,
        ILogger<FromEmailSubmissionService> logger,
        CancellationToken ct)
    {
        if (!TryCoreType(entityType, out var kind, out _))
        {
            return ProblemDetailsHelper.ValidationProblem(
                new Dictionary<string, string[]> { ["entityType"] = new[] { "entityType must be Matter, Project or WorkAssignment." } });
        }

        var core = new InvoiceCoreRecord(kind, entityId);
        IReadOnlyCollection<Guid> linked;
        try
        {
            var references = await participations.GetReferencedOrganizationIdsAsync(core.EntityLogicalName, new[] { entityId }, ct);
            if (references is null || !references.TryGetValue(entityId, out var record) || record.Unreadable || record.OrganizationIds is null)
            {
                logger.LogWarning("The firms of {Entity} {RecordId} could not be read", core.EntityLogicalName, entityId);
                return FirmsUnavailable();
            }

            linked = record.OrganizationIds;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "The firms of {Entity} {RecordId} could not be read", core.EntityLogicalName, entityId);
            return FirmsUnavailable();
        }

        var named = await submissions.ReadActiveFirmNamesAsync(linked, ct);
        if (named is null)
        {
            return FirmsUnavailable();
        }

        return Results.Ok(named
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.FirmId)
            .Select(f => new CoreRecordFirm(f.FirmId, f.Name))
            .ToList());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GET /api/finance/invoices/{invoiceId}/correction-prefill (task 097e, contract section 10)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// What the Outlook add-in needs to prefill a correction of one invoice (contract section 10). The route's Finance filter has already put
    /// the caller's own Write on the invoice (uniform 404 without it, for an unknown invoice, at step 0 and on a rights fault). The invoice is
    /// then read app-only, and <c>canCorrect</c> is the intake's own guard (<see cref="InvoiceSubmissionService.JudgeCorrectionPrefillAsync"/>)
    /// over the invoice's own firm, record and number. Any read fault is 503, never a partial answer, except the OPTIONAL
    /// <c>replacedBy</c> (<see cref="FindReplacementAsync"/>), which a fault leaves null.
    /// </summary>
    internal static async Task<IResult> GetCorrectionPrefillAsync(
        Guid invoiceId,
        HttpContext http,
        InvoiceSubmissionService submissions,
        Spaarke.Dataverse.IGenericEntityService entities,
        Spaarke.Core.Auth.AuthorizationService authorization,
        ILogger<FromEmailSubmissionService> logger,
        CancellationToken ct)
    {
        Microsoft.Xrm.Sdk.Entity? invoice;
        try
        {
            invoice = await entities.RetrieveAsync(
                Services.Finance.FinanceBaseValues.InvoiceEntity, invoiceId, [.. InvoiceSubmissionService.CorrectionInvoiceColumns, InvoiceNameColumn], ct);
        }
        catch (Exception ex) when (InvoiceSubmissionEndpoints.IsNotFound(ex))
        {
            invoice = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Correction prefill: invoice {InvoiceId} could not be read", invoiceId);
            return PrefillUnavailable();
        }

        if (invoice is null)
        {
            // Gone since the rights check: the same answer as a refusal, so nothing about the invoice is revealed.
            return FinanceAuthorizationFilter.UniformRecordNotFound(http);
        }

        var firmId = invoice.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>(InvoiceSubmissionPersister.InvoiceVendorOrg)?.Id;
        var single = Services.Finance.EligibleApproverResolver.SingleCoreOf(Services.Finance.FinanceBaseValues.InvoiceEntity, invoice);
        if (firmId is not { } firm || firm == Guid.Empty || single is not { } found
            || InvoiceSubmissionService.CoreRecordKindOfTable(found.Entity) is not { } kind)
        {
            // The intake refuses such an invoice too (different_firm / different_core_record); there is nothing coherent to prefill.
            logger.LogWarning("Correction prefill: invoice {InvoiceId} names no single firm and core record", invoiceId);
            return PrefillUnavailable();
        }

        var core = new InvoiceCoreRecord(kind, found.Id);
        var judgement = await submissions.JudgeCorrectionPrefillAsync(invoice, core, firm, ct);
        if (judgement is null)
        {
            return PrefillUnavailable();
        }

        var firmName = await ReadNameAsync(entities, InvoiceSubmissionService.OrganizationEntity, firm, logger, ct);
        var coreName = await ReadNameAsync(entities, core.EntityLogicalName, core.RecordId, logger, ct);
        if (firmName is null || coreName is null)
        {
            return PrefillUnavailable();
        }

        var statusCode = invoice.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("statuscode")?.Value;
        var status = statusCode is { } code && Services.Finance.InvoiceStatusCodes.TryFromStatusCode(code, out var known) ? known : (Services.Finance.InvoiceLifecycleStatus?)null;
        var tracking = judgement.Submission?.GetAttributeValue<string>(TrackingNumberAllocator.TrackingNumberColumn);
        var replacedBy = status == Services.Finance.InvoiceLifecycleStatus.Superseded
            ? await FindReplacementAsync(judgement.Submission, http, entities, authorization, logger, ct)
            : null;

        return Results.Ok(new CorrectionPrefillResponse(
            invoiceId,
            new CorrectionPrefillFirm(firm, firmName),
            new CorrectionPrefillCoreRecord(CoreTypeName(kind), core.RecordId, coreName),
            status?.ToString(),
            string.IsNullOrWhiteSpace(tracking) ? null : tracking,
            judgement.Submission?.GetAttributeValue<string>(TrackingNumberAllocator.FirmInvoiceNumberColumn) ?? string.Empty,
            judgement.CanCorrect,
            judgement.Reason,
            replacedBy));
    }

    /// <summary>The invoice's primary name column (<c>sprk_name</c>, the one map <see cref="Services.Communication.RegardingNameFields.PrimaryNameField"/>).</summary>
    private static readonly string InvoiceNameColumn =
        Services.Communication.RegardingNameFields.PrimaryNameField(Services.Finance.FinanceBaseValues.InvoiceEntity)
        ?? throw new InvalidOperationException("No primary name column is known for sprk_invoice.");

    private static string CoreTypeName(InvoiceCoreRecordKind kind) => kind switch
    {
        InvoiceCoreRecordKind.Matter => "Matter",
        InvoiceCoreRecordKind.Project => "Project",
        _ => "WorkAssignment",
    };

    /// <summary>The record's primary name, read app-only (an id for a blank name, as the firm list does); null when it could not be read.</summary>
    private static async Task<string?> ReadNameAsync(
        Spaarke.Dataverse.IGenericEntityService entities, string table, Guid id, ILogger logger, CancellationToken ct,
        string route = "Correction prefill")
    {
        var nameColumn = Services.Communication.RegardingNameFields.PrimaryNameField(table);
        if (nameColumn is null)
        {
            logger.LogWarning("{Route}: no primary name column is known for {Entity}", route, table);
            return null;
        }

        try
        {
            var row = await entities.RetrieveAsync(table, id, [nameColumn], ct);
            if (row is null)
            {
                logger.LogWarning("{Route}: {Entity} {RecordId} was not found", route, table, id);
                return null;
            }

            var name = row.GetAttributeValue<string>(nameColumn);
            return string.IsNullOrWhiteSpace(name) ? id.ToString() : name;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "{Route}: the name of {Entity} {RecordId} could not be read", route, table, id);
            return null;
        }
    }

    /// <summary>
    /// <c>replacedBy</c> of a Superseded invoice (contract section 10): the DIRECT replacement, only when the caller can Read it, else null.
    /// The replacement is the invoice of the submission whose <c>sprk_resubmissionof</c> is the old invoice's own submission. Several: the
    /// one not itself Superseded; still not exactly one: null (a Warning). The replacement is read app-only to choose it, but only the
    /// caller's own Read (<see cref="FinanceAuthorizationFilter.CallerHoldsRightsAsync"/>, the filter's Record check, operation
    /// <c>finance.read</c>) lets it into the answer, so a record the caller cannot open is never named. DELIBERATE: this field is optional,
    /// so a refusal OR a fault anywhere in finding or reading the replacement leaves it null (a Warning for a fault) and never turns the
    /// whole answer into a 503; every other field is still exactly what the intake would judge.
    /// </summary>
    private static async Task<CorrectionPrefillReplacement?> FindReplacementAsync(
        Microsoft.Xrm.Sdk.Entity? ownSubmission,
        HttpContext http,
        Spaarke.Dataverse.IGenericEntityService entities,
        Spaarke.Core.Auth.AuthorizationService authorization,
        ILogger logger,
        CancellationToken ct)
    {
        if (ownSubmission is null)
        {
            return null;
        }

        try
        {
            var replacement = (await SupersededSubmissionNote.FindReplacementAsync(entities, logger, ownSubmission.Id, [InvoiceNameColumn], ct))?.Invoice;

            if (replacement is null || CallerResolution.ResolveObjectId(http.User) is not { Length: > 0 } userId)
            {
                return null;
            }

            var mayRead = await FinanceAuthorizationFilter.CallerHoldsRightsAsync(
                authorization, userId, FinanceAuthorizationFilter.InvoiceEntitySet, replacement.Id, "finance.read",
                TokenHelper.ExtractBearerTokenOrNull(http), ct);
            if (!mayRead)
            {
                return null;
            }

            var name = replacement.GetAttributeValue<string>(InvoiceNameColumn);
            return new CorrectionPrefillReplacement(replacement.Id, string.IsNullOrWhiteSpace(name) ? replacement.Id.ToString() : name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Correction prefill: the replacement of a superseded invoice could not be read; replacedBy is null");
            return null;
        }
    }

    private static IResult PrefillUnavailable() =>
        InvoiceSubmissionHttp.Refused(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.CoreRecordUnavailable,
            "The invoice could not be read right now. Try again shortly.");

    private static IResult FirmsUnavailable() =>
        InvoiceSubmissionHttp.Refused(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.CoreRecordUnavailable,
            "The firms of this record could not be read right now. Try again shortly.");
}
