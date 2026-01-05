# AI / Analysis Dataverse Entity Model

_Generated from ai-dataverse-entity-model.docx_

## Entity: Output Types

### Metadata

| Property            | Value            |
| ------------------- | ---------------- |
| Display Name        | Output Types     |
| Plural Display Name | Output Types     |
| Description         |                  |
| Schema Name         | sprk_OutputTypes |
| Logical Name        | sprk_outputtypes |
| Object Type Code    | 10550            |
| Is Custom Entity    | True             |
| Ownership Type      | UserOwned        |

### Attributes

| Logical Name       | Schema Name        | Display Name | Attribute Type   | Description                            | Is Custom | Type   | Additional data              |
| ------------------ | ------------------ | ------------ | ---------------- | -------------------------------------- | --------- | ------ | ---------------------------- |
| sprk_name          | sprk_Name          | Name         | String           |                                        | True      | Simple | Format: Text Max length: 850 |
| sprk_outputtypesid | sprk_OutputTypesId | Output Types | Uniqueidentifier | Unique identifier for entity instances | False     | Simple |                              |

## Entity: Analysis Tool

### Metadata

| Property            | Value                                                 |
| ------------------- | ----------------------------------------------------- |
| Display Name        | Analysis Tool                                         |
| Plural Display Name | Analysis Tools                                        |
| Description         | Reusable AI tools (extractors, analyzers, generators) |
| Schema Name         | sprk_analysistool                                     |
| Logical Name        | sprk_analysistool                                     |
| Object Type Code    | 10800                                                 |
| Is Custom Entity    | True                                                  |
| Ownership Type      | UserOwned                                             |

### Attributes

| Logical Name        | Schema Name         | Display Name   | Attribute Type   | Description                            | Is Custom | Type   | Additional data                     |
| ------------------- | ------------------- | -------------- | ---------------- | -------------------------------------- | --------- | ------ | ----------------------------------- |
| sprk_analysisid     | sprk_AnalysisId     | Analysis       | Lookup           | Not Translated                         | True      | Simple | Targets: sprk_analysis              |
| sprk_analysisidname | sprk_AnalysisIdName | Not Translated | String           | Not Translated                         | True      | Simple | Format: Text Max length: 200        |
| sprk_analysistoolid | sprk_analysistoolId | Analysis Tool  | Uniqueidentifier | Unique identifier for entity instances | False     | Simple |                                     |
| sprk_configuration  | sprk_configuration  | Configuration  | Memo             | JSON configuration for the tool        | True      | Simple | Format: TextArea Max length: 100000 |
| sprk_description    | sprk_description    | Description    | Memo             | Not Translated                         | True      | Simple | Format: TextArea Max length: 4000   |
| sprk_handlerclass   | sprk_handlerclass   | Handler Class  | String           | C# class implementing the tool         | True      | Simple | Format: Text Max length: 200        |
| sprk_name           | sprk_name           | Name           | String           | Tool name                              | True      | Simple | Format: Text Max length: 200        |
| sprk_tooltypeid     | sprk_ToolTypeId     | Tool Type      | Lookup           | Not Translated                         | True      | Simple | Targets: sprk_aitooltype            |
| sprk_tooltypeidname | sprk_ToolTypeIdName | Not Translated | String           | Not Translated                         | True      | Simple | Format: Text Max length: 850        |

## Entity: Analysis Skill

### Metadata

| Property            | Value                                                                         |
| ------------------- | ----------------------------------------------------------------------------- |
| Display Name        | Analysis Skill                                                                |
| Plural Display Name | Analysis Skills                                                               |
| Description         | Defines behavioral instructions (e.g. Write concisely, Use legal terminology) |
| Schema Name         | sprk_analysisskill                                                            |
| Logical Name        | sprk_analysisskill                                                            |
| Object Type Code    | 10797                                                                         |
| Is Custom Entity    | True                                                                          |
| Ownership Type      | UserOwned                                                                     |

### Attributes

