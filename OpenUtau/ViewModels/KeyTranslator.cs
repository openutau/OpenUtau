using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Input;
using OpenUtau.Core;
using OpenUtau.Core.Editing;
using OpenUtau.Core.Util;

namespace OpenUtau.App.ViewModels {
    public class ShortcutKey {
        public string ActionId { get; set; }
        public KeyGesture Gesture { get; set; }

        public ShortcutKey(string actionId, string shortcut) {
            ActionId = actionId;
            Gesture = KeyTranslator.StringToGesture(shortcut);
        }

        public override string ToString() => $"{ActionId}: {KeyTranslator.GetFriendlyName(Gesture.Key, Gesture.KeyModifiers)}";
    }
    
    public static class KeyTranslator {
        public static readonly bool IsMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        public static List<ShortcutKey> Shortcuts { get; set; } = new List<ShortcutKey>();
        public static Preferences.ShortcutBinding[] DefShortcuts { get; }

        static KeyTranslator() {
            DefShortcuts = [
                // Playback & Selection
                new Preferences.ShortcutBinding("PlayOrPause", ["Space"]),
                new Preferences.ShortcutBinding("PlaySelection", ["Alt+Space"]),
                new Preferences.ShortcutBinding("ClearSelection", ["Escape"]),
                new Preferences.ShortcutBinding("SelectAll", ["Ctrl+A"]),
                new Preferences.ShortcutBinding("DeselectAll", ["Ctrl+D"]),

                // UI & Windows
                new Preferences.ShortcutBinding("CloseWindow", ["Alt+F4"]),
                new Preferences.ShortcutBinding("menu.tools.fullscreen", ["F11"]),
                new Preferences.ShortcutBinding("OpenPluginMenu", ["N"]),

                // Lyrics
                new Preferences.ShortcutBinding("EditLyrics", ["Return"]),

                // Tools
                new Preferences.ShortcutBinding("ToolSelect1", ["D1"]),
                new Preferences.ShortcutBinding("ToolSelect2Main", ["D2"]),
                new Preferences.ShortcutBinding("ToolSelect2Alt", ["Ctrl+D2"]),
                new Preferences.ShortcutBinding("ToolSelect3", ["D3"]),
                new Preferences.ShortcutBinding("ToolSelect4Main", ["D4"]),
                new Preferences.ShortcutBinding("ToolSelect4Overwrite", ["Ctrl+D4"]),
                new Preferences.ShortcutBinding("ToolSelect4Line", ["Shift+D4"]),
                new Preferences.ShortcutBinding("ToolSelect4LineOverwrite", ["Ctrl+Shift+D4"]),
                
                // New Pitch Curve Tools
                new Preferences.ShortcutBinding("ToolSelectSCurve", ["D5"]),
                new Preferences.ShortcutBinding("ToolSelectSine", ["Shift+D5"]),
                new Preferences.ShortcutBinding("ToolSelectSmoothen", ["D6"]),
                
                // Knife Tool (Bumped to D7)
                new Preferences.ShortcutBinding("ToolSelect5", ["D7"]),

                // Expressions
                new Preferences.ShortcutBinding("ExpSelect1", ["Alt+D1"]),
                new Preferences.ShortcutBinding("ExpSelect2", ["Alt+D2"]),
                new Preferences.ShortcutBinding("ExpSelect3", ["Alt+D3"]),
                new Preferences.ShortcutBinding("ExpSelect4", ["Alt+D4"]),
                new Preferences.ShortcutBinding("ExpSelect5", ["Alt+D5"]),
                new Preferences.ShortcutBinding("ExpSelect6", ["Alt+D6"]),
                new Preferences.ShortcutBinding("ExpSelect7", ["Alt+D7"]),
                new Preferences.ShortcutBinding("ExpSelect8", ["Alt+D8"]),
                new Preferences.ShortcutBinding("ExpSelect9", ["Alt+D9"]),
                new Preferences.ShortcutBinding("ExpSelect10", ["Alt+D0"]),

                // Toggles
                new Preferences.ShortcutBinding("ToggleFinalPitch", ["R"]),
                new Preferences.ShortcutBinding("ToggleTips", ["T"]),
                new Preferences.ShortcutBinding("ToggleVibrato", ["U"]),
                new Preferences.ShortcutBinding("TogglePitch", ["I"]),
                new Preferences.ShortcutBinding("TogglePhoneme", ["O"]),
                new Preferences.ShortcutBinding("ToggleExpressions", ["L"]),
                new Preferences.ShortcutBinding("ToggleSnap", ["P"]),
                new Preferences.ShortcutBinding("OpenSnapMenu", ["Alt+P"]),
                new Preferences.ShortcutBinding("ToggleNoteParams", ["OemPipe"]),
                new Preferences.ShortcutBinding("TogglePlayTone", ["Y"]),
                new Preferences.ShortcutBinding("ToggleWaveform", ["W"]),

                // Transposition
                new Preferences.ShortcutBinding("TransposeUp", ["Up"]),
                new Preferences.ShortcutBinding("pianoroll.menu.notes.octaveup", ["Ctrl+Up"]),
                new Preferences.ShortcutBinding("TransposeDown", ["Down"]),
                new Preferences.ShortcutBinding("pianoroll.menu.notes.octavedown", ["Ctrl+Down"]),

                // Note Movement & Sizing
                new Preferences.ShortcutBinding("MoveCursorLeft", ["Left"]),
                new Preferences.ShortcutBinding("ResizeNotesLeft", ["Alt+Left"]),
                new Preferences.ShortcutBinding("MoveNotesLeft", ["Ctrl+Left"]),
                new Preferences.ShortcutBinding("ExtendSelectionLeft", ["Shift+Left"]),
                new Preferences.ShortcutBinding("MoveCursorRight", ["Right"]),
                new Preferences.ShortcutBinding("ResizeNotesRight", ["Alt+Right"]),
                new Preferences.ShortcutBinding("MoveNotesRight", ["Ctrl+Right"]),
                new Preferences.ShortcutBinding("ExtendSelectionRight", ["Shift+Right"]),

                // Edit Operations
                new Preferences.ShortcutBinding("menu.edit.undo", ["Ctrl+Z"]),
                new Preferences.ShortcutBinding("menu.edit.redo", ["Ctrl+Y", "Ctrl+Shift+Z"]),
                new Preferences.ShortcutBinding("menu.edit.copy", ["Ctrl+C"]),
                new Preferences.ShortcutBinding("menu.edit.cut", ["Ctrl+X"]),
                new Preferences.ShortcutBinding("menu.edit.paste", ["Ctrl+V"]),
                new Preferences.ShortcutBinding("PastePlain", ["Ctrl+Shift+V"]),
                new Preferences.ShortcutBinding("PasteParameters", ["Alt+V"]),
                new Preferences.ShortcutBinding("InsertNote", ["Insert"]),
                new Preferences.ShortcutBinding("menu.edit.delete", ["Delete"]),
                new Preferences.ShortcutBinding("MergeNotes", ["Ctrl+U"]),

                // Playhead & Timeline Navigation
                new Preferences.ShortcutBinding("PlayheadHome", ["Home"]),
                new Preferences.ShortcutBinding("SelectToStart", ["Shift+Home"]),
                new Preferences.ShortcutBinding("PlayheadEnd", ["End"]),
                new Preferences.ShortcutBinding("SelectToEnd", ["Shift+End"]),
                new Preferences.ShortcutBinding("PlayheadLeft", ["Oem4"]),
                new Preferences.ShortcutBinding("PlayheadToSelectionStart", ["Ctrl+Oem4"]),
                new Preferences.ShortcutBinding("PlayheadToViewStart", ["Shift+Oem4"]),
                new Preferences.ShortcutBinding("PlayheadRight", ["OemCloseBrackets"]),
                new Preferences.ShortcutBinding("PlayheadToSelectionEnd", ["Ctrl+OemCloseBrackets"]),
                new Preferences.ShortcutBinding("PlayheadToViewEnd", ["Shift+OemCloseBrackets"]),

                // Scrolling & Zooming
                new Preferences.ShortcutBinding("ScrollLeft", ["A"]),
                new Preferences.ShortcutBinding("ScrollRight", ["D"]),
                new Preferences.ShortcutBinding("ScrollUp", ["Alt+W"]),
                new Preferences.ShortcutBinding("ScrollDown", ["Alt+S"]),
                new Preferences.ShortcutBinding("ZoomIn", ["E"]),
                new Preferences.ShortcutBinding("ZoomOut", ["Q"]),

                // Track & Project Operations
                new Preferences.ShortcutBinding("NewProject", ["Ctrl+N"]),
                new Preferences.ShortcutBinding("OpenProject", ["Ctrl+O"]),
                new Preferences.ShortcutBinding("SaveProject", ["Ctrl+S"]),
                new Preferences.ShortcutBinding("SaveProjectAs", ["Ctrl+Shift+S"]),
                new Preferences.ShortcutBinding("SoloTrack", ["Shift+S"]),
                new Preferences.ShortcutBinding("MuteTrack", ["Shift+M"]),
                new Preferences.ShortcutBinding("FocusSelection", ["F"]),
                new Preferences.ShortcutBinding("SearchNote", ["Ctrl+F"]),

                // Parts Navigation
                new Preferences.ShortcutBinding("MoveToNextPart", ["PageDown"]),
                new Preferences.ShortcutBinding("MoveToPrevPart", ["PageUp"]),

                new Preferences.ShortcutBinding("pianoroll.menu.notes.loadrenderedpitch", ["Ctrl+R"]),
                new Preferences.ShortcutBinding("pianoroll.menu.notes.refreshrealcurves", ["Ctrl+Shift+R"]),
                new Preferences.ShortcutBinding("pianoroll.menu.notes.bakepitch", ["Alt+K"]),

                // Tails and Overlap
                new Preferences.ShortcutBinding("pianoroll.menu.notes.addtaildash", ["Alt+OemMinus"]),
                new Preferences.ShortcutBinding("pianoroll.menu.notes.addtailrest", ["Shift+Alt+R"]),
                new Preferences.ShortcutBinding("pianoroll.menu.notes.removetaildash", ["Ctrl+Alt+OemMinus"]),
                new Preferences.ShortcutBinding("pianoroll.menu.notes.removetailrest", ["Ctrl+Alt+R"]),
                new Preferences.ShortcutBinding("pianoroll.menu.notes.fixoverlap", ["Alt+F"]),
                new Preferences.ShortcutBinding("pianoroll.menu.notes.autolegato", ["Alt+A"]),

                // Common notes
                new Preferences.ShortcutBinding("pianoroll.menu.notes.commonnotecopy", ["Ctrl+Shift+C"]),
                new Preferences.ShortcutBinding("pianoroll.menu.notes.commonnotepaste", ["Ctrl+Shift+P"]),

                // Timings
                new Preferences.ShortcutBinding("pianoroll.menu.notes.randomizetiming", ["Alt+T"]),
                new Preferences.ShortcutBinding("pianoroll.menu.notes.randomizeoffset", ["Ctrl+Alt+T"]),

                // Lang
                new Preferences.ShortcutBinding("pianoroll.menu.lyrics.romajitohiragana", ["Ctrl+Shift+J"]),
                new Preferences.ShortcutBinding("pianoroll.menu.lyrics.hiraganatoromaji", ["Ctrl+Alt+J"]),
                new Preferences.ShortcutBinding("pianoroll.menu.lyrics.javcvtocv", ["Ctrl+Shift+K"]),
                new Preferences.ShortcutBinding("pianoroll.menu.notes.hanzitopinyin", ["Ctrl+Alt+H"]),

                // Suffixes and Phonetic Hints
                new Preferences.ShortcutBinding("pianoroll.menu.lyrics.removetonesuffix", ["Ctrl+Alt+S"]),
                new Preferences.ShortcutBinding("pianoroll.menu.lyrics.removelettersuffix", ["Ctrl+Shift+S"]),
                new Preferences.ShortcutBinding("pianoroll.menu.lyrics.movesuffixtovoicecolor", ["Ctrl+Alt+C"]),
                new Preferences.ShortcutBinding("pianoroll.menu.lyrics.removephonetichint", ["Ctrl+Alt+P"]),

                // Dash and Slur
                new Preferences.ShortcutBinding("pianoroll.menu.lyrics.dashtoplus", ["Alt+OemPlus"]),
                new Preferences.ShortcutBinding("pianoroll.menu.lyrics.dashtoplustilda", ["Ctrl+Alt+OemPlus"]),
                new Preferences.ShortcutBinding("pianoroll.menu.lyrics.insertslur", ["Alt+I"]),

                // Reset
                new Preferences.ShortcutBinding("pianoroll.menu.notes.reset.all", ["Ctrl+Shift+Delete"]),
                new Preferences.ShortcutBinding("pianoroll.menu.notes.reset.allparameters", ["Ctrl+Alt+I"]),
                new Preferences.ShortcutBinding("pianoroll.menu.notes.reset.exps", ["Ctrl+Shift+E"]),
                new Preferences.ShortcutBinding("pianoroll.menu.notes.clear.vibratos", ["Ctrl+Alt+V"]),
                new Preferences.ShortcutBinding("pianoroll.menu.notes.reset.vibratos", ["Ctrl+Shift+U"]),
                new Preferences.ShortcutBinding("pianoroll.menu.notes.reset.pitchbends", ["Ctrl+Alt+B"]),
                new Preferences.ShortcutBinding("pianoroll.menu.notes.reset.phonemetimings", ["Ctrl+Shift+T"]),
                new Preferences.ShortcutBinding("pianoroll.menu.notes.reset.aliases", ["Ctrl+Alt+A"]),

                // other toggles
                new Preferences.ShortcutBinding("Lock Pitch Points", ["Ctrl+Shift+L"]),
                new Preferences.ShortcutBinding("Lock Vibrato", ["Ctrl+Alt+U"]),
                new Preferences.ShortcutBinding("Lock Expressions", ["Ctrl+Alt+E"]),
                new Preferences.ShortcutBinding("Show Portrait", ["Shift+Alt+P"]),
                new Preferences.ShortcutBinding("Show Icon", ["Shift+Alt+I"]),
                new Preferences.ShortcutBinding("Show Ghost Notes", ["Alt+G"]),
                new Preferences.ShortcutBinding("Use Track Color", ["Alt+C"]),
                new Preferences.ShortcutBinding("Detach Piano Roll", ["Shift+Alt+D"]),
                new Preferences.ShortcutBinding("Hide Piano Roll", ["Shift+Alt+H"]),
                new Preferences.ShortcutBinding("lyricsreplace.replace", ["Ctrl+H"]),
                new Preferences.ShortcutBinding("Quantize Notes", ["Alt+Q"]),
                new Preferences.ShortcutBinding("Randomize Tuning", ["Alt+R"]),
                new Preferences.ShortcutBinding("Lengthen Crossfade", ["Alt+L"]),
                new Preferences.ShortcutBinding("Add Breath", ["Alt+B"]),
                new Preferences.ShortcutBinding("Edit Note Defaults", ["Alt+N"]),
                new Preferences.ShortcutBinding("Open Singers Window", ["Alt+O"]),
                new Preferences.ShortcutBinding("Open Expressions", ["Alt+E"])
            ];
        }

