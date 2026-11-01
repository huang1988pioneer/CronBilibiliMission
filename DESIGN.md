# Cookie reader UI

Traditional Chinese desktop workbench. The main task is to read four Cookie values, inspect their expiry, and copy or publish them to GitHub Secrets.

## Layout

- A compact white header contains the application name and the three-step task description.
- The 328 px settings column contains file intake, expiry, expandable GitHub settings, and expandable background recorder status. It scrolls independently.
- The results panel contains its own heading, a scrollable list of four fields, and a pinned verification/export action bar.
- Each field uses a vertical stack: name, wrapping read-only value with a separate copy button, then wrapping metadata and expiry warnings. Content determines height.
- Account information and recommendations remain available below the fields in an expander. Long titles and descriptions wrap without height caps.
- The bottom status message wraps across the window width.

## Typography and color

Segoe UI with Microsoft JhengHei fallbacks. Main text 14 px, headings 16/24 px, supporting metadata 12 px. Monospace is reserved for Cookie values. Controls have a minimum height of 40 px, with 36 px compact actions.

Background `#EEF1F5`, white panels, field background `#F5F7FA`, ink `#1A1D23`, secondary text `#4E5560`, borders `#DCE2EA`. Rose `#D94877` marks primary actions. Expiry and status colors retain their existing meanings.

## Verification

The initial desktop view was visually inspected. Headless tests measure the actual Avalonia controls at 960×640, 1080×740, 1180×860, and 1440×900. They check field separation, growing long values and metadata, scrolling to the final field, pinned actions, and expanded settings access. Tests attach data after opening to avoid loading local credentials or starting network services.

## Release

Pushing a `v*` tag whose version matches the project runs tests, builds four self-contained runtime packages, generates SHA-256 checksums, and publishes a GitHub Release. Version-specific release notes live in `docs/releases/`.