| Logical Name         | Schema Name          | Display Name    | Attribute Type   | Description                            | Is Custom | Type   | Additional data                                                                               |
| -------------------- | -------------------- | --------------- | ---------------- | -------------------------------------- | --------- | ------ | --------------------------------------------------------------------------------------------- |
| sprk_analysisid      | sprk_AnalysisId      | Analysis        | Lookup           | Not Translated                         | True      | Simple | Targets: sprk_analysis                                                                        |
| sprk_analysisidname  | sprk_AnalysisIdName  | Not Translated  | String           | Not Translated                         | True      | Simple | Format: Text Max length: 200                                                                  |
| sprk_analysisskillid | sprk_analysisskillId | Analysis Skill  | Uniqueidentifier | Unique identifier for entity instances | False     | Simple |                                                                                               |
| sprk_category        | sprk_category        | Category        | Picklist         | Not Translated                         | True      | Simple | Options: 100000000: Tone 100000001: Style 100000002: Format 100000003: Expertise Default: N/A |
| sprk_categoryname    | sprk_categoryName    | Not Translated  | Virtual          | Not Translated                         | True      | Simple |                                                                                               |
| sprk_description     | sprk_description     | Description     | Memo             | Not Translated                         | True      | Simple | Format: TextArea Max length: 4000                                                             |
| sprk_name            | sprk_name            | Name            | String           | Skill name                             | True      | Simple | Format: Text Max length: 200                                                                  |
| sprk_promptfragment  | sprk_promptfragment  | Prompt Fragment | Memo             | Instruction to add to the prompt       | True      | Simple | Format: TextArea Max length: 100000                                                           |
| sprk_skilltypeid     | sprk_SkillTypeId     | Skill Type      | Lookup           | Not Translated                         | True      | Simple | Targets: sprk_aiskilltype                                                                     |
| sprk_skilltypeidname | sprk_SkillTypeIdName | Not Translated  | String           | Not Translated                         | True      | Simple | Format: Text Max length: 850                                                                  |

## Entity: Analysis Output

### Metadata

| Property            | Value               |
| ------------------- | ------------------- |
| Display Name        | Analysis Output     |
| Plural Display Name | Analysis Outputs    |
| Description         |                     |
| Schema Name         | sprk_analysisoutput |
| Logical Name        | sprk_analysisoutput |
| Object Type Code    | 10632               |
| Is Custom Entity    | True                |
| Ownership Type      | UserOwned           |

### Attributes

| Logical Name          | Schema Name           | Display Name    | Attribute Type   | Description                            | Is Custom | Type   | Additional data              |
| --------------------- | --------------------- | --------------- | ---------------- | -------------------------------------- | --------- | ------ | ---------------------------- |
| sprk_analysisid       | sprk_AnalysisId       | Analysis        | Lookup           | Not Translated                         | True      | Simple | Targets: sprk_analysis       |
| sprk_analysisidname   | sprk_AnalysisIdName   | Not Translated  | String           | Not Translated                         | True      | Simple | Format: Text Max length: 200 |
| sprk_analysisoutputid | sprk_analysisoutputId | Analysis Output | Uniqueidentifier | Unique identifier for entity instances | False     | Simple |                              |
| sprk_name             | sprk_Name             | Name            | String           |                                        | True      | Simple | Format: Text Max length: 850 |
| sprk_outputtypeid     | sprk_OutputTypeId     | Output Type     | Lookup           | Not Translated                         | True      | Simple | Targets: sprk_aioutputtype   |
| sprk_outputtypeidname | sprk_OutputTypeIdName | Not Translated  | String           | Not Translated                         | True      | Simple | Format: Text Max length: 850 |

## Entity: Analysis Playbook

### Metadata

| Property            | Value                                                      |
| ------------------- | ---------------------------------------------------------- |
| Display Name        | Analysis Playbook                                          |
| Plural Display Name | Analysis Playbooks                                         |
| Description         | Reusable combinations of Action + Scopes + Output settings |
| Schema Name         | sprk_analysisplaybook                                      |
| Logical Name        | sprk_analysisplaybook                                      |
| Object Type Code    | 10804                                                      |
| Is Custom Entity    | True                                                       |
| Ownership Type      | UserOwned                                                  |

### Attributes

