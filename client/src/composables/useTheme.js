import { ref, watch } from 'vue'

const STORAGE_KEY = 'bugtracker.theme'

function readStoredTheme() {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    return stored === 'light' || stored === 'dark' ? stored : 'system'
  } catch {
    // Private windows and blocked site data throw on access.
    return 'system'
  }
}

const theme = ref(readStoredTheme())

function apply(value) {
  // "system" removes the stamp so the prefers-color-scheme rules take over.
  if (value === 'system') {
    document.documentElement.removeAttribute('data-theme')
  } else {
    document.documentElement.setAttribute('data-theme', value)
  }

  try {
    if (value === 'system') {
      localStorage.removeItem(STORAGE_KEY)
    } else {
      localStorage.setItem(STORAGE_KEY, value)
    }
  } catch {
    // Persisting the preference is a convenience, never a requirement.
  }
}

apply(theme.value)
watch(theme, apply)

export function useTheme() {
  const prefersDark = () =>
    typeof window !== 'undefined' &&
    window.matchMedia?.('(prefers-color-scheme: dark)').matches

  const isDark = () => (theme.value === 'system' ? prefersDark() : theme.value === 'dark')

  return {
    theme,
    isDark,
    toggle() {
      theme.value = isDark() ? 'light' : 'dark'
    }
  }
}
