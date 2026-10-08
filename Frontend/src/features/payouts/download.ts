import { authController } from '@/auth'
import { ApiError } from '@/services/api'
import { fileErrorMessage, parseFileName } from './view'
import type { ExportType } from './types'

const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api'

/**
 * Downloads one bank transfer file (contract payouts.md 2.3). The success answer is the `.xlsx` itself, not the JSON envelope, so
 * `apiClient` cannot be used: it is fetched with the Admin's token as a blob and saved under the name the server gave it.
 */
export async function downloadExport(batchId: number, type: ExportType, fallbackName: string): Promise<string> {
  const token = authController.getAccessToken()
  let res: Response
  try {
    res = await fetch(`${BASE_URL}/admin/payout-batches/${batchId}/export?type=${encodeURIComponent(type)}`, {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    })
  } catch {
    throw new ApiError(0, 'Không kết nối được máy chủ.')
  }

  if (!res.ok) {
    if (res.status === 401 && token) authController.handleUnauthorized()
    throw new ApiError(res.status, fileErrorMessage(res.status, await res.text().catch(() => '')))
  }

  const fileName = parseFileName(res.headers.get('Content-Disposition'), fallbackName)
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