| Logical Name            | Schema Name             | Display Name      | Attribute Type   | Description                            | Is Custom | Type   | Additional data                          |
| ----------------------- | ----------------------- | ----------------- | ---------------- | -------------------------------------- | --------- | ------ | ---------------------------------------- |
| sprk_analysisplaybookid | sprk_analysisplaybookId | Analysis Playbook | Uniqueidentifier | Unique identifier for entity instances | False     | Simple |                                          |
| sprk_description        | sprk_description        | Description       | Memo             | Not Translated                         | True      | Simple | Format: TextArea Max length: 4000        |
| sprk_ispublic           | sprk_ispublic           | Is Public         | Boolean          | Visible to all users                   | True      | Simple | True: Yes False: No Default Value: False |
| sprk_ispublicname       | sprk_ispublicName       | Not Translated    | Virtual          | Not Translated                         | True      | Simple |                                          |
| sprk_name               | sprk_name               | Name              | String           | Playbook name                          | True      | Simple | Format: Text Max length: 200             |
| sprk_outputtypeid       | sprk_OutputTypeId       | Output Type       | Lookup           | Not Translated                         | True      | Simple | Targets: sprk_aioutputtype               |
| sprk_outputtypeidname   | sprk_OutputTypeIdName   | Not Translated    | String           | Not Translated                         | True      | Simple | Format: Text Max length: 850             |

## Entity: Analysis Knowledge

### Metadata

| Property            | Value                                                              |
| ------------------- | ------------------------------------------------------------------ |
| Display Name        | Analysis Knowledge                                                 |
| Plural Display Name | Analysis Knowledge                                                 |
| Description         | Knowledge sources for RAG (rules, policies, templates, prior work) |
| Schema Name         | sprk_analysisknowledge                                             |
| Logical Name        | sprk_analysisknowledge                                             |
| Object Type Code    | 10798                                                              |
| Is Custom Entity    | True                                                               |
| Ownership Type      | UserOwned                                                          |

### Attributes

| Logical Name               | Schema Name                | Display Name       | Attribute Type   | Description                            | Is Custom | Type   | Additional data                     |
| -------------------------- | -------------------------- | ------------------ | ---------------- | -------------------------------------- | --------- | ------ | ----------------------------------- |
| sprk_analysisid            | sprk_AnalysisId            | Analysis           | Lookup           | Not Translated                         | True      | Simple | Targets: sprk_analysis              |
| sprk_analysisidname        | sprk_AnalysisIdName        | Not Translated     | String           | Not Translated                         | True      | Simple | Format: Text Max length: 200        |
| sprk_analysisknowledgeid   | sprk_analysisknowledgeId   | Analysis Knowledge | Uniqueidentifier | Unique identifier for entity instances | False     | Simple |                                     |
| sprk_content               | sprk_content               | Content            | Memo             | Inline content for rules/templates     | True      | Simple | Format: TextArea Max length: 100000 |
| sprk_description           | sprk_description           | Description        | Memo             | Not Translated                         | True      | Simple | Format: TextArea Max length: 4000   |
| sprk_documentid            | sprk_documentid            | Document           | Lookup           | Not Translated                         | True      | Simple | Targets: sprk_document              |
| sprk_documentidname        | sprk_documentidName        | Not Translated     | String           | Not Translated                         | True      | Simple | Format: Text Max length: 850        |
| sprk_knowledgesourceid     | sprk_KnowledgeSourceId     | Knowledge Source   | Lookup           | Not Translated                         | True      | Simple | Targets: sprk_aiknowledgesource     |
| sprk_knowledgesourceidname | sprk_KnowledgeSourceIdName | Not Translated     | String           | Not Translated                         | True      | Simple | Format: Text Max length: 850        |
| sprk_knowledgetypeid       | sprk_KnowledgeTypeId       | Knowledge Type     | Lookup           | Not Translated                         | True      | Simple | Targets: sprk_aiknowledgetype       |
| sprk_knowledgetypeidname   | sprk_KnowledgeTypeIdName   | Not Translated     | String           | Not Translated                         | True      | Simple | Format: Text Max length: 850        |
| sprk_name                  | sprk_name                  | Name               | String           | Knowledge source name                  | True      | Simple | Format: Text Max length: 200        |

## Entity: Analysis Action Type

### Metadata

| Property            | Value                   |
| ------------------- | ----------------------- |
| Display Name        | Analysis Action Type    |
| Plural Display Name | Analysis Action Types   |
| Description         |                         |
| Schema Name         | sprk_AnalysisActionType |
| Logical Name        | sprk_analysisactiontype |
| Object Type Code    | 10696                   |
| Is Custom Entity    | True                    |
| Ownership Type      | UserOwned               |

### Attributes

