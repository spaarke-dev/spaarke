using System.Diagnostics;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// The external-access administration audit trail (the person removal and reinstatement records, event ids 9821 and 9822).
/// <para><b>The rule: admin audit records are exported regardless of trace sampling.</b> Two things keep them:</para>
/// <list type="bullet">
/// <item>They are written under their own category, <see cref="Category"/>, which sits under <c>Sprk.Bff.Api</c>, so the
/// configured minimum level for <c>Sprk.Bff.Api</c> (Information in production) passes them whatever <c>Default</c> is. A
/// guard test reads the appsettings templates as the host layers them and fails if the category's effective level rises
/// above Information.</item>
/// <item>They are written through <see cref="Write"/>, outside the request's trace context: the log exporter drops a record
/// that carries a span whose trace was not sampled, and a record with no span is always exported. The correlation id stays
/// in the record's own properties.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>CLAUDE.md §11. Existing: the audit was written with the request's <c>ILogger&lt;Program&gt;</c> inside the request's
/// Activity. Extension: a logging option or a BFF-wide sampler change would alter every log record of the BFF (that change is
/// tracked separately); this keeps the scope to the administration audit. Cost of doing nothing: with the production
/// <c>Default</c> level and trace-based log sampling, a removal or reinstatement can leave no audit record at all.</para>
/// </remarks>
internal static class ExternalAccessAudit
{
    /// <summary>The audit category. Under <c>Sprk.Bff.Api</c> on purpose: see the class summary.</summary>
    public const string Category = "Sprk.Bff.Api.Audit.ExternalAccess";

    /// <summary>
    /// Runs <paramref name="write"/> synchronously against <paramref name="auditLogger"/> with <see cref="Activity.Current"/>
    /// cleared, and restores the saved Activity in a <c>finally</c>, also when <paramref name="write"/> throws. The log record
    /// captures its trace context when it is emitted, so the record carries no span or trace id and trace sampling does not
    /// apply to it; the exporter's later batch export does not change that. Only <paramref name="write"/> runs outside the
    /// trace; nothing awaits inside it.
    /// </summary>
    public static void Write(ILogger auditLogger, Action<ILogger> write)
    {
        ArgumentNullException.ThrowIfNull(auditLogger);
        ArgumentNullException.ThrowIfNull(write);

        var saved = Activity.Current;
        Activity.Current = null;
        try
        {
            write(auditLogger);
        }
        finally
        {
            Activity.Current = saved;
        }
    }
}
