<script setup>
import { ref } from 'vue'
import SeverityBadge from './SeverityBadge.vue'

defineProps({
  bugs: { type: Array, default: () => [] },
  sort: { type: String, default: 'dateFound' },
  direction: { type: String, default: 'desc' },
  loading: { type: Boolean, default: false },
  busyId: { type: Number, default: null }
})

const emit = defineEmits(['sort', 'edit', 'delete', 'toggle-resolved'])

const expanded = ref(new Set())

const COLUMNS = [
  { key: 'title', label: 'Bug', sortable: true },
  { key: 'dateFound', label: 'Date found', sortable: true },
  { key: 'severity', label: 'Severity', sortable: true },
  { key: 'environment', label: 'Environment', sortable: true },
  { key: 'notes', label: 'Notes', sortable: false },
  { key: 'resolved', label: 'Resolved', sortable: true },
  { key: 'automation', label: 'Automation', sortable: false },
  { key: 'actions', label: '', sortable: false }
]

function toggleExpanded(id) {
  const next = new Set(expanded.value)
  next.has(id) ? next.delete(id) : next.add(id)
  expanded.value = next
}

function formatDate(value) {
  if (!value) return '—'
  // Parse as a plain calendar date; no timezone shifting on a date-only value.
  const [year, month, day] = value.split('-').map(Number)
  return new Date(year, month - 1, day).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric'
  })
}

function ariaSort(column, sort, direction) {
  if (!column.sortable || sort !== column.key) return 'none'
  return direction === 'asc' ? 'ascending' : 'descending'
}
</script>

