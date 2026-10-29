using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// ebilling-r1 task 049: <see cref="SignalWriter.PreflightRefusal"/>, the writer's I/O-free request checks, shared with
/// <see cref="SignalWriter.WriteAsync"/> (one method, <c>CheckRequest</c>, behind both). Each case below is checked twice from one
/// table: the preflight returns the stable code, and <c>WriteAsync</c> still throws exactly the exception it threw before the
/// extraction and writes nothing. A check removed from the shared method therefore fails a preflight row and a WriteAsync row.
/// </summary>
public partial class SignalWriterTests
{
    private sealed record PreflightCase(Func<SignalWriteRequest, SignalWriteRequest> Mutate, string Code, Type Exception);

    private static readonly Dictionary<string, PreflightCase> PreflightCases = new()
    {
        ["subject name not a logical name"] = new(r => r with { SubjectEntityLogicalName = "Sprk-Matter" }, SignalWriteRefusal.SubjectEntityInvalid, typeof(ArgumentException)),
        ["subject name blank"] = new(r => r with { SubjectEntityLogicalName = " " }, SignalWriteRefusal.SubjectEntityInvalid, typeof(ArgumentException)),
        ["subject id empty"] = new(r => r with { SubjectId = Guid.Empty }, SignalWriteRefusal.SubjectIdEmpty, typeof(ArgumentException)),
        ["policy code blank"] = new(r => r with { PolicyCode = "  " }, SignalWriteRefusal.PolicyCodeMissing, typeof(ArgumentException)),
        ["policy code 51 chars"] = new(r => r with { PolicyCode = new string('x', 51) }, SignalWriteRefusal.PolicyCodeTooLong, typeof(ArgumentException)),
        ["headline blank"] = new(r => r with { ShortHeadline = "" }, SignalWriteRefusal.HeadlineMissing, typeof(ArgumentException)),
        ["headline 201 chars"] = new(r => r with { ShortHeadline = new string('x', 201) }, SignalWriteRefusal.HeadlineTooLong, typeof(ArgumentException)),
        ["lane not an option value"] = new(r => r with { Lane = 0 }, SignalWriteRefusal.LaneInvalid, typeof(ArgumentException)),
        ["severity not an option value"] = new(r => r with { Severity = 999999999 }, SignalWriteRefusal.SeverityInvalid, typeof(ArgumentException)),
        ["episode 0"] = new(r => r with { Episode = 0 }, SignalWriteRefusal.EpisodeInvalid, typeof(ArgumentException)),
        ["template blank"] = new(r => r with { MessageTemplate = "  " }, SignalWriteRefusal.TemplateMissing, typeof(ArgumentException)),
        ["template null"] = new(r => r with { MessageTemplate = null! }, SignalWriteRefusal.TemplateMissing, typeof(ArgumentNullException)),
        ["fact values null"] = new(r => r with { FactValues = null! }, SignalWriteRefusal.FactValuesMissing, typeof(ArgumentNullException)),
        ["template token not in the snapshot"] = new(r => r with { MessageTemplate = "Saw {{neverRead}}." }, SignalWriteRefusal.SentenceTemplateInvalid, typeof(SignalSentenceTemplateException)),
        ["template token has a null fact"] = new(r => r with { FactValues = new Dictionary<string, object?> { ["category"] = null } }, SignalWriteRefusal.SentenceTemplateInvalid, typeof(SignalSentenceTemplateException)),
        ["template has a malformed placeholder"] = new(r => r with { MessageTemplate = "A {{category communication." }, SignalWriteRefusal.SentenceTemplateInvalid, typeof(SignalSentenceTemplateException)),
        ["rendered sentence 2001 chars"] = new(
            r => r with { MessageTemplate = "{{longFact}}", FactValues = new Dictionary<string, object?> { ["longFact"] = new string('x', 2001) } },
            SignalWriteRefusal.SentenceTooLong, typeof(ArgumentException)),
        ["subject table not verified"] = new(r => r with { SubjectEntityLogicalName = "sprk_servicerequest" }, SignalWriteRefusal.SubjectNotVerified, typeof(SignalWriterEscalationException)),
    };

    public static TheoryData<string> PreflightCaseNames() => new(PreflightCases.Keys);

    private static SignalWriter PreflightWriter(FakeOrganizationService org, ILogger<SignalWriter>? logger = null) =>
        Build(org, new FakeSysadminClient().WithMatterBusinessUnit(MatterId, BusinessUnitId), new FakeTimeProvider(FirstRun), logger);

    [Fact(DisplayName = "049 preflight: a valid request is not refused, for every verified subject, with and without a severity")]
    public void Preflight_AValidRequest_ReturnsNull()
    {
        SignalWriter.PreflightRefusal(MatterSubjectRequest()).Should().BeNull();
        SignalWriter.PreflightRefusal(CommunicationSubjectRequest()).Should().BeNull("a null severity and a second subject are valid");
        foreach (var subject in SignalWriter.VerifiedSubjects)
        {
            SignalWriter.PreflightRefusal(MatterSubjectRequest() with { SubjectEntityLogicalName = subject }).Should().BeNull(subject);
        }
    }

