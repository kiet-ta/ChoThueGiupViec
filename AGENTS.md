# AGENTS.md

Monorepo: `Backend/` (.NET 10 API) and `Frontend/` (React + Vite + shadcn/ui).

- Backend: read [Backend/ARCHITECTURE.md](Backend/ARCHITECTURE.md). Run commands inside `Backend/`.
- Frontend: read [Frontend/ARCHITECTURE.md](Frontend/ARCHITECTURE.md). Run commands inside `Frontend/`.
- Harness design: [HARNESS.md](HARNESS.md). Spec: `.spec/spec.md`. Quyết định đã chốt (ưu tiên hơn kế hoạch, dưới PRD): `.spec/decisions.md`. Tickets: GitHub Issues (source of truth), mirrored in `.spec/tasks.md` (a GENERATED, untracked file: `node harness/sync-issues.mjs`; never commit or hand-edit it).
- Dev wiring: Vite proxies `/api` to `http://localhost:5004`; no CORS config needed in dev.
- NEVER commit secrets (API keys, tokens, `.env`).
- DB = SQL Server. Backend ở môi trường Development tự tạo 1 tài khoản Admin để test khi DB mới (chi tiết + thông tin đăng nhập dev: `.spec/plan/00-overview.md` §10). Không tự tạo/hard-code admin khác.

## Rules for agents (enforced by `harness/`, not by trust)
1. **EVIDENCE OR IT DID NOT HAPPEN.** Every report, claim of "done", "works", "fixed" or "failed" MUST cite evidence: command + output, test name, `file:line`, or a `.harness/evidence/*.log` path. For errors, state exactly where (`file:line`, step name). If you have no evidence, do not do it and do not claim it; making it up counts as fabrication. Say "unknown / not verified" instead.
2. **No ticket, no code.** Work only on a GitHub Issue (label `ticket`) whose status is `in-progress` in `.spec/tasks.md`. Branch name: `ticket/<issue#>-<slug>`. One ticket = one branch = one agent. **Start every ticket ONLY with `node harness/start-ticket.mjs <issue#>`** (PowerShell: `./harness/run.ps1 start-ticket <issue#>`): it validates the issue and its blockers, creates (or resumes) the branch from a freshly fetched `origin/main`, sets `in-progress`, refreshes `.spec/tasks.md` and runs `scope-check`. Never create or switch ticket branches by hand; never code on `main`.
3. **Scope is hard.** Touch only the ticket's `allowed` paths. `scope-check` blocks the commit otherwise. If more scope is needed: open a GitHub Issue (template "Scope exception") with evidence, then stop. Never work around it.
4. **Protected paths** (`harness/protected-paths.txt`: hooks, CI, harness, secrets, AGENTS.md, appsettings) are off-limits unless the ticket lists them literally.
5. **Do not invent**: no packages, endpoints, entities, env vars, files or APIs not named in the ticket/spec. Verify a symbol exists (grep, with output as evidence) before using it. If something is missing, ask in the issue; do not guess.
6. **No drive-by changes**: no refactors, renames or reformatting outside the ticket diff.
7. **Never bypass the gates**: no `--no-verify`, no editing hooks, no disabling lint/tests, no force push, no direct push to `main`. `harness/guard-command.sh` blocks destructive commands.
8. **Self-correction budget**: on `verify` failure fix and retry at most 3 times; then comment on the issue with the failing evidence and stop (`blocked`).

