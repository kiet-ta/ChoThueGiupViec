import { Link } from 'react-router-dom'
import { 
  ShieldCheck, 
  ShieldAlert, 
  ShieldX, 
  Coins, 
  Award, 
  Crown, 
  Building2, 
  Users, 
  CalendarClock, 
  FileCheck2, 
  ArrowRight,
  Sparkles
} from 'lucide-react'
import { useResource } from '@/hooks/use-resource'
import { agenciesApi } from './api'
import { formatVnd } from '../../lib/format.ts'
import { StatusBadge } from '@/components/status'
import { tierLabel, isVerifiedPartner } from './view'

export function PartnerDashboardPage() {
  const escrowResource = useResource(() => agenciesApi.escrow(), [])
  const subResource = useResource(() => agenciesApi.subscription(), [])
  const bankResource = useResource(() => agenciesApi.bank(), [])

  const escrow = escrowResource.data
  const sub = subResource.data
  const bank = bankResource.data

  const balance = escrow?.escrowDepositBalance ?? 0
  const minBalance = escrow?.minEscrowBalance ?? 5000000
  const status = escrow?.agencyStatus ?? 'PENDING'
  const slaScore = escrow?.slaScore ?? 100
  const isSuspended = status === 'SUSPENDED'
  const isActive = status === 'ACTIVE'
  const isPending = status === 'PENDING'

  const balancePercent = Math.min(100, Math.round((balance / minBalance) * 100))

  return (
    <main className="w-full max-w-full pb-16 animate-in fade-in duration-300">
      {/* HERO SECTION */}
      <section className="relative overflow-hidden rounded-2xl bg-gradient-to-br from-[#1a2f25] via-[#15271f] to-[#0e1a14] p-8 text-white shadow-xl md:p-12 mb-10 border border-emerald-900/40">
        <div className="absolute -right-24 -top-24 h-96 w-96 rounded-full bg-emerald-500/10 blur-3xl pointer-events-none" />
        <div className="absolute right-1/4 -bottom-32 h-64 w-64 rounded-full bg-emerald-700/15 blur-2xl pointer-events-none" />

        <div className="relative z-10 max-w-5xl">
          <div className="flex flex-wrap items-center gap-3 mb-4">
            <span className="inline-flex items-center gap-1.5 rounded-full bg-emerald-500/20 px-3.5 py-1 text-xs font-semibold tracking-wide text-emerald-300 uppercase backdrop-blur-md border border-emerald-500/30">
              <Sparkles className="size-3.5" /> Cổng Đối Tác Doanh Nghiệp
            </span>

            {escrowResource.loading ? (
              <span className="inline-block h-6 w-36 animate-pulse rounded-full bg-emerald-500/20" />
            ) : (
              <>
                {isPending && (
                  <span className="inline-flex items-center gap-1.5 rounded-full bg-amber-500/20 px-3.5 py-1 text-xs font-semibold text-amber-300 border border-amber-500/30">
                    <ShieldAlert className="size-3.5" /> Chờ Kích Hoạt Ký Quỹ
                  </span>
                )}
                {isActive && (
                  <span className="inline-flex items-center gap-1.5 rounded-full bg-emerald-500/20 px-3.5 py-1 text-xs font-semibold text-emerald-200 border border-emerald-500/30">
                    <ShieldCheck className="size-3.5" /> Đã Kích Hoạt Vận Hành
                  </span>
                )}
                {isSuspended && (
                  <span className="inline-flex items-center gap-1.5 rounded-full bg-red-500/20 px-3.5 py-1 text-xs font-semibold text-red-300 border border-red-500/30">
                    <ShieldX className="size-3.5" /> Đình Chỉ Hoạt Động
                  </span>
                )}
              </>
            )}

            {subResource.loading ? (
              <span className="inline-block h-6 w-28 animate-pulse rounded-full bg-sky-500/20" />
            ) : (
              sub && isVerifiedPartner(sub) && (
                <span className="inline-flex items-center gap-1.5 rounded-full bg-sky-500/20 px-3 py-1 text-xs font-semibold text-sky-200 border border-sky-500/30">
                  <Crown className="size-3.5 text-sky-300" /> Verified Partner
                </span>
              )
            )}
          </div>

          <h1 className="text-3xl sm:text-4xl lg:text-5xl font-semibold tracking-tight leading-tight text-white max-w-4xl">
            Bảng điều khiển & Vận hành Doanh nghiệp
          </h1>

          <p className="mt-4 text-base sm:text-lg text-emerald-100/80 max-w-2xl font-light leading-relaxed">
            Hệ sinh thái kết nối đơn dịch vụ dọn dẹp cao cấp. Quản trị toàn diện ký quỹ trách nhiệm, lịch ca nhân sự và cam kết SLA theo thời gian thực.
          </p>

          <div className="mt-8 flex flex-wrap items-center gap-4">
            <Link
              to="escrow"
              className="inline-flex items-center gap-2 rounded-xl bg-emerald-500 px-5 py-3 text-sm font-semibold text-white shadow-lg shadow-emerald-950/40 transition-all duration-300 hover:bg-emerald-400 hover:shadow-emerald-900/60"
            >
              <Coins className="size-4" /> Nạp Quỹ Ký Quỹ
            </Link>
            <Link
              to="workers"
              className="inline-flex items-center gap-2 rounded-xl bg-white/10 px-5 py-3 text-sm font-semibold text-white backdrop-blur-md transition-all duration-300 hover:bg-white/20 border border-white/15"
            >
              <Users className="size-4" /> Quản Lý Đội Ngũ Thợ
            </Link>
            <Link
              to="jobs"
              className="inline-flex items-center gap-2 rounded-xl px-4 py-3 text-sm font-medium text-emerald-200 hover:text-white transition-colors"
            >
              Xem Đơn Hàng <ArrowRight className="size-4" />
            </Link>
          </div>
        </div>
      </section>

      {/* BENTO GRID: All cards rendered with 100% opacity & clean skeleton loading */}
      <section className="mb-12">
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-5">
          
          {/* CARD 1: ESCROW BOND (Span 2) */}
          <div className="group relative col-span-1 md:col-span-2 rounded-2xl border border-border bg-card p-6 shadow-sm transition-all duration-300 hover:shadow-md hover:border-emerald-600/40 opacity-100">
            <div className="flex items-start justify-between">
              <div className="flex-1">
                <span className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Ký Quỹ Trách Nhiệm</span>
                {escrowResource.loading ? (
                  <div className="mt-2 space-y-2">
                    <div className="h-9 w-48 animate-pulse rounded-lg bg-muted" />
                    <div className="h-4 w-60 animate-pulse rounded bg-muted/60" />
                  </div>
                ) : (
                  <>
                    <div className="mt-2 text-3xl font-bold tracking-tight tabular-nums text-foreground">
                      {formatVnd(balance)}
                    </div>
                    <div className="mt-1 text-xs text-muted-foreground">
                      Định mức vận hành tối thiểu: <span className="font-medium text-foreground">{formatVnd(minBalance)}</span>
                    </div>
                  </>
                )}
              </div>
              <div className="rounded-xl bg-emerald-50 p-3 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300">
                <Coins className="size-6" />
              </div>
            </div>

            {/* Progress Bar */}
            <div className="mt-6">
              <div className="flex items-center justify-between text-xs font-medium text-muted-foreground mb-1.5">
                <span>Trạng thái quỹ</span>
                {escrowResource.loading ? (
                  <span className="inline-block h-3 w-8 animate-pulse rounded bg-muted" />
                ) : (
                  <span>{balancePercent}%</span>
                )}
              </div>
              <div className="h-2 w-full overflow-hidden rounded-full bg-muted">
                <div 
                  className={`h-full transition-all duration-700 ease-out rounded-full ${
                    balance >= minBalance ? 'bg-emerald-600' : balance > 0 ? 'bg-amber-500' : 'bg-destructive'
                  }`}
                  style={{ width: `${balancePercent}%` }}
                />
              </div>
            </div>

            <div className="mt-6 flex items-center justify-between border-t border-border/60 pt-4">
              <span className="text-xs text-muted-foreground">
                {escrowResource.loading ? (
                  'Đang đồng bộ số dư ký quỹ…'
                ) : balance >= minBalance ? (
                  'Đạt chuẩn nhận đơn Premium'
                ) : (
                  'Cần nạp bổ sung để kích hoạt'
                )}
              </span>
              <Link 
                to="escrow"
                className="inline-flex items-center gap-1 text-xs font-semibold text-primary hover:underline"
              >
                Nạp tiền MoMo QR <ArrowRight className="size-3.5" />
              </Link>
            </div>
          </div>

          {/* CARD 2: SLA SCORE (Span 1) */}
          <div className="group col-span-1 rounded-2xl border border-border bg-card p-6 shadow-sm transition-all duration-300 hover:shadow-md hover:border-emerald-600/40 opacity-100">
            <div className="flex items-start justify-between">
              <div className="flex-1">
                <span className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Chỉ Số SLA</span>
                {escrowResource.loading ? (
                  <div className="mt-2 space-y-2">
                    <div className="h-9 w-28 animate-pulse rounded-lg bg-muted" />
                    <div className="h-4 w-36 animate-pulse rounded bg-muted/60" />
                  </div>
                ) : (
                  <>
                    <div className="mt-2 text-3xl font-bold tracking-tight tabular-nums text-foreground">
                      {slaScore.toFixed(1)} <span className="text-base font-normal text-muted-foreground">/ 100</span>
                    </div>
                    <div className="mt-1 text-xs text-muted-foreground">
                      {slaScore >= 70 ? 'Mức dịch vụ xuất sắc' : slaScore >= 50 ? 'Cảnh báo nguy cơ vi phạm' : 'Bị loại khỏi Premium'}
                    </div>
                  </>
                )}
              </div>
              <div className="rounded-xl bg-blue-50 p-3 text-blue-700 dark:bg-blue-950 dark:text-blue-300">
                <Award className="size-6" />
              </div>
            </div>

            <div className="mt-6 space-y-2 border-t border-border/60 pt-4 text-xs text-muted-foreground">
              <div className="flex justify-between items-center">
                <span>Bỏ ca (No-show):</span>
                <span className="font-semibold text-destructive">-20 điểm</span>
              </div>
              <div className="flex justify-between items-center">
                <span>Thiếu thợ (Shortage):</span>
                <span className="font-semibold text-destructive">-10 điểm</span>
              </div>
              <div className="flex justify-between items-center">
                <span>Khiếu nại (Quality):</span>
                <span className="font-semibold text-destructive">-5 điểm</span>
              </div>
            </div>

            <div className="mt-4 pt-3 border-t border-border/40">
              <Link to="escrow" className="inline-flex items-center gap-1 text-xs font-semibold text-primary hover:underline">
                Chi tiết & Kháng cáo <ArrowRight className="size-3.5" />
              </Link>
            </div>
          </div>

          {/* CARD 3: SUBSCRIPTION PACKAGE (Span 1) */}
          <div className="group col-span-1 rounded-2xl border border-border bg-card p-6 shadow-sm transition-all duration-300 hover:shadow-md hover:border-emerald-600/40 opacity-100">
            <div className="flex items-start justify-between">
              <div className="flex-1">
                <span className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Gói Thuê Bao</span>
                {subResource.loading ? (
                  <div className="mt-2 space-y-2">
                    <div className="h-7 w-24 animate-pulse rounded-lg bg-muted" />
                    <div className="h-4 w-32 animate-pulse rounded bg-muted/60" />
                  </div>
                ) : (
                  <>
                    <div className="mt-2 flex items-center gap-2">
                      <StatusBadge tone={sub?.tier === 'PRO' ? 'accent' : 'neutral'}>
                        {sub ? tierLabel(sub.tier) : 'FREE'}
                      </StatusBadge>
                      {sub?.tier === 'PRO' && <Crown className="size-4 text-amber-500" />}
                    </div>
                    <div className="mt-2 text-xs text-muted-foreground">
                      Hạn mức: <strong className="text-foreground">{sub?.workerCount ?? 0}</strong> / {sub?.workerQuota ?? 3} thợ
                    </div>
                  </>
                )}
              </div>
              <div className="rounded-xl bg-amber-50 p-3 text-amber-700 dark:bg-amber-950 dark:text-amber-300">
                <Crown className="size-6" />
              </div>
            </div>

            <div className="mt-6 border-t border-border/60 pt-4 text-xs text-muted-foreground space-y-2">
              <div className="flex justify-between items-center">
                <span>Hoa hồng sàn:</span>
                <span className="font-semibold text-foreground">
                  {subResource.loading ? '…' : sub?.tier === 'PRO' ? '0%' : '20%'}
                </span>
              </div>
              <div className="flex justify-between items-center">
                <span>Báo cáo hiệu suất:</span>
                <span className="font-semibold text-foreground">
                  {subResource.loading ? '…' : sub?.hasAnalytics ? 'Mở khóa' : 'Khóa'}
                </span>
              </div>
            </div>

            <div className="mt-4 pt-3 border-t border-border/40">
              <Link to="subscription" className="inline-flex items-center gap-1 text-xs font-semibold text-primary hover:underline">
                {sub?.tier === 'PRO' ? 'Quản lý gói Pro' : 'Nâng cấp gói Pro'} <ArrowRight className="size-3.5" />
              </Link>
            </div>
          </div>

          {/* CARD 4: PAYOUT BANK ACCOUNT (Span 2) */}
          <div className="col-span-1 md:col-span-2 rounded-2xl border border-border bg-card p-6 shadow-sm opacity-100">
            <div className="flex items-center gap-3 mb-4">
              <div className="rounded-xl bg-purple-50 p-2.5 text-purple-700 dark:bg-purple-950 dark:text-purple-300">
                <Building2 className="size-5" />
              </div>
              <div>
                <h2 className="text-sm font-semibold text-foreground">Tài Khoản Quyết Toán Doanh Nghiệp</h2>
                <p className="text-xs text-muted-foreground">Nơi nhận thanh toán doanh thu hoàn tất ca làm cuối tháng</p>
              </div>
            </div>

            {bankResource.loading ? (
              <div className="h-20 w-full animate-pulse rounded-xl bg-muted" />
            ) : bank && bank.isSet ? (
              <div className="rounded-xl bg-muted/50 p-4 border border-border/80 flex flex-wrap items-center justify-between gap-4">
                <div>
                  <div className="text-xs text-muted-foreground uppercase font-medium">Ngân hàng thụ hưởng</div>
                  <div className="text-base font-semibold text-foreground mt-0.5">{bank.bankName}</div>
                  <div className="text-sm font-mono text-muted-foreground mt-0.5">{bank.bankAccountNo}</div>
                </div>
                <Link to="bank" className="text-xs font-semibold text-primary hover:underline">
                  Thay đổi STK
                </Link>
              </div>
            ) : (
              <div className="rounded-xl bg-amber-500/10 p-4 border border-amber-500/20 flex flex-wrap items-center justify-between gap-4">
                <div className="text-xs text-amber-800 dark:text-amber-200">
                  Chưa khai báo tài khoản ngân hàng doanh nghiệp. Hãy cập nhật để tránh chậm trễ chi trả Payout cuối kỳ.
                </div>
                <Link to="bank" className="text-xs font-bold text-amber-900 dark:text-amber-100 underline">
                  Khai báo ngay
                </Link>
              </div>
            )}
          </div>

          {/* CARD 5: OPERATIONS & ROSTER (Span 2) */}
          <div className="col-span-1 md:col-span-2 rounded-2xl border border-border bg-card p-6 shadow-sm opacity-100">
            <div className="flex items-center gap-3 mb-4">
              <div className="rounded-xl bg-emerald-50 p-2.5 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300">
                <CalendarClock className="size-5" />
              </div>
              <div>
                <h2 className="text-sm font-semibold text-foreground">Lịch Trực Ca & Nhận Đơn Premium</h2>
                <p className="text-xs text-muted-foreground">Phân bổ ca trực theo tuần để đón luồng đơn đặt trước tự động</p>
              </div>
            </div>

            <div className="grid grid-cols-2 gap-3 text-xs">
              <div className="rounded-xl border border-border/80 bg-muted/30 p-3.5">
                <div className="text-muted-foreground font-medium">Khung giờ phân ca</div>
                <div className="text-foreground font-semibold mt-1">Sáng (08-12h) • Chiều (13-17h) • Tối (17:30-20:30)</div>
              </div>
              <div className="rounded-xl border border-border/80 bg-muted/30 p-3.5">
                <div className="text-muted-foreground font-medium">Cơ chế điều phối</div>
                <div className="text-foreground font-semibold mt-1">Khóa công suất nguyên tử (Atomic Check)</div>
              </div>
            </div>

            <div className="mt-4 flex items-center justify-between pt-3 border-t border-border/60">
              <span className="text-xs text-muted-foreground">Cập nhật lịch trước thứ Hai hàng tuần</span>
              <Link to="roster" className="text-xs font-semibold text-primary hover:underline">
                Mở bảng phân ca <ArrowRight className="size-3.5 inline ml-0.5" />
              </Link>
            </div>
          </div>

        </div>
      </section>

      {/* OPERATIONAL HUB ROADMAP */}
      <section className="rounded-2xl border border-border bg-card p-6 md:p-8 shadow-sm opacity-100">
        <div className="max-w-2xl mb-6">
          <span className="text-xs font-semibold uppercase tracking-wider text-emerald-600 dark:text-emerald-400">Quy Trình Chuẩn Hóa</span>
          <h2 className="text-xl md:text-2xl font-bold tracking-tight text-foreground mt-1">
            4 Bước Kích Hoạt & Vận Hành Doanh Nghiệp
          </h2>
          <p className="text-sm text-muted-foreground mt-1">
            Hoàn thiện tuần tự các bước dưới đây để doanh nghiệp sẵn sàng tiếp nhận các hợp đồng dịch vụ cao cấp.
          </p>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
          
          <Link
            to="escrow"
            className="group rounded-xl border border-border/80 bg-background p-5 transition-all duration-300 hover:border-emerald-600/50 hover:shadow-md hover:-translate-y-1"
          >
            <div className="flex items-center justify-between mb-3">
              <span className="flex size-7 items-center justify-center rounded-lg bg-emerald-100 dark:bg-emerald-950 font-bold text-xs text-emerald-800 dark:text-emerald-300">
                01
              </span>
              <Coins className="size-4 text-muted-foreground group-hover:text-emerald-600 transition-colors" />
            </div>
            <h3 className="text-sm font-semibold text-foreground group-hover:text-emerald-700 dark:group-hover:text-emerald-300 transition-colors">
              Ký Quỹ Trách Nhiệm
            </h3>
            <p className="text-xs text-muted-foreground mt-1.5 leading-relaxed">
              Nạp tối thiểu 5.000.000 VNĐ qua MoMo QR để chuyển tài khoản sang trạng thái ACTIVE.
            </p>
          </Link>

          <Link
            to="workers"
            className="group rounded-xl border border-border/80 bg-background p-5 transition-all duration-300 hover:border-emerald-600/50 hover:shadow-md hover:-translate-y-1"
          >
            <div className="flex items-center justify-between mb-3">
              <span className="flex size-7 items-center justify-center rounded-lg bg-emerald-100 dark:bg-emerald-950 font-bold text-xs text-emerald-800 dark:text-emerald-300">
                02
              </span>
              <FileCheck2 className="size-4 text-muted-foreground group-hover:text-emerald-600 transition-colors" />
            </div>
            <h3 className="text-sm font-semibold text-foreground group-hover:text-emerald-700 dark:group-hover:text-emerald-300 transition-colors">
              Cam Kết Bảo Lãnh
            </h3>
            <p className="text-xs text-muted-foreground mt-1.5 leading-relaxed">
              Đại diện ký đóng dấu mẫu cam kết A4 và nộp file PDF lên hệ thống trước khi thêm thợ.
            </p>
          </Link>

          <Link
            to="workers"
            className="group rounded-xl border border-border/80 bg-background p-5 transition-all duration-300 hover:border-emerald-600/50 hover:shadow-md hover:-translate-y-1"
          >
            <div className="flex items-center justify-between mb-3">
              <span className="flex size-7 items-center justify-center rounded-lg bg-emerald-100 dark:bg-emerald-950 font-bold text-xs text-emerald-800 dark:text-emerald-300">
                03
              </span>
              <Users className="size-4 text-muted-foreground group-hover:text-emerald-600 transition-colors" />
            </div>
            <h3 className="text-sm font-semibold text-foreground group-hover:text-emerald-700 dark:group-hover:text-emerald-300 transition-colors">
              Import Thợ Cơ Hữu
            </h3>
            <p className="text-xs text-muted-foreground mt-1.5 leading-relaxed">
              Nhập danh sách nhân viên từ file Excel (.xlsx), miễn eKYC cá nhân vì đã có bảo lãnh.
            </p>
          </Link>

          <Link
            to="roster"
            className="group rounded-xl border border-border/80 bg-background p-5 transition-all duration-300 hover:border-emerald-600/50 hover:shadow-md hover:-translate-y-1"
          >
            <div className="flex items-center justify-between mb-3">
              <span className="flex size-7 items-center justify-center rounded-lg bg-emerald-100 dark:bg-emerald-950 font-bold text-xs text-emerald-800 dark:text-emerald-300">
                04
              </span>
              <CalendarClock className="size-4 text-muted-foreground group-hover:text-emerald-600 transition-colors" />
            </div>
            <h3 className="text-sm font-semibold text-foreground group-hover:text-emerald-700 dark:group-hover:text-emerald-300 transition-colors">
              Lên Lịch Trực Ca
            </h3>
            <p className="text-xs text-muted-foreground mt-1.5 leading-relaxed">
              Gán thợ vào các Block Slots trong tuần để hệ thống điều phối đơn Premium tự động.
            </p>
          </Link>

        </div>
      </section>
    </main>
  )
}
