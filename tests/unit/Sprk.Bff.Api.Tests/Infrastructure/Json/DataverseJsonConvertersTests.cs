using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Json;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.Json;

[Trait("status", "repaired")]
public class DataverseJsonConvertersTests
{
    [Fact]
    public void BracedGuidConverter_DeserializesBracedGuid()
    {
        // Arrange - Dataverse sends GUIDs with braces
        var guid = Guid.NewGuid();
        var json = $@"{{ ""Id"": ""{{{guid}}}"" }}";

        // Act
        var result = JsonSerializer.Deserialize<TestGuidModel>(json, DataverseJsonOptions.Default);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(guid);
    }

    [Fact]
    public void BracedGuidConverter_DeserializesUnbracedGuid()
    {
        // Arrange - Standard GUID format still works
        var guid = Guid.NewGuid();
        var json = $@"{{ ""Id"": ""{guid}"" }}";

        // Act
        var result = JsonSerializer.Deserialize<TestGuidModel>(json, DataverseJsonOptions.Default);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(guid);
    }

    [Fact]
    public void BracedGuidConverter_SerializesWithoutBraces()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var model = new TestGuidModel { Id = guid };

        // Act
        var json = JsonSerializer.Serialize(model, DataverseJsonOptions.Default);

        // Assert
        json.Should().Contain($"\"{guid}\"");
        json.Should().NotContain($"{{{{{guid}}}}}"); // Should NOT have braces
    }

    [Fact]
    public void BracedGuidConverter_HandlesEmptyGuid()
    {
        // Arrange
        var json = @"{ ""Id"": ""00000000-0000-0000-0000-000000000000"" }";

        // Act
        var result = JsonSerializer.Deserialize<TestGuidModel>(json, DataverseJsonOptions.Default);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(Guid.Empty);
    }

    [Fact]
    public void NullableBracedGuidConverter_DeserializesBracedGuid()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var json = $@"{{ ""NullableId"": ""{{{guid}}}"" }}";

        // Act
        var result = JsonSerializer.Deserialize<TestNullableGuidModel>(json, DataverseJsonOptions.Default);

        // Assert
        result.Should().NotBeNull();
        result!.NullableId.Should().Be(guid);
    }

    [Fact]
    public void NullableBracedGuidConverter_DeserializesNull()
    {
        // Arrange
        var json = @"{ ""NullableId"": null }";

        // Act
        var result = JsonSerializer.Deserialize<TestNullableGuidModel>(json, DataverseJsonOptions.Default);

        // Assert
        result.Should().NotBeNull();
        result!.NullableId.Should().BeNull();
    }

    [Fact]
    public void NullableBracedGuidConverter_DeserializesEmptyString()
    {
        // Arrange - Empty string should be treated as null
        var json = @"{ ""NullableId"": """" }";

        // Act
        var result = JsonSerializer.Deserialize<TestNullableGuidModel>(json, DataverseJsonOptions.Default);

        // Assert
        result.Should().NotBeNull();
        result!.NullableId.Should().BeNull();
    }

    [Fact]
    public void NullableBracedGuidConverter_SerializesNull()
    {
        // Arrange
        var model = new TestNullableGuidModel { NullableId = null };

        // Act
        var json = JsonSerializer.Serialize(model, DataverseJsonOptions.Default);

        // Assert
        json.Should().Contain("null");
    }

    [Fact]
    public void DataverseJsonOptions_Default_IsCaseInsensitive()
    {
        // Arrange - Dataverse uses PascalCase property names
        var guid = Guid.NewGuid();
        var json = $@"{{ ""id"": ""{guid}"" }}"; // lowercase

        // Act
        var result = JsonSerializer.Deserialize<TestGuidModel>(json, DataverseJsonOptions.Default);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(guid);
    }

    private class TestGuidModel
    {
        public Guid Id { get; set; }
    }

    private class TestNullableGuidModel
    {
        public Guid? NullableId { get; set; }
    }

    private class TestDateTimeModel
    {
        public DateTime Date { get; set; }
    }

    private class TestNullableDateTimeModel
    {
        public DateTime? NullableDate { get; set; }
    }

    // WCF DateTime Converter Tests

    [Fact]
    public void WcfDateTimeConverter_DeserializesWcfFormat()
    {
        // Arrange - WCF format: /Date(1234567890000)/
        // 1234567890000 ms = 2009-02-13T23:31:30.000Z
        var json = @"{ ""Date"": ""/Date(1234567890000)/"" }";

        // Act
        var result = JsonSerializer.Deserialize<TestDateTimeModel>(json, DataverseJsonOptions.Default);

        // Assert
        result.Should().NotBeNull();
        result!.Date.Should().Be(new DateTime(2009, 2, 13, 23, 31, 30, DateTimeKind.Utc));
    }

    [Fact]
    public void WcfDateTimeConverter_DeserializesWcfFormatWithTimezone()
    {
        // Arrange - WCF format with timezone: /Date(1234567890000+0000)/
        var json = @"{ ""Date"": ""/Date(1234567890000+0000)/"" }";

        // Act
        var result = JsonSerializer.Deserialize<TestDateTimeModel>(json, DataverseJsonOptions.Default);

        // Assert
        result.Should().NotBeNull();
        result!.Date.Should().Be(new DateTime(2009, 2, 13, 23, 31, 30, DateTimeKind.Utc));
    }

    [Fact]
    public void WcfDateTimeConverter_DeserializesIso8601Format()
    {
        // Arrange - Standard ISO 8601 format still works
        var json = @"{ ""Date"": ""2026-01-12T15:30:00Z"" }";

        // Act
        var result = JsonSerializer.Deserialize<TestDateTimeModel>(json, DataverseJsonOptions.Default);

        // Assert
        result.Should().NotBeNull();
        result!.Date.Year.Should().Be(2026);
        result.Date.Month.Should().Be(1);
        result.Date.Day.Should().Be(12);
    }

    [Fact]
    public void NullableWcfDateTimeConverter_DeserializesNull()
    {
        // Arrange
        var json = @"{ ""NullableDate"": null }";

        // Act
        var result = JsonSerializer.Deserialize<TestNullableDateTimeModel>(json, DataverseJsonOptions.Default);

        // Assert
        result.Should().NotBeNull();
        result!.NullableDate.Should().BeNull();
    }

    [Fact]
    public void NullableWcfDateTimeConverter_DeserializesWcfFormat()
    {
        // Arrange
        var json = @"{ ""NullableDate"": ""/Date(1234567890000)/"" }";

        // Act
        var result = JsonSerializer.Deserialize<TestNullableDateTimeModel>(json, DataverseJsonOptions.Default);

        // Assert
        result.Should().NotBeNull();
        result!.NullableDate.Should().Be(new DateTime(2009, 2, 13, 23, 31, 30, DateTimeKind.Utc));
    }

    [Fact]
    public void WcfDateTimeConverter_SerializesAsIso8601()
    {
        // Arrange
        var model = new TestDateTimeModel { Date = new DateTime(2026, 1, 12, 15, 30, 0, DateTimeKind.Utc) };

        // Act
        var json = JsonSerializer.Serialize(model, DataverseJsonOptions.Default);

        // Assert
        json.Should().Contain("2026-01-12");
        json.Should().NotContain("/Date(");
    }
}
