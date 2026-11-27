// Regression anchor for GitHub issue #3307. POST /api/ai/search/records with searchIndexName set to an allowed index that is not a
// records index (a document index) answered 500 "Record Search Failed", with the index's own error text as the detail. It is the
// caller's choice of index, so it is now a 400 that names the index; and no 500 from either search route carries an exception's text.
//
// The index's answer is reproduced at the SearchClient double: an index without the records fields refuses the record-search request
// with a 400, as Azure AI Search does.

using System.Security.Claims;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.RecordSearch;
using Sprk.Bff.Api.Services.RecordMatching;
using Xunit;
using AzureSearchOptions = Azure.Search.Documents.SearchOptions;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

public sealed class Issue3307_RecordSearchOnANonRecordsIndexTests
{
    private const string Tenant = "a221a95e-6abc-4434-aecc-e48338a1b2f2";
    private const string RecordsIndex = "spaarke-records-index";
    private const string DocumentIndex = "spaarke-discovery-index";
    private const string IndexError = "Unknown field 'recordName' in highlight; the index has no such field.";

    private static RecordSearchService CreateService(string namedIndex, bool indexRefusesTheRequest)
    {
        var searchClient = new Mock<SearchClient>();
        searchClient
            .Setup(c => c.SearchAsync<SearchIndexDocument>(It.IsAny<string>(), It.IsAny<AzureSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, AzureSearchOptions _, CancellationToken _) =>
            {
                if (indexRefusesTheRequest)
                {
                    throw new RequestFailedException(400, IndexError);
                }

                return Response.FromValue(
                    SearchModelFactory.SearchResults(new List<SearchResult<SearchIndexDocument>>(), 0, null, null, null!), null!);
            });

        var deployment = new Mock<IKnowledgeDeploymentService>();
        deployment
            .Setup(d => d.GetSearchClientAsync(It.IsAny<string>(), namedIndex, It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchClient.Object);

        var embeddingCache = new Mock<IEmbeddingCache>();
        embeddingCache.Setup(e => e.ComputeContentHash(It.IsAny<string>())).Returns("hash");

        return new RecordSearchService(
            new Mock<SearchIndexClient>().Object,
            Mock.Of<IOpenAiClient>(),
            embeddingCache.Object,
            new Mock<ITenantCache>().Object,
            Options.Create(new DocumentIntelligenceOptions { AiSearchIndexName = RecordsIndex }),
            NullLogger<RecordSearchService>.Instance,
            deployment.Object,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AzureAd:TenantId"] = Tenant }).Build(),
            new Mock<IHttpContextAccessor>().Object);
    }

    private static RecordSearchRequest Request(string index) => new()
    {
        Query = "agreement",
        RecordTypes = [RecordEntityType.Matter],
        SearchIndexName = index,
        Options = new RecordSearchOptions { HybridMode = RecordHybridSearchMode.KeywordOnly },
    };

    [Fact]
    public async Task SearchAsync_OnAnAllowedIndexThatIsNotARecordsIndex_ThrowsA400NamingTheIndex()
    {
        var act = () => CreateService(DocumentIndex, indexRefusesTheRequest: true).SearchAsync(Request(DocumentIndex));

        var problem = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        problem.StatusCode.Should().Be(400);
        problem.Code.Should().Be(SearchErrorCodes.IndexNotRecordsIndex);
        problem.Detail.Should().Contain(DocumentIndex).And.NotContain(IndexError, "the index's own error text is not returned");
    }

    [Fact]
    public async Task SearchAsync_WhenTheConfiguredRecordsIndexRefusesTheRequest_StaysAServerError()
    {
        // A 400 from the records index itself is a defect in the request this service built, not the caller's choice of index.
        var act = () => CreateService(RecordsIndex, indexRefusesTheRequest: true).SearchAsync(Request(RecordsIndex));

        await act.Should().ThrowAsync<RequestFailedException>();
    }

    [Fact]
    public async Task SearchAsync_OnTheRecordsIndexNamedExplicitly_StillAnswers()
    {
        var response = await CreateService(RecordsIndex, indexRefusesTheRequest: false).SearchAsync(Request(RecordsIndex));

        response.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task PostRecordSearch_WhenTheSearchFails_Answers500WithoutTheExceptionText()
    {
        var result = await RecordSearchEndpoints.PostRecordSearch(
            Request(RecordsIndex),
            new FailingRecordSearch(),
            new AuthorizationService(Mock.Of<IAccessDataSource>(), Array.Empty<IAuthorizationRule>(), NullLogger<AuthorizationService>.Instance),
            CallerContext(),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var problem = result.Should().BeAssignableTo<IValueHttpResult>().Which.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(500);
        problem.Detail.Should().NotContain(IndexError);
    }

    private static DefaultHttpContext CallerContext()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("tid", Tenant), new Claim(CallerResolution.ObjectIdClaim, "cccccccc-3307-0000-0000-000000000001")], "Test")),
        };
        httpContext.Request.Headers.Authorization = "Bearer test-caller-token";
        httpContext.Items[RecordSearchAuthorization.HttpContextItemsKey] = new RecordSearchAuthorization
        {
            RequiresPerRowRecordAuthorization = true,
            RequestedEntitySets = new Dictionary<string, string> { [RecordEntityType.Matter] = "sprk_matters" },
        };
        return httpContext;
    }

    private sealed class FailingRecordSearch : IRecordSearchService
    {
        public Task<RecordSearchResponse> SearchAsync(RecordSearchRequest request, CancellationToken cancellationToken = default) =>
            throw new RequestFailedException(500, IndexError);
    }
}
