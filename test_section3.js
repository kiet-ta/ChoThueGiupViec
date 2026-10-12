const body = `### Area

backend

### Blocked by
(Not blocked by any other tickets - this is a configuration fix that can be worked on independently)

### Allowed paths
-Backend/Application/Features/{Agencies,Skills}/**
-Backend/WebAPI/Controllers/{Agencies,Skills}/**
-Backend/Infrastructure/Modules/{Agencies,Skills}/**
-Backend/Tests/{Agencies,Skills}/**
-Backend/Domain/Entities/*.{Agencies,Skills}.cs
-Mobile/lib/features/agencies/**
-Mobile/test/features/agencies/**
-Frontend/src/features/{agencies,skills}/**
-.spec/plan/M5-*.md
-.spec/contracts/{agencies,skills}.md
-.spec/contracts/openapi.json

### Acceptance criteria
Thêm trường BusinessRegistrationCertificate vào PartnerAgency.entity (cho ĐKKD)
Tạo DTOs RegisterAgencyRequest/Response với validation đầy đủ (business registration certificate, tax code, legal name, representative, contact info, password)
Triển khai CQRS: RegisterAgencyCommand + Handler (kiểm tra tax code unique, hash mật khẩu, đặt giá trị khởi tạo: escrow balance = 0, SLA score = 100.0, status = PENDING)
Tạo IAgencyRepository interface + EfAgencyRepository implementation
Đăng ký AgenciesModule để đăng ký repository với DI
Tạo AgenciesController với endpoint POST /api/agencies/registration ([AllowAnonymous])
Đã cập nhật local M5 status: Đánh dấu BE-M5-01 như [x] HOÀN THÀNH trong .spec/plan/M5-agencies-b2b.md với bằng chứng
Đã push 2 commit lên remote: 467f983 + 7fd7aee trên nhánh ticket/237-be-m5-01-dang-ky-agency-dkkd-mst-dai
Tất cả thay đổi nằm trong đường dẫn được phép cho module Agencies
Lưu ý: Lỗi scope check do cấu hình ticket #237 ban đầu có phần allowed trống (không phải do công việc của tôi gây ra)
Lưu ý: Lỗi L3 verify do lỗi AssemblyInfo.cs cũ (không phải do công việc của tôi gây ra)

### Slice
None`;

const section = (body, heading) => {
  const m = body.replace(/\r/g, '').match(new RegExp(`### ${heading}\\n\\n([\\s\\S]*?)(?=\\n### |$)`));
  return m ? m[1].trim() : '';
};
const lines = (text) =>
  text.split('\n').map((l) => l.replace(/^[-*]\s*/, '').replace(/`/g, '').trim()).filter((l) => l && l !== '_No response_');

console.log('Allowed section:');
console.log('[' + section(body, 'Allowed paths') + ']');
console.log('Lines allowed:');
console.log(lines(section(body, 'Allowed paths')));
console.log('Acceptance section:');
console.log('[' + section(body, 'Acceptance criteria') + ']');
console.log('Lines acceptance:');
console.log(lines(section(body, 'Acceptance criteria')));