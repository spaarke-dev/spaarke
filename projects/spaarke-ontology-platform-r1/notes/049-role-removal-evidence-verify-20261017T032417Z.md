# Task 049 role-removal evidence (verify)

- Environment: https://spaarkedev1.crm.dynamics.com (org 'spaarkedev1')
- Run (UTC): 20261017T032417Z
- Operator systemuserid: 1d02f31c-1872-f011-b4cb-7c1e52671ad0
- Mode: verify
- Script: scripts/Remove-ConsoleSignalWriteAndDecisionRecordCreate.ps1

## Run transcript

```text
Environment : https://spaarkedev1.crm.dynamics.com (org 'spaarkedev1')
Operator    : systemuserid 1d02f31c-1872-f011-b4cb-7c1e52671ad0
Mode        : VERIFY (read-only)

INHERITED COPIES (read-back only; the script never edits a copy): each must match its root for the privileges below
  Spaarke Ontology Administrator   Secure Record            matches root
  Spaarke Ontology Administrator   Spaarke Business Unit 1  matches root
  Spaarke Ontology Administrator   Spaarke Demo             matches root
  Spaarke Ontology Administrator   Spaarke Dev 1            matches root
  Spaarke Ontology Administrator   Spaarke Test 1           matches root
  Spaarke Console User             Secure Record            matches root
  Spaarke Console User             Spaarke Business Unit 1  matches root
  Spaarke Console User             Spaarke Demo             matches root
  Spaarke Console User             Spaarke Dev 1            matches root
  Spaarke Console User             Spaarke Test 1           matches root

UNION OF ROLES (NFR-11), LIVE state

  Write on sprk_decisionrecord (prvWritesprk_DecisionRecord): 3 role(s)
    System Administrator               admin            Global  humans=2 apps=14 teams=1 (humans via teams: 0)
        human: Delegated Admin <crmoln2@microsoft.com>
        human: Ralph Schroeder <ralph.schroeder@spaarke.com>
        teams: Spaarke Demo
    System Customizer                  admin            Global  humans=0 apps=2 teams=0 (humans via teams: 0)
    Service Writer                     platform         Global  humans=0 apps=14 teams=0 (humans via teams: 0)

  Delete on sprk_decisionrecord (prvDeletesprk_DecisionRecord): 3 role(s)
    System Administrator               admin            Global  humans=2 apps=14 teams=1 (humans via teams: 0)
        human: Delegated Admin <crmoln2@microsoft.com>
        human: Ralph Schroeder <ralph.schroeder@spaarke.com>
        teams: Spaarke Demo
    System Customizer                  admin            Global  humans=0 apps=2 teams=0 (humans via teams: 0)
    Service Deleter                    platform         Global  humans=0 apps=8 teams=0 (humans via teams: 0)

  Create on sprk_decisionrecord (prvCreatesprk_DecisionRecord): 4 role(s)
    System Administrator               admin            Global  humans=2 apps=14 teams=1 (humans via teams: 0)
        human: Delegated Admin <crmoln2@microsoft.com>
        human: Ralph Schroeder <ralph.schroeder@spaarke.com>
        teams: Spaarke Demo
    System Customizer                  admin            Global  humans=0 apps=2 teams=0 (humans via teams: 0)
    Service Writer                     platform         Global  humans=0 apps=14 teams=0 (humans via teams: 0)
    Spaarke Ontology Service           spaarke-service  Global  humans=0 apps=2 teams=0 (humans via teams: 0)

  Write on sprk_signal (prvWritesprk_Signal): 4 role(s)
    System Administrator               admin            Global  humans=2 apps=14 teams=1 (humans via teams: 0)
        human: Delegated Admin <crmoln2@microsoft.com>
        human: Ralph Schroeder <ralph.schroeder@spaarke.com>
        teams: Spaarke Demo
    System Customizer                  admin            Global  humans=0 apps=2 teams=0 (humans via teams: 0)
    Service Writer                     platform         Global  humans=0 apps=14 teams=0 (humans via teams: 0)
    Spaarke Ontology Service           spaarke-service  Global  humans=0 apps=2 teams=0 (humans via teams: 0)

ASSERTIONS
  PASS A1   Write/Delete on sprk_decisionrecord is held only by platform and admin roles; platform roles have no human member; every team membership was read
  PASS A2   Create on sprk_decisionrecord is held only by platform roles, admin roles and Spaarke Ontology Service (which has no human member)
  PASS A3   Spaarke Console User does not hold Write on sprk_signal
  PASS A4   Control: Spaarke Ontology Service still holds Write on sprk_signal and Create on sprk_decisionrecord
  PASS A5   Write on sprk_signal is held only by platform roles, admin roles and Spaarke Ontology Service, which has no human member (one named waiver)

FINDING (not an assertion): non-platform, non-admin, non-writer roles that still hold Write on sprk_signal
  none

RESULT: PASS (verify)
```

