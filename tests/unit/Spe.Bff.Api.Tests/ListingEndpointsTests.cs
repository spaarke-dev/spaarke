using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Headers;
using System.Net;
using FluentAssertions;
using Newtonsoft.Json;
using Spe.Bff.Api.Models;
using Xunit;

namespace Spe.Bff.Api.Tests;

public class ListingEndpointsTests : IClassFixture<CustomWebAppFactory>
{
    private readonly HttpClient _client;

    public ListingEndpointsTests(CustomWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ListChildren_WithoutBearer_Returns401()
    {
        var response = await _client.GetAsync("/api/obo/containers/test-container/children");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListChildren_DefaultList_ReturnsSortedByNameAsc()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await _client.GetAsync("/api/obo/containers/test-container/children");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<ListingResponse>(content);

        result.Should().NotBeNull();
        result!.Items.Should().NotBeEmpty();
        result.Items.Count.Should().BeLessOrEqualTo(50); // Default top

        // Should be sorted by name ascending
        var names = result.Items.Select(x => x.Name).ToList();
        names.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task ListChildren_WithTopAndSkip_ReturnsCorrectPagination()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await _client.GetAsync("/api/obo/containers/test-container/children?top=10&skip=5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<ListingResponse>(content);

        result.Should().NotBeNull();
        result!.Items.Should().NotBeEmpty();
        result.Items.Count.Should().BeLessOrEqualTo(10);
    }

    [Fact]
    public async Task ListChildren_WithOrderBySize_ReturnsSortedBySize()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await _client.GetAsync("/api/obo/containers/test-container/children?orderBy=size&orderDir=desc");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<ListingResponse>(content);

        result.Should().NotBeNull();
        result!.Items.Should().NotBeEmpty();

        // Should be sorted by size descending (files only, folders have null size)
        var fileSizes = result.Items.Where(x => x.Size.HasValue).Select(x => x.Size!.Value).ToList();
        if (fileSizes.Count > 1)
        {
            fileSizes.Should().BeInDescendingOrder();
        }
    }

    [Fact]
    public async Task ListChildren_WithOrderByLastModified_ReturnsSortedByDate()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await _client.GetAsync("/api/obo/containers/test-container/children?orderBy=lastModifiedDateTime&orderDir=asc");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<ListingResponse>(content);

        result.Should().NotBeNull();
        result!.Items.Should().NotBeEmpty();

        // Should be sorted by last modified date ascending
        var dates = result.Items.Where(x => x.LastModifiedDateTime.HasValue).Select(x => x.LastModifiedDateTime!.Value).ToList();
        if (dates.Count > 1)
        {
            dates.Should().BeInAscendingOrder();
        }
    }

    [Fact]
    public async Task ListChildren_IncludesNextLink_WhenMoreResultsExist()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await _client.GetAsync("/api/obo/containers/test-container/children?top=10&skip=0");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<ListingResponse>(content);

        result.Should().NotBeNull();
        result!.NextLink.Should().NotBeNullOrEmpty();
        result.NextLink.Should().Contain("skip=10");
        result.NextLink.Should().Contain("top=10");
    }

    [Fact]
    public async Task ListChildren_ItemDtoHasCorrectStructure()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await _client.GetAsync("/api/obo/containers/test-container/children?top=5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<ListingResponse>(content);

        result.Should().NotBeNull();
        result!.Items.Should().NotBeEmpty();

        var item = result.Items.First();
        item.Id.Should().NotBeNullOrEmpty();
        item.Name.Should().NotBeNullOrEmpty();
        item.ETag.Should().NotBeNullOrEmpty();
        item.LastModifiedDateTime.Should().NotBeNull();

        // Files should have size and contentType, folders should have Folder property
        if (item.Folder != null)
        {
            item.Size.Should().BeNull();
            item.ContentType.Should().BeNull();
            item.Folder.Should().NotBeNull();
        }
        else
        {
            item.Size.Should().NotBeNull();
            item.ContentType.Should().NotBeNullOrEmpty();
            item.Folder.Should().BeNull();
        }
    }

    [Fact]
    public async Task ListChildren_InvalidOrderBy_DefaultsToName()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await _client.GetAsync("/api/obo/containers/test-container/children?orderBy=invalid");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<ListingResponse>(content);

        result.Should().NotBeNull();
        result!.Items.Should().NotBeEmpty();

        // Should default to name sorting
        var names = result.Items.Select(x => x.Name).ToList();
        names.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task ListChildren_MaxTop200_EnforcesLimit()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await _client.GetAsync("/api/obo/containers/test-container/children?top=500");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonConvert.DeserializeObject<ListingResponse>(content);

        result.Should().NotBeNull();
        result!.Items.Should().NotBeEmpty();
        result.Items.Count.Should().BeLessOrEqualTo(200); // Max limit should be enforced
    }
}