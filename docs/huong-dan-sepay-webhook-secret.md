# Hướng dẫn tạo và gắn Webhook Secret của SePay

Tài liệu này giúp một máy khác dựng lại cấu hình nhận webhook SePay (Test mode) cho đúng chuẩn.
Nội dung đối chiếu với code: `Backend/Infrastructure/Modules/Payments/SePay/` và `Backend/WebAPI/Controllers/Payments/PaymentIpnController.cs`.

> Chỉ dùng **sandbox** (quy tắc G-1: không tiền thật). Không commit secret, token hay file `.env` lên git.

## 1. Webhook secret là gì, dùng để làm gì

Endpoint nhận webhook là công khai (`[AllowAnonymous]`), nên ai biết URL cũng gọi được.
Secret là chìa khoá chung giữa SePay và backend để chứng minh request thật sự do SePay gửi.

Với mỗi webhook, SePay gửi kèm 2 header:

| Header | Ý nghĩa |
|---|---|
| `X-SePay-Timestamp` | Thời điểm gửi, dạng Unix giây |
| `X-SePay-Signature` | `sha256=` + HMAC-SHA256 dạng hex thường |

Backend tính lại chữ ký theo đúng công thức (file `SePayProtocol.cs`):

```
signature = "sha256=" + hex_lowercase( HMAC_SHA256( key = WebhookSecret,
                                                    message = "<timestamp>.<rawBody>" ) )
```

`<rawBody>` là **nguyên văn** nội dung request, từng ký tự một. Backend đọc body thô trước khi parse JSON.

Backend từ chối nếu: thiếu header, timestamp lệch quá **300 giây**, chữ ký không khớp,
hoặc body không có `transferType = "in"`, `code` (chuỗi, không rỗng), `transferAmount` (số nguyên > 0).

Vì vậy secret ở hai phía (dashboard SePay và backend) phải **giống hệt nhau từng ký tự**.
Thừa hay thiếu một khoảng trắng hoặc xuống dòng cũng làm mọi webhook bị từ chối.

## 2. Tạo secret

Dùng chuỗi ngẫu nhiên đủ dài (khuyến nghị 32 byte, tức 64 ký tự hex). Chọn một cách:

```bash
# Git Bash / Linux / macOS
openssl rand -hex 32
```

```powershell
# PowerShell
-join ((1..32) | ForEach-Object { '{0:x2}' -f (Get-Random -Maximum 256) })
```

Quy tắc:

- Không chứa chữ `PLACEHOLDER` (code coi giá trị này là chưa cấu hình: `SePayOptions.IsReal`).
- Không có khoảng trắng hoặc xuống dòng ở đầu và cuối.
- Mỗi môi trường (máy dev, UAT) nên dùng secret riêng. Không dùng lại secret của môi trường thật.
- Lưu vào nơi an toàn (trình quản lý mật khẩu). Không dán vào chat, issue hay commit.

## 3. Gắn secret vào backend

Backend đọc cấu hình mục `SePay`. Bốn giá trị sau **bắt buộc đủ** thì cổng SePay mới được bật
(`SePayOptions.HasCredentials`); thiếu một giá trị thì cổng không được đăng ký và app dùng Fake gateway.

| Khoá cấu hình | Biến môi trường | Ví dụ / ghi chú |
|---|---|---|
| `SePay:AccountNumber` | `SePay__AccountNumber` | Số tài khoản nhận tiền (tài khoản test trên SePay) |
| `SePay:BankName` | `SePay__BankName` | Tên ngân hàng như SePay hiển thị |
| `SePay:WebhookSecret` | `SePay__WebhookSecret` | **Secret ở bước 2** |
| `SePay:ApiToken` | `SePay__ApiToken` | Token API sandbox, dùng cho job đối soát |
| `SePay:ApiBaseUrl` | `SePay__ApiBaseUrl` | Mặc định `https://userapi-sandbox.sepay.vn/v2`. **Phải** là host sandbox, sai thì app dừng khi khởi động |

### Cách A. Chạy bằng Docker kit `mobile-test-kit` (cách đang dùng cho UAT)

Tạo hoặc sửa file `.env` (file này **không được commit**) trong thư mục `mobile-test-kit/`:

```env
SEPAY_ACCOUNT_NUMBER=<số tài khoản test>
SEPAY_BANK_NAME=<tên ngân hàng>
SEPAY_WEBHOOK_SECRET=<secret ở bước 2>
SEPAY_API_TOKEN=<token API sandbox>
```

`docker-compose.yml` của kit tự map các biến này sang `SePay__*` cho container backend.
Để trống thì cổng SePay không bật. Sau khi sửa `.env`, tạo lại container backend:

```bash
docker compose up -d --force-recreate backend
```

### Cách B. Chạy backend trực tiếp (dotnet run)

Dùng user-secrets, không ghi vào `appsettings.json`:

```bash
cd Backend
dotnet user-secrets set "SePay:AccountNumber" "<số tài khoản test>"
dotnet user-secrets set "SePay:BankName" "<tên ngân hàng>"
dotnet user-secrets set "SePay:WebhookSecret" "<secret ở bước 2>"
dotnet user-secrets set "SePay:ApiToken" "<token API sandbox>"
```

Hoặc đặt biến môi trường `SePay__WebhookSecret` và các biến còn lại như bảng trên.

## 4. Mở URL công khai cho SePay gọi vào

SePay phải gọi được vào backend, mà máy local không có địa chỉ công khai, nên dùng Cloudflare Tunnel
(profile `tunnel` của Docker kit):

