(() => {
  const key = 'troly-appearance'
  const valid = new Set(['system', 'light', 'dark'])
  let mode = 'system'
  try {
    const saved = localStorage.getItem(key)
    if (valid.has(saved)) mode = saved
  } catch { /* System appearance works even when storage is unavailable. */ }
  const apply = (value) => {
    if (value === 'system') delete document.documentElement.dataset.theme
    else document.documentElement.dataset.theme = value
  }
  apply(mode)
  document.addEventListener('DOMContentLoaded', () => {
    const select = document.getElementById('appearanceMode')
    if (!select) return
    select.value = mode
    select.addEventListener('change', () => {
      if (!valid.has(select.value)) return
      mode = select.value
      apply(mode)
      try { localStorage.setItem(key, mode) } catch { /* Session choice still applies. */ }
    })
  })
})()
