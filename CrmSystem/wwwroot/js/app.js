/* =====================================================================
   CRM System - shared front-end helpers
   File: wwwroot/js/app.js
   Depends on: window.AppConfig (rendered by _Layout), Bootstrap bundle,
               jQuery validation (unobtrusive), Tom Select, Chart.js (per page).
   Public API (window.App):
     App.t(key, ...args)               translated text
     App.toast(message, type)          success | error | warning | info
     App.confirm({title, message, confirmText, danger}) -> Promise<boolean>
     App.loading(true|false)           top progress bar
     App.get(url, params) / App.post(url, data)   JSON + CSRF + error toasts
     App.openForm(url, {onSaved})      popup add/edit form (server-rendered partial)
     App.initDynamic(root)             enhance selects / toggles / validation in new HTML
     App.cssVar, formatNumber, formatDate, formatTime, formatMoney, timeAgo, onThemeChange
   Declarative attributes (no page script needed):
     data-open-form="/Clients/Create"  [data-reload-grid="clientsGrid" | data-reload="page"]
     data-delete="/Clients/Delete/5"   [data-confirm-message="..."] [data-reload-grid|data-reload]
     data-post="/FollowUps/Cancel/5"   same options as data-delete (generic POST action)
     <select data-remote="/Lookup/Clients" data-depends-on="ClientId">   remote searchable select
     <select data-enhance>              searchable local select
     <div data-show-when="ScheduleFollowUp">   shown while that checkbox is checked
   ===================================================================== */
