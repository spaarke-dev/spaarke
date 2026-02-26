using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai.SemanticSearch;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.SemanticSearch;

/// <summary>
/// Unit tests for SearchFilterBuilder.
/// Validates OData filter generation, tenant isolation, scope filtering, and injection prevention.
/// </summary>
public class SearchFilterBuilderTests
{
    private const string TestTenantId = "test-tenant-123";
    private const string TestEntityType = "matter";
    private const string TestEntityId = "00000000-0000-0000-0000-000000000001";

    #region BuildFilter - Tenant Isolation Tests

    [Fact]
    public void BuildFilter_AlwaysIncludesTenantFilter()
    {
        var filter = SearchFilterBuilder.BuildFilter(
            TestTenantId,
            "entity",
            TestEntityType,
            TestEntityId,
            null,
            null);

        Assert.Contains($"tenantId eq '{TestTenantId}'", filter);
    }

    [Fact]
    public void BuildFilter_EscapesTenantIdWithSingleQuote()
    {
        var tenantId = "tenant-with'quote";
        var filter = SearchFilterBuilder.BuildFilter(
            tenantId,
            "entity",
            TestEntityType,
            TestEntityId,
            null,
            null);

        // Single quotes should be escaped as double quotes
        Assert.Contains("tenant-with''quote", filter);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildFilter_ThrowsOnNullOrEmptyTenantId(string? tenantId)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            SearchFilterBuilder.BuildFilter(tenantId!, "entity", TestEntityType, TestEntityId, null, null));
    }

    #endregion

    #region BuildFilter - Entity Scope Tests

    [Fact]
    public void BuildFilter_EntityScope_GeneratesCorrectFilter()
    {
        var filter = SearchFilterBuilder.BuildFilter(
            TestTenantId,
            "entity",
            TestEntityType,
            TestEntityId,
            null,
            null);

        Assert.Contains($"tenantId eq '{TestTenantId}'", filter);
        Assert.Contains($"parentEntityType eq '{TestEntityType}'", filter);
        Assert.Contains($"parentEntityId eq '{TestEntityId}'", filter);
    }

    [Theory]
    [InlineData("Matter", "matter")]
    [InlineData("PROJECT", "project")]
    [InlineData("Invoice", "invoice")]
    [InlineData("ACCOUNT", "account")]
    [InlineData("contact", "contact")]
    public void BuildFilter_EntityScope_NormalizesEntityType(string inputType, string expectedType)
    {
        var filter = SearchFilterBuilder.BuildFilter(
            TestTenantId,
            "entity",
            inputType,
            TestEntityId,
            null,
            null);

        Assert.Contains($"parentEntityType eq '{expectedType}'", filter);
    }

    [Fact]
    public void BuildFilter_EntityScope_ThrowsOnMissingEntityType()
    {
        Assert.Throws<ArgumentException>(() =>
            SearchFilterBuilder.BuildFilter(TestTenantId, "entity", null, TestEntityId, null, null));
    }

    [Fact]
    public void BuildFilter_EntityScope_ThrowsOnMissingEntityId()
    {
        Assert.Throws<ArgumentException>(() =>
            SearchFilterBuilder.BuildFilter(TestTenantId, "entity", TestEntityType, null, null, null));
    }

    #endregion

    #region BuildFilter - DocumentIds Scope Tests

    [Fact]
    public void BuildFilter_DocumentIdsScope_UsesSearchIn()
    {
        var documentIds = new List<string> { "doc1", "doc2", "doc3" };

        var filter = SearchFilterBuilder.BuildFilter(
            TestTenantId,
            "documentIds",
            null,
            null,
            documentIds,
            null);

        Assert.Contains("search.in(documentId,", filter);
        Assert.Contains("doc1", filter);
        Assert.Contains("doc2", filter);
        Assert.Contains("doc3", filter);
    }

    [Fact]
    public void BuildFilter_DocumentIdsScope_ThrowsOnEmptyList()
    {
        Assert.Throws<ArgumentException>(() =>
            SearchFilterBuilder.BuildFilter(TestTenantId, "documentIds", null, null, new List<string>(), null));
    }

    [Fact]
    public void BuildFilter_DocumentIdsScope_ThrowsOnNullList()
    {
        Assert.Throws<ArgumentException>(() =>
            SearchFilterBuilder.BuildFilter(TestTenantId, "documentIds", null, null, null, null));
    }

    [Fact]
    public void BuildFilter_DocumentIdsScope_CaseInsensitive()
    {
        var documentIds = new List<string> { "doc1" };

        // Both "documentIds" and "DOCUMENTIDS" should work
        var filter1 = SearchFilterBuilder.BuildFilter(TestTenantId, "documentIds", null, null, documentIds, null);
        var filter2 = SearchFilterBuilder.BuildFilter(TestTenantId, "DOCUMENTIDS", null, null, documentIds, null);

        Assert.Contains("search.in(documentId,", filter1);
        Assert.Contains("search.in(documentId,", filter2);
    }

    #endregion

    #region BuildFilter - All Scope Tests

    [Fact]
    public void BuildFilter_AllScope_IncludesTenantFilter()
    {
        var filter = SearchFilterBuilder.BuildFilter(
            TestTenantId, "all", null, null, null, null);

        Assert.Contains($"tenantId eq '{TestTenantId}'", filter);
    }

    [Fact]
    public void BuildFilter_AllScope_DoesNotIncludeParentEntityTypeFilter()
    {
        var filter = SearchFilterBuilder.BuildFilter(
            TestTenantId, "all", null, null, null, null);

        Assert.DoesNotContain("parentEntityType", filter);
        Assert.DoesNotContain("parentEntityId", filter);
    }

    [Fact]
    public void BuildFilter_AllScope_OnlyContainsTenantFilter()
    {
        var filter = SearchFilterBuilder.BuildFilter(
            TestTenantId, "all", null, null, null, null);

        Assert.Equal($"tenantId eq '{TestTenantId}'", filter);
    }

    [Fact]
    public void BuildFilter_AllScope_CaseInsensitive()
    {
        var filter1 = SearchFilterBuilder.BuildFilter(TestTenantId, "all", null, null, null, null);
        var filter2 = SearchFilterBuilder.BuildFilter(TestTenantId, "ALL", null, null, null, null);
        var filter3 = SearchFilterBuilder.BuildFilter(TestTenantId, "All", null, null, null, null);

        Assert.Equal(filter1, filter2);
        Assert.Equal(filter1, filter3);
    }

    [Fact]
    public void BuildFilter_AllScope_WithOptionalFilters()
    {
        var filters = new SearchFilters
        {
            DocumentTypes = new List<string> { "contract" },
            FileTypes = new List<string> { "pdf" }
        };

        var filter = SearchFilterBuilder.BuildFilter(
            TestTenantId, "all", null, null, null, filters);

        Assert.Contains($"tenantId eq '{TestTenantId}'", filter);
        Assert.Contains("search.in(documentType,", filter);
        Assert.Contains("search.in(fileType,", filter);
        Assert.DoesNotContain("parentEntityType", filter);
    }

    [Fact]
    public void BuildScopeFilter_AllScope_ReturnsNull()
    {
        var result = SearchFilterBuilder.BuildScopeFilter("all", null, null, null);
        Assert.Null(result);
    }

    #endregion

    #region BuildFilter - Invalid Scope Tests

    [Theory]
    [InlineData("invalid")]
    [InlineData("")]
    public void BuildFilter_ThrowsOnInvalidScope(string scope)
    {
        Assert.Throws<ArgumentException>(() =>
            SearchFilterBuilder.BuildFilter(TestTenantId, scope, null, null, null, null));
    }

    [Fact]
    public void BuildFilter_ThrowsOnNullScope()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            SearchFilterBuilder.BuildFilter(TestTenantId, null!, null, null, null, null));
    }

    #endregion

    #region BuildFilter - Optional Filters Tests

    [Fact]
    public void BuildFilter_WithDocumentTypesFilter()
    {
        var filters = new SearchFilters
        {
            DocumentTypes = new List<string> { "contract", "agreement" }
        };

        var filter = SearchFilterBuilder.BuildFilter(
            TestTenantId, "entity", TestEntityType, TestEntityId, null, filters);

        Assert.Contains("search.in(documentType, 'contract,agreement', ',')", filter);
    }

    [Fact]
    public void BuildFilter_WithFileTypesFilter()
    {
        var filters = new SearchFilters
        {
            FileTypes = new List<string> { "PDF", "DOCX" }
        };

        var filter = SearchFilterBuilder.BuildFilter(
            TestTenantId, "entity", TestEntityType, TestEntityId, null, filters);

        // File types should be lowercased
        Assert.Contains("search.in(fileType, 'pdf,docx', ',')", filter);
    }

    [Fact]
    public void BuildFilter_WithTagsFilter()
    {
        var filters = new SearchFilters
        {
            Tags = new List<string> { "important", "urgent" }
        };

        var filter = SearchFilterBuilder.BuildFilter(
            TestTenantId, "entity", TestEntityType, TestEntityId, null, filters);

        Assert.Contains("tags/any(t: t eq 'important')", filter);
        Assert.Contains("tags/any(t: t eq 'urgent')", filter);
        Assert.Contains(" or ", filter);
    }

    [Fact]
    public void BuildFilter_WithDateRangeFilter_FromOnly()
    {
        var fromDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var filters = new SearchFilters
        {
            DateRange = new DateRangeFilter { From = fromDate }
        };

        var filter = SearchFilterBuilder.BuildFilter(
            TestTenantId, "entity", TestEntityType, TestEntityId, null, filters);

        Assert.Contains("createdAt ge", filter);
        Assert.Contains("2024-01-01", filter);
    }

    [Fact]
    public void BuildFilter_WithDateRangeFilter_ToOnly()
    {
        var toDate = new DateTimeOffset(2024, 12, 31, 23, 59, 59, TimeSpan.Zero);
        var filters = new SearchFilters
        {
            DateRange = new DateRangeFilter { To = toDate }
        };

        var filter = SearchFilterBuilder.BuildFilter(
            TestTenantId, "entity", TestEntityType, TestEntityId, null, filters);

        Assert.Contains("createdAt le", filter);
        Assert.Contains("2024-12-31", filter);
    }

    [Fact]
    public void BuildFilter_WithDateRangeFilter_BothFromAndTo()
    {
        var fromDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var toDate = new DateTimeOffset(2024, 12, 31, 23, 59, 59, TimeSpan.Zero);
        var filters = new SearchFilters
        {
            DateRange = new DateRangeFilter { From = fromDate, To = toDate }
        };

        var filter = SearchFilterBuilder.BuildFilter(
            TestTenantId, "entity", TestEntityType, TestEntityId, null, filters);

        Assert.Contains("createdAt ge", filter);
        Assert.Contains("createdAt le", filter);
        Assert.Contains(" and ", filter);
    }

    [Theory]
    [InlineData("createdAt", "createdAt")]
    [InlineData("updatedAt", "updatedAt")]
    [InlineData("CREATEDAT", "createdAt")]
    [InlineData("UPDATEDAT", "updatedAt")]
    public void BuildFilter_WithDateRangeFilter_FieldNormalization(string inputField, string expectedField)
    {
        var filters = new SearchFilters
        {
            DateRange = new DateRangeFilter
            {
                Field = inputField,
                From = DateTimeOffset.UtcNow
            }
        };

        var filter = SearchFilterBuilder.BuildFilter(
            TestTenantId, "entity", TestEntityType, TestEntityId, null, filters);

        Assert.Contains($"{expectedField} ge", filter);
    }

    [Fact]
    public void BuildFilter_WithAllOptionalFilters()
    {
        var filters = new SearchFilters
        {
            DocumentTypes = new List<string> { "contract" },
            FileTypes = new List<string> { "pdf" },
            Tags = new List<string> { "important" },
            DateRange = new DateRangeFilter { From = DateTimeOffset.UtcNow }
        };

        var filter = SearchFilterBuilder.BuildFilter(
            TestTenantId, "entity", TestEntityType, TestEntityId, null, filters);

        Assert.Contains("tenantId eq", filter);
        Assert.Contains("parentEntityType eq", filter);
        Assert.Contains("search.in(documentType,", filter);
        Assert.Contains("search.in(fileType,", filter);
        Assert.Contains("tags/any", filter);
        Assert.Contains("createdAt ge", filter);
    }

    #endregion

    #region Injection Prevention Tests

    [Fact]
    public void EscapeODataValue_EscapesSingleQuotes()
    {
        var maliciousInput = "test'injection";
        var escaped = SearchFilterBuilder.EscapeODataValue(maliciousInput);

        Assert.Equal("test''injection", escaped);
    }

    [Fact]
    public void EscapeODataValue_EscapesMultipleSingleQuotes()
    {
        var maliciousInput = "test''multiple'''quotes";
        var escaped = SearchFilterBuilder.EscapeODataValue(maliciousInput);

        Assert.Equal("test''''multiple''''''quotes", escaped);
    }

    [Fact]
    public void EscapeODataValue_HandlesEmptyString()
    {
        Assert.Equal("", SearchFilterBuilder.EscapeODataValue(""));
    }

    [Fact]
    public void EscapeODataValue_HandlesNormalString()
    {
        var normalInput = "normal-value-123";
        Assert.Equal(normalInput, SearchFilterBuilder.EscapeODataValue(normalInput));
    }

    [Fact]
    public void BuildFilter_PreventsInjectionInEntityType()
    {
        // Attempt SQL/OData injection via entityType
        var maliciousEntityType = "matter' or 1 eq 1 or parentEntityType eq '";

        var filter = SearchFilterBuilder.BuildFilter(
            TestTenantId, "entity", maliciousEntityType, TestEntityId, null, null);

        // The single quotes should be escaped as double quotes
        // This makes the injection harmless as it becomes a literal string value
        Assert.Contains("matter'' or 1 eq 1", filter);
        Assert.Contains("''", filter); // Escaped quotes present
    }

    [Fact]
    public void BuildFilter_PreventsInjectionInDocumentIds()
    {
        // Attempt SQL/OData injection via documentIds
        var maliciousIds = new List<string> { "doc1', 'doc2') or 1 eq 1 --" };

        var filter = SearchFilterBuilder.BuildFilter(
            TestTenantId, "documentIds", null, null, maliciousIds, null);

        // The single quotes should be escaped as double quotes
        Assert.Contains("doc1''", filter);
        Assert.Contains("''doc2", filter);
    }

    [Fact]
    public void BuildFilter_PreventsInjectionInTags()
    {
        // Attempt OData injection via tags
        var filters = new SearchFilters
        {
            Tags = new List<string> { "tag') or 1 eq 1 --" }
        };

        var filter = SearchFilterBuilder.BuildFilter(
            TestTenantId, "entity", TestEntityType, TestEntityId, null, filters);

        // The single quotes should be escaped as double quotes
        Assert.Contains("tag'')", filter);
    }

    #endregion

    #region BuildFilter - EntityTypes Filter Tests

    [Fact]
    public void BuildEntityTypesFilter_SingleValue_GeneratesSearchIn()
    {
        var result = SearchFilterBuilder.BuildEntityTypesFilter(new List<string> { "matter" });
        Assert.Equal("search.in(parentEntityType, 'matter', ',')", result);
    }

    [Fact]
    public void BuildEntityTypesFilter_MultipleValues_GeneratesSearchIn()
    {
        var result = SearchFilterBuilder.BuildEntityTypesFilter(new List<string> { "matter", "project", "invoice" });
        Assert.Equal("search.in(parentEntityType, 'matter,project,invoice', ',')", result);
    }

    [Fact]
    public void BuildEntityTypesFilter_NormalizesToLowercase()
    {
        var result = SearchFilterBuilder.BuildEntityTypesFilter(new List<string> { "Matter", "PROJECT" });
        Assert.Equal("search.in(parentEntityType, 'matter,project', ',')", result);
    }

    [Fact]
    public void BuildEntityTypesFilter_ReturnsNullForNull()
    {
        var result = SearchFilterBuilder.BuildEntityTypesFilter(null);
        Assert.Null(result);
    }

    [Fact]
    public void BuildEntityTypesFilter_ReturnsNullForEmptyList()
    {
        var result = SearchFilterBuilder.BuildEntityTypesFilter(new List<string>());
        Assert.Null(result);
    }

    [Fact]
    public void BuildFilter_AllScope_WithEntityTypesFilter()
    {
        var filters = new SearchFilters
        {
            EntityTypes = new List<string> { "matter", "project" }
        };

        var filter = SearchFilterBuilder.BuildFilter(
            TestTenantId, "all", null, null, null, filters);

        Assert.Contains($"tenantId eq '{TestTenantId}'", filter);
        Assert.Contains("search.in(parentEntityType, 'matter,project', ',')", filter);
        // Should not have parentEntityId since it's scope=all
        Assert.DoesNotContain("parentEntityId", filter);
    }

    [Fact]
    public void BuildFilter_AllScope_WithEntityTypesAndOtherFilters()
    {
        var filters = new SearchFilters
        {
            EntityTypes = new List<string> { "matter" },
            DocumentTypes = new List<string> { "contract" },
            FileTypes = new List<string> { "pdf" }
        };

        var filter = SearchFilterBuilder.BuildFilter(
            TestTenantId, "all", null, null, null, filters);

        Assert.Contains($"tenantId eq '{TestTenantId}'", filter);
        Assert.Contains("search.in(parentEntityType, 'matter', ',')", filter);
        Assert.Contains("search.in(documentType,", filter);
        Assert.Contains("search.in(fileType,", filter);
    }

    [Fact]
    public void BuildFilter_EntityScope_WithEntityTypesFilter_BothApplied()
    {
        // When scope=entity AND entityTypes filter provided, both apply
        // The scope filter gives parentEntityType eq 'matter' AND parentEntityId eq 'id'
        // The entityTypes filter gives search.in(parentEntityType, 'matter,project', ',')
        // Both are included in the filter
        var filters = new SearchFilters
        {
            EntityTypes = new List<string> { "matter", "project" }
        };

        var filter = SearchFilterBuilder.BuildFilter(
            TestTenantId, "entity", TestEntityType, TestEntityId, null, filters);

        Assert.Contains("parentEntityType eq 'matter'", filter);
        Assert.Contains("parentEntityId eq", filter);
        Assert.Contains("search.in(parentEntityType, 'matter,project', ',')", filter);
    }

    #endregion

    #region Individual Method Tests

    [Fact]
    public void BuildTenantFilter_GeneratesCorrectFormat()
    {
        var result = SearchFilterBuilder.BuildTenantFilter("tenant-123");
        Assert.Equal("tenantId eq 'tenant-123'", result);
    }

    [Fact]
    public void BuildEntityScopeFilter_GeneratesCorrectFormat()
    {
        var result = SearchFilterBuilder.BuildEntityScopeFilter("matter", "guid-123");
        Assert.Equal("parentEntityType eq 'matter' and parentEntityId eq 'guid-123'", result);
    }

    [Fact]
    public void BuildDocumentIdsScopeFilter_GeneratesCorrectFormat()
    {
        var ids = new List<string> { "id1", "id2" };
        var result = SearchFilterBuilder.BuildDocumentIdsScopeFilter(ids);
        Assert.Equal("search.in(documentId, 'id1,id2', ',')", result);
    }

    [Fact]
    public void BuildDocumentTypesFilter_ReturnsNullForEmptyList()
    {
        var result = SearchFilterBuilder.BuildDocumentTypesFilter(new List<string>());
        Assert.Null(result);
    }

    [Fact]
    public void BuildDocumentTypesFilter_ReturnsNullForNull()
    {
        var result = SearchFilterBuilder.BuildDocumentTypesFilter(null);
        Assert.Null(result);
    }

    [Fact]
    public void BuildFileTypesFilter_LowercasesValues()
    {
        var result = SearchFilterBuilder.BuildFileTypesFilter(new List<string> { "PDF", "DOCX" });
        Assert.Equal("search.in(fileType, 'pdf,docx', ',')", result);
    }

    [Fact]
    public void BuildTagsFilter_UsesOrForMultipleTags()
    {
        var result = SearchFilterBuilder.BuildTagsFilter(new List<string> { "a", "b" });
        Assert.Equal("(tags/any(t: t eq 'a') or tags/any(t: t eq 'b'))", result);
    }

    [Fact]
    public void BuildDateRangeFilter_ReturnsNullForNull()
    {
        var result = SearchFilterBuilder.BuildDateRangeFilter(null);
        Assert.Null(result);
    }

    [Fact]
    public void BuildDateRangeFilter_ReturnsNullForEmptyRange()
    {
        var result = SearchFilterBuilder.BuildDateRangeFilter(new DateRangeFilter());
        Assert.Null(result);
    }

    #endregion
}
