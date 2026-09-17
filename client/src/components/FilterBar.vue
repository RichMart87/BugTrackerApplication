<script setup>
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { ENVIRONMENTS, SEVERITIES } from '../api/bugApi.js'

const props = defineProps({
  modelValue: { type: Object, required: true },
  resultCount: { type: Number, default: 0 },
  loading: { type: Boolean, default: false }
})

const emit = defineEmits(['update:modelValue'])

const searchText = ref(props.modelValue.search ?? '')
let debounceTimer = null

// Keep the box in step when filters are cleared from outside this component.
watch(
  () => props.modelValue.search,
  (value) => {
    if ((value ?? '') !== searchText.value) searchText.value = value ?? ''
  }
)

watch(searchText, (value) => {
  clearTimeout(debounceTimer)
  // One request per pause in typing rather than one per keystroke.
  debounceTimer = setTimeout(() => patch({ search: value.trim() }), 300)
})

onBeforeUnmount(() => clearTimeout(debounceTimer))

function patch(changes) {
  emit('update:modelValue', { ...props.modelValue, ...changes })
}

function toggleIn(key, value) {
  const current = props.modelValue[key] ?? []
  patch({ [key]: current.includes(value) ? current.filter((v) => v !== value) : [...current, value] })
}

function isActive(key, value) {
  return (props.modelValue[key] ?? []).includes(value)
}

const activeCount = computed(() => {
  const f = props.modelValue
  return (
    (f.search ? 1 : 0) +
    (f.severity?.length ?? 0) +
    (f.environment?.length ?? 0) +
    (f.resolved === '' || f.resolved === null || f.resolved === undefined ? 0 : 1) +
    (f.foundByAutomation === '' || f.foundByAutomation === null || f.foundByAutomation === undefined ? 0 : 1) +
    (f.from ? 1 : 0) +
    (f.to ? 1 : 0)
  )
})

function reset() {
  searchText.value = ''
  emit('update:modelValue', {
    search: '',
    severity: [],
    environment: [],
    resolved: '',
    foundByAutomation: '',
    from: '',
    to: ''
  })
}
</script>

<template>
  <section class="filters card" aria-label="Filters">
    <div class="filters__row">
      <div class="filters__search">
        <svg class="filters__search-icon" viewBox="0 0 16 16" aria-hidden="true">
          <circle cx="7" cy="7" r="4.5" fill="none" stroke="currentColor" stroke-width="1.6" />
          <path d="M10.5 10.5 L14 14" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" />
        </svg>
        <input
          v-model="searchText"
          type="search"
          class="input filters__search-input"
          placeholder="Search title, description or notes…"
          aria-label="Search bugs"
        />
      </div>

      <div class="filters__group" role="group" aria-label="Severity">
        <button
          v-for="severity in SEVERITIES"
          :key="severity"
          type="button"
          class="chip"
          :class="[`chip--${severity.toLowerCase()}`, { 'chip--on': isActive('severity', severity) }]"
          :aria-pressed="isActive('severity', severity)"
          @click="toggleIn('severity', severity)"
        >
          {{ severity }}
        </button>
      </div>

      <div class="filters__group" role="group" aria-label="Environment">
        <button
          v-for="environment in ENVIRONMENTS"
          :key="environment"
          type="button"
          class="chip"
          :class="{ 'chip--on': isActive('environment', environment) }"
          :aria-pressed="isActive('environment', environment)"
          @click="toggleIn('environment', environment)"
        >
          {{ environment }}
        </button>
      </div>
    </div>

    <div class="filters__row filters__row--secondary">
      <label class="filters__inline">
        <span class="filters__inline-label">Status</span>
        <select
          class="select"
          :value="modelValue.resolved"
          aria-label="Resolved status"
          @change="patch({ resolved: $event.target.value })"
        >
          <option value="">All</option>
          <option value="false">Open</option>
          <option value="true">Resolved</option>
        </select>
      </label>

      <label class="filters__inline">
        <span class="filters__inline-label">Found by</span>
        <select
          class="select"
          :value="modelValue.foundByAutomation"
          aria-label="Found by automation"
          @change="patch({ foundByAutomation: $event.target.value })"
        >
          <option value="">Anyone</option>
          <option value="true">Automation</option>
          <option value="false">A person</option>
        </select>
      </label>

      <label class="filters__inline">
        <span class="filters__inline-label">From</span>
        <input
          type="date"
          class="input"
          :value="modelValue.from"
          aria-label="Found on or after"
          @change="patch({ from: $event.target.value })"
        />
      </label>

      <label class="filters__inline">
        <span class="filters__inline-label">To</span>
        <input
          type="date"
          class="input"
          :value="modelValue.to"
          aria-label="Found on or before"
          @change="patch({ to: $event.target.value })"
        />
      </label>

      <p class="filters__count" aria-live="polite">
        <template v-if="loading">Loading…</template>
        <template v-else>
          <strong>{{ resultCount.toLocaleString() }}</strong>
          {{ resultCount === 1 ? 'bug' : 'bugs' }}
          <template v-if="activeCount">matched</template>
        </template>
      </p>

      <button
        type="button"
        class="btn btn--ghost"
        :disabled="!activeCount"
        @click="reset"
      >
        Clear filters<template v-if="activeCount"> ({{ activeCount }})</template>
      </button>
    </div>
  </section>
</template>

<style scoped>
.filters {
  padding: 14px 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.filters__row {
  display: flex;
  flex-wrap: wrap;
  gap: 10px 18px;
  align-items: center;
}

.filters__row--secondary {
  padding-top: 12px;
  border-top: 1px solid var(--hairline);
}

.filters__search {
  position: relative;
  flex: 1 1 260px;
  min-width: 200px;
}

.filters__search-icon {
  position: absolute;
  left: 11px;
  top: 50%;
  transform: translateY(-50%);
  width: 15px;
  height: 15px;
  color: var(--ink-muted);
  pointer-events: none;
}

.filters__search-input {
  padding-left: 33px;
}

.filters__group {
  display: flex;
  gap: 5px;
  flex-wrap: wrap;
}

.chip {
  padding: 5px 11px;
  border-radius: 999px;
  border: 1px solid var(--hairline);
  background: var(--surface-raised);
  color: var(--ink-secondary);
  font-size: 12px;
  font-weight: 550;
  cursor: pointer;
  transition: background-color 120ms ease, color 120ms ease, border-color 120ms ease;
}

.chip:hover {
  border-color: var(--baseline);
  color: var(--ink);
}

.chip--on {
  background: var(--accent);
  border-color: transparent;
  color: var(--accent-contrast);
}

.chip--critical.chip--on {
  background: var(--critical);
  color: #fff;
}

.chip--high.chip--on {
  background: var(--serious);
  color: #2a1408;
}

.chip--medium.chip--on {
  background: var(--warning);
  color: #2a2003;
}

.filters__inline {
  display: inline-flex;
  align-items: center;
  gap: 7px;
}

.filters__inline-label {
  font-size: 12px;
  font-weight: 600;
  color: var(--ink-secondary);
  white-space: nowrap;
}

.filters__inline .select,
.filters__inline .input {
  width: auto;
  min-width: 118px;
  padding-block: 6px;
}

.filters__count {
  margin: 0 0 0 auto;
  font-size: 12.5px;
  color: var(--ink-secondary);
}

.filters__count strong {
  color: var(--ink);
}

@media (max-width: 640px) {
  .filters__count {
    margin-left: 0;
  }
}
</style>
