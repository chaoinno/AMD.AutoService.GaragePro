const scripts = new Map<string, Promise<void>>()

/** Called only from effects. Keep one script even under React StrictMode's effect replay. */
export function loadExternalScript(src: string): Promise<void> {
  const existing = scripts.get(src)
  if (existing) return existing
  const promise = new Promise<void>((resolve, reject) => {
    const script = document.createElement('script')
    script.src = src
    script.async = true
    const timeout = window.setTimeout(() => fail(), 20_000)
    function fail() {
      window.clearTimeout(timeout)
      script.remove()
      scripts.delete(src)
      reject(new Error('โหลดบริการยืนยันตัวตนไม่สำเร็จ'))
    }
    script.onload = () => { window.clearTimeout(timeout); resolve() }
    script.onerror = fail
    document.head.appendChild(script)
  })
  scripts.set(src, promise)
  return promise
}
