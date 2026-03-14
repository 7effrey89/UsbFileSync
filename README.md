# UsbFileSync

UsbFileSync is a Windows desktop file synchronization tool built with WPF and .NET 8. It is designed for synchronizing a source location and one or more destination locations with support for one-way and two-way sync, preview-first workflows, cancellation, and safe file copy behavior.

## Supported Filesystems

UsbFileSync now routes sync IO through a volume abstraction instead of assuming every source and destination is a plain Windows path.

- **Windows mounted volumes** (`WindowsMountedVolume`): read/write using normal `System.IO` paths.
- **Linux ext volumes**: UsbFileSync can browse mounted ext volumes without elevation when Windows exposes a readable raw mounted-volume handle such as `\\.\D:`. When UsbFileSync is launched elevated and can resolve the selected drive back to its underlying `PhysicalDriveN`, ext2/ext3/ext4 destinations can also be written through the bundled SharpExt4 backend. If you try to sync to an ext4 destination without elevation, the app will offer to relaunch itself as administrator.
- **macOS HFS+ volumes**: **read-only by design**. On Windows, UsbFileSync can probe drive roots such as `D:\` as HFS+ (`Mac OS Extended (Journaled)`) sources through an embedded DiscUtils HFS+ backend. Any write attempt against an HFS+ target is still rejected before the sync can modify the volume.

The HFS+ backend intentionally enforces read-only behavior so the application does not expose unsupported write-back flows. The source-side browse flow also uses a shell-free drive picker so selecting an unreadable macOS/removable drive does not invoke the standard Windows folder browser and its format-disk prompt first.

## Current Capabilities

- One-way synchronization from source to destination.
- Two-way synchronization using last-write-time reconciliation.
- Optional multiple destination paths so the same source can be synchronized to more than one destination in the same analyze/sync session.
- Persistent `.sync-metadata/file-index.json` tracking shared across one-way and two-way sync sessions, including per-root IDs plus both `LastSyncedByRootId` and friendly `LastSyncedByRootName` metadata for debugging file ownership history.
- Detection of new files, modified files, deleted files, renamed files, and empty directories.
- Folder creation and folder deletion synchronization.
- Preview-first workflow with filtered tabs for new, changed, deleted, unchanged, and all items.
- Per-row checkboxes in the preview so only selected planned items are synchronized.
- The busy overlay now includes a `Cancel` action while preview analysis is running, so long preview builds can be stopped without waiting for completion.
- Completed preview rows remain visible after synchronization and are marked done until the next analyze refresh, while already-applied rows are retired from future queue selection.
- Per-file progress, transfer speed, queue visibility, and activity logging during synchronization.
- Start and stop synchronization from the main window.
- Safe cancellation for file copy operations.
- Optional SHA-256 checksum validation for each copied file.
- Configurable parallel file copy count, including `0` for adaptive auto parallelism.
- Settings persistence between runs.
- Browse buttons for selecting the source folder/drive and one or more destination folders. The custom browser now covers both flows from one UI: sources can navigate normal Windows folders, HFS+ source folders, and mounted ext source folders, while destinations can navigate normal Windows folders and mounted ext destination folders. Destination browsing uses lightweight ext-volume discovery so the picker opens quickly, while writable ext4 validation remains part of sync-time validation.
- Read-only source and destination path fields that show Explorer-style drive names such as `XTIVIA (F:)` when unfocused, and the raw path when focused for easy copy/select behavior.
- Custom application and window icon tailored to the sync workflow.
- Windows shell file icons in the preview so items match Explorer more closely.
- Clickable source and destination preview paths that open Explorer and select the file when possible, plus a right-click menu with `Open file` and `Open file folder` actions.
- A `Show comparison` action on the source preview item context menu that opens a side-by-side source/destination comparison dialog with metadata and file previews.
- Sync planning and execution through pluggable `IVolumeSource` backends so non-mounted filesystems can be integrated without rewriting the core planner.
- Embedded PDF preview in the comparison dialog using WebView2, plus an embedded media player for common audio and video formats when the local Windows codecs support them.
- Office document preview for `docx`, `pptx`, `xlsx`, and related macro-enabled/template variants using built-in text extraction, with a Microsoft Office application fallback for Word, PowerPoint, and Excel files that cannot be parsed directly on the local machine.
- A **Previewer** dropdown on every comparison pane that lets you switch between the built-in viewer and the Windows Shell preview handler. Office documents offer additional Open XML and Office Interop modes.
- Preview provider mappings in `Application Settings` so you can assign file extensions to the `Text`, `Image`, `Office`, `PDF`, `Media`, or `Unsupported` preview providers.

### Comparison Preview Modes

Every supported file type in the comparison dialog has a **Previewer** dropdown at the top-right corner of each pane. The default selection depends on the file type, and additional modes appear when they are supported for that format.

| File type | Default mode | Description |
|---|---|---|
| **Text** | Text Viewer | Displays the file content as plain text in a scrollable text block. |
| **Image** | Image Viewer | Renders the image with zoom in/out support. |
| **PDF** | PDF Viewer | Displays the PDF using an embedded WebView2 control. |
| **Media** | Media Player | Plays the audio or video file with play/pause/stop controls. |
| **Office** | Shell Preview when available, otherwise Open XML | Uses the Windows Shell preview handler when installed; otherwise falls back to built-in text extraction, with Office Interop also available when Microsoft Office is installed. |

Text, image, PDF, and media files offer **Shell Preview** as a second option when a Windows Shell preview handler is registered for that file type. When selected, the pane loads the same preview you see in File Explorer's preview pane. If no handler is registered, the option is hidden from the dropdown.

### Office Document Preview Modes

Office documents (`.docx`, `.pptx`, `.xlsx`, and related variants) have three dedicated modes:

| Mode | Description | Requirements | Best for |
|---|---|---|---|
| **Shell Preview** (default) | Uses the Windows Shell preview handler to render a native, high-fidelity preview of the document. This is the same preview you see in File Explorer's preview pane. | A registered Shell preview handler for the file type, typically installed with Microsoft Office or the free Office viewers. | Rich visual previews with formatting, images, and layout intact. |
| **Open XML** | Parses the Office Open XML package directly using the Open XML SDK and ExcelDataReader. Extracts the text content without launching any external application. | None — fully built-in. | Quick text-only previews when Office is not installed, or for lightweight comparison of document content. |
| **Office Interop** | Launches the corresponding Office application (Word, Excel, or PowerPoint) invisibly via COM automation, opens the document read-only, and reads its text content. | Microsoft Office installed on the machine. | Encrypted or complex documents that Open XML cannot parse, or when you need the Office application's own text rendering. |

If Shell Preview fails (no handler registered or the handler returns an error), the pane displays the error message and you can switch to another mode. Similarly, if Open XML or Office Interop extraction fails, diagnostic details are shown in the pane.

### Using Show Comparison

1. Click `Analyze` to build the synchronization preview.
2. In the preview table, right-click the source item name for the row you want to inspect.
3. Select `Show comparison`.
4. Review the side-by-side source and destination panes, including metadata, file previews, and clickable full paths.

![Show comparison preview](docs/images/Screenshot%202026-03-11%20234859.png)

## Safety Behavior

UsbFileSync now uses a safe copy design for file transfers:

- File copies are written to a temporary file in the destination folder first.
- When the transfer completes successfully, the temporary file is committed into place.
- Copied files have their destination last-write timestamp reset to the source timestamp, and preview analysis tolerates small filesystem rounding differences such as the 2-second granularity common on some removable drives.
- If synchronization is cancelled or a copy fails, the temporary file is deleted.
- Interrupted overwrites do not corrupt the existing destination file.

This means stopping a synchronization does not leave half-written files behind at the final destination path.

## Synchronization Modes

### One-Way

In one-way mode, the source location is treated as the source of truth.

- New and modified files are copied from source to destination.
- Deleted files are removed from destination.
- New folders are created on destination.
- Missing folders on source are removed from destination when empty.
- When multiple destinations are configured, the one-way planner queues the same source-driven changes separately for each destination.
- Optional `Detect moves` support can turn a matching delete-plus-create pair into a rename or move on the destination instead of recopying the file. 
Detect moves only affects one-way planning. When the option is enabled, the planner looks for a file that exists only on the source and a matching file that exists only on the destination with the same fingerprint, then turns that into a MoveOnDestination action. That means instead of “copy the new path and delete the old path”, it can do “rename/move the existing destination file”.

- Successful one-way sync also refreshes the shared `.sync-metadata` baseline so later two-way sync sessions have an up-to-date history.

### Two-Way

In two-way mode, the source and each configured destination are compared pair-by-pair.

- New files can be copied in either direction.
- Modified files are resolved by comparing last write times, with persisted `.sync-metadata` state used to distinguish true deletions from files that should not be resurrected on the next session.
- New folders can be created on either side.
- When multiple destinations are configured, each destination is analyzed and synchronized independently against the same source location during the same run.
- `Detect moves` does not currently apply in two-way mode, so the checkbox is disabled in the UI when `TwoWay` is selected.

## Metadata Model

UsbFileSync persists synchronization history in `.sync-metadata/file-index.json` on both sides of the sync pair.

At a high level the document contains:

- `RootId`: the stable ID generated for the current sync root.
- `RootName`: a friendly name for the current sync root, usually a drive label like `XTIVIA (F:)`, `New Volume (D:)`, or the root path when no drive label is available.
- `PeerStates`: a dictionary keyed by the other side's `RootId`.

Each `PeerStates` entry represents the shared baseline between the current root and one peer root:

- `PeerRootName`: a friendly name for the peer identified by that `PeerStates` key.
- `RecordedAtUtc`: when that peer-state snapshot was last written.
- `Entries`: the tracked files for that peer relationship, keyed by relative path.

Each file entry inside `Entries` contains:

- `RelativePath`: the file path relative to the sync root.
- `Length`: the file size in bytes when the baseline was recorded.
- `LastWriteTimeUtc`: the file's filesystem modified time in UTC.
- `ChecksumSha256`: an optional SHA-256 hash recorded for files copied while `Checksums` was enabled, reused later when the tracked file still matches the stored size and modified time.
- `IsDeleted`: whether the file is currently tracked as deleted in the shared baseline.
- `DeletedAtUtc`: when the deletion was recorded, or `null` if the file is not tracked as deleted.
- `LastSyncedByRootId`: the root ID of the side whose version was last accepted for that file.
- `LastSyncedByRootName`: a friendly name for `LastSyncedByRootId`, usually the source folder.

Important distinctions:

- `RecordedAtUtc` is when UsbFileSync saved the metadata snapshot. It is not the file's modified time.
- `LastWriteTimeUtc` comes from the real filesystem timestamp, stored in UTC. It is not derived from the filename.
- `LastSyncedByRootId` and `LastSyncedByRootName` describe which side last supplied the accepted version of that file. They do not mean the metadata file itself “belongs” to that side.

Example interpretation:

- If an entry has `LastSyncedByRootName: "New Volume (D:)"`, that means the `D:` side was the last side whose version won for that file.
- If an entry has `IsDeleted: true`, the metadata is preserving a deletion baseline so a later two-way sync does not accidentally recreate the file from the other side.

## User Interface Features

The main window includes:

- Source path selection plus add/remove controls for multiple destination paths.
- Sync mode selection.
- `Detect moves` and `Dry run` options with hover tooltips that explain their behavior.
- Optional `Checksums` validation toggle with a tooltip describing the integrity/performance tradeoff.
- `Analyze` and `Synchronize` actions.
- A large synchronization preview table.
- Filter tabs for `New Files`, `Changed`, `Deleted`, `Unchanged`, and `All`.
- Shared selection checkboxes across filtered tabs, including select-all checkboxes in each preview header.
- Edit menu actions for `Select All In Tab`, `Select By Pattern`, and `Invert Selection` against the active preview tab.
- Action, status, sync action, transfer speed, drive location, source metadata, and destination metadata columns, with the same column set available across all preview tabs.
- Excel-style column filter dropdowns on the preview table headers, with searchable value lists, sort controls, and bulk select or deselect actions for narrowing the current tab.
- Source and destination paths stay underlined for clickability, and only the side that will be changed is color-highlighted in the preview.
- The sync action column uses a directional chevron that fills as each item progresses, while the Action column keeps the raw function name for the planned operation.
- Rounded progress bars remain in the remaining queue panel.
- A bottom dashboard with `Remaining queue` and `Activity log`.
- View menu actions for toggling the bottom info boxes and resetting the layout split.
- Resizable layout for the preview and bottom dashboard.
- Adjustable width split between the queue and the activity log.
- A Help menu with an About dialog showing the app name, version, and GitHub repository link.

## Settings

The settings dialog currently supports:

- `Parallel copies`: number of file copy operations allowed to run at the same time.
- `0` enables auto mode, which estimates a starting parallelism and adjusts it during the copy batch.
- `Hide macOS system files in HFS+ preview and sync planning`: filters common filesystem metadata such as `.Spotlight-V100`, `.fseventsd`, `.journal`, and `HFS+ Private Data` out of the HFS+ sync view.

The main sync settings area also supports:

- `Checksums`: validates each copied file with SHA-256 before it is committed into place.
- When checksum validation is enabled during a copy, UsbFileSync also stores the verified SHA-256 in metadata so later verified copies can skip recomputing the source hash when that source file is unchanged.
- Successful checksum-enabled syncs also say that checksum verification passed, so the UI confirms the extra validation actually ran.

## Project Structure

- `UsbFileSync.App`: WPF desktop application, views, dialogs, and view models.
- `UsbFileSync.Core`: synchronization models, strategies, services, and settings persistence contracts.
- `UsbFileSync.Tests`: xUnit test project covering core logic and selected UI-facing behavior.

## Build And Run

### Build the solution

```powershell
dotnet build UsbFileSync.sln -v minimal
```

### Run the desktop app

From the solution root, `dotnet run` still needs an explicit startup project because the solution contains multiple projects. Use the app project directly, or launch `Launch UsbFileSync App` from VS Code.

```powershell
dotnet run --project UsbFileSync.App/UsbFileSync.App.csproj
```

### Run the test suite

```powershell
dotnet test UsbFileSync.Tests/UsbFileSync.Tests.csproj -v minimal
```

## Testing Approach

The solution includes automated coverage for:

- sync planning for one-way and two-way modes
- overwrite action classification
- folder creation and deletion handling
- preview categorization
- settings persistence
- queue path formatting
- view model interaction behavior
- safe cancellation of in-progress file copies

## Known Limitations

- Cancellation is safe, but it is not a true resume system. Restarting synchronization re-analyzes the file set and starts the interrupted file from the beginning.
- One-way and two-way sync both persist pairwise metadata inside `.sync-metadata`, but two-way conflict resolution still falls back to last write times when both sides changed the same file between sync sessions.
- The app is currently Windows-only.
- Linux volumes still depend on the bundled SharpExt4 backend for write access. If the app is not elevated, or if the selected drive cannot be reopened through `PhysicalDriveN`, UsbFileSync falls back to read-only ext browsing for that volume.
- HFS+ volumes are intentionally treated as read-only targets and will throw a read-only volume error if selected as a write destination.
- If the HFS+ backend cannot open the selected drive, analyze and sync will stop with the HFS+ volume error reported by the app.

## Development Notes

- New functionality should include or update automated tests.
- New functionality should also be reflected in this README so the documented feature set stays current.
