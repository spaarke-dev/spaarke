using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Ontology;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.Integration.Contract.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

// spaarkeai-mcp-bff-api-r1 task 011 (FR-09). This part of the class holds the byte-level golden contract tests of the decision-plan,
// suppression and decision routes. It is a part of DecisionCommitServiceTests on purpose: the commit route's boundary rig (Rig) is a
// private type of that class, and the plan and suppression routes read the same Signal through the same rig (SignalCoreRecordAccess,
// DecisionPlanService over the same scripted caller client), so the baselines reuse the #2580 fixture shapes instead of copying them.
// The worklist route's baselines are in SignalWorklistRouteContractBaselineTests (they need the worklist World fixture).
public sealed partial class DecisionCommitServiceTests
{
    /// <summary>
    /// The REAL <c>MapDecisionPlanEndpoints</c> and <c>MapDecisionCommitEndpoints</c> (routing, <c>RequireAuthorization</c>, binding,
    /// the handlers, the JSON writer) on a minimal TestServer host, the real services over the Rig's module-boundary fakes. Each case
    /// records status, Content-Type and body bytes as they are today; <c>Baselines/{case}.json</c> is the contract tasks 012-017
    /// must keep. Every reason code the routes return today has one case; the codes not baselined are listed, with the reason, in
    /// <c>projects/spaarkeai-mcp-bff-api-r1/notes/contract-baseline.md</c>.
    /// </summary>
    public sealed class SignalRoutesContractBaselineTests
    {
        private const string PlanRoute = "/api/v1/signals/{0}/decision-plan";
        private const string SuppressionRoute = "/api/v1/signals/{0}/suppression";
        private const string DecisionRoute = "/api/v1/signals/{0}/decision";

        private static readonly Guid BadPlanVersion = Guid.Parse("c0430011-0000-4000-8000-0000000000b0");

        /// <summary>A stable review id per case, so a case's request (and its response) is the same on every run.</summary>
        private static Guid Rv(int n) => Guid.Parse("c0430011-0000-4000-8000-" + n.ToString("D12", System.Globalization.CultureInfo.InvariantCulture));

        // ── the host ──────────────────────────────────────────────────────────────────────────────────────────────

        private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
        {
            public const string SchemeName = "SignalsContractTest";
            public const string CallerHeader = "X-Test-Caller";

            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                if (!Request.Headers.ContainsKey(CallerHeader))
                {
                    return Task.FromResult(AuthenticateResult.NoResult());
                }

                var identity = new ClaimsIdentity([new Claim("tid", Tenant), new Claim("oid", CallerOid.ToString("D"))], SchemeName);
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
            }
        }

        private sealed class SignalsHost : IAsyncDisposable
        {
            private WebApplication? _app;
            private HttpClient? _client;

            public static async Task<SignalsHost> StartAsync(Rig rig)
            {
                var host = new SignalsHost();
                await host.InitializeAsync(rig);
                return host;
            }

