// Dashboard charts. The numbers come from data-* attributes rendered by the server,
// the colours come from the CSS theme tokens, so charts follow the white / black theme.

const chartDataElement = document.getElementById('chartData');
const chartValues = chartDataElement ? chartDataElement.dataset : {};
const readJson = attributeName => JSON.parse(chartValues[attributeName] || '[]');
const indianNumber = new Intl.NumberFormat('en-IN', { maximumFractionDigits: 0 });
const formatRupees = amount => '₹' + indianNumber.format(amount);
// Short axis labels in lakh / crore, the way Indian sales teams read numbers.
const shortRupees = amount => amount >= 10000000 ? '₹' + (amount / 10000000).toFixed(1) + 'Cr'
    : amount >= 100000 ? '₹' + (amount / 100000).toFixed(1) + 'L'
    : formatRupees(amount);

let drawnCharts = [];

function themeColour(tokenName) {
    return getComputedStyle(document.documentElement).getPropertyValue(tokenName).trim();
}

// Shows "No data in this period" instead of an empty chart frame.
const emptyChartMessage = {
    id: 'emptyChartMessage',
    afterDraw(chart) {
        const hasValues = chart.data.datasets.some(dataset => dataset.data.some(value => Number(value) > 0));
        if (hasValues || !chart.chartArea) return;
        const { ctx, chartArea } = chart;
        ctx.save();
        ctx.textAlign = 'center';
        ctx.fillStyle = themeColour('--text-muted');
        ctx.font = '13px -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif';
        ctx.fillText('No data in this period', (chartArea.left + chartArea.right) / 2, (chartArea.top + chartArea.bottom) / 2);
        ctx.restore();
    }
};

function moneyAxis(gridColour) {
    return {
        beginAtZero: true,
        border: { display: false },
        grid: { color: gridColour },
        ticks: { callback: value => shortRupees(value) }
    };
}

function drawCharts() {
    if (typeof Chart === 'undefined' || !chartDataElement) return;
    drawnCharts.forEach(chart => chart.destroy());
    drawnCharts = [];

    const palette = ['--chart-1', '--chart-2', '--chart-3', '--chart-4', '--chart-5', '--chart-6'].map(themeColour);
    const gridColour = themeColour('--chart-grid');
    const textColour = themeColour('--text-muted');
    const panelColour = themeColour('--page-bg');

    Chart.defaults.font.family = '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif';
    Chart.defaults.font.size = 11.5;
    Chart.defaults.color = textColour;
    Chart.defaults.animation = false;

    const moneyTooltip = { callbacks: { label: context => formatRupees(context.raw) } };

    const pipelineCanvas = document.getElementById('pipelineChart');
    if (pipelineCanvas) {
        drawnCharts.push(new Chart(pipelineCanvas, {
            type: 'bar',
            data: { labels: readJson('stages'), datasets: [{ data: readJson('pipeline'), backgroundColor: palette, borderRadius: 8, maxBarThickness: 42 }] },
            options: {
                maintainAspectRatio: false,
                plugins: { legend: { display: false }, tooltip: moneyTooltip },
                scales: { x: { grid: { display: false }, border: { display: false } }, y: moneyAxis(gridColour) }
            },
            plugins: [emptyChartMessage]
        }));
    }

    const leadCanvas = document.getElementById('leadChart');
    if (leadCanvas) {
        drawnCharts.push(new Chart(leadCanvas, {
            type: 'doughnut',
            data: { labels: readJson('leadLabels'), datasets: [{ data: readJson('leadCounts'), backgroundColor: palette, borderWidth: 3, borderColor: panelColour, hoverOffset: 4 }] },
            options: {
                maintainAspectRatio: false,
                cutout: '70%',
                plugins: { legend: { position: 'bottom', labels: { usePointStyle: true, boxWidth: 7, padding: 14 } } }
            },
            plugins: [emptyChartMessage]
        }));
    }

    const salesCanvas = document.getElementById('salesChart');
    if (salesCanvas) {
        drawnCharts.push(new Chart(salesCanvas, {
            type: 'line',
            data: {
                labels: readJson('months'),
                datasets: [{ data: readJson('sales'), borderColor: palette[0], backgroundColor: gridColour, fill: true, tension: .35, pointRadius: 3, pointBackgroundColor: palette[0], borderWidth: 2 }]
            },
            options: {
                maintainAspectRatio: false,
                plugins: { legend: { display: false }, tooltip: moneyTooltip },
                scales: { x: { grid: { display: false }, border: { display: false } }, y: moneyAxis(gridColour) }
            },
            plugins: [emptyChartMessage]
        }));
    }

    const teamCanvas = document.getElementById('teamChart');
    if (teamCanvas) {
        drawnCharts.push(new Chart(teamCanvas, {
            type: 'bar',
            data: {
                labels: readJson('teamNames'),
                datasets: [
                    { label: 'Open pipeline', data: readJson('teamPipeline'), backgroundColor: palette[1], borderRadius: 6, maxBarThickness: 22 },
                    { label: 'Won value', data: readJson('teamWon'), backgroundColor: palette[0], borderRadius: 6, maxBarThickness: 22 }
                ]
            },
            options: {
                indexAxis: 'y',
                maintainAspectRatio: false,
                plugins: {
                    legend: { position: 'bottom', labels: { usePointStyle: true, boxWidth: 7, padding: 14 } },
                    tooltip: { callbacks: { label: context => context.dataset.label + ': ' + formatRupees(context.raw) } }
                },
                scales: { y: { grid: { display: false }, border: { display: false } }, x: moneyAxis(gridColour) }
            },
            plugins: [emptyChartMessage]
        }));
    }
}

drawCharts();
document.addEventListener('acxiom:themechange', drawCharts);
