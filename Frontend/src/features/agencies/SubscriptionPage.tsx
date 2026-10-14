import { useState } from 'react'
import { StatusBadge } from '@/components/status'
import { Button } from '@/components/ui/button'
import { useResource } from '@/hooks/use-resource'
import { agenciesApi } from './api'
import { useAction } from './actions'
import { Alert, PageHeader, PayBox, Panel } from './shared'
import type { SubscriptionPurchase } from './types'
import { commissionText, expiryWarning, featureList, isVerifiedPartner, priceText, quotaText, subscriptionStatusText, tierLabel } from './view'

/** WEB-M5-01: package, worker quota and buying or renewing a package (contract agencies.md, decisions Q08, D9). */
export function SubscriptionPage() {
  const state = useResource(() => agenciesApi.subscription(), [])
  const packages = useResource(() => agenciesApi.packages(), [])
  const [purchase, setPurchase] = useState<SubscriptionPurchase | null>(null)
  const action = useAction()
  // Today in Vietnam (UTC+7), read once per visit.
  const [today] = useState(() => new Date(Date.now() + 7 * 3_600_000).toISOString().slice(0, 10))

  async function buy(packageId: number) {
    const result = await action.run(() => agenciesApi.subscribe(packageId))
    if (result) setPurchase(result)
  }

  const current = state.data
  const quota = current ? quotaText(current) : null
  const warning = current ? expiryWarning(current, today) : null

  return (
    <div className="flex flex-col gap-6">
      <PageHeader eyebrow="Đối tác · Gói thuê bao" title="Gói thuê bao và hạn mức thợ">
        Gói Free tối đa 3 thợ, hoa hồng 20%. Gói Pro mở bảng phân ca, báo cáo hiệu suất và ưu tiên điều phối. Hết hạn Pro có 7 ngày gia hạn,
        sau đó thợ vượt hạn mức bị khoá (không bị xoá). Gói không tự gia hạn; hệ thống nhắc trước 7 ngày và 1 ngày.
      </PageHeader>

      {state.error && <Alert>{state.error}</Alert>}
      {warning && <Alert>{warning}</Alert>}
      {current && quota && (
        <Panel title="Gói hiện tại">
          <div className="flex flex-wrap items-center gap-3">
            <StatusBadge tone={current.tier === 'PRO' ? 'accent' : 'neutral'}>{tierLabel(current.tier)}</StatusBadge>
            {isVerifiedPartner(current) && <StatusBadge tone="accent">Verified Partner</StatusBadge>}
            <span className="text-sm">{subscriptionStatusText(current)}</span>
          </div>
          <div className="mt-3 text-sm">
            Số thợ: <span className="font-semibold tabular-nums">{quota.text}</span>
            {quota.full && <span className="ml-2 text-destructive">Đã đủ hạn mức, nâng cấp gói để thêm thợ.</span>}
          </div>
          <ul className="mt-2 list-disc pl-5 text-sm text-muted-foreground">
            {featureList(current.package).length === 0 ? <li>Chưa có tính năng Pro</li> : featureList(current.package).map((f) => <li key={f}>{f}</li>)}
          </ul>
        </Panel>
      )}

      <Panel title="Các gói">
        {packages.error && <Alert>{packages.error}</Alert>}
        <div className="grid gap-4 md:grid-cols-3">
          {(packages.data ?? []).map((p) => (
            <article key={p.packageId} className="flex flex-col gap-2 rounded-lg border border-border p-4">
              <div className="flex items-center justify-between">
                <h3 className="font-semibold">{p.packageName}</h3>
                <StatusBadge tone={p.tier === 'PRO' ? 'accent' : 'neutral'}>{tierLabel(p.tier)}</StatusBadge>
              </div>
              <div className="text-lg font-semibold tabular-nums">{priceText(p)}</div>
              <div className="text-sm text-muted-foreground">
                Tối đa {p.workerQuota} thợ · hoa hồng {commissionText(p.commissionRate)}
              </div>
              <ul className="list-disc pl-5 text-sm">
                {featureList(p).map((f) => (
                  <li key={f}>{f}</li>
                ))}
              </ul>
              {p.price > 0 && (
                <Button size="sm" className="mt-auto" disabled={action.busy} onClick={() => void buy(p.packageId)}>
                  {action.busy ? 'Đang tạo mã…' : current?.subscription ? 'Gia hạn' : 'Mua gói'}
                </Button>
              )}
            </article>
          ))}
        </div>
        {action.error && <div className="mt-3"><Alert>{action.error}</Alert></div>}
      </Panel>

      {purchase && (
        <Panel title="Thanh toán gói">
          <p className="mb-2 text-sm">
            Số tiền: <span className="font-semibold tabular-nums">{purchase.amount.toLocaleString('vi-VN')} đ</span>. Gói được kích hoạt ngay khi thanh toán thành công.
          </p>
          <PayBox qrPayload={purchase.qrPayload} payUrl={purchase.payUrl} expiresAt={purchase.expiresAt} />
          <div className="mt-3">
            <Button variant="ghost" size="sm" onClick={() => { setPurchase(null); state.reload() }}>
              Tôi đã thanh toán, tải lại
            </Button>
          </div>
        </Panel>
      )}
    </div>
  )
}
