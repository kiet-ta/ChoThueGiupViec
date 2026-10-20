import { isValidTaxCode } from './register.ts'

export interface AgencyGuaranteeForm {
  legalName: string
  taxCode: string
  legalRepresentative: string
  signedDate: string
}

export type AgencyGuaranteeErrors = Partial<Record<keyof AgencyGuaranteeForm, string>>

export function guaranteeProblems(form: AgencyGuaranteeForm): AgencyGuaranteeErrors {
  const errors: AgencyGuaranteeErrors = {}
  const legalName = form.legalName.trim()
  const taxCode = form.taxCode.trim()
  const representative = form.legalRepresentative.trim()

  if (!legalName) errors.legalName = 'Nhập tên pháp lý của Agency.'
  else if (legalName.length > 200) errors.legalName = 'Tên pháp lý tối đa 200 ký tự.'
  if (!isValidTaxCode(taxCode)) errors.taxCode = 'Nhập MST gồm 10 chữ số, hoặc 10 chữ số và 3 số chi nhánh.'
  if (!representative) errors.legalRepresentative = 'Nhập người đại diện pháp luật.'
  else if (representative.length > 100) errors.legalRepresentative = 'Tên người đại diện tối đa 100 ký tự.'

  return errors
}

function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, (character) => ({
    '&': '&amp;',
    '<': '&lt;',
    '>': '&gt;',
    '"': '&quot;',
    "'": '&#39;',
  })[character]!)
}

function dateText(value: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  return match ? `${match[3]}/${match[2]}/${match[1]}` : '____ / ____ / ______'
}