| Logical Name              | Schema Name               | Display Name         | Attribute Type   | Description                            | Is Custom | Type   | Additional data              |
| ------------------------- | ------------------------- | -------------------- | ---------------- | -------------------------------------- | --------- | ------ | ---------------------------- |
| sprk_analysisactiontypeid | sprk_AnalysisActionTypeId | Analysis Action Type | Uniqueidentifier | Unique identifier for entity instances | False     | Simple |                              |
| sprk_name                 | sprk_Name                 | Name                 | String           |                                        | True      | Simple | Format: Text Max length: 850 |

## Entity: Analysis Action

### Metadata

| Property            | Value                                                            |
| ------------------- | ---------------------------------------------------------------- |
| Display Name        | Analysis Action                                                  |
| Plural Display Name | Analysis Actions                                                 |
| Description         | Defines what the AI should do (e.g. Summarize, Review Agreement) |
| Schema Name         | sprk_analysisaction                                              |
| Logical Name        | sprk_analysisaction                                              |
| Object Type Code    | 10796                                                            |
| Is Custom Entity    | True                                                             |
| Ownership Type      | UserOwned                                                        |

### Attributes

| Logical Name          | Schema Name           | Display Name    | Attribute Type   | Description                            | Is Custom | Type   | Additional data                       |
| --------------------- | --------------------- | --------------- | ---------------- | -------------------------------------- | --------- | ------ | ------------------------------------- |
| sprk_actiontype       | sprk_actiontype       | Action Type     | Lookup           |                                        | True      | Simple | Targets: sprk_analysisactiontype      |
| sprk_actiontypeid     | sprk_ActionTypeId     | Action Type     | Lookup           | Not Translated                         | True      | Simple | Targets: sprk_analysisactiontype      |
| sprk_actiontypeidname | sprk_ActionTypeIdName | Not Translated  | String           | Not Translated                         | True      | Simple | Format: Text Max length: 850          |
| sprk_actiontypename   | sprk_actiontypeName   | Not Translated  | String           | Not Translated                         | True      | Simple | Format: Text Max length: 850          |
| sprk_analysisactionid | sprk_analysisactionId | Analysis Action | Uniqueidentifier | Unique identifier for entity instances | False     | Simple |                                       |
| sprk_analysisid       | sprk_AnalysisId       | Analysis        | Lookup           | Not Translated                         | True      | Simple | Targets: sprk_analysis                |
| sprk_analysisidname   | sprk_AnalysisIdName   | Not Translated  | String           | Not Translated                         | True      | Simple | Format: Text Max length: 200          |
| sprk_description      | sprk_description      | Description     | Memo             | User-facing description of the action  | True      | Simple | Format: TextArea Max length: 4000     |
| sprk_name             | sprk_name             | Name            | String           | Action name (e.g. Summarize Document)  | True      | Simple | Format: Text Max length: 200          |
| sprk_sortorder        | sprk_sortorder        | Sort Order      | Integer          | Display order in the UI                | True      | Simple | Minimum value: 0 Maximum value: 10000 |
| sprk_systemprompt     | sprk_systemprompt     | System Prompt   | Memo             | Base prompt template for the AI        | True      | Simple | Format: TextArea Max length: 100000   |

## Entity: Analysis

### Metadata

| Property            | Value                                              |
| ------------------- | -------------------------------------------------- |
| Display Name        | Analysis                                           |
| Plural Display Name | Analyses                                           |
| Description         | AI-driven analysis execution records for documents |
| Schema Name         | sprk_analysis                                      |
| Logical Name        | sprk_analysis                                      |
| Object Type Code    | 10795                                              |
| Is Custom Entity    | True                                               |
| Ownership Type      | UserOwned                                          |

### Attributes