## Lệnh "phân tích dự án" (áp dụng cho MỌI agent, mọi thành viên)
Kế hoạch 6 người nằm ở `.spec/plan/` (tổng quan + 1 file checklist mỗi người), roster ở `.spec/team.md`, spec ở `.spec/spec.md`.
Khi người dùng nói **"phân tích dự án"** (không phân biệt hoa/thường/dấu), làm đúng các bước, **chỉ đọc, không viết code, không tick ô, không tạo issue**:
1. **Hỏi tên**: chỉ hỏi "Bạn tên gì?" rồi DỪNG chờ trả lời. Không đoán, không đọc kế hoạch trước khi có tên.
2. **Tra slot** trong `.spec/team.md` (khớp tên bỏ dấu/hoa-thường). Không thấy → liệt kê các slot M1–M6 và hỏi "bạn là slot nào?"; nhắc người đó nhờ người điều phối thêm tên vào `.spec/team.md`. Vẫn mơ hồ → dừng, không tự chọn.
3. **Thu thập bằng chứng** (chạy lệnh, không suy diễn): `git fetch origin`, `git branch --show-current`, `git log origin/main --oneline -5`; đọc `.spec/plan/00-overview.md` (gate G0–G4, trạng thái Q#), `.spec/decisions.md` (quy tắc & giá trị đã chốt, tiếng Anh), file checklist của slot, `.spec/tasks.md` (làm mới trước bằng `node harness/sync-issues.mjs`; file sinh tự động, không tracked; ticket `in-progress` có mã task của slot). Gate/task đối chiếu với repo (file/commit có thật) chứ không chỉ tin ô tick; mâu thuẫn thì nêu rõ. Không kiểm chứng được → ghi "unknown / not verified".
4. **Trả lời theo khuôn**:
   - Dòng đầu: `Tên — Slot — vai trò — module sở hữu`.
   - Bảng gate G0–G4: ✅/⏳ + bằng chứng.
   - Checklist đầy đủ của slot theo Wave, mỗi task một trạng thái: `✅ xong` · `▶ SẴN SÀNG` (gate + mọi `needs` đã xong, mọi `Q#` liên quan đã `✅`) · `⏳ CHỜ <gate/task>` · `❓ CẦN QUYẾT ĐỊNH Q#`.
   - **Việc nên làm tiếp (tối đa 3)**: CHỈ liệt kê task đang `▶`, ưu tiên Wave 0 → Backend → Web/Mobile. Không có task `▶` thì ghi đúng "chưa có việc ▶" và nêu gate/task đang chờ. Không gợi ý viết code khi chưa có ticket `in-progress` (rule 2); Wave 0 như contract `*-00` hay nền Web/Mobile vẫn phải qua ticket.
   - Cách bắt đầu một task: tạo GitHub Issue theo template **Ticket** (title có mã task, ví dụ `[ticket] BE-M2-05 …`; *Allowed paths* = "Allowed mặc định" ở đầu file checklist + file cần thêm; *Blocked by*/*Acceptance* lấy từ task), để nhãn `ticket` + `ready`, rồi chạy `node harness/start-ticket.mjs <issue#>` (tự đổi nhãn sang `in-progress`, tạo nhánh `ticket/<issue#>-<slug>` từ `origin/main` mới nhất, làm mới tasks.md). Không tạo nhánh bằng tay. Chỉ làm khi người dùng đồng ý chọn task đó.
   - Câu hỏi mở `Q#` liên quan tới slot đang chưa `✅`.
5. **Sau khi làm xong task** (trong chính PR của ticket): đổi `[ ]`→`[x]` + `— evidence: <log|PR#>` ở file checklist **của mình** (file này luôn nằm trong `allowed`). Không bao giờ sửa checklist của slot khác; cần port/schema/contract của người khác → "Scope exception".
6. Thay đổi giữa chừng (gate mở, Q# được chốt) do người điều phối/M1 cập nhật `00-overview.md`; agent chỉ báo lại khi được hỏi "phân tích dự án" lần nữa.

## Skills and agent config (shared by every agent, checked by the harness)
- **Skills source of truth: `.agents/skills/<name>/SKILL.md`** (Codex / Antigravity read it). `.claude/skills/` is a **generated mirror** for Claude Code: never edit it by hand and never put Claude-only skills there.
- **Command rules:** Claude = hook in `.claude/settings.json` -> `harness/guard-command.sh`. Codex = `.codex/rules/default.rules`. Both mirror the same bans (force push, push to main, hard reset, `--no-verify`, recursive force delete, DB drop).
- `CLAUDE.md` only imports `AGENTS.md`; the rules live here.
- Add or change a skill/rule only through a ticket whose `allowed` lists **literally** `.agents/**`, `.claude/**`, `.codex/**` or `CLAUDE.md` (protected paths). After editing `.agents/skills`, run `node harness/sync-agent-skills.mjs`.
- `sh harness/check-agent-config.sh` (also in `verify` L1, pre-commit and CI) fails on: missing rule/skill files, a skill without `name`/`description`, a drifted `.claude/skills` mirror, a removed guard hook, a weakened permission mode, or tracked credentials (`.codex/auth.json`, `.claude/settings.local.json`).
- Never copy `~/.codex` or `~/.claude` wholesale into the repo (they contain credentials and session data).

## Verification (run before saying "done")
`sh harness/verify.sh L1|L2|L3` (PowerShell: `./harness/run.ps1 verify L3`). Paste the final line and log path in the PR (template enforces it).