/* =====================================================================
   Reusable grid definitions (same columns on module pages, client profile,
   project page and reports).
   File: wwwroot/js/columns.js      Depends on: app.js, grid.js, ui.js
   Each builder: Cols.xxxGrid(selector, { clientId, projectId, hideClient, params, ...gridOptions })
   ===================================================================== */
(function () {
    "use strict";
    const t = App.t, esc = App.escapeHtml, u = App.url;

    const reloadAttr = (gridId) => `data-reload-grid="${gridId}"`;
    const clientCell = (r) => r.clientId ? `<a class="cell-main" href="${u("Clients/Details/" + r.clientId)}">${esc(r.companyName)}</a>` : esc(r.companyName);

    function make(selector, url, columns, defaults, opts) {
        opts = opts || {};
        const fixed = {};
        if (opts.clientId) fixed.clientId = opts.clientId;
        if (opts.projectId) fixed.projectId = opts.projectId;
        if (opts.hideClient) columns = columns.filter((c) => c.key !== "companyName");
        const extra = opts.params;
        return new CrmGrid(selector, Object.assign({}, defaults, opts, {
            url,
            columns,
            params: () => Object.assign({}, fixed, extra ? extra() : {})
        }));
    }

    // ---------------------------------------------------------------- activities
    function activitiesGrid(selector, opts) {
        const id = document.querySelector(selector).id;
        return make(selector, u("Activities/List"), [
            { key: "activityDate", title: t("Field.Date"), sortable: true, width: "130px", render: (r) => Ui.dateTime(r.activityDate, r.activityTime) },
            { key: "companyName", title: t("Field.Company"), sortable: true, render: clientCell },
            { key: "typeName", title: t("Field.Type"), sortable: true, render: (r) => Ui.typeChip(r.typeName, r.typeIcon, r.typeColor) },
            { key: "contactName", title: t("Field.Contact"), sortable: true, mobileHidden: true, render: (r) => Ui.dash(r.contactName) },
            { key: "notes", title: t("Field.Notes"), render: (r) => (r.subject ? `<div class="cell-main">${esc(r.subject)}</div>` : "") + Ui.clip(r.notes) },
            { key: "outcome", title: t("Field.Outcome"), mobileHidden: true, render: (r) => Ui.clip(r.outcome) },
            { key: "performedByName", title: t("Field.PerformedBy"), sortable: true, mobileHidden: true },
            {
                key: "actions", title: "", align: "end", render: (r) => r.canEdit ? Ui.actions([
                    { icon: "pencil", title: t("Common.Edit"), attrs: `data-open-form="${u("Activities/Edit/" + r.activityId)}" ${reloadAttr(id)}` },
                    { icon: "trash", title: t("Common.Delete"), danger: true, attrs: `data-delete="${u("Activities/Delete/" + r.activityId)}" ${reloadAttr(id)}` }
                ]) : ""
            }
        ], { sort: "activityDate", dir: "desc" }, opts);
    }

    // ---------------------------------------------------------------- follow-ups
    function followUpsGrid(selector, opts) {
        const id = document.querySelector(selector).id;
        return make(selector, u("FollowUps/List"), [
            { key: "effectiveStatus", title: t("Field.Status"), sortable: true, width: "120px", render: (r) => Ui.status("FollowUpStatus", r.effectiveStatus) },
            { key: "dueAtUtc", title: t("Field.Due"), sortable: true, width: "150px", render: (r) => Ui.dateTime(r.dueDate, r.dueTime) },
            { key: "companyName", title: t("Field.Company"), sortable: true, render: clientCell },
            { key: "typeName", title: t("Field.Type"), sortable: true, mobileHidden: true, render: (r) => Ui.typeChip(r.typeName, r.typeIcon, r.typeColor) },
            { key: "contactName", title: t("Field.Contact"), sortable: true, render: (r) => r.contactName ? Ui.twoLines(r.contactName, r.contactPhone) : Ui.dash() },
            { key: "description", title: t("Field.Description"), render: (r) => Ui.clip(r.description) },
            { key: "assignedToName", title: t("Field.AssignedTo"), sortable: true, mobileHidden: true },
            { key: "priority", title: t("Field.Priority"), sortable: true, render: (r) => Ui.status("Priority", r.priority) },
            {
                key: "actions", title: "", align: "end", render: (r) => Ui.actions([
                    { icon: "eye", title: t("Common.View"), attrs: `data-open-form="${u("FollowUps/Details/" + r.followUpId)}" ${reloadAttr(id)}` },
                    r.isOpen && r.canEdit ? { icon: "checkmark_circle", title: t("FollowUps.MarkCompleted"), attrs: `data-open-form="${u("FollowUps/Complete/" + r.followUpId)}" ${reloadAttr(id)}` } : null,
                    r.isOpen && r.canEdit ? { icon: "pencil", title: t("Common.Edit"), attrs: `data-open-form="${u("FollowUps/Edit/" + r.followUpId)}" ${reloadAttr(id)}` } : null,
                    r.isOpen && r.canEdit ? { icon: "slash_circle", title: t("FollowUps.Cancel"), danger: true, attrs: `data-open-form="${u("FollowUps/Cancel/" + r.followUpId)}" ${reloadAttr(id)}` } : null
                ])
            }
        ], {
            sort: "dueAtUtc", dir: "asc",
            rowClass: (r) => r.effectiveStatus === 4 ? "row-alert" : (r.effectiveStatus === 3 ? "row-muted" : "")
        }, opts);
    }

    // ---------------------------------------------------------------- projects
    function projectsGrid(selector, opts) {
        const id = document.querySelector(selector).id;
        return make(selector, u("Projects/List"), [
            { key: "projectName", title: t("Field.ProjectName"), sortable: true, render: (r) => `<a class="cell-main" href="${u("Projects/Details/" + r.projectId)}">${esc(r.projectName)}</a>` },
            { key: "companyName", title: t("Field.Company"), sortable: true, render: clientCell },
            { key: "projectStatus", title: t("Field.Status"), sortable: true, render: (r) => Ui.status("ProjectStatus", r.projectStatus) },
            { key: "projectValue", title: t("Field.Value"), sortable: true, align: "end", render: (r) => Ui.money(r.projectValue, r.currency) },
            { key: "probability", title: t("Field.Probability"), sortable: true, align: "center", render: (r) => `<span class="tabular">${r.probability}%</span>` },
            { key: "expectedEndDate", title: t("Field.ExpectedEndDate"), sortable: true, mobileHidden: true, render: (r) => Ui.date(r.expectedEndDate) },
            { key: "assignedToName", title: t("Field.AssignedTo"), sortable: true, mobileHidden: true, render: (r) => Ui.dash(r.assignedToName) },
            {
                key: "actions", title: "", align: "end", render: (r) => r.canEdit ? Ui.actions([
                    { icon: "pencil", title: t("Common.Edit"), attrs: `data-open-form="${u("Projects/Edit/" + r.projectId)}" ${reloadAttr(id)}` },
                    { icon: "trash", title: t("Common.Delete"), danger: true, attrs: `data-delete="${u("Projects/Delete/" + r.projectId)}" ${reloadAttr(id)}` }
                ]) : ""
            }
        ], { sort: "createdAt", dir: "desc", rowUrl: (r) => u("Projects/Details/" + r.projectId) }, opts);
    }

    // ---------------------------------------------------------------- quotations
    const openStatuses = [1, 2, 3];
    function quotationsGrid(selector, opts) {
        const id = document.querySelector(selector).id;
        return make(selector, u("Quotations/List"), [
            { key: "quotationNumber", title: t("Field.QuotationNumber"), sortable: true, render: (r) => Ui.twoLines(r.quotationNumber, r.title) },
            { key: "companyName", title: t("Field.Company"), sortable: true, render: clientCell },
            { key: "submissionDate", title: t("Field.SubmissionDate"), sortable: true, render: (r) => Ui.date(r.submissionDate) },
            { key: "quotationValue", title: t("Field.Value"), sortable: true, align: "end", render: (r) => Ui.money(r.quotationValue, r.currency) },
            { key: "validUntil", title: t("Field.ValidUntil"), sortable: true, render: (r) => Ui.date(r.validUntil) + (r.validUntil && openStatuses.includes(r.status) ? "<div>" + Ui.daysLeft(r.validUntil) + "</div>" : "") },
            { key: "status", title: t("Field.Status"), sortable: true, render: (r) => Ui.status("QuotationStatus", r.status) },
            { key: "tenderNumber", title: t("Field.TenderNumber"), mobileHidden: true, render: (r) => Ui.dash(r.tenderNumber) },
            {
                key: "actions", title: "", align: "end", render: (r) => r.canEdit ? Ui.actions([
                    { icon: "pencil", title: t("Common.Edit"), attrs: `data-open-form="${u("Quotations/Edit/" + r.quotationId)}" ${reloadAttr(id)}` },
                    { icon: "trash", title: t("Common.Delete"), danger: true, attrs: `data-delete="${u("Quotations/Delete/" + r.quotationId)}" ${reloadAttr(id)}` }
                ]) : ""
            }
        ], { sort: "submissionDate", dir: "desc" }, opts);
    }

    // ---------------------------------------------------------------- tenders
    const openTender = [1, 2, 3, 4];
    function tendersGrid(selector, opts) {
        const id = document.querySelector(selector).id;
        return make(selector, u("Tenders/List"), [
            { key: "tenderNumber", title: t("Field.TenderNumber"), sortable: true, render: (r) => Ui.twoLines(r.tenderNumber, r.title) },
            { key: "companyName", title: t("Field.Company"), sortable: true, render: clientCell },
            { key: "submissionDate", title: t("Field.SubmissionDate"), sortable: true, render: (r) => Ui.date(r.submissionDate) },
            { key: "closingDate", title: t("Field.ClosingDate"), sortable: true, render: (r) => r.closingDate ? App.formatDateTime(r.closingDate) + (openTender.includes(r.status) ? "<div>" + Ui.daysLeft(r.closingDate) + "</div>" : "") : Ui.dash() },
            { key: "tenderValue", title: t("Field.Value"), sortable: true, align: "end", render: (r) => Ui.money(r.tenderValue, r.currency) },
            { key: "status", title: t("Field.Status"), sortable: true, render: (r) => Ui.status("TenderStatus", r.status) },
            {
                key: "actions", title: "", align: "end", render: (r) => r.canEdit ? Ui.actions([
                    { icon: "pencil", title: t("Common.Edit"), attrs: `data-open-form="${u("Tenders/Edit/" + r.tenderId)}" ${reloadAttr(id)}` },
                    { icon: "trash", title: t("Common.Delete"), danger: true, attrs: `data-delete="${u("Tenders/Delete/" + r.tenderId)}" ${reloadAttr(id)}` }
                ]) : ""
            }
        ], { sort: "closingDate", dir: "asc" }, opts);
    }

    // ---------------------------------------------------------------- contacts
    function contactsGrid(selector, opts) {
        const id = document.querySelector(selector).id;
        return make(selector, u("Contacts/List"), [
            { key: "fullName", title: t("Field.FullName"), sortable: true, render: (r) => `<span class="cell-main">${esc(r.fullName)}</span>${r.isPrimary ? " " + Ui.badge("blue", "star_fill", t("Contacts.Primary")) : ""}` },
            { key: "companyName", title: t("Field.Company"), sortable: true, render: clientCell },
            { key: "jobTitle", title: t("Field.JobTitle"), sortable: true, render: (r) => Ui.twoLines(r.jobTitle || "—", r.department) },
            { key: "phone", title: t("Field.Phone"), render: (r) => (r.mobile || r.phone) ? `<a dir="ltr" href="tel:${esc(r.mobile || r.phone)}">${esc(r.mobile || r.phone)}</a>` : Ui.dash() },
            { key: "email", title: t("Field.Email"), sortable: true, mobileHidden: true, render: (r) => r.email ? `<a href="mailto:${esc(r.email)}">${esc(r.email)}</a>` : Ui.dash() },
            {
                key: "actions", title: "", align: "end", render: (r) => r.canEdit ? Ui.actions([
                    { icon: "pencil", title: t("Common.Edit"), attrs: `data-open-form="${u("Contacts/Edit/" + r.contactId)}" ${reloadAttr(id)}` },
                    { icon: "trash", title: t("Common.Delete"), danger: true, attrs: `data-delete="${u("Contacts/Delete/" + r.contactId)}" ${reloadAttr(id)}` }
                ]) : ""
            }
        ], { sort: "fullName", dir: "asc" }, opts);
    }

    window.Cols = { activitiesGrid, followUpsGrid, projectsGrid, quotationsGrid, tendersGrid, contactsGrid };
})();
