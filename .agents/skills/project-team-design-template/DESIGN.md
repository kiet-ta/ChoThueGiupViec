---
name: TỔ ẤM
colors:
  surface: '#fbf9f4'
  surface-dim: '#dbdad5'
  surface-bright: '#fbf9f4'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#f5f3ee'
  surface-container: '#f0eee9'
  surface-container-high: '#eae8e3'
  surface-container-highest: '#e4e2dd'
  on-surface: '#1b1c19'
  on-surface-variant: '#424842'
  inverse-surface: '#30312e'
  inverse-on-surface: '#f2f1ec'
  outline: '#727971'
  outline-variant: '#c2c8bf'
  surface-tint: '#44664b'
  primary: '#416448'
  on-primary: '#ffffff'
  primary-container: '#597d60'
  on-primary-container: '#f6fff3'
  inverse-primary: '#aad0ae'
  secondary: '#7d5700'
  on-secondary: '#ffffff'
  secondary-container: '#ffc55f'
  on-secondary-container: '#755100'
  tertiary: '#7e4f58'
  on-tertiary: '#ffffff'
  tertiary-container: '#996770'
  on-tertiary-container: '#fffbff'
  error: '#ba1a1a'
  on-error: '#ffffff'
  error-container: '#ffdad6'
  on-error-container: '#93000a'
  primary-fixed: '#c5ecc9'
  primary-fixed-dim: '#aad0ae'
  on-primary-fixed: '#00210c'
  on-primary-fixed-variant: '#2c4e34'
  secondary-fixed: '#ffdeaa'
  secondary-fixed-dim: '#f5bd58'
  on-secondary-fixed: '#271900'
  on-secondary-fixed-variant: '#5f4100'
  tertiary-fixed: '#ffd9df'
  tertiary-fixed-dim: '#f3b7c1'
  on-tertiary-fixed: '#331019'
  on-tertiary-fixed-variant: '#663a43'
  background: '#fbf9f4'
  on-background: '#1b1c19'
  surface-variant: '#e4e2dd'
  canvas-background: '#FAF8F3'
  card-surface: '#FFFFFF'
  primary-hover: '#56705B'
  text-primary: '#2F3A34'
  text-secondary: '#6B7280'
  subtle-border: rgba(229, 231, 235, 0.75)
typography:
  headline-lg:
    fontFamily: Newsreader
    fontSize: 40px
    fontWeight: '600'
    lineHeight: '1.2'
  headline-lg-mobile:
    fontFamily: Newsreader
    fontSize: 32px
    fontWeight: '600'
    lineHeight: '1.25'
  headline-md:
    fontFamily: Newsreader
    fontSize: 28px
    fontWeight: '500'
    lineHeight: '1.3'
  headline-sm:
    fontFamily: Newsreader
    fontSize: 22px
    fontWeight: '500'
    lineHeight: '1.4'
  body-lg:
    fontFamily: Plus Jakarta Sans
    fontSize: 18px
    fontWeight: '400'
    lineHeight: '1.6'
  body-md:
    fontFamily: Plus Jakarta Sans
    fontSize: 16px
    fontWeight: '400'
    lineHeight: '1.5'
  body-sm:
    fontFamily: Plus Jakarta Sans
    fontSize: 14px
    fontWeight: '400'
    lineHeight: '1.5'
  label-md:
    fontFamily: Plus Jakarta Sans
    fontSize: 14px
    fontWeight: '500'
    lineHeight: '1.4'
  label-sm:
    fontFamily: Plus Jakarta Sans
    fontSize: 12px
    fontWeight: '500'
    lineHeight: '1.4'
  metric:
    fontFamily: Plus Jakarta Sans
    fontSize: 24px
    fontWeight: '600'
    lineHeight: '1.2'
rounded:
  sm: 0.25rem
  DEFAULT: 0.5rem
  md: 0.75rem
  lg: 1rem
  xl: 1.5rem
  full: 9999px
spacing:
  gutter: 1.5rem
  margin: 2rem
  space-xs: 0.25rem
  space-sm: 0.5rem
  space-md: 1rem
  space-lg: 1.5rem
  space-xl: 2rem
  space-2xl: 4rem
  space-3xl: 6rem
---

## Brand & Style

