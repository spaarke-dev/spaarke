using System.Net;
using System.ServiceModel;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Communication.Threads;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// The Direct stamp's columns exist only where the schema script has run. When the write that files a message into a Direct
/// thread is refused because a stamp column does not exist on the table, the message is filed once more with the thread
/// lookup alone, and one Error with <c>direct_message.recipient_column_missing</c> names the script that adds the column.
/// Only that fault retries, and only a write that carried stamp columns: every other fault, and a write without them, keeps
/// the best-effort behaviour it had (one attempt, the message unthreaded, a non-fatal Warning).
/// </summary>
public partial class DirectMessageRecipientStampTests
{
    private const string ColumnMissingCode = "direct_message.recipient_column_missing";
    private const string SchemaScript = "scripts/Set-DirectRecipientSchema.ps1 -Apply";

    /// <summary>The ways the missing-attribute refusal reaches the filing write.</summary>
    public static TheoryData<string> MissingRecipientColumnFaults => new()
    {
        "sdk-typed-fault-wrapped",   // the generic entity service's wrapper over the SDK's typed fault
        "sdk-typed-fault-localized", // the typed code with a message carrying none of the English phrases
        "web-api-unknown-property",  // the Web API's 0x80060888 "Could not find a property named"
    };

    // ── Must fire ──────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(MissingRecipientColumnFaults))]
    public async Task SendJoin_WhenTheRecipientColumnIsMissing_FilesTheMessageWithTheThreadLookupOnly_AndLogsOneError(string fault)
    {
        _messageUpdateFault = FaultWhileCarrying(RecipientColumn, MissingColumnFault(fault, RecipientColumn));

        await Service(Join(DirectThread)).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        AssertFiledWithTheThreadLookupOnlyAfterOneRetry(_resolverLog);
    }

    [Fact]
    public async Task SendCreate_WhenTheRecipientColumnIsMissing_FilesTheMessageWithTheThreadLookupOnly_AndLogsOneError()
    {
        _createdThreadHoldsThePair = true;
        _messageUpdateFault = FaultWhileCarrying(RecipientColumn, MissingColumnFault("sdk-typed-fault-wrapped", RecipientColumn));

        await Service(new StubStrategy(ThreadKeyResolution.CreateNew)).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        _created.Should().ContainSingle(e => e.LogicalName == "sprk_communicationthread");
        AssertFiledWithTheThreadLookupOnlyAfterOneRetry(_resolverLog);
    }

    [Fact]
    public async Task InboundJoin_WhenTheRecipientColumnIsMissing_FilesTheMessageWithTheThreadLookupOnly_AndLogsOneError()
    {
        _messageRow = MessageRow(sentBy: null);
        _messageUpdateFault = FaultWhileCarrying(RecipientColumn, MissingColumnFault("sdk-typed-fault-wrapped", RecipientColumn));

        await InboundHandler(Join(DirectThread)).ProcessAsync(InboundJob(SenderChatId), CancellationToken.None);

        _messageUpdateAttempts.First(u => u.ContainsKey(ThreadLookup))
            .Should().ContainKey(SenderColumn, "the inbound row's sender rides in the same write");
        AssertFiledWithTheThreadLookupOnlyAfterOneRetry(_resolverLog);
    }

    [Theory]
    [MemberData(nameof(MissingRecipientColumnFaults))]
    public async Task Respond_WhenTheRecipientColumnIsMissing_FilesTheMessageWithTheThreadLookupOnly_AndLogsOneError(string fault)
    {
        _messageUpdateFault = FaultWhileCarrying(RecipientColumn, MissingColumnFault(fault, RecipientColumn));

        await Service(resolver: null).SendAsync(MessageRequest() with { ThreadId = DirectThread }, Caller(), CancellationToken.None);

        AssertFiledWithTheThreadLookupOnlyAfterOneRetry(_serviceLog);
        _serviceLog.Entries.Should().NotContain(e => e.Message.Contains("Explicit thread stamp failed"));
    }

