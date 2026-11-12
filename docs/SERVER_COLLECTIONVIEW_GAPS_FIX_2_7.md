# Server list spacing/gutter fix (VelaRay 2.7)

## Root cause
Previous code assigned `Margin="0,0,10,12"` to the root `Border` of a `CollectionView` item. That did not give a dependable 12dp gap in the native WinUI item layout or dependable space between the card and the overlaid scrollbar. Source tests previously asserted only the string, not the actual layout relationship.

## UI-only fix
- Explicit `<LinearItemsLayout Orientation="Vertical" ItemSpacing="12" />` on `ProfilesList`, with no bottom margin on the card.
- The DataTemplate root is a `Grid ColumnDefinitions="*,18"`: the card is in column 0, and column 1 is a real empty 18dp strip. This allocates room for an approximately 8dp WinUI overlay scrollbar plus 10dp clearance; actual scrollbar width depends on DPI, theme, and OS settings.
- Move the selected-state visual state manager to the template root. Setters explicitly target the child `ProfileCardBorder` via `TargetName`, so blue selection styling and the separate `IsConnected` indicator stay independent.
- Preserve 78dp minimum card height, 12/11dp internal padding, content bindings, 336dp sidebar width, and `ProfilesSelectionChanged` event. No C# or proxy behavior changed.

## Validation
`python scripts/check_server_collectionview_layout.py` and `python scripts/check_server_card_spacing_and_centered_traffic.py`. For a real pixel/scrollbar test, run the MAUI app on Windows, create enough servers to scroll, and verify the gap at 100%/150%/200% display scaling.

Important: These static checks cannot verify WinUI render geometry; a Windows run is still needed.