<template>
  <div class="grid card" :class="{ 'grid--loading': loading }">
    <div class="grid__scroll">
      <table class="grid__table">
        <caption class="sr-only">Bug reports</caption>
        <thead>
          <tr>
            <th
              v-for="column in COLUMNS"
              :key="column.key"
              scope="col"
              :aria-sort="ariaSort(column, sort, direction)"
              :class="[`col-${column.key}`, { 'col--sortable': column.sortable }]"
            >
              <button
                v-if="column.sortable"
                type="button"
                class="grid__sort"
                @click="emit('sort', column.key)"
              >
                {{ column.label }}
                <span class="grid__sort-arrow" :class="{ 'grid__sort-arrow--on': sort === column.key }">
                  {{ sort === column.key && direction === 'asc' ? '↑' : '↓' }}
                </span>
              </button>
              <span v-else>{{ column.label }}</span>
            </th>
          </tr>
        </thead>

        <tbody>
          <template v-for="bug in bugs" :key="bug.id">
            <tr class="grid__row" :class="{ 'grid__row--resolved': bug.resolved }">
              <td class="col-title">
                <button type="button" class="grid__title" @click="toggleExpanded(bug.id)">
                  <span class="grid__chevron" :class="{ 'grid__chevron--open': expanded.has(bug.id) }" aria-hidden="true">›</span>
                  <span>
                    <span class="grid__title-text">{{ bug.title }}</span>
                    <span class="grid__id tabular">#{{ bug.id }}</span>
                  </span>
                </button>
                <p class="grid__description">{{ bug.description }}</p>
              </td>

              <td class="col-dateFound tabular">{{ formatDate(bug.dateFound) }}</td>

              <td class="col-severity"><SeverityBadge :severity="bug.severity" /></td>

              <td class="col-environment">
                <span class="pill env" :class="`env--${bug.environment.toLowerCase()}`">{{ bug.environment }}</span>
              </td>

              <td class="col-notes">
                <span v-if="bug.notes" class="grid__notes">{{ bug.notes }}</span>
                <span v-else class="grid__muted">—</span>
              </td>

              <td class="col-resolved">
                <button
                  type="button"
                  class="toggle"
                  :class="{ 'toggle--on': bug.resolved }"
                  :disabled="busyId === bug.id"
                  :aria-pressed="bug.resolved"
                  :title="bug.resolved ? 'Mark as open' : 'Mark as resolved'"
                  @click="emit('toggle-resolved', bug)"
                >
                  <span class="toggle__track"><span class="toggle__thumb"></span></span>
                  {{ bug.resolved ? 'Yes' : 'No' }}
                </button>
              </td>

              <td class="col-automation">
                <span v-if="bug.foundByAutomation" class="pill automation">
                  <span aria-hidden="true">⚙</span> Automated
                </span>
                <span v-else class="grid__muted">Manual</span>
              </td>

              <td class="col-actions">
                <div class="grid__actions">
                  <button type="button" class="btn btn--ghost btn--icon" title="Edit bug" @click="emit('edit', bug)">
                    <svg viewBox="0 0 16 16" width="15" height="15" aria-hidden="true">
                      <path d="M11.5 2.5l2 2L6 12l-3 1 1-3 7.5-7.5z" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round" />
                    </svg>
                    <span class="sr-only">Edit</span>
                  </button>
                  <button type="button" class="btn btn--ghost btn--icon grid__delete" title="Delete bug" @click="emit('delete', bug)">
                    <svg viewBox="0 0 16 16" width="15" height="15" aria-hidden="true">
                      <path d="M3 4.5h10M6.5 4.5V3h3v1.5M5 4.5l.6 8h4.8l.6-8" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round" />
                    </svg>
                    <span class="sr-only">Delete</span>
                  </button>
                </div>
              </td>
            </tr>

            <tr v-if="expanded.has(bug.id)" class="grid__detail-row">
              <td :colspan="COLUMNS.length">
                <div class="detail">
                  <div class="detail__block">
                    <h4>Description</h4>
                    <p>{{ bug.description }}</p>
                  </div>
                  <div class="detail__block">
                    <h4>Notes</h4>
                    <p :class="{ 'grid__muted': !bug.notes }">{{ bug.notes || 'No notes captured.' }}</p>
                  </div>
                  <div class="detail__meta">
                    <span><strong>Logged</strong> {{ new Date(bug.createdUtc).toLocaleString() }}</span>
                    <span><strong>Updated</strong> {{ new Date(bug.updatedUtc).toLocaleString() }}</span>
                    <span v-if="bug.dateResolved"><strong>Resolved</strong> {{ formatDate(bug.dateResolved) }}</span>
                  </div>
                </div>
              </td>
            </tr>
          </template>

          <tr v-if="!bugs.length && !loading">
            <td :colspan="COLUMNS.length" class="grid__empty">
              <p class="grid__empty-title">No bugs match these filters</p>
              <p class="grid__empty-hint">Try clearing a filter, or log the first bug with “New bug”.</p>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style scoped>
.grid {
  overflow: hidden;
  transition: opacity 150ms ease;
}

.grid--loading {
  opacity: 0.55;
}

.grid__scroll {
  overflow-x: auto;
}

.grid__table {
  width: 100%;
  min-width: 940px;
  border-collapse: collapse;
  font-size: 13px;
}

thead th {
  position: sticky;
  top: 0;
  z-index: 1;
  background: var(--surface-sunken);
  padding: 9px 12px;
  text-align: left;
  font-size: 11px;
  font-weight: 650;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--ink-muted);
  border-bottom: 1px solid var(--hairline);
  white-space: nowrap;
}

.grid__sort {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  border: none;
  background: none;
  padding: 0;
  font: inherit;
  color: inherit;
  letter-spacing: inherit;
  text-transform: inherit;
  cursor: pointer;
}

.grid__sort:hover {
  color: var(--ink);
}

.grid__sort-arrow {
  opacity: 0;
  font-size: 10px;
}

.grid__sort-arrow--on {
  opacity: 1;
  color: var(--accent);
}

.grid__sort:hover .grid__sort-arrow {
  opacity: 0.5;
}

