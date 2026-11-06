using System.Net;
using System.Net.Http;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 019 pass 2 (privacy): a failed PATCH through
/// <see cref="DataverseWebApiService"/> logs ids, field NAMES, the status and Dataverse's error CODE — never a field
/// value and never the error message text (which can echo one). Every unified create reaches
/// <c>UpdateRecordFieldsAsync</c> (RecordCreationService step 11), so a failed email-draft create used to log the
/// recipients and body, and a failed quick-create the name and description.
/// </summary>
/// <remarks>Same offline-service technique as <c>DataverseWebApiServiceImpersonationTests</c>: a stub handler and a
/// static credential through the protected test constructor. The logger captures the rendered message AND the structured
/// state, because a value can hide in either.</remarks>
[Trait("status", "new")]
public sealed class UpdateFailureLogPrivacyTests
{
    private const string Sentinel = "SENTINEL-EMAIL@example.com";
    private const string ErrorCode = "0x80060892";

    // Dataverse's message text echoes the value that was written; only the code may be logged.
    private static readonly string ErrorBody =
        "{\"error\":{\"code\":\"" + ErrorCode + "\",\"message\":\"Invalid value '" + Sentinel + "' for attribute sprk_to.\"}}";

    private static Dictionary<string, object?> Fields() => new()
    {
        ["sprk_to"] = Sentinel,
        ["sprk_body"] = "Dear " + Sentinel + ", please see the attached.",
    };

    [Fact]
    public async Task UpdateRecordFieldsAsync_FailedPatch_LogsFieldNamesStatusAndCode_NeverValues()
    {
        var log = new CapturingLogger();
        var sut = new OfflineService(Handler(), log);

        var act = () => sut.UpdateRecordFieldsAsync("sprk_communication", Guid.NewGuid(), Fields());

        await act.Should().ThrowAsync<HttpRequestException>();
        AssertNoValueLogged(log, expectedMessageStart: "UpdateRecordFieldsAsync PATCH failed");
    }

    [Fact]
    public async Task UpdateExistingRecordFieldsAsync_FailedPatch_LogsFieldNamesStatusAndCode_NeverValues()
    {
        var log = new CapturingLogger();
        var sut = new OfflineService(Handler(), log);

        var act = () => sut.UpdateExistingRecordFieldsAsync("sprk_communication", Guid.NewGuid(), Fields());

        await act.Should().ThrowAsync<HttpRequestException>();
        AssertNoValueLogged(log, expectedMessageStart: "UpdateExistingRecordFieldsAsync PATCH failed");
    }

    [Fact]
    public async Task UpdateRecordFieldsIfUnchangedAsync_FailedPatch_LogsFieldNamesStatusAndCode_NeverValues()
    {
        var log = new CapturingLogger();
        var sut = new OfflineService(Handler(), log);

        var act = () => sut.UpdateRecordFieldsIfUnchangedAsync("sprk_communication", Guid.NewGuid(), Fields(), 7);

        await act.Should().ThrowAsync<HttpRequestException>();
        AssertNoValueLogged(log, expectedMessageStart: "UpdateRecordFieldsIfUnchangedAsync PATCH failed");
    }

    /// <summary>A code that is not a plain HRESULT is dropped, not logged: the code field cannot carry text through.</summary>
    [Fact]
    public async Task UpdateRecordFieldsAsync_FailedPatch_ANonHresultCodeIsNotLogged()
    {
        var body = "{\"error\":{\"code\":\"" + Sentinel + "\",\"message\":\"x\"}}";
        var log = new CapturingLogger();
        var sut = new OfflineService(Handler(body), log);

        var act = () => sut.UpdateRecordFieldsAsync("sprk_communication", Guid.NewGuid(), Fields());

        await act.Should().ThrowAsync<HttpRequestException>();
        log.Everything.Should().NotContain(Sentinel);
    }

    private static void AssertNoValueLogged(CapturingLogger log, string expectedMessageStart)
    {
        var failure = log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Subject;
        failure.Message.Should().StartWith(expectedMessageStart);
        failure.Message.Should().Contain("sprk_to").And.Contain("sprk_body", "field NAMES are allowed");
        failure.Message.Should().Contain("BadRequest").And.Contain(ErrorCode);

        log.Everything.Should().NotContain(Sentinel, "no log message or structured state may carry a field value");
        log.Everything.Should().NotContain("Dear ", "no log message or structured state may carry a field value");
        log.Everything.Should().NotContain("Invalid value", "the Dataverse message text can echo a value; only its code may be logged");
    }

    private static StubHandler Handler(string? errorBody = null) => new(request =>
        request.Method == HttpMethod.Patch
            ? new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(errorBody ?? ErrorBody, Encoding.UTF8, "application/json"),
            }
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"EntitySetName\":\"sprk_communications\"}", Encoding.UTF8, "application/json"),
            });

    private sealed record Entry(LogLevel Level, string Message, string State);

    private sealed class CapturingLogger : ILogger<DataverseWebApiService>
    {
        public List<Entry> Entries { get; } = new();

        /// <summary>Every rendered message and every structured key/value, joined, for a substring check.</summary>
        public string Everything => string.Join("\n", Entries.Select(e => e.Message + "\n" + e.State));

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var structured = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join(";", pairs.Select(p => $"{p.Key}={p.Value}"))
                : string.Empty;
            Entries.Add(new Entry(logLevel, formatter(state, exception), structured + (exception?.ToString() ?? string.Empty)));
        }
    }

    private sealed class OfflineService(StubHandler handler, ILogger<DataverseWebApiService> logger) : DataverseWebApiService(
        new HttpClient(handler),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
        }).Build(),
        logger,
        new Sprk.Bff.Api.Services.Ai.Membership.NullMembershipCacheInvalidator(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Sprk.Bff.Api.Services.Ai.Membership.NullMembershipCacheInvalidator>.Instance),
        confidentialClients: null,
        credential: new StaticTokenCredential());

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static readonly AccessToken Token = new("test-token", DateTimeOffset.MaxValue);

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => new(Token);
    }
}
