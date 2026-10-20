import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import { buildGuaranteeHtml, guaranteeProblems, type AgencyGuaranteeForm } from '../src/features/agencies/guarantee.ts'

const valid: AgencyGuaranteeForm = {
  legalName: 'Công ty Ánh Dương',
  taxCode: '0312345678-001',
  legalRepresentative: 'Nguyễn Văn An',
  signedDate: '2026-10-20',
}

describe('Agency guarantee form', () => {
  it('requires Agency identity fields and accepts the registered tax-code formats', () => {
    assert.deepEqual(guaranteeProblems(valid), {})
    assert.ok(guaranteeProblems({ ...valid, legalName: ' ' }).legalName)
    assert.ok(guaranteeProblems({ ...valid, taxCode: '0312345678-01' }).taxCode)
    assert.ok(guaranteeProblems({ ...valid, legalRepresentative: '' }).legalRepresentative)
  })

  it('renders a printable Agency-wide guarantee with escaped identity fields', () => {
    const html = buildGuaranteeHtml({ ...valid, legalName: '<script>alert(1)</script> & Co' })

    assert.match(html, /@page\{size:A4/)
    assert.match(html, /toàn bộ nhân sự Agency nhập qua nền tảng, hiện tại và các đợt sau/)
    assert.match(html, /&lt;script&gt;alert\(1\)&lt;\/script&gt; &amp; Co/)
    assert.doesNotMatch(html, /<script>alert\(1\)<\/script>/)
    assert.match(html, /20\/10\/2026/)
  })
})