export function buildGuaranteeHtml(form: AgencyGuaranteeForm): string {
  const legalName = escapeHtml(form.legalName.trim())
  const taxCode = escapeHtml(form.taxCode.trim())
  const representative = escapeHtml(form.legalRepresentative.trim())
  const signedDate = escapeHtml(dateText(form.signedDate))

  return `<!doctype html>
<html lang="vi">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Cam kết bảo lãnh trách nhiệm nhân sự</title>
<style>
  @page{size:A4;margin:0}*{box-sizing:border-box}body{margin:0;color:#18324b;font-family:Arial,"Segoe UI",sans-serif;background:#eef1f4}.toolbar{max-width:210mm;margin:12px auto;padding:12px 16px;background:#fff4d8;border:1px solid #e7c888;border-radius:6px;font-size:13px;line-height:1.5}.toolbar button{margin-left:10px;padding:8px 12px;border:0;border-radius:4px;background:#18324b;color:#fff;font-weight:bold;cursor:pointer}.paper{position:relative;width:210mm;height:297mm;margin:0 auto 20px;padding:14mm;background:#fff;box-shadow:0 5px 22px #18324b20;overflow:hidden}.top-rule{position:absolute;inset:0 0 auto;height:2mm;background:#18324b}.brand{display:flex;justify-content:space-between;align-items:center;border-bottom:1px solid #d8dee5;padding-bottom:6mm}.brand strong{font-size:13px;letter-spacing:1.7px}.brand span{font-size:9px;letter-spacing:1.2px;color:#607080}.eyebrow{margin:9mm 0 2mm;color:#a98143;font-size:9px;font-weight:bold;letter-spacing:1.5px}.title{font:700 25px/1.15 Georgia,"Times New Roman",serif;margin:0}.subtitle{font:19px Georgia,"Times New Roman",serif;margin:1mm 0 2mm}.lead{font-size:10px;color:#607080;margin:0 0 7mm}.meta{display:flex;justify-content:space-between;background:#f5f7f9;padding:3mm 4mm;margin-bottom:7mm}.label{display:block;font-size:8px;color:#607080;font-weight:bold;letter-spacing:.8px;margin-bottom:2mm}.meta strong{font-size:9px;font-weight:normal;margin-left:3mm}.section{margin-bottom:7mm}.section-title{font-size:9px;letter-spacing:1px;font-weight:bold;border-bottom:1px solid #d8dee5;padding-bottom:2mm;margin:0 0 4mm}.value{font-size:10px;min-height:6mm;border-bottom:1px solid #9eabb7;padding-bottom:1mm;overflow-wrap:anywhere}.row{display:flex;gap:7mm;margin-top:4mm}.row>div{flex:1}.copy{font-size:10px;line-height:1.65;margin:0}.scope{font-size:9.5px;line-height:1.6;padding-left:5mm;margin:3mm 0}.scope li{margin-bottom:1.5mm;padding-left:1mm}.signatures{display:grid;grid-template-columns:1fr 1fr;gap:12mm;margin-top:5mm}.signature{text-align:center}.signature strong{display:block;font-size:8px;letter-spacing:.7px}.signature p{font-size:8px;color:#607080;margin:1.5mm 0 3mm}.signbox{height:27mm;border:1px dashed #cbd4dc;display:grid;place-items:center;color:#71808d;font-size:8px}.footer{border-top:1px solid #d8dee5;margin-top:7mm;padding-top:3mm;display:flex;justify-content:space-between;gap:8mm;font-size:7.5px;line-height:1.4;color:#607080}.print-only{display:none}@media print{html,body{width:210mm;height:297mm;background:#fff}.toolbar{display:none}.paper{margin:0;box-shadow:none;print-color-adjust:exact;-webkit-print-color-adjust:exact}}
</style>
</head>
<body>
  <div class="toolbar">Kiểm tra thông tin, chọn “In / Lưu PDF”, rồi chọn “Save as PDF”. Tài liệu chưa có chữ ký cho đến khi đại diện Agency ký.<button type="button" onclick="window.print()">In / Lưu PDF</button></div>
  <article class="paper">
    <div class="top-rule"></div>
    <header class="brand"><strong>GIÚP VIỆC</strong><span>ĐỐI TÁC DOANH NGHIỆP</span></header>
    <div class="eyebrow">CAM KẾT • AGENCY STAFF</div>
    <h1 class="title">Cam kết bảo lãnh</h1><div class="subtitle">trách nhiệm nhân sự</div>
    <p class="lead">Văn bản áp dụng cho nhân sự do Agency đăng ký và quản lý trên nền tảng.</p>
    <div class="meta"><div><span class="label">MÃ VĂN BẢN</span><strong>AGY-GUARANTEE / __________</strong></div><div><span class="label">NGÀY KÝ</span><strong>${signedDate}</strong></div></div>
    <section class="section"><h2 class="section-title">01 &nbsp; THÔNG TIN AGENCY</h2>
      <div><span class="label">TÊN PHÁP LÝ</span><div class="value">${legalName}</div></div>
      <div class="row"><div><span class="label">MÃ SỐ THUẾ</span><div class="value">${taxCode}</div></div><div><span class="label">NGƯỜI ĐẠI DIỆN PHÁP LUẬT</span><div class="value">${representative}</div></div></div>
    </section>
    <section class="section"><h2 class="section-title">02 &nbsp; NỘI DUNG CAM KẾT</h2>
      <p class="copy">Chúng tôi xác nhận thông tin nhân sự do Agency cung cấp là chính xác và cam kết chịu trách nhiệm đối với nhân sự do mình đăng ký, quản lý và điều phối thực hiện dịch vụ trên nền tảng, theo các quy định và thỏa thuận áp dụng.</p>
      <ul class="scope"><li>Phạm vi áp dụng: toàn bộ nhân sự Agency nhập qua nền tảng, hiện tại và các đợt sau; không giới hạn trong một file Excel hoặc một đợt nhập.</li><li>Cam kết tiếp tục có hiệu lực cho đến khi được thay thế; Agency cần ký bản mới nếu thông tin pháp lý hoặc nội dung cam kết thay đổi.</li></ul>
    </section>
    <section><h2 class="section-title">03 &nbsp; KÝ XÁC NHẬN</h2>
      <div class="signatures"><div class="signature"><strong>ĐẠI DIỆN PHÁP LUẬT</strong><p>Ký tay hoặc ký điện tử</p><div class="signbox">VÙNG CHỮ KÝ</div><p>Họ tên: ${representative}</p></div><div class="signature"><strong>AGENCY</strong><p>Đóng dấu nếu có sử dụng</p><div class="signbox">VÙNG CON DẤU (NẾU CÓ)</div><p>Ngày ký: ${signedDate}</p></div></div>
    </section>
    <footer class="footer"><span>Agency chịu trách nhiệm bảo đảm nội dung đã được người đại diện pháp luật ký trước khi nộp.</span><span>MẪU CAM KẾT AGENCY</span></footer>
  </article>
</body>
</html>`
}
