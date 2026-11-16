export interface OtpRequestResponse {
  expiresInSeconds: number
  resendAvailableInSeconds: number
  sandboxOtp?: string | null
}

/** Extracts a non-empty sandbox OTP returned only in Development when FakeOtpSender is active. */
export function extractSandboxOtp(
  response: Pick<OtpRequestResponse, 'sandboxOtp'> | null | undefined,
): string | null {
  const raw = typeof response?.sandboxOtp === 'string' ? response.sandboxOtp.trim() : ''
  return raw.length > 0 ? raw : null
}
