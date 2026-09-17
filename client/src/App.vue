<script setup>
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { ApiError, bugApi } from './api/bugApi.js'
import { useTheme } from './composables/useTheme.js'
import { useToasts } from './composables/useToasts.js'
import BugFormModal from './components/BugFormModal.vue'
import BugTable from './components/BugTable.vue'
import ConfirmDialog from './components/ConfirmDialog.vue'
import FilterBar from './components/FilterBar.vue'
import StatTile from './components/StatTile.vue'
import ToastStack from './components/ToastStack.vue'
import TrendChart from './components/TrendChart.vue'

const toasts = useToasts()
const { theme, isDark, toggle: toggleTheme } = useTheme()

const filters = ref({
  search: '',
  severity: [],
  environment: [],
  resolved: '',
  foundByAutomation: '',
  from: '',
  to: ''
})

const paging = reactive({ page: 1, pageSize: 25 })
const sorting = reactive({ sort: 'dateFound', direction: 'desc' })

const bugs = ref([])
const totalCount = ref(0)
const totalPages = ref(0)
const summary = ref(null)
const trend = ref([])
const trendMonths = ref(12)

const loadingList = ref(true)
const loadingStats = ref(true)
const busyId = ref(null)
const loadError = ref('')

const modalOpen = ref(false)
const editingBug = ref(null)
const saving = ref(false)
const serverErrors = ref({})

const pendingDelete = ref(null)
const deleting = ref(false)

const exportMenuOpen = ref(false)
const exporting = ref('')
const exportMenu = ref(null)

/** Filter state translated into the query the API expects. */
const queryParams = computed(() => ({
  search: filters.value.search || undefined,
  severity: filters.value.severity,
  environment: filters.value.environment,
  resolved: filters.value.resolved === '' ? undefined : filters.value.resolved,
  foundByAutomation: filters.value.foundByAutomation === '' ? undefined : filters.value.foundByAutomation,
  from: filters.value.from || undefined,
  to: filters.value.to || undefined,
  sort: sorting.sort,
  direction: sorting.direction
}))

async function loadBugs() {
  loadingList.value = true
  loadError.value = ''

  try {
    const result = await bugApi.list({ ...queryParams.value, page: paging.page, pageSize: paging.pageSize })
    bugs.value = result.items
    totalCount.value = result.totalCount
    totalPages.value = result.totalPages

    // A filter change can leave us past the end of the result set.
    if (paging.page > result.totalPages && result.totalPages > 0) {
      paging.page = result.totalPages
    }
  } catch (error) {
    loadError.value = error instanceof ApiError ? error.message : 'Something went wrong loading bugs.'
    bugs.value = []
    totalCount.value = 0
  } finally {
    loadingList.value = false
  }
}

async function loadStats() {
  loadingStats.value = true

  try {
    const [summaryResult, trendResult] = await Promise.all([
      bugApi.summary(),
      bugApi.monthlyTrend(trendMonths.value)
    ])
    summary.value = summaryResult
    trend.value = trendResult
  } catch {
    // The grid is the primary view; a stats hiccup should not blank the page.
    toasts.error('Could not refresh the dashboard metrics.')
  } finally {
    loadingStats.value = false
  }
}

function refreshAll() {
  return Promise.all([loadBugs(), loadStats()])
}

// Any filter or sort change resets to the first page before refetching. Moving off
// page 1 is left to the page watcher below so the change only costs one request.
watch(
  () => [queryParams.value, paging.pageSize],
  () => {
    if (paging.page === 1) {
      loadBugs()
    } else {
      paging.page = 1
    }
  },
  { deep: true }
)

watch(() => paging.page, loadBugs)

watch(trendMonths, async (months) => {
  try {
    trend.value = await bugApi.monthlyTrend(months)
  } catch {
    toasts.error('Could not load the trend for that range.')
  }
})

onMounted(() => {
  refreshAll()
  document.addEventListener('click', onDocumentClick)
})

onBeforeUnmount(() => document.removeEventListener('click', onDocumentClick))

function onDocumentClick(event) {
  if (exportMenuOpen.value && exportMenu.value && !exportMenu.value.contains(event.target)) {
    exportMenuOpen.value = false
  }
}