        public static void LoadShortcuts() {
            Shortcuts.Clear();
            Shortcuts = GetMergedShortcuts().SelectMany(s => s.Shortcuts
                .Select(key => new ShortcutKey(s.ActionId, key)))
                .Where(key => key.Gesture.Key != Key.None)
                .ToList();

            // external batch edits
            var edits = DocManager.Inst.ExternalBatchEditTypes
                .Select(type => Activator.CreateInstance(type) as BatchEdit)
                .OfType<BatchEdit>();
            Shortcuts.AddRange(Preferences.Default.PluginShortcuts.SelectMany(s => s.Shortcuts
                .Where(key => edits.Any(edit => edit.Name == s.ActionId))
                .Select(key => new ShortcutKey(s.ActionId, key)))
                .Where(key => key.Gesture.Key != Key.None));
            // legacy plugins
            Shortcuts.AddRange(Preferences.Default.PluginShortcuts.SelectMany(s => s.Shortcuts
                .Where(key => DocManager.Inst.Plugins.Any(plugin => plugin.Name == s.ActionId))
                .Select(key => new ShortcutKey(s.ActionId, key)))
                .Where(key => key.Gesture.Key != Key.None));
        }

        public static List<Preferences.ShortcutBinding> GetMergedShortcuts() {
            var merged = new List<Preferences.ShortcutBinding>();
            foreach (var sc in DefShortcuts) {
                var customized = Preferences.Default.Shortcuts.FirstOrDefault(pref => pref.ActionId == sc.ActionId);
                if (customized != null) {
                    merged.Add(new Preferences.ShortcutBinding(sc.ActionId, customized.Shortcuts));
                } else {
                    merged.Add(sc);
                }
            }
            return merged;
        }