## READ-BACK: RetrieveRolePrivilegesRole per business-unit copy

```json
{
  "Spaarke Ontology Administrator": [
    {
      "BusinessUnit": "Spaarke",
      "IsRoot": true,
      "RoleId": "2f2b2137-bfbe-f111-aaaf-0022482913fc",
      "Count": 38,
      "Privileges": [
        "prvAppendsprk_BudgetRevision=Deep",
        "prvAppendsprk_DecisionRecord=Deep",
        "prvAppendsprk_Policy=Deep",
        "prvAppendsprk_PolicyVersion=Deep",
        "prvAppendsprk_Signal=Deep",
        "prvAppendTosprk_BudgetRevision=Deep",
        "prvAppendTosprk_DecisionRecord=Deep",
        "prvAppendTosprk_Policy=Deep",
        "prvAppendTosprk_PolicyVersion=Deep",
        "prvAppendTosprk_Signal=Deep",
        "prvAssignsprk_Signal=Deep",
        "prvCreateSharePointData=Global",
        "prvCreatesprk_BudgetRevision=Deep",
        "prvCreatesprk_Policy=Deep",
        "prvCreatesprk_PolicyVersion=Deep",
        "prvCreatesprk_TriageCategory=Global",
        "prvDeletesprk_BudgetRevision=Deep",
        "prvDeletesprk_Policy=Deep",
        "prvDeletesprk_PolicyVersion=Deep",
        "prvDeletesprk_Signal=Deep",
        "prvReadPluginAssembly=Global",
        "prvReadPluginType=Global",
        "prvReadSdkMessage=Global",
        "prvReadSdkMessageProcessingStep=Global",
        "prvReadSdkMessageProcessingStepImage=Global",
        "prvReadSharePointData=Global",
        "prvReadSharePointDocument=Global",
        "prvReadsprk_BudgetRevision=Deep",
        "prvReadsprk_DecisionRecord=Deep",
        "prvReadsprk_Policy=Deep",
        "prvReadsprk_PolicyVersion=Deep",
        "prvReadsprk_Signal=Deep",
        "prvReadsprk_TriageCategory=Global",
        "prvWriteSharePointData=Global",
        "prvWritesprk_BudgetRevision=Deep",
        "prvWritesprk_Policy=Deep",
        "prvWritesprk_PolicyVersion=Deep",
        "prvWritesprk_TriageCategory=Global"
      ]
    },
    {
      "BusinessUnit": "Secure Record",
      "IsRoot": false,
      "RoleId": "efca5b32-7a2c-4e2b-a9d6-c6803b13912b",
      "Count": 38,
      "Privileges": [
        "prvAppendsprk_BudgetRevision=Deep",
        "prvAppendsprk_DecisionRecord=Deep",
        "prvAppendsprk_Policy=Deep",
        "prvAppendsprk_PolicyVersion=Deep",
        "prvAppendsprk_Signal=Deep",
        "prvAppendTosprk_BudgetRevision=Deep",
        "prvAppendTosprk_DecisionRecord=Deep",
        "prvAppendTosprk_Policy=Deep",
        "prvAppendTosprk_PolicyVersion=Deep",
        "prvAppendTosprk_Signal=Deep",
        "prvAssignsprk_Signal=Deep",
        "prvCreateSharePointData=Global",
        "prvCreatesprk_BudgetRevision=Deep",
        "prvCreatesprk_Policy=Deep",
        "prvCreatesprk_PolicyVersion=Deep",
        "prvCreatesprk_TriageCategory=Global",
        "prvDeletesprk_BudgetRevision=Deep",
        "prvDeletesprk_Policy=Deep",
        "prvDeletesprk_PolicyVersion=Deep",
        "prvDeletesprk_Signal=Deep",
        "prvReadPluginAssembly=Global",
        "prvReadPluginType=Global",
        "prvReadSdkMessage=Global",
        "prvReadSdkMessageProcessingStep=Global",
        "prvReadSdkMessageProcessingStepImage=Global",
        "prvReadSharePointData=Global",
        "prvReadSharePointDocument=Global",
        "prvReadsprk_BudgetRevision=Deep",
        "prvReadsprk_DecisionRecord=Deep",
        "prvReadsprk_Policy=Deep",
        "prvReadsprk_PolicyVersion=Deep",
        "prvReadsprk_Signal=Deep",
        "prvReadsprk_TriageCategory=Global",
        "prvWriteSharePointData=Global",
        "prvWritesprk_BudgetRevision=Deep",
        "prvWritesprk_Policy=Deep",
        "prvWritesprk_PolicyVersion=Deep",
        "prvWritesprk_TriageCategory=Global"
      ]
    },
    {
      "BusinessUnit": "Spaarke Business Unit 1",
      "IsRoot": false,
      "RoleId": "4f6a91ff-6ace-4efa-9e94-795c96929230",
      "Count": 38,
      "Privileges": [
        "prvAppendsprk_BudgetRevision=Deep",
        "prvAppendsprk_DecisionRecord=Deep",
        "prvAppendsprk_Policy=Deep",
        "prvAppendsprk_PolicyVersion=Deep",
        "prvAppendsprk_Signal=Deep",
        "prvAppendTosprk_BudgetRevision=Deep",
        "prvAppendTosprk_DecisionRecord=Deep",
        "prvAppendTosprk_Policy=Deep",
        "prvAppendTosprk_PolicyVersion=Deep",
        "prvAppendTosprk_Signal=Deep",
        "prvAssignsprk_Signal=Deep",
        "prvCreateSharePointData=Global",
        "prvCreatesprk_BudgetRevision=Deep",
        "prvCreatesprk_Policy=Deep",
        "prvCreatesprk_PolicyVersion=Deep",
        "prvCreatesprk_TriageCategory=Global",
        "prvDeletesprk_BudgetRevision=Deep",
        "prvDeletesprk_Policy=Deep",
        "prvDeletesprk_PolicyVersion=Deep",
        "prvDeletesprk_Signal=Deep",
        "prvReadPluginAssembly=Global",
        "prvReadPluginType=Global",
        "prvReadSdkMessage=Global",
        "prvReadSdkMessageProcessingStep=Global",
        "prvReadSdkMessageProcessingStepImage=Global",
        "prvReadSharePointData=Global",
        "prvReadSharePointDocument=Global",
        "prvReadsprk_BudgetRevision=Deep",
        "prvReadsprk_DecisionRecord=Deep",
        "prvReadsprk_Policy=Deep",
        "prvReadsprk_PolicyVersion=Deep",
        "prvReadsprk_Signal=Deep",
        "prvReadsprk_TriageCategory=Global",
        "prvWriteSharePointData=Global",
        "prvWritesprk_BudgetRevision=Deep",
        "prvWritesprk_Policy=Deep",
        "prvWritesprk_PolicyVersion=Deep",
        "prvWritesprk_TriageCategory=Global"
      ]
    },
    {
      "BusinessUnit": "Spaarke Demo",
      "IsRoot": false,
      "RoleId": "d785bc47-4c32-48a0-9384-dbd691f4820d",
      "Count": 38,
      "Privileges": [
        "prvAppendsprk_BudgetRevision=Deep",
        "prvAppendsprk_DecisionRecord=Deep",
        "prvAppendsprk_Policy=Deep",
        "prvAppendsprk_PolicyVersion=Deep",
        "prvAppendsprk_Signal=Deep",
        "prvAppendTosprk_BudgetRevision=Deep",
        "prvAppendTosprk_DecisionRecord=Deep",
        "prvAppendTosprk_Policy=Deep",
        "prvAppendTosprk_PolicyVersion=Deep",
        "prvAppendTosprk_Signal=Deep",
        "prvAssignsprk_Signal=Deep",
        "prvCreateSharePointData=Global",
        "prvCreatesprk_BudgetRevision=Deep",
        "prvCreatesprk_Policy=Deep",
        "prvCreatesprk_PolicyVersion=Deep",
        "prvCreatesprk_TriageCategory=Global",
        "prvDeletesprk_BudgetRevision=Deep",
        "prvDeletesprk_Policy=Deep",
        "prvDeletesprk_PolicyVersion=Deep",
        "prvDeletesprk_Signal=Deep",
        "prvReadPluginAssembly=Global",
        "prvReadPluginType=Global",
        "prvReadSdkMessage=Global",
        "prvReadSdkMessageProcessingStep=Global",
        "prvReadSdkMessageProcessingStepImage=Global",
        "prvReadSharePointData=Global",
        "prvReadSharePointDocument=Global",
        "prvReadsprk_BudgetRevision=Deep",
        "prvReadsprk_DecisionRecord=Deep",
        "prvReadsprk_Policy=Deep",
        "prvReadsprk_PolicyVersion=Deep",
        "prvReadsprk_Signal=Deep",
        "prvReadsprk_TriageCategory=Global",
        "prvWriteSharePointData=Global",
        "prvWritesprk_BudgetRevision=Deep",
        "prvWritesprk_Policy=Deep",
        "prvWritesprk_PolicyVersion=Deep",
        "prvWritesprk_TriageCategory=Global"
      ]
    },
    {
      "BusinessUnit": "Spaarke Dev 1",
      "IsRoot": false,
      "RoleId": "675098f9-43b0-4090-899c-b418143d9644",
      "Count": 38,
      "Privileges": [
        "prvAppendsprk_BudgetRevision=Deep",
        "prvAppendsprk_DecisionRecord=Deep",
        "prvAppendsprk_Policy=Deep",
        "prvAppendsprk_PolicyVersion=Deep",
        "prvAppendsprk_Signal=Deep",
        "prvAppendTosprk_BudgetRevision=Deep",
        "prvAppendTosprk_DecisionRecord=Deep",
        "prvAppendTosprk_Policy=Deep",
        "prvAppendTosprk_PolicyVersion=Deep",
        "prvAppendTosprk_Signal=Deep",
        "prvAssignsprk_Signal=Deep",
        "prvCreateSharePointData=Global",
        "prvCreatesprk_BudgetRevision=Deep",
        "prvCreatesprk_Policy=Deep",
        "prvCreatesprk_PolicyVersion=Deep",
        "prvCreatesprk_TriageCategory=Global",
        "prvDeletesprk_BudgetRevision=Deep",
        "prvDeletesprk_Policy=Deep",
        "prvDeletesprk_PolicyVersion=Deep",
        "prvDeletesprk_Signal=Deep",
        "prvReadPluginAssembly=Global",
        "prvReadPluginType=Global",
        "prvReadSdkMessage=Global",
        "prvReadSdkMessageProcessingStep=Global",
        "prvReadSdkMessageProcessingStepImage=Global",
        "prvReadSharePointData=Global",
        "prvReadSharePointDocument=Global",
        "prvReadsprk_BudgetRevision=Deep",
        "prvReadsprk_DecisionRecord=Deep",
        "prvReadsprk_Policy=Deep",
        "prvReadsprk_PolicyVersion=Deep",
        "prvReadsprk_Signal=Deep",
        "prvReadsprk_TriageCategory=Global",
        "prvWriteSharePointData=Global",
        "prvWritesprk_BudgetRevision=Deep",
        "prvWritesprk_Policy=Deep",
        "prvWritesprk_PolicyVersion=Deep",
        "prvWritesprk_TriageCategory=Global"
      ]
    },
    {
      "BusinessUnit": "Spaarke Test 1",
      "IsRoot": false,
      "RoleId": "2c4541b2-ff50-4277-9ab0-aef18f87d309",
      "Count": 38,
      "Privileges": [
        "prvAppendsprk_BudgetRevision=Deep",
        "prvAppendsprk_DecisionRecord=Deep",
        "prvAppendsprk_Policy=Deep",
        "prvAppendsprk_PolicyVersion=Deep",
        "prvAppendsprk_Signal=Deep",
        "prvAppendTosprk_BudgetRevision=Deep",
        "prvAppendTosprk_DecisionRecord=Deep",
        "prvAppendTosprk_Policy=Deep",
        "prvAppendTosprk_PolicyVersion=Deep",
        "prvAppendTosprk_Signal=Deep",
        "prvAssignsprk_Signal=Deep",
        "prvCreateSharePointData=Global",
        "prvCreatesprk_BudgetRevision=Deep",
        "prvCreatesprk_Policy=Deep",
        "prvCreatesprk_PolicyVersion=Deep",
        "prvCreatesprk_TriageCategory=Global",
        "prvDeletesprk_BudgetRevision=Deep",
        "prvDeletesprk_Policy=Deep",
        "prvDeletesprk_PolicyVersion=Deep",
        "prvDeletesprk_Signal=Deep",
        "prvReadPluginAssembly=Global",
        "prvReadPluginType=Global",
        "prvReadSdkMessage=Global",
        "prvReadSdkMessageProcessingStep=Global",
        "prvReadSdkMessageProcessingStepImage=Global",
        "prvReadSharePointData=Global",
        "prvReadSharePointDocument=Global",
        "prvReadsprk_BudgetRevision=Deep",
        "prvReadsprk_DecisionRecord=Deep",
        "prvReadsprk_Policy=Deep",
        "prvReadsprk_PolicyVersion=Deep",
        "prvReadsprk_Signal=Deep",
        "prvReadsprk_TriageCategory=Global",
        "prvWriteSharePointData=Global",
        "prvWritesprk_BudgetRevision=Deep",
        "prvWritesprk_Policy=Deep",
        "prvWritesprk_PolicyVersion=Deep",
        "prvWritesprk_TriageCategory=Global"
      ]
    }
  ],
  "Spaarke Console User": [
    {
      "BusinessUnit": "Spaarke",
      "IsRoot": true,
      "RoleId": "51c924e2-bdbe-f111-a05b-3833c5e9614d",
      "Count": 21,
      "Privileges": [
        "prvAppendsprk_DecisionRecord=Deep",
        "prvAppendsprk_Signal=Deep",
        "prvAppendTosprk_BudgetRevision=Deep",
        "prvAppendTosprk_DecisionRecord=Deep",
        "prvAppendTosprk_Policy=Deep",
        "prvAppendTosprk_PolicyVersion=Deep",
        "prvAppendTosprk_Signal=Deep",
        "prvCreateSharePointData=Global",
        "prvReadPluginAssembly=Global",
        "prvReadPluginType=Global",
        "prvReadSdkMessage=Global",
        "prvReadSdkMessageProcessingStep=Global",
        "prvReadSdkMessageProcessingStepImage=Global",
        "prvReadSharePointData=Global",
        "prvReadSharePointDocument=Global",
        "prvReadsprk_BudgetRevision=Deep",
        "prvReadsprk_DecisionRecord=Deep",
        "prvReadsprk_Policy=Deep",
        "prvReadsprk_PolicyVersion=Deep",
        "prvReadsprk_Signal=Deep",
        "prvWriteSharePointData=Global"
      ]
    },
    {
      "BusinessUnit": "Secure Record",
      "IsRoot": false,
      "RoleId": "fc78e729-b316-4bb7-a133-00fee13d7765",
      "Count": 21,
      "Privileges": [
        "prvAppendsprk_DecisionRecord=Deep",
        "prvAppendsprk_Signal=Deep",
        "prvAppendTosprk_BudgetRevision=Deep",
        "prvAppendTosprk_DecisionRecord=Deep",
        "prvAppendTosprk_Policy=Deep",
        "prvAppendTosprk_PolicyVersion=Deep",
        "prvAppendTosprk_Signal=Deep",
        "prvCreateSharePointData=Global",
        "prvReadPluginAssembly=Global",
        "prvReadPluginType=Global",
        "prvReadSdkMessage=Global",
        "prvReadSdkMessageProcessingStep=Global",
        "prvReadSdkMessageProcessingStepImage=Global",
        "prvReadSharePointData=Global",
        "prvReadSharePointDocument=Global",
        "prvReadsprk_BudgetRevision=Deep",
        "prvReadsprk_DecisionRecord=Deep",
        "prvReadsprk_Policy=Deep",
        "prvReadsprk_PolicyVersion=Deep",
        "prvReadsprk_Signal=Deep",
        "prvWriteSharePointData=Global"
      ]
    },
    {
      "BusinessUnit": "Spaarke Business Unit 1",
      "IsRoot": false,
      "RoleId": "ac932c91-a3f3-463b-b6f1-bfdb1e2bf96e",
      "Count": 21,
      "Privileges": [
        "prvAppendsprk_DecisionRecord=Deep",
        "prvAppendsprk_Signal=Deep",
        "prvAppendTosprk_BudgetRevision=Deep",
        "prvAppendTosprk_DecisionRecord=Deep",
        "prvAppendTosprk_Policy=Deep",
        "prvAppendTosprk_PolicyVersion=Deep",
        "prvAppendTosprk_Signal=Deep",
        "prvCreateSharePointData=Global",
        "prvReadPluginAssembly=Global",
        "prvReadPluginType=Global",
        "prvReadSdkMessage=Global",
        "prvReadSdkMessageProcessingStep=Global",
        "prvReadSdkMessageProcessingStepImage=Global",
        "prvReadSharePointData=Global",
        "prvReadSharePointDocument=Global",
        "prvReadsprk_BudgetRevision=Deep",
        "prvReadsprk_DecisionRecord=Deep",
        "prvReadsprk_Policy=Deep",
        "prvReadsprk_PolicyVersion=Deep",
        "prvReadsprk_Signal=Deep",
        "prvWriteSharePointData=Global"
      ]
    },
    {
      "BusinessUnit": "Spaarke Demo",
      "IsRoot": false,
      "RoleId": "e54fe91e-2e59-4588-ab03-8e7c8b51be53",
      "Count": 21,
      "Privileges": [
        "prvAppendsprk_DecisionRecord=Deep",
        "prvAppendsprk_Signal=Deep",
        "prvAppendTosprk_BudgetRevision=Deep",
        "prvAppendTosprk_DecisionRecord=Deep",
        "prvAppendTosprk_Policy=Deep",
        "prvAppendTosprk_PolicyVersion=Deep",
        "prvAppendTosprk_Signal=Deep",
        "prvCreateSharePointData=Global",
        "prvReadPluginAssembly=Global",
        "prvReadPluginType=Global",
        "prvReadSdkMessage=Global",
        "prvReadSdkMessageProcessingStep=Global",
        "prvReadSdkMessageProcessingStepImage=Global",
        "prvReadSharePointData=Global",
        "prvReadSharePointDocument=Global",
        "prvReadsprk_BudgetRevision=Deep",
        "prvReadsprk_DecisionRecord=Deep",
        "prvReadsprk_Policy=Deep",
        "prvReadsprk_PolicyVersion=Deep",
        "prvReadsprk_Signal=Deep",
        "prvWriteSharePointData=Global"
      ]
    },
    {
      "BusinessUnit": "Spaarke Dev 1",
      "IsRoot": false,
      "RoleId": "8d0e14a1-ceb8-4d27-a73e-888db5b21fe3",
      "Count": 21,
      "Privileges": [
        "prvAppendsprk_DecisionRecord=Deep",
        "prvAppendsprk_Signal=Deep",
        "prvAppendTosprk_BudgetRevision=Deep",
        "prvAppendTosprk_DecisionRecord=Deep",
        "prvAppendTosprk_Policy=Deep",
        "prvAppendTosprk_PolicyVersion=Deep",
        "prvAppendTosprk_Signal=Deep",
        "prvCreateSharePointData=Global",
        "prvReadPluginAssembly=Global",
        "prvReadPluginType=Global",
        "prvReadSdkMessage=Global",
        "prvReadSdkMessageProcessingStep=Global",
        "prvReadSdkMessageProcessingStepImage=Global",
        "prvReadSharePointData=Global",
        "prvReadSharePointDocument=Global",
        "prvReadsprk_BudgetRevision=Deep",
        "prvReadsprk_DecisionRecord=Deep",
        "prvReadsprk_Policy=Deep",
        "prvReadsprk_PolicyVersion=Deep",
        "prvReadsprk_Signal=Deep",
        "prvWriteSharePointData=Global"
      ]
    },
    {
      "BusinessUnit": "Spaarke Test 1",
      "IsRoot": false,
      "RoleId": "a30b4998-b9c4-4b1b-af80-e0fcfd38b7e7",
      "Count": 21,
      "Privileges": [
        "prvAppendsprk_DecisionRecord=Deep",
        "prvAppendsprk_Signal=Deep",
        "prvAppendTosprk_BudgetRevision=Deep",
        "prvAppendTosprk_DecisionRecord=Deep",
        "prvAppendTosprk_Policy=Deep",
        "prvAppendTosprk_PolicyVersion=Deep",
        "prvAppendTosprk_Signal=Deep",
        "prvCreateSharePointData=Global",
        "prvReadPluginAssembly=Global",
        "prvReadPluginType=Global",
        "prvReadSdkMessage=Global",
        "prvReadSdkMessageProcessingStep=Global",
        "prvReadSdkMessageProcessingStepImage=Global",
        "prvReadSharePointData=Global",
        "prvReadSharePointDocument=Global",
        "prvReadsprk_BudgetRevision=Deep",
        "prvReadsprk_DecisionRecord=Deep",
        "prvReadsprk_Policy=Deep",
        "prvReadsprk_PolicyVersion=Deep",
        "prvReadsprk_Signal=Deep",
        "prvWriteSharePointData=Global"
      ]
    }
  ]
}
```

