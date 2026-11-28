# Cookie reader UI

Traditional Chinese desktop workbench. The main task is to read four Cookie values, inspect their expiry, and copy or publish them to GitHub Secrets.

## Layout

- The white header pairs a layered-file mark with the application name and a local parsing indicator.
- The 320 px settings column scrolls independently. Choose an account and open a file with the blue primary button, drop it into the window, or paste a path and press Enter. Expiry reminders, GitHub settings and recorder status sit below the intake controls.
- Before import, a blueprint diagram connects `cookies.txt` to the four field names. The footer lists the file picker and reload shortcuts. After import, the diagram gives way to a scrollable field list with a pinned verification and export toolbar.
- Each field uses a vertical stack: name, wrapping read-only value with a separate copy button, then wrapping metadata and expiry warnings. Content determines height.
- Account information and recommendations remain available below the fields in an expander. Long titles and descriptions wrap without height caps.
- The bottom status message wraps across the window width.

## Typography and color

Inter with system Traditional Chinese fallbacks. Body text is 14 px, headings are 20–25 px, and metadata is 12 px. Field names, values and keyboard shortcuts use monospace. Standard buttons and inputs are at least 42 px tall, with 36 px compact actions.

The palette in `BilibiliCookieReader/App.axaml` uses an ice-blue workspace, white surfaces, navy text and cobalt primary actions. Teal traces connect the diagram to the field names. Expiry reminders pair their state colors with text.

## Verification

The initial desktop view was visually inspected. Headless tests measure the actual Avalonia controls at 960×640, 1080×740, 1180×860, and 1440×900. They check field separation, growing long values and metadata, scrolling to the final field, pinned actions, and expanded settings access. Tests attach data after opening to avoid loading local credentials or starting network services.

## Release

Pushing a `v*` tag whose version matches the project runs tests, builds four self-contained runtime packages, generates SHA-256 checksums, and publishes a GitHub Release. Version-specific release notes live in `docs/releases/`.
