import assert from 'node:assert/strict'
import { describe, it } from 'node:test'
import {
  EMPTY_REGISTER_FORM,
  normalizePhone,
  registerBody,
  registerFailure,
  registerProblems,
  type RegisterForm,
} from '../src/features/agencies/register.ts'

const valid: RegisterForm = {
  taxCode: '0312345678',
  legalName: 'Cong ty E2E',
  legalRepresentative: 'Nguyen Van A',
  contactPhone: '0901234567',
  contactEmail: 'partner@example.com',
  password: 'Passw0rd!Passw0rd',
  bankName: '',
  bankAccountNo: '',
}

describe('registration form', () => {
  it('accepts a complete form without a bank account', () => {
    assert.deepEqual(registerProblems(valid), {})
  })

  it('asks for every required field', () => {
    const problems = registerProblems(EMPTY_REGISTER_FORM)
    for (const key of ['taxCode', 'legalName', 'legalRepresentative', 'contactPhone', 'contactEmail', 'password']) {
      assert.ok(problems[key as keyof RegisterForm], key)
    }
    assert.equal(problems.bankName, undefined)
  })

  it('checks the tax code, the phone, the email and the password like the backend', () => {
    assert.ok(registerProblems({ ...valid, taxCode: '123' }).taxCode)
    assert.equal(registerProblems({ ...valid, taxCode: '0312345678-001' }).taxCode, undefined)
    assert.ok(registerProblems({ ...valid, contactPhone: '0201234567' }).contactPhone)
    assert.equal(registerProblems({ ...valid, contactPhone: '+84 901 234 567' }).contactPhone, undefined)
    assert.ok(registerProblems({ ...valid, contactEmail: 'not-an-email' }).contactEmail)
    assert.ok(registerProblems({ ...valid, password: 'short1A' }).password)
    assert.ok(registerProblems({ ...valid, password: 'alllowercase123' }).password)
  })

  it('wants the bank name and account together, the account as 6 to 30 digits', () => {
    assert.ok(registerProblems({ ...valid, bankName: 'VCB' }).bankAccountNo)
    assert.ok(registerProblems({ ...valid, bankAccountNo: '0011001234' }).bankName)
    assert.ok(registerProblems({ ...valid, bankName: 'VCB', bankAccountNo: '12ab' }).bankAccountNo)
    assert.deepEqual(registerProblems({ ...valid, bankName: 'VCB', bankAccountNo: '0011 0012 34' }), {})
  })
})

describe('registration body', () => {
  it('trims, normalises the phone and sends the bank pair only when filled in', () => {
    assert.equal(normalizePhone('+84 901-234-567'), '0901234567')
    const withoutBank = registerBody({ ...valid, legalName: '  Cong ty E2E ', contactPhone: '84901234567' })
    assert.equal(withoutBank.legalName, 'Cong ty E2E')
    assert.equal(withoutBank.contactPhone, '0901234567')
    assert.equal('bankName' in withoutBank, false)
    const withBank = registerBody({ ...valid, bankName: ' ACB ', bankAccountNo: '9988 7766 55' })
    assert.deepEqual([withBank.bankName, withBank.bankAccountNo], ['ACB', '9988776655'])
  })
})

describe('registration failures', () => {
  it('puts the 400 field errors on their fields (any casing)', () => {
    const failure = registerFailure(400, null, { TaxCode: ['bad tax code'], contactemail: ['bad email'] })
    assert.equal(failure.fields.taxCode, 'bad tax code')
    assert.equal(failure.fields.contactEmail, 'bad email')
    assert.equal(failure.message, null)
  })

  it('explains the two 409 codes and a network failure', () => {
    assert.match(registerFailure(409, { code: 'AGENCY_TAX_CODE_EXISTS' }).message ?? '', /Mã số thuế/)
    assert.match(registerFailure(409, { code: 'AGENCY_EMAIL_EXISTS' }).message ?? '', /Email/)
    assert.ok(registerFailure(409, { code: 'AGENCY_TAX_CODE_EXISTS' }).fields.taxCode)
    assert.match(registerFailure(0, null).message ?? '', /kết nối/)
    assert.ok(registerFailure(500, null).message)
  })
})
