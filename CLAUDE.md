# Zebra Printer Configurator - Project Conventions

## UI markup: Material Web Components

For **new or changed** `.razor` markup, use `@material/web`'s custom elements (`<md-filled-button>`,
`<md-outlined-button>`, `<md-list>`, `<md-list-item>`, `<md-elevation>`, etc.) instead of plain HTML
elements or Bootstrap classes. This applies going forward only - existing Bootstrap-based markup
(most of the app, as of the UI restyle that predates this convention) is not being retroactively
migrated; touch it only if you're already changing that file for another reason.

The component library is vendored locally, not loaded from a CDN - this app must keep working with
no internet access (Bluetooth-only) in the field, the same reason no Google Fonts or other CDN
dependency is used anywhere else in the app:

- `src/Zebra.Printer.Configurator.App/wwwroot/lib/material-web/all.bundle.js` - a fully self-contained
  bundle of `@material/web` v2.5.0 (Lit and all transitive dependencies inlined, zero external
  imports - see the file's own header comment for provenance and how to update it), loaded via
  `<script type="module">` in `index.html`. Importing it registers every `<md-*>` element globally,
  so no per-component import is needed in individual `.razor` files.
- `src/Zebra.Printer.Configurator.App/wwwroot/lib/material-web/theme.css` - brand-color token
  overrides (see below). Every `<md-*>` component already has Material's own baseline color scheme
  built in as a CSS fallback, so components render correctly even without this file.

## CSS: semantic Material tokens, not fixed hex codes

In custom CSS (`app.css`, `.razor.css` files), reference Material 3's semantic color tokens
(`var(--md-sys-color-primary)`, `var(--md-sys-color-surface)`, `var(--md-sys-color-on-surface)`,
etc. - see `theme.css`'s doc comment for the full set `@material/web` defines) rather than hardcoded
hex values, so a future theme change updates every consumer at once. `app.css` currently also defines
its own `--color-*` tokens (from the pre-Material-Web restyle) - prefer the `--md-sys-color-*` tokens
for anything new; the two systems aren't unified yet.

`theme.css` only overrides the primary-related tokens (with this app's existing brand blue,
`#146C94`) and is a **hand-picked approximation**, not a true Material 3 tonal palette - generating
one properly requires the HCT color space algorithm (Material Theme Builder, or the
`@material/material-color-utilities` package), which needs real JS tooling (Node/npm) this project
doesn't have. If that tooling becomes available, regenerate `theme.css` properly rather than
hand-picking further token values.

## Platform-specific API bindings: DI, not direct calls

When a feature needs system-level/platform integration (e.g. matching the Android status bar color,
or any other native API not exposed through MAUI's own cross-platform abstractions), follow this
project's existing pattern throughout `Infrastructure.Android`: define an interface in
`Core/Abstractions/`, implement it in `Infrastructure.Android/` against the real Android API, and
register it in `App/MauiProgram.cs`. Never call Android APIs directly from `Core` or `UI` project
code (they can't - those projects don't reference Android - but the same separation applies even in
places that technically could). See `IAppLog`/`AppLog`, `IHostNetworkInfoService`/
`HostNetworkInfoService`, `IBluetoothPermissionService`/`BluetoothPermissionService` for the existing
shape of this pattern.

## Established release workflow

After any code change: `dotnet build Zebra.Printer.Configurator.slnx -c Release` must be 0
warnings/0 errors; run all three test suites (`dotnet test` on
`tests/Zebra.Printer.Configurator.UnitTests`, `ComponentTests`, `IntegrationTests`); commit with a
detailed message; push to `origin/develop`.

`ApplicationVersion` (the Android build number/versionCode) is no longer hand-bumped - it's
auto-computed in `Zebra.Printer.Configurator.App.csproj`'s `SetAppVersion` target from the current
UTC date/time (mirrors `ECommerce.Mobile.SwiftPick/Fetch/Fetch.csproj`'s scheme), so every build
already gets a fresh, monotonically increasing value. Only bump `VersionMajor`/`VersionMinor` (which
`ApplicationDisplayVersion` is built from) by hand, and only for an actual user-facing release, not
every commit.
