using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.RecordCreation;

namespace Sprk.Bff.Api.Services.Dataverse.FieldMapping;

/// <summary>
/// field-mapping-server-write-path-r1 task 028 (FR-17): an EMAIL send filed under one <c>sprk_analysis</c> whose subject or body
/// is empty gets it from the active <c>sprk_analysis</c> to <c>sprk_communication</c> field-mapping profile. Fill-only: a value
/// the sender typed is never replaced.
/// </summary>
/// <remarks>
/// <para><b>The same machinery as the create.</b> The profile's rules are read and normalised as the create reads them
/// (<see cref="RecordCreationService.NormalizedRule"/>); the analysis is read AS THE CALLER first
/// (<see cref="MappingSourceReadProbe.ReadAsync"/>: Read probe, then the app-only read), masked to the columns the caller can
/// read (<see cref="MappingSourceCallerMask"/>, #2188), and put to the mapping-source rule
/// (<see cref="MappingSourceCopyGate.DecideAsync"/>); the values are computed by <see cref="CreateTimeFieldMapping.ApplyFillOnly"/>
/// into a scratch row (never created) and only <c>sprk_subject</c> and <c>sprk_body</c> are taken from it. Only the rules that
/// aim at a column the request left empty are read, so a typed subject reads nothing for the subject.</para>
/// <para><b>Mapping-source rule.</b> The email row's owner is decided later in the send, so it is not known here: the copy gate is
/// asked with no row owner, and a source that is secure, or whose answer cannot be told, fills nothing (fail closed).</para>
/// <para><b>Never an error, never a leak.</b> A denied, missing, unreadable or faulted source, an absent profile or rule, and any
/// fault leave the request exactly as it was; the only trace is a log line that names no record and no value. The send then
/// stands or fails on its own validation (<c>ValidateRequest</c> refuses a subject or body that is still empty).</para>
/// <para>ADR-010: static, no DI registration; the collaborators are read from the request's own services, as the private-message
/// gate is. ADR-008 path A: no route filter changed.</para>
/// </remarks>
internal static class SendTimeFieldMapping
{
    internal const string AnalysisTable = MappingSourceReadProbe.AnalysisLogicalName;
    internal const string CommunicationTable = "sprk_communication";
    private const string ScratchRowName = "scratch";
    internal const string SubjectColumn = "sprk_subject";
    internal const string BodyColumn = "sprk_body";

