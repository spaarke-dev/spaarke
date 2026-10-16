# Task 049 role-removal evidence (verify)

- Environment: https://spaarkedev1.crm.dynamics.com (org 'spaarkedev1')
- Run (UTC): 20261016T145241Z
- Operator systemuserid: 1d02f31c-1872-f011-b4cb-7c1e52671ad0
- Mode: verify
- Script: scripts/Remove-ConsoleSignalWriteAndDecisionRecordCreate.ps1

## Run transcript

```text
Environment : https://spaarkedev1.crm.dynamics.com (org 'spaarkedev1')
Operator    : systemuserid 1d02f31c-1872-f011-b4cb-7c1e52671ad0
Mode        : VERIFY (read-only)

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

  Create on sprk_decisionrecord (prvCreatesprk_DecisionRecord): 6 role(s)
    System Administrator               admin            Global  humans=2 apps=14 teams=1 (humans via teams: 0)
        human: Delegated Admin <crmoln2@microsoft.com>
        human: Ralph Schroeder <ralph.schroeder@spaarke.com>
        teams: Spaarke Demo
    System Customizer                  admin            Global  humans=0 apps=2 teams=0 (humans via teams: 0)
    Service Writer                     platform         Global  humans=0 apps=14 teams=0 (humans via teams: 0)
    Spaarke Console User               spaarke-human    Deep    humans=4 apps=0 teams=1 (humans via teams: 12)
        human: Chelsea Friez <chelsea.friez@demo.spaarke.com>
        human: Jessica Broyles <jessica.broyles@demo.spaarke.com>
        human: Lori Witkin <lori.witkin@demo.spaarke.com>
        human: Ralph Schroeder <ralph.schroeder@spaarke.com>
        teams: Spaarke
    Spaarke Ontology Administrator     spaarke-human    Deep    humans=1 apps=0 teams=0 (humans via teams: 0)
        human: Ralph Schroeder <ralph.schroeder@spaarke.com>
    Spaarke Ontology Service           spaarke-service  Global  humans=0 apps=2 teams=0 (humans via teams: 0)

  Write on sprk_signal (prvWritesprk_Signal): 6 role(s)
    System Administrator               admin            Global  humans=2 apps=14 teams=1 (humans via teams: 0)
        human: Delegated Admin <crmoln2@microsoft.com>
        human: Ralph Schroeder <ralph.schroeder@spaarke.com>
        teams: Spaarke Demo
    System Customizer                  admin            Global  humans=0 apps=2 teams=0 (humans via teams: 0)
    Service Writer                     platform         Global  humans=0 apps=14 teams=0 (humans via teams: 0)
    Spaarke Console User               spaarke-human    Deep    humans=4 apps=0 teams=1 (humans via teams: 12)
        human: Chelsea Friez <chelsea.friez@demo.spaarke.com>
        human: Jessica Broyles <jessica.broyles@demo.spaarke.com>
        human: Lori Witkin <lori.witkin@demo.spaarke.com>
        human: Ralph Schroeder <ralph.schroeder@spaarke.com>
        teams: Spaarke
    Spaarke Ontology Administrator     spaarke-human    Deep    humans=1 apps=0 teams=0 (humans via teams: 0)
        human: Ralph Schroeder <ralph.schroeder@spaarke.com>
    Spaarke Ontology Service           spaarke-service  Global  humans=0 apps=2 teams=0 (humans via teams: 0)

ASSERTIONS
  PASS A1   Write/Delete on sprk_decisionrecord is held only by platform and admin roles; platform roles have no human member
  FAIL A2   Create on sprk_decisionrecord is held only by platform roles, admin roles and Spaarke Ontology Service -> unexpected holders: [Spaarke Console User, Spaarke Ontology Administrator]
  FAIL A3   Spaarke Console User does not hold Write on sprk_signal -> Console User still holds it
  PASS A4   Control: Spaarke Ontology Service still holds Write on sprk_signal and Create on sprk_decisionrecord

FINDING (not an assertion): non-platform, non-admin, non-writer roles that still hold Write on sprk_signal
  Spaarke Console User (Deep); human members: Chelsea Friez <chelsea.friez@demo.spaarke.com>; Jessica Broyles <jessica.broyles@demo.spaarke.com>; Lori Witkin <lori.witkin@demo.spaarke.com>; Ralph Schroeder <ralph.schroeder@spaarke.com>
  Spaarke Ontology Administrator (Deep); human members: Ralph Schroeder <ralph.schroeder@spaarke.com>

RESULT: FAIL (verify): 2 problem(s)
```

