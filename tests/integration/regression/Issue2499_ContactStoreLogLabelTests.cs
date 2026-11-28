using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

/// <summary>
/// #2499 (r4 task 045, FR-30, NFR-04): a failed contact-store read logs no tenant, no oid and no response body. Two reads
/// (<c>FindSystemUsersByGuestObjectIdAsync</c>, <c>FindHomeContactsAsync</c>, and the by-oid read) passed no log label, so the warning carried the
/// raw request path (the guest oid; the home tenant and oid). Dataverse also echoes the request's whole filter in an error
/// message, so for EVERY caller the warning now carries the status and the Dataverse error code only; the proof's thrown
/// exception message does the same. What a read RETURNS is unchanged (the membership refusal still reads the code out of the
/// store's <c>"{status}: {body}"</c> error).
/// <para>The store's own HTTP seam (an <see cref="IHttpClientFactory"/> double, as <c>DataverseContactIdentityStoreTests</c>
/// uses); time is a <see cref="FakeTimeProvider"/>.</para>
/// </summary>
public class Issue2499_ContactStoreLogLabelTests
{
    private static readonly Guid HomeTenant = Guid.Parse("a2499000-0000-4000-8000-0000000000a1");
    private static readonly Guid Oid = Guid.Parse("a2499000-0000-4000-8000-0000000000a2");
    private static readonly Guid ContactId = Guid.Parse("a2499000-0000-4000-8000-0000000000a3");

    private const string BodyMarker = "BODYMARKER-2499-MUST-NOT-BE-LOGGED";
    private const string ErrorCode = "0x80060888";

    /// <summary>A Dataverse error whose message echoes the request's filter values and a recognisable marker.</summary>
    private static readonly string EchoBody =
        $"{{\"error\":{{\"code\":\"{ErrorCode}\",\"message\":\"bad filter {HomeTenant:D} {Oid:D} {BodyMarker}\"}}}}";

    private static DataverseContactIdentityStore Store(HttpStatusCode code, string body, CapturingLogger logger) =>
        new(new FixedHttp(code, body), _ => Task.FromResult("token"), "https://org.test.invalid", logger,
            new FakeTimeProvider(new DateTimeOffset(2026, 11, 27, 9, 0, 0, TimeSpan.Zero)));

    private static Task<object> Read(DataverseContactIdentityStore store, string read) => read switch
    {
        "guest systemuser lookup" => Box(store.FindSystemUsersByGuestObjectIdAsync(Oid, CancellationToken.None)),
        "home contact lookup" => Box(store.FindHomeContactsAsync(HomeTenant, Oid, CancellationToken.None)),
        "home link lookup" => Box(store.FindHomeLinkedSystemUsersAsync(HomeTenant, Oid, CancellationToken.None)),
        "home tenant read" => Box(store.ReadHomeTenantAsync(ContactId, CancellationToken.None)),
        "contacts by email" => Box(store.FindActiveContactsByEmailAsync("o'brien@firm.example", CancellationToken.None)),
        "contacts by oid" => Box(store.FindContactsByOidAsync(Oid, CancellationToken.None)),
        "contacts by key mirror" => Box(store.FindContactsByKeyMirrorAsync(Oid, CancellationToken.None)),
        "contact by id" => Box(store.GetContactAsync(ContactId, CancellationToken.None)),
        _ => throw new ArgumentOutOfRangeException(nameof(read), read, null),
    };

    private static async Task<object> Box<T>(Task<T> task) => (await task)!;

    // ── Must fire: the reads that logged their raw path (guest oid; home tenant and oid; the by-oid read's oid) ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("guest systemuser lookup")]
    [InlineData("home contact lookup")]
    [InlineData("contacts by oid")]
    [InlineData("contacts by key mirror")]
    public async Task AFailedRead_OfAFormerlyUnlabelledRead_LogsAFixedLabelAndTheStatus_NeverTheTenantOrTheOid(string label)
    {
        var logger = new CapturingLogger();

        await Read(Store(HttpStatusCode.InternalServerError, EchoBody, logger), label);

        var line = logger.Warnings.Should().ContainSingle().Subject;
        line.Should().Contain(label).And.Contain("500").And.Contain(ErrorCode);
        line.Should().NotContainAny(TenantAndOid());
    }

    [Theory]
    [InlineData("guest systemuser lookup")]
    [InlineData("home contact lookup")]
    [InlineData("contacts by oid")]
    [InlineData("contacts by key mirror")]
    public async Task AThrownRead_OfAFormerlyUnlabelledRead_LogsAFixedLabel_NeverTheTenantOrTheOid(string label)
    {
        var logger = new CapturingLogger();
        var store = new DataverseContactIdentityStore(new FixedHttp(HttpStatusCode.OK, "{}"),
            _ => throw new InvalidOperationException("token source down"), "https://org.test.invalid", logger);

        await Read(store, label);

        var line = logger.Warnings.Should().ContainSingle().Subject;
        line.Should().Contain(label);
        line.Should().NotContainAny(TenantAndOid());
    }

    // ── Must fire: no failed read, labelled or not, logs the response body ───────────────────────────────────────────────

    [Theory]
    [InlineData("guest systemuser lookup")]
    [InlineData("home contact lookup")]
    [InlineData("home link lookup")]
    [InlineData("home tenant read")]
    [InlineData("contacts by email")]
    [InlineData("contacts by oid")]
    [InlineData("contact by id")]
    public async Task AFailedRead_NeverLogsTheResponseBody_OnlyTheStatusAndTheErrorCode(string read)
    {
        var logger = new CapturingLogger();

        await Read(Store(HttpStatusCode.BadRequest, EchoBody, logger), read);

        var line = logger.Warnings.Should().ContainSingle(read).Subject;
        line.Should().Contain("400").And.Contain(ErrorCode);
        line.Should().NotContain(BodyMarker).And.NotContain("bad filter");
    }

