# MERQO VERTEXA — UI/UX Design System

## 1. Design intent

A precision instrument, not a game overlay: deep charcoal surfaces, machined
spacing, restrained cyan instrumentation accents, data-forward typography, zero
gratuitous glow. Every pixel of colour must carry meaning (state), never decoration.

## 2. Design tokens (`src/MERQO.VERTEXA.App/Themes/Tokens.*.xaml`)

### Colour (dark — default)
| Token | Value | Use |
|---|---|---|
| `Surface0` | `#0B0F14` | app background |
| `Surface1` | `#111823` | cards |
| `Surface2` | `#18212E` | raised / hover |
| `Surface3` | `#202B3A` | inputs, pressed |
| `Stroke` | `#26323F` | hairline borders |
| `StrokeStrong` | `#33455A` | focus/selected borders |
| `TextPrimary` | `#E8EEF6` | headings, values |
| `TextSecondary` | `#9FB0C3` | labels |
| `TextTertiary` | `#6B7C8F` | metadata, sources |
| `Accent` | `#2FD4E8` | primary action, instrument accent |
| `AccentDeep` | `#1B8FA3` | pressed/active |
| `Info` | `#4FA8FF` | informational |
| `Success` | `#37D67A` | healthy |
| `Warning` | `#F5B042` | degraded |
| `Critical` | `#FF5D5D` | failing |
| `Unknown` | `#7A8AA0` | unavailable/insufficient evidence |

Light theme mirrors the semantic names with `#F6F8FB / #FFFFFF / #EDF1F7`
surfaces and darkened accents (`#0C7C8C` accent) for AA contrast.

### Typography
`Segoe UI Variable Display`/`Segoe UI` (fallback chain), monospace
`Cascadia Mono`/`Consolas` for measured values.
Scale: Display 28/34 · H1 22 · H2 17 · Body 13 · Small 12 · Micro 11 (letter-spaced
uppercase for labels). Numeric values always monospace + tabular figures.

### Spacing / radius / elevation
4-pt grid: 4, 8, 12, 16, 20, 24, 32, 40. Radius: 6 (controls), 10 (cards),
14 (modals). Elevation: 1 `0 1 2 #0006`, 2 `0 4 12 #0008`, 3 `0 12 32 #000A`.
Motion: 120 ms (micro), 180 ms (element), 260 ms (panel); easing
`cubic-bezier(0.2,0,0,1)`; all motion suppressed under *Reduced motion*.

## 3. Layout

```
┌──────────┬─────────────────────────────────────────────────────────────┐
│ MERQO    │  Top bar: page title · breadcrumb · search (Ctrl+K) · status │
│ VERTEXA  ├─────────────────────────────────────────────────────────────┤
│ ──────   │                                                             │
│ Dash     │   Content: responsive card grid (12-col, min 320 px)       │
│ System   │   ≥1440: 4 col · 1200: 3 · 960: 2 · <860: 1 (sidebar rail) │
│ CPU      │                                                             │
│ GPU      │                                                             │
│ Memory   │                                                             │
│ Storage  │                                                             │
│ Battery  │                                                             │
│ Display  │                                                             │
│ Keyboard │                                                             │
│ Audio    │                                                             │
│ Camera   │                                                             │
│ Network  │                                                             │
│ Drivers  │                                                             │
│ Perf     │                                                             │
│ Reports  │                                                             │
│ Settings │                                                             │
│ About    │                                                             │
└──────────┴─────────────────────────────────────────────────────────────┘
```
Sidebar is 232 px, collapses to a 64 px icon rail below 1100 px width, animated
with a width storyboard that is disabled under reduced motion.

## 4. Component states

Every data-bearing component has all of: `Loading` (skeleton shimmer), `Available`,
`Unavailable` (dashed border, `Unknown` colour, reason text, action hint),
`AccessDenied` (lock glyph + "run elevated"), `NotTested` (off-white outline),
`Invalid`. No component may render a blank box: an unknown metric always shows its
reason.

## 5. Accessibility

- Minimum contrast 4.5:1 for body text, 3:1 for large text and 3:1 for focus rings.
- All interactive elements reachable by Tab, visible focus ring (`StrokeStrong` +
  2 px accent inset), `Enter`/`Space` activation.
- Icons never carry meaning alone (always paired with text or tooltip).
- Data visualisations have text equivalents (the value and the source are in the
  card, not only in the chart).
- Full keyboard operation of the display and keyboard labs.
- Reduced-motion preference persists and disables all non-essential animation.

## 6. Copy rules

Labels are nouns (`Battery wear`), values are `value + unit`, sources are shown as
`Source · method · collected 14:32:07`. Unavailable copy is *specific and honest*:
"Design capacity not exposed by this battery's firmware" — never "N/A" alone.