## PLAN (root roles only)

```json

```

## Removals actually made (RemovePrivilegeRole calls that returned)

```json

```

## Union of roles

```json
{
  "Write on sprk_decisionrecord": [
    {
      "RootId": "10fbf21c-1872-f011-b4cb-7c1e52671ad0",
      "Name": "System Administrator",
      "Class": "admin",
      "Managed": true,
      "Depth": "Global",
      "Copies": 1,
      "Members": {
        "HumanUsers": [
          "Delegated Admin <crmoln2@microsoft.com>",
          "Ralph Schroeder <ralph.schroeder@spaarke.com>"
        ],
        "DisabledHumans": 1,
        "AppUsers": 14,
        "Teams": [
          "Spaarke Demo"
        ],
        "HumansViaTeams": 0,
        "TeamReadFailures": []
      }
    },
    {
      "RootId": "80fff21c-1872-f011-b4cb-7c1e52671ad0",
      "Name": "System Customizer",
      "Class": "admin",
      "Managed": true,
      "Depth": "Global",
      "Copies": 1,
      "Members": {
        "HumanUsers": [],
        "DisabledHumans": 0,
        "AppUsers": 2,
        "Teams": [],
        "HumansViaTeams": 0,
        "TeamReadFailures": []
      }
    },
    {
      "RootId": "68f2d372-e1a5-4298-9a93-4b39757caf93",
      "Name": "Service Writer",
      "Class": "platform",
      "Managed": true,
      "Depth": "Global",
      "Copies": 1,
      "Members": {
        "HumanUsers": [],
        "DisabledHumans": 0,
        "AppUsers": 14,
        "Teams": [],
        "HumansViaTeams": 0,
        "TeamReadFailures": []
      }
    }
  ],
  "Delete on sprk_decisionrecord": [
    {
      "RootId": "10fbf21c-1872-f011-b4cb-7c1e52671ad0",
      "Name": "System Administrator",
      "Class": "admin",
      "Managed": true,
      "Depth": "Global",
      "Copies": 1,
      "Members": {
        "HumanUsers": [
          "Delegated Admin <crmoln2@microsoft.com>",
          "Ralph Schroeder <ralph.schroeder@spaarke.com>"
        ],
        "DisabledHumans": 1,
        "AppUsers": 14,
        "Teams": [
          "Spaarke Demo"
        ],
        "HumansViaTeams": 0,
        "TeamReadFailures": []
      }
    },
    {
      "RootId": "80fff21c-1872-f011-b4cb-7c1e52671ad0",
      "Name": "System Customizer",
      "Class": "admin",
      "Managed": true,
      "Depth": "Global",
      "Copies": 1,
      "Members": {
        "HumanUsers": [],
        "DisabledHumans": 0,
        "AppUsers": 2,
        "Teams": [],
        "HumansViaTeams": 0,
        "TeamReadFailures": []
      }
    },
    {
      "RootId": "3b305bc7-5cf5-464b-985e-d6237f6496f1",
      "Name": "Service Deleter",
      "Class": "platform",
      "Managed": true,
      "Depth": "Global",
      "Copies": 1,
      "Members": {
        "HumanUsers": [],
        "DisabledHumans": 0,
        "AppUsers": 8,
        "Teams": [],
        "HumansViaTeams": 0,
        "TeamReadFailures": []
      }
    }
  ],
  "Create on sprk_decisionrecord": [
    {
      "RootId": "10fbf21c-1872-f011-b4cb-7c1e52671ad0",
      "Name": "System Administrator",
      "Class": "admin",
      "Managed": true,
      "Depth": "Global",
      "Copies": 1,
      "Members": {
        "HumanUsers": [
          "Delegated Admin <crmoln2@microsoft.com>",
          "Ralph Schroeder <ralph.schroeder@spaarke.com>"
        ],
        "DisabledHumans": 1,
        "AppUsers": 14,
        "Teams": [
          "Spaarke Demo"
        ],
        "HumansViaTeams": 0,
        "TeamReadFailures": []
      }
    },
    {
      "RootId": "80fff21c-1872-f011-b4cb-7c1e52671ad0",
      "Name": "System Customizer",
      "Class": "admin",
      "Managed": true,
      "Depth": "Global",
      "Copies": 1,
      "Members": {
        "HumanUsers": [],
        "DisabledHumans": 0,
        "AppUsers": 2,
        "Teams": [],
        "HumansViaTeams": 0,
        "TeamReadFailures": []
      }
    },
    {
      "RootId": "68f2d372-e1a5-4298-9a93-4b39757caf93",
      "Name": "Service Writer",
      "Class": "platform",
      "Managed": true,
      "Depth": "Global",
      "Copies": 1,
      "Members": {
        "HumanUsers": [],
        "DisabledHumans": 0,
        "AppUsers": 14,
        "Teams": [],
        "HumansViaTeams": 0,
        "TeamReadFailures": []
      }
    },
    {
      "RootId": "b1fb7ee0-bfbe-f111-aaaf-0022482913fc",
      "Name": "Spaarke Ontology Service",
      "Class": "spaarke-service",
      "Managed": false,
      "Depth": "Global",
      "Copies": 1,
      "Members": {
        "HumanUsers": [],
        "DisabledHumans": 0,
        "AppUsers": 2,
        "Teams": [],
        "HumansViaTeams": 0,
        "TeamReadFailures": []
      }
    }
  ],
  "Write on sprk_signal": [
    {
      "RootId": "10fbf21c-1872-f011-b4cb-7c1e52671ad0",
      "Name": "System Administrator",
      "Class": "admin",
      "Managed": true,
      "Depth": "Global",
      "Copies": 1,
      "Members": {
        "HumanUsers": [
          "Delegated Admin <crmoln2@microsoft.com>",
          "Ralph Schroeder <ralph.schroeder@spaarke.com>"
        ],
        "DisabledHumans": 1,
        "AppUsers": 14,
        "Teams": [
          "Spaarke Demo"
        ],
        "HumansViaTeams": 0,
        "TeamReadFailures": []
      }
    },
    {
      "RootId": "80fff21c-1872-f011-b4cb-7c1e52671ad0",
      "Name": "System Customizer",
      "Class": "admin",
      "Managed": true,
      "Depth": "Global",
      "Copies": 1,
      "Members": {
        "HumanUsers": [],
        "DisabledHumans": 0,
        "AppUsers": 2,
        "Teams": [],
        "HumansViaTeams": 0,
        "TeamReadFailures": []
      }
    },
    {
      "RootId": "68f2d372-e1a5-4298-9a93-4b39757caf93",
      "Name": "Service Writer",
      "Class": "platform",
      "Managed": true,
      "Depth": "Global",
      "Copies": 1,
      "Members": {
        "HumanUsers": [],
        "DisabledHumans": 0,
        "AppUsers": 14,
        "Teams": [],
        "HumansViaTeams": 0,
        "TeamReadFailures": []
      }
    },
    {
      "RootId": "b1fb7ee0-bfbe-f111-aaaf-0022482913fc",
      "Name": "Spaarke Ontology Service",
      "Class": "spaarke-service",
      "Managed": false,
      "Depth": "Global",
      "Copies": 1,
      "Members": {
        "HumanUsers": [],
        "DisabledHumans": 0,
        "AppUsers": 2,
        "Teams": [],
        "HumansViaTeams": 0,
        "TeamReadFailures": []
      }
    }
  ]
}
```
