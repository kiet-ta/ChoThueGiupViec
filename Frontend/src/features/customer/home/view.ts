import type { FavoriteWorker } from '../account/types.ts'
import { formatVnd } from '../../../lib/format.ts'
import type { OrderSummary, PriceQuote, ShiftOption } from '../booking/types.ts'

/** Use the Vietnam calendar month and count worker-hours for known shifts. */
export function monthStats(orders: OrderSummary[], shifts: ShiftOption[], now: Date): { count: number; hours: number } {
  const month = new Date(now.getTime() + 7 * 60 * 60 * 1000).toISOString().slice(0, 7)
  const current = orders.filter((order) => order.orderStatus === 'COMPLETED' && order.scheduledDate.slice(0, 7) === month)
  const minutes = (time: string) => {
    const match = /^(\d{2}):(\d{2})/.exec(time)
    return match ? Number(match[1]) * 60 + Number(match[2]) : null
  }
  const hours = current.reduce((sum, order) => {
    const shift = shifts.find((item) => item.shiftCode === order.shiftCode)
    if (!shift) return sum
    const start = minutes(shift.startLocal), end = minutes(shift.endLocal)
    if (start === null || end === null) return sum
    return sum + ((end < start ? end + 1440 : end) - start) / 60 * order.requiredWorkers
  }, 0)
  return { count: current.length, hours }
}

export function topFavorites(list: FavoriteWorker[], n: number): FavoriteWorker[] {
  return [...list].sort((a, b) => b.ratingAvg - a.ratingAvg || b.completedJobs - a.completedJobs).slice(0, n)
}

/**
 * HOME-PRICE-01 (#940): what a tier row of the home shows. Since D36 an ECONOMY house over the standard area is one worker priced as 2 units
 * (`isContinuousWork`, `unitPrice` 260000 but `totalAmount` 520000): the customer pays the total, so the home shows it.
 */
export const homeTierPrice = (quote: Pick<PriceQuote, 'unitPrice' | 'totalAmount' | 'isContinuousWork'>) =>
  formatVnd(quote.isContinuousWork ? quote.totalAmount : quote.unitPrice)

export const homeTierCaption = (quote: Pick<PriceQuote, 'isContinuousWork'>) =>
  quote.isContinuousWork ? 'trọn gói · 1 thợ, có thể làm thêm giờ' : 'mỗi thợ / ca'
