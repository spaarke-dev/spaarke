using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// The <c>sprk_invoice.sprk_trackingnumber</c> column constants (ebilling-r1 task 049 part A, D-124): the invoice's copy of the D-87
/// tracking number must always fit what the allocator can produce, or the invoice create inside 049's transaction would fault on length.
/// </summary>
/// <remarks>Maintain-class (ADR-038): the length is a contract between the schema script, the allocator's cap and the processor's copy.</remarks>
public sealed class InvoiceTrackingNumberColumnTests
{
    [Fact]
    public void LogicalName_IsTheSubmissionsColumnName_ByDesign()
    {
        InvoiceReviewService.InvTrackingNumber.Should().Be("sprk_trackingnumber");
        InvoiceReviewService.InvTrackingNumber.Should().Be(TrackingNumberAllocator.TrackingNumberColumn);
    }

    [Fact]
    public void MaxLength_FitsTheLongestTrackingNumberTheAllocatorCanProduce()
    {
        // {firm invoice number, at most MaxFirmInvoiceNumberLength} + "." + {n, at most the 10 digits of int.MaxValue}.
        var longest = new string('X', TrackingNumberAllocator.MaxFirmInvoiceNumberLength) + "." + int.MaxValue.ToString();

        longest.Length.Should().BeLessThanOrEqualTo(InvoiceReviewService.InvTrackingNumberMaxLength);
        InvoiceReviewService.InvTrackingNumberMaxLength.Should().Be(100, "the schema script creates the column at 100, as the submission's");
    }
}
