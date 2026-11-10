import { authController } from '@/auth'
import { ApiError } from '@/services/api'
import { exportErrorMessage, toQuery } from '../ledger/view'
import { parseFileName } from '../payouts/view'
import type { LedgerFilters } from '../ledger/types'

const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api/v1'

/**
 * Downloads the agency's own statement (`GET /agencies/me/ledger/export`, contract ledger.md section 9). The success answer is the
 * `.xlsx` itself, not the JSON envelope, so `apiClient` cannot be used: it is fetched with the Partner's token as a blob.
 */
export async function downloadAgencyStatement(filters: LedgerFilters): Promise<string> {
  const token = authController.getAccessToken()
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(toQuery({ ...filters, partyType: '', partyId: '' }))) {
    if (value !== undefined) params.set(key, String(value))
  }

  let res: Response
  try {
    res = await fetch(`${BASE_URL}/agencies/me/ledger/export?${params.toString()}`, { headers: token ? { Authorization: `Bearer ${token}` } : {} })
  } catch {
    throw new ApiError(0, 'Không kết nối được máy chủ.')
  }

  if (!res.ok) {
    if (res.status === 401 && token) authController.handleUnauthorized()
    throw new ApiError(res.status, exportErrorMessage(res.status, await res.text().catch(() => '')))
  }

  const fileName = parseFileName(res.headers.get('Content-Disposition'), `ledger-agency-${filters.from || 'start'}_${filters.to || 'now'}.xlsx`)
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