        public static void SaveShortcuts(IEnumerable<ShortcutItemViewModel> items) {
            var diff = new List<Preferences.ShortcutBinding>();
            var plugin = new  List<Preferences.ShortcutBinding>();
            var edits = DocManager.Inst.ExternalBatchEditTypes
                .Select(type => Activator.CreateInstance(type) as BatchEdit)
                .OfType<BatchEdit>();

            foreach (var item in items) {
                var defKey = DefShortcuts.FirstOrDefault(s => s.ActionId == item.ActionId);
                if (defKey != null) {
                    item.Gestures.RemoveAll(g => g.Key == Key.None);
                    var gestures = item.Gestures.Select(GestureToString).ToArray();
                    if (defKey.Shortcuts.Length != gestures.Length) {
                        diff.Add(new Preferences.ShortcutBinding(item.ActionId, gestures));
                    } else if (!defKey.Shortcuts.OrderBy(x => x).SequenceEqual(gestures.OrderBy(x => x))) {
                        diff.Add(new Preferences.ShortcutBinding(item.ActionId, gestures));
                    }
                } else if (edits.Any(edit => edit.Name == item.ActionId) || DocManager.Inst.Plugins.Any(plugin => plugin.Name == item.ActionId)) {
                    item.Gestures.RemoveAll(g => g.Key == Key.None);
                    if (item.Gestures.Count == 0) continue;
                    var gestures = item.Gestures.Select(GestureToString).ToArray();
                    plugin.Add(new Preferences.ShortcutBinding(item.ActionId, gestures));
                }
            }
            Preferences.Default.Shortcuts = diff.ToArray();
            Preferences.Default.PluginShortcuts = plugin.ToArray();
            Preferences.Save();
            LoadShortcuts();
        }

