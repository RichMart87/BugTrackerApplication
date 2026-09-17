<script setup>
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'

const props = defineProps({
  points: { type: Array, default: () => [] },
  months: { type: Number, default: 12 },
  loading: { type: Boolean, default: false }
})

const emit = defineEmits(['update:months'])

const RANGES = [6, 12, 24]

const plot = ref(null)
const width = ref(960)
const height = ref(300)
const hoverIndex = ref(null)
const showTable = ref(false)

let observer = null

onMounted(() => {
  if (!plot.value || typeof ResizeObserver === 'undefined') return

  // Rendering in real pixels (rather than scaling a fixed viewBox) keeps the
  // axis type at a constant size on every screen width.
  observer = new ResizeObserver(([entry]) => {
    width.value = Math.max(320, Math.round(entry.contentRect.width))
    height.value = entry.contentRect.width < 560 ? 240 : 300
  })
  observer.observe(plot.value)
})

onBeforeUnmount(() => observer?.disconnect())

const margin = computed(() => ({
  top: 18,
  right: 16,
  bottom: width.value < 560 ? 40 : 46,
  left: 44
}))

const innerWidth = computed(() => Math.max(10, width.value - margin.value.left - margin.value.right))
const innerHeight = computed(() => Math.max(10, height.value - margin.value.top - margin.value.bottom))

const bandWidth = computed(() => (props.points.length ? innerWidth.value / props.points.length : innerWidth.value))

/** Bars are capped at 24px and never fill their band - the leftover is deliberate air. */
const barWidth = computed(() => Math.max(3, Math.min(24, bandWidth.value * 0.52)))

const peak = computed(() => {
  const values = props.points.flatMap((p) => [p.found, p.resolved])
  return Math.max(1, ...values)
})

/** Rounds the axis top to a clean 1/2/5 x 10^n step so ticks read as round numbers. */
const scale = computed(() => {
  const target = peak.value
  const magnitude = 10 ** Math.floor(Math.log10(target))
  const step = [1, 2, 2.5, 5, 10].map((m) => m * magnitude).find((s) => target / s <= 4) ?? magnitude * 10
  const max = Math.ceil(target / step) * step

  const ticks = []
  for (let value = 0; value <= max + 1e-9; value += step) {
    ticks.push(Math.round(value))
  }

  return { max, ticks }
})

function yFor(value) {
  return margin.value.top + innerHeight.value - (value / scale.value.max) * innerHeight.value
}

function centerFor(index) {
  return margin.value.left + bandWidth.value * (index + 0.5)
}

const bars = computed(() =>
  props.points.map((point, index) => {
    const x = centerFor(index) - barWidth.value / 2
    const y = yFor(point.found)
    const barHeight = margin.value.top + innerHeight.value - y
    return { point, index, x, y, height: barHeight, width: barWidth.value }
  })
)

/** Rounded at the data end, square where it meets the baseline. */
function barPath(bar) {
  const r = Math.min(4, bar.width / 2, bar.height)
  const bottom = bar.y + bar.height
  if (bar.height <= 0) return ''
  return `M${bar.x},${bottom} L${bar.x},${bar.y + r} Q${bar.x},${bar.y} ${bar.x + r},${bar.y} L${bar.x + bar.width - r},${bar.y} Q${bar.x + bar.width},${bar.y} ${bar.x + bar.width},${bar.y + r} L${bar.x + bar.width},${bottom} Z`
}

const linePath = computed(() => {
  if (props.points.length === 0) return ''
  return props.points
    .map((point, index) => `${index === 0 ? 'M' : 'L'}${centerFor(index)},${yFor(point.resolved)}`)
    .join(' ')
})

/** Thins x labels when the bands get too narrow to hold them side by side. */
const labelStep = computed(() => Math.max(1, Math.ceil(52 / Math.max(bandWidth.value, 1))))

const latestIndex = computed(() => props.points.length - 1)
const latest = computed(() => props.points[latestIndex.value] ?? null)

const headline = computed(() => {
  if (!latest.value) return ''
  const { label, found, change, percentChange } = latest.value
  if (change === null || change === undefined) {
    return `${label}: ${found} found`
  }
  if (change === 0) return `${label}: ${found} found, unchanged from the month before`
  const direction = change > 0 ? 'up' : 'down'
  const percent = percentChange === null || percentChange === undefined ? '' : ` (${Math.abs(percentChange)}%)`
  return `${label}: ${found} found, ${direction} ${Math.abs(change)}${percent} on the month before`
})

