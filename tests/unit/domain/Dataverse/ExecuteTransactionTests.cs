using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Moq;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// Domain tests for <see cref="IGenericEntityService.ExecuteTransactionAsync"/> (ebilling-r1 task 049 part A, G1): the multi-table
/// all-or-nothing transaction the submission pipeline persists an invoice, its lines, its approval portion and the document link in.
///
/// <para>Same seam as <see cref="BulkUpdateTransactionTests"/>: the request is built and the failure is worded by two pure functions,
/// <see cref="DataverseServiceClientImpl.BuildTransaction"/> and <see cref="DataverseServiceClientImpl.MapTransactionFailure"/>, tested
/// directly because the <c>ServiceClient</c> is built inside the class and <c>Execute</c> cannot be overridden (ADR-038 B8 / B1).
/// Fault propagation is shown through an in-memory fake that behaves as Dataverse does (applies requests in order, rolls back on a fault,
/// raises an <see cref="ExecuteTransactionFault"/>) and words its failure with the production mapper.</para>
///
/// KEEP path: tests/unit/domain/** (ADR-038 — pure domain logic).
/// </summary>
public class ExecuteTransactionTests
{
    private static readonly Guid InvoiceId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid LineId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid DocumentId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    private static List<OrganizationRequest> InvoiceUnit() =>
    [
        new CreateRequest { Target = new Entity("sprk_invoice", InvoiceId) { ["sprk_invoicenumber"] = "INV-1" } },
        new CreateRequest { Target = new Entity("sprk_billingevent", LineId) { ["sprk_amount"] = new Money(-12.5m) } },
        new UpdateRequest { Target = new Entity("sprk_document", DocumentId) { ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId) } },
    ];

    // ---- BuildTransaction (request building) -------------------------------------------------------------------------------

    [Fact]
    public void BuildTransaction_ForRequestsOfSeveralTables_ReturnsOneTransactionHoldingThemInOrder()
    {
        var requests = InvoiceUnit();

        var transaction = DataverseServiceClientImpl.BuildTransaction(requests);

        transaction.Should().BeOfType<ExecuteTransactionRequest>();
        transaction.Requests.Should().HaveCount(3);
        transaction.Requests.Should().Equal(requests, "the requests keep their order and identity — nothing is rebuilt or sent outside the transaction");
        transaction.Requests[0].Should().BeOfType<CreateRequest>();
        transaction.Requests[2].Should().BeOfType<UpdateRequest>();
        transaction.ReturnResponses.Should().BeFalse("no caller reads per-request responses");
    }

    [Fact]
    public void BuildTransaction_DoesNotChangeTheCallersList()
    {
        var requests = InvoiceUnit();

        DataverseServiceClientImpl.BuildTransaction(requests);

        requests.Should().HaveCount(3);
    }

    [Fact]
    public void BuildTransaction_ForNoRequests_ThrowsBeforeAnyIo()
    {
        var none = () => DataverseServiceClientImpl.BuildTransaction([]);
        var nothing = () => DataverseServiceClientImpl.BuildTransaction(null!);

        none.Should().Throw<ArgumentException>().WithParameterName("requests");
        nothing.Should().Throw<ArgumentException>().WithParameterName("requests");
    }

    [Fact]
    public void BuildTransaction_ForANullEntry_Throws_NamingItsIndex()
    {
        var requests = new List<OrganizationRequest> { InvoiceUnit()[0], null! };

        var act = () => DataverseServiceClientImpl.BuildTransaction(requests);

        act.Should().Throw<ArgumentException>().WithMessage("*index 1*");
    }

    [Fact]
    public void BuildTransaction_ForANestedBatch_Throws_BecauseDataverseCannotNestThem()
    {
        var nestedMultiple = new List<OrganizationRequest> { InvoiceUnit()[0], new ExecuteMultipleRequest() };
        var nestedTransaction = new List<OrganizationRequest> { new ExecuteTransactionRequest() };

        var multiple = () => DataverseServiceClientImpl.BuildTransaction(nestedMultiple);
        var transaction = () => DataverseServiceClientImpl.BuildTransaction(nestedTransaction);

        multiple.Should().Throw<ArgumentException>().WithMessage("*index 1*ExecuteMultipleRequest*");
        transaction.Should().Throw<ArgumentException>().WithMessage("*index 0*ExecuteTransactionRequest*");
    }

    // ---- MapTransactionFailure (fault wording) ------------------------------------------------------------------------------

    private static FaultException<OrganizationServiceFault> TransactionFault(int failedIndex, string message = "Principal lacks write access") => new(
        new ExecuteTransactionFault { FaultedRequestIndex = failedIndex, ErrorCode = unchecked((int)0x80040220), Message = message },
        new FaultReason(message));

    [Fact]
    public void MapTransactionFailure_ForAFaultNamingARequest_CarriesItsIndex_AndNamesTheRecordAndNotTheValues()
    {
        var requests = InvoiceUnit();

        var mapped = DataverseServiceClientImpl.MapTransactionFailure(requests, TransactionFault(1));

        mapped.FailedRequestIndex.Should().Be(1);
        mapped.OutcomeUnknown.Should().BeFalse();
        mapped.Should().BeAssignableTo<InvalidOperationException>("a caller that handles BulkUpdateAsync's failure handles this one");
        mapped.Message.Should().Contain("request index 1").And.Contain("CreateRequest sprk_billingevent " + LineId).And.Contain("NO requests were applied");
        mapped.Message.Should().NotContain("12.5", "ADR-015: no field value in a failure message");
        mapped.Message.Should().NotContain("INV-1");
    }

    [Fact]
    public void MapTransactionFailure_ForAnUpdateRequest_NamesItsTable()
    {
        var mapped = DataverseServiceClientImpl.MapTransactionFailure(InvoiceUnit(), TransactionFault(2));

        mapped.FailedRequestIndex.Should().Be(2);
        mapped.Message.Should().Contain("UpdateRequest sprk_document " + DocumentId);
    }

    [Fact]
    public void MapTransactionFailure_ForAFaultThatArrivesWrapped_StillCarriesTheIndex()
    {
        var wrapped = new InvalidOperationException("sdk wrapper", TransactionFault(0));

        var mapped = DataverseServiceClientImpl.MapTransactionFailure(InvoiceUnit(), wrapped);

        mapped.FailedRequestIndex.Should().Be(0);
        mapped.InnerException.Should().BeSameAs(wrapped);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    public void MapTransactionFailure_ForAnIndexOutsideTheList_CarriesNoIndex_AndStillSaysNothingPartialLanded(int outOfRange)
    {
        var mapped = DataverseServiceClientImpl.MapTransactionFailure(InvoiceUnit(), TransactionFault(outOfRange));

        mapped.FailedRequestIndex.Should().BeNull("an index that names no request must not be reported as one");
        mapped.OutcomeUnknown.Should().BeFalse();
        mapped.Message.Should().Contain("never a partial set");
    }

    [Fact]
    public void MapTransactionFailure_ForAFaultThatIsNotATransactionFault_CarriesNoIndex()
    {
        var plain = new FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040220), Message = "boom" }, new FaultReason("boom"));

        var mapped = DataverseServiceClientImpl.MapTransactionFailure(InvoiceUnit(), plain);

        mapped.FailedRequestIndex.Should().BeNull();
        mapped.OutcomeUnknown.Should().BeFalse();
        mapped.Message.Should().Contain("did not identify which request faulted");
    }

    [Fact]
    public void MapTransactionFailure_ForAnExceptionWithNoFault_SaysTheOutcomeIsUnknown_AndNeverClaimsNothingLanded()
    {
        var timeout = new TimeoutException("The request channel timed out");

        var mapped = DataverseServiceClientImpl.MapTransactionFailure(InvoiceUnit(), timeout);

        mapped.OutcomeUnknown.Should().BeTrue("a timeout can land after the commit");
        mapped.FailedRequestIndex.Should().BeNull();
        mapped.InnerException.Should().BeSameAs(timeout);
        mapped.Message.Should().Contain("outcome is unknown").And.NotContain("NO requests were applied");
    }

    // ---- Fault propagation through a fake that behaves as Dataverse does ---------------------------------------------------

    /// <summary>
    /// In-memory stand-in for the ServiceClient transaction: applies each request in order to a staging copy, commits the copy only
    /// when every request succeeded, and otherwise raises the fault Dataverse raises (an <see cref="ExecuteTransactionFault"/> with the
    /// failing index), worded by the production mapper. The failing rule is a stand-in for any server-side refusal.
    /// </summary>
    private sealed class TransactionalFake
    {
        public Dictionary<(string table, Guid id), Entity> Committed { get; } = new();
        public Func<OrganizationRequest, bool> Faults { get; init; } = _ => false;
        public int Calls { get; private set; }

        public Task ExecuteTransactionAsync(IReadOnlyList<OrganizationRequest> requests, CancellationToken ct = default)
        {
            Calls++;
            var transaction = DataverseServiceClientImpl.BuildTransaction(requests); // the same validation the real path does
            var staging = new Dictionary<(string table, Guid id), Entity>(Committed);

            for (var index = 0; index < transaction.Requests.Count; index++)
            {
                var request = transaction.Requests[index];
                if (Faults(request))
                {
                    var fault = new FaultException<OrganizationServiceFault>(
                        new ExecuteTransactionFault { FaultedRequestIndex = index, Message = "refused" }, new FaultReason("refused"));
                    throw DataverseServiceClientImpl.MapTransactionFailure(requests, fault); // staging is dropped: rolled back
                }

                switch (request)
                {
                    case CreateRequest create:
                        staging[(create.Target.LogicalName, create.Target.Id)] = create.Target;
                        break;
                    case UpdateRequest update:
                        staging[(update.Target.LogicalName, update.Target.Id)] = update.Target;
                        break;
                }
            }

            foreach (var kvp in staging)
                Committed[kvp.Key] = kvp.Value;
            return Task.CompletedTask;
        }

        /// <summary>The fake behind the interface a caller depends on (Moq supplies the rest of the interface; it is never called).</summary>
        public IGenericEntityService AsService()
        {
            var service = new Mock<IGenericEntityService>(MockBehavior.Strict);
            service
                .Setup(s => s.ExecuteTransactionAsync(It.IsAny<IReadOnlyList<OrganizationRequest>>(), It.IsAny<CancellationToken>()))
                .Returns((IReadOnlyList<OrganizationRequest> requests, CancellationToken ct) => ExecuteTransactionAsync(requests, ct));
            return service.Object;
        }
    }

    [Fact]
    public async Task AFaultOnALaterRequest_RollsEverythingBack_AndSurfacesTheFailingIndex()
    {
        // The document update (index 2) is refused after the invoice and line creates succeeded.
        var fake = new TransactionalFake { Faults = r => r is UpdateRequest };

        var act = async () => await fake.AsService().ExecuteTransactionAsync(InvoiceUnit());

        var thrown = (await act.Should().ThrowAsync<DataverseTransactionException>()).Which;
        thrown.FailedRequestIndex.Should().Be(2);
        thrown.OutcomeUnknown.Should().BeFalse();
        fake.Committed.Should().BeEmpty("all-or-nothing: the invoice and the line created before the fault are not left behind");
    }

    [Fact]
    public async Task WhenNoRequestFaults_AllOfThemAreApplied()
    {
        var fake = new TransactionalFake();

        await fake.AsService().ExecuteTransactionAsync(InvoiceUnit());

        fake.Committed.Keys.Select(k => k.table).Should().BeEquivalentTo("sprk_invoice", "sprk_billingevent", "sprk_document");
    }

    [Fact]
    public async Task ASubstituteThatDoesNotModelTransactions_FailsLoudly_InsteadOfWritingRowsOneByOne()
    {
        // CallBase runs the interface's default body: the contract for any fake that does not override the method.
        var substitute = new Mock<IGenericEntityService> { CallBase = true };

        var act = async () => await substitute.Object.ExecuteTransactionAsync(InvoiceUnit());

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*ExecuteTransactionAsync*");
        substitute.Verify(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