## PLAN or APPLIED removals

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
        "HumansViaTeams": 0
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
        "HumansViaTeams": 0
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
        "HumansViaTeams": 0
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
        "HumansViaTeams": 0
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
        "HumansViaTeams": 0
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
        "HumansViaTeams": 0
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
        "HumansViaTeams": 0
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
        "HumansViaTeams": 0
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
        "HumansViaTeams": 0
      }
    },
    {
      "RootId": "51c924e2-bdbe-f111-a05b-3833c5e9614d",
      "Name": "Spaarke Console User",
      "Class": "spaarke-human",
      "Managed": false,
      "Depth": "Deep",
      "Copies": 1,
      "Members": {
        "HumanUsers": [
          "Chelsea Friez <chelsea.friez@demo.spaarke.com>",
          "Jessica Broyles <jessica.broyles@demo.spaarke.com>",
          "Lori Witkin <lori.witkin@demo.spaarke.com>",
          "Ralph Schroeder <ralph.schroeder@spaarke.com>"
        ],
        "DisabledHumans": 0,
        "AppUsers": 0,
        "Teams": [
          "Spaarke"
        ],
        "HumansViaTeams": 12
      }
    },
    {
      "RootId": "2f2b2137-bfbe-f111-aaaf-0022482913fc",
      "Name": "Spaarke Ontology Administrator",
      "Class": "spaarke-human",
      "Managed": false,
      "Depth": "Deep",
      "Copies": 1,
      "Members": {
        "HumanUsers": [
          "Ralph Schroeder <ralph.schroeder@spaarke.com>"
        ],
        "DisabledHumans": 0,
        "AppUsers": 0,
        "Teams": [],
        "HumansViaTeams": 0
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
        "HumansViaTeams": 0
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
        "HumansViaTeams": 0
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
        "HumansViaTeams": 0
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
        "HumansViaTeams": 0
      }
    },
    {
      "RootId": "51c924e2-bdbe-f111-a05b-3833c5e9614d",
      "Name": "Spaarke Console User",
      "Class": "spaarke-human",
      "Managed": false,
      "Depth": "Deep",
      "Copies": 1,
      "Members": {
        "HumanUsers": [
          "Chelsea Friez <chelsea.friez@demo.spaarke.com>",
          "Jessica Broyles <jessica.broyles@demo.spaarke.com>",
          "Lori Witkin <lori.witkin@demo.spaarke.com>",
          "Ralph Schroeder <ralph.schroeder@spaarke.com>"
        ],
        "DisabledHumans": 0,
        "AppUsers": 0,
        "Teams": [
          "Spaarke"
        ],
        "HumansViaTeams": 12
      }
    },
    {
      "RootId": "2f2b2137-bfbe-f111-aaaf-0022482913fc",
      "Name": "Spaarke Ontology Administrator",
      "Class": "spaarke-human",
      "Managed": false,
      "Depth": "Deep",
      "Copies": 1,
      "Members": {
        "HumanUsers": [
          "Ralph Schroeder <ralph.schroeder@spaarke.com>"
        ],
        "DisabledHumans": 0,
        "AppUsers": 0,
        "Teams": [],
        "HumansViaTeams": 0
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
        "HumansViaTeams": 0
      }
    }
  ]
}
```