    [Fact(DisplayName = "049 preflight: exactly 50-char policy code, 200-char headline, 2000-char sentence and episode 1 pass (the limits are inclusive)")]
    public void Preflight_TheLengthLimits_AreInclusive()
    {
        var atLimits = MatterSubjectRequest() with
        {
            PolicyCode = new string('p', 50),
            ShortHeadline = new string('h', 200),
            MessageTemplate = "{{longFact}}",
            FactValues = new Dictionary<string, object?> { ["longFact"] = new string('s', 2000) },
            Episode = 1,
        };

        SignalWriter.PreflightRefusal(atLimits).Should().BeNull();
    }

    [Fact(DisplayName = "049 preflight: a null request is refused with request_missing and WriteAsync throws ArgumentNullException")]
    public async Task Preflight_ANullRequest_IsRefused_AndWriteAsyncStillThrowsArgumentNull()
    {
        SignalWriter.PreflightRefusal(null!).Should().Be(SignalWriteRefusal.RequestMissing);

        var org = new FakeOrganizationService();
        var act = async () => await PreflightWriter(org).WriteAsync(null!);

        await act.Should().ThrowExactlyAsync<ArgumentNullException>();
        org.CreateCallCount.Should().Be(0);
    }

    [Theory(DisplayName = "049 preflight: each refused request gets its stable code, and WriteAsync throws the same exception as before and writes nothing")]
    [MemberData(nameof(PreflightCaseNames))]
    public async Task Preflight_RefusedRequest_GetsItsCode_AndWriteAsyncStillThrowsExactlyAsBefore(string name)
    {
        var testCase = PreflightCases[name];
        var request = testCase.Mutate(MatterSubjectRequest());

        SignalWriter.PreflightRefusal(request).Should().Be(testCase.Code, name);

        var org = new FakeOrganizationService();
        var act = async () => await PreflightWriter(org).WriteAsync(request);
        (await act.Should().ThrowAsync<Exception>(name)).Which.Should().BeOfType(testCase.Exception, name);
        org.CreateCallCount.Should().Be(0, "the refusal precedes the first Dataverse call");
        org.Rows.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 preflight: the codes are stable strings, one per check, and the two with a writer reason use that reason's value")]
    public void Preflight_TheCodes_AreDistinct_AndStable()
    {
        var tested = PreflightCases.Values.Select(c => c.Code).Distinct().Append(SignalWriteRefusal.RequestMissing).ToArray();
        var declared = typeof(SignalWriteRefusal).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToArray();
        declared.Should().OnlyHaveUniqueItems("one code per distinct check");
        declared.Should().BeEquivalentTo(tested, "no declared code is untested, and none tested is undeclared");
        SignalWriteRefusal.SentenceTemplateInvalid.Should().Be(OntologyWriterFailureReason.SentenceTemplateInvalid);
        SignalWriteRefusal.SubjectNotVerified.Should().Be(OntologyWriterFailureReason.MatterDerivationUnverified);
        SignalWriteRefusal.PolicyCodeTooLong.Should().Be("policy_code_too_long");
        SignalWriteRefusal.SentenceTooLong.Should().Be("sentence_too_long");
    }

    [Theory(DisplayName = "049 preflight: an unrenderable template and an unverified subject are still logged at Error (WriteRefused) and metered by WriteAsync; an argument refusal is not")]
    [InlineData("template token not in the snapshot", OntologyWriterFailureReason.SentenceTemplateInvalid)]
    [InlineData("subject table not verified", OntologyWriterFailureReason.MatterDerivationUnverified)]
    [InlineData("lane not an option value", null)]
    [InlineData("rendered sentence 2001 chars", null)]
    public async Task Preflight_WriteAsyncLoggingOfARefusal_IsUnchanged(string name, string? loggedReason)
    {
        var scope = Guid.NewGuid();
        FailureMetricScope.Value = scope;
        var (listener, failureCount) = ListenFailuresScoped(scope);
        using var _ = listener;
        var logger = new CapturingLogger<SignalWriter>();

        var act = async () => await PreflightWriter(new FakeOrganizationService(), logger).WriteAsync(PreflightCases[name].Mutate(MatterSubjectRequest()));
        await act.Should().ThrowAsync<Exception>();

        if (loggedReason is null)
        {
            logger.Entries.Should().BeEmpty("an argument refusal is a caller bug and was never logged");
            failureCount().Should().Be(0);
            return;
        }

        var entry = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Subject;
        entry.EventId.Id.Should().Be(OntologyWriterEvents.WriteRefused.Id);
        entry.Field("Reason").Should().Be(loggedReason);
        failureCount().Should().Be(1);
    }
}
