# VelaRay 2.7 — Stable actions, language, and bounded logging

## Fixed-width actions

- Top-right **View configuration** has `WidthRequest=140`.
- Top-right **Connect / Switch / Disconnect** has `WidthRequest=156`.
- Footer **Connect / Switch / Disconnect** has `WidthRequest=156` and **View running config** uses `WidthRequest=174`.
- Changing connection state or UI language does not change the width of either action group.
- Selecting a different server continues to **only select it for editing**; connection switching requires a click.

## Language selection

- Replace the old binary language switch with two explicit choices: **中文** and **EN**.
- Active language is highlighted. Applying a language persists the preference without restarting or changing the current proxy connection.
- Both controls remain beside the actual running server/status in the top toolbar. The logo area contains no action buttons.

## Logging — low I/O, memory bounded

- The footer contains a vertical scroll bar and automatically scrolls to the latest record at the bottom.
- Only the latest **1,000** records are retained for display; older displayed records are evicted.
- Incoming log events **only enqueue in memory**; they never perform synchronous disk I/O.
- The asynchronous writer checks its pending queue every **60 seconds**. It writes a batch **only when at least 1,000 pending records** exist. When fewer than 1,000 records are pending, no automatic disk write occurs.
- The pending disk queue is separately bounded at **2,048 records**. During exceptional log floods or disk stalls, oldest pending records are dropped, and a warning summarizing dropped records is written when a sufficiently large batch is eventually saved.
- On normal application shutdown, a pending batch of 1,000 or more is saved; **fewer than 1,000 pending records are deliberately discarded**, according to the low-I/O policy. Crashes can lose the unpersisted buffer.
- **Export logs** is an explicit user action. It persists even a partial pending batch before producing the export file, intentionally overriding the automatic 1,000-record threshold.
- Disk logs use `logs/app.log`, rotate at **10 MiB**, and retain **5** older numbered archives. Clearing visible logs does not delete archived or pending disk logs.
- Each entry is limited to 16,384 characters to cap pathological entries. Depending on volume, the pending buffer can use more memory than the 1,000-line UI buffer; both are capped.

## Verification

`python scripts/check_ui_logging_policy.py` checks UI/buffering source contracts. `dotnet run --project tests/VelaRay.SmokeTests -c Release` includes executable tests for the time/size threshold, bounded history and queue, explicit export, and partial-batch shutdown. Full MAUI compilation and rendering should still be tested on Windows with `scripts/verify-windows.ps1 -SkipPublish`.

### Compile fix — runtime log store API

`StopCoreAndProxyAsync` no longer invokes `RuntimeLogStore.FlushNow()`: that method was removed when logging changed to thresholded background writes. Disconnect now leaves pending log entries in the bounded queue until the normal 60-second / 1,000-entry check. Manual **Export logs** remains available to persist a partial batch. This preserves the requested low-I/O policy and resolves CS1061.
