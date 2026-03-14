# Future Improvements

This note captures suggested follow-up work after the recent worker-process refactor.

## Current State

UsbFileSync now separates concerns more cleanly than before:

- `UsbFileSync.Core` owns sync logic and domain models.
- `UsbFileSync.Platform.Windows` owns Windows-specific volume access and sync preparation.
- `UsbFileSync.Contracts` owns worker protocol messages and serialization helpers.
- `UsbFileSync.App` owns the WPF UI and worker/session orchestration.

That is a solid intermediate architecture, but there are still some areas that can be improved.

## Suggested Next Steps

### 1. Split the worker into its own executable

Right now the worker runs in headless mode inside `UsbFileSync.App.exe`.

Possible improvement:

- Create a dedicated `UsbFileSync.Worker` project.
- Keep `UsbFileSync.App` responsible only for UI and orchestration.
- Keep `UsbFileSync.Worker` responsible only for sync execution.

Benefits:

- Cleaner startup model.
- Tighter process boundary between UI and worker.
- Easier packaging, logging, and testing of the worker independently.
- Less risk of UI-specific concerns leaking into worker startup.

### 2. Re-analyze in the worker before executing

The current worker executes the planned action snapshot sent by the UI.

Possible improvement:

- Send selected action keys instead of treating the full action list as authoritative.
- Let the worker re-run analysis against the latest filesystem state.
- Filter the fresh analysis down to the selected action keys before execution.

Benefits:

- Safer execution if source or destination contents change between preview and sync.
- Stronger authority on the execution side.
- Less dependence on stale UI snapshots.

### 3. Introduce a dedicated worker session manager

The reusable elevated worker session currently lives inside `WorkerSyncExecutionClient`.

Possible improvement:

- Extract session lifetime and upgrade logic into a dedicated service.
- Keep the execution client focused on request/response behavior.

Benefits:

- Clearer responsibilities.
- Easier unit testing of worker reuse, elevation upgrade, and reconnect behavior.
- Simpler future support for retries or health checks.

### 4. Persist worker job state for recovery

Today progress is streamed live, but there is no durable job state outside the active session.

Possible improvement:

- Persist in-flight job metadata under the local app data folder.
- Record state such as queued, running, cancelled, failed, and completed.

Benefits:

- Better crash diagnostics.
- Foundation for recovery or reconnect scenarios.
- Easier support logging when a sync is interrupted.

### 5. Move more sync validation into shared services

Some sync validation still starts in the view model before execution is handed off.

Possible improvement:

- Consolidate more validation rules into shared non-UI services.
- Keep the view model focused on user feedback and command flow.

Benefits:

- Lower duplication risk.
- Fewer chances for UI validation and worker execution rules to drift apart.
- Better long-term maintainability as more filesystems or worker behaviors are added.

### 6. Add focused end-to-end worker integration tests

Current coverage is strong at the unit and focused integration level, but there is still room for one more layer.

Possible improvement:

- Add a test fixture that launches the worker path end to end.
- Verify sync progress, cancellation, and elevated-session reuse behavior across the process boundary.

Benefits:

- Better confidence in IPC behavior.
- Better protection against protocol regressions.
- Better coverage of session reuse logic.

## Recommended Order

If these improvements are tackled incrementally, this order is recommended:

1. Re-analyze in the worker before executing.
2. Extract a dedicated worker session manager.
3. Add end-to-end worker integration tests.
4. Split the worker into its own executable.
5. Add persistent job-state tracking if recovery becomes important.

## Guiding Principle

The UI should stay responsible for presentation, selection, and workflow.

The worker side should stay responsible for execution, privilege boundaries, and filesystem interaction.

Any logic that both sides need should continue to move into shared projects instead of being duplicated.