            private async Task InitializeAsync(Rig rig)
            {
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
                builder.Logging.ClearProviders();
                builder.Services
                    .AddAuthentication(o =>
                    {
                        o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                        o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                builder.Services.AddAuthorization();
                builder.Services.AddRateLimiter(opt =>
                    opt.AddPolicy("dataverse-query", _ => RateLimitPartition.GetNoLimiter("dataverse-query-test")));

                var access = new SignalCoreRecordAccess(rig.User.Object, rig.Entities.Object, rig.Identity.Object, NullLogger<SignalCoreRecordAccess>.Instance);
                var schema = new RuleBodySchemaValidator();
                var validator = new PolicyVersionValidator(schema, new PredicateCompiler(schema, TimeProvider.System), NullLogger<PolicyVersionValidator>.Instance);
                builder.Services.AddSingleton(access);
                builder.Services.AddSingleton(new DecisionPlanService(rig.Entities.Object, NullLogger<DecisionPlanService>.Instance));
                builder.Services.AddSingleton(new RuleBodyDescriber(validator, new Mock<IGenericEntityService>().Object, NullLogger<RuleBodyDescriber>.Instance));
                builder.Services.AddSingleton(rig.PlanReads);
                builder.Services.AddSingleton(new SignalSuppressionService(
                    rig.Entities.Object, rig.WriterClient(), rig.Clock, new ConfigurationBuilder().Build(),
                    new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger<SignalSuppressionService>.Instance));
                builder.Services.AddSingleton(rig.User.Object);
                builder.Services.AddSingleton(rig.Service());
                // Task 013 (B-1): the plan and suppression routes now call the decision capability, registered as SignalsModule registers
                // it. This host maps no worklist route, so the capability's list collaborator is a stateless placeholder that is never called.
                builder.Services.AddSingleton((SignalReadService)RuntimeHelpers.GetUninitializedObject(typeof(SignalReadService)));
                builder.Services.AddScoped<IDecisionCapability, DecisionCapability>();

                builder.WebHost.UseTestServer();
                _app = builder.Build();
                _app.UseRouting();
                _app.UseAuthentication();
                _app.UseAuthorization();
                _app.UseRateLimiter();
                _app.MapDecisionPlanEndpoints();
                _app.MapDecisionCommitEndpoints();

                await _app.StartAsync();
                _client = _app.GetTestClient();
            }

            public async Task<GoldenContract.Observed> SendAsync(HttpMethod method, string url, object? body = null, bool authenticated = true)
            {
                using var request = new HttpRequestMessage(method, url);
                if (authenticated)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
                    request.Headers.Add(TestAuthHandler.CallerHeader, "1");
                }

                if (body is not null)
                {
                    request.Content = new StringContent(
                        JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json");
                }

                using var response = await _client!.SendAsync(request);
                return await GoldenContract.ObserveAsync(response);
            }

            public async ValueTask DisposeAsync()
            {
                _client?.Dispose();
                if (_app is not null)
                {
                    await _app.StopAsync();
                    await _app.DisposeAsync();
                }
            }
        }

        // ── helpers ───────────────────────────────────────────────────────────────────────────────────────────────

        private static async Task<GoldenContract.Observed> GetAsync(Rig rig, string route, Guid? signal = null, bool authenticated = true)
        {
            await using var host = await SignalsHost.StartAsync(rig);
            return await host.SendAsync(HttpMethod.Get, string.Format(System.Globalization.CultureInfo.InvariantCulture, route, signal ?? Primary), authenticated: authenticated);
        }

        private static async Task<GoldenContract.Observed> PostDecisionAsync(Rig rig, DecisionReview? review, Guid? signal = null, bool authenticated = true)
        {
            await using var host = await SignalsHost.StartAsync(rig);
            return await host.SendAsync(
                HttpMethod.Post, string.Format(System.Globalization.CultureInfo.InvariantCulture, DecisionRoute, signal ?? Primary), review, authenticated);
        }

        /// <summary>Asserts the case produced the status and reason code it is named for, then compares it with its golden file.</summary>
        private static void Verify(string caseName, GoldenContract.Observed observed, int status, string? reasonCode = null)
        {
            observed.Status.Should().Be(status, observed.Body);
            if (reasonCode is not null)
            {
                JsonDocument.Parse(observed.Body).RootElement.GetProperty("reasonCode").GetString().Should().Be(reasonCode, observed.Body);
            }

            GoldenContract.AssertMatches(caseName, observed);
        }

        private static void AddBadPlanSignal(Rig rig)
        {
            rig.AddSignal(Primary, DecisionLane.Decide, Matter, BadPlanVersion);
            rig.Entities.Setup(e => e.RetrieveAsync("sprk_policyversion", BadPlanVersion, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Entity("sprk_policyversion", BadPlanVersion) { ["sprk_decisionplan"] = "{\"actions\":[\"send-budget-inquiry\",\"bogus\"]}" });
        }

        /// <summary>A no-core Do item whose subject read faults (5xx): a fault of ours after the Signal was readable, never the uniform 404.</summary>
        private static Rig NoCoreSubjectReadFaults()
        {
            var rig = NoCoreRig();
            rig.SubjectReadFails = DataverseUserResponse.Fail(500, DataverseUserClientErrorCodes.ServiceError, "boom");
            return rig;
        }

        // ══════════════════════════════════════════════════════════════════════════════════════════════════════════
        // GET /api/v1/signals/{id}/decision-plan
        // ══════════════════════════════════════════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task Plan_200_PathB() =>
            Verify("plan.200.path-b", await GetAsync(new Rig(), PlanRoute), 200);

        [Fact]
        public async Task Plan_200_CreateBudgetTakesReviseBudgetsPlace_WhenTheMatterHasNoBudget()
        {
            var rig = new Rig { ActiveBudgets = 0 };
            var observed = await GetAsync(rig, PlanRoute);
            var codes = JsonDocument.Parse(observed.Body).RootElement.GetProperty("actions").EnumerateArray().Select(a => a.GetProperty("code").GetString()).ToList();
            codes.Should().Contain("create-budget").And.NotContain("revise-budget");
            Verify("plan.200.create-budget", observed, 200);
        }

        [Fact]
        public async Task Plan_200_DoLane()
        {
            var rig = new Rig();
            rig.AddSignal(DoSignal, DecisionLane.Do, Matter, DoVersion, subjectEntity: "sprk_todo", subjectId: ItemTodo);
            rig.TodoMatter[ItemTodo] = Matter;
            Verify("plan.200.do-lane", await GetAsync(rig, PlanRoute, DoSignal), 200);
        }

        [Fact]
        public async Task Plan_401_Anonymous() =>
            Verify("plan.401.anonymous", await GetAsync(new Rig(), PlanRoute, authenticated: false), 401);

        [Fact]
        public async Task Plan_403_CallerUnresolved() =>
            Verify("plan.403.caller-unresolved", await GetAsync(new Rig { CallerUnresolved = true }, PlanRoute), 403, "sdap.access.deny.caller_unresolved");

        [Fact]
        public async Task Plan_404_Uniform_SignalAndCoreUnreadableAreTheSameBytes()
        {
            var signalUnreadable = new Rig();
            signalUnreadable.CallerCannotRead.Add(Primary);
            var coreUnreadable = new Rig();
            coreUnreadable.CallerCannotReadCore.Add(Matter);

            var first = await GetAsync(signalUnreadable, PlanRoute);
            var second = await GetAsync(coreUnreadable, PlanRoute);

            Verify("plan.404.uniform", first, 404);
            GoldenContract.Normalize(second.Body, false).Should().Be(GoldenContract.Normalize(first.Body, false), "the uniform 404 never says which check failed");
            second.Status.Should().Be(first.Status);
        }

        [Fact]
        public async Task Plan_422_PlanDoesNotResolveAgainstTheCatalog()
        {
            var rig = new Rig();
            AddBadPlanSignal(rig);
            Verify("plan.422.refused", await GetAsync(rig, PlanRoute), 422, "ontology.decisionplan.refused");
        }

        [Fact]
        public async Task Plan_503_AccessUnavailable_WhenANoCoreSubjectReadFaults()
        {
            Verify("plan.503.access-unavailable", await GetAsync(NoCoreSubjectReadFaults(), PlanRoute, DoSignal), 503, "ontology.signal.access_unavailable");
        }

        // ══════════════════════════════════════════════════════════════════════════════════════════════════════════
        // GET /api/v1/signals/{id}/suppression
        // ══════════════════════════════════════════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task Suppression_401_Anonymous() =>
            Verify("suppression.401.anonymous", await GetAsync(new Rig(), SuppressionRoute, authenticated: false), 401);

        [Fact]
        public async Task Suppression_403_CallerUnresolved() =>
            Verify("suppression.403.caller-unresolved", await GetAsync(new Rig { CallerUnresolved = true }, SuppressionRoute), 403, "sdap.access.deny.caller_unresolved");

        [Fact]
        public async Task Suppression_404_Uniform()
        {
            var rig = new Rig();
            rig.CallerCannotRead.Add(Primary);
            Verify("suppression.404.uniform", await GetAsync(rig, SuppressionRoute), 404);
        }

        [Fact]
        public async Task Suppression_503_AccessUnavailable_WhenANoCoreSubjectReadFaults()
        {
            Verify("suppression.503.access-unavailable", await GetAsync(NoCoreSubjectReadFaults(), SuppressionRoute, DoSignal), 503, "ontology.signal.access_unavailable");
        }

        [Fact]
        public async Task Suppression_503_CountUnavailable_AfterAuthorization()
        {
            // The Rig scripts no suppression data: the authorised caller's count read fails, which is the fault answer (503), never a guess.
            Verify("suppression.503.unavailable", await GetAsync(new Rig(), SuppressionRoute), 503, "ontology.suppression.unavailable");
        }

        // ══════════════════════════════════════════════════════════════════════════════════════════════════════════
        // POST /api/v1/signals/{id}/decision
        // ══════════════════════════════════════════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task Decision_200_Recorded_PathB()
        {
            var rig = new Rig();
            var observed = await PostDecisionAsync(rig, PathBReview(reviewId: Rv(1)));
            rig.Records.Should().ContainSingle();
            Verify("decision.200.recorded", observed, 200);
        }

        [Fact]
        public async Task Decision_200_Dismissal()
        {
            var review = Dismissal("not-material") with { ReviewId = Rv(2) };
            Verify("decision.200.dismissal", await PostDecisionAsync(new Rig(), review), 200);
        }

        [Fact]
        public async Task Decision_200_RepeatedReviewId_ReturnsTheFirstOutcome_AndWritesNothing()
        {
            var rig = new Rig();
            var review = PathBReview(reviewId: Rv(3));
            (await PostDecisionAsync(rig, review)).Status.Should().Be(200);
            var writes = rig.Writes.Count();

            var repeat = await PostDecisionAsync(rig, review);

            repeat.Body.Should().Contain("\"replayed\":true");
            rig.Writes.Count().Should().Be(writes);
            Verify("decision.200.replayed", repeat, 200);
        }

        [Fact]
        public async Task Decision_401_Anonymous() =>
            Verify("decision.401.anonymous", await PostDecisionAsync(new Rig(), PathBReview(reviewId: Rv(4)), authenticated: false), 401);

        [Fact]
        public async Task Decision_403_CallerUnresolved() =>
            Verify("decision.403.caller-unresolved", await PostDecisionAsync(new Rig { CallerUnresolved = true }, PathBReview(reviewId: Rv(5))), 403, "sdap.access.deny.caller_unresolved");

        [Fact]
        public async Task Decision_403_NotAuthorized_CallerCannotAppendToTheMatter()
        {
            var rig = new Rig();
            rig.Rights[("sprk_matters", Matter)] = AccessRights.Read | AccessRights.Write;
            Verify("decision.403.not-authorized", await PostDecisionAsync(rig, Dismissal("not-material") with { ReviewId = Rv(6) }), 403, DecisionCommitReasons.NotAuthorized);
        }

        [Fact]
        public async Task Decision_403_ActionNotAuthorized_ThePreflightOfATakenAction()
        {
            var rig = new Rig();
            rig.Executors["send-budget-inquiry"].Refusal =
                DecisionActionOutcome.Refused("send-budget-inquiry", DecisionActionReasons.NotAuthorized, "You cannot file under this matter.");
            Verify("decision.403.action-not-authorized", await PostDecisionAsync(rig, PathBReview(reviewId: Rv(7))), 403, DecisionActionReasons.NotAuthorized);
        }

        [Fact]
        public async Task Decision_403_SignalSubjectMissing()
        {
            var rig = new Rig();
            rig.Signals[Primary]["sprk_regardingrecordid"] = ItemTodo.ToString("D");
            Verify("decision.403.signal-subject-missing", await PostDecisionAsync(rig, PathBReview(inquiry: false, todo: false, reviewId: Rv(8))), 403, DecisionCommitReasons.SignalSubjectMissing);
        }

        [Fact]
        public async Task Decision_404_Uniform_SignalAndCoreUnreadableAreTheSameBytes()
        {
            var signalUnreadable = new Rig();
            signalUnreadable.CallerCannotRead.Add(Primary);
            var coreUnreadable = new Rig();
            coreUnreadable.CallerCannotReadCore.Add(Matter);

            var first = await PostDecisionAsync(signalUnreadable, PathBReview(reviewId: Rv(9)));
            var second = await PostDecisionAsync(coreUnreadable, PathBReview(reviewId: Rv(9)));

            Verify("decision.404.uniform", first, 404);
            GoldenContract.Normalize(second.Body, false).Should().Be(GoldenContract.Normalize(first.Body, false), "the uniform 404 never says which check failed");
        }

        [Fact]
        public async Task Decision_409_SignalNotOpen()
        {
            var rig = new Rig();
            rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, Resolved, SignalWriter.ResolutionActed, Now.UtcDateTime.AddMinutes(-1));
            Verify("decision.409.signal-not-open", await PostDecisionAsync(rig, PathBReview(reviewId: Rv(10))), 409, DecisionCommitReasons.SignalNotOpen);
        }

        [Fact]
        public async Task Decision_409_ReviewInProgress()
        {
            var rig = new Rig();
            var held = await rig.Lease.TryAcquireAsync(DecisionCommitService.SignalLeaseId(Tenant, Primary), null, TimeSpan.FromMinutes(1), CancellationToken.None);
            held.Status.Should().Be(Spaarke.Scheduling.ScheduledJobLeaseStatus.Granted);
            Verify("decision.409.review-in-progress", await PostDecisionAsync(rig, PathBReview(reviewId: Rv(11))), 409, DecisionCommitReasons.ReviewInProgress);
        }

        [Fact]
        public async Task Decision_409_DecidedInInvoiceReview()
        {
            var rig = new Rig();
            rig.AddSignal(Primary, DecisionLane.Decide, Matter, PathB, subjectEntity: "sprk_billingevent", subjectId: BillingSubjectId);
            Verify("decision.409.decided-in-invoice-review", await PostDecisionAsync(rig, PathBReview(inquiry: false, todo: false, reviewId: Rv(12))), 409, DecisionCommitReasons.DecidedInInvoiceReview);
        }

        [Fact]
        public async Task Decision_409_InquiryDispositionRequired()
        {
            var rig = InquiryTodoRig(InquiryTodo, InquiryHint(ServiceRequestId));
            var review = MarkComplete() with { ReviewId = Rv(13) };
            Verify("decision.409.inquiry-disposition-required", await PostDecisionAsync(rig, review, DoSignal), 409, DecisionCommitReasons.InquiryDispositionRequired);
        }

        [Fact]
        public async Task Decision_422_ReviewInvalid_AlsoResolveIsCapped()
        {
            var also = Enumerable.Range(0, 26).Select(i => Guid.Parse("c0430011-0000-4000-8000-" + (100 + i).ToString("D12", System.Globalization.CultureInfo.InvariantCulture))).ToList();
            Verify("decision.422.review-invalid", await PostDecisionAsync(new Rig(), PathBReview(reviewId: Rv(14), also: also)), 422, DecisionCommitReasons.ReviewInvalid);
        }

        [Fact]
        public async Task Decision_422_AlsoResolveNotAllowed_NamesTheItemItself() =>
            Verify("decision.422.also-resolve-not-allowed", await PostDecisionAsync(new Rig(), PathBReview(reviewId: Rv(15), also: [Primary])), 422, DecisionCommitReasons.AlsoResolveNotAllowed);

        [Fact]
        public async Task Decision_422_NotConfirmed() =>
            Verify("decision.422.not-confirmed", await PostDecisionAsync(new Rig(), PathBReview(reviewId: Rv(16)) with { Confirmed = false }), 422, DecisionCommitReasons.NotConfirmed);

        [Fact]
        public async Task Decision_422_ActionsDoNotMatchPlan()
        {
            var review = new DecisionReview
            {
                ReviewId = Rv(17),
                Confirmed = true,
                Actions = [Take("send-budget-inquiry", InquiryValues), Skip("revise-budget")],
            };
            Verify("decision.422.actions-do-not-match-plan", await PostDecisionAsync(new Rig(), review), 422, DecisionCommitReasons.ActionsDoNotMatchPlan);
        }

        [Fact]
        public async Task Decision_422_UnresolvedAction()
        {
            var review = PathBReview(reviewId: Rv(18));
            review = review with { Actions = [.. review.Actions!, Take("escalate")] };
            Verify("decision.422.unresolved-action", await PostDecisionAsync(new Rig(), review), 422, DecisionCommitReasons.UnresolvedAction);
        }

        [Fact]
        public async Task Decision_422_InvalidParameter()
        {
            var review = PathBReview(reviewId: Rv(19)) with
            {
                Actions = [Take("send-budget-inquiry", Values(("to", "a@x.example"))), Skip("revise-budget"), Skip("approve-variance")],
            };
            Verify("decision.422.invalid-parameter", await PostDecisionAsync(new Rig(), review), 422, DecisionCommitReasons.InvalidParameter);
        }

        [Fact]
        public async Task Decision_422_ActionsExclude() =>
            Verify("decision.422.actions-exclude", await PostDecisionAsync(new Rig(), PathBReview(revise: true, approve: true, reviewId: Rv(20))), 422, DecisionCommitReasons.ActionsExclude);

        [Fact]
        public async Task Decision_422_DismissalReasonRequired() =>
            Verify("decision.422.dismissal-reason-required", await PostDecisionAsync(new Rig(), Dismissal(null) with { ReviewId = Rv(21) }), 422, DecisionCommitReasons.DismissalReasonRequired);

        [Fact]
        public async Task Decision_422_DismissalReasonUnknown() =>
            Verify("decision.422.dismissal-reason-unknown", await PostDecisionAsync(new Rig(), Dismissal("not-mine") with { ReviewId = Rv(22) }), 422, DecisionCommitReasons.DismissalReasonUnknown);

        [Fact]
        public async Task Decision_422_DismissalDetailRequired() =>
            Verify("decision.422.dismissal-detail-required", await PostDecisionAsync(new Rig(), Dismissal("other") with { ReviewId = Rv(23) }), 422, DecisionCommitReasons.DismissalDetailRequired);

        [Fact]
        public async Task Decision_422_DismissalWithActions()
        {
            var review = PathBReview(reviewId: Rv(24)) with { Dismissal = new DecisionReviewDismissal { Code = "not-material" } };
            Verify("decision.422.dismissal-with-actions", await PostDecisionAsync(new Rig(), review), 422, DecisionCommitReasons.DismissalWithActions);
        }

        [Fact]
        public async Task Decision_422_ReviewChanged_ARetryThatChangesTheReview()
        {
            var rig = new Rig();
            rig.Executors["add-todo"].Result = DecisionActionOutcome.Failed("add-todo", DecisionActionReasons.WriteFailed, "no");
            var review = PathBReview(inquiry: false, reviewId: Rv(25));
            (await PostDecisionAsync(rig, review)).Status.Should().Be(500);
            Verify("decision.422.review-changed", await PostDecisionAsync(rig, review with { Retry = true, Because = "something else" }), 422, DecisionCommitReasons.ReviewChanged);
        }

        [Fact]
        public async Task Decision_500_NotResumable_ARetryAfterAFailedWrite()
        {
            var rig = new Rig();
            rig.Executors["revise-budget"].Result = DecisionActionOutcome.Failed("revise-budget", DecisionActionReasons.WriteFailed,
                "The amount was not written", written: [new DecisionRecordRef("sprk_budgetrevision", RevisionId)]);
            var review = PathBReview(inquiry: false, reviewId: Rv(26));
            (await PostDecisionAsync(rig, review)).Status.Should().Be(500);
            rig.Executors["revise-budget"].Result = null;
            Verify("decision.500.not-resumable", await PostDecisionAsync(rig, review with { Retry = true }), 500, DecisionCommitReasons.NotResumable);
        }

        [Fact]
        public async Task Decision_422_PlanRefused_ThePlanDoesNotResolveAgainstTheCatalog()
        {
            var rig = new Rig();
            AddBadPlanSignal(rig);
            Verify("decision.422.plan-refused", await PostDecisionAsync(rig, PathBReview(reviewId: Rv(27))), 422, DecisionCommitReasons.PlanRefused);
        }

        [Fact]
        public async Task Decision_422_CoreChanged_TheCoreMovedBetweenTheCallersReadAndTheAppOnlyRead()
        {
            var rig = new Rig();
            rig.CallerSeesCore[Primary] = OtherMatter;
            Verify("decision.422.core-changed", await PostDecisionAsync(rig, PathBReview(reviewId: Rv(28))), 422, DecisionCommitReasons.CoreChanged);
        }

        [Fact]
        public async Task Decision_422_CoreUnsupported_TheCatalogRowIsGone()
        {
            var matterType = new Entity("sprk_recordtype_ref", TypeMatter) { ["sprk_recordlogicalname"] = "sprk_matter" };
            var rig = new Rig();
            rig.Entities.SetupSequence(e => e.RetrieveAsync("sprk_recordtype_ref", TypeMatter, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(matterType)
                .ThrowsAsync(new System.ServiceModel.FaultException<Microsoft.Xrm.Sdk.OrganizationServiceFault>(
                    new Microsoft.Xrm.Sdk.OrganizationServiceFault { ErrorCode = unchecked((int)0x80040217) }, "missing"));
            Verify("decision.422.core-unsupported", await PostDecisionAsync(rig, Dismissal("not-material") with { ReviewId = Rv(29) }), 422, DecisionCommitReasons.CoreUnsupported);
        }

        [Fact]
        public async Task Decision_503_CoreUnsupported_TheCoreTypeReadFaults()
        {
            // The caller's access check reads the record type first; the commit's own read is the second, and it faults: 503, where a
            // catalog row that is gone is 422 (the case above). Same code, different status.
            var matterType = new Entity("sprk_recordtype_ref", TypeMatter) { ["sprk_recordlogicalname"] = "sprk_matter" };
            var rig = new Rig();
            rig.Entities.SetupSequence(e => e.RetrieveAsync("sprk_recordtype_ref", TypeMatter, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(matterType)
                .ThrowsAsync(new TimeoutException("no answer"));
            Verify("decision.503.core-unsupported", await PostDecisionAsync(rig, Dismissal("not-material") with { ReviewId = Rv(40) }), 503, DecisionCommitReasons.CoreUnsupported);
        }

        [Fact]
        public async Task Decision_422_ItemOwnerUnsupported_TheResolverRefusesTheNoCoreItem()
        {
            var rig = NoCoreRig();
            rig.Owner = RecordOwnerResolution.Refused(RecordOwnerRefusal.SecureParentNotIsolated, "secret detail about the parent");
            Verify("decision.422.item-owner-unsupported", await PostDecisionAsync(rig, MarkComplete() with { ReviewId = Rv(30) }, DoSignal), 422, DecisionCommitReasons.ItemOwnerUnsupported);
        }

        [Fact]
        public async Task Decision_422_RecordRefused_TheDecisionRecordOwnerCannotBeResolved()
        {
            var rig = new Rig { Owner = RecordOwnerResolution.Refused(RecordOwnerRefusal.NoOwnerSource, "no team") };
            Verify("decision.422.record-refused", await PostDecisionAsync(rig, PathBReview(reviewId: Rv(31))), 422, DecisionCommitReasons.RecordRefused);
        }

        [Fact]
        public async Task Decision_500_StepFailed()
        {
            var rig = new Rig();
            rig.Executors["add-todo"].Result = DecisionActionOutcome.Failed("add-todo", DecisionActionReasons.WriteFailed, "no");
            Verify("decision.500.step-failed", await PostDecisionAsync(rig, PathBReview(inquiry: false, reviewId: Rv(32))), 500);
        }

        [Fact]
        public async Task Decision_500_RecordWriteFailed()
        {
            var rig = new Rig { FailRecordWith = new TimeoutException("no answer") };
            Verify("decision.500.record-write-failed", await PostDecisionAsync(rig, PathBReview(reviewId: Rv(33))), 500, DecisionCommitReasons.RecordWriteFailed);
        }

        [Fact]
        public async Task Decision_503_SignalUnreadable_TheTodosNotesCannotBeRead()
        {
            var rig = InquiryTodoRig(InquiryTodo, InquiryHint(ServiceRequestId));
            rig.TodoNotesReadFails = DataverseUserResponse.Fail(503, DataverseUserClientErrorCodes.ServiceError, "boom");
            Verify("decision.503.signal-unreadable", await PostDecisionAsync(rig, MarkComplete() with { ReviewId = Rv(34) }, DoSignal), 503, DecisionCommitReasons.SignalUnreadable);
        }

        [Fact]
        public async Task Decision_503_IdempotencyUnavailable_TheLedgerFaults()
        {
            var rig = new Rig { LedgerStore = new FaultingLedger() };
            Verify("decision.503.idempotency-unavailable", await PostDecisionAsync(rig, PathBReview(reviewId: Rv(35))), 503, DecisionCommitReasons.IdempotencyUnavailable);
        }

        [Fact]
        public async Task Decision_503_SecureFamilyUnavailable()
        {
            var rig = new Rig { FamiliesFault = new TimeoutException("no answer") };
            rig.AddSignal(Second, DecisionLane.Decide, Matter, PathB);
            Verify("decision.503.secure-family-unavailable", await PostDecisionAsync(rig, PathBReview(reviewId: Rv(36), also: [Second])), 503, DecisionCommitReasons.SecureFamilyUnavailable);
        }

        [Fact]
        public async Task Decision_503_ItemOwnerUnsupported_TheResolverFaults()
        {
            var rig = NoCoreRig();
            rig.OwnerFault = new TimeoutException("no answer");
            Verify("decision.503.item-owner-unsupported", await PostDecisionAsync(rig, MarkComplete() with { ReviewId = Rv(37) }, DoSignal), 503, DecisionCommitReasons.ItemOwnerUnsupported);
        }

        [Fact]
        public async Task Decision_503_RecordRefused_TheDecisionRecordOwnerResolverFaults()
        {
            var rig = new Rig { OwnerFault = new TimeoutException("no answer") };
            Verify("decision.503.record-refused", await PostDecisionAsync(rig, PathBReview(reviewId: Rv(38))), 503, DecisionCommitReasons.RecordRefused);
        }

        [Fact]
        public async Task Decision_503_WritersIdentityCannotBeRead_AnswersIdempotencyUnavailable()
        {
            // Pinned as it is today: the writer's WhoAmI fault surfaces as idempotency_unavailable, not record_refused.
            var rig = new Rig { WhoAmIFault = new TimeoutException("whoami") };
            Verify("decision.503.idempotency-unavailable-writer-identity", await PostDecisionAsync(rig, PathBReview(inquiry: false, todo: false, reviewId: Rv(39))), 503, DecisionCommitReasons.IdempotencyUnavailable);
        }

        /// <summary>A review ledger that cannot be reached: every call faults, so the idempotency check cannot be made.</summary>
        private sealed class FaultingLedger : ITenantCache
        {
            public Task<T?> GetAsync<T>(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default) =>
                Task.FromException<T?>(new TimeoutException("ledger"));

            public Task SetAsync<T>(string tenantId, string resource, string id, int version, T value, TimeSpan? ttl = null,
                string cacheInstance = "default", CancellationToken ct = default) => Task.FromException(new TimeoutException("ledger"));

            public Task RemoveAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default) =>
                Task.FromException(new TimeoutException("ledger"));

            public Task<T> GetOrCreateAsync<T>(string tenantId, string resource, string id, int version, Func<CancellationToken, Task<T>> factory,
                TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default) =>
                Task.FromException<T>(new TimeoutException("ledger"));
        }
    }
}
