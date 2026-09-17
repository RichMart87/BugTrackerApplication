import { ref } from 'vue'

const toasts = ref([])
let nextId = 1

function push(message, tone, timeout) {
  const id = nextId++
  toasts.value = [...toasts.value, { id, message, tone }]

  if (timeout > 0) {
    setTimeout(() => dismiss(id), timeout)
  }

  return id
}

function dismiss(id) {
  toasts.value = toasts.value.filter((toast) => toast.id !== id)
}

export function useToasts() {
  return {
    toasts,
    dismiss,
    success: (message) => push(message, 'success', 4000),
    // Failures stay put for longer - they usually need to be read, not glimpsed.
    error: (message) => push(message, 'error', 8000),
    info: (message) => push(message, 'info', 4000)
  }
}
