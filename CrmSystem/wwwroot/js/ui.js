/* =====================================================================
   Shared rendering helpers for grids and pages
   File: wwwroot/js/ui.js
   Every status badge = icon + text (+ color) -> never color alone.
   ===================================================================== */
(function () {
    "use strict";
    const t = App.t, esc = App.escapeHtml;

    const badge = (color, icon, text) =>
        `<span class="badge-soft ${color}"><i class="f7-icons" aria-hidden="true">${icon}</i>${esc(text)}</span>`;

    const map = {
        ClientStatus: { 1: ["green", "checkmark_circle"], 2: ["gray", "pause_circle"], 3: ["blue", "sparkles"], 4: ["gray", "archivebox"] },
        ProjectStatus: { 1: ["blue", "sparkles"], 2: ["indigo", "bubble_left_bubble_right"], 3: ["teal", "doc_text"], 4: ["purple", "hammer"], 5: ["orange", "arrow_right_arrow_left"], 6: ["green", "rosette"], 7: ["red", "xmark_circle"], 8: ["gray", "pause_circle"] },
        QuotationStatus: { 1: ["gray", "pencil"], 2: ["blue", "paperplane"], 3: ["orange", "eye"], 4: ["green", "checkmark_seal"], 5: ["red", "xmark_circle"], 6: ["gray", "clock"], 7: ["gray", "slash_circle"] },
        TenderStatus: { 1: ["blue", "flag"], 2: ["indigo", "pencil"], 3: ["teal", "paperplane"], 4: ["orange", "eye"], 5: ["green", "rosette"], 6: ["red", "xmark_circle"], 7: ["gray", "slash_circle"], 8: ["gray", "clock"] },
        FollowUpStatus: { 1: ["blue", "clock"], 2: ["green", "checkmark_circle"], 3: ["gray", "slash_circle"], 4: ["red", "exclamationmark_circle"] },
        ReminderStatus: { 1: ["blue", "bell"], 2: ["green", "bell_fill"], 3: ["orange", "bell_fill"], 4: ["gray", "bell_slash"], 5: ["red", "exclamationmark_triangle"] },
        Priority: { 1: ["gray", "arrow_down"], 2: ["blue", "minus"], 3: ["orange", "arrow_up"], 4: ["red", "exclamationmark"] }
    };

    function status(kind, value) {
        if (value === null || value === undefined) return "";
        const [color, icon] = (map[kind] && map[kind][value]) || ["gray", "circle"];
        return badge(color, icon, t(`${kind}.${value}`));
    }

    /** Activity type chip: colored icon square + name. */
    function typeChip(name, icon, color) {
        const c = color || "#8E8E93";
        return `<span class="type-chip"><span class="dot" style="background:${esc(c)}22;color:${esc(c)}"><i class="f7-icons" aria-hidden="true">${esc(icon || "circle")}</i></span>${esc(name)}</span>`;
    }

    const link = (href, text) => `<a href="${esc(href)}" class="cell-main">${esc(text)}</a>`;
    const twoLines = (main, sub) => `<div class="cell-main">${esc(main ?? "—")}</div>${sub ? `<div class="cell-sub">${esc(sub)}</div>` : ""}`;
    const clip = (text) => text ? `<div class="cell-clip" title="${esc(text)}">${esc(text)}</div>` : `<span class="text-tertiary">—</span>`;
    const dash = (v) => (v === null || v === undefined || v === "") ? `<span class="text-tertiary">—</span>` : esc(v);
    const date = (v) => v ? `<span class="tabular ltr">${App.formatDate(v)}</span>` : dash(null);
    const dateTime = (d, tm) => d ? `<span class="tabular ltr">${App.formatDate(d)}</span>${tm ? ` <span class="text-tertiary tabular ltr">${App.formatTime(tm)}</span>` : ""}` : dash(null);
    const money = (v, cur) => (v === null || v === undefined) ? dash(null) : `<span class="tabular">${esc(App.formatMoney(v, cur))}</span>`;

    /** Days until a date (local calendar) as a colored badge: expired / today / n days. */
    function daysLeft(isoDate) {
        if (!isoDate) return "";
        const today = new Date(App.config.todayLocal + "T00:00:00Z");
        const target = new Date(String(isoDate).substring(0, 10) + "T00:00:00Z");
        const days = Math.round((target - today) / 86400000);
        if (days < 0) return badge("red", "clock", t("Ui.ExpiredDaysAgo", -days));
        if (days === 0) return badge("orange", "clock", t("Ui.Today"));
        if (days <= 7) return badge("orange", "clock", t("Ui.DaysLeft", days));
        return badge("gray", "clock", t("Ui.DaysLeft", days));
    }

    /** Row action buttons. items: [{icon, title, attrs, danger}] */
    function actions(items) {
        return `<div class="row-actions">${items.filter(Boolean).map((a) =>
            `<button type="button" class="icon-btn ${a.danger ? "danger" : ""}" title="${esc(a.title)}" aria-label="${esc(a.title)}" ${a.attrs}><i class="f7-icons sm" aria-hidden="true">${a.icon}</i></button>`).join("")}</div>`;
    }

    window.Ui = { badge, status, typeChip, link, twoLines, clip, dash, date, dateTime, money, daysLeft, actions };
})();