tbody td {
  padding: 10px 12px;
  border-bottom: 1px solid var(--hairline);
  vertical-align: top;
}

.grid__row:hover td {
  background: var(--surface-sunken);
}

.grid__row--resolved .grid__title-text {
  color: var(--ink-secondary);
}

.col-title {
  min-width: 260px;
  max-width: 340px;
}

.grid__title {
  display: flex;
  align-items: flex-start;
  gap: 6px;
  border: none;
  background: none;
  padding: 0;
  font: inherit;
  text-align: left;
  color: var(--ink);
  font-weight: 600;
  cursor: pointer;
}

.grid__title:hover .grid__title-text {
  color: var(--accent);
}

.grid__chevron {
  color: var(--ink-muted);
  transition: transform 120ms ease;
  line-height: 1.3;
}

.grid__chevron--open {
  transform: rotate(90deg);
}

.grid__id {
  margin-left: 7px;
  font-size: 11px;
  font-weight: 500;
  color: var(--ink-muted);
}

.grid__description,
.grid__notes {
  display: -webkit-box;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.grid__description {
  margin: 3px 0 0 18px;
  font-size: 12px;
  color: var(--ink-secondary);
}

.col-notes {
  max-width: 190px;
  font-size: 12px;
  color: var(--ink-secondary);
}

.grid__muted {
  color: var(--ink-muted);
}

.env {
  background: var(--surface-sunken);
  border: 1px solid var(--hairline);
  color: var(--ink-secondary);
}

.env--production {
  border-color: color-mix(in srgb, var(--critical) 45%, transparent);
  color: var(--sev-critical-ink);
  background: var(--sev-critical-bg);
}

.env--staging {
  border-color: color-mix(in srgb, var(--warning) 45%, transparent);
  color: var(--sev-medium-ink);
  background: var(--sev-medium-bg);
}

.automation {
  background: var(--accent-wash);
  color: var(--accent);
}

.toggle {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  border: none;
  background: none;
  padding: 0;
  font: inherit;
  font-size: 12px;
  font-weight: 600;
  color: var(--ink-secondary);
  cursor: pointer;
}

.toggle:disabled {
  opacity: 0.5;
  cursor: wait;
}

.toggle__track {
  width: 30px;
  height: 17px;
  border-radius: 999px;
  background: var(--baseline);
  padding: 2px;
  display: inline-flex;
  transition: background-color 140ms ease;
}

.toggle__thumb {
  width: 13px;
  height: 13px;
  border-radius: 50%;
  background: #fff;
  box-shadow: var(--shadow-sm);
  transition: transform 140ms ease;
}

.toggle--on {
  color: var(--good-text);
}

.toggle--on .toggle__track {
  background: var(--good);
}

.toggle--on .toggle__thumb {
  transform: translateX(13px);
}

.grid__actions {
  display: flex;
  gap: 2px;
  justify-content: flex-end;
}

.grid__delete:hover {
  color: var(--critical);
}

.grid__detail-row td {
  background: var(--surface-sunken);
  padding: 0 12px 14px 30px;
}

.detail {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 14px;
  padding-top: 4px;
}

.detail__block h4 {
  margin: 0 0 3px;
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--ink-muted);
}

.detail__block p {
  margin: 0;
  font-size: 12.5px;
  color: var(--ink-secondary);
  line-height: 1.55;
  white-space: pre-wrap;
}

.detail__meta {
  grid-column: 1 / -1;
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
  font-size: 11.5px;
  color: var(--ink-muted);
  padding-top: 4px;
  border-top: 1px solid var(--hairline);
}

.detail__meta strong {
  font-weight: 600;
  color: var(--ink-secondary);
}

.grid__empty {
  padding: 46px 20px;
  text-align: center;
}

.grid__empty-title {
  margin: 0;
  font-weight: 600;
}

.grid__empty-hint {
  margin: 4px 0 0;
  color: var(--ink-muted);
  font-size: 12.5px;
}
</style>