| Logical Name          | Schema Name           | Display Name     | Attribute Type   | Description                               | Is Custom | Type   | Additional data                            |
| --------------------- | --------------------- | ---------------- | ---------------- | ----------------------------------------- | --------- | ------ | ------------------------------------------ |
| sprk_actionid         | sprk_actionid         | Action           | Lookup           | Analysis action definition                | True      | Simple | Targets: sprk_analysisaction               |
| sprk_actionidname     | sprk_actionidName     | Not Translated   | String           | Not Translated                            | True      | Simple | Format: Text Max length: 200               |
| sprk_analysisid       | sprk_analysisId       | Analysis         | Uniqueidentifier | Unique identifier for entity instances    | False     | Simple |                                            |
| sprk_chathistory      | sprk_chathistory      | Chat History     | Memo             |                                           | True      | Simple | Format: Text Max length: 1048576           |
| sprk_completedon      | sprk_completedon      | Completed On     | DateTime         | Analysis completion timestamp             | True      | Simple | Format: DateAndTime                        |
| sprk_documentid       | sprk_documentid       | Document         | Lookup           | Parent document being analyzed            | True      | Simple | Targets: sprk_document                     |
| sprk_documentidname   | sprk_documentidName   | Not Translated   | String           | Not Translated                            | True      | Simple | Format: Text Max length: 850               |
| sprk_errormessage     | sprk_errormessage     | Error Message    | Memo             | Error details if analysis failed          | True      | Simple | Format: TextArea Max length: 2000          |
| sprk_finaloutput      | sprk_finaloutput      | Final Output     | Memo             |                                           | True      | Simple | Format: Text Max length: 100000            |
| sprk_inputtokens      | sprk_inputtokens      | Input Tokens     | Integer          | Token usage (input)                       | True      | Simple | Minimum value: 0 Maximum value: 2147483647 |
| sprk_name             | sprk_name             | Name             | String           | Analysis title/name                       | True      | Simple | Format: Text Max length: 200               |
| sprk_outputfileid     | sprk_outputfileid     | Output File      | Lookup           | Saved output as new Document              | True      | Simple | Targets: sprk_document                     |
| sprk_outputfileidname | sprk_outputfileidName | Not Translated   | String           | Not Translated                            | True      | Simple | Format: Text Max length: 850               |
| sprk_outputtokens     | sprk_outputtokens     | Output Tokens    | Integer          | Token usage (output)                      | True      | Simple | Minimum value: 0 Maximum value: 2147483647 |
| sprk_playbook         | sprk_Playbook         | Playbook         | Lookup           |                                           | True      | Simple | Targets: sprk_analysisplaybook             |
| sprk_playbookname     | sprk_PlaybookName     | Not Translated   | String           | Not Translated                            | True      | Simple | Format: Text Max length: 200               |
| sprk_sessionid        | sprk_sessionid        | Session ID       | String           | Current editing session identifier        | True      | Simple | Format: Text Max length: 50                |
| sprk_startedon        | sprk_startedon        | Started On       | DateTime         | Analysis start timestamp                  | True      | Simple | Format: DateAndTime                        |
| sprk_workingdocument  | sprk_workingdocument  | Working Document | Memo             | Current working output in Markdown format | True      | Simple | Format: TextArea Max length: 100000        |

## Entity: AI Tool Type

### Metadata

| Property            | Value           |
| ------------------- | --------------- |
| Display Name        | AI Tool Type    |
| Plural Display Name | AI Tool Types   |
| Description         |                 |
| Schema Name         | sprk_AIToolType |
| Logical Name        | sprk_aitooltype |
| Object Type Code    | 10821           |
| Is Custom Entity    | True            |
| Ownership Type      | UserOwned       |

### Attributes

| Logical Name      | Schema Name       | Display Name | Attribute Type   | Description                            | Is Custom | Type   | Additional data              |
| ----------------- | ----------------- | ------------ | ---------------- | -------------------------------------- | --------- | ------ | ---------------------------- |
| sprk_aitooltypeid | sprk_AIToolTypeId | AI Tool Type | Uniqueidentifier | Unique identifier for entity instances | False     | Simple |                              |
| sprk_name         | sprk_Name         | Name         | String           |                                        | True      | Simple | Format: Text Max length: 850 |

## Entity: AI Skill Type

### Metadata

| Property            | Value            |
| ------------------- | ---------------- |
| Display Name        | AI Skill Type    |
| Plural Display Name | AI Skill Types   |
| Description         |                  |
| Schema Name         | sprk_AISkillType |
| Logical Name        | sprk_aiskilltype |
| Object Type Code    | 10816            |
| Is Custom Entity    | True             |
| Ownership Type      | UserOwned        |

### Attributes

| Logical Name       | Schema Name        | Display Name  | Attribute Type   | Description                            | Is Custom | Type   | Additional data              |
| ------------------ | ------------------ | ------------- | ---------------- | -------------------------------------- | --------- | ------ | ---------------------------- |
| sprk_aiskilltypeid | sprk_AISkillTypeId | AI Skill Type | Uniqueidentifier | Unique identifier for entity instances | False     | Simple |                              |
| sprk_name          | sprk_Name          | Name          | String           |                                        | True      | Simple | Format: Text Max length: 850 |

## Entity: AI Retrieval Mode

### Metadata