    // ── Must not fire ──────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Faults on the combined write that are not "this column does not exist": none of them retries.</summary>
    public static TheoryData<string, bool> OtherFaultsOnEachPath
    {
        get
        {
            var data = new TheoryData<string, bool>();
            foreach (var fault in new[] { "privilege-on-the-column", "throttle", "http-403", "missing-referenced-row" })
            {
                data.Add(fault, false); // the resolver's join
                data.Add(fault, true);  // the respond path
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(OtherFaultsOnEachPath))]
    public async Task AnyOtherFaultOnTheCombinedWrite_IsNotRetried_AndKeepsTheNonFatalWarning(string fault, bool respond)
    {
        _messageUpdateFault = FaultWhileCarrying(RecipientColumn, OtherFault(fault));

        await Send(respond);

        _messageUpdateAttempts.Should().ContainSingle().Which.Should().ContainKey(RecipientColumn);
        _messageUpdates.Should().BeEmpty("the fault leaves the message unthreaded, as before");
        AllLogEntries().Should().NotContain(e => e.EventId.Name == ColumnMissingCode);
        var log = respond ? _serviceLog.Entries : _resolverLog.Entries;
        log.Should().Contain(e => e.Level == LogLevel.Warning
            && e.Message.Contains(respond ? "Explicit thread stamp failed (non-fatal)" : "Thread resolution failed (non-fatal)"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFilingWriteWithoutStampColumns_ThatFaultsOnAMissingAttribute_IsNotRetried(bool respond)
    {
        _threadShares = new() { UserShare(Other, 0) }; // no pair, so the write carries the thread lookup alone
        _messageUpdateFault = _ => MissingColumnFault("sdk-typed-fault-wrapped", ThreadLookup);

        await Send(respond);

        _messageUpdateAttempts.Should().ContainSingle().Which.Keys.Should().BeEquivalentTo(new[] { ThreadLookup });
        _messageUpdates.Should().BeEmpty();
        AllLogEntries().Should().NotContain(e => e.EventId.Name == ColumnMissingCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASuccessfulCombinedWrite_IsExactlyOneUpdate(bool respond)
    {
        await Send(respond);

        _messageUpdateAttempts.Should().ContainSingle().Which.Should().ContainKey(RecipientColumn).And.ContainKey(ThreadLookup);
        _entities.Verify(
            e => e.UpdateAsync("sprk_communication", MessageId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        AllLogEntries().Should().NotContain(e => e.EventId.Name == ColumnMissingCode);
    }

    // ── Rig ────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private Task Send(bool respond) => respond
        ? Service(resolver: null).SendAsync(MessageRequest() with { ThreadId = DirectThread }, Caller(), CancellationToken.None)
        : Service(Join(DirectThread)).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

    private IEnumerable<(LogLevel Level, EventId EventId, string Message)> AllLogEntries() =>
        _resolverLog.Entries.Concat(_serviceLog.Entries);

    /// <summary>The write faults while it carries <paramref name="column"/>; without it, it lands.</summary>
    private static Func<Dictionary<string, object>, Exception?> FaultWhileCarrying(string column, Exception fault) =>
        fields => fields.ContainsKey(column) ? fault : null;

    private void AssertFiledWithTheThreadLookupOnlyAfterOneRetry<T>(RecordingLogger<T> log)
    {
        // The filing writes: the ones carrying the thread lookup (an inbound message also gets updates of its own).
        var filingAttempts = _messageUpdateAttempts.Where(u => u.ContainsKey(ThreadLookup)).ToList();
        filingAttempts.Should().HaveCount(2, "the refused write is retried once");
        filingAttempts[0].Should().ContainKey(RecipientColumn);

        var retry = filingAttempts[1];
        retry.Keys.Should().BeEquivalentTo(new[] { ThreadLookup }, "the retry carries the thread lookup alone");
        ((EntityReference)retry[ThreadLookup]).Id.Should().Be(DirectThread);
        _messageUpdates.Where(u => u.ContainsKey(ThreadLookup)).Should().ContainSingle().Which.Should().BeSameAs(retry);

        var errors = AllLogEntries().Where(e => e.EventId.Name == ColumnMissingCode).ToList();
        errors.Should().ContainSingle();
        log.Entries.Should().ContainSingle(e => e.EventId.Name == ColumnMissingCode)
            .Which.Level.Should().Be(LogLevel.Error);
        errors[0].Message.Should().Contain(ColumnMissingCode).And.Contain(SchemaScript)
            .And.Contain(MessageId.ToString()).And.Contain(DirectThread.ToString());
    }

    private static Exception MissingColumnFault(string shape, string column)
    {
        var sdkText = $"'sprk_communication' entity doesn't contain attribute with Name = '{column}' and NameMapping = 'Logical'.";
        return shape switch
        {
            "sdk-typed-fault-wrapped" => new InvalidOperationException(
                $"Failed to update sprk_communication record {MessageId}: {sdkText}",
                SdkFault(unchecked((int)0x80041103), sdkText)),
            "sdk-typed-fault-localized" => new InvalidOperationException(
                "Échec de la mise à jour",
                SdkFault(unchecked((int)0x80041103), $"L'entité 'sprk_communication' ne contient pas l'attribut '{column}'.")),
            "web-api-unknown-property" => new HttpRequestException(
                $"0x80060888 Could not find a property named '{column}' on type 'Microsoft.Dynamics.CRM.sprk_communication'.",
                null, HttpStatusCode.BadRequest),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };
    }

    private static Exception OtherFault(string kind) => kind switch
    {
        "privilege-on-the-column" => new InvalidOperationException(
            "Failed to update sprk_communication record",
            SdkFault(unchecked((int)0x80040220), $"The user is missing the privilege to update '{RecipientColumn}' on 'sprk_communication'.")),
        "throttle" => new InvalidOperationException(
            "Failed to update sprk_communication record",
            SdkFault(unchecked((int)0x80072322), "Number of requests exceeded the limit of 6000 over time window of 300 seconds.")),
        "http-403" => new HttpRequestException(
            "Response status code does not indicate success: 403 (Forbidden).", null, HttpStatusCode.Forbidden),
        "missing-referenced-row" => new InvalidOperationException(
            "Failed to update sprk_communication record",
            SdkFault(unchecked((int)0x80040217), $"{RecipientColumn}: systemuser With Id = {Other} Does Not Exist")),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static FaultException<OrganizationServiceFault> SdkFault(int code, string message) =>
        new(new OrganizationServiceFault { ErrorCode = code, Message = message }, new FaultReason(message));
}