function onSort(key) {
  if (sorting.sort === key) {
    sorting.direction = sorting.direction === 'asc' ? 'desc' : 'asc'
  } else {
    sorting.sort = key
    // Dates and flags read best newest/true first; text reads best A-Z.
    sorting.direction = key === 'title' ? 'asc' : 'desc'
  }
}

function openCreate() {
  editingBug.value = null
  serverErrors.value = {}
  modalOpen.value = true
}

function openEdit(bug) {
  editingBug.value = bug
  serverErrors.value = {}
  modalOpen.value = true
}

async function saveBug(payload) {
  saving.value = true
  serverErrors.value = {}

  try {
    if (editingBug.value) {
      await bugApi.update(editingBug.value.id, payload)
      toasts.success(`Bug #${editingBug.value.id} updated.`)
    } else {
      const created = await bugApi.create(payload)
      toasts.success(`Bug #${created.id} logged.`)
    }

    modalOpen.value = false
    editingBug.value = null
    await refreshAll()
  } catch (error) {
    if (error instanceof ApiError && Object.keys(error.fieldErrors).length) {
      serverErrors.value = error.fieldErrors
    }
    toasts.error(error instanceof ApiError ? error.message : 'Could not save the bug.')
  } finally {
    saving.value = false
  }
}

async function toggleResolved(bug) {
  busyId.value = bug.id
  const next = !bug.resolved

  try {
    const updated = await bugApi.setResolved(bug.id, next)
    // Patch in place so the row does not jump while the list is re-sorted.
    bugs.value = bugs.value.map((item) => (item.id === updated.id ? updated : item))
    toasts.success(`Bug #${bug.id} marked ${next ? 'resolved' : 'open'}.`)
    loadStats()
  } catch (error) {
    toasts.error(error instanceof ApiError ? error.message : 'Could not update the bug.')
  } finally {
    busyId.value = null
  }
}

async function confirmDelete() {
  if (!pendingDelete.value) return
  deleting.value = true

  try {
    await bugApi.remove(pendingDelete.value.id)
    toasts.success(`Bug #${pendingDelete.value.id} deleted.`)
    pendingDelete.value = null
    await refreshAll()
  } catch (error) {
    toasts.error(error instanceof ApiError ? error.message : 'Could not delete the bug.')
  } finally {
    deleting.value = false
  }
}

async function exportAs(format) {
  exportMenuOpen.value = false
  exporting.value = format

  try {
    // Exports honour the filters currently applied to the grid, not just this page.
    const filename = await bugApi.download(format, queryParams.value)
    toasts.success(`Downloaded ${filename}`)
  } catch (error) {
    toasts.error(error instanceof ApiError ? error.message : `Could not export the ${format.toUpperCase()}.`)
  } finally {
    exporting.value = ''
  }
}

const rangeLabel = computed(() => {
  if (!totalCount.value) return 'No results'
  const first = (paging.page - 1) * paging.pageSize + 1
  const last = Math.min(paging.page * paging.pageSize, totalCount.value)
  return `${first.toLocaleString()}–${last.toLocaleString()} of ${totalCount.value.toLocaleString()}`
})

const momDelta = computed(() =>
  summary.value
    ? {
        value: summary.value.monthOverMonthChange,
        percent: summary.value.monthOverMonthPercent,
        goodDirection: 'down'
      }
    : null
)
</script>

