/**
 * TodoWizardDialog.tsx
 * Thin wrapper around CreateRecordWizard for "Create New To Do".
 *
 * A To Do is a sprk_event record with sprk_todoflag=true.
 * Default export enables React.lazy() dynamic import.
 *
 * Dependencies are injected via props -- no solution-specific imports.
 * Uses IDataService (not IWebApi) for shared library portability.
 */
import * as React from 'react';
import { Button, Text, tokens } from '@fluentui/react-components';
import { CheckmarkCircleFilled } from '@fluentui/react-icons';
import { CreateRecordWizard, } from '../CreateRecordWizard';
import { CreateTodoStep } from './CreateTodoStep';
import { TodoService } from './todoService';
import { EMPTY_TODO_FORM } from './formTypes';
import { searchContactsAsLookup, searchOrganizationsAsLookup, searchUsersAsLookup, } from '../CreateMatterWizard/matterService';
import { EntityCreationService } from '../../services/EntityCreationService';
// ---------------------------------------------------------------------------
// TodoWizardDialog
// ---------------------------------------------------------------------------
const TodoWizardDialog = ({ open, onClose, dataService, authenticatedFetch, bffBaseUrl, embedded, resolveSpeContainerId, }) => {
    const [formValid, setFormValid] = React.useState(false);
    const [formValues, setFormValues] = React.useState(EMPTY_TODO_FORM);
    const formValuesRef = React.useRef(formValues);
    formValuesRef.current = formValues;
    React.useEffect(() => {
        if (open) {
            setFormValid(false);
            setFormValues(EMPTY_TODO_FORM);
        }
    }, [open]);
    const handleSearchContacts = React.useCallback((query) => searchContactsAsLookup(dataService, query), [dataService]);
    const handleSearchOrganizations = React.useCallback((query) => searchOrganizationsAsLookup(dataService, query), [dataService]);
    const handleSearchUsers = React.useCallback((query) => searchUsersAsLookup(dataService, query), [dataService]);
    const config = React.useMemo(() => ({
        title: 'Create New To Do',
        entityLabel: 'to do',
        filesStepSubtitle: 'Upload documents to associate with this to do, or click Next to skip.',
        finishingLabel: 'Creating to do\u2026',
        infoStep: {
            id: 'create-record',
            label: 'To Do Details',
            canAdvance: () => formValid,
            renderContent: (_wizardFiles) => (React.createElement(CreateTodoStep, { dataService: dataService, onValidChange: setFormValid, onFormValues: setFormValues, initialFormValues: formValues })),
        },
        searchContacts: handleSearchContacts,
        searchOrganizations: handleSearchOrganizations,
        searchUsers: handleSearchUsers,
        resolveSpeContainerId: resolveSpeContainerId
            ? resolveSpeContainerId
            : () => Promise.resolve(''),
        buildEmailSubject: (entityName) => `New To Do: ${entityName}`,
        buildEmailBody: (fields) => `Dear Colleague,\n\nA new to do item, "${fields.title || ''}", has been created.\n\n` +
            `Please review and take any necessary action.\n\n` +
            `Kind regards,\n[Your Name]`,
        getEntityName: () => formValuesRef.current.title,
        getFormFields: () => ({
            title: formValuesRef.current.title,
        }),
        onFinish: async (_context) => {
            const currentFormValues = formValuesRef.current;
            const todoService = new TodoService(dataService);
            const result = await todoService.createTodo(currentFormValues);
            if (!result.success) {
                throw new Error(result.errorMessage ?? 'Failed to create to do');
            }
            const todoId = result.todoId;
            const todoName = result.todoName;
            const warnings = [];
            // Send email (if selected)
            if (_context.selectedActions.includes('send-email') && _context.followOn.emailTo.trim()) {
                // Wrap IDataService to adapt to IWebApiWithCreate expected by EntityCreationService
                const webApiAdapter = {
                    createRecord: async (entityName, data) => {
                        const id = await dataService.createRecord(entityName, data);
                        return { id };
                    },
                    retrieveRecord: (entityName, id, options) => dataService.retrieveRecord(entityName, id, options),
                    retrieveMultipleRecords: (entityName, options) => dataService.retrieveMultipleRecords(entityName, options),
                    updateRecord: async (entityName, id, data) => {
                        await dataService.updateRecord(entityName, id, data);
                        return { id };
                    },
                    deleteRecord: async (entityName, id) => {
                        await dataService.deleteRecord(entityName, id);
                        return { id };
                    },
                };
                const entityService = new EntityCreationService(webApiAdapter, authenticatedFetch, bffBaseUrl);
                const emailResult = await entityService.sendEmail({
                    to: _context.followOn.emailTo,
                    subject: _context.followOn.emailSubject,
                    body: _context.followOn.emailBody,
                    associations: [{ entityType: 'sprk_event', entityId: todoId, entityName: todoName }],
                });
                if (!emailResult.success && emailResult.warning)
                    warnings.push(emailResult.warning);
            }
            const hasWarnings = warnings.length > 0;
            return {
                icon: (React.createElement(CheckmarkCircleFilled, { fontSize: 64, style: { color: tokens.colorPaletteGreenForeground1 } })),
                title: hasWarnings ? 'To Do created with warnings' : 'To Do created!',
                body: (React.createElement(Text, { size: 300, style: { color: tokens.colorNeutralForeground2 } },
                    React.createElement("span", { style: { color: tokens.colorBrandForeground1, fontWeight: 600 } },
                        "\u201C",
                        todoName,
                        "\u201D"),
                    ' ',
                    "has been added to your to do list",
                    hasWarnings
                        ? ', though some operations could not complete. See details below.'
                        : '.')),
                actions: (React.createElement(Button, { appearance: "primary", onClick: onClose }, "Done")),
                warnings,
            };
        },
    }), [formValid, formValues, dataService, authenticatedFetch, bffBaseUrl, handleSearchContacts, handleSearchOrganizations, handleSearchUsers, onClose, resolveSpeContainerId]);
    // Adapt IDataService to the webApi shape that CreateRecordWizard expects
    const webApiAdapter = React.useMemo(() => ({
        createRecord: async (entityName, data) => {
            const id = await dataService.createRecord(entityName, data);
            return { id };
        },
        retrieveRecord: (entityName, id, options) => dataService.retrieveRecord(entityName, id, options),
        retrieveMultipleRecords: (entityName, options, _maxPageSize) => dataService.retrieveMultipleRecords(entityName, options),
    }), [dataService]);
    return (React.createElement(CreateRecordWizard, { open: open, onClose: onClose, webApi: webApiAdapter, config: config, embedded: embedded }));
};
export { TodoWizardDialog };
export default TodoWizardDialog;
//# sourceMappingURL=TodoWizardDialog.js.map