    private static readonly IReadOnlySet<string> NothingProtected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <paramref name="request"/> with an empty subject and/or body filled from the analysis mapping, or <paramref name="request"/>
    /// itself when nothing applies or nothing could be filled. Never throws except a cancellation of <paramref name="ct"/>.
    /// </summary>
    internal static async Task<SendCommunicationRequest> FillEmptySubjectAndBodyAsync(
        SendCommunicationRequest request,
        HttpContext? httpContext,
        IGenericEntityService entities,
        ILogger logger,
        string correlationId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(logger);

        var needSubject = string.IsNullOrWhiteSpace(request.Subject);
        var needBody = string.IsNullOrWhiteSpace(request.Body);
        if ((!needSubject && !needBody)
            || !IsCallerEmail(request)
            || OnlyAnalysisAssociation(request) is not { } analysisId
            || httpContext?.RequestServices is not { } services)
        {
            return request;
        }

        try
        {
            var profiles = services.GetService<IFieldMappingDataverseService>();
            var probe = services.GetService<CallerRecordAccessProbe>();
            var user = services.GetService<IDataverseUserClient>();
            var configuration = services.GetService<IConfiguration>();
            if (profiles is null || probe is null || user is null || configuration is null)
            {
                logger.LogWarning(
                    "[FIELD-MAPPING-SEND] The mapping services are not composed for this request; the send is unchanged | CorrelationId: {CorrelationId}",
                    correlationId);
                return request;
            }

            // The profile is keyed by the two TABLES; no profile, or none of its rules aimed at an empty column, is the silent no-op.
            var profile = await profiles
                .GetFieldMappingProfileWithRulesAsync(AnalysisTable, CommunicationTable, activeRulesOnly: true, ct)
                .ConfigureAwait(false);
            var rules = (profile?.Rules ?? [])
                .Select(RecordCreationService.NormalizedRule)
                .Where(r => (needSubject && r.TargetField == SubjectColumn) || (needBody && r.TargetField == BodyColumn))
                .ToList();
            if (rules.Count == 0)
            {
                return request;
            }

            // FR-04: Read as the caller FIRST, then app-only. Missing, denied, unmapped or faulted: one outcome, nothing filled.
            var sourceColumns = CreateTimeFieldMapping.CollectSourceColumns(rules);
            var readColumns = sourceColumns
                .Concat(MappingSourceCopyGate.SourceGateColumns(AnalysisTable))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var read = await MappingSourceReadProbe.ReadAsync(
                    MappingSourceReader.ForCaller(probe, TokenHelper.ExtractBearerTokenOrNull(httpContext)),
                    new MappingSource(AnalysisTable, analysisId), readColumns, entities, logger, ct)
                .ConfigureAwait(false);
            if (!read.Readable || read.Record is not { } appRead)
            {
                logger.LogInformation(
                    "[FIELD-MAPPING-SEND] The analysis is not available to the caller; the send is unchanged | CorrelationId: {CorrelationId}",
                    correlationId);
                return request;
            }

            // The mapping-source rule: the email's owner is not decided yet, so a source that is secure (or cannot be told) fills nothing.
            var decision = await MappingSourceCopyGate
                .DecideAsync(AnalysisTable, appRead, rowOwner: null,
                    token => SecureChildShareSynchronizer.ResolveSecureOwnerTeamForCopyGateAsync(entities, configuration, token), ct)
                .ConfigureAwait(false);
            if (decision.Verdict != MappingSourceCopyVerdict.Allowed)
            {
                logger.LogInformation(
                    "[FIELD-MAPPING-SEND] The analysis is {Verdict} ({Reason}); nothing was filled | CorrelationId: {CorrelationId}",
                    decision.Verdict, decision.Reason, correlationId);
                return request;
            }

            // #2188: the app-only read ignores field-level security, so it is masked to what the caller can read (one caller read).
            Entity? masked = null;
            if (sourceColumns.Count > 0)
            {
                masked = await MappingSourceCallerMask
                    .ApplyAsync(user, AnalysisTable, analysisId, appRead, sourceColumns, logger, ct)
                    .ConfigureAwait(false);
                if (masked is null)
                {
                    logger.LogInformation(
                        "[FIELD-MAPPING-SEND] The analysis could not be read as the caller; the send is unchanged | CorrelationId: {CorrelationId}",
                        correlationId);
                    return request;
                }
            }

            // A scratch row the engine writes the mapped values into; it is read back and dropped, never created (the engine does not
            // read its table name). Not named for the communication table: that would read as a create of one to the census scan.
            var scratch = new Entity(ScratchRowName);
            var engineWarnings = new List<string>();
            CreateTimeFieldMapping.ApplyFillOnly(rules, scratch, masked, NothingProtected, engineWarnings, logger);
            if (engineWarnings.Count > 0)
            {
                logger.LogInformation(
                    "[FIELD-MAPPING-SEND] {Count} mapping note(s) from the engine | CorrelationId: {CorrelationId}", engineWarnings.Count, correlationId);
            }

            var subject = needSubject ? TextOf(scratch, SubjectColumn) : null;
            var body = needBody ? TextOf(scratch, BodyColumn) : null;
            if (subject is null && body is null)
            {
                return request;
            }

            return request with
            {
                Subject = subject ?? request.Subject,
                Body = body ?? request.Body,
                // The mapped body is text: the client's own mapped-body path sent it as plain text, and the send must not read it as markup.
                BodyFormat = body is null ? request.BodyFormat : BodyFormat.PlainText,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[FIELD-MAPPING-SEND] Filling the subject or body from the analysis mapping faulted; the send is unchanged | CorrelationId: {CorrelationId}",
                correlationId);
            return request;
        }
    }

    /// <summary>A caller's email send: not a message, not system mail, not a server-only send.</summary>
    private static bool IsCallerEmail(SendCommunicationRequest request) =>
        request.CommunicationType == CommunicationType.Email && request.SystemMail is null && request.ServerSend is null;

    /// <summary>The analysis' id when exactly ONE association is an analysis (and it names a record); otherwise <see langword="null"/>.</summary>
    private static Guid? OnlyAnalysisAssociation(SendCommunicationRequest request)
    {
        var analyses = (request.Associations ?? [])
            .Where(a => string.Equals(a.EntityType?.Trim(), AnalysisTable, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return analyses.Count == 1 && analyses[0].EntityId != Guid.Empty ? analyses[0].EntityId : null;
    }

    /// <summary>The scratch row's text in <paramref name="column"/>, or <see langword="null"/> when the mapping left it empty.</summary>
    private static string? TextOf(Entity scratch, string column) =>
        scratch.Attributes.TryGetValue(column, out var value) && value is string text && !string.IsNullOrWhiteSpace(text) ? text : null;
}