```bash
cd mobile-test-kit
docker compose --profile tunnel up -d tunnel
docker compose logs tunnel
```

Trong log tìm dòng có địa chỉ dạng `https://xxxx.trycloudflare.com`.

> URL này **đổi mỗi lần chạy lại tunnel**. Mỗi lần đổi phải cập nhật lại Webhook URL trên dashboard SePay.
> Nếu quên, SePay gọi vào địa chỉ cũ và webhook không tới (job đối soát nền sẽ hỏi SePay để bù, nhưng chậm hơn).

URL webhook đầy đủ để dán vào SePay:

```
https://<địa-chỉ-tunnel>/api/v1/payments/ipn/sepay
```

Phương thức: `POST`, nội dung JSON.

## 5. Khai báo webhook trên dashboard SePay

1. Đăng nhập dashboard **SePay sandbox** (không dùng môi trường thật).
2. Vào phần cấu hình webhook của tài khoản test và thêm webhook mới.
3. Điền **URL** ở bước 4.
4. Điền **secret** đúng bằng giá trị ở bước 2 (nếu dashboard có ô secret hoặc khoá ký).
5. Chọn sự kiện **tiền vào** (`transferType = in`).
6. Lưu.

> Chưa kiểm chứng: tên chính xác các nút và ô trên giao diện SePay, và việc dashboard cho tự nhập secret
> hay tự sinh secret để mình sao chép. Nếu dashboard **tự sinh** secret thì làm ngược lại: sao chép secret
> từ SePay rồi đặt vào `SEPAY_WEBHOOK_SECRET` ở bước 3, thay vì dùng giá trị ở bước 2.
> Phần này cần người đang mở dashboard xác nhận.

## 6. Kiểm tra bằng tay (không cần SePay)

Tự ký một request giả để chắc secret và đường dẫn đúng. Thay `<SECRET>` và `<HOST>` rồi chạy bằng Git Bash:

```bash
SECRET='<SECRET>'
HOST='http://localhost:5004'      # hoặc URL tunnel
TS=$(date +%s)
BODY='{"transferType":"in","code":"TA0000000000","transferAmount":100000}'
SIG="sha256=$(printf '%s.%s' "$TS" "$BODY" | openssl dgst -sha256 -hmac "$SECRET" -hex | sed 's/^.* //')"

curl -i -X POST "$HOST/api/v1/payments/ipn/sepay" \
  -H "Content-Type: application/json" \
  -H "X-SePay-Timestamp: $TS" \
  -H "X-SePay-Signature: $SIG" \
  --data-binary "$BODY"
```

Cách đọc kết quả:

| Kết quả | Nghĩa |
|---|---|
| `200` và `{"success":true}` | Chữ ký đúng và webhook được chấp nhận (hoặc đã xử lý trước đó) |
| `400` và `{"success":false}` | Bị từ chối: sai chữ ký, timestamp lệch, body thiếu trường, hoặc mã `code` không khớp giao dịch nào đang chờ |

Lưu ý khi thử:

- `--data-binary "$BODY"` giữ nguyên body. Đừng dùng `-d` kèm xuống dòng thừa, vì chữ ký tính trên body thô.
- Mã `code` thật có dạng `TA` + 10 chữ số, do backend sinh khi tạo QR (`CodePrefix` mặc định `TA`).
  Để thấy đơn chuyển sang PAID, hãy dùng đúng mã của một giao dịch đang PENDING;
  mã bịa vẫn kiểm được chữ ký nhưng sẽ không khớp giao dịch nào.
- Server không tự trả lý do từ chối. Muốn biết nguyên nhân, xem log backend.

## 7. Lỗi thường gặp

| Triệu chứng | Nguyên nhân hay gặp | Cách xử lý |
|---|---|---|
| Thanh toán không bao giờ chuyển PAID, backend không nhận request | URL tunnel đã đổi, dashboard còn URL cũ | Lấy URL mới từ `docker compose logs tunnel`, cập nhật trên SePay |
| Luôn `400` dù URL đúng | Secret hai bên lệch (thừa dấu cách, xuống dòng, hoặc sai ký tự) | Đặt lại cùng một giá trị ở cả hai phía, tạo lại container backend |
| `400`, nghi do timestamp | Đồng hồ máy chạy backend lệch hơn 300 giây | Đồng bộ giờ hệ thống |
| App không bật cổng SePay (dùng Fake) | Thiếu một trong 4 giá trị, hoặc có chữ `PLACEHOLDER` | Điền đủ `AccountNumber`, `BankName`, `WebhookSecret`, `ApiToken` |
| App dừng khi khởi động, báo "SePay API must use the sandbox host" | `ApiBaseUrl` không phải `userapi-sandbox.sepay.vn` | Bỏ cấu hình tuỳ chỉnh hoặc đặt lại URL sandbox |
| Sửa `.env` nhưng không có tác dụng | Container chưa được tạo lại | `docker compose up -d --force-recreate backend` |

## 8. Checklist cho máy mới

- [ ] Tạo secret ngẫu nhiên (bước 2) và lưu nơi an toàn
- [ ] Điền đủ 4 giá trị `SEPAY_*` vào `.env` (không commit)
- [ ] Tạo lại container backend
- [ ] Chạy tunnel và lấy URL công khai
- [ ] Khai báo webhook trên dashboard SePay sandbox: URL `.../api/v1/payments/ipn/sepay`, secret khớp
- [ ] Chạy lệnh thử ở bước 6 và nhận `200` với `{"success":true}`
- [ ] Mỗi lần restart tunnel: cập nhật lại URL trên SePay
