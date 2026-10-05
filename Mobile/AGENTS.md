# Mobile Agent Guidelines: TỔ ẤM — Nordic Care

## Rules for Mobile Development
1. **Design System Compliance:**
   - Adhere strictly to `.agents/skills/project-team-design-template/` (`DESIGN.md` and `t_m_nordic_care_design_system_design.md`).
   - Do NOT invent colors, fonts, or shapes.
   - Use `NordicColors`, `NordicTypography`, `NordicTheme`, `NordicButton`, `NordicCard`, `TrustBadge`, and `EyebrowBadge`.
2. **Feature Isolation:**
   - Code features inside `Mobile/lib/features/<module>/`.
   - Never directly cross-import private state across features; use shared contracts or events.
3. **Verification Before Claiming Done:**
   - `cd Mobile && flutter analyze` must report 0 issues.
   - `cd Mobile && flutter test` must pass 100%.
   - `./harness/run.ps1 verify L3` must pass.