const headlineTone = computed(() => {
  const change = latest.value?.change
  if (change === null || change === undefined || change === 0) return 'flat'
  // More bugs found than last month reads as a regression.
  return change > 0 ? 'bad' : 'good'
})

const hovered = computed(() => (hoverIndex.value === null ? null : props.points[hoverIndex.value] ?? null))

const tooltipStyle = computed(() => {
  if (hoverIndex.value === null) return {}
  const x = centerFor(hoverIndex.value)
  // Flip the tooltip to the other side of the crosshair near the right edge.
  const flip = x > width.value - 150
  return {
    left: `${flip ? x - 12 : x + 12}px`,
    top: `${margin.value.top}px`,
    transform: flip ? 'translateX(-100%)' : 'none'
  }
})

function changeText(point) {
  if (point.change === null || point.change === undefined) return 'no prior month'
  if (point.change === 0) return 'no change'
  const percent =
    point.percentChange === null || point.percentChange === undefined ? '' : ` (${Math.abs(point.percentChange)}%)`
  return `${point.change > 0 ? '▲ up' : '▼ down'} ${Math.abs(point.change)}${percent}`
}

function changeTone(point) {
  if (point.change === null || point.change === undefined || point.change === 0) return 'flat'
  return point.change > 0 ? 'bad' : 'good'
}

const ariaSummary = computed(() => {
  if (!props.points.length) return 'Bugs found per month. No data yet.'
  const parts = props.points.map((p) => `${p.label}: ${p.found} found, ${p.resolved} resolved`)
  return `Bugs found and resolved per month. ${parts.join('. ')}.`
})
</script>

