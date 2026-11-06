// Draws a sensor's 7-day trend as a bar chart, matching the chart on the Android sensor page
(function () {
    'use strict';

    var retryCount = 0;

    function renderTrendChart() {
        var canvas = document.getElementById('sh-trend-canvas');
        if (!canvas) {
            return;
        }

        if (!window.Chart) {
            if (retryCount < 20) {
                retryCount++;
                setTimeout(renderTrendChart, 50);
            } else {
                var plot = document.getElementById('sh-trend-plot-wrap');
                var empty = document.getElementById('sh-trend-empty-state');
                if (plot) plot.style.display = 'none';
                if (empty) {
                    empty.style.display = '';
                    empty.querySelector('.sh-trend-empty-title').textContent = 'Chart unavailable';
                    empty.querySelector('.sh-trend-empty-sub').textContent = 'The chart library could not load. Your reported reading is still shown above.';
                }
            }
            return;
        }

        var card = canvas.closest('.sh-trend-card');
        var labels = JSON.parse(canvas.getAttribute('data-labels') || '[]');
        var values = JSON.parse(canvas.getAttribute('data-values') || '[]');
        var unit = canvas.getAttribute('data-unit') || '';

        var readings = values.filter(function (v) { return v !== null && typeof v === 'number' && !isNaN(v); });
        if (readings.length === 0) {
            return;
        }

        var max = Math.max.apply(null, readings);
        var min = Math.min.apply(null, readings);

        // Small readings like pH get one decimal
        var decimals = Math.abs(max) < 20 ? 1 : 0;

        function format(value) {
            return value === null ? '\u2013' : Number(value).toFixed(decimals);
        }

        var axisMin = max === min ? 0 : min * 0.7;
        var axisMax = max;
        if (max === min) {
            axisMax = max > 0 ? max / 0.8 : 1;
        }

        function cssColour(element, name, fallback) {
            var value = getComputedStyle(element).getPropertyValue(name).trim();
            return value || fallback;
        }

        function colours() {
            return {
                bar: cssColour(card || canvas, '--trend', '#2f80ed'),
                text: cssColour(document.body, '--sh-text', '#1f2a22'),
                muted: cssColour(document.body, '--sh-text-muted', '#6b7a70')
            };
        }

        // Writes each day's value above its bar
        var valueLabels = {
            id: 'shValueLabels',
            afterDatasetsDraw: function (chartInstance) {
                var ctx = chartInstance.ctx;
                var meta = chartInstance.getDatasetMeta(0);
                var palette = colours();

                ctx.save();
                ctx.font = '700 11px ' + getComputedStyle(document.body).fontFamily;
                ctx.textAlign = 'center';
                ctx.textBaseline = 'bottom';

                meta.data.forEach(function (bar, index) {
                    var value = values[index];

                    if (value === null) {
                        var bottom = chartInstance.chartArea.bottom;
                        ctx.globalAlpha = 0.35;
                        ctx.fillStyle = palette.muted;
                        ctx.fillRect(bar.x - 14, bottom - 3, 28, 3);
                        ctx.globalAlpha = 1;
                        ctx.fillText(format(value), bar.x, bottom - 8);
                        return;
                    }

                    ctx.fillStyle = palette.text;
                    ctx.fillText(format(value), bar.x, bar.y - 4);
                });

                ctx.restore();
            }
        };

        var reduceMotion = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        var palette = colours();

        // Destroy previous chart instance if canvas is re-rendered
        if (canvas._chartInstance) {
            canvas._chartInstance.destroy();
        }

        var chart = new Chart(canvas, {
            type: 'bar',
            data: {
                labels: labels,
                datasets: [{
                    data: values,
                    backgroundColor: palette.bar,
                    borderRadius: { topLeft: 4, topRight: 4, bottomLeft: 2, bottomRight: 2 },
                    borderSkipped: false,
                    maxBarThickness: 28
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: reduceMotion ? false : { duration: 400 },
                layout: { padding: { top: 22 } },
                scales: {
                    x: {
                        grid: { display: false },
                        border: { display: false },
                        ticks: { color: palette.muted, font: { size: 11, weight: '500' } }
                    },
                    y: {
                        display: false,
                        min: axisMin,
                        max: axisMax
                    }
                },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        displayColors: false,
                        callbacks: {
                            label: function (item) {
                                var value = values[item.dataIndex];
                                return value === null ? 'No readings' : format(value) + (unit ? ' ' + unit : '');
                            }
                        }
                    }
                }
            },
            plugins: [valueLabels]
        });

        canvas._chartInstance = chart;

        // Redraws in the new colours when the site switches between light and dark mode
        function refreshColours() {
            var next = colours();
            chart.data.datasets[0].backgroundColor = next.bar;
            chart.options.scales.x.ticks.color = next.muted;
            chart.update('none');
        }

        new MutationObserver(refreshColours).observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme', 'class'] });

        if (window.matchMedia) {
            var darkQuery = window.matchMedia('(prefers-color-scheme: dark)');
            if (darkQuery.addEventListener) {
                darkQuery.addEventListener('change', refreshColours);
            }
        }
    }

    window.renderTrendChart = renderTrendChart;

    window.updateSensorTrendRealtime = function (liveNumericValue) {
        var canvas = document.getElementById('sh-trend-canvas');
        if (!canvas || typeof liveNumericValue !== 'number' || isNaN(liveNumericValue)) {
            return;
        }

        var values = [];
        try {
            values = JSON.parse(canvas.getAttribute('data-values') || '[]');
        } catch (e) {
            values = [];
        }

        if (!Array.isArray(values) || values.length === 0) {
            return;
        }

        var lastIdx = values.length - 1;
        values[lastIdx] = liveNumericValue;
        canvas.setAttribute('data-values', JSON.stringify(values));

        var plotWrap = document.getElementById('sh-trend-plot-wrap');
        var rangeLabel = document.getElementById('sh-trend-range-label');
        var emptyState = document.getElementById('sh-trend-empty-state');

        if (plotWrap) plotWrap.style.display = '';
        if (rangeLabel) rangeLabel.style.display = '';
        if (emptyState) emptyState.style.display = 'none';

        renderTrendChart();
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', renderTrendChart);
    } else {
        renderTrendChart();
    }
})();
