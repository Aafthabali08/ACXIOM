// Extra browser-side help for the record form. The server repeats every one of these checks,
// this file only gives the user quicker feedback.

const recordForm = document.getElementById('recordForm');

if (recordForm) {
    const moduleName = recordForm.dataset.module;
    const stageSelect = document.getElementById('Stage');
    const statusSelect = document.getElementById('Status');
    const probabilityInput = document.getElementById('Probability');
    const closeDateInput = document.getElementById('CloseDate');
    const dueDateInput = document.getElementById('DueDate');

    // Open deals cannot close in the past; planned follow-ups cannot be scheduled in the past.
    function updateDateLimits() {
        if (closeDateInput) {
            closeDateInput.min = statusSelect.value === 'Open' ? closeDateInput.dataset.today : '';
        }
        if (dueDateInput && moduleName === 'followups') {
            const mustBeFuture = dueDateInput.dataset.isNew === 'true' || statusSelect.value === 'Planned';
            dueDateInput.min = mustBeFuture ? dueDateInput.dataset.today : '';
        }
    }

    // Stage decides the status (Won / Lost / Open) and fixes the probability for closed deals.
    function onStageChanged() {
        const closedStage = ['Won', 'Lost'].includes(stageSelect.value);
        statusSelect.value = closedStage ? stageSelect.value : 'Open';
        if (stageSelect.value === 'Won') probabilityInput.value = 100;
        if (stageSelect.value === 'Lost') probabilityInput.value = 0;
        updateDateLimits();
    }

    // Inline messages worded as in the assignment (PDF 5.4). The server checks the same rules again.
    function mustNotBeInPast(dateInput) {
        if (dateInput.id === 'CloseDate') return statusSelect.value === 'Open';
        if (dateInput.id === 'DueDate') return moduleName === 'followups' && (dateInput.dataset.isNew === 'true' || statusSelect.value === 'Planned');
        return false;
    }
    if (window.jQuery && jQuery.validator) {
        jQuery.validator.addMethod('notpast', (value, element) => !value || !mustNotBeInPast(element) || value >= element.dataset.today);
        jQuery.validator.unobtrusive.adapters.addBool('notpast');
        jQuery.validator.addMethod('positive', value => value === '' || Number(value) > 0);
        jQuery.validator.unobtrusive.adapters.addBool('positive');
    }

    stageSelect?.addEventListener('change', onStageChanged);
    statusSelect?.addEventListener('change', updateDateLimits);
    updateDateLimits();

    // Follow-ups and activities must point to exactly one customer, lead or opportunity.
    const relatedFieldIds = moduleName === 'followups' ? ['CustomerId', 'LeadId', 'OpportunityId']
        : moduleName === 'activities' ? ['CustomerId', 'LeadId'] : [];
    const relatedFields = relatedFieldIds.map(fieldId => document.getElementById(fieldId)).filter(Boolean);

    function checkRelatedRecord() {
        if (relatedFields.length === 0) return;
        const selectedCount = relatedFields.filter(field => field.value).length;
        relatedFields[0].setCustomValidity(selectedCount === 1 ? '' : 'Select exactly one related record.');
    }
    relatedFields.forEach(field => field.addEventListener('change', checkRelatedRecord));

    // The related record and the new record must have the same owner, so picking a customer,
    // lead or opportunity also picks its owner in "Assigned to" (managers and admins only see more than one).
    const ownerSelect = document.getElementById('OwnerId');
    const ownerByRecordId = JSON.parse(recordForm.dataset.recordOwners || '{}');
    ['CustomerId', 'LeadId', 'OpportunityId'].map(fieldId => document.getElementById(fieldId)).filter(Boolean).forEach(relatedSelect => {
        relatedSelect.addEventListener('change', () => {
            const ownerId = ownerByRecordId[relatedSelect.value];
            if (ownerSelect && ownerId && [...ownerSelect.options].some(option => option.value === ownerId)) {
                ownerSelect.value = ownerId;
            }
        });
    });

    recordForm.addEventListener('submit', submitEvent => {
        checkRelatedRecord();
        // jQuery unobtrusive validation shows the inline messages. Only when it finds nothing wrong
        // do we fall back to the browser check, which covers date limits and the related-record rule.
        const inlineValidationFailed = window.jQuery && jQuery(recordForm).data('validator') && !jQuery(recordForm).valid();
        if (inlineValidationFailed) {
            submitEvent.preventDefault();
            return;
        }
        if (!recordForm.checkValidity()) {
            submitEvent.preventDefault();
            recordForm.reportValidity();
        }
    });
}
