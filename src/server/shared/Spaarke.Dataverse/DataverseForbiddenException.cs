using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Spaarke.Dataverse;

/// <summary>
/// A 403 from a multi-row read (<see cref="DataverseWebApiService.RetrieveMultipleImpersonatedAsync"/>,
/// <see cref="DataverseWebApiService.RetrieveMultipleAppOnlyAsync"/>), carrying Dataverse's OData <c>error.code</c> and
/// <c>error.message</c> so a caller can tell a missing TABLE privilege of the impersonated user from every other refusal.
/// field-mapping-server-write-path-r1 task 071 (fix round 1): the caller-scoped family read lists zero rows for a table the
/// caller holds no Read privilege on, and fails on every other 403.
/// </summary>
/// <remarks>
/// <para>It IS an <see cref="HttpRequestException"/> with <see cref="HttpRequestException.StatusCode"/> 403 and the message
/// <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/> gives, so every existing caller reads it exactly as before. The
/// Dataverse text (which names the principal's id) is kept in <see cref="ErrorMessage"/>, never in <see cref="Exception.Message"/>.</para>
/// <para><b>Component justification (CLAUDE.md §11).</b> Existing: the read threw a bare <see cref="HttpRequestException"/>
/// and dropped the body; <c>DataverseWebApiService.TryReadErrorAsync</c> reads it for the principal-access calls only.
/// Extension: this subclass is that extension: the same exception type, now with the body's code and message. Cost of doing
/// nothing: a caller without Read on one child table cannot open a family at all (seen on dev: a Basic user and
/// <c>sprk_policies</c>).</para>
/// </remarks>
public sealed class DataverseForbiddenException : HttpRequestException
{
    public DataverseForbiddenException(string? errorCode, string? errorMessage)
        : base("Response status code does not indicate success: 403 (Forbidden).", inner: null, statusCode: HttpStatusCode.Forbidden)
    {
        ErrorCode = errorCode?.Trim();
        ErrorMessage = errorMessage;
    }

    /// <summary>The OData <c>error.code</c> (e.g. <c>0x80040220</c>), or null when the body had none.</summary>
    public string? ErrorCode { get; }

    /// <summary>The OData <c>error.message</c>, or null when the body had none. Names the principal: never log it whole.</summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Whether this is Dataverse's answer that the principal holds no Read privilege on <paramref name="table"/>: the
    /// access-check code (<see cref="DataverseWebApiService.AccessCheckDeniedErrorCode"/>), a message naming
    /// <c>prvRead{table}</c> as a whole token (any casing; <c>prvReadsprk_policyversion</c> is not <c>sprk_policy</c>'s), and
    /// NOT naming the application user's impersonation privilege (ADR-028: that 403 is a fault, never an answer).
    /// </summary>
    public bool IsMissingReadPrivilegeOn(string table)
    {
        if (string.IsNullOrWhiteSpace(table)
            || !string.Equals(ErrorCode, DataverseWebApiService.AccessCheckDeniedErrorCode, StringComparison.OrdinalIgnoreCase)
            || ErrorMessage is null
            || DataverseWebApiService.NamesImpersonationPrivilege(ErrorMessage))
        {
            return false;
        }

        return Regex.IsMatch(
            ErrorMessage,
            $"(?<![A-Za-z0-9_])prvRead{Regex.Escape(table.Trim())}(?![A-Za-z0-9_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
