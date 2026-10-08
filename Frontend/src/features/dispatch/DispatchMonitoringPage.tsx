import { useEffect, useState, useMemo } from 'react'
import { DataTable, type Column } from '@/components/data-table/DataTable'
import { Button } from '@/components/ui/button'
import { fetchAdminIncidents } from './api'
import {
  type IncidentLogDto,
  formatIncidentType,
  formatReDispatchStatus,
} from './types'

export function DispatchMonitoringPage() {
  const [incidents, setIncidents] = useState<IncidentLogDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [filterType, setFilterType] = useState<string>('ALL')
  const [filterStatus, setFilterStatus] = useState<string>('ALL')
  const [selectedIncident, setSelectedIncident] = useState<IncidentLogDto | null>(null)

  const loadData = async () => {
    setLoading(true)
    setError(null)
    try {
      const data = await fetchAdminIncidents()
      setIncidents(data)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Lỗi khi tải dữ liệu sự cố')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void (async () => {
      try {
        const data = await fetchAdminIncidents()
        setIncidents(data)
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Lỗi khi tải dữ liệu sự cố')
      } finally {
        setLoading(false)
      }
    })()
  }, [])

  const filteredIncidents = useMemo(() => {
    return incidents.filter((item) => {
      if (filterType !== 'ALL' && item.incidentType !== filterType) return false
      if (filterStatus !== 'ALL' && item.reDispatchStatus !== filterStatus) return false
      return true
    })
  }, [incidents, filterType, filterStatus])

  const columns: Column<IncidentLogDto>[] = [
    {
      id: 'incidentId',
      header: 'Mã Sự Cố',
      cell: (row) => <span className="font-mono font-medium">#{row.incidentId}</span>,
    },
    {
      id: 'assignmentId',
      header: 'Ca Làm',
      cell: (row) => <span className="font-mono text-muted-foreground">#{row.assignmentId}</span>,
    },
    {
      id: 'workerId',
      header: 'Thợ',
      cell: (row) => <span>Thợ #{row.workerId}</span>,
    },
    {
      id: 'incidentType',
      header: 'Loại Sự Cố',
      cell: (row) => (
        <span className="inline-flex items-center rounded-md bg-muted px-2 py-1 text-xs font-medium">
          {formatIncidentType(row.incidentType)}
        </span>
      ),
    },
    {
      id: 'description',
      header: 'Mô Tả',
      cell: (row) => (
        <span className="line-clamp-2 max-w-xs text-sm" title={row.description}>
          {row.description}
        </span>
      ),
    },
    {
      id: 'penaltyExemption',
      header: 'Miễn Phạt (BR-10)',
      cell: (row) => (
        <span
          className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-semibold ${
            row.isPenaltyExempt
              ? 'bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-400'
              : 'bg-red-100 text-red-800'
          }`}
        >
          {row.isPenaltyExempt ? 'Miễn phạt 100%' : 'Có phạt'}
        </span>
      ),
    },
    {
      id: 'reDispatchStatus',
      header: 'Điều Phối Lại',
      cell: (row) => {
        const info = formatReDispatchStatus(row.reDispatchStatus)
        const colorClasses = {
          warning: 'bg-amber-100 text-amber-800 dark:bg-amber-900/30 dark:text-amber-400',
          success: 'bg-emerald-100 text-emerald-800 dark:bg-emerald-900/30 dark:text-emerald-400',
          destructive: 'bg-rose-100 text-rose-800 dark:bg-rose-900/30 dark:text-rose-400',
          secondary: 'bg-slate-100 text-slate-800 dark:bg-slate-800 dark:text-slate-200',
        }[info.variant]

        return (
          <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${colorClasses}`}>
            {info.label}
          </span>
        )
      },
    },
    {
      id: 'actions',
      header: 'Chi Tiết',
      align: 'right',
      cell: (row) => (
        <Button
          variant="outline"
          size="sm"
          onClick={() => setSelectedIncident(row)}
          className="text-xs"
        >
          Xem bằng chứng
        </Button>
      ),
    },
  ]

  return (
    <div className="space-y-6 p-6">
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Giám Sát Điều Phối & Sự Cố</h1>
          <p className="text-sm text-muted-foreground">
            Bảng điều hành thời gian thực về điều phối và sự cố bất khả kháng hiện trường (BR-10).
          </p>
        </div>
        <Button onClick={loadData} variant="outline" size="sm" disabled={loading}>
          Làm mới
        </Button>
      </div>

      <div className="flex flex-wrap items-center gap-4 rounded-lg border bg-card p-4">
        <div className="flex items-center gap-2">
          <label htmlFor="filter-type" className="text-xs font-medium text-muted-foreground">
            Loại sự cố:
          </label>
          <select
            id="filter-type"
            value={filterType}
            onChange={(e) => setFilterType(e.target.value)}
            className="rounded border border-input bg-background px-3 py-1 text-sm shadow-sm"
          >
            <option value="ALL">Tất cả</option>
            <option value="ACCIDENT">Tai nạn / Hỏng xe</option>
            <option value="HEALTH">Sức khỏe</option>
            <option value="SEVERE_WEATHER">Thiên tai</option>
            <option value="OTHER">Khác</option>
          </select>
        </div>

        <div className="flex items-center gap-2">
          <label htmlFor="filter-status" className="text-xs font-medium text-muted-foreground">
            Trạng thái điều phối:
          </label>
          <select
            id="filter-status"
            value={filterStatus}
            onChange={(e) => setFilterStatus(e.target.value)}
            className="rounded border border-input bg-background px-3 py-1 text-sm shadow-sm"
          >
            <option value="ALL">Tất cả</option>
            <option value="SEARCHING">Đang tìm thợ</option>
            <option value="REASSIGNED">Đã đổi thợ</option>
            <option value="FAILED">Hủy ca</option>
          </select>
        </div>
      </div>

      <DataTable
        columns={columns}
        rows={filteredIncidents}
        rowKey={(row) => row.incidentId}
        loading={loading}
        error={error}
        onRetry={loadData}
        emptyMessage="Hiện không có sự cố nào trên hiện trường"
      />

      {selectedIncident && (
        <div
          role="dialog"
          aria-modal="true"
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
        >
          <div className="w-full max-w-lg rounded-xl border bg-card p-6 shadow-xl">
            <div className="flex items-center justify-between border-b pb-3">
              <h3 className="text-lg font-bold">
                Chi tiết sự cố #{selectedIncident.incidentId}
              </h3>
              <Button
                variant="ghost"
                size="sm"
                onClick={() => setSelectedIncident(null)}
              >
                ✕
              </Button>
            </div>

            <div className="mt-4 space-y-4">
              <div>
                <p className="text-xs text-muted-foreground">Mô tả sự cố:</p>
                <p className="mt-1 text-sm">{selectedIncident.description}</p>
              </div>

              <div className="grid grid-cols-2 gap-2 text-xs">
                <div>
                  <span className="text-muted-foreground">Toạ độ GPS:</span>
                  <p className="font-mono mt-0.5">
                    {selectedIncident.latitude.toFixed(4)}, {selectedIncident.longitude.toFixed(4)}
                  </p>
                </div>
                <div>
                  <span className="text-muted-foreground">Thời gian báo:</span>
                  <p className="mt-0.5">{new Date(selectedIncident.reportedAt).toLocaleString('vi-VN')}</p>
                </div>
              </div>

              {selectedIncident.photoEvidenceUrl && (
                <div>
                  <p className="text-xs text-muted-foreground mb-1">Ảnh bằng chứng hiện trường:</p>
                  <img
                    src={selectedIncident.photoEvidenceUrl}
                    alt="Bằng chứng hiện trường"
                    className="max-h-60 w-full rounded-md object-cover border"
                  />
                </div>
              )}

              <div className="rounded bg-muted p-3 text-xs">
                <p className="font-medium">Chính sách bảo vệ:</p>
                <p className="text-muted-foreground mt-0.5">
                  Thợ được miễn phạt 100% theo điều khoản BR-10 do có đầy đủ bằng chứng ảnh và toạ độ GPS hợp lệ.
                </p>
              </div>
            </div>

            <div className="mt-6 flex justify-end">
              <Button onClick={() => setSelectedIncident(null)}>Đóng</Button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
