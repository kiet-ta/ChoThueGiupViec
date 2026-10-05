---
name: project-team-design-template
description: Apply the TỔ ẤM Nordic Care design system (colors, typography, spacing, layout, components) when building Web or Mobile UI for this project
---

# Project Team Design System: TỔ ẤM — Nordic Care

## 1. When to Use This Skill
Use this skill whenever building, modifying, styling, or reviewing any user interface across **Frontend** (`Frontend/`) or **Mobile** (`Mobile/`).

## 2. Token Precedence (Leader Decision, 2026-10-06)
Two source documents live in this directory. When designing UI, adhere to this strict hierarchy:
1. **Tier 1 (Single Source of Truth):** YAML tokens in [`DESIGN.md`](DESIGN.md) by exact token name and value.
2. **Tier 2 (Clarification):** Prose in [`DESIGN.md`](DESIGN.md).
3. **Tier 3 (Layout & Tone Only):** [`t_m_nordic_care_design_system_design.md`](t_m_nordic_care_design_system_design.md) supplies layout patterns, bento card structure, and emotional tone only.

> [!IMPORTANT]
> If `DESIGN.md` has no token for a required property, you must NOT copy an arbitrary hex from the other file. Use the closest `DESIGN.md` token or ask for clarification in the ticket.

### Where the Files Disagree (Use `DESIGN.md` Values)
| Attribute / Token | `DESIGN.md` (Authoritative) | Other File (Illustrative Only) | Action / Note |
|---|---|---|---|
| `primary` | `#416448` | `#2D5A43` | Use `#416448` |
| `primary-hover` | `#56705B` | `#224432` | Use `#56705B` |
| `canvas-background` | `#FAF8F3` (or `surface: #fbf9f4`) | `#FAF8F5` | Use `#FAF8F3` |
| `card-surface` | `#FFFFFF` | `#FFFFFF` | Use `#FFFFFF` |
| `text-primary` | `#2F3A34` | `#1F2923` | Use `#2F3A34` |
| `text-secondary` | `#6B7280` | `#4B5563` | Use `#6B7280` |
| `amber` | `#D9A441` (prose) | `#D99B26` | Use `#D9A441` for stars/badges |
| Typography: Headlines | `Newsreader` (`headline-*` tokens) | `Plus Jakarta Sans` / `Newsreader` | Use `Newsreader` |
| Typography: Body & Controls | `Plus Jakarta Sans` (`body-*`, `label-*`, `metric`) | `Plus Jakarta Sans` | Use `Plus Jakarta Sans` |

### Known Contradictions Inside `DESIGN.md`
Reported to the leader; follow the YAML tokens:
1. **Brand Primary Name:** The prose calls Brand Primary Sage Green `#6B8F71`, but YAML defines `primary: #416448` and has no `#6B8F71` token. Follow YAML `#416448`.
2. **Hover Lightness:** `primary-hover` (`#56705B`) is lighter than `primary` (`#416448`). Follow YAML `#56705B`.

## 3. Copy and Numbers Are Illustrative
The marketing copy and numbers in `t_m_nordic_care_design_system_design.md` (e.g., 50,000,000đ insurance, HEPA 99.9%, "240.000 đ / ca 3h", "Haisey", kit cards, ADR-0010) are layout filler:
- Pricing is dynamic domain data (`PRICE_RULE` table, decisions Q01).
- Shifts are up to 4 hours (PRD BR-01).
- Any guarantee, certification, or insurance claim requires explicit leader approval before appearing on a production screen.

## 4. Platform Guidance
- **Web (`Frontend/`):** Admin Console and Partner Portal desktop interface (1440×900 viewport; `.spec/plan/00-overview.md` section 8, D2/D3). Stack: React + Vite + Tailwind CSS v4 + shadcn/ui (`Frontend/ARCHITECTURE.md`).
- **Mobile (`Mobile/`):** Customer and Worker mobile application (390×844 frame; `.spec/plan/00-overview.md` section 8, D2/D3). Stack: Flutter cross-platform.
- Mapping tokens into global web themes (`Frontend/src/index.css`) and declaring fonts in `Mobile/pubspec.yaml` require their own dedicated tickets and must not be done inside a feature ticket.
