# UsbFileSync

UsbFileSync is a Windows desktop file synchronization tool built with WPF and .NET 8. It is designed for synchronizing a source location and a destination location with support for one-way and two-way sync, preview-first workflows, cancellation, and safe file copy behavior.

## Current Capabilities

- One-way synchronization from source to destination.
- Two-way synchronization using last-write-time reconciliation.
- Persistent `.sync-metadata/file-index.json` tracking shared across one-way and two-way sync sessions, including per-root IDs plus both `LastSyncedByRootId` and friendly `LastSyncedByRootName` metadata for debugging file ownership history.
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
- Read-only source and destination path fields that show Explorer-style drive names such as `XTIVIA (F:)` when unfocused, and the raw path when focused for easy copy/select behavior.
- Custom application and window icon tailored to the sync workflow.
- Windows shell file icons in the preview so items match Explorer more closely.
- Clickable source and destination preview paths that open Explorer and select the file when possible, plus a right-click menu with `Open file` and `Open file folder` actions.

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
- Optional `Detect moves` support can turn a matching delete-plus-create pair into a rename or move on the destination instead of recopying the file. 
Detect moves only affects one-way planning. When the option is enabled, the planner looks for a file that exists only on the source and a matching file that exists only on the destination with the same fingerprint, then turns that into a MoveOnDestination action. That means instead of “copy the new path and delete the old path”, it can do “rename/move the existing destination file”.

- Successful one-way sync also refreshes the shared `.sync-metadata` baseline so later two-way sync sessions have an up-to-date history.

### Two-Way

In two-way mode, both sides are compared.

- New files can be copied in either direction.
- Modified files are resolved by comparing last write times, with persisted `.sync-metadata` state used to distinguish true deletions from files that should not be resurrected on the next session.
- New folders can be created on either side.
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

- Source and destination path selection.
- Sync mode selection.
- `Detect moves` and `Dry run` options with hover tooltips that explain their behavior.
- Optional `Checksums` validation toggle with a tooltip describing the integrity/performance tradeoff.
- `Analyze` and `Synchronize` actions.
- A large synchronization preview table.
- Filter tabs for `New Files`, `Changed`, `Deleted`, `Unchanged`, and `All`.
- Shared selection checkboxes across filtered tabs, including select-all checkboxes in each preview header.
- Action, status, sync action, transfer speed, source metadata, and destination metadata columns, with the same column set available across all preview tabs.
- Source and destination paths stay underlined for clickability, and only the side that will be changed is color-highlighted in the preview.
- The sync action column uses a directional chevron that fills as each item progresses, while the Action column keeps the raw function name for the planned operation.
- Rounded progress bars remain in the remaining queue panel.
- A bottom dashboard with `Remaining queue` and `Activity log`.
- Resizable layout for the preview and bottom dashboard.
- Adjustable width split between the queue and the activity log.

## Settings

The settings dialog currently supports:

- `Parallel copies`: number of file copy operations allowed to run at the same time.
- `0` means unlimited parallel copy operations for the current copy batch.

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

## Development Notes

- New functionality should include or update automated tests.
- New functionality should also be reflected in this README so the documented feature set stays current.
