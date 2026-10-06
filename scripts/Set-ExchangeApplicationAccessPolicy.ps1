<#
.SYNOPSIS
    RETIRED (customer-provisioning-orchestration-r1 task 251, 2026-10-04). Do not use.

.DESCRIPTION
    This script created Exchange Online ApplicationAccessPolicy entries for a customer's BFF app
    registration + managed identity, signing in with a certificate. Both halves are retired:

      - Microsoft calls ApplicationAccessPolicy legacy ("replaced by RBAC for Applications") and caps
        it at a few hundred policies per tenant. H14a now grants the stamp's managed identity the
        Exchange "Application Mail.*" roles, scoped to the customer's mail-enabled security group
        (owner D26).
      - Nothing signs in to Exchange with a certificate any more. The L2 Worker signs in as
        'Spaarke Exchange Admin' through its managed identity's federated credential and passes the
        token to the H14a sidecar (owner D24).

    Where the logic lives now:
      src/server/services/Sprk.Provisioning.ControlPlane.Sidecar/SidecarCore.psm1  (Invoke-MailboxAccessApply)
      src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/IntegrationWiring/H14aExchangePolicySubHandler.cs
    Design + live evidence: projects/customer-provisioning-orchestration-r1/notes/t251-exchange-sidecar-design.md
#>
throw "Set-ExchangeApplicationAccessPolicy.ps1 is retired (task 251): H14a uses Exchange RBAC for Applications through the L2 Worker's sidecar. See the script header."