| Property            | Value                |
| ------------------- | -------------------- |
| Display Name        | AI Retrieval Mode    |
| Plural Display Name | AI Retrieval Modes   |
| Description         |                      |
| Schema Name         | sprk_AIRetrievalMode |
| Logical Name        | sprk_airetrievalmode |
| Object Type Code    | 10817                |
| Is Custom Entity    | True                 |
| Ownership Type      | UserOwned            |

### Attributes

| Logical Name                 | Schema Name                  | Display Name          | Attribute Type   | Description                            | Is Custom | Type   | Additional data                                                                                                     |
| ---------------------------- | ---------------------------- | --------------------- | ---------------- | -------------------------------------- | --------- | ------ | ------------------------------------------------------------------------------------------------------------------- |
| sprk_airetrievalmodeid       | sprk_AIRetrievalModeId       | AI Retrieval Mode     | Uniqueidentifier | Unique identifier for entity instances | False     | Simple |                                                                                                                     |
| sprk_code                    | sprk_Code                    | Code                  | String           |                                        | True      | Simple | Format: Text Max length: 100                                                                                        |
| sprk_defaultazureservice     | sprk_DefaultAzureService     | Default Azure Service | Picklist         |                                        | True      | Simple | Options: 100000000: Azure AI Search 100000001: Dataverse 100000002: Azure Functions 100000003: ComsosDB Default: -1 |
| sprk_defaultazureservicename | sprk_defaultazureserviceName | Not Translated        | Virtual          | Not Translated                         | True      | Simple |                                                                                                                     |
| sprk_description             | sprk_Description             | Description           | Memo             |                                        | True      | Simple | Format: Text Max length: 4000                                                                                       |
| sprk_executiontype           | sprk_executiontype           | Execution Type        | Picklist         |                                        | True      | Simple | Options: 100000000: RAG 100000001: Structured 100000002: Rules 100000003: Graph 100000004: Event Default: -1        |
| sprk_executiontypename       | sprk_executiontypeName       | Not Translated        | Virtual          | Not Translated                         | True      | Simple |                                                                                                                     |
| sprk_isdeterministic         | sprk_IsDeterministic         | Is Deterministic      | Boolean          |                                        | True      | Simple | True: Yes False: No Default Value: False                                                                            |
| sprk_isdeterministicname     | sprk_isdeterministicName     | Not Translated        | Virtual          | Not Translated                         | True      | Simple |                                                                                                                     |
| sprk_name                    | sprk_Name                    | Name                  | String           |                                        | True      | Simple | Format: Text Max length: 850                                                                                        |
| sprk_supportscitations       | sprk_supportscitations       | Supports Citations    | Boolean          |                                        | True      | Simple | True: Yes False: No Default Value: False                                                                            |
| sprk_supportscitationsname   | sprk_supportscitationsName   | Not Translated        | Virtual          | Not Translated                         | True      | Simple |                                                                                                                     |
| sprk_supportsiteration       | sprk_supportsiteration       | Supports Iteration    | Boolean          |                                        | True      | Simple | True: Yes False: No Default Value: False                                                                            |
| sprk_supportsiterationname   | sprk_supportsiterationName   | Not Translated        | Virtual          | Not Translated                         | True      | Simple |                                                                                                                     |
| sprk_version                 | sprk_Version                 | Version               | Decimal          |                                        | True      | Simple | Minimum value: -100000000000 Maximum value: 100000000000 Precision: 2                                               |

## Entity: AI Output Type

### Metadata

| Property            | Value             |
| ------------------- | ----------------- |
| Display Name        | AI Output Type    |
| Plural Display Name | AI Output Types   |
| Description         |                   |
| Schema Name         | sprk_AIOutputType |
| Logical Name        | sprk_aioutputtype |
| Object Type Code    | 10827             |
| Is Custom Entity    | True              |
| Ownership Type      | UserOwned         |

### Attributes

| Logical Name        | Schema Name         | Display Name   | Attribute Type   | Description                            | Is Custom | Type   | Additional data              |
| ------------------- | ------------------- | -------------- | ---------------- | -------------------------------------- | --------- | ------ | ---------------------------- |
| sprk_aioutputtypeid | sprk_AIOutputTypeId | AI Output Type | Uniqueidentifier | Unique identifier for entity instances | False     | Simple |                              |
| sprk_name           | sprk_Name           | Name           | String           |                                        | True      | Simple | Format: Text Max length: 850 |

