import { authController } from '@/auth'
import { ApiError } from '@/services/api'
import { parseFileName } from '../payouts/view'
import { exportErrorMessage, exportFallbackName, toQuery } from './view'
import type { LedgerFilters } from './types'

const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api/v1'

/**
 * Downloads the statement (contract ledger.md section 9). The success answer is the `.xlsx` itself, not the JSON envelope, so
 * `apiClient` cannot be used: it is fetched with the Admin's token as a blob and saved under the name the server gave it.
 */
export async function downloadStatement(filters: LedgerFilters): Promise<string> {
  const token = authController.getAccessToken()
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(toQuery(filters))) {
    if (value !== undefined) params.set(key, String(value))
  }

  let res: Response
  try {
    res = await fetch(`${BASE_URL}/admin/ledger/export?${params.toString()}`, { headers: token ? { Authorization: `Bearer ${token}` } : {} })
  } catch {
    throw new ApiError(0, 'Không kết nối được máy chủ.')
  }

  if (!res.ok) {
    if (res.status === 401 && token) authController.handleUnauthorized()
    throw new ApiError(res.status, exportErrorMessage(res.status, await res.text().catch(() => '')))
  }

  const fileName = parseFileName(res.headers.get('Content-Disposition'), exportFallbackName(filters))
  const url = URL.createObjectURL(await res.blob())
  try {
    const a = document.createElement('a')
    a.href = url
    a.download = fileName
    a.click()
  } finally {
    URL.revokeObjectURL(url)
  }
  return fileName
}