    /// <summary>A failed WRITE logs the status and the Dataverse error code only, never the response body; what the write
    /// returns is unchanged (its error text still carries the body, for the callers that parse it).</summary>
    [Fact]
    public async Task AFailedWrite_LogsTheStatusAndTheErrorCode_NeverTheBody_AndStillReturnsTheBodyInItsError()
    {
        var logger = new CapturingLogger();

        var write = await Store(HttpStatusCode.InternalServerError, EchoBody, logger)
            .BindHomeOidAsync(ContactId, "W/\"1\"", HomeTenant, Oid, CancellationToken.None);

        write.Status.Should().Be(StoreWriteStatus.Failed);
        write.Error.Should().Contain(BodyMarker, "the returned error text is what callers parse; only the log changed");
        var line = logger.Warnings.Should().ContainSingle().Subject;
        line.Should().Contain("500").And.Contain(ErrorCode);
        line.Should().NotContain(BodyMarker).And.NotContain("bad filter").And.NotContainAny(TenantAndOid());
    }

    /// <summary>MUST NOT FIRE (control): a body that is not a Dataverse error still logs nothing of itself, and the log line
    /// names the missing code rather than going silent.</summary>
    [Fact]
    public async Task AFailedRead_WhoseBodyIsNotAnError_LogsNoCode_AndNoBody()
    {
        var logger = new CapturingLogger();

        await Read(Store(HttpStatusCode.BadGateway, $"<html>{BodyMarker}</html>", logger), "contacts by oid");

        logger.Warnings.Should().ContainSingle().Which.Should().Contain("502").And.Contain("(no error code)").And.NotContain(BodyMarker);
    }

    // ── What a read returns is unchanged: the membership refusal still reads its code out of the error string ────────────

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "0x80072560", true)]
    [InlineData(HttpStatusCode.Forbidden, "0x80040220", false)]
    [InlineData(HttpStatusCode.BadRequest, "0x80072560", false)]
    public async Task TheMembershipRefusal_IsStillRecognised_FromTheErrorString_WhileTheLogCarriesNoBody(
        HttpStatusCode status, string code, bool refused)
    {
        var logger = new CapturingLogger();
        var body = $"{{\"error\":{{\"code\":\"{code}\",\"message\":\"The user is not a member {BodyMarker}\"}}}}";

        var result = await Store(status, body, logger).IsEnvironmentMembershipRefusedAsync(CancellationToken.None);

        result.Should().Be(refused);
        logger.Warnings.Should().ContainSingle().Which.Should().Contain("membership check").And.Contain(code).And.NotContain(BodyMarker);
    }

    // ── The readability proof's thrown exception carries the status and the code, not the body ──────────────────────────

    [Fact]
    public async Task AFailedProofRead_LogsAnExceptionWhoseMessageCarriesTheStatusAndTheCode_NeverTheBody()
    {
        var logger = new CapturingLogger();

        // The proof's first read (WhoAmI) is refused: the proof reports Faulted and logs the exception it caught.
        await Store(HttpStatusCode.Forbidden, EchoBody, logger).ProbeBindingReadabilityAsync(CancellationToken.None);

        var logged = logger.Entries.Where(e => e.Exception is not null).Should().ContainSingle("the proof logs the one exception").Subject;
        logged.Exception!.Message.Should().Contain("403").And.Contain(ErrorCode);
        logged.Exception.Message.Should().NotContain(BodyMarker).And.NotContain("bad filter").And.NotContainAny(TenantAndOid());
        logger.Entries.Select(e => e.Text).Should().OnlyContain(t => !t.Contains(BodyMarker) && !t.Contains("bad filter"));
    }

    // ── The pure helper the proof's message uses ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("403: {\"error\":{\"code\":\"0x80072560\",\"message\":\"x\"}}", "0x80072560")]
    [InlineData("500: {\"error\":{\"code\":\"0x80060888\",\"message\":\"a very long message that the store cut off at four hund", "0x80060888")]
    [InlineData("400: {\"error\":{\"message\":\"no code here\"}}", null)]
    [InlineData("400: {\"error\":{\"message\":\"nested\",\"innererror\":{\"code\":\"inner\"}}}", null)]
    [InlineData("400: {\"other\":{\"code\":\"0x1\"}}", null)]
    [InlineData("400: {\"error\":{\"code\":\"a secret sentence with spaces\"}}", null)]
    [InlineData("an exception message with no json", null)]
    [InlineData(null, null)]
    public void ErrorCodeOfError_ReadsTheCodeOfAPossiblyTruncatedErrorString_AndNothingElse(string? error, string? expected)
        => DataverseContactIdentityStore.ErrorCodeOfError(error).Should().Be(expected);

    private static string[] TenantAndOid() =>
        new[] { HomeTenant.ToString("D"), HomeTenant.ToString("N"), Oid.ToString("D"), Oid.ToString("N") };

    /// <summary>Answers every request with one scripted status and body.</summary>
    private sealed class FixedHttp(HttpStatusCode code, string body) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(code, body));

        private sealed class Handler(HttpStatusCode code, string body) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
                => Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
        }
    }

    /// <summary>Records every Warning-or-above entry: its formatted text, and its exception when it has one.</summary>
    private sealed class CapturingLogger : ILogger
    {
        public List<(string Text, Exception? Exception)> Entries { get; } = new();

        /// <summary>The text of each entry, with its exception's full text appended (a log sink prints both).</summary>
        public IEnumerable<string> Warnings => Entries.Select(e => e.Text);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Warning) return;
            Entries.Add((formatter(state, exception) + (exception is null ? string.Empty : " " + exception), exception));
        }
    }
}
