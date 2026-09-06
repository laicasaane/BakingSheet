# Error logging

BakingSheet diagnostics describe the failed operation, the available source location, and a corrective action. Raw-sheet messages include the external sheet name, container property, page sub-name, and physical spreadsheet cell. When a page is transposed, the cell is still reported in the original physical orientation.

Page sub-names distinguish pages that share the same cell coordinates. Pages without a sub-name are shown as `(default)`.

Structured loggers can read named fields and the existing scopes. Message-only sinks still receive raw sheet, page, and cell context in the rendered message. User input is passed as structured arguments, so braces in values are treated as data. Null input is rendered separately from an empty string where the distinction affects the failure.

Examples include invalid headers with the rejected segment and schema reason, value-conversion failures with the destination type, collection errors with the row and first relevant input cell, JSON errors with their JSON path and object types, and Unity adapter errors with asset or inspected-object context.

Exceptions caught by an existing conversion path are supplied to `ILogger` as exception objects. `UnityLogger` renders that exception after the formatted message, including its inner exceptions and captured stack. Exceptions that already propagate from load, save, I/O, or service operations continue to propagate.

Logging an error does not necessarily stop the whole import. Existing behavior is preserved: an invalid header stops its page, some row errors reject only the current row or collection entry, and later pages or rows may continue.

Diagnostic wording is intended for people and is not a stable machine-readable contract. Consumers should use log severity and structured fields rather than compare the complete rendered message.
