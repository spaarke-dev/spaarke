#!/usr/bin/env bash
# Filtered verification for this project (NEVER run Sprk.Bff.Api.Tests unfiltered: the host reaches ~8 GB).
# Usage: bash verify-suites.sh <repo-or-worktree-path> <log-dir>
repo="$1"; L="$2"; mkdir -p "$L"
cd "$repo" || exit 2
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet build Spaarke.sln -nologo -v q > "$L/sln.log" 2>&1; echo "sln exit=$?"
grep -E "Warning\(s\)|Error\(s\)" "$L/sln.log"
dotnet build tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj -nologo -v q > "$L/arch-build.log" 2>&1; echo "arch build exit=$?"
dotnet test tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj --no-build -nologo > "$L/arch.log" 2>&1
grep -E "^\s+Failed |Passed!|Failed!" "$L/arch.log" | head -20
F="FullyQualifiedName~RecordCreation|FullyQualifiedName~FieldMapping|FullyQualifiedName~MappingSourceCallerMask|FullyQualifiedName~ChildAccessPermission|FullyQualifiedName~RecordOwnership|FullyQualifiedName~RecordRepair|FullyQualifiedName~SecureRoot|FullyQualifiedName~OwnedChildWrite|FullyQualifiedName~Office|FullyQualifiedName~SearchIndex|FullyQualifiedName~CrossSurface|FullyQualifiedName~UploadFinalization|FullyQualifiedName~RecordCreatorPerson|FullyQualifiedName~SecureChildOwnershipAiToolTests|FullyQualifiedName~RefileWriterAfterRefileGuardTests|FullyQualifiedName~RouteAuthorizationGuardTests|FullyQualifiedName~RecordOwnerAssignmentCensusTests|FullyQualifiedName~DocumentDestroy|FullyQualifiedName~ExternalDeleteOwn|FullyQualifiedName~DocumentCheckout|FullyQualifiedName~InheritedAccessPermission|FullyQualifiedName~CoreAncestor|FullyQualifiedName~SecureChildShare|FullyQualifiedName~RecordTableRule|FullyQualifiedName~SecurableEntity|FullyQualifiedName~ChildRecord|FullyQualifiedName~CreatorPerson|FullyQualifiedName~SecureRootAccessColumns|FullyQualifiedName~ExternalDocumentNarrowing|FullyQualifiedName~ProvisionalOwnNarrowing|FullyQualifiedName~DocumentCommunicationFields|FullyQualifiedName~EmailDocumentCommunicationLink|FullyQualifiedName~UploadFinalization|FullyQualifiedName~Issue1079|FullyQualifiedName~ServerBindSpelling|FullyQualifiedName~OwnerCreatorColumnAppOnly|FullyQualifiedName~CommunicationServiceArchive|FullyQualifiedName~EarlierInvoiceReadability|FullyQualifiedName~GateDecisionRecordSharing|FullyQualifiedName~DocumentBffOwnedColumn|FullyQualifiedName~ConfidentialClientMigration|FullyQualifiedName~AssignedAccess|FullyQualifiedName~CreateTimeFieldMapping"
dotnet test tests/unit/Sprk.Bff.Api.Tests --no-build --nologo --filter "$F" > "$L/suites.log" 2>&1
grep -E "^\s+Failed |Passed!|Failed!" "$L/suites.log" | head -30
