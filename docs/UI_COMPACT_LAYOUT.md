# VelaRay 2.3 · Compact header and node JSON selector

## Implemented

- Keep the application window title and top brand text as **VelaRay**, without decorative runtime/framework/format subtitles.
- Replace the previous two language-selection buttons with one MAUI `Switch`, labeled `中文` and `English`; language persists immediately and requires no restart. A programmatic-update guard prevents init-time toggle events from repeatedly writing settings.
- Show `JSON配置格式 / JSON format` plus a **V4/V5 Picker** on the right of the **连接 / Connection** section heading within the selected node's editor. The separate configuration-format card, banner and explanatory copy were removed.
- Retain independent `ServerProfile.ConfigVersion` per node. The existing JSON generator, validator and runner remain untouched; no migration logic was added.
- Preserve the existing three-entry left navigation and the global-scope notices in the two global settings sections.

## Windows visual checks

1. Confirm the native window title says only `VelaRay`, with its logo.
2. At 100%, 150%, and 200% display scaling, check that `中文 / English` text and Switch remain visible; changing the active node does not shift the language control.
3. Check the Connection heading's JSON selector at narrow and wide window widths, and confirm V4 and V5 options are not truncated.
4. Create two nodes with different V4/V5 selections. Switch between nodes, save and restart; the individual formats should persist.
5. Change language while connected and confirm the core is not restarted.

These are acceptance checks to run on Windows. Source static checks do not replace a Windows MAUI build or visual verification.