## Entity: AI Knowledge Type

### Metadata

| Property            | Value                |
| ------------------- | -------------------- |
| Display Name        | AI Knowledge Type    |
| Plural Display Name | AI Knowledge Types   |
| Description         |                      |
| Schema Name         | sprk_AIKnowledgeType |
| Logical Name        | sprk_aiknowledgetype |
| Object Type Code    | 10496                |
| Is Custom Entity    | True                 |
| Ownership Type      | UserOwned            |

### Attributes

| Logical Name           | Schema Name            | Display Name      | Attribute Type   | Description                            | Is Custom | Type   | Additional data              |
| ---------------------- | ---------------------- | ----------------- | ---------------- | -------------------------------------- | --------- | ------ | ---------------------------- |
| sprk_aiknowledgetypeid | sprk_AIKnowledgeTypeId | AI Knowledge Type | Uniqueidentifier | Unique identifier for entity instances | False     | Simple |                              |
| sprk_name              | sprk_Name              | Name              | String           |                                        | True      | Simple | Format: Text Max length: 850 |

## Entity: AI Knowledge Source

### Metadata

| Property            | Value                  |
| ------------------- | ---------------------- |
| Display Name        | AI Knowledge Source    |
| Plural Display Name | AI Knowledge Sources   |
| Description         |                        |
| Schema Name         | sprk_AIKnowledgeSource |
| Logical Name        | sprk_aiknowledgesource |
| Object Type Code    | 10650                  |
| Is Custom Entity    | True                   |
| Ownership Type      | UserOwned              |

### Attributes

| Logical Name             | Schema Name              | Display Name        | Attribute Type   | Description                            | Is Custom | Type   | Additional data               |
| ------------------------ | ------------------------ | ------------------- | ---------------- | -------------------------------------- | --------- | ------ | ----------------------------- |
| sprk_aiknowledgesourceid | sprk_AIKnowledgeSourceId | AI Knowledge Source | Uniqueidentifier | Unique identifier for entity instances | False     | Simple |                               |
| sprk_knowledgetypeid     | sprk_KnowledgeTypeId     | Knowledge Type      | Lookup           | Not Translated                         | True      | Simple | Targets: sprk_aiknowledgetype |
| sprk_knowledgetypeidname | sprk_KnowledgeTypeIdName | Not Translated      | String           | Not Translated                         | True      | Simple | Format: Text Max length: 850  |
| sprk_name                | sprk_Name                | Name                | String           |                                        | True      | Simple | Format: Text Max length: 850  |
| sprk_retrievalmodeid     | sprk_RetrievalModeId     | Retrieval Mode      | Lookup           | Not Translated                         | True      | Simple | Targets: sprk_airetrievalmode |
| sprk_retrievalmodeidname | sprk_RetrievalModeIdName | Not Translated      | String           | Not Translated                         | True      | Simple | Format: Text Max length: 850  |

## Entity: AI Knowledge Deployment

### Metadata

| Property            | Value                      |
| ------------------- | -------------------------- |
| Display Name        | AI Knowledge Deployment    |
| Plural Display Name | AI Knowledge Deployments   |
| Description         |                            |
| Schema Name         | sprk_AIKnowledgeDeployment |
| Logical Name        | sprk_aiknowledgedeployment |
| Object Type Code    | 10440                      |
| Is Custom Entity    | True                       |
| Ownership Type      | UserOwned                  |

### Attributes

| Logical Name                 | Schema Name                  | Display Name            | Attribute Type   | Description                            | Is Custom | Type   | Additional data                 |
| ---------------------------- | ---------------------------- | ----------------------- | ---------------- | -------------------------------------- | --------- | ------ | ------------------------------- |
| sprk_aiknowledgedeploymentid | sprk_AIKnowledgeDeploymentId | AI Knowledge Deployment | Uniqueidentifier | Unique identifier for entity instances | False     | Simple |                                 |
| sprk_knowledgesourceid       | sprk_KnowledgeSourceId       | Knowledge Source        | Lookup           | Not Translated                         | True      | Simple | Targets: sprk_aiknowledgesource |
| sprk_knowledgesourceidname   | sprk_KnowledgeSourceIdName   | Not Translated          | String           | Not Translated                         | True      | Simple | Format: Text Max length: 850    |
| sprk_name                    | sprk_Name                    | Name                    | String           |                                        | True      | Simple | Format: Text Max length: 850    |