        public static void ResetShortcuts() {
            Preferences.Default.Shortcuts = [];
            Preferences.Default.PluginShortcuts = [];
            Preferences.Save();
            LoadShortcuts();
        }

        public static KeyGesture StringToGesture(string shortcut) {
            try {
                var gesture = KeyGesture.Parse(shortcut);
                return GestureConverter(gesture);
            } catch {
                return new KeyGesture(Key.None);
            }
        }
        public static string GestureToString(KeyGesture gesture) {
            return GestureConverter(gesture).ToString();
        }
        private static KeyGesture GestureConverter(KeyGesture gesture) {
            if (IsMac) {
                var m = gesture.KeyModifiers;
                bool hasCtrl = m.HasFlag(KeyModifiers.Control);
                bool hasMeta = m.HasFlag(KeyModifiers.Meta);
                if (hasCtrl) {
                    m &= ~KeyModifiers.Control;
                    m |= KeyModifiers.Meta;
                }
                if (hasMeta) {
                    m &= ~KeyModifiers.Meta;
                    m |= KeyModifiers.Control;
                }
                return new KeyGesture(gesture.Key, m);
            } else {
                return gesture;
            }
        }

        public static KeyGesture? GetGestureForMenu(string actionId) {
            // Since only one shortcut can be displayed in the menu, if there are multiple shortcuts, it returns the first one
            return Shortcuts.FirstOrDefault(s => s.ActionId == actionId)?.Gesture;
        }
        