<template>
  <div class="app">
    <header class="topbar">
      <div class="topbar__inner">
        <div class="brand">
          <span class="brand__mark" aria-hidden="true">
            <svg viewBox="0 0 24 24" width="18" height="18">
              <path
                d="M12 4a4 4 0 0 1 4 4v1h1.5a1 1 0 0 1 0 2H16v2h1.5a1 1 0 0 1 0 2H16v1a4 4 0 0 1-8 0v-1H6.5a1 1 0 0 1 0-2H8v-2H6.5a1 1 0 0 1 0-2H8V8a4 4 0 0 1 4-4z"
                fill="currentColor"
              />
            </svg>
          </span>
          <div>
            <h1 class="brand__name">BugTracker</h1>
            <p class="brand__tagline">Defect intake, triage and reporting</p>
          </div>
        </div>

        <div class="topbar__actions">
          <div ref="exportMenu" class="menu">
            <button
              type="button"
              class="btn"
              :disabled="Boolean(exporting)"
              aria-haspopup="menu"
              :aria-expanded="exportMenuOpen"
              @click="exportMenuOpen = !exportMenuOpen"
            >
              <svg viewBox="0 0 16 16" width="14" height="14" aria-hidden="true">
                <path d="M8 2v8m0 0 3-3m-3 3L5 7M3 12.5h10" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
              </svg>
              {{ exporting ? `Exporting ${exporting.toUpperCase()}…` : 'Export' }}
            </button>

            <Transition name="menu">
              <div v-if="exportMenuOpen" class="menu__panel card" role="menu">
                <button type="button" class="menu__item" role="menuitem" @click="exportAs('csv')">
                  <strong>CSV</strong>
                  <em>Spreadsheet-ready, every filtered row</em>
                </button>
                <button type="button" class="menu__item" role="menuitem" @click="exportAs('pdf')">
                  <strong>PDF report</strong>
                  <em>Printable summary with the monthly trend</em>
                </button>
              </div>
            </Transition>
          </div>

          <button
            type="button"
            class="btn btn--icon"
            :title="`Theme: ${theme}`"
            :aria-label="`Switch to ${isDark() ? 'light' : 'dark'} theme`"
            @click="toggleTheme"
          >
            <svg v-if="isDark()" viewBox="0 0 16 16" width="15" height="15" aria-hidden="true">
              <circle cx="8" cy="8" r="3.2" fill="currentColor" />
              <path d="M8 1v1.8M8 13.2V15M1 8h1.8M13.2 8H15M3 3l1.3 1.3M11.7 11.7 13 13M13 3l-1.3 1.3M4.3 11.7 3 13"
                stroke="currentColor" stroke-width="1.4" stroke-linecap="round" />
            </svg>
            <svg v-else viewBox="0 0 16 16" width="15" height="15" aria-hidden="true">
              <path d="M13.5 9.6A5.8 5.8 0 0 1 6.4 2.5a5.8 5.8 0 1 0 7.1 7.1z" fill="currentColor" />
            </svg>
          </button>

          <button type="button" class="btn btn--primary" @click="openCreate">
            <span aria-hidden="true">＋</span> New bug
          </button>
        </div>
      </div>
    </header>

    <main class="page">
      <section class="tiles" aria-label="Summary">
        <StatTile
          label="Total bugs"
          :value="summary?.total ?? 0"
          :caption="`${summary?.resolutionRatePercent ?? 0}% resolved overall`"
          :loading="loadingStats && !summary"
        />
        <StatTile
          label="Open"
          :value="summary?.open ?? 0"
          :caption="`${summary?.openCritical ?? 0} critical · ${summary?.openInProduction ?? 0} in production`"
          :loading="loadingStats && !summary"
        />
        <StatTile
          label="Found this month"
          :value="summary?.foundThisMonth ?? 0"
          :caption="`${summary?.foundLastMonth ?? 0} found last month`"
          :delta="momDelta"
          :loading="loadingStats && !summary"
        />
        <StatTile
          label="Found by automation"
          :value="summary?.foundByAutomation ?? 0"
          :caption="`${summary?.automationSharePercent ?? 0}% of all defects`"
          :loading="loadingStats && !summary"
        />
      </section>

      <TrendChart v-model:months="trendMonths" :points="trend" :loading="loadingStats" />

      <FilterBar v-model="filters" :result-count="totalCount" :loading="loadingList" />

      <p v-if="loadError" class="banner banner--error" role="alert">
        {{ loadError }}
        <button type="button" class="btn btn--ghost" @click="refreshAll">Retry</button>
      </p>

      <BugTable
        :bugs="bugs"
        :sort="sorting.sort"
        :direction="sorting.direction"
        :loading="loadingList"
        :busy-id="busyId"
        @sort="onSort"
        @edit="openEdit"
        @delete="pendingDelete = $event"
        @toggle-resolved="toggleResolved"
      />

      <nav v-if="totalCount > 0" class="pager" aria-label="Pagination">
        <p class="pager__range tabular">{{ rangeLabel }}</p>

        <label class="pager__size">
          Rows
          <select v-model.number="paging.pageSize" class="select" aria-label="Rows per page">
            <option :value="10">10</option>
            <option :value="25">25</option>
            <option :value="50">50</option>
            <option :value="100">100</option>
          </select>
        </label>

        <div class="pager__buttons">
          <button type="button" class="btn" :disabled="paging.page <= 1" @click="paging.page--">Previous</button>
          <span class="pager__page tabular">Page {{ paging.page }} of {{ Math.max(totalPages, 1) }}</span>
          <button type="button" class="btn" :disabled="paging.page >= totalPages" @click="paging.page++">Next</button>
        </div>
      </nav>
    </main>

    <BugFormModal
      :open="modalOpen"
      :bug="editingBug"
      :saving="saving"
      :server-errors="serverErrors"
      @close="modalOpen = false"
      @submit="saveBug"
    />

    <ConfirmDialog
      :open="Boolean(pendingDelete)"
      title="Delete this bug?"
      :message="pendingDelete ? `“${pendingDelete.title}” (#${pendingDelete.id}) will be permanently removed. This cannot be undone.` : ''"
      :busy="deleting"
      @confirm="confirmDelete"
      @cancel="pendingDelete = null"
    />

    <ToastStack />
  </div>
</template>

<style scoped>
.app {
  min-height: 100vh;
}

.topbar {
  position: sticky;
  top: 0;
  z-index: 50;
  background: color-mix(in srgb, var(--surface) 88%, transparent);
  backdrop-filter: blur(10px);
  border-bottom: 1px solid var(--hairline);
  padding-top: env(safe-area-inset-top, 0px);
}

.topbar__inner {
  max-width: 1320px;
  margin: 0 auto;
  padding: 12px 20px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  flex-wrap: wrap;
}

.brand {
  display: flex;
  align-items: center;
  gap: 11px;
  min-width: 0;
}

.brand__mark {
  width: 34px;
  height: 34px;
  border-radius: 10px;
  display: grid;
  place-items: center;
  background: var(--accent);
  color: var(--accent-contrast);
  flex: none;
}

.brand__name {
  margin: 0;
  font-size: 16px;
  font-weight: 660;
  letter-spacing: -0.01em;
}

.brand__tagline {
  margin: 0;
  font-size: 11.5px;
  color: var(--ink-muted);
}

.topbar__actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.menu {
  position: relative;
}

.menu__panel {
  position: absolute;
  right: 0;
  top: calc(100% + 6px);
  width: 260px;
  padding: 5px;
  background: var(--surface-raised);
  box-shadow: var(--shadow);
  z-index: 60;
}

.menu__item {
  display: block;
  width: 100%;
  text-align: left;
  border: none;
  background: none;
  padding: 8px 10px;
  border-radius: var(--radius-sm);
  cursor: pointer;
}

.menu__item:hover {
  background: var(--surface-sunken);
}

.menu__item strong {
  display: block;
  font-size: 13px;
  font-weight: 600;
}

.menu__item em {
  display: block;
  font-style: normal;
  font-size: 11.5px;
  color: var(--ink-muted);
}

.menu-enter-active,
.menu-leave-active {
  transition: opacity 120ms ease, transform 120ms ease;
}

.menu-enter-from,
.menu-leave-to {
  opacity: 0;
  transform: translateY(-4px);
}

.page {
  max-width: 1320px;
  margin: 0 auto;
  padding: 20px 20px calc(40px + env(safe-area-inset-bottom, 0px));
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.tiles {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(210px, 1fr));
  gap: 14px;
}

.banner {
  margin: 0;
  padding: 11px 14px;
  border-radius: var(--radius);
  font-size: 13px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.banner--error {
  background: var(--sev-critical-bg);
  color: var(--sev-critical-ink);
  border: 1px solid color-mix(in srgb, var(--critical) 35%, transparent);
}

.pager {
  display: flex;
  align-items: center;
  gap: 16px;
  flex-wrap: wrap;
  font-size: 12.5px;
  color: var(--ink-secondary);
}

.pager__range {
  margin: 0;
}

.pager__size {
  display: inline-flex;
  align-items: center;
  gap: 7px;
}

.pager__size .select {
  width: auto;
  padding-block: 5px;
}

.pager__buttons {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-left: auto;
}

@media (max-width: 640px) {
  .topbar__inner {
    padding: 10px 14px;
  }

  .page {
    padding-inline: 14px;
  }

  .pager__buttons {
    margin-left: 0;
  }
}
</style>
