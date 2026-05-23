# Batch Edit Macros

Contributions to editing macros are welcomed.
            
An batch edit should:
- Starts with `docManager.StartUndoGroup();`                
- Performs modifications by calling `docManager.ExecuteCmd();` with commands.
  - Some commands come with a single note variation and a multiple notes variation, e.g., `AddNoteCommand(UVoicePart part, UNote note)` and `AddNoteCommand(UVoicePart part, List<UNote> notes)`. Use the multiple notes varitaion for batch edits.
- Ends with `docManager.EndUndoGroup();`
- Has a localized name.
