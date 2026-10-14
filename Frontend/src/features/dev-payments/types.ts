/** An open (PENDING) sandbox payment of the Fake gateway, as GET /api/dev/payments lists it (Development only). */
export interface DevPayment {
  paymentId: number
  /** ORDER | EXTENSION | SUBSCRIPTION | ESCROWDEPOSIT */
  purpose: string
  orderId: number | null
  subscriptionId: number | null
  agencyId: number | null
  amount: number
  gatewayTxnRef: string
  createdAt: string
}

export interface DevConfirmResult {
  /** CONFIRMED | ALREADY_PROCESSED */
  result: string
}
