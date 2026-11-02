// `Payment` of .spec/contracts/payments.md 1.3.
export interface Payment {
  paymentId: number
  purpose: string
  orderId: number | null
  extensionId: number | null
  gateway: string
  amount: number
  txnStatus: string
  qrPayload: string | null
  payUrl: string | null
  expiresAt: string
  paidAt: string | null
  createdAt: string
  sandbox: boolean
}
