import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { agenciesApi } from '@/features/agencies/api'
import {
  EMPTY_REGISTER_FORM,
  registerBody,
  registerFailure,
  registerProblems,
  type RegisterFieldErrors,
  type RegisterForm,
} from '@/features/agencies/register'
import { ApiError } from '@/services/api'

const INPUT =
  'h-10 w-full rounded-lg border border-input bg-background px-3 text-sm outline-none focus-visible:ring-3 focus-visible:ring-ring/50 aria-invalid:border-destructive'

interface FieldProps {
  label: string
  name: keyof RegisterForm
  form: RegisterForm
  errors: RegisterFieldErrors
  onChange: (name: keyof RegisterForm, value: string) => void
  type?: string
  autoComplete?: string
  hint?: string
}

function Field({ label, name, form, errors, onChange, type = 'text', autoComplete, hint }: FieldProps) {
  return (
    <label className="flex flex-col gap-1 text-sm">
      {label}
      <input
        className={INPUT}
        type={type}
        autoComplete={autoComplete}
        value={form[name]}
        onChange={(e) => onChange(name, e.target.value)}
        aria-invalid={!!errors[name]}
      />
      {hint && !errors[name] && <span className="text-xs text-muted-foreground">{hint}</span>}
      {errors[name] && <span className="text-xs text-destructive">{errors[name]}</span>}
    </label>
  )
}

/** Public self-service registration of a Partner agency (contract agencies.md §2, decisions Q25): the agency starts PENDING until its escrow is paid. */
export function RegisterAgencyPage() {
  const [form, setForm] = useState<RegisterForm>(EMPTY_REGISTER_FORM)
  const [errors, setErrors] = useState<RegisterFieldErrors>({})
  const [message, setMessage] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [registered, setRegistered] = useState<string | null>(null)
  const navigate = useNavigate()

  function change(name: keyof RegisterForm, value: string) {
    setForm((f) => ({ ...f, [name]: value }))
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    const problems = registerProblems(form)
    setErrors(problems)
    setMessage(null)
    if (Object.keys(problems).length > 0) return
    setSubmitting(true)
    try {
      await agenciesApi.register(registerBody(form))
      setRegistered(form.contactEmail.trim())
    } catch (err) {
      const failure = err instanceof ApiError ? registerFailure(err.status, err.data, err.fieldErrors) : registerFailure(0, null)
      setErrors(failure.fields)
      setMessage(failure.message)
    } finally {
      setSubmitting(false)
    }
  }

  if (registered) {
    return (
      <main className="flex min-h-screen items-center justify-center bg-muted/40 p-6">
        <section className="flex w-full max-w-md flex-col gap-3 rounded-xl border border-border bg-card p-6 text-card-foreground" role="status">
          <h1 className="text-xl font-semibold">Đã đăng ký</h1>
          <p className="text-sm">
            Tài khoản <strong>{registered}</strong> đã được tạo ở trạng thái chờ kích hoạt. Hãy đăng nhập bằng vai trò <em>Đối tác doanh nghiệp</em> và nạp ký quỹ tối thiểu
            ở mục <em>Ký quỹ &amp; SLA</em> để công ty hoạt động.
          </p>
          <Button type="button" onClick={() => navigate('/login')}>
            Đến trang đăng nhập
          </Button>
        </section>
      </main>
    )
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-muted/40 p-6">
      <form
        onSubmit={onSubmit}
        noValidate
        className="flex w-full max-w-md flex-col gap-4 rounded-xl border border-border bg-card p-6 text-card-foreground"
      >
        <h1 className="text-xl font-semibold">Đăng ký đối tác doanh nghiệp</h1>
        <p className="text-sm text-muted-foreground">Công ty cung cấp nhân sự đăng ký một lần; sau khi nạp ký quỹ tối thiểu, công ty được nhận đơn Premium.</p>

        <Field label="Mã số thuế" name="taxCode" form={form} errors={errors} onChange={change} hint="10 chữ số, hoặc 10 chữ số-3 chữ số cho chi nhánh" />
        <Field label="Tên pháp lý của công ty" name="legalName" form={form} errors={errors} onChange={change} />
        <Field label="Người đại diện pháp luật" name="legalRepresentative" form={form} errors={errors} onChange={change} />
        <Field label="Số điện thoại liên hệ" name="contactPhone" form={form} errors={errors} onChange={change} autoComplete="tel" />
        <Field label="Email (dùng để đăng nhập)" name="contactEmail" form={form} errors={errors} onChange={change} type="email" autoComplete="username" />
        <Field
          label="Mật khẩu"
          name="password"
          form={form}
          errors={errors}
          onChange={change}
          type="password"
          autoComplete="new-password"
          hint="Từ 10 ký tự, có chữ hoa, chữ thường và chữ số"
        />

        <fieldset className="flex flex-col gap-3 rounded-lg border border-border p-3">
          <legend className="px-1 text-sm font-medium">Tài khoản nhận tiền (tuỳ chọn, có thể khai sau)</legend>
          <Field label="Ngân hàng" name="bankName" form={form} errors={errors} onChange={change} />
          <Field label="Số tài khoản" name="bankAccountNo" form={form} errors={errors} onChange={change} hint="6 đến 30 chữ số" />
        </fieldset>

        {message && (
          <p role="alert" className="text-sm text-destructive">
            {message}
          </p>
        )}

        <Button type="submit" disabled={submitting}>
          {submitting ? 'Đang đăng ký…' : 'Đăng ký'}
        </Button>
        <p className="text-center text-sm">
          Đã có tài khoản?{' '}
          <Link to="/login" className="text-primary underline">
            Đăng nhập
          </Link>
        </p>
      </form>
    </main>
  )
}
