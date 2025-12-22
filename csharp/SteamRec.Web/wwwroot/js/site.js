// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

const SteamRecDashboard = (() => {
    const hoverCards = [];

    const parseAxes = (raw) => {
        if (!raw) return [];
        try {
            return JSON.parse(raw);
        } catch {
            return [];
        }
    };

    const ensureChart = (canvas, axes) => {
        if (!canvas || !axes.length || typeof Chart === "undefined") return null;
        const ctx = canvas.getContext("2d");

        const labels = axes.map(a => a.name);
        const userData = axes.map(a => a.userScore);
        const gameData = axes.map(a => a.gameScore);

        return new Chart(ctx, {
            type: "radar",
            data: {
                labels: labels,
                datasets: [
                    {
                        label: "Jouw profiel",
                        data: userData,
                        backgroundColor: "rgba(102,192,244,0.2)",
                        borderColor: "#66c0f4",
                        pointBackgroundColor: "#66c0f4"
                    },
                    {
                        label: "Game",
                        data: gameData,
                        backgroundColor: "rgba(119,221,119,0.15)",
                        borderColor: "#77dd77",
                        pointBackgroundColor: "#77dd77"
                    }
                ]
            },
            options: {
                responsive: true,
                scales: {
                    r: {
                        beginAtZero: true,
                        suggestedMax: 1,
                        angleLines: { color: "rgba(255,255,255,0.1)" },
                        grid: { color: "rgba(255,255,255,0.2)" },
                        pointLabels: { color: "#c7d5e0", font: { size: 10 } },
                        ticks: { display: false }
                    }
                },
                plugins: {
                    legend: {
                        display: true,
                        labels: { color: "#c7d5e0" }
                    },
                    tooltip: {
                        callbacks: {
                            label: (ctx) => `${ctx.dataset.label}: ${(ctx.raw * 100).toFixed(0)}%`
                        }
                    }
                }
            }
        });
    };

    const attachHoverHandlers = (wrapper) => {
        const panel = wrapper.querySelector(".explain-card");
        const canvas = wrapper.querySelector("canvas");
        const axes = parseAxes(wrapper.dataset.axes);
        let chartInstance = null;

        const open = () => {
            panel?.classList.add("show");
            if (!chartInstance) {
                chartInstance = ensureChart(canvas, axes);
            }
        };

        const close = () => {
            panel?.classList.remove("show");
        };

        wrapper.addEventListener("mouseenter", open);
        wrapper.addEventListener("focusin", open);
        wrapper.addEventListener("mouseleave", close);
        wrapper.addEventListener("focusout", close);
    };

    const init = () => {
        const cards = document.querySelectorAll(".rec-hover-card");
        cards.forEach(card => {
            if (hoverCards.includes(card)) return;
            hoverCards.push(card);
            attachHoverHandlers(card);
        });
    };

    document.addEventListener("DOMContentLoaded", init);
    return { init };
})();
