using Xunit;

// FileDownloader and UpdateSession keep update state in static fields — one process
// can only run one update at a time, which is entirely correct for the real launcher
// (a second instance is blocked by the .launcher-lock file), but it means tests in this
// file CANNOT run in parallel: two tests writing to the shared static clientFolder at
// the same time would catch each other's state.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