(function () {
    "use strict";

    const cfg = window.AppConfig || { lang: "en", rtl: false, i18n: {}, antiforgery: "", urls: {} };
    const themeListeners = [];
    let loadingCount = 0;

    // ------------------------------------------------------------------
    // Text & formatting
    // ------------------------------------------------------------------
    function t(key) {
        let text = (cfg.i18n && cfg.i18n[key]) || key;
        for (let i = 1; i < arguments.length; i++) text = text.replace("{" + (i - 1) + "}", arguments[i]);
        return text;
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;").replace(/'/g, "&#39;");
    }

    const locale = cfg.lang === "ar" ? "ar-OM-u-nu-latn" : "en-GB";
    const numberFormat = new Intl.NumberFormat(locale);
    const moneyFormat = new Intl.NumberFormat(locale, { minimumFractionDigits: 3, maximumFractionDigits: 3 });

    const formatNumber = (n) => numberFormat.format(Number(n) || 0);
    const formatMoney = (n, currency) => (n === null || n === undefined || n === "") ? "—" : `${moneyFormat.format(Number(n))} ${currency || cfg.currency || ""}`.trim();

    /** "2026-11-15" -> 15/11/2026 (calendar date, no time-zone shift). */
    function formatDate(iso) {
        if (!iso) return "";
        const [y, m, d] = String(iso).substring(0, 10).split("-");
        return d && m && y ? `${d}/${m}/${y}` : String(iso);
    }

    /** "14:30:00" -> 02:30 PM. */
    function formatTime(hhmm) {
        if (!hhmm) return "";
        const [h, m] = String(hhmm).split(":").map(Number);
        return new Intl.DateTimeFormat(locale, { hour: "2-digit", minute: "2-digit", hour12: true }).format(new Date(2000, 0, 1, h, m));
    }

    /** "2026-11-15T10:00:00" (local) -> 15/11/2026 10:00 AM */
    function formatDateTime(isoLocal) {
        if (!isoLocal) return "";
        const s = String(isoLocal);
        return `${formatDate(s)} ${s.length > 11 ? formatTime(s.substring(11, 16)) : ""}`.trim();
    }

    function timeAgo(isoUtc) {
        if (!isoUtc) return "";
        const ms = Date.now() - new Date(String(isoUtc).endsWith("Z") ? isoUtc : isoUtc + "Z").getTime();
        const minutes = Math.max(0, Math.round(ms / 60000));
        if (minutes < 1) return t("Js.JustNow");
        if (minutes < 60) return t("Js.MinutesAgo", minutes);
        const hours = Math.round(minutes / 60);
        if (hours < 24) return t("Js.HoursAgo", hours);
        return t("Js.DaysAgo", Math.round(hours / 24));
    }

    const cssVar = (name) => getComputedStyle(document.documentElement).getPropertyValue(name).trim();

    /** App-relative URL (works when the site is hosted in an IIS virtual directory). */
    const url = (path) => (cfg.basePath || "/") + String(path).replace(/^\/+/, "");

    // ------------------------------------------------------------------
    // Toasts
    // ------------------------------------------------------------------
    const toastIcons = { success: "checkmark_circle_fill", error: "xmark_octagon_fill", warning: "exclamationmark_triangle_fill", info: "info_circle_fill" };

    function toast(message, type = "info", timeout = 4500) {
        const stack = document.getElementById("toastStack");
        if (!stack || !message) return;
        const el = document.createElement("div");
        el.className = "app-toast " + type;
        el.setAttribute("role", type === "error" ? "alert" : "status");
        el.innerHTML = `<i class="f7-icons" aria-hidden="true">${toastIcons[type] || toastIcons.info}</i>
                        <div class="flex-grow-1">${escapeHtml(message)}</div>
                        <button type="button" class="toast-close" aria-label="${escapeHtml(t("Common.Close"))}"><i class="f7-icons sm">xmark</i></button>`;
        const remove = () => {
            if (el.classList.contains("leaving")) return;
            el.classList.add("leaving");
            setTimeout(() => el.remove(), 220);
        };
        el.querySelector(".toast-close").addEventListener("click", remove);
        stack.appendChild(el);
        if (timeout > 0) setTimeout(remove, type === "error" || type === "warning" ? timeout + 3000 : timeout);
    }

    // ------------------------------------------------------------------
    // Confirm dialog
    // ------------------------------------------------------------------
    function confirmDialog(options = {}) {
        const modalEl = document.getElementById("confirmModal");
        if (!modalEl || !window.bootstrap) return Promise.resolve(window.confirm(options.message || t("Js.Confirm.Title")));

        modalEl.querySelector("[data-confirm-title]").textContent = options.title || t("Js.Confirm.Title");
        modalEl.querySelector("[data-confirm-message]").textContent = options.message || "";
        const okBtn = modalEl.querySelector("[data-confirm-ok]");
        okBtn.textContent = options.confirmText || t("Js.Confirm.Yes");
        okBtn.className = "btn " + (options.danger === false ? "btn-primary" : "btn-danger");
        modalEl.querySelector("[data-confirm-icon]").hidden = options.danger === false;

        const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
        return new Promise((resolve) => {
            let result = false;
            const onOk = () => { result = true; modal.hide(); };
            const onHidden = () => {
                okBtn.removeEventListener("click", onOk);
                modalEl.removeEventListener("hidden.bs.modal", onHidden);
                resolve(result);
            };
            okBtn.addEventListener("click", onOk);
            modalEl.addEventListener("hidden.bs.modal", onHidden);
            modal.show();
        });
    }

    // ------------------------------------------------------------------
    // Loading indicator
    // ------------------------------------------------------------------
    function loading(on) {
        loadingCount = Math.max(0, loadingCount + (on ? 1 : -1));
        const bar = document.getElementById("topProgress");
        if (bar) bar.classList.toggle("active", loadingCount > 0);
    }

    // ------------------------------------------------------------------
    // HTTP
    // ------------------------------------------------------------------
    async function request(method, url, body, { silent = false, showLoading = true } = {}) {
        if (showLoading) loading(true);
        try {
            const headers = { "Accept": "application/json", "X-Requested-With": "XMLHttpRequest" };
            if (method !== "GET") headers["RequestVerificationToken"] = cfg.antiforgery;
            let payload;
            if (body instanceof FormData) payload = body;
            else if (body !== undefined) { headers["Content-Type"] = "application/json"; payload = JSON.stringify(body); }

            const response = await fetch(url, { method, headers, body: payload, credentials: "same-origin" });
            if (response.status === 401 || (response.redirected && response.url.includes("/Account/Login"))) {
                if (!silent) toast(t("Js.Error.Session"), "error");
                setTimeout(() => window.location.reload(), 1500);
                throw new Error("unauthorized");
            }
            const isJson = (response.headers.get("content-type") || "").includes("application/json");
            const data = isJson ? await response.json() : null;
            if (!response.ok) {
                const message = (data && (data.message || data.title)) || t("Js.Error.Generic");
                if (!silent) toast(message, "error");
                const error = new Error(message);
                error.status = response.status;
                error.data = data;
                throw error;
            }
            return data;
        } catch (err) {
            if (err instanceof TypeError && !silent) toast(t("Js.Error.Network"), "error");
            throw err;
        } finally {
            if (showLoading) loading(false);
        }
    }

    function buildUrl(url, params) {
        if (!params) return url;
        const u = new URL(url, window.location.origin);
        Object.entries(params).forEach(([k, v]) => {
            if (v !== null && v !== undefined && v !== "") u.searchParams.set(k, v);
        });
        return u.pathname + u.search;
    }

    const get = (url, params, opts) => request("GET", buildUrl(url, params), undefined, opts);
    const post = (url, data, opts) => request("POST", url, data ?? {}, opts);

    // ------------------------------------------------------------------
    // Popup forms (Create / Edit). The server returns the whole modal content
    // (header + body + footer inside a <form>). On success it returns JSON.
    // ------------------------------------------------------------------
    let formModal = null;

    async function openForm(url, options = {}) {
        const modalEl = document.getElementById("formModal");
        const content = modalEl.querySelector(".modal-content");
        formModal = formModal || new bootstrap.Modal(modalEl, { backdrop: "static" });
        modalEl.querySelector(".modal-dialog").className = "modal-dialog modal-dialog-centered modal-dialog-scrollable " + (options.size || "modal-lg");
        content.innerHTML = `<div class="p-5 text-center"><div class="spinner mx-auto"></div></div>`;
        formModal.show();

        try {
            loading(true);
            const response = await fetch(url, { headers: { "X-Requested-With": "XMLHttpRequest" }, credentials: "same-origin" });
            if (response.redirected && response.url.includes("/Account/Login")) { window.location.reload(); return; }
            const contentType = response.headers.get("content-type") || "";
            if (!response.ok || contentType.includes("application/json")) {
                const data = contentType.includes("application/json") ? await response.json() : null;
                toast((data && data.message) || t("Js.Error.Generic"), "error");
                formModal.hide();
                return;
            }
            content.innerHTML = await response.text();
            wireForm(content, options);
        } catch {
            toast(t("Js.Error.Network"), "error");
            formModal.hide();
        } finally {
            loading(false);
        }
    }

    /** Scripts inserted with innerHTML do not run: re-create them so partial views can include small scripts. */
    function runScripts(container) {
        container.querySelectorAll("script").forEach((old) => {
            const s = document.createElement("script");
            s.textContent = old.textContent;
            old.replaceWith(s);
        });
    }

    function wireForm(content, options) {
        initDynamic(content);
        runScripts(content);
        const form = content.querySelector("form");
        if (!form) return;
        const firstInput = form.querySelector("input:not([type=hidden]):not([disabled]), textarea, select:not(.tomselected)");
        if (firstInput) setTimeout(() => firstInput.focus(), 200);

        form.addEventListener("submit", async (e) => {
            e.preventDefault();
            if (window.jQuery && jQuery(form).valid && !jQuery(form).valid()) return;
            const submitBtn = form.querySelector("[type=submit]");
            if (submitBtn) submitBtn.disabled = true;
            loading(true);
            try {
                const response = await fetch(form.action, {
                    method: "POST",
                    body: new FormData(form),
                    headers: { "X-Requested-With": "XMLHttpRequest", "RequestVerificationToken": cfg.antiforgery },
                    credentials: "same-origin"
                });
                const contentType = response.headers.get("content-type") || "";
                if (contentType.includes("application/json")) {
                    const data = await response.json();
                    if (response.ok && data.ok) {
                        formModal.hide();
                        toast(data.message || t("Js.Saved"), "success");
                        if (data.data && data.data.warning) toast(data.data.warning, "warning", 9000);
                        afterSuccess(options, data);
                    } else {
                        toast(data.message || t("Js.Error.Generic"), "error");
                    }
                } else {
                    // 400 + HTML = the same form with validation messages
                    content.innerHTML = await response.text();
                    wireForm(content, options);
                    const firstError = content.querySelector(".field-validation-error, .alert-glass:not(:empty)");
                    if (firstError) firstError.scrollIntoView({ block: "center", behavior: "smooth" });
                }
            } catch {
                toast(t("Js.Error.Network"), "error");
            } finally {
                loading(false);
                if (submitBtn) submitBtn.disabled = false;
            }
        });
    }

    function afterSuccess(options, data) {
        if (typeof options.onSaved === "function") options.onSaved(data);
        if (options.reloadGrid && window.CrmGrid) {
            let found = false;
            options.reloadGrid.split(",").forEach((id) => { const g = CrmGrid.get(id.trim()); if (g) { g.reload(); found = true; } });
            if (!found) CrmGrid.reloadAll();
        }
        if (options.reload === "page") setTimeout(() => window.location.reload(), 400);
        if (data && data.data && data.data.redirect) setTimeout(() => { window.location.href = data.data.redirect; }, 400);
        document.dispatchEvent(new CustomEvent("crm:saved", { detail: data }));
    }

    // ------------------------------------------------------------------
    // Dynamic enhancement of new HTML (forms, filters)
    // ------------------------------------------------------------------
    function tomRender(item, escape) {
        const code = item.code ? `<span class="ts-code">${escape(item.code)}</span>` : "";
        const extra = item.extra ? `<div class="ts-extra">${escape(item.extra)}</div>` : "";
        return `<div><span>${escape(item.name)}</span>${code}${extra}</div>`;
    }

    function initRemoteSelect(select) {
        if (select.tomselect || !window.TomSelect) return;
        const url = select.getAttribute("data-remote");
        const dependsOn = select.getAttribute("data-depends-on");
        const form = select.closest("form") || document;
        const parentField = () => dependsOn ? form.querySelector(`[name="${dependsOn}"]`) : null;
        const extraParams = () => {
            const p = {};
            const parent = parentField();
            if (parent) p[select.getAttribute("data-depends-param") || (dependsOn.charAt(0).toLowerCase() + dependsOn.slice(1))] = parent.value;
            return p;
        };

        // data-create-field="NewClientName": the user may type a NEW name. The select then gets
        // value "0" and the typed name goes into that hidden field; the server creates the record.
        const createField = select.getAttribute("data-create-field");
        const createHidden = createField ? form.querySelector(`[name="${createField}"]`) : null;
        const newFields = form.querySelector(`[data-new-fields-for="${select.name}"]`);
        const syncNew = () => {
            const isNew = select.value === "0";
            if (createHidden && !isNew) createHidden.value = "";
            if (newFields) {
                newFields.hidden = !isNew;
                newFields.querySelectorAll("input, select, textarea").forEach((el) => { el.disabled = !isNew; });
            }
        };

        const ts = new TomSelect(select, {
            // inside a popup the list opens above the dialog instead of being clipped by its scroll area
            dropdownParent: select.closest(".modal") ? "body" : null,
            valueField: "id",
            labelField: "name",
            searchField: ["name", "code", "extra"],
            maxOptions: 50,
            preload: "focus",
            loadThrottle: 300,
            allowEmptyOption: true,
            placeholder: select.getAttribute("data-placeholder") || t("Common.Select"),
            plugins: select.hasAttribute("required") || select.hasAttribute("data-no-clear") ? [] : ["clear_button"],
            create: createHidden ? (input) => {
                const name = input.trim();
                createHidden.value = name;
                if (ts.options["0"]) ts.removeOption("0", true);
                return { id: "0", name, isNew: true };
            } : false,
            createFilter: (input) => input.trim().length >= 2,
            persist: false,
            render: {
                option: tomRender,
                item: (item, escape) => `<div>${escape(item.name)}${String(item.id) === "0" ? ` <span class="badge-soft indigo">${escapeHtml(t("Common.New"))}</span>` : ""}</div>`,
                option_create: (data, escape) => `<div class="create"><i class="f7-icons sm" aria-hidden="true">plus_circle</i> ${escape(t("Common.AddNew", data.input))}</div>`,
                no_results: () => `<div class="no-results">${escapeHtml(t("Common.NoResults"))}</div>`,
                loading: () => `<div class="spinner spinner-sm"></div>`
            },
            shouldLoad: () => {
                const parent = parentField();
                return !dependsOn || (parent && parent.value);
            },
            load: (query, callback) => {
                get(url, Object.assign({ search: query, take: 50 }, extraParams()), { showLoading: false, silent: true })
                    .then((items) => callback(items))
                    .catch(() => callback());
            }
        });

        select.addEventListener("change", syncNew);
        syncNew();

        const parent = parentField();
        if (parent) {
            const reset = () => {
                ts.clear(true);
                ts.clearOptions();
                if (createHidden) createHidden.value = "";
                syncNew();
                ts.settings.preload && ts.load("");
                if (parent.value) ts.enable(); else ts.disable();
                select.dispatchEvent(new Event("change", { bubbles: true }));
            };
            parent.addEventListener("change", reset);
            if (!parent.value) ts.disable();
        }
    }

    function initDynamic(root) {
        root = root || document;
        // Free text follows what is typed: English notes left-to-right, Arabic notes right-to-left
        root.querySelectorAll("textarea:not([dir]), input[type=text]:not([dir]), input:not([type]):not([dir])").forEach((el) => el.setAttribute("dir", "auto"));
        root.querySelectorAll("select[data-remote]").forEach(initRemoteSelect);
        root.querySelectorAll("select[data-enhance]").forEach((select) => {
            if (select.tomselect || !window.TomSelect) return;
            new TomSelect(select, {
                dropdownParent: select.closest(".modal") ? "body" : null,
                allowEmptyOption: true,
                plugins: select.multiple ? ["remove_button"] : [],
                placeholder: select.getAttribute("data-placeholder") || "",
                render: { no_results: () => `<div class="no-results">${escapeHtml(t("Common.NoResults"))}</div>` }
            });
        });

        // Show/hide sections linked to a checkbox
        root.querySelectorAll("[data-show-when]").forEach((section) => {
            const form = section.closest("form") || document;
            const checkbox = form.querySelector(`[name="${section.getAttribute("data-show-when")}"][type=checkbox]`);
            if (!checkbox) return;
            const sync = () => {
                section.hidden = !checkbox.checked;
                section.querySelectorAll("input, select, textarea").forEach((el) => { el.disabled = !checkbox.checked; });
                section.querySelectorAll("select.tomselected").forEach((el) => el.tomselect && (checkbox.checked ? el.tomselect.enable() : el.tomselect.disable()));
            };
            checkbox.addEventListener("change", sync);
            sync();
        });

        // Client-side validation for injected forms
        if (window.jQuery && jQuery.validator && jQuery.validator.unobtrusive) {
            root.querySelectorAll("form").forEach((form) => {
                jQuery(form).removeData("validator").removeData("unobtrusiveValidation");
                jQuery.validator.unobtrusive.parse(form);
            });
        }
    }

    // ------------------------------------------------------------------
    // Declarative actions: data-open-form / data-delete / data-post
    // ------------------------------------------------------------------
    function initActions() {
        document.addEventListener("click", async (e) => {
            const opener = e.target.closest("[data-open-form]");
            if (opener) {
                e.preventDefault();
                openForm(opener.getAttribute("data-open-form") || opener.getAttribute("href"), {
                    reloadGrid: opener.getAttribute("data-reload-grid"),
                    reload: opener.getAttribute("data-reload"),
                    size: opener.getAttribute("data-size")
                });
                return;
            }

            const action = e.target.closest("[data-delete], [data-post]");
            if (action) {
                e.preventDefault();
                const isDelete = action.hasAttribute("data-delete");
                const url = action.getAttribute(isDelete ? "data-delete" : "data-post");
                const message = action.getAttribute("data-confirm-message") || (isDelete ? t("Js.Confirm.Delete") : null);
                if (message && !(await confirmDialog({ message, danger: isDelete || action.hasAttribute("data-danger"), confirmText: action.getAttribute("data-confirm-text") || undefined }))) return;
                try {
                    const data = await post(url, {});
                    toast((data && data.message) || t("Js.Saved"), "success");
                    afterSuccess({ reloadGrid: action.getAttribute("data-reload-grid"), reload: action.getAttribute("data-reload") }, data);
                } catch { /* toast already shown */ }
            }
        });

        // Generic confirmation for normal forms marked data-confirm
        document.addEventListener("submit", async (e) => {
            const form = e.target;
            const message = form.getAttribute("data-confirm");
            if (!message || form.dataset.confirmed === "1") return;
            e.preventDefault();
            if (await confirmDialog({ message })) {
                form.dataset.confirmed = "1";
                form.requestSubmit();
            }
        });
    }

    // ------------------------------------------------------------------
    // Theme
    // ------------------------------------------------------------------
    const THEME_KEY = "crm-theme";
    function storedTheme() { try { return localStorage.getItem(THEME_KEY) || "auto"; } catch { return "auto"; } }
    function effectiveTheme(pref) {
        if (pref === "light" || pref === "dark") return pref;
        return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
    }
    function applyTheme(pref) {
        const root = document.documentElement;
        if (pref === "light" || pref === "dark") root.setAttribute("data-theme", pref);
        else root.removeAttribute("data-theme");
        document.querySelectorAll("[data-theme-option]").forEach((el) =>
            el.setAttribute("aria-checked", el.getAttribute("data-theme-option") === pref ? "true" : "false"));
        const effective = effectiveTheme(pref);
        themeListeners.forEach((fn) => { try { fn(effective); } catch (e) { console.error(e); } });
    }
    function setTheme(pref) {
        try { localStorage.setItem(THEME_KEY, pref); } catch { /* private mode */ }
        applyTheme(pref);
    }
    const onThemeChange = (fn) => themeListeners.push(fn);

    // ------------------------------------------------------------------
    // Popovers & sidebar
    // ------------------------------------------------------------------
    function initPopovers() {
        const closeAll = (except) => {
            document.querySelectorAll(".glass-popover.open").forEach((p) => {
                if (p === except) return;
                p.classList.remove("open");
                const trig = document.querySelector(`[data-popover-target="#${p.id}"]`);
                if (trig) trig.setAttribute("aria-expanded", "false");
            });
        };
        document.querySelectorAll("[data-popover-target]").forEach((trigger) => {
            const pop = document.querySelector(trigger.getAttribute("data-popover-target"));
            if (!pop) return;
            trigger.setAttribute("aria-expanded", "false");
            trigger.addEventListener("click", (e) => {
                e.stopPropagation();
                const willOpen = !pop.classList.contains("open");
                closeAll(pop);
                pop.classList.toggle("open", willOpen);
                trigger.setAttribute("aria-expanded", willOpen ? "true" : "false");
                if (willOpen) pop.dispatchEvent(new CustomEvent("popover:open"));
            });
            pop.addEventListener("click", (e) => e.stopPropagation());
        });
        document.addEventListener("click", () => closeAll(null));
        document.addEventListener("keydown", (e) => { if (e.key === "Escape") closeAll(null); });
    }

    function initSidebar() {
        const body = document.body;
        document.querySelectorAll("[data-sidebar-toggle]").forEach((btn) =>
            btn.addEventListener("click", () => body.classList.toggle("sidebar-open")));
        document.querySelector(".sidebar-scrim")?.addEventListener("click", () => body.classList.remove("sidebar-open"));
        document.addEventListener("keydown", (e) => { if (e.key === "Escape") body.classList.remove("sidebar-open"); });
    }

    // ------------------------------------------------------------------
    // Notifications: bell (in-app) + browser notifications
    // ------------------------------------------------------------------
    const notifIcons = { 1: "bell_fill", 2: "exclamationmark_circle_fill", 3: "person_crop_circle_badge_plus", 4: "clock_fill", 5: "doc_text_fill", 6: "hammer_fill", 7: "tray_arrow_down_fill", 8: "info_circle_fill" };
    const LAST_SEEN_KEY = "crm-last-browser-notification";

    function notificationItemHtml(n) {
        return `
            <div class="notif-item ${n.isRead ? "" : "unread"}" data-id="${n.id}">
                <div class="notif-icon"><i class="f7-icons sm" aria-hidden="true">${notifIcons[n.type] || "bell_fill"}</i></div>
                <div class="flex-grow-1 min-w-0">
                    <div class="notif-title">${escapeHtml(n.title)}</div>
                    ${n.companyName ? `<div class="notif-text"><strong>${escapeHtml(n.companyName)}</strong></div>` : ""}
                    <div class="notif-text">${escapeHtml(n.message)}</div>
                    <div class="notif-meta">
                        <span>${escapeHtml(timeAgo(n.createdAtUtc))}</span>
                        ${n.eventAtLocal ? `<span>${escapeHtml(t("Js.Notifications.Scheduled"))}: ${escapeHtml(formatDateTime(n.eventAtLocal))}</span>` : ""}
                        ${n.url ? `<a href="${escapeHtml(n.url)}" data-open-notif>${escapeHtml(t("Js.Notifications.Open"))}</a>` : ""}
                        ${n.isRead ? "" : `<button type="button" data-mark-read>${escapeHtml(t("Js.Notifications.MarkRead"))}</button>`}
                    </div>
                </div>
            </div>`;
    }

    function renderBell(data) {
        const badge = document.getElementById("bellBadge");
        const list = document.getElementById("bellList");
        if (!badge || !list) return;
        const unread = data.unreadCount || 0;
        badge.textContent = unread > 99 ? "99+" : String(unread);
        badge.hidden = unread === 0;
        document.getElementById("bellButton")?.setAttribute("aria-label", `${t("Top.Notifications")}${unread ? " (" + unread + ")" : ""}`);
        list.innerHTML = (!data.items || data.items.length === 0)
            ? `<div class="popover-empty"><i class="f7-icons" aria-hidden="true">bell_slash</i>${escapeHtml(t("Top.NoNotifications"))}</div>`
            : data.items.map(notificationItemHtml).join("");
        showBrowserNotifications(data.items || []);
    }

    /** Desktop notifications for new unread items (only when the user allowed it). */
    function showBrowserNotifications(items) {
        if (!cfg.browserNotifications || !("Notification" in window)) return;
        let lastSeen = 0;
        try { lastSeen = Number(localStorage.getItem(LAST_SEEN_KEY) || 0); } catch { /* ignore */ }
        const maxId = items.reduce((m, n) => Math.max(m, n.id), lastSeen);
        if (!lastSeen) { try { localStorage.setItem(LAST_SEEN_KEY, String(maxId)); } catch { } return; } // first visit: no flood
        const fresh = items.filter((n) => !n.isRead && n.id > lastSeen);
        try { localStorage.setItem(LAST_SEEN_KEY, String(maxId)); } catch { /* ignore */ }
        if (!fresh.length || Notification.permission !== "granted") return;

        fresh.slice(0, 3).forEach((n) => {
            const note = new Notification(n.title, { body: (n.companyName ? n.companyName + "\n" : "") + n.message, tag: "crm-" + n.id, lang: cfg.lang });
            note.onclick = () => { window.focus(); if (n.url) window.location.href = n.url; note.close(); };
        });
        post(cfg.urls.browserShown, { ids: fresh.map((n) => n.id) }, { silent: true, showLoading: false }).catch(() => { });
    }

    async function refreshBell() {
        if (!cfg.urls || !cfg.urls.bell) return;
        try {
            const data = await get(cfg.urls.bell, null, { silent: true, showLoading: false });
            if (data) renderBell(data);
            document.dispatchEvent(new CustomEvent("crm:bell", { detail: data }));
        } catch { /* next poll retries */ }
    }

    async function markRead(id) {
        await post(cfg.urls.markRead + "/" + id, {}, { showLoading: false });
        refreshBell();
    }

    function initBell() {
        const list = document.getElementById("bellList");
        if (!list) return;
        document.addEventListener("click", async (e) => {
            const item = e.target.closest(".notif-item");
            if (!item) return;
            const id = item.getAttribute("data-id");
            if (e.target.closest("[data-mark-read]")) {
                await markRead(id);
                document.dispatchEvent(new CustomEvent("crm:notification-read", { detail: { id } }));
            } else if (e.target.closest("[data-open-notif]") && item.classList.contains("unread")) {
                post(cfg.urls.markRead + "/" + id, {}, { silent: true, showLoading: false });
            }
        });
        document.getElementById("markAllRead")?.addEventListener("click", async () => {
            await post(cfg.urls.markAllRead, {}, { showLoading: false });
            refreshBell();
            document.dispatchEvent(new CustomEvent("crm:notification-read", { detail: { all: true } }));
        });
        document.getElementById("bellPopover")?.addEventListener("popover:open", refreshBell);

        refreshBell();
        const interval = Math.max(10, cfg.pollIntervalSeconds || 30) * 1000;
        setInterval(() => { if (!document.hidden) refreshBell(); }, interval);
        document.addEventListener("visibilitychange", () => { if (!document.hidden) refreshBell(); });
    }

    async function requestBrowserPermission() {
        if (!("Notification" in window)) { toast(t("Notif.BrowserUnsupported"), "warning"); return "unsupported"; }
        const result = await Notification.requestPermission();
        toast(result === "granted" ? t("Notif.BrowserEnabled") : t("Notif.BrowserDenied"), result === "granted" ? "success" : "warning");
        return result;
    }

    // ------------------------------------------------------------------
    // Chart.js defaults (when Chart.js is on the page)
    // ------------------------------------------------------------------
    function initChartDefaults() {
        if (!window.Chart) return;
        const apply = () => {
            Chart.defaults.font.family = getComputedStyle(document.body).fontFamily;
            Chart.defaults.font.size = 12;
            Chart.defaults.color = cssVar("--chart-text");
            Chart.defaults.borderColor = cssVar("--chart-grid");
            Chart.defaults.plugins.legend.rtl = !!cfg.rtl;
            Chart.defaults.plugins.tooltip.rtl = !!cfg.rtl;
            Chart.defaults.plugins.tooltip.backgroundColor = cssVar("--surface-popover") || "rgba(255,255,255,.98)";
            Chart.defaults.plugins.tooltip.titleColor = cssVar("--text");
            Chart.defaults.plugins.tooltip.bodyColor = cssVar("--text-2");
            Chart.defaults.plugins.tooltip.borderColor = cssVar("--separator-strong");
            Chart.defaults.plugins.tooltip.borderWidth = 1;
            Chart.defaults.plugins.tooltip.padding = 10;
            Chart.defaults.plugins.tooltip.cornerRadius = 10;
            Chart.defaults.plugins.tooltip.boxPadding = 4;
            Chart.defaults.plugins.legend.labels.usePointStyle = true;
            Chart.defaults.plugins.legend.labels.boxWidth = 8;
            Chart.defaults.plugins.legend.labels.boxHeight = 8;
            Chart.defaults.animation.duration = window.matchMedia("(prefers-reduced-motion: reduce)").matches ? 0 : 400;
        };
        apply();
        onThemeChange(apply);
    }

    // ------------------------------------------------------------------
    // Boot
    // ------------------------------------------------------------------
    document.addEventListener("DOMContentLoaded", () => {
        if (window.jQuery && jQuery.validator) {
            jQuery.validator.setDefaults({ ignore: "[type=hidden], :hidden:not(select.tomselected), [disabled]" });
        }
        initChartDefaults();
        initSidebar();
        initPopovers();
        initActions();
        initDynamic(document);

        document.querySelectorAll("[data-theme-option]").forEach((el) =>
            el.addEventListener("click", () => setTheme(el.getAttribute("data-theme-option"))));
        applyTheme(storedTheme());
        window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", () => { if (storedTheme() === "auto") applyTheme("auto"); });

        initBell();
        (cfg.messages || []).forEach((m) => toast(m.text, m.type));
    });

    window.App = {
        config: cfg, t, escapeHtml, toast, confirm: confirmDialog, loading,
        get, post, openForm, initDynamic, cssVar, url,
        formatNumber, formatMoney, formatDate, formatTime, formatDateTime, timeAgo,
        onThemeChange, setTheme, effectiveTheme: () => effectiveTheme(storedTheme()),
        refreshBell, markRead, notificationItemHtml, requestBrowserPermission
    };
})();
