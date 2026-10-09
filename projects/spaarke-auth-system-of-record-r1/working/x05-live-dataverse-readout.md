# Live Dataverse read-out (dev, read-only via Dataverse MCP)

> Captured 2026-10-09 with two read-only SELECT queries. Environment = the one the Dataverse MCP connection targets (dev).

## A. sprk_* environment variables (definitions and current values)

| schema name | type | default | current value |
|---|---|---|---|
| `sprk_ApplicationInsightsKey` | Secret | `—` | `— (no value row; default applies)` |
| `sprk_AzureAiSearchEndpoint` | String | `—` | `— (no value row; default applies)` |
| `sprk_AzureAiSearchKey` | Secret | `—` | `— (no value row; default applies)` |
| `sprk_AzureOpenAiEndpoint` | String | `—` | `https://spaarke-openai-dev.openai.azure.com/` |
| `sprk_AzureOpenAiKey` | Secret | `—` | `— (no value row; default applies)` |
| `sprk_BffApiAppId` | String | `1e40baad-e065-4aea-a8d4-4b7ab273458c` | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| `sprk_BffApiBaseUrl` | String | `https://spaarke-bff-dev.azurewebsites.net/api` | `https://spaarke-bff-dev.azurewebsites.net/api` |
| `sprk_CustomerTenantId` | String | `—` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| `sprk_DeploymentEnvironment` | String | `Development` | `— (no value row; default applies)` |
| `sprk_DocumentIntelligenceEndpoint` | String | `—` | `— (no value row; default applies)` |
| `sprk_DocumentIntelligenceKey` | Secret | `—` | `— (no value row; default applies)` |
| `sprk_EnableAiFeatures` | Boolean | `yes` | `— (no value row; default applies)` |
| `sprk_EnableMultiDocumentAnalysis` | Boolean | `no` | `— (no value row; default applies)` |
| `sprk_KeyVaultUrl` | String | `—` | `— (no value row; default applies)` |
| `sprk_MsalClientId` | String | `1e40baad-e065-4aea-a8d4-4b7ab273458c` | `170c98e1-d486-4355-bcbe-170454e0207c` |
| `sprk_PromptFlowEndpoint` | String | `—` | `— (no value row; default applies)` |
| `sprk_RedisConnectionString` | Secret | `—` | `— (no value row; default applies)` |
| `sprk_ReportingModuleEnabled` | Boolean | `yes` | `yes` |
| `sprk_ShareLinkBaseUrl` | String | `—` | `— (no value row; default applies)` |
| `sprk_SharePointEmbeddedContainerId` | String | `b!yLRd…(default)` | `b!vzGD…(set)` |
| `sprk_TenantId` | String | `a221a95e-6abc-4434-aecc-e48338a1b2f2` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |

Observations:
- `sprk_MsalClientId` current value `170c98e1` (SDAP-PCF-CLIENT) overrides its default `1e40baad` (the BFF app).
- `sprk_BffApiBaseUrl` value ends in `/api` (relevant to master commit c8a87d818, which strips a trailing `/api` in access-control form scripts).
- `sprk_TenantId` and `sprk_CustomerTenantId` are both Spaarke's tenant `a221a95e…`.
- Secret-type variables have no value rows in dev (values not read).

## B. Application users (160 rows, all listed; ★ = Spaarke-related by name or known app id)

