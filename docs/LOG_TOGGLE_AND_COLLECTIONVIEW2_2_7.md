# VelaRay 2.7: Log persistence toggle + MAUI 11 CollectionView2

## Log behavior

- New **Enable log file writing** switch in **Proxy settings → Core routing & general settings**, default `true` for both fresh and upgraded settings (missing JSON property deserializes to the initializer default).
- Runtime output remains visible in the bottom log panel while switched off; only the last 1,000 entries are retained in memory, older entries are discarded.
- When switched off, any queued, unflushed entries are discarded; it does not delete pre-existing log archives or previously saved files. The `logs` directory is not created on startup solely for logging.
- A full batch already being flushed may complete before the switch operation returns. Afterwards, no automatic file write is issued until logging is re-enabled.
- With logging enabled, preserve the existing 60-second write check and 1,000-line minimum; pending queue max 2,048; 10MB file rotation with 5 archives. On shutdown, batches smaller than 1,000 are discarded.
- Turning logging back on only records **new** entries, never backfills messages from the disabled interval.
- **Export logs** is disabled when file logging is off to honor the no-write promise. Existing on-disk log files are kept.
- No V2Fly core restart, proxy mode change or PAC/routing modification is required.

## MAUI 11

The project now enables `<UseWindowsCollectionView2Handler>true</UseWindowsCollectionView2Handler>` for the Windows `ItemsRepeater`-based handler. Server card borders are driven by `ServerProfile.IsSelected`; actual running status is driven separately by `IsConnected`. Default card stroke is visible without interaction. The previous .NET 11 XamlC localized binding fix is retained.

**Acceptance tests on Windows**: Start with a saved selection, verify every card border on first render; select another without connecting; test 2dp spacing, right 10dp gutter, scrollbar, 78dp min card height, dark/light mode, window widths and DPI scales. Toggle logging off, generate >1,000 lines and verify no new `logs/app.log`; toggle on, generate >=1,000 and check the next 60s flush. Check that `Export logs` is disabled while off.

To verify: `./scripts/verify-windows.ps1 -SkipPublish` after installing .NET 11 SDK and MAUI Windows workload. Linux static checks cannot validate actual WinUI rendering or Windows publishing.
