---
name: project-team-design-template
description: Apply the TỔ ẤM Nordic Care design system (colors, typography, spacing, layout, components) when building Web or Mobile UI for this project
---

# Project Team Design System: TỔ ẤM — Nordic Care

## When to Use This Skill
Use this skill whenever designing, styling, building, or reviewing any user interface for this project across **Frontend** (`Frontend/`) or **Mobile** (`Mobile/`).

## Source Documents
Do not guess or invent visual tokens. Read and follow these two authoritative source documents in this directory:
1. [DESIGN.md](DESIGN.md): Contains the machine-readable YAML design tokens (hex colors, typography sizes/weights, corner radii, spacing scale) and the core Scandinavian family home aesthetic specifications.
2. [t_m_nordic_care_design_system_design.md](t_m_nordic_care_design_system_design.md): In-depth UI design specifications, layout patterns (top navigation, 4 trust pill badges, 3-column equipment cards, hero service card, price estimation widget, high-contrast dark green anchor), and component library specs based on the Haisey Home Care template.

## How to Apply Tokens

### 1. Web (Frontend)
- Always read `Frontend/ARCHITECTURE.md` first.
- Map the color tokens, typography, and borders to the existing Tailwind CSS and `shadcn/ui` theme architecture.
- Maintain responsive fluid grids with macro-whitespace (`py-16` to `py-24`) and rounded pill CTAs (`rounded-full`).

### 2. Mobile (Flutter)
- Configure ThemeData and custom widget styles adhering to the TỔ ẤM palette:
  - Canvas: `#FAF8F5` / `#FAF8F3` (Warm Ivory)
  - Primary: `#2D5A43` / `#416448` (Deep Forest Pine)
  - Accent / Secondary: `#6B8F71` (Sage Green)
  - Amber Accent: `#D99B26` / `#D9A441` (Ratings, trust guarantees)
  - Surfaces: `#FFFFFF` with double-bezel or hairline borders (`#E5E7EB`)
- Design target frame: 390×844 (standard mobile layout per decisions D2).

## Core Rules
- **No inventing:** All color hex codes, font families (`Newsreader`, `Plus Jakarta Sans`), font weights, spacing rhythms, and component shapes must come strictly from the two source files.
- **Ticket scope:** Screens and UI components must only be modified within an active feature ticket permitted by the harness.
