*Event Related Table Schema


**Event sprk_event

| Field in Design      | Field In Dataverse        | Schema Name                  | Type                | Notes                                                                                                                                        |
|----------------------|---------------------------|------------------------------|---------------------|----------------------------------------------------------------------------------------------------------------------------------------------|
| Subject              | Event Name                | sprk_eventname               | Single line text    |                                                                                                                                              |
| Description          | Description               | sprk_description             | Multiline text      |                                                                                                                                              |
|                      | Regarding Account         | sprk_regardingaccount        | Lookup              | Relationship (N:1) sprk_event_RegardingAccount_n1                                                                                            |
|                      | Regarding Analysis        | sprk_regardinganalysis       | Lookup              | Relationship (N:1) sprk_event_RegardingAnalysis_n1                                                                                           |
|                      | Regarding Contact         | sprk_regardingcontact        | Lookup              | Relationship (N:1) sprk_event_RegardingContact_n1                                                                                            |
|                      | Regarding Invoice         | sprk_regardinginvoice        | Lookup              | Relationship (N:1) sprk_event_RegardingInvoice_n1                                                                                            |
|                      | Regarding Matter          | sprk_regardingmatter         | Lookup              | Relationship (N:1) sprk_event_RegardingMatter_n1                                                                                             |
|                      | Regarding Project         | sprk_regardingproject        | Lookup              | Relationship (N:1) sprk_event_RegardingProject_n1                                                                                            |
|                      | Regarding Budget          | sprk_regardingbudget         | Lookup              | Relationship (N:1) sprk_event_RegardingBudget_n1                                                                                             |
|                      | Regarding Work Assignment | sprk_regardingworkassignment | Lookup              | Relationship (N:1) sprk_event_RegardingWorkAssignment_n1                                                                                     |
|                      | Regarding Record Id       | sprk_regardingrecordid       | Single line of text |                                                                                                                                              |
|                      | Regarding Record Name     | sprk_regardingrecordname     | Single line of text |                                                                                                                                              |
|                      | Regarding Record Type     | sprk_regardingrecordtype     | Choice              | Project (0), Matter (1), Invoice (2), Analysis (3), Account (4), Contact (5)                                                                 |
| Related Task Event   | Related Event             | sprk_relatedevent            | Lookup              | Relationship (N:1) event_RelatedEvent_n1                                                                                                     |
|                      | Related Event Type        | sprk_relatedeventtype        | Choice              | Reminder (0), Notification (1), Extension (2)                                                                                                |
| Reminder Offset Type | Related Eevnt Offset Type | sprk_relatedeventoffsettype  | Choice              | Hours Before Due (0), Hours After Due (1), Days Before Due (2), Days After Due (3), Fixed Date/Time (4)                                      |
| State                | Status                    | statecode                    | Choice              | Active (0); Inactive (1)                                                                                                                     |
| Remind At            | Remind At                 | sprk_remindat                | DateTime            |                                                                                                                                              |
|                      | Status Reason             | statuscode                   | Choice              | Draft (1), Planned (659,490,001), Ooen (659,4909,002), On Hold (659,490,003); Completed (2), Cancelled (659,490,004), Deleted (659,490,005)  |
| Event Date           | Base Date                 | sprk_basedate                | Date only           |                                                                                                                                              |
| Due Date             | Due Date                  | sprk_duedate                 | Date Only           |                                                                                                                                              |
| Completed On         | Completed Date            | sprk_completeddate           | Date only           |                                                                                                                                              |
| Priority             | Priority                  | sprk_priority                | Choice              | Low (0), Normal (1), High (2), Urgent (3)                                                                                                    |
| Source               | Source                    | sprk_source                  | Choice              | User (0), System (1), Workflow (2), External (3)                                                                                             |
| Event Type           | Event Type                | sprk_eventtype_ref           | Lookup              | relationship (N:1) sprk_event_EventType_n1                                                                                                   |
| Event Set            | Event Set                 | sprk_eventset                | Lookup              | relationship (N:1) sprk_event_EventSet_n1                                                                                                    |


**Event Log sprk_eventlog

| Field in Design | Field In Dataverse | Schema Name       | Type              | Notes                                                               |
|-----------------|--------------------|-------------------|-------------------|---------------------------------------------------------------------|
|                 | Event Set Name     | sprk_eventsetname | Single line text  | Primary field                                                       |
| Event           | Event              | sprk_event        | Lookup            | Relationship (N:1)                                                  |
| Action          | Action             | sprk_action       | Choice            | Created (0), Updated (1), Completed (2), Cancelled (3), Deleted (4) |
| Details         | Description        | sprk_description  | Multiline Text    |                                                                     |
|                 | Event Log          | sprk_eventlogid   | Unique identifier | Primary key                                                         |


**Event Type sprk_eventype

| Field in Design     | Field In Dataverse | Schema Name           | Type                | Notes                    |
|---------------------|--------------------|-----------------------|---------------------|--------------------------|
| Name                | Name               | sprk_name             | Single line text    | Primary field            |
| Code                | Event Code         | sprk_eventcode        | Single line of text |                          |
| Description         | Description        | sprk_description      | Multiline of text   |                          |
| Is Active           | Status             | statecode             | Choice              | Active (0), Inactive (1) |
|                     | Status Reason      | statuscode            | Choice              | Active (1), Inactive (2) |
| Requires Due Date   | Requires Due Date  | sprk_requiresduedate  | Choice              | No (0), Yes (1)          |
| Requires Event Date | Requires Base Date | sprk_requiresbasedate | Choice              | No (0), Yes (1)          |


