// override ได้ด้วย VITE_LEGACY_ASSET_BASE_URL ใน web/.env.local (ไม่ commit)
export const legacyAssetBaseUrl = import.meta.env.VITE_LEGACY_ASSET_BASE_URL
  ?? 'https://media.garage-pro.net/'
