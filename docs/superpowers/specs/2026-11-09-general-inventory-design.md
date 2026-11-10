# General inventory UX

The existing WinUI inventory should work for personal belongings, collections, shared equipment, supplies, and business stock. The user requested a modest generalization plus an end-to-end UX review, preserving the implemented Excel and label capabilities.

Use a name-first item form with generated, editable item IDs and a default quantity of one. Keep organization/barcodes, reservations/minimums, sourcing/value, and custom fields in clearly named optional sections. Existing values remain editable and are preserved. Show general language such as Add quantity, Remove quantity, Set total quantity, and Move location; retain existing JSON fields, stock action values, and Excel headers for compatibility.

Preserve selection through refreshes, distinguish single-item and batch actions, make an exact count start at the current total, explain quantity changes, and separate scan shortcuts from Excel controls. Item details omit empty commercial metadata. Low-quantity warnings require a configured positive minimum. Recovery controls should appear prominently only when recovery is needed. Narrow windows must retain usable search, filters, commands, details, and forms.

Approach: improve the existing native controls and data model. Renaming text alone would leave the overloaded forms and unsafe count default; inventing separate inventory modes or a new asset/loan model would exceed the requested slight generalization.

Verification covers name-only creation, fractional quantities, all quantity actions, failed-operation integrity, selection/batch labels, filters, archive/restore, CSV/Excel round trips and conflicts, PDF export, navigation, and responsive layouts. Capture current screenshots before changes and inspect the final screenshots. Use isolated development identity/data on this migrated PC. Desktop Excel and printer claims require current application/hardware evidence.