| | full name | application id | Entra object id | disabled | business unit | known as |
|---|---|---|---|---|---|---|
|  | # AIBuilderProd | `ef32e2a3-262a-44e5-a270-4dfb7b6d0bb2` | `` | False | Spaarke |  |
|  | # AIBuilder_StructuredML_Gcc_CDS_Legacy | `8b62382d-110e-4db8-83a6-c7e8ee84296a` | `` | False | Spaarke |  |
|  | # AIBuilder_StructuredML_Prod_CDS | `be5f0473-6b57-40f8-b0a9-b3054b41b99e` | `` | False | Spaarke |  |
|  | # Agent 365 Tools | `ea9ffc3e-8a23-4a7d-836d-234d7c7565c1` | `` | False | Spaarke |  |
|  | # ApolloProdFirstParty | `8c04f0eb-27fc-44cc-9e48-914b9202890a` | `` | False | Spaarke |  |
|  | # AppAgents S2S Prod Application | `ba23ec0e-2282-4622-b270-0c3808d014dd` | `` | False | Spaarke |  |
|  | # AppDeploymentOrchestration | `886d9650-b672-4531-b16f-4617b5492d2f` | `` | False | Spaarke |  |
|  | # AriaMdlExporter | `3bd99f43-a70c-49da-9378-6e15c2dee59d` | `` | False | Spaarke |  |
|  | # BAP | `978b42f5-e03a-4695-b8df-454959d032c8` | `` | False | Spaarke |  |
|  | # BizQA | `aeb01831-b358-4750-92ce-722e4f3ea7e8` | `` | False | Spaarke |  |
|  | # CAPI_Prod | `8f839529-6eb6-4a14-8b41-5cc3348c97b6` | `` | False | Spaarke |  |
|  | # CCADataAnalyticsML | `8840ddc6-aa78-428a-b02c-b64c19f43e86` | `` | False | Spaarke |  |
|  | # CDSAcisInfraAppGlobal | `36ce0b96-1fb8-475d-a90e-e5e16909a22f` | `` | False | Spaarke |  |
|  | # CDSFileStorage | `07ce06e6-4ae9-4466-bca4-2984fa04d057` | `` | False | Spaarke |  |
|  | # CDSGlobalDiscovery | `6eb29b24-9d89-4f26-bf2f-9a84ed2499b8` | `` | False | Spaarke |  |
|  | # CDSReportService-APJ | `fc3595c0-7e9e-49a3-9e42-dba7cb49874f` | `` | False | Spaarke |  |
|  | # CDSReportService-CAN | `830ad5e7-9d40-4a34-80e6-86d250c6d3de` | `` | False | Spaarke |  |
|  | # CDSReportService-CHE | `b07f1bd3-d0df-4399-a37f-9cbca7667f1c` | `` | False | Spaarke |  |
|  | # CDSReportService-EMEA | `bbc6bd9b-90e6-4e0a-8655-de0d8ff55ac5` | `` | False | Spaarke |  |
|  | # CDSReportService-FRA | `b6f57e5e-5889-46b0-b214-d94067c31cf7` | `` | False | Spaarke |  |
|  | # CDSReportService-GBR | `84cc28bd-d7ad-4631-9c3c-144e12a242ee` | `` | False | Spaarke |  |
|  | # CDSReportService-GER | `2f6478e9-1793-48cb-afe1-0c2e6ca7dff0` | `` | False | Spaarke |  |
|  | # CDSReportService-IND | `3713c6b4-6a96-4b21-9af5-aeb07cf9bc1c` | `` | False | Spaarke |  |
|  | # CDSReportService-JPN | `6b3be972-4d64-497f-b58d-2de904334482` | `` | False | Spaarke |  |
|  | # CDSReportService-KOR | `51d0f3cc-2ffa-4e7b-a152-ad169e1f7f74` | `` | False | Spaarke |  |
|  | # CDSReportService-NAM | `4ff9e282-7ac7-42f8-b48b-a99a415897b7` | `` | False | Spaarke |  |
|  | # CDSReportService-NOR | `5b0ca8c6-322f-4b08-a4b3-3e1a985957ca` | `` | False | Spaarke |  |
|  | # CDSReportService-OCE | `46c33425-7d07-4ac4-8892-d38b88afbd34` | `` | False | Spaarke |  |
|  | # CDSReportService-SAM | `d884866a-a28a-4c20-8f77-e5847b7dd00a` | `` | False | Spaarke |  |
|  | # CDSReportService-SGP | `2c44fdb6-b856-4859-a4f9-fb5544a87648` | `` | False | Spaarke |  |
|  | # CDSReportService-SWE | `cf996560-fa0c-4eb3-9643-712471068b48` | `` | False | Spaarke |  |
|  | # CDSReportService-UAE | `49dbc5b7-3473-42e3-bcc9-8b801ac48f16` | `` | False | Spaarke |  |
|  | # CDSReportService-ZAF | `0a4b6ee5-e28b-4e32-b6db-51425f49901f` | `` | False | Spaarke |  |
|  | # CDSUserManagement | `c92229fa-e4e7-47fc-81a8-01386459c021` | `` | False | Spaarke |  |
|  | # CDSUserManagementApi | `7b9e7d05-a719-471f-9e41-5387557bd84b` | `` | False | Spaarke |  |
|  | # CatalogServiceNam | `2787672d-a8be-4b05-8d90-11bd6043c7f7` | `` | False | Spaarke |  |
|  | # CloudFlowRunHistory | `e19b049c-3802-4ef1-881b-13bb47db0cb4` | `` | False | Spaarke |  |
|  | # ConnectorManagementServiceBackend | `59534ab2-0027-4a45-b9ec-61d1b6b871c9` | `` | False | Spaarke |  |
|  | # ContactCenterRTA ReportingService Prod App | `bfdb51ce-f2f9-4a80-89fe-7e7abef15c3b` | `` | False | Spaarke |  |
|  | # ContactCenterRTA ReportingWarmpathService Prod App | `9b92cfc4-72ce-4359-a4da-3a9e876663b8` | `` | False | Spaarke |  |
|  | # Contextual AI S2S Prod Application | `1a28d27c-cc86-4773-b5ef-10a70a3da179` | `` | False | Spaarke |  |
|  | # D365OfficeDataSvc | `08ad9b35-1979-40a6-a6f2-289cf7289374` | `` | False | Spaarke |  |
|  | # DAMS | `84e37c07-7362-4d9f-b4b1-09be02be0195` | `` | False | Spaarke |  |
|  | # DAMSIsland | `5293b54e-dc85-45d3-a067-2102f619174f` | `` | False | Spaarke |  |
|  | # DORS_Prod | `300e430c-5476-4a1d-b223-35194efd4e57` | `` | False | Spaarke |  |
|  | # DV-MetadataService | `354dde10-0e43-4fff-8859-a34fcc65d392` | `` | False | Spaarke |  |
|  | # DataLakeStorage | `546068c3-99b1-4890-8e93-c8aeadcfe56a` | `` | False | Spaarke |  |
|  | # DataServices | `fbfc635b-88c9-40c7-8f58-a64cc69c8c8f` | `` | False | Spaarke |  |
|  | # DataSyncFramework-APJ | `a4cc9e72-f218-4f77-b329-4b7f68ecc625` | `` | False | Spaarke |  |
|  | # DataSyncFramework-CAN | `78f6a875-fef6-49ed-a504-b38bcfb510b4` | `` | False | Spaarke |  |
|  | # DataSyncFramework-CHE | `16a2f0d9-0bea-4cbd-b847-d16cd6e75f5b` | `` | False | Spaarke |  |
|  | # DataSyncFramework-EMEA-FRA-GBR-GER | `9357c0fc-7ead-4bc5-9657-a3136481d220` | `` | False | Spaarke |  |
|  | # DataSyncFramework-IND-UAE-ZAF | `c3357a20-2a2b-4453-9c36-bde9504fedcb` | `` | False | Spaarke |  |
|  | # DataSyncFramework-JPN | `3adc5a17-683e-427b-be42-c53854d16cea` | `` | False | Spaarke |  |
|  | # DataSyncFramework-KOR | `428e1d8d-5724-44ff-a6ea-2e03021ba87d` | `` | False | Spaarke |  |
|  | # DataSyncFramework-NAM | `7a575ec8-8d12-42eb-9edc-b93f3aa92c48` | `` | False | Spaarke |  |
|  | # DataSyncFramework-NOR | `f2851b16-ed8b-4d67-a521-c0031a53623a` | `` | False | Spaarke |  |
|  | # DataSyncFramework-OCE | `7c7fb27f-9d49-4b02-995d-3337bbe02c97` | `` | False | Spaarke |  |
|  | # DataSyncFramework-SAM | `65b3a33d-a83d-4867-9eee-8de1ed5f4afe` | `` | False | Spaarke |  |
|  | # DataSyncService-APJ | `babc3507-b8d0-4675-82bf-48503fe799cc` | `` | False | Spaarke |  |
|  | # DataSyncService-CAN | `998ee914-eed7-42c2-b235-99056a1e8cd9` | `` | False | Spaarke |  |
|  | # DataSyncService-CHE | `06dd99d8-4bb3-4be2-bd41-ea7d90bbb66f` | `` | False | Spaarke |  |
|  | # DataSyncService-EMEA-FRA-GBR-GER | `b509b65b-fced-4fca-ab6b-d4802a571f7e` | `` | False | Spaarke |  |
|  | # DataSyncService-IND-UAE-ZAF | `e6e54007-8ccf-4333-8702-339919243c9c` | `` | False | Spaarke |  |
|  | # DataSyncService-JPN | `bcf29efb-c7be-4be9-8d6a-bf584b04ae11` | `` | False | Spaarke |  |
|  | # DataSyncService-KOR | `ab0fd681-e577-45e6-a68f-3c4d948a5ae2` | `` | False | Spaarke |  |
|  | # DataSyncService-NAM | `d4121b85-e1a0-4606-91c0-9f3ad0513296` | `` | False | Spaarke |  |
|  | # DataSyncService-NOR | `4bac8391-0d93-4131-8103-6a816726a4f1` | `` | False | Spaarke |  |
|  | # DataSyncService-OCE | `0923d34c-9090-4013-a1e6-bdc0b40297ea` | `` | False | Spaarke |  |
|  | # DataSyncService-SAM | `71702cdd-7db9-4393-9112-66d67bbaad81` | `` | False | Spaarke |  |
|  | # Dataverse Information Protection | `3196dfc3-4211-4632-b214-6eb482ebc1d6` | `` | False | Spaarke |  |
|  | # DataverseReportService | `a97cd606-1ee8-4f11-a6e3-1130171f9403` | `` | False | Spaarke |  |
|  | # Deflection | `c5eab488-a722-4b91-98d7-b3f3f2fca099` | `` | False | Spaarke |  |
|  | # Dynamics365Athena2 | `7f15f9d9-cad0-44f1-bbba-d36650e07765` | `` | False | Spaarke |  |
|  | # Dynamics365SCV | `d6037e40-282c-493d-8f63-f255e36c6ef4` | `` | False | Spaarke |  |
|  | # DynamicsInstaller | `5bdbebb2-509f-458e-b56e-d0b934dfdafa` | `` | False | Spaarke |  |
|  | # EnterpriseSales | `b20d0d3a-dc90-485b-ad11-6031e769e221` | `` | False | Spaarke |  |
|  | # EventStreamsService | `0a294109-6daf-4f27-8482-9ff84bd2ee9e` | `` | False | Spaarke |  |
|  | # Flow-CDSNativeConnector | `3e7d837d-b5c3-42b7-96c0-39a1c5b36595` | `` | False | Spaarke |  |
|  | # Flow-CDSNativeConnectorAsia | `7f243e3b-906a-4a32-b946-10670ab1f75a` | `` | False | Spaarke |  |
|  | # Flow-CDSNativeConnectorAustralia | `bb9b626b-82f3-4c8b-8402-6bf7cdd162e0` | `` | False | Spaarke |  |
|  | # Flow-CDSNativeConnectorBrazil | `d9c3cbe8-48a4-4ce4-b9ba-09dcf5f6aa1c` | `` | False | Spaarke |  |
|  | # Flow-CDSNativeConnectorCanada | `2a1f2ae8-b674-4b1d-97ee-50d25bc03d84` | `` | False | Spaarke |  |
|  | # Flow-CDSNativeConnectorEurope | `9d816c2d-5388-4e18-85cc-754a6e051604` | `` | False | Spaarke |  |
|  | # Flow-CDSNativeConnectorFrance | `3cfbbaf9-d5b2-4ddb-b212-2c888fc84b11` | `` | False | Spaarke |  |
|  | # Flow-CDSNativeConnectorGermany | `c6a9976b-9beb-43b8-9aea-52a55ba8e39b` | `` | False | Spaarke |  |
|  | # Flow-CDSNativeConnectorIndia | `65086433-a120-4658-93ff-2a3709b490e0` | `` | False | Spaarke |  |
|  | # Flow-CDSNativeConnectorItaly | `7288f221-7593-439a-b683-c0dee2ff9438` | `` | False | Spaarke |  |
|  | # Flow-CDSNativeConnectorJapan | `6903d455-735f-4680-a37d-b99e4ec0effa` | `` | False | Spaarke |  |
|  | # Flow-CDSNativeConnectorKorea | `766c30f5-2c1f-423c-b651-6c63567a0466` | `` | False | Spaarke |  |
|  | # Flow-CDSNativeConnectorNorway | `19343905-f69a-4694-b591-4e03ba8dd43b` | `` | False | Spaarke |  |
|  | # Flow-CDSNativeConnectorPoland | `922af705-a0f3-440a-8483-5ad51f0272c4` | `` | False | Spaarke |  |
|  | # Flow-CDSNativeConnectorPreview | `6e163c41-83b6-40e2-a66f-ae3001494d6a` | `` | False | Spaarke |  |
|  | # Flow-CDSNativeConnectorSingapore | `25e45960-83d2-44bb-bbc7-6df1715d4810` | `` | False | Spaarke |  |
|  | # Flow-CDSNativeConnectorSouthAfrica | `099ce982-5f59-4f19-8ac0-1fd296e39f14` | `` | False | Spaarke |  |
|  | # Flow-CDSNativeConnectorSweden | `b1868054-83fb-49be-afe9-a723d1c6d3f0` | `` | False | Spaarke |  |
|  | # Flow-CDSNativeConnectorSwitzerland | `cbb61646-e3d4-4310-9a93-a4a6ce81ac01` | `` | False | Spaarke |  |
|  | # Flow-CDSNativeConnectorUAE | `87aa1d02-457f-4980-a89e-b7534dc5a5bb` | `` | False | Spaarke |  |
|  | # Flow-CDSNativeConnectorUK | `06957b3e-4fcc-4ac0-ad7a-3b6e5a8e36e9` | `` | False | Spaarke |  |
|  | # Flow-CDSNativeConnectorUS | `66adb445-d114-4905-9f2c-ddb926a0a7c8` | `` | False | Spaarke |  |
|  | # Flow-RP | `730d33da-0894-409f-a907-c577151719c5` | `` | False | Spaarke |  |
|  | # GlobalActiveMetricsMonitoringAlerting | `539cea1d-ee00-437f-8189-421e05581f9e` | `` | False | Spaarke |  |
| ★ | # InsightsAppsPlatform | `99ff962b-6252-4b98-8478-0c65a3ea1925` | `` | False | Spaarke |  |
|  | # JobsServiceProd | `e548fb5c-c385-41a6-a31d-6dbc2f0ca8a3` | `` | False | Spaarke |  |
|  | # KISPAdminProd | `965f757f-1a14-47b9-8b2d-4258de0eee16` | `` | False | Spaarke |  |
|  | # KISPIngProd | `808a016e-7cdd-4f5d-8961-c2d69cb444bb` | `` | False | Spaarke |  |
|  | # KISPSyncProd | `dc0f4699-2205-4718-be44-6ade1ccd505b` | `` | False | Spaarke |  |
|  | # KnowledgeQuality | `c99209fa-3f1e-41e7-a509-192430e9ae2a` | `` | False | Spaarke |  |
|  | # Learning Agent | `3a04767c-79cc-4474-9bbf-c5c041f6dc3a` | `` | False | Spaarke |  |
|  | # MCSAIServer | `a0fe9e95-d2cb-4cc8-b91a-fd3b6a42af72` | `` | False | Spaarke |  |
| ★ | # Microsoft Copilot Studio | `96ff4394-9197-43aa-b393-6a41652e21f8` | `` | False | Spaarke |  |
|  | # MicrosoftCustomerEngagementPortalInfra | `30d3b129-0b19-4da1-957d-b4b75098695a` | `` | False | Spaarke |  |
|  | # MicrosoftDynamicsNRDService | `4ade18ba-d41e-45d6-a563-97c67fc0be15` | `` | False | Spaarke |  |
|  | # Omnichannel | `d9ce8cfa-8bd8-4ff1-b39b-5e5dd5742935` | `` | False | Spaarke |  |
|  | # PPMI-powerpagesmanagedidentity-300f0de7-55ce-4fd2-9f3c-bc1fc919d645 | `55e7dd70-0f4f-4836-a82f-89204d09738d` | `6a54e58f-8b70-45d9-b19a-dc1d203d0543` | False | Spaarke |  |
|  | # PPMI-powerpagesmanagedidentity-5f29a057-ff44-4484-abe6-da5f0c81c788 | `e169fb26-f914-4d90-b02a-f6f622cf3e7e` | `4ecb8f76-c180-4dc1-9d88-15e510bb64d1` | False | Spaarke |  |
|  | # PPMI-powerpagesmanagedidentity-ac5479f4-8730-4891-b719-5f882a777273 | `48ff1d33-ea71-4cba-84ec-0d445bee2d88` | `eb892d9c-faa3-4614-bcc9-80dbbf080bef` | False | Spaarke |  |
|  | # PPMIService-PROD | `58e835ab-2e39-46a9-b797-accce6633447` | `` | False | Spaarke |  |
|  | # Pegasus-NAM | `044cab92-64ee-4ca0-9102-255df6c95096` | `` | True | Spaarke |  |
|  | # Power Policy Prod | `c81c4f54-f70c-49e3-a01d-0a44ed684c5a` | `` | False | Spaarke |  |
|  | # Power Policy Service CM PROD | `342f61e2-a864-4c50-87de-86abc6790d49` | `` | False | Spaarke |  |
|  | # PowerAppsCustomerManagementPlaneBackend | `3570e63c-5acf-4f3f-9f15-a49faa5120d3` | `` | False | Spaarke |  |
|  | # PowerAppsDataPlaneBackend | `b6fb6bd6-f0fb-4a60-beb1-4e50afd0eaa9` | `` | False | Spaarke |  |
|  | # PowerAutomate-AiFlowsAppUser-Web | `57ac09e7-b33a-4d99-86f2-d577c3617d64` | `` | False | Spaarke |  |
|  | # PowerAutomate-AiFlowsAppUser-Worker | `5274a130-e0cd-4337-9329-b31dd4610d64` | `` | False | Spaarke |  |
|  | # PowerAutomate-DesktopFlowAI | `dda20762-8b68-4af9-8796-803539494020` | `` | False | Spaarke |  |
|  | # PowerAutomate-DesktopFlowAggregation | `46115bd3-e7b8-4fbc-aa6e-24fdaf02abf1` | `` | False | Spaarke |  |
|  | # PowerAutomate-DesktopFlowRuntime | `53ab501f-3793-448d-8c4e-cae80d89a8f3` | `` | False | Spaarke |  |
|  | # PowerAutomate-MachineManagementRelay | `5dd8e2c0-c583-4711-ba8f-d4fdb7ded207` | `` | False | Spaarke |  |
|  | # PowerAutomate-MachineProvisioning | `51699864-8078-4c9e-a688-09a1db1b2e09` | `` | False | Spaarke |  |
|  | # PowerAutomate-ProcessMining | `dad3c6de-ed58-42ef-989f-9c0303aaeedc` | `` | False | Spaarke |  |
|  | # PowerBIApplicationUser | `00000009-0000-0000-c000-000000000000` | `` | False | Spaarke |  |
|  | # PowerCards-S2S-IL-Prod | `25c0ac13-af11-43d3-ac26-9758831eb238` | `` | False | Spaarke |  |
|  | # PowerPages Data Runtime PROD | `f61b1d65-0f1b-4ec8-8721-cfeca7c30416` | `` | False | Spaarke |  |
|  | # PowerPlatformAuthorization | `8d605dfc-1a04-4da6-9be2-8426724af3f3` | `` | False | Spaarke |  |
| ★ | # PowerPlatformCopilotGovernance | `9c797c40-1fdd-40d9-9389-bbc3891a5bf8` | `` | False | Spaarke |  |
|  | # PowerPlatformDataAnalytics | `7dcff627-a295-4553-9229-b1f3513f82a8` | `` | False | Spaarke |  |
|  | # PowerPlatformEnvironmentManagement | `d1a7a85b-e8f5-4f95-8335-92e4c439fafc` | `` | False | Spaarke |  |
|  | # PowerQueryOnline | `f3b07414-6bf4-46e6-b63f-56941f3f4128` | `` | False | Spaarke |  |
|  | # PpdfCDSClient | `99335b6b-7d9d-4216-8dee-883b26e0ccf7` | `` | False | Spaarke |  |
| ★ | # ProductInsights | `ffa7d2fe-fc04-4599-9f6d-7ca06dd0c4fd` | `` | False | Spaarke |  |
|  | # ProjectForTheWebBackgroundServices | `e62ee429-f250-4aef-a588-7b511fa9a67a` | `` | False | Spaarke |  |
|  | # PromptColumnRuntime | `ada86e08-5ddd-4d97-bd96-bf302f8e4cfc` | `` | False | Spaarke |  |
|  | # PurviewApp | `2d446674-08a2-46f8-9024-a5b078a57c48` | `` | False | Spaarke |  |
|  | # Quartzite | `7d403f55-17ed-4218-869b-0a14fc08cf62` | `` | False | Spaarke |  |
|  | # RelevanceSearch | `1884bdbf-452a-4a11-9c76-afdbdb1b3768` | `` | False | Spaarke |  |
|  | # SSSAdminProd | `bf05f9a3-4755-4682-b2b4-c3d46fe2f12a` | `` | False | Spaarke |  |
|  | # SharePointOnline | `00000003-0000-0ff1-ce00-000000000000` | `` | False | Spaarke |  |
|  | # Source Control Integration Prod App | `b95e6836-cb65-4476-a40e-4c95309530d9` | `` | False | Spaarke |  |
| ★ | # Spaarke DMS-SPE Dev 1 | `fd1325aa-a709-4f15-b1f5-600f30d28875` | `4003c822-ff87-42ee-87e0-44f5eece5944` | True | Spaarke |  |
|  | # TPSProxyService | `2b61b865-d0bd-4c60-9efa-6fa934eefaac` | `` | False | Spaarke |  |
| ★ | # github-actions-spe-infrastructure | `8c85a481-f3a0-46de-b84e-3ede8a4d60c3` | `6edc4cee-1837-4d6d-a0c7-a1c8d6ad1c98` | False | Spaarke | A20 GitHub OIDC |
| ★ | # mi-bff-api-dev | `5967251e-171c-46fe-a6c2-ef843c90309d` | `9fd47efb-7962-492b-ac44-e5ccd0268ebb` | False | Spaarke |  |
| ★ | # mi-ontology-writer-dev | `69040982-612e-469e-a85f-26d5172367c5` | `6cf6d7b6-8dc2-4cfb-a971-649df8605be6` | False | Spaarke |  |
| ★ | # spaarke-bff-api-prod | `92ecc702-d9ae-492d-957e-563244e93d8c` | `56ca416c-931e-4a8f-a023-78b2036d478f` | False | Spaarke |  |
| ★ | # spe-api-dev-67e2xz | `6bbcfa82-14a0-40b5-8695-a271f4bac521` | `56ae2188-c978-4734-ad16-0bc288973f20` | False | Spaarke |  |
| ★ | # sprk-controlplane-dev-uami | `965a4a01-01e1-442b-97a6-6a98308018b3` | `38f7693f-e6e2-4a3e-9acf-7f9e29dd4044` | False | Spaarke | A15 L2 UAMI clientId |
|  | Microsoft Flow | `0eda3b13-ddc9-4c25-b7dd-2f6ea073d6b7` | `` | False | Spaarke |  |
|  | Power Apps Checker Application | `c9299480-c13a-49db-a7ae-cdfe54fe0313` | `23a05708-3e36-4dee-b780-c9604aa782e7` | False | Spaarke |  |
| ★ | SDAP-BFF-SPE-API | `1e40baad-e065-4aea-a8d4-4b7ab273458c` | `d93c832e-9b1d-4ccc-a2a8-9419fbf3fc18` | False | Spaarke | A1 dev BFF |
