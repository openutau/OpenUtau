using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.TextMate;
using OpenUtau.App.ViewModels;
using TextMateSharp.Grammars;
using Serilog;

namespace OpenUtau.App.Views {
    public record struct ParseDiagnostic(int StartLine, int StartColumn, int EndLine, int EndColumn, string Message);

    class DiagnosticRenderer : IBackgroundRenderer {
        public static readonly IBrush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x14, 0x00));
        static readonly IPen errorPen = new Pen(ErrorBrush, 1);
        readonly DictionaryErrorWindow window;

        public DiagnosticRenderer(DictionaryErrorWindow window) {
            this.window = window;
        }

        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext) {
            if (!textView.VisualLinesValid) return;
            
            foreach (var d in window.diagnostics) {
                var (start, end) = window.OffsetsOf(d);
                var segment = new TextSegment { StartOffset = start, EndOffset = end };
                
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
                    drawingContext.DrawGeometry(null, errorPen, geometry);
                }
            }
        }
    }

    public partial class DictionaryErrorWindow : Window {
        private readonly TextEditor _editor;
        private readonly DispatcherTimer _validateTimer;
        private readonly TextMate.Installation _textMate;
        private readonly DiagnosticRenderer _diagnosticRenderer;
        
        internal List<ParseDiagnostic> diagnostics = new();

        public DictionaryErrorWindow() {
            InitializeComponent();
            _editor = this.FindControl<TextEditor>("Editor")!;
            
            // Setup Syntax Highlighting
            var registry = new RegistryOptions(ThemeManager.IsDarkMode ? ThemeName.DarkPlus : ThemeName.LightPlus);
            _textMate = _editor.InstallTextMate(registry);
            _editor.Options.ConvertTabsToSpaces = true;
            _editor.Options.IndentationSize = 2;

            // Register the Squiggly Line Renderer
            _diagnosticRenderer = new DiagnosticRenderer(this);
            _editor.TextArea.TextView.BackgroundRenderers.Add(_diagnosticRenderer);

            // Register Hover Events
            _editor.PointerHover += OnEditorPointerHover;
            _editor.PointerHoverStopped += (s, e) => ToolTip.SetIsOpen(_editor, false);

            // Setup Debounced Validation Timer
            _validateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _validateTimer.Tick += (s, e) => {
                _validateTimer.Stop();
                ValidateSyntax();
            };

            _editor.TextChanged += (s, e) => {
                if (DataContext is DictionaryErrorWindowViewModel vm) {
                    vm.RawText = _editor.Text;
                }
                _validateTimer.Stop();
                _validateTimer.Start();
            };

            this.Opened += Window_Opened;
        }

        // Calculates exact character offsets for the renderer and hover tips
        internal (int start, int end) OffsetsOf(ParseDiagnostic d) {
            var document = _editor.Document;
            if (document == null) return (0, 0);

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

        private void OnEditorPointerHover(object? sender, Avalonia.Input.PointerEventArgs e) {
            var position = _editor.GetPositionFromPoint(e.GetPosition(_editor));
            if (position == null) return;

            int offset = _editor.Document.GetOffset(position.Value.Location);
            
            // Check if the mouse is inside an error line
            var problem = diagnostics.FirstOrDefault(d => {
                var (start, end) = OffsetsOf(d);
                return start <= offset && offset <= end;
            });

            if (problem.Message != null) {
                var text = new TextBlock { 
                    Text = problem.Message, 
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = DiagnosticRenderer.ErrorBrush 
                };
                
                var tip = new StackPanel { MaxWidth = 420 };
                tip.Children.Add(text);

                ToolTip.SetTip(_editor, tip);
                ToolTip.SetIsOpen(_editor, true);
            } else {
                ToolTip.SetIsOpen(_editor, false);
            }
        }

        private void Window_Opened(object? sender, EventArgs e) {
            if (DataContext is DictionaryErrorWindowViewModel vm) {
                _editor.Document = new TextDocument(vm.RawText);
                
                var registry = new RegistryOptions(ThemeManager.IsDarkMode ? ThemeName.DarkPlus : ThemeName.LightPlus);
                string ext = vm.FilePath.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) ? ".ini" : ".yaml";
                _textMate.SetGrammar(registry.GetScopeByLanguageId(registry.GetLanguageByExtension(ext).Id));
                
                ValidateSyntax();
            }
        }

        private void ValidateSyntax() {
            var saveButton = this.FindControl<Button>("SaveButton");
            var statusText = this.FindControl<TextBlock>("StatusText");
            var problemList = this.FindControl<ListBox>("ProblemList");
            
            if (DataContext is not DictionaryErrorWindowViewModel vm) return;

            bool hasError = false;
            string errorDetail = "";
            
            diagnostics.Clear();

            try {
                if (vm.FilePath.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) {
                    var lines = _editor.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    for (int i = 0; i < lines.Length; i++) {
                        string line = lines[i].Trim();
                        if (string.IsNullOrEmpty(line) || line.StartsWith(";") || line.StartsWith("#") || (line.StartsWith("[") && line.EndsWith("]"))) continue;

                        if (line.StartsWith("=") || (line.Contains("=") && line.Split('=').Length == 1)) {
                            diagnostics.Add(new ParseDiagnostic(i + 1, 1, i + 1, 999, $"Line {i + 1}: Malformed key-value pair."));
                            throw new Exception($"Line {i + 1}: Malformed key-value pair.");
                        }
                    }
                } else {
                    var yaml = new YamlDotNet.RepresentationModel.YamlStream();
                    yaml.Load(new System.IO.StringReader(_editor.Text));
                }
            } catch (Exception ex) {
                hasError = true;
                int bracketErrorLine = -1;
                if (!vm.FilePath.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) {
                    var fileLines = _editor.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    for (int i = 0; i < fileLines.Length; i++) {
                        string l = fileLines[i];
                        if (l.TrimStart().StartsWith("#")) continue;
                        
                        // Count opening vs closing brackets
                        int curly = l.Count(c => c == '{') - l.Count(c => c == '}');
                        int square = l.Count(c => c == '[') - l.Count(c => c == ']');
                        
                        if (curly != 0 || square != 0) {
                            bracketErrorLine = i + 1;
                            break;
                        }
                    }
                }
                if (bracketErrorLine != -1) {
                    errorDetail = "Mismatched brackets detected ('{', '}', '[', or ']').";
                    diagnostics.Add(new ParseDiagnostic(bracketErrorLine, 1, bracketErrorLine, 999, errorDetail));
                } 
                else if (ex is YamlDotNet.Core.YamlException yamlEx) {
                    errorDetail = yamlEx.InnerException?.Message ?? yamlEx.Message;
                    diagnostics.Add(new ParseDiagnostic(yamlEx.Start.Line, yamlEx.Start.Column, yamlEx.End.Line, yamlEx.End.Column, errorDetail));
                } 
                else {
                    errorDetail = ex.InnerException?.Message ?? ex.Message;
                    diagnostics.Add(new ParseDiagnostic(1, 1, 1, 999, errorDetail));
                }
            }

            if (saveButton != null) saveButton.IsEnabled = !hasError;
            
            if (problemList != null && statusText != null) {
                if (hasError) {
                    problemList.ItemsSource = new[] { errorDetail };
                    problemList.IsVisible = true;
                    statusText.Text = "1 Error found. Fix it before saving.";
                    statusText.Foreground = Brushes.Red;
                } else {
                    problemList.IsVisible = false;
                    statusText.Text = "No syntax errors.";
                    statusText.Foreground = Brushes.Green;
                }
            }
            _editor.TextArea.TextView.InvalidateLayer(_diagnosticRenderer.Layer);
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e) {
            Close(false);
        }

        private void SaveButton_Click(object? sender, RoutedEventArgs e) {
            if (DataContext is DictionaryErrorWindowViewModel vm) {
                vm.SaveCorrections();
                Close(true);
            }
        }
    }
}