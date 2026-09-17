<script setup>
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { ENVIRONMENTS, SEVERITIES } from '../api/bugApi.js'

const props = defineProps({
  open: { type: Boolean, default: false },
  /** Passing a bug switches the dialog to edit mode. */
  bug: { type: Object, default: null },
  saving: { type: Boolean, default: false },
  serverErrors: { type: Object, default: () => ({}) }
})

const emit = defineEmits(['close', 'submit'])

const today = new Date().toISOString().slice(0, 10)

function blankForm() {
  return {
    title: '',
    dateFound: today,
    description: '',
    severity: 'Medium',
    environment: 'QA',
    notes: '',
    resolved: false,
    foundByAutomation: false
  }
}

const form = ref(blankForm())
const touched = ref({})
const titleInput = ref(null)

const isEdit = computed(() => Boolean(props.bug))

watch(
  () => [props.open, props.bug],
  async ([open]) => {
    if (!open) return

    form.value = props.bug
      ? {
          title: props.bug.title,
          dateFound: props.bug.dateFound,
          description: props.bug.description,
          severity: props.bug.severity,
          environment: props.bug.environment,
          notes: props.bug.notes ?? '',
          resolved: props.bug.resolved,
          foundByAutomation: props.bug.foundByAutomation
        }
      : blankForm()

    touched.value = {}
    await nextTick()
    titleInput.value?.focus()
  },
  { immediate: true }
)

/** Mirrors the server's DataAnnotations so the user sees problems before a round trip. */
const errors = computed(() => {
  const result = {}
  const { title, description, dateFound } = form.value

  if (!title.trim()) result.title = 'A short title is required.'
  else if (title.trim().length < 3) result.title = 'Use at least 3 characters.'
  else if (title.length > 200) result.title = 'Keep the title under 200 characters.'

  if (!description.trim()) result.description = 'Describe what happened.'
  else if (description.trim().length < 5) result.description = 'Use at least 5 characters.'
  else if (description.length > 4000) result.description = 'Keep the description under 4000 characters.'

  if (!dateFound) result.dateFound = 'Pick the date the bug was found.'
  else if (dateFound > today) result.dateFound = 'The date found cannot be in the future.'

  if ((form.value.notes ?? '').length > 4000) result.notes = 'Keep notes under 4000 characters.'

  return result
})

const isValid = computed(() => Object.keys(errors.value).length === 0)

function errorFor(field) {
  // Server errors win: they reflect a submission that actually failed.
  return props.serverErrors?.[field] ?? (touched.value[field] ? errors.value[field] : undefined)
}

function submit() {
  touched.value = { title: true, description: true, dateFound: true, notes: true }
  if (!isValid.value || props.saving) return

  emit('submit', {
    title: form.value.title.trim(),
    dateFound: form.value.dateFound,
    description: form.value.description.trim(),
    severity: form.value.severity,
    environment: form.value.environment,
    notes: form.value.notes.trim() ? form.value.notes.trim() : null,
    resolved: form.value.resolved,
    foundByAutomation: form.value.foundByAutomation
  })
}

function close() {
  if (!props.saving) emit('close')
}

// Escape closes the dialog from anywhere inside it, not only when the overlay
// itself holds focus.
function onKeydown(event) {
  if (event.key === 'Escape' && props.open) close()
}

onMounted(() => window.addEventListener('keydown', onKeydown))
onBeforeUnmount(() => window.removeEventListener('keydown', onKeydown))
</script>

<template>
  <Teleport to="body">
    <Transition name="modal">
      <div
        v-if="open"
        class="overlay"
        role="dialog"
        aria-modal="true"
        :aria-label="isEdit ? 'Edit bug' : 'Log a new bug'"
        @click.self="close"
        @keydown.esc="close"
      >
        <div class="modal card">
          <header class="modal__head">
            <div>
              <h2 class="modal__title">{{ isEdit ? `Edit bug #${bug.id}` : 'Log a new bug' }}</h2>
              <p class="modal__subtitle">
                {{ isEdit ? 'Update the details and save.' : 'Capture what broke, where, and how bad it is.' }}
              </p>
            </div>
            <button type="button" class="btn btn--ghost btn--icon" aria-label="Close" @click="close">✕</button>
          </header>

          <form class="modal__body" novalidate @submit.prevent="submit">
            <div class="field">
              <label class="field__label" for="bug-title">Title</label>
              <input
                id="bug-title"
                ref="titleInput"
                v-model="form.title"
                class="input"
                :class="{ 'input--invalid': errorFor('title') }"
                placeholder="Short summary, e.g. Checkout total ignores discount"
                maxlength="200"
                @blur="touched.title = true"
              />
              <p v-if="errorFor('title')" class="field__error">{{ errorFor('title') }}</p>
            </div>

            <div class="modal__grid">
              <div class="field">
                <label class="field__label" for="bug-date">Date found</label>
                <input
                  id="bug-date"
                  v-model="form.dateFound"
                  type="date"
                  class="input"
                  :class="{ 'input--invalid': errorFor('dateFound') }"
                  :max="today"
                  @blur="touched.dateFound = true"
                />
                <p v-if="errorFor('dateFound')" class="field__error">{{ errorFor('dateFound') }}</p>
              </div>

              <div class="field">
                <label class="field__label" for="bug-severity">Severity</label>
                <select id="bug-severity" v-model="form.severity" class="select">
                  <option v-for="severity in SEVERITIES" :key="severity" :value="severity">{{ severity }}</option>
                </select>
              </div>

              <div class="field">
                <label class="field__label" for="bug-environment">Environment found in</label>
                <select id="bug-environment" v-model="form.environment" class="select">
                  <option v-for="environment in ENVIRONMENTS" :key="environment" :value="environment">
                    {{ environment }}
                  </option>
                </select>
              </div>
            </div>

            <div class="field">
              <label class="field__label" for="bug-description">Description</label>
              <textarea
                id="bug-description"
                v-model="form.description"
                class="textarea"
                :class="{ 'textarea--invalid': errorFor('description') }"
                placeholder="What happened, what you expected, and how to reproduce it."
                maxlength="4000"
                @blur="touched.description = true"
              ></textarea>
              <p v-if="errorFor('description')" class="field__error">{{ errorFor('description') }}</p>
            </div>

            <div class="field">
              <label class="field__label" for="bug-notes">Notes <span class="field__optional">optional</span></label>
              <textarea
                id="bug-notes"
                v-model="form.notes"
                class="textarea textarea--short"
                placeholder="Workarounds, links to logs, who reported it…"
                maxlength="4000"
              ></textarea>
              <p v-if="errorFor('notes')" class="field__error">{{ errorFor('notes') }}</p>
            </div>

            <div class="switches">
              <label class="switch">
                <input v-model="form.foundByAutomation" type="checkbox" />
                <span class="switch__box" aria-hidden="true"></span>
                <span>
                  <strong>Found by automation</strong>
                  <em>An automated test surfaced this, not a person.</em>
                </span>
              </label>

              <label class="switch">
                <input v-model="form.resolved" type="checkbox" />
                <span class="switch__box" aria-hidden="true"></span>
                <span>
                  <strong>Already resolved</strong>
                  <em>Tick if this was fixed before it was logged.</em>
                </span>
              </label>
            </div>

            <footer class="modal__foot">
              <button type="button" class="btn" :disabled="saving" @click="close">Cancel</button>
              <button type="submit" class="btn btn--primary" :disabled="saving">
                {{ saving ? 'Saving…' : isEdit ? 'Save changes' : 'Create bug' }}
              </button>
            </footer>
          </form>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.overlay {
  position: fixed;
  inset: 0;
  z-index: 100;
  background: rgba(11, 11, 11, 0.45);
  backdrop-filter: blur(2px);
  display: flex;
  align-items: flex-start;
  justify-content: center;
  padding: max(24px, env(safe-area-inset-top, 0px)) 16px 24px;
  overflow-y: auto;
}

.modal {
  width: 100%;
  max-width: 620px;
  background: var(--surface);
  box-shadow: var(--shadow-lg);
  margin: auto;
}

.modal__head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  padding: 18px 20px 12px;
  border-bottom: 1px solid var(--hairline);
}

.modal__title {
  margin: 0;
  font-size: 16px;
  font-weight: 640;
}

.modal__subtitle {
  margin: 2px 0 0;
  font-size: 12.5px;
  color: var(--ink-muted);
}

.modal__body {
  padding: 16px 20px 18px;
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.modal__grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
  gap: 14px;
}

.field__optional {
  font-weight: 500;
  color: var(--ink-muted);
  text-transform: none;
}

.textarea--short {
  min-height: 62px;
}

.switches {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(230px, 1fr));
  gap: 10px;
}

.switch {
  display: flex;
  align-items: flex-start;
  gap: 9px;
  padding: 10px 12px;
  border: 1px solid var(--hairline);
  border-radius: var(--radius-sm);
  background: var(--surface-sunken);
  cursor: pointer;
  transition: border-color 120ms ease;
}

.switch:hover {
  border-color: var(--baseline);
}

.switch input {
  position: absolute;
  opacity: 0;
  width: 0;
  height: 0;
}

.switch__box {
  width: 16px;
  height: 16px;
  margin-top: 2px;
  border-radius: 5px;
  border: 1.5px solid var(--baseline);
  background: var(--surface-raised);
  flex: none;
  position: relative;
  transition: background-color 120ms ease, border-color 120ms ease;
}

.switch input:checked + .switch__box {
  background: var(--accent);
  border-color: var(--accent);
}

.switch input:checked + .switch__box::after {
  content: '';
  position: absolute;
  left: 4.5px;
  top: 1px;
  width: 4px;
  height: 8px;
  border: solid #fff;
  border-width: 0 2px 2px 0;
  transform: rotate(45deg);
}

.switch input:focus-visible + .switch__box {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}

.switch strong {
  display: block;
  font-size: 12.5px;
  font-weight: 600;
}

.switch em {
  display: block;
  font-style: normal;
  font-size: 11.5px;
  color: var(--ink-muted);
}

.modal__foot {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  padding-top: 4px;
  border-top: 1px solid var(--hairline);
  margin-top: 2px;
  padding-top: 14px;
}

.modal-enter-active,
.modal-leave-active {
  transition: opacity 160ms ease;
}

.modal-enter-active .modal,
.modal-leave-active .modal {
  transition: transform 180ms cubic-bezier(0.22, 1, 0.36, 1);
}

.modal-enter-from,
.modal-leave-to {
  opacity: 0;
}

.modal-enter-from .modal,
.modal-leave-to .modal {
  transform: translateY(-12px) scale(0.99);
}

@media (prefers-reduced-motion: reduce) {
  .modal-enter-active,
  .modal-leave-active,
  .modal-enter-active .modal,
  .modal-leave-active .modal {
    transition: none;
  }
}
</style>
