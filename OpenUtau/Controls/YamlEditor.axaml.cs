using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.TextMate;
using OpenUtau.App.Views;
using OpenUtau.Core;
using Serilog;
using TextMateSharp.Grammars;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// Edits a YAML file the app reads into a C# type, such as character.yaml, with syntax highlighting.
    /// Errors (the app could not load the file) block saving; warnings (e.g. unknown keys) do not.
    /// </summary>
    public partial class YamlEditor : UserControl {
        static readonly byte[] utf8Bom = { 0xEF, 0xBB, 0xBF };

        string filePath = string.Empty;
        Type configType = typeof(object);
        Action? onSaved;
        string savedText = string.Empty;
        bool hasBom;
        bool wasDirty;
        int validationVersion;
        List<YamlDiagnostic> diagnostics = new List<YamlDiagnostic>();
        string validatedText = string.Empty;
        readonly DispatcherTimer validateTimer;
        readonly DiagnosticRenderer renderer;
        readonly TextMate.Installation textMate;
        CompletionWindow? completionWindow;

        /// <summary>Raised when the text starts or stops differing from the file.</summary>
        public event EventHandler? DirtyChanged;

        public YamlEditor() {
            InitializeComponent();
            // The TextMate theme only colors the tokens; the editor keeps the app theme's background.
            var registry = new RegistryOptions(ThemeManager.IsDarkMode ? ThemeName.DarkPlus : ThemeName.LightPlus);
            textMate = Editor.InstallTextMate(registry);
            textMate.SetGrammar(registry.GetScopeByLanguageId(registry.GetLanguageByExtension(".yaml").Id));
            // TextMate colors only the lines on screen, and there are none until the editor shows.
            AttachedToVisualTree += (s, e) => Dispatcher.UIThread.Post(
                () => textMate.EditorModel.InvalidateViewPortLines(), DispatcherPriority.Background);
            renderer = new DiagnosticRenderer(this);
            Editor.TextArea.TextView.BackgroundRenderers.Add(renderer);
            Editor.Options.ConvertTabsToSpaces = true;
            Editor.Options.IndentationSize = 2;

            validateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            validateTimer.Tick += (s, e) => {
                validateTimer.Stop();
                Validate();
            };
            Editor.TextChanged += (s, e) => {
                UpdateState();
                validateTimer.Stop();
                validateTimer.Start();
            };
            Editor.PointerHover += OnEditorPointerHover;
            Editor.TextArea.TextEntered += OnTextEntered;
            Editor.PointerHoverStopped += (s, e) => ToolTip.SetIsOpen(Editor, false);
            ProblemList.SelectionChanged += OnProblemSelected;
            // Errors in red; warnings keep the inherited text color. Avalonia may pass a null item.
            ProblemList.ItemTemplate = new FuncDataTemplate<ProblemItem>((item, _) => {
                var text = new TextBlock { Text = item?.Text };
                if (item?.Diagnostic.IsError == true) {
                    text.Foreground = DiagnosticRenderer.ErrorBrush;
                }
                return text;
            });
            SaveButton.Click += (s, e) => Save();
            RevertButton.Click += (s, e) => Revert();
            AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        }

        public string FileName => Path.GetFileName(filePath);

        /// <summary>Opens <paramref name="path"/>, or an empty document if it does not exist yet.</summary>
        public void Load(string path, Type type, Action? onSaved) {
            filePath = path;
            configType = type;
            this.onSaved = onSaved;
            savedText = ReadFile();
            Editor.Document = new TextDocument(savedText);
            UpdateState();
            Validate();
        }

        string ReadFile() {
            if (!File.Exists(filePath)) {
                hasBom = false;
                return string.Empty;
            }
            var bytes = File.ReadAllBytes(filePath);
            hasBom = bytes.AsSpan().StartsWith(utf8Bom);
            return Encoding.UTF8.GetString(bytes, hasBom ? utf8Bom.Length : 0, bytes.Length - (hasBom ? utf8Bom.Length : 0));
        }

        public bool IsDirty => Editor.Text != savedText;

        bool HasErrors => diagnostics.Any(d => d.IsError);

        void UpdateState() {
            SaveButton.IsEnabled = IsDirty && !HasErrors;
            RevertButton.IsEnabled = IsDirty;
            if (IsDirty != wasDirty) {
                wasDirty = IsDirty;
                DirtyChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        async void Validate() {
            var text = Editor.Text;
            int version = ++validationVersion;
            List<YamlDiagnostic> result;
            try {
                result = await Task.Run(() => YamlValidator.Validate(text, configType));
            } catch (Exception e) {
                Log.Error(e, $"Failed to validate {filePath}");
                return;
            }
            // Otherwise a newer validation is on its way.
            if (version == validationVersion) {
                ShowDiagnostics(text, result);
            }
        }

        void ShowDiagnostics(string text, List<YamlDiagnostic> result) {
            validatedText = text;
            diagnostics = result;
            ProblemList.ItemsSource = diagnostics.Select(d => new ProblemItem(d, $"{d.StartLine}:{d.StartColumn}  {Describe(d)}")).ToList();
            ProblemList.IsVisible = diagnostics.Count > 0;
            int errors = diagnostics.Count(d => d.IsError);
            StatusText.Text = errors > 0
                ? string.Format(ThemeManager.GetString("yamleditor.errors"), errors, diagnostics.Count - errors)
                : diagnostics.Count > 0
                ? string.Format(ThemeManager.GetString("yamleditor.warnings"), diagnostics.Count)
                : ThemeManager.GetString("yamleditor.noproblems");
            Editor.TextArea.TextView.InvalidateLayer(renderer.Layer);
            UpdateState();
        }

        // Validates now if the last validation is out of date, e.g. right after typing.
        void ValidateNow() {
            if (Editor.Text != validatedText) {
                ++validationVersion;
                ShowDiagnostics(Editor.Text, YamlValidator.Validate(Editor.Text, configType));
            }
        }

        static string Describe(YamlDiagnostic d) => d.Kind switch {
            YamlDiagnosticKind.Syntax => string.Format(ThemeManager.GetString("yamleditor.syntax"), d.Detail),
            YamlDiagnosticKind.UnknownKey => string.Format(ThemeManager.GetString("yamleditor.unknownkey"), d.Detail),
            YamlDiagnosticKind.WrongType => string.Format(ThemeManager.GetString("yamleditor.wrongtype"), d.Detail),
            _ => d.Detail,
        };

        record ProblemItem(YamlDiagnostic Diagnostic, string Text) {
            public override string ToString() => Text;
        }

        void OnProblemSelected(object? sender, SelectionChangedEventArgs e) {
            if (ProblemList.SelectedItem is not ProblemItem item) {
                return;
            }
            var (start, end) = OffsetsOf(item.Diagnostic);
            Editor.Select(start, end - start);
            Editor.ScrollTo(item.Diagnostic.StartLine, item.Diagnostic.StartColumn);
            Editor.TextArea.Focus();
        }

        // Help for the key under the pointer, then any problems there.
        void OnEditorPointerHover(object? sender, PointerEventArgs e) {
            var position = Editor.GetPositionFromPoint(e.GetPosition(Editor));
            if (position == null) {
                return;
            }
            int offset = Editor.Document.GetOffset(position.Value.Location);
            var problems = diagnostics.Where(d => {
                var (start, end) = OffsetsOf(d);
                return start <= offset && offset <= end;
            }).ToList();
            var key = YamlValidator.DescribeKeyAt(Editor.Text, configType, position.Value.Line, position.Value.Column);
            if (key == null && problems.Count == 0) {
                return;
            }
            var tip = key != null ? KeyTip(key) : new StackPanel { MaxWidth = 420, Spacing = 2 };
            foreach (var problem in problems) {
                var text = new TextBlock { Text = Describe(problem), TextWrapping = TextWrapping.Wrap };
                if (problem.IsError) {
                    text.Foreground = DiagnosticRenderer.ErrorBrush;
                }
                tip.Children.Add(text);
            }
            ToolTip.SetTip(Editor, tip);
            ToolTip.SetIsOpen(Editor, true);
        }

        // A key's name, description, value type and default.
        static StackPanel KeyTip(YamlKeyInfo key) {
            var tip = new StackPanel { MaxWidth = 420, Spacing = 2 };
            tip.Children.Add(new TextBlock { Text = key.Key, FontWeight = FontWeight.SemiBold });
            if (!string.IsNullOrEmpty(key.Description)) {
                tip.Children.Add(new TextBlock { Text = key.Description, TextWrapping = TextWrapping.Wrap });
            }
            var value = string.Format(ThemeManager.GetString("yamleditor.value"), DescribeType(key.ValueType));
            var defaultValue = FormatDefault(key.DefaultValue);
            if (defaultValue != null) {
                value += " · " + string.Format(ThemeManager.GetString("yamleditor.default"), defaultValue);
            }
            tip.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });
            return tip;
        }

        // Suggests keys or values for the caret. Typing only shows the ones matching what is typed.
        void ShowSuggestions(bool typing) {
            if (completionWindow != null) {
                return;
            }
            var caret = Editor.TextArea.Caret;
            var suggestions = YamlValidator.SuggestAt(Editor.Text, configType, caret.Line, caret.Column);
            if (suggestions == null) {
                return;
            }
            var line = Editor.Document.GetLineByNumber(caret.Line);
            int start = line.Offset + suggestions.StartColumn - 1;
            var typed = Editor.Document.GetText(start, caret.Offset - start);
            var newLine = TextUtilities.GetNewLineFromDocument(Editor.Document, caret.Line);
            var items = suggestions.Keys
                .Select(key => (ICompletionData)new KeySuggestion(this, key, suggestions.StartColumn - 1, newLine))
                .Concat(suggestions.Values.Select(value => new ValueSuggestion(value)))
                .Where(item => item.Text.Contains(typed, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (items.Count == 0 || (typing && items.Count == 1 && items[0].Text == typed)) {
                return;
            }
            completionWindow = new CompletionWindow(Editor.TextArea) { StartOffset = start };
            foreach (var item in items) {
                completionWindow.CompletionList.CompletionData.Add(item);
            }
            completionWindow.Closed += (s, e) => completionWindow = null;
            completionWindow.Show();
            // So Enter or Tab takes the best match right away.
            completionWindow.CompletionList.SelectedItem =
                items.FirstOrDefault(item => item.Text.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) ?? items[0];
        }

        void OnTextEntered(object? sender, TextInputEventArgs e) {
            if (string.IsNullOrEmpty(e.Text)) {
                return;
            }
            // A new line: suggest the keys it can take, once it is indented.
            if (e.Text.Contains('\n') || e.Text.Contains('\r')) {
                Dispatcher.UIThread.Post(() => ShowSuggestions(typing: true), DispatcherPriority.Background);
                return;
            }
            char c = e.Text[^1];
            // While typing a key or a value, or right after "key: " or "- ".
            bool afterMarker = false;
            if (c == ' ') {
                var caret = Editor.TextArea.Caret;
                var before = Editor.Document.GetText(Editor.Document.GetLineByNumber(caret.Line).Offset, caret.Column - 1).TrimEnd();
                afterMarker = before.EndsWith(':') || before.EndsWith('-');
            }
            if (char.IsLetterOrDigit(c) || c == '_' || afterMarker) {
                ShowSuggestions(typing: true);
            }
        }

        class KeySuggestion : ICompletionData {
            readonly YamlEditor editor;
            readonly YamlKeyInfo key;
            readonly int column;
            readonly string newLine;

            public KeySuggestion(YamlEditor editor, YamlKeyInfo key, int column, string newLine) {
                this.editor = editor;
                this.key = key;
                this.column = column;
                this.newLine = newLine;
            }

            public IImage? Image => null;
            public string Text => key.Key;
            public object Content => key.Key;
            public object Description => KeyTip(key);
            public double Priority => 0;

            // Scalars get "key: "; sections a new, deeper line; lists a new line starting a list item.
            public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) {
                var type = Nullable.GetUnderlyingType(key.ValueType) ?? key.ValueType;
                bool isScalar = type == typeof(string) || type.IsPrimitive || type.IsEnum || type == typeof(decimal);
                string text = isScalar ? key.Key + ": "
                    : YamlValidator.DictionaryValueType(type) == null && YamlValidator.ItemType(type) != null
                    ? key.Key + ":" + newLine + new string(' ', column) + "- "
                    : key.Key + ":" + newLine + new string(' ', column + 2);
                textArea.Document.Replace(completionSegment, text);
                // Then the value's choices, or the keys of the section or list item just started.
                Dispatcher.UIThread.Post(() => editor.ShowSuggestions(typing: false), DispatcherPriority.Background);
            }
        }

        class ValueSuggestion : ICompletionData {
            public ValueSuggestion(YamlValue value) {
                Text = value.Text;
                Description = value.Description;
            }

            public IImage? Image => null;
            public string Text { get; }
            public object Content => Text;
            public object? Description { get; }
            public double Priority => 0;

            public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) {
                textArea.Document.Replace(completionSegment, Text);
            }
        }

        // What a value of this type looks like in YAML, e.g. "list of text".
        static string DescribeType(Type type) {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type == typeof(string)) {
                return ThemeManager.GetString("yamleditor.type.text");
            }
            if (type == typeof(bool)) {
                return ThemeManager.GetString("yamleditor.type.bool");
            }
            if (type.IsEnum) {
                return string.Format(ThemeManager.GetString("yamleditor.type.choice"), string.Join(", ", Enum.GetNames(type)));
            }
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) {
                return ThemeManager.GetString("yamleditor.type.number");
            }
            if (type.IsPrimitive) {
                return ThemeManager.GetString("yamleditor.type.integer");
            }
            var valueType = YamlValidator.DictionaryValueType(type);
            if (valueType != null) {
                return string.Format(ThemeManager.GetString("yamleditor.type.map"), DescribeType(valueType));
            }
            var itemType = YamlValidator.ItemType(type);
            if (itemType != null) {
                return string.Format(ThemeManager.GetString("yamleditor.type.list"), DescribeType(itemType));
            }
            return ThemeManager.GetString("yamleditor.type.section");
        }

        // Defaults worth showing: scalars that are set, written as in YAML.
        static string? FormatDefault(object? value) => value switch {
            null => null,
            string s => string.IsNullOrEmpty(s) ? null : s,
            bool b => b ? "true" : "false",
            IFormattable f when value.GetType().IsPrimitive => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            Enum e => e.ToString(),
            _ => null,
        };

        // Diagnostics may come from slightly older text, so clamp to the document.
        (int start, int end) OffsetsOf(YamlDiagnostic d) {
            var document = Editor.Document;
            int Offset(int line, int column) {
                line = Math.Clamp(line, 1, document.LineCount);
                var docLine = document.GetLineByNumber(line);
                return docLine.Offset + Math.Clamp(column - 1, 0, docLine.Length);
            }
            int start = Offset(d.StartLine, d.StartColumn);
            int end = Math.Max(start, Offset(d.EndLine, d.EndColumn));
            if (end == start) {
                end = document.GetLineByOffset(start).EndOffset;
            }
            return (start, end);
        }

        // The app could not load a file with errors, so those block saving; warnings do not.
        public bool Save() {
            ValidateNow();
            if (HasErrors) {
                var first = ProblemList.Items.OfType<ProblemItem>().First(item => item.Diagnostic.IsError);
                ProblemList.SelectedItem = null;
                ProblemList.SelectedItem = first;
                return false;
            }
            try {
                var text = Editor.Text;
                var bytes = Encoding.UTF8.GetBytes(text);
                File.WriteAllBytes(filePath, hasBom ? utf8Bom.Concat(bytes).ToArray() : bytes);
                savedText = text;
                UpdateState();
                onSaved?.Invoke();
                return true;
            } catch (Exception e) {
                Log.Error(e, $"Failed to save {filePath}");
                if (TopLevel.GetTopLevel(this) is Window window) {
                    _ = MessageBox.ShowError(window, e);
                }
                return false;
            }
        }

        public void Revert() {
            savedText = ReadFile();
            Editor.Document.Text = savedText;
            UpdateState();
        }

        void OnPreviewKeyDown(object? sender, KeyEventArgs e) {
            if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Control) {
                e.Handled = true;
                ShowSuggestions(typing: false);
            } else if (e.Key == Key.S && (e.KeyModifiers == KeyModifiers.Control || e.KeyModifiers == KeyModifiers.Meta)) {
                e.Handled = true;
                if (IsDirty) {
                    Save();
                }
            }
        }

        /// <summary>
        /// Asks what to do with unsaved changes before the file is closed. Returns false if the user
        /// chose to keep editing, which is only offered when <paramref name="canStay"/>.
        /// </summary>
        public async Task<bool> ConfirmClose(Window owner, bool canStay) {
            if (!IsDirty) {
                return true;
            }
            ValidateNow();
            var caption = ThemeManager.GetString("yamleditor.unsaved.caption");
            if (HasErrors) {
                if (!canStay) {
                    await MessageBox.Show(owner, string.Format(ThemeManager.GetString("yamleditor.discarded"), FileName),
                        caption, MessageBox.MessageBoxButtons.Ok);
                    Revert();
                    return true;
                }
                var discard = await MessageBox.Show(owner, string.Format(ThemeManager.GetString("yamleditor.unsaved.errors"), FileName),
                    caption, MessageBox.MessageBoxButtons.YesNo);
                if (discard != MessageBox.MessageBoxResult.Yes) {
                    return false;
                }
                Revert();
                return true;
            }
            var result = await MessageBox.Show(owner, string.Format(ThemeManager.GetString("yamleditor.unsaved"), FileName),
                caption, canStay ? MessageBox.MessageBoxButtons.YesNoCancel : MessageBox.MessageBoxButtons.YesNo);
            if (result == MessageBox.MessageBoxResult.Cancel || (result == MessageBox.MessageBoxResult.Yes && !Save())) {
                return false;
            }
            if (result == MessageBox.MessageBoxResult.No) {
                Revert();
            }
            return true;
        }

        /// <summary>Wavy underlines under each diagnostic: red for errors, amber for warnings.</summary>
        class DiagnosticRenderer : IBackgroundRenderer {
            public static readonly IBrush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x14, 0x00));
            static readonly IPen errorPen = new Pen(ErrorBrush, 1);
            static readonly IPen warningPen = new Pen(new SolidColorBrush(Color.FromRgb(0xD4, 0x8B, 0x00)), 1);
            readonly YamlEditor editor;

            public DiagnosticRenderer(YamlEditor editor) {
                this.editor = editor;
            }

            public KnownLayer Layer => KnownLayer.Selection;

            public void Draw(TextView textView, DrawingContext drawingContext) {
                if (!textView.VisualLinesValid) {
                    return;
                }
                foreach (var d in editor.diagnostics) {
                    var (start, end) = editor.OffsetsOf(d);
                    var segment = new TextSegment { StartOffset = start, EndOffset = end };
                    var pen = d.IsError ? errorPen : warningPen;
                    foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment)) {
                        var geometry = new StreamGeometry();
                        using (var context = geometry.Open()) {
                            double y = rect.Bottom - 1;
                            context.BeginFigure(new Avalonia.Point(rect.Left, y), false);
                            for (double x = rect.Left + 2, dy = -2; x <= rect.Right + 2; x += 2, dy = -dy) {
                                context.LineTo(new Avalonia.Point(x, y + (dy < 0 ? -2 : 0)));
                            }
                            context.EndFigure(false);
                        }
                        drawingContext.DrawGeometry(null, pen, geometry);
                    }
                }
            }
        }
    }
}