<template>
  <section class="card chart">
    <header class="chart__head">
      <div class="chart__titles">
        <h2 class="chart__title">Bugs found month over month</h2>
        <p class="chart__subtitle" :class="`chart__subtitle--${headlineTone}`">
          {{ loading ? 'Loading trend…' : headline || 'No bugs recorded yet.' }}
        </p>
      </div>

      <div class="chart__controls">
        <div class="legend">
          <span class="legend__item">
            <span class="legend__swatch legend__swatch--found" aria-hidden="true"></span>Found
          </span>
          <span class="legend__item">
            <span class="legend__swatch legend__swatch--resolved" aria-hidden="true"></span>Resolved
          </span>
        </div>

        <div class="range" role="group" aria-label="Trend range">
          <button
            v-for="range in RANGES"
            :key="range"
            type="button"
            class="range__btn"
            :class="{ 'range__btn--active': months === range }"
            :aria-pressed="months === range"
            @click="emit('update:months', range)"
          >
            {{ range }}m
          </button>
        </div>

        <button type="button" class="btn btn--ghost btn--sm" @click="showTable = !showTable">
          {{ showTable ? 'Show chart' : 'Show table' }}
        </button>
      </div>
    </header>

    <div v-if="!showTable" ref="plot" class="chart__plot" :class="{ 'chart__plot--loading': loading }">
      <svg
        :width="width"
        :height="height"
        role="img"
        :aria-label="ariaSummary"
        @mouseleave="hoverIndex = null"
      >
        <!-- Gridlines: hairline, solid, recessive -->
        <g>
          <line
            v-for="tick in scale.ticks"
            :key="`grid-${tick}`"
            :x1="margin.left"
            :x2="width - margin.right"
            :y1="yFor(tick)"
            :y2="yFor(tick)"
            :stroke="tick === 0 ? 'var(--baseline)' : 'var(--gridline)'"
            stroke-width="1"
            shape-rendering="crispEdges"
          />
          <text
            v-for="tick in scale.ticks"
            :key="`tick-${tick}`"
            :x="margin.left - 10"
            :y="yFor(tick) + 4"
            text-anchor="end"
            class="axis-text tabular"
          >
            {{ tick }}
          </text>
        </g>

        <!-- Hover crosshair sits behind the marks -->
        <line
          v-if="hoverIndex !== null"
          :x1="centerFor(hoverIndex)"
          :x2="centerFor(hoverIndex)"
          :y1="margin.top"
          :y2="margin.top + innerHeight"
          stroke="var(--baseline)"
          stroke-width="1"
        />

        <!-- Bars: bugs found -->
        <path
          v-for="bar in bars"
          :key="`bar-${bar.point.month}`"
          :d="barPath(bar)"
          fill="var(--series-found)"
          :opacity="hoverIndex === null || hoverIndex === bar.index ? 1 : 0.45"
        />

        <!-- Line: bugs resolved, same unit and scale as the bars -->
        <path
          :d="linePath"
          fill="none"
          stroke="var(--series-resolved)"
          stroke-width="2"
          stroke-linejoin="round"
          stroke-linecap="round"
        />
        <circle
          v-for="(point, index) in points"
          :key="`dot-${point.month}`"
          :cx="centerFor(index)"
          :cy="yFor(point.resolved)"
          :r="hoverIndex === index ? 5 : 4"
          fill="var(--series-resolved)"
          stroke="var(--surface)"
          stroke-width="2"
        />

        <!-- Selective direct label: the latest bar only -->
        <text
          v-if="latest && bars.length"
          :x="centerFor(latestIndex)"
          :y="yFor(latest.found) - 8"
          text-anchor="middle"
          class="value-text tabular"
        >
          {{ latest.found }}
        </text>

        <!-- X labels -->
        <text
          v-for="(point, index) in points"
          v-show="index % labelStep === 0 || index === latestIndex"
          :key="`x-${point.month}`"
          :x="centerFor(index)"
          :y="margin.top + innerHeight + 18"
          text-anchor="middle"
          class="axis-text"
        >
          {{ point.label.split(' ')[0] }}
        </text>
        <text
          v-for="(point, index) in points"
          v-show="(index % labelStep === 0 || index === latestIndex) && (index === 0 || point.label.split(' ')[1] !== points[index - 1].label.split(' ')[1])"
          :key="`y-${point.month}`"
          :x="centerFor(index)"
          :y="margin.top + innerHeight + 32"
          text-anchor="middle"
          class="axis-text axis-text--year"
        >
          {{ point.label.split(' ')[1] }}
        </text>

        <!-- Hit targets: full-height bands, wider than the marks themselves -->
        <rect
          v-for="(point, index) in points"
          :key="`hit-${point.month}`"
          :x="margin.left + bandWidth * index"
          :y="margin.top"
          :width="bandWidth"
          :height="innerHeight"
          fill="transparent"
          @mouseenter="hoverIndex = index"
          @focus="hoverIndex = index"
        />
      </svg>

      <div v-if="hovered" class="tooltip" :style="tooltipStyle">
        <p class="tooltip__title">{{ hovered.label }}</p>
        <p class="tooltip__row">
          <span class="tooltip__swatch tooltip__swatch--found" aria-hidden="true"></span>
          Found <strong class="tabular">{{ hovered.found }}</strong>
        </p>
        <p class="tooltip__row">
          <span class="tooltip__swatch tooltip__swatch--resolved" aria-hidden="true"></span>
          Resolved <strong class="tabular">{{ hovered.resolved }}</strong>
        </p>
        <p class="tooltip__row tooltip__row--muted">
          Still open <strong class="tabular">{{ hovered.open }}</strong>
        </p>
        <p class="tooltip__delta" :class="`tooltip__delta--${changeTone(hovered)}`">
          {{ changeText(hovered) }} vs prior month
        </p>
      </div>

      <p v-if="!loading && !points.length" class="chart__empty">
        No bugs recorded yet. Add one and the trend will appear here.
      </p>
    </div>

    <!-- The table view is the accessible equivalent of the chart, not a fallback. -->
    <div v-else class="chart__table-wrap">
      <table class="chart__table">
        <thead>
          <tr>
            <th scope="col">Month</th>
            <th scope="col" class="num">Found</th>
            <th scope="col" class="num">Resolved</th>
            <th scope="col" class="num">Still open</th>
            <th scope="col">Change vs prior month</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="point in points" :key="point.month">
            <th scope="row">{{ point.label }}</th>
            <td class="num tabular">{{ point.found }}</td>
            <td class="num tabular">{{ point.resolved }}</td>
            <td class="num tabular">{{ point.open }}</td>
            <td :class="`delta delta--${changeTone(point)}`">{{ changeText(point) }}</td>
          </tr>
          <tr v-if="!points.length">
            <td colspan="5" class="chart__empty-cell">No bugs recorded yet.</td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>
</template>

<style scoped>
.chart {
  padding: 18px 20px 14px;
}

