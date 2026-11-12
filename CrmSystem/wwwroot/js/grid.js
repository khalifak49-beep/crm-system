/* =====================================================================
   CrmGrid - lightweight server-side data grid (no external dependency)
   File: wwwroot/js/grid.js
   Server contract (see Infrastructure/Grid/GridRequest.cs):
     GET url?page=1&pageSize=20&sort=field&dir=asc&search=text&...params
     -> { items: [...], total: 123, page: 1, pageSize: 20 }
   Usage:
     const grid = new CrmGrid("#clientsGrid", {
       url: "/Clients/List",
       columns: [
         { key: "companyName", title: "Company", sortable: true, render: (row) => "..." },
         { key: "status", title: "Status", width: "110px", align: "center" },
         { key: "actions", title: "", render: (row) => "<button ...>" }
       ],
       sort: "companyName", dir: "asc",
       params: () => ({ status: statusSelect.value }),   // extra filters
       rowUrl: (row) => "/Clients/Details/" + row.clientId // optional click-through
     });
     grid.reload();
   On phones (< 768px) rows are shown as cards (labels come from column titles).
   ===================================================================== */
(function () {
    "use strict";

    const registry = new Map();

    class CrmGrid {
        constructor(target, options) {
            this.el = typeof target === "string" ? document.querySelector(target) : target;
            if (!this.el) throw new Error("CrmGrid: element not found " + target);
            this.opt = Object.assign({
                pageSize: 20,
                pageSizes: [10, 20, 50, 100],
                searchable: true,
                searchPlaceholder: App.t("Grid.Search"),
                emptyText: App.t("Grid.Empty"),
                params: () => ({}),
                dir: "asc"
            }, options);
            this.state = { page: 1, pageSize: this.opt.pageSize, sort: this.opt.sort || "", dir: this.opt.dir, search: "" };
            this.seq = 0;
            this.lastResult = null;
            this.build();
            if (this.el.id) registry.set(this.el.id, this);
            if (this.opt.autoLoad !== false) this.load();
        }

        static get(id) { return registry.get(id); }

        /** Reload every grid on the page (used after a save when the target grid is unknown). */
        static reloadAll() { registry.forEach((g) => { if (document.body.contains(g.el)) g.reload(); }); }

        build() {
            const searchId = (this.el.id || "grid") + "Search";
            this.el.classList.add("crm-grid");
            this.el.innerHTML = `
                ${this.opt.searchable ? `
                <div class="grid-toolbar">
                    <label class="visually-hidden" for="${searchId}">${App.escapeHtml(this.opt.searchPlaceholder)}</label>
                    <div class="grid-search">
                        <i class="f7-icons sm" aria-hidden="true">search</i>
                        <input type="search" id="${searchId}" class="form-control" placeholder="${App.escapeHtml(this.opt.searchPlaceholder)}" autocomplete="off" />
                    </div>
                    <div class="grid-toolbar-extra"></div>
                </div>` : ""}
                <div class="grid-body">
                    <table class="grid-table">
                        <thead><tr>${this.opt.columns.map((c) => this.headerCell(c)).join("")}</tr></thead>
                        <tbody></tbody>
                    </table>
                    <div class="loading-veil" hidden><div class="spinner"></div></div>
                </div>
                <div class="grid-footer">
                    <span class="grid-info" aria-live="polite"></span>
                    <div class="grid-pager"></div>
                    <label class="grid-size">
                        <span class="visually-hidden">${App.escapeHtml(App.t("Grid.PageSize"))}</span>
                        <select class="form-select form-select-sm">${this.opt.pageSizes.map((s) => `<option value="${s}" ${s === this.state.pageSize ? "selected" : ""}>${s}</option>`).join("")}</select>
                    </label>
                </div>`;

            this.tbody = this.el.querySelector("tbody");
            this.veil = this.el.querySelector(".loading-veil");

            const search = this.el.querySelector(".grid-search input");
            if (search) {
                let timer;
                search.addEventListener("input", () => {
                    clearTimeout(timer);
                    timer = setTimeout(() => { this.state.search = search.value.trim(); this.state.page = 1; this.load(); }, 350);
                });
            }

            this.el.querySelectorAll("th[data-sort] button").forEach((btn) => {
                btn.addEventListener("click", () => {
                    const key = btn.closest("th").dataset.sort;
                    if (this.state.sort === key) this.state.dir = this.state.dir === "asc" ? "desc" : "asc";
                    else { this.state.sort = key; this.state.dir = "asc"; }
                    this.state.page = 1;
                    this.load();
                });
            });

            this.el.querySelector(".grid-size select").addEventListener("change", (e) => {
                this.state.pageSize = Number(e.target.value);
                this.state.page = 1;
                this.load();
            });

            this.el.querySelector(".grid-pager").addEventListener("click", (e) => {
                const btn = e.target.closest("button[data-page]");
                if (!btn || btn.disabled) return;
                this.state.page = Number(btn.dataset.page);
                this.load();
            });

            if (this.opt.rowUrl) {
                this.tbody.addEventListener("click", (e) => {
                    if (e.target.closest("a, button, input, select, label, .no-row-click")) return;
                    const tr = e.target.closest("tr[data-index]");
                    if (!tr || !this.lastResult) return;
                    const url = this.opt.rowUrl(this.lastResult.items[Number(tr.dataset.index)]);
                    if (url) window.location.href = url;
                });
            }
        }

        /** Container for page-specific controls placed next to the search box. */
        get toolbar() { return this.el.querySelector(".grid-toolbar-extra"); }

        headerCell(col) {
            const style = col.width ? ` style="width:${col.width}"` : "";
            const cls = [col.align ? "text-" + col.align : "", col.mobileHidden ? "mobile-hidden" : ""].join(" ").trim();
            if (!col.sortable) return `<th scope="col" class="${cls}"${style}>${App.escapeHtml(col.title || "")}</th>`;
            return `<th scope="col" class="${cls}" data-sort="${col.sortKey || col.key}"${style}>
                        <button type="button" class="sort-btn">${App.escapeHtml(col.title || "")}<i class="f7-icons sort-icon" aria-hidden="true"></i></button>
                    </th>`;
        }

        updateSortIndicators() {
            this.el.querySelectorAll("th[data-sort]").forEach((th) => {
                const active = th.dataset.sort === this.state.sort;
                th.setAttribute("aria-sort", active ? (this.state.dir === "asc" ? "ascending" : "descending") : "none");
                th.querySelector(".sort-icon").textContent = active ? (this.state.dir === "asc" ? "chevron_up" : "chevron_down") : "";
            });
        }

        async load() {
            const seq = ++this.seq;
            this.veil.hidden = false;
            try {
                const params = Object.assign({}, this.opt.params() || {}, {
                    page: this.state.page, pageSize: this.state.pageSize,
                    sort: this.state.sort, dir: this.state.dir, search: this.state.search
                });
                const result = await App.get(this.opt.url, params, { showLoading: false });
                if (seq !== this.seq) return;
                this.lastResult = result;
                this.state.page = result.page;
                this.renderRows(result);
                this.renderFooter(result);
                this.updateSortIndicators();
                if (this.opt.onLoaded) this.opt.onLoaded(result, this);
            } catch (err) {
                if (seq === this.seq) this.tbody.innerHTML = `<tr><td colspan="${this.opt.columns.length}" class="grid-empty">${App.escapeHtml(App.t("Js.Error.Generic"))}</td></tr>`;
            } finally {
                if (seq === this.seq) this.veil.hidden = true;
            }
        }

        reload(resetPage) {
            if (resetPage) this.state.page = 1;
            return this.load();
        }

        renderRows(result) {
            if (!result.items.length) {
                this.tbody.innerHTML = `<tr><td colspan="${this.opt.columns.length}" class="grid-empty">
                    <i class="f7-icons" aria-hidden="true">tray</i><div>${App.escapeHtml(this.opt.emptyText)}</div></td></tr>`;
                return;
            }
            this.tbody.innerHTML = result.items.map((row, i) => {
                const cells = this.opt.columns.map((col) => {
                    const raw = row[col.key];
                    const html = col.render ? col.render(row, raw) : App.escapeHtml(raw ?? "");
                    const cls = [col.align ? "text-" + col.align : "", col.className || "", col.mobileHidden ? "mobile-hidden" : ""].join(" ").trim();
                    return `<td class="${cls}" data-label="${App.escapeHtml(col.title || "")}">${html}</td>`;
                }).join("");
                const rowCls = (this.opt.rowClass ? this.opt.rowClass(row) : "") + (this.opt.rowUrl ? " clickable" : "");
                return `<tr data-index="${i}" class="${rowCls.trim()}">${cells}</tr>`;
            }).join("");
        }

        renderFooter(result) {
            const from = result.total === 0 ? 0 : (result.page - 1) * result.pageSize + 1;
            const to = Math.min(result.total, result.page * result.pageSize);
            this.el.querySelector(".grid-info").textContent = App.t("Grid.Info", App.formatNumber(from), App.formatNumber(to), App.formatNumber(result.total));

            const pages = Math.max(1, Math.ceil(result.total / result.pageSize));
            const current = result.page;
            const list = [];
            const add = (p) => { if (!list.includes(p) && p >= 1 && p <= pages) list.push(p); };
            add(1); for (let p = current - 1; p <= current + 1; p++) add(p); add(pages);
            list.sort((a, b) => a - b);

            const prevIcon = App.config.rtl ? "chevron_right" : "chevron_left";
            const nextIcon = App.config.rtl ? "chevron_left" : "chevron_right";
            let html = `<button type="button" class="page-btn" data-page="${current - 1}" ${current <= 1 ? "disabled" : ""} aria-label="${App.escapeHtml(App.t("Grid.Previous"))}"><i class="f7-icons sm">${prevIcon}</i></button>`;
            let last = 0;
            list.forEach((p) => {
                if (p - last > 1) html += `<span class="page-gap">…</span>`;
                html += `<button type="button" class="page-btn ${p === current ? "active" : ""}" data-page="${p}" ${p === current ? 'aria-current="page"' : ""}>${p}</button>`;
                last = p;
            });
            html += `<button type="button" class="page-btn" data-page="${current + 1}" ${current >= pages ? "disabled" : ""} aria-label="${App.escapeHtml(App.t("Grid.Next"))}"><i class="f7-icons sm">${nextIcon}</i></button>`;
            this.el.querySelector(".grid-pager").innerHTML = html;
        }
    }

    window.CrmGrid = CrmGrid;
})();
