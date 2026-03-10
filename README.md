# UsbFileSync

UsbFileSync is a Windows desktop file synchronization tool built with WPF and .NET 8. It is designed for synchronizing a source location and a destination location with support for one-way and two-way sync, preview-first workflows, cancellation, and safe file copy behavior.

## Current Capabilities

- One-way synchronization from source to destination.
- Two-way synchronization using last-write-time reconciliation.
- Persistent `.sync-metadata/file-index.json` tracking for two-way sync sessions, including per-device IDs and `lastSyncedBy` file ownership metadata.
- Detection of new files, modified files, deleted files, renamed files, and empty directories.
- Folder creation and folder deletion synchronization.
- Preview-first workflow with filtered tabs for new, changed, deleted, unchanged, and all items.
- Per-row checkboxes in the preview so only selected planned items are synchronized.
- Per-file progress, transfer speed, queue visibility, and activity logging during synchronization.
- Start and stop synchronization from the main window.
- Safe cancellation for file copy operations.
- Optional SHA-256 checksum validation for each copied file.
- Configurable parallel file copy count, including `0` for unlimited parallel copy operations.
- Settings persistence between runs.
- Browse buttons for selecting source and destination folders.
- Custom application and window icon tailored to the sync workflow.
- Windows shell file icons in the preview so items match Explorer more closely.
- Clickable source and destination preview paths that open Explorer and select the file when possible, plus a right-click menu with `Open file` and `Open file folder` actions.

## Safety Behavior

UsbFileSync now uses a safe copy design for file transfers:

- File copies are written to a temporary file in the destination folder first.
- When the transfer completes successfully, the temporary file is committed into place.
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

### Two-Way

In two-way mode, both sides are compared.

- New files can be copied in either direction.
- Modified files are resolved by comparing last write times, with persisted `.sync-metadata` state used to distinguish true deletions from files that should not be resurrected on the next session.
- New folders can be created on either side.

## User Interface Features

The main window includes:

- Source and destination path selection.
- Sync mode selection.
- `Detect moves` and `Dry run` options with hover tooltips that explain their behavior.
- Optional `Checksums` validation toggle with a tooltip describing the integrity/performance tradeoff.
- `Analyze` and `Synchronize` actions.
- A large synchronization preview table.
- Filter tabs for `New Files`, `Changed`, `Deleted`, `Unchanged`, and `All`.
- Shared selection checkboxes across filtered tabs, including select-all checkboxes in each preview header.
- Action, status, progress, transfer speed, source metadata, and destination metadata columns, with the same column set available across all preview tabs.
- A bottom dashboard with `Remaining queue` and `Activity log`.
- Resizable layout for the preview and bottom dashboard.
- Adjustable width split between the queue and the activity log.

## Settings

The settings dialog currently supports:

- `Parallel copies`: number of file copy operations allowed to run at the same time.
- `0` means unlimited parallel copy operations for the current copy batch.

The main sync settings area also supports:

- `Checksums`: validates each copied file with SHA-256 before it is committed into place.

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
- Two-way sync persists pairwise metadata inside `.sync-metadata`, but conflict resolution still falls back to last write times when both sides changed the same file between sync sessions.
- The app is currently Windows-only.

## Development Notes

- New functionality should include or update automated tests.
- New functionality should also be reflected in this README so the documented feature set stays current.
