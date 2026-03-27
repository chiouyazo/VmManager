# VM Manager UI Guide

This doc describes the target UI design for VM Manager. It follows the StorkDrop design philosophy (see `storkdrop-design-philosophy.md`) adapted with VM Manager's blue accent color.

## Color Tokens

| Token | Value | Usage |
|---|---|---|
| Accent | #2196F3 | Primary buttons, active states, progress bars, accent top bar |
| AccentDark | #1976D2 | Hover states on accent elements |
| AccentLight | #E3F2FD | Hover background on outlined buttons, tag badges |
| Success | #2E7D32 | Running VM state, connected indicators |
| Error | #D32F2F | Error messages, danger buttons, stopped indicators |
| Warning | #F57C00 | Warning banners |
| Surface | #F5F5F5 | Sidebar, status bar, expander headers |
| Background | #FFFFFF | Content area, cards |
| OnSurface | #1A1A1A | Primary text |
| OnSurfaceVariant | #666666 | Secondary text, descriptions |
| Border | #E0E0E0 | Card borders, dividers |
| Hint | #999999 | Placeholder text |

## Layout

- Sidebar: 220px, Surface background
- Content: flexible, 24px padding
- Status bar: bottom, Surface background, shows Hyper-V/Docker/Registry status

## MVVM Architecture

Every page follows the Model-View-ViewModel pattern. Business logic lives in ViewModels, not in XAML code-behind files. Code-behind contains only DataContext wiring and minimal UI event handling.

| Page | ViewModel | Responsibility |
|---|---|---|
| Marketplace | `ImagesViewModel` | Catalog loading, filtering, import orchestration |
| My VMs | `MyVmsViewModel` | VM listing, start/stop/delete, snapshot operations |
| Settings | `SettingsViewModel` | Feed configuration, VM defaults, save/load |
| Main Window | `MainWindowViewModel` | Status bar state, navigation |
| Setup Wizard | `SetupWizardViewModel` | First-run configuration flow |

ViewModels do not access the filesystem directly. All persistence is delegated to services behind interfaces (`IVmTrackingService`, `ILocalImageMetadataService`, `IAppPaths`).

All user-visible strings are in `Properties/Resources.resx` for localization support.

## Pages

### Marketplace

**Header:** Title "Marketplace" (28px SemiBold) with search bar and refresh button on the right.

**Search bar:** Rounded container with search icon inside, placeholder "Search images...", filters as you type by name/description/features.

**Filters row:** Below search, shows dropdowns for Source (All/OCI/Nexus/Local) and clickable tag pills for features. Clicking a feature badge adds it as a filter. Active filters show as removable pills.

**Image cards:** WrapPanel layout (responsive grid). Each card is 280px wide:
- Blue accent top bar (4px)
- Image name (18px Bold) with source badge (OCI/Nexus/Local pill)
- Description (13px, OnSurfaceVariant)
- Feature tags as small pills
- Version list with Import/Create buttons
- Shared Snapshots section (when present)

**Loading:** Semi-transparent overlay with centered progress bar + "Loading catalog..." text. No inline loading bars between content.

### My VMs

**Header:** Title "My VMs" (28px SemiBold) with refresh button.

**Grouping:** Expander groups: "Hyper-V" (managed, expanded), "Hyper-V (External)" (collapsed), "Docker" (collapsed).

**VM cards:** Each card shows:
- Row 1: VM name (15px SemiBold), state badge (colored pill), origin link
- Row 1 right: Action buttons (Start, Stop, Connect, Rename, Delete)
- Row 2: Memory, uptime, notes input
- Row 3: Snapshot expander with lazy-loaded list

**Snapshot section:** Collapsible with "Snapshots (N)" header. Each snapshot has Restore/Clone/Push/Delete buttons. Create form at the bottom with name input + Create button + Reset to base button.

**Progress:** When an operation is running, a progress bar with status text appears below the status bar. The notification pane (green/red) only shows results after completion.

**Button states:** Rename, Delete, Snapshot, Restore require the VM to be Off. Disabled buttons show "Requires the VM to be turned off" tooltip.

### Settings

**Layout:** Scrollable, max-width 700px centered.

**Sections (each in a card):**
1. **Feeds** - Configurable list of feed entries (OCI, Nexus, or Local), each with type-specific fields (URL, credentials, repository, catalog path). Managed via `FeedEntryViewModel`.
2. **VM Defaults** - Memory (slider), CPU (slider), VM storage path, credentials
3. **Locale** - Apply locale checkbox, locale dropdown (en-US, de-DE, etc.), keyboard layout dropdown (US, German, etc.)

All inputs use the global TextBox style with rounded corners and placeholder text.

### Setup Wizard (first run)

3-step wizard (700x500, not resizable):
1. **Welcome** - Logo, app name, description
2. **Configuration** - VM storage path, memory/CPU defaults, locale/keyboard selection
3. **Feeds** - Configure OCI registry and/or Nexus and/or local path, test connection

Bottom bar: step indicator + Back/Next/Finish buttons.

## Component Rules

### No emoji as icons
Use Segoe MDL2 Assets for all icons. Map:
- Start: &#xE768;
- Stop: &#xE71A;
- Connect: &#xE703;
- Delete: &#xE74D;
- Rename: &#xE70F;
- Snapshot: &#xE787;
- Restore: &#xE777;
- Clone: &#xE8C8;
- Push: &#xE898;
- Refresh: &#xE72C;
- Search: &#xE721;

### No stacked notification bars
Status messages use the notification pane (Row 2) for results only. Progress goes in a separate area with its own text. Never both at the same time.

### Consistent progress bars
Height: 4px everywhere. CornerRadius: 2px. Background: #F5F5F5. Foreground: Accent.

### Card shadows
All cards: DropShadow(BlurRadius=6, ShadowDepth=1, Opacity=0.1). Slightly stronger than current 0.06.

### Scrollbar
Visible thumb (6px wide, CornerRadius 3, #CCCCCC default, #999999 hover). Track is transparent.

### Locale/Keyboard dropdowns
Locale shows friendly names: "English (United States)", "German (Germany)", etc.
Keyboard shows layout names: "US (QWERTY)", "German (QWERTZ)", etc.
Both map to the correct system identifiers internally.