        public static string? GetActionIdFromKey(Key pressedKey, KeyModifiers pressedMods) {
            foreach (var sc in Shortcuts) {
                if (IsKeyMatch(sc.Gesture.Key, pressedKey) && sc.Gesture.KeyModifiers == pressedMods) {
                    return sc.ActionId;
                }
            }
            return null;
        }

        public static bool IsKeyMatch(Key savedKey, Key pressedKey) {
            if (savedKey == pressedKey) return true;
            return savedKey switch {
                Key.OemPipe => pressedKey == Key.Oem5 || pressedKey == Key.OemBackslash,
                Key.OemOpenBrackets => pressedKey == Key.Oem4,
                Key.OemCloseBrackets => pressedKey == Key.Oem6,
                Key.OemQuotes => pressedKey == Key.Oem7,
                Key.OemSemicolon => pressedKey == Key.Oem1,
                Key.OemTilde => pressedKey == Key.Oem3 || pressedKey == Key.Oem8,
                Key.OemMinus => pressedKey == Key.Subtract,
                Key.OemPlus => pressedKey == Key.Add,
                Key.OemQuestion => pressedKey == Key.Oem2,
                _ => false
            };
        }

        public static string GetFriendlyName(Key key, KeyModifiers modifiers) {
            string mods = GetFriendlyModifiersName(modifiers);
            string friendlyKey = GetFriendlyName(key.ToString());
            if (!string.IsNullOrEmpty(mods)) {
                return IsMac ? $"{mods} {friendlyKey}": $"{mods} + {friendlyKey}";
            }
            return friendlyKey;
        }

