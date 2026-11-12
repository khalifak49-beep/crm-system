/* =====================================================================
   Dashboard page
   File: wwwroot/js/dashboard.js
   - Filters (period presets, dates, client, employee) kept in the URL
   - Loads /Dashboard/Data (all numbers from SQL Server)
   - Chart.js charts using the validated categorical palette (--series-*)
   - Every chart can switch to a data table (accessibility / exact values)
   - DevExtreme DataGrid with server-side paging for open follow-ups
   ===================================================================== */
(function () {
    "use strict";

    const cfg = window.DashboardConfig;
    const t = App.t;
    const rtl = !!App.config.rtl;
    const intlLocale = App.config.lang === "ar" ? "ar-OM-u-nu-latn" : "en-GB";

    // ------------------------------------------------------------------
    // State (initialised from the URL so refresh / share keeps filters)
    // ------------------------------------------------------------------
    const query = new URLSearchParams(window.location.search);
    const state = {
        from: query.get("from") || "",
        to: query.get("to") || "",
        clientId: query.get("clientId") ? Number(query.get("clientId")) : null,
        userId: query.get("userId") || null,
        granularity: query.get("granularity") || "auto",
        preset: query.get("from") || query.get("to") ? "custom" : (query.get("preset") || "thisYear")
    };

    const charts = {};
    let requestSeq = 0;
    let grid = null;
    let clientBox = null;
    let employeeBox = null;

    // ------------------------------------------------------------------
    // Date helpers (dates are calendar dates in Asia/Muscat, as strings)
    // ------------------------------------------------------------------
    function parseIso(iso) { const [y, m, d] = iso.split("-").map(Number); return new Date(Date.UTC(y, m - 1, d)); }
    function toIso(date) { return date.toISOString().substring(0, 10); }

    function presetRange(preset) {
        const today = parseIso(cfg.todayLocal);
        const y = today.getUTCFullYear(), m = today.getUTCMonth();
        switch (preset) {
            case "thisMonth": return [toIso(new Date(Date.UTC(y, m, 1))), cfg.todayLocal];
            case "last30": return [toIso(new Date(today.getTime() - 29 * 86400000)), cfg.todayLocal];
            case "lastYear": return [`${y - 1}-01-01`, `${y - 1}-12-31`];
            default: return [`${y}-01-01`, cfg.todayLocal];
        }
    }

    function bucketLabel(key, granularity) {
        if (granularity === "year") return key;
        if (granularity === "month") {
            const [y, m] = key.split("-").map(Number);
            return new Intl.DateTimeFormat(intlLocale, { month: "short", year: "numeric", timeZone: "UTC" }).format(new Date(Date.UTC(y, m - 1, 1)));
        }
        return new Intl.DateTimeFormat(intlLocale, { day: "2-digit", month: "short", timeZone: "UTC" }).format(parseIso(key));
    }

    // ------------------------------------------------------------------
    // Filters UI
    // ------------------------------------------------------------------
    const fromInput = document.getElementById("fromDate");
    const toInput = document.getElementById("toDate");

    function applyPresetToInputs() {
        if (state.preset !== "custom") {
            const [f, tDate] = presetRange(state.preset);
            state.from = f;
            state.to = tDate;
        }
        fromInput.value = state.from;
        toInput.value = state.to;
        document.querySelectorAll("#presets [data-preset]").forEach((b) =>
            b.setAttribute("aria-pressed", b.dataset.preset === state.preset ? "true" : "false"));
    }

    function syncUrl() {
        const p = new URLSearchParams();
        if (state.preset === "custom") { p.set("from", state.from); p.set("to", state.to); }
        else if (state.preset !== "thisYear") p.set("preset", state.preset);
        if (state.clientId) p.set("clientId", state.clientId);
        if (state.userId) p.set("userId", state.userId);
        if (state.granularity !== "auto") p.set("granularity", state.granularity);
        const qs = p.toString();
        history.replaceState(null, "", window.location.pathname + (qs ? "?" + qs : ""));
    }

    function initFilters() {
        applyPresetToInputs();

        document.querySelectorAll("#presets [data-preset]").forEach((btn) =>
            btn.addEventListener("click", () => {
                state.preset = btn.dataset.preset;
                state.granularity = "auto";
                applyPresetToInputs();
                refresh();
            }));

        const onDateChange = () => {
            if (!fromInput.value || !toInput.value) return;
            state.preset = "custom";
            state.from = fromInput.value;
            state.to = toInput.value;
            state.granularity = "auto";
            applyPresetToInputs();
            refresh();
        };
        fromInput.addEventListener("change", onDateChange);
        toInput.addEventListener("change", onDateChange);

        document.querySelectorAll("#granularity [data-granularity]").forEach((btn) =>
            btn.addEventListener("click", () => {
                state.granularity = btn.dataset.granularity;
                loadData();
                syncUrl();
            }));

        // Client / employee: Tom Select (remote search) created by App.initDynamic from data-remote
        clientBox = document.getElementById("clientFilter");
        employeeBox = document.getElementById("employeeFilter");
        App.initDynamic(document.querySelector(".filters"));
        if (state.clientId && clientBox.tomselect) {
            App.get(App.url("Lookup/Clients"), { id: state.clientId }, { showLoading: false }).then((items) => {
                if (items[0]) { clientBox.tomselect.addOption(items[0]); clientBox.tomselect.setValue(String(items[0].id), true); }
            });
        }
        if (state.userId && employeeBox && employeeBox.tomselect) {
            App.get(App.url("Lookup/Employees"), { id: state.userId }, { showLoading: false }).then((items) => {
                if (items[0]) { employeeBox.tomselect.addOption(items[0]); employeeBox.tomselect.setValue(items[0].id, true); }
            });
        }
        clientBox.addEventListener("change", () => { state.clientId = clientBox.value ? Number(clientBox.value) : null; refresh(); });
        if (employeeBox) employeeBox.addEventListener("change", () => { state.userId = employeeBox.value || null; refresh(); });
        document.getElementById("resetFilters").addEventListener("click", () => {
            state.preset = "thisYear";
            state.granularity = "auto";
            state.clientId = null;
            state.userId = null;
            applyPresetToInputs();
            if (clientBox.tomselect) clientBox.tomselect.clear(true);
            if (employeeBox && employeeBox.tomselect) employeeBox.tomselect.clear(true);
            refresh();
        });
    }

    // ------------------------------------------------------------------
    // Data loading
    // ------------------------------------------------------------------
    function refresh() {
        syncUrl();
        loadData();
        reloadGrid();
    }

    async function loadData() {
        const seq = ++requestSeq;
        document.querySelectorAll("[data-stat]").forEach((el) => el.classList.add("loading"));
        try {
            const data = await App.get(cfg.urls.data, {
                from: state.from, to: state.to,
                clientId: state.clientId, userId: state.userId,
                granularity: state.granularity
            });
            if (seq !== requestSeq) return;      // a newer request is in flight
            renderStats(data.stats);
            renderCharts(data);
            document.querySelectorAll("#granularity [data-granularity]").forEach((b) =>
                b.setAttribute("aria-pressed", b.dataset.granularity === data.range.granularity ? "true" : "false"));
            const at = new Date(data.generatedAtLocal);
            document.getElementById("updatedAt").textContent =
                t("Dashboard.UpdatedAt", new Intl.DateTimeFormat(intlLocale, { hour: "2-digit", minute: "2-digit", hour12: true }).format(at));
        } catch (err) {
            console.error(err);
        } finally {
            if (seq === requestSeq) document.querySelectorAll("[data-stat]").forEach((el) => el.classList.remove("loading"));
        }
    }

    function renderStats(stats) {
        Object.entries(stats).forEach(([key, value]) => {
            const el = document.querySelector(`[data-stat="${key}"]`);
            if (el) el.textContent = App.formatNumber(value);
        });
        const overdueTile = document.querySelector('[data-alert-when="overdueFollowUps"]');
        if (overdueTile) overdueTile.classList.toggle("alert-red", stats.overdueFollowUps > 0);
    }

    // ------------------------------------------------------------------
    // Charts
    // ------------------------------------------------------------------
    const series = (n) => App.cssVar(`--series-${n}`);

    function withAlpha(color, alpha) {
        const hex = color.replace("#", "");
        if (hex.length !== 6) return color;
        const r = parseInt(hex.substring(0, 2), 16), g = parseInt(hex.substring(2, 4), 16), b = parseInt(hex.substring(4, 6), 16);
        return `rgba(${r}, ${g}, ${b}, ${alpha})`;
    }

    function baseScales(horizontal) {
        const grid = { color: App.cssVar("--chart-grid"), drawTicks: false };
        const valueAxis = { beginAtZero: true, ticks: { precision: 0, padding: 6 }, grid, border: { display: false } };
        const categoryAxis = { grid: { display: false }, ticks: { padding: 6, autoSkip: true, maxRotation: 0 }, border: { display: false } };
        if (horizontal) {
            return {
                x: { ...valueAxis, reverse: rtl },
                y: { ...categoryAxis, position: rtl ? "right" : "left", ticks: { ...categoryAxis.ticks, autoSkip: false } }
            };
        }
        return { x: categoryAxis, y: { ...valueAxis, position: rtl ? "right" : "left" } };
    }

    function upsertChart(key, canvasId, config) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;
        if (charts[key]) {
            charts[key].data = config.data;
            charts[key].options = config.options;
            charts[key].update();
        } else {
            charts[key] = new Chart(canvas, config);
        }
        const total = config.data.datasets.reduce((sum, ds) => sum + ds.data.reduce((a, b) => a + (Number(b) || 0), 0), 0);
        canvas.parentElement.querySelector(".chart-empty").hidden = total > 0;
        const card = canvas.closest("[data-chart-card]");
        if (card && !card.querySelector("[data-table]").hidden) renderTable(card, charts[key]);
    }

    function lineDataset(label, data, color, fill) {
        return {
            label, data,
            borderColor: color,
            backgroundColor: fill ? withAlpha(color, 0.12) : color,
            fill: !!fill,
            borderWidth: 2,
            tension: 0.3,
            cubicInterpolationMode: "monotone",   // smooth but never overshoots below 0
            pointRadius: data.length <= 1 ? 4 : 0,
            pointHoverRadius: 5,
            pointHoverBorderWidth: 2,
            pointHoverBorderColor: App.cssVar("--surface-solid"),
            pointBackgroundColor: color
        };
    }

    function barDataset(label, data, color) {
        return {
            label, data,
            backgroundColor: color,
            hoverBackgroundColor: withAlpha(color, 0.85),
            borderRadius: 4,
            borderSkipped: "start",       // round only the data end, flat at the baseline
            maxBarThickness: 18,
            categoryPercentage: 0.8,
            barPercentage: 0.9
        };
    }

    function renderCharts(data) {
        const gran = data.range.granularity;
        const labels = data.timeline.labels.map((k) => bucketLabel(k, gran));
        const lineOptions = (legend) => ({
            maintainAspectRatio: false,
            interaction: { mode: "index", intersect: false },
            plugins: { legend: { display: legend, position: "top", align: "start" } },
            scales: baseScales(false)
        });

        upsertChart("timeline", "chartTimeline", {
            type: "line",
            data: { labels, datasets: [lineDataset(t("Js.Activities"), data.timeline.values, series(1), true)] },
            options: lineOptions(false)
        });

        upsertChart("comparison", "chartComparison", {
            type: "line",
            data: {
                labels,
                datasets: [
                    lineDataset(t("Js.Calls"), data.comparison.calls, series(1), false),
                    lineDataset(t("Js.Emails"), data.comparison.emails, series(2), false),
                    lineDataset(t("Js.Visits"), data.comparison.visits, series(3), false)
                ]
            },
            options: lineOptions(true)
        });

        const barOptions = {
            indexAxis: "y",
            maintainAspectRatio: false,
            plugins: { legend: { display: false } },
            scales: baseScales(true)
        };

        upsertChart("topClients", "chartTopClients", {
            type: "bar",
            data: {
                labels: data.topClients.map((c) => c.label),
                datasets: [barDataset(t("Js.Activities"), data.topClients.map((c) => c.value), series(1))]
            },
            options: barOptions
        });

        upsertChart("projects", "chartProjects", {
            type: "bar",
            data: {
                labels: data.projectsByStatus.map((p) => t("ProjectStatus." + p.status)),
                datasets: [barDataset(t("Js.Count"), data.projectsByStatus.map((p) => p.count), series(1))]
            },
            options: barOptions
        });

        if (data.employeePerformance) {
            upsertChart("employees", "chartEmployees", {
                type: "bar",
                data: {
                    labels: data.employeePerformance.map((e) => e.label),
                    datasets: [barDataset(t("Js.Activities"), data.employeePerformance.map((e) => e.value), series(1))]
                },
                options: barOptions
            });
        }
    }

    /** Re-color charts after a light/dark switch (colors come from CSS tokens). */
    function recolorCharts() {
        Object.values(charts).forEach((chart) => {
            chart.data.datasets.forEach((ds, i) => {
                const color = series(i + 1);
                if (chart.config.type === "line") {
                    ds.borderColor = color;
                    ds.pointBackgroundColor = color;
                    ds.backgroundColor = ds.fill ? withAlpha(color, 0.12) : color;
                    ds.pointHoverBorderColor = App.cssVar("--surface-solid");
                } else {
                    ds.backgroundColor = color;
                    ds.hoverBackgroundColor = withAlpha(color, 0.85);
                }
            });
            const horizontal = chart.options.indexAxis === "y";
            chart.options.scales = baseScales(horizontal);
            chart.update("none");
        });
    }

    // ------------------------------------------------------------------
    // Chart <-> table toggle
    // ------------------------------------------------------------------
    function renderTable(card, chart) {
        const container = card.querySelector("[data-table]");
        const datasets = chart.data.datasets;
        const head = `<tr><th>${App.escapeHtml(chart.options.indexAxis === "y" ? t("Js.Name") : t("Js.Period"))}</th>${datasets.map((d) => `<th class="num">${App.escapeHtml(d.label)}</th>`).join("")}</tr>`;
        const body = chart.data.labels.map((label, i) =>
            `<tr><td>${App.escapeHtml(label)}</td>${datasets.map((d) => `<td class="num">${App.formatNumber(d.data[i])}</td>`).join("")}</tr>`).join("");
        container.innerHTML = `<table class="data-table"><thead>${head}</thead><tbody>${body}</tbody></table>`;
    }

    function initTableToggles() {
        document.querySelectorAll("[data-chart-card]").forEach((card) => {
            const btn = card.querySelector("[data-toggle-table]");
            const box = card.querySelector(".chart-box");
            const table = card.querySelector("[data-table]");
            btn.addEventListener("click", () => {
                const showTable = table.hidden;
                const chart = charts[card.dataset.chartCard];
                if (showTable && chart) renderTable(card, chart);
                table.hidden = !showTable;
                box.hidden = showTable;
                const label = showTable ? t("Js.ShowChart") : t("Js.ShowTable");
                btn.setAttribute("aria-label", label);
                btn.title = label;
                btn.querySelector(".f7-icons").textContent = showTable ? "chart_bar" : "table";
            });
        });
    }

    // ------------------------------------------------------------------
    // Open follow-ups grid (shared definition from columns.js)
    // ------------------------------------------------------------------
    function reloadGrid() {
        if (grid) grid.reload(true);
    }

    function initGrid() {
        grid = Cols.followUpsGrid("#followUpsGrid", {
            pageSize: 10,
            params: () => ({ tab: "open", clientId: state.clientId || "", userId: state.userId || "" })
        });
    }
    // ------------------------------------------------------------------
    // Boot
    // ------------------------------------------------------------------
    document.addEventListener("DOMContentLoaded", () => {
        initFilters();
        initTableToggles();
        initGrid();
        App.onThemeChange(recolorCharts);
        loadData();
    });
})();