This design system embodies an authentic, emotionally resonant Scandinavian family home atmosphere (Hygge & Hearth). The visual world communicates hospitality, care, domestic peace, and immediate clarity about the services offered, tailored for urban homeowners, working parents, and apartment residents.

- **Design Style:** A fusion of **Minimalism** and **Tactile / Skeuomorphic** nuances, utilizing an asymmetric editorial bento layout, generous macro-whitespace, fluid spring physics, and high-end double-bezel cards.
- **Brand Personality:** Warm, trustworthy, meticulous, inviting, and professional.
- **Emotional Response:** Comfort, security, domestic peace, and absolute reliability.

## Colors

The color palette is anchored by a warm ivory canvas that eliminates visual glare and eye fatigue, complemented by organic sage green for primary actions and warm amber for trusted highlights. 

- **Canvas Background:** `#FAF8F3` serves as the primary foundation surface, resembling warm artisanal paper and light natural birch wood.
- **Card & Container Surface:** `#FFFFFF` pure white for floating surfaces using nested double-bezel architecture.
- **Brand Primary:** `#6B8F71` (Sage Green) for primary CTAs and active states, communicating care, hygiene, and domestic peace.
- **Brand Accent:** `#D9A441` (Warm Amber) restricted to highlight 5-star ratings, policy badges, and tool-kit guarantees.
- **Text & Borders:** Deep organic charcoal (`#2F3A34`) for high-contrast headlines, muted gray-green (`#6B7280`) for metadata, and crisp soft gray dividers.

## Typography

The typography architecture uses a hybrid editorial approach combining literary serif headlines with crisp, modern sans-serif body text.

- **Primary Headlines:** Set in `Newsreader` to deliver a warm, deeply hospitable literary tone.
- **Body & Subtitles:** Set in `Plus Jakarta Sans` for clean legibility across family guidance and interactive form options.
- **Metrics & Numbers:** Utilizes `Plus Jakarta Sans` SemiBold (or `JetBrains Mono` for tabular data) to ensure transparent pricing, square footage, and ratings are instantly scannable.

## Layout & Spacing

The layout model relies on a fluid grid system combined with generous macro-whitespace (`py-24` to `py-32`) to let content breathe deeply and ensure a 0.5-second scanability.

- **Grid Model:** 12-column responsive fluid grid with consistent gutters and outer canvas margins that adapt gracefully across mobile, tablet, and desktop viewports.
- **Spacing Rhythm:** Built on a strict 4px/8px modular scale, emphasizing vertical whitespace to reinforce the calm, uncluttered Scandinavian aesthetic.

## Elevation & Depth

Visual hierarchy is conveyed through high-end double-bezel architecture and subtle tonal layering rather than heavy drop shadows.

- **Double-Bezel Containers:** Cards and interactive surfaces feature an outer rim (`bg-black/[0.02]`) enclosing a pure white (`#FFFFFF`) inner core, creating a refined, floating appearance.
- **Ambient Depth:** Low-opacity, diffused shadows are used sparingly to elevate active modals and floating action bars, preserving a clean, flat-editorial aesthetic.

## Shapes

The shape language relies on friendly, balanced roundedness (`rounded-lg` of 1rem and base elements at 0.5rem) to evoke organic comfort and approachability. Sharp corners are avoided entirely to maintain the soft, welcoming Hygge atmosphere.

## Components

- **Buttons:** Primary buttons use the Sage Green (`#6B8F71`) background with `Plus Jakarta Sans` medium text, featuring tactile push feedback and dark sage (`#56705B`) hover states. Secondary buttons use ghost or outlined styles with crisp soft gray borders.
- **Chips & Badges:** Pill-shaped metadata tags and trust markers accented with Warm Amber (`#D9A441`) for ratings and guarantees.
- **Input Fields:** Clean rounded input wrappers with soft gray borders, deep charcoal text, and subtle focus states reflecting the primary sage color.
- **Cards:** Asymmetric editorial bento cards built with the signature double-bezel architecture, housing service categories (Apartments, Villas, Studios) and Before/After VoL optical inspection previews.
- **Checkboxes & Radios:** Custom-styled circular and square selectors with smooth check transitions in primary sage green.
- **Lists:** Clean itemized service lists with generous line heights and distinct numerical metrics for transparent pricing and time quotas.