        public static string GetFriendlyName(string keyName) {
            return keyName switch {
                // Modifiers
                "Windows" or "LWin" or "RWin" => IsMac ? "⌘" : "Win",
                "LeftAlt" or "RightAlt" or "Alt" => IsMac ? "⌥" : "Alt",
                "Control" or "LeftCtrl" or "RightCtrl" or "LControl" or "RControl" => IsMac ? "⌃" : "Ctrl",
                "Shift" or "LeftShift" or "RightShift" => IsMac ? "⇧" : "Shift",
                
                // Navigation & Editing
                "Escape" => "Esc",
                "Return" => "Enter",
                "Back" => "Backspace",
                "Delete" => "Delete",
                "Insert" => "Ins",
                "PageUp" => "PgUp",
                "PageDown" => "PgDn",
                "Capital" => "Caps Lock",
                "Scroll" => "Scroll Lock",
                "NumLock" => "Num Lock",
                "Snapshot" => "Print Screen",

                // Numpad
                "Divide" => "(Num /)",
                "Multiply" => "(Num *)",
                "Subtract" => "(Num -)",
                "Add" => "(Num +)",
                "Decimal" => "(Num .)",
                "NumPad0" => "Num 0", "NumPad1" => "Num 1", "NumPad2" => "Num 2",
                "NumPad3" => "Num 3", "NumPad4" => "Num 4", "NumPad5" => "Num 5",
                "NumPad6" => "Num 6", "NumPad7" => "Num 7", "NumPad8" => "Num 8",
                "NumPad9" => "Num 9",

                // Digits
                "D1" => "1", "D2" => "2", "D3" => "3", "D4" => "4", "D5" => "5",
                "D6" => "6", "D7" => "7", "D8" => "8", "D9" => "9", "D0" => "0",

                // OEM Symbols
                "OemTilde" or "Oem8" or "Oem3" => "~",
                "OemMinus" or "OemMinusSign" => "-",
                "OemPlus" or "OemPlusSign" => "=",
                "OemOpenBrackets" or "Oem4" => "[",
                "OemCloseBrackets" or "Oem6" => "]",
                "OemPipe" or "Oem5" or "OemBackslash" => "\\",
                "OemSemicolon" or "Oem1" => ";",
                "OemQuotes" or "Oem7" => "'",
                "OemComma" or "OemCommaSign" => ",",
                "OemPeriod" or "OemPeriodSign" => ".",
                "OemQuestion" or "Oem2" => "/",

                _ => keyName
            };
        }

        public static string GetFriendlyModifiersName(KeyModifiers modifiers) {
            if (modifiers == KeyModifiers.None) return string.Empty;

            var parts = new List<string>();
            if (modifiers.HasFlag(KeyModifiers.Control)) {
                parts.Add(IsMac ? "⌃" : "Ctrl");
            }
            if (modifiers.HasFlag(KeyModifiers.Alt)) {
                parts.Add(IsMac ? "⌥" : "Alt");
            }
            if (modifiers.HasFlag(KeyModifiers.Shift)) {
                parts.Add(IsMac ? "⇧" : "Shift");
            }
            if (modifiers.HasFlag(KeyModifiers.Meta)) {
                parts.Add(IsMac ? "⌘" : "Win");
            }

            return string.Join(IsMac ? " " : " + ", parts);
        }
    }
}