.chart__head {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  align-items: flex-start;
  justify-content: space-between;
  margin-bottom: 6px;
}

.chart__title {
  margin: 0;
  font-size: 15px;
  font-weight: 640;
}

.chart__subtitle {
  margin: 3px 0 0;
  font-size: 12.5px;
  color: var(--ink-secondary);
}

.chart__subtitle--bad {
  color: var(--critical);
  font-weight: 550;
}

.chart__subtitle--good {
  color: var(--good-text);
  font-weight: 550;
}

.chart__controls {
  display: flex;
  align-items: center;
  gap: 14px;
  flex-wrap: wrap;
}

.legend {
  display: flex;
  gap: 12px;
  font-size: 12px;
  color: var(--ink-secondary);
}

.legend__item {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.legend__swatch {
  width: 10px;
  height: 10px;
  border-radius: 3px;
  flex: none;
}

.legend__swatch--found {
  background: var(--series-found);
}

.legend__swatch--resolved {
  background: var(--series-resolved);
  border-radius: 50%;
}

.range {
  display: inline-flex;
  padding: 2px;
  gap: 2px;
  background: var(--surface-sunken);
  border-radius: var(--radius-sm);
}

.range__btn {
  border: none;
  background: transparent;
  color: var(--ink-secondary);
  font-size: 12px;
  font-weight: 600;
  padding: 4px 10px;
  border-radius: 6px;
  cursor: pointer;
}

.range__btn--active {
  background: var(--surface-raised);
  color: var(--ink);
  box-shadow: var(--shadow-sm);
}

.btn--sm {
  padding: 5px 10px;
  font-size: 12px;
}

.chart__plot {
  position: relative;
  width: 100%;
  transition: opacity 150ms ease;
}

.chart__plot--loading {
  opacity: 0.45;
}

.chart__plot svg {
  display: block;
  max-width: 100%;
}

.axis-text {
  font-size: 11px;
  fill: var(--ink-muted);
}

.axis-text--year {
  font-size: 10px;
}

.value-text {
  font-size: 11.5px;
  font-weight: 650;
  fill: var(--ink-secondary);
}

.tooltip {
  position: absolute;
  z-index: 5;
  min-width: 150px;
  padding: 9px 11px;
  border-radius: var(--radius-sm);
  background: var(--surface-raised);
  border: 1px solid var(--hairline);
  box-shadow: var(--shadow);
  pointer-events: none;
}

.tooltip__title {
  margin: 0 0 5px;
  font-size: 12px;
  font-weight: 650;
}

.tooltip__row {
  margin: 0;
  font-size: 12px;
  color: var(--ink-secondary);
  display: flex;
  align-items: center;
  gap: 6px;
}

.tooltip__row strong {
  margin-left: auto;
  color: var(--ink);
}

.tooltip__row--muted {
  color: var(--ink-muted);
}

.tooltip__swatch {
  width: 8px;
  height: 8px;
  border-radius: 2px;
  flex: none;
}

.tooltip__swatch--found {
  background: var(--series-found);
}

.tooltip__swatch--resolved {
  background: var(--series-resolved);
  border-radius: 50%;
}

.tooltip__delta {
  margin: 6px 0 0;
  padding-top: 6px;
  border-top: 1px solid var(--hairline);
  font-size: 11.5px;
  font-weight: 600;
}

.tooltip__delta--bad {
  color: var(--critical);
}

.tooltip__delta--good {
  color: var(--good-text);
}

.tooltip__delta--flat {
  color: var(--ink-muted);
}

.chart__empty {
  position: absolute;
  inset: 0;
  display: grid;
  place-items: center;
  margin: 0;
  color: var(--ink-muted);
  font-size: 13px;
}

.chart__table-wrap {
  overflow-x: auto;
}

.chart__table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}

.chart__table th,
.chart__table td {
  padding: 7px 10px;
  text-align: left;
  border-bottom: 1px solid var(--hairline);
  white-space: nowrap;
}

.chart__table thead th {
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--ink-muted);
}

.chart__table tbody th {
  font-weight: 600;
}

.num {
  text-align: right;
}

.delta--bad {
  color: var(--critical);
  font-weight: 550;
}

.delta--good {
  color: var(--good-text);
  font-weight: 550;
}

.delta--flat {
  color: var(--ink-muted);
}

.chart__empty-cell {
  text-align: center;
  color: var(--ink-muted);
  padding: 20px;
}
</style>
