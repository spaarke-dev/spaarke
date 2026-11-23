# The invoice-family columns ONLY the BFF application user writes (#2799, unified-access-control-r2): for each family table,
# its core-record lookups and its invoice link. THE ONE LIST the scripts read:
#   - scripts/Set-DocumentPointerFieldSecurity.ps1 -Target InvoiceFamilyLinks (and its entry point
#     scripts/Set-InvoiceFamilyFieldSecurity.ps1) secures exactly these columns;
#   - scripts/common/Find-ClientInvoiceFamilyWrite.ps1 detects a client write of them on these tables.
# Pinned BOTH WAYS to the BFF's InvoiceFamilyLockedWrite.LockedColumns (derived from InvoiceFamilyCoreRecordGuard) and its
# EntitySets by tests/Spaarke.ArchTests/InvoiceFamilyFieldLockGuardTests.cs. Change the guard first, then this file.
# Entity sets and navigation names: spaarkedev1 metadata, 2026-11-13 (every navigation property is the column's schema name,
# i.e. the column name in another case; the script checks that on the target environment).
@{
    sprk_billingevent       = @{ EntitySet = 'sprk_billingevents';       Columns = @('sprk_invoice', 'sprk_matter', 'sprk_project', 'sprk_regardingworkassignment') }
    sprk_externalidentifier = @{ EntitySet = 'sprk_externalidentifiers'; Columns = @('sprk_regardinginvoice', 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingworkassignment') }
    sprk_invoice            = @{ EntitySet = 'sprk_invoices';            Columns = @('sprk_matter', 'sprk_project', 'sprk_regardingworkassignment', 'sprk_supersedesinvoice') }
    sprk_invoiceapproval    = @{ EntitySet = 'sprk_invoiceapprovals';    Columns = @('sprk_invoice', 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingworkassignment') }
    sprk_invoicecorrection  = @{ EntitySet = 'sprk_invoicecorrections';  Columns = @('sprk_invoice', 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingworkassignment') }
    sprk_invoicesubmission  = @{ EntitySet = 'sprk_invoicesubmissions';  Columns = @('sprk_invoice', 'sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingworkassignment') }
}
