import { PartnerDashboardPage } from '@/features/agencies/PartnerDashboardPage'

export function AreaHomePage({ title }: { title: string }) {
  if (title === 'Partner Portal') {
    return <PartnerDashboardPage />
  }
  return <h1 className="text-2xl font-semibold">{title}</h1>
}

