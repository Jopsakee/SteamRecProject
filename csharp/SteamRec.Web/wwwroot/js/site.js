// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

document.addEventListener("DOMContentLoaded", () => {
    const table = document.querySelector("[data-radar-labels]");
    if (!table) {
        return;
    }

    const labels = (table.dataset.radarLabels || "")
        .split("|")
        .map(l => l.trim())
        .filter(l => l.length > 0);

    if (!labels.length) {
        return;
    }

    const userValues = (table.dataset.radarUser || "")
        .split(",")
        .map(v => parseFloat(v))
        .filter(v => !Number.isNaN(v));

    const rows = table.querySelectorAll("[data-recommendation-row][data-game-radar]");

// Write your JavaScript code.

const hoverCard = document.createElement("div");
    hoverCard.id = "radar-hover-card";
    hoverCard.innerHTML = `
        <div class="radar-hover-top">
            <div class="radar-hover-title"></div>
        </div>
        <div class="radar-hover-subtitle">Explainable AI · Quick glance at this pick</div>
        <div class="radar-hover-grid">
            <div class="radar-hover-chart">
                <canvas id="radar-hover-canvas" width="240" height="200"></canvas>
            </div>
            <div class="radar-hover-meta">
                <div class="meta-label">Your profile vs. this game</div>
                <ul class="meta-list" data-meta-list></ul>
            </div>
        </div>
    `;
    document.body.appendChild(hoverCard);

    const canvas = hoverCard.querySelector("#radar-hover-canvas");
    const title = hoverCard.querySelector(".radar-hover-title");
    const metaList = hoverCard.querySelector("[data-meta-list]");
    let chart = null;

    const clamp = (value, min, max) => Math.min(Math.max(value, min), max);
    const toNumber = (value, fallback = 0) => {
        const parsed = parseFloat(value);
        return Number.isNaN(parsed) ? fallback : parsed;
    };

    const scale = (value, min, max) => {
        if (max === min) return 0;
        const ratio = (value - min) / (max - min);
        return clamp(ratio * 100, 0, 100);
    };

    const buildRadarData = (row) => {
        const gameValues = (row.dataset.gameRadar || "")
            .split(",")
            .map(toNumber);

        return {
            label: row.dataset.gameName || "Game",
            labels,
            userValues,
            gameValues: gameValues.slice(0, labels.length)
        };
    };

    const ensureChart = (data) => {
        if (!chart) {
            chart = new Chart(canvas, {
                type: "radar",
                data: {
                    labels: [
                        ...labels
                    ],
                    datasets: [
                        {
                            label: "Your profile",
                            data: data.userValues.map(v => scale(v, 0, 1)),
                            backgroundColor: "rgba(102, 192, 244, 0.12)",
                            borderColor: "#66c0f4",
                            pointBackgroundColor: "#8fceff",
                            pointBorderColor: "#0b141c",
                            pointRadius: 4,
                            pointHoverRadius: 5,
                            borderWidth: 2.2
                        },
                        {
                            label: data.label,
                            data: data.gameValues.map(v => scale(v, 0, 1)),
                            backgroundColor: "rgba(115, 227, 157, 0.15)",
                            borderColor: "#6ee59f",
                            pointBackgroundColor: "#6ee59f",
                            pointBorderColor: "#0b141c",
                            pointRadius: 5,
                            pointHoverRadius: 6,
                            borderWidth: 2
                        }
                    ]
                },
                options: {
                    responsive: false,
                    maintainAspectRatio: false,
                    scales: {
                        r: {
                            suggestedMin: 0,
                            suggestedMax: 100,
                            angleLines: { color: "rgba(255,255,255,0.08)", lineWidth: 1 },
                            grid: { color: "rgba(255,255,255,0.12)", lineWidth: 1 },
                            pointLabels: { color: "#e5f1ff", font: { size: 12, weight: "600" } },
                            ticks: {
                                display: true,
                                showLabelBackdrop: false,
                                color: "rgba(199, 213, 224, 0.6)",
                                font: { size: 9 },
                                maxTicksLimit: 4
                            }
                        }
                    },
                    plugins: {
                        legend: {
                            display: true,
                            labels: { color: "#c7d5e0", boxWidth: 12, usePointStyle: true }
                        },
                        tooltip: { enabled: false }
                    }
                }
            });
        } else {
            chart.data.labels = labels;
            chart.data.datasets[0].data = data.userValues.map(v => scale(v, 0, 1));
            chart.data.datasets[1].data = data.gameValues.map(v => scale(v, 0, 1));
            chart.data.datasets[1].label = data.label;
            chart.update();
        }
    };

    const fillMetaList = (data) => {
        metaList.innerHTML = "";
        data.labels.forEach((label, idx) => {
            const userVal = data.userValues[idx] ?? 0;
            const gameVal = data.gameValues[idx] ?? 0;
            const li = document.createElement("li");
            li.innerHTML = `
                <div class="meta-row">
                    <span class="meta-axis">${label}</span>
                    <div class="meta-values">
                        <span class="badge meta-badge user">You ${(userVal * 100).toFixed(0)}%</span>
                        <span class="badge meta-badge game">Game ${(gameVal * 100).toFixed(0)}%</span>
                    </div>
                </div>
            `;
            metaList.appendChild(li);
        });
    };

    const positionHoverCard = (event) => {
        const padding = 12;
        const cardRect = hoverCard.getBoundingClientRect();
        const viewportWidth = window.innerWidth;
        const viewportHeight = window.innerHeight;

        let left = event.clientX + padding;
        let top = event.clientY + padding;

        if (left + cardRect.width > viewportWidth) {
            left = event.clientX - cardRect.width - padding;
        }
        if (top + cardRect.height > viewportHeight) {
            top = event.clientY - cardRect.height - padding;
        }

        hoverCard.style.left = `${Math.max(left, padding)}px`;
        hoverCard.style.top = `${Math.max(top, padding)}px`;
    };

    const showHoverCard = (row, event) => {
        const data = buildRadarData(row);
        ensureChart(data);
        title.textContent = data.label;
        fillMetaList(data);
        hoverCard.classList.add("visible");
        positionHoverCard(event);
    };

    const hideHoverCard = () => {
        hoverCard.classList.remove("visible");
    };

    rows.forEach((row) => {
        row.addEventListener("mouseenter", (event) => showHoverCard(row, event));
        row.addEventListener("mousemove", positionHoverCard);
        row.addEventListener("mouseleave", hideHoverCard);
    });
});