using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.TextMate;
using OpenUtau.App.Controls;
using OpenUtau.App.Utils;
using OpenUtau.Core;
using OpenUtau.Plugin.Builtin;
using Serilog;
using TextMateSharp.Grammars;

namespace OpenUtau.App.Views {
    public partial class YamlMigrationDialog : Window {
        public enum DiffType { Added, Local, Modified }

        private readonly string targetFilePath = string.Empty;
        private readonly string oldContent = string.Empty;
        private readonly string newTemplateContent = string.Empty;
        private readonly string targetVersion = string.Empty;
        private string initialMergedText = string.Empty;

        private TextMate.Installation oldTextMate = null!;
        private TextMate.Installation newTextMate = null!;
        private TextMate.Installation resultTextMate = null!;

        private DiffLineRenderer oldDiffRenderer = null!;
        private DiffLineRenderer newDiffRenderer = null!;
        private DiffLineRenderer resultDiffRenderer = null!;

        // Error & Warning Highlighter
        private readonly DiagnosticRenderer diagnosticRenderer;
        private List<YamlDiagnostic> diagnostics = new();

        private readonly DispatcherTimer validateTimer = null!;
        private bool hasErrors = false;

        public bool MigrationCompleted { get; private set; } = false;

        public YamlMigrationDialog() {
            InitializeComponent();
            diagnosticRenderer = new DiagnosticRenderer(this);
        }

        public YamlMigrationDialog(string filePath, string oldYaml, string templateYaml, string oldVersion, string newVersion) {
            InitializeComponent();
            targetFilePath = filePath;
            oldContent = oldYaml;
            newTemplateContent = templateYaml;
            targetVersion = newVersion;

            TitleBanner.Text = string.Format(
                ThemeManager.GetString("yamlmigration.banner.format"),
                Path.GetFileName(filePath),
                oldVersion,
                newVersion);

            OldVersionLabel.Text = string.Format(
                ThemeManager.GetString("yamlmigration.local.version"),
                oldVersion);

            NewVersionLabel.Text = string.Format(
                ThemeManager.GetString("yamlmigration.incoming.version"),
                newVersion);

            try {
                SetupSyntaxHighlighting();
            } catch (Exception ex) {
                Log.Warning(ex, "[YamlMigrationDialog] Syntax highlighting setup skipped.");
            }

            OldEditor.Document = new TextDocument(oldContent);
            NewEditor.Document = new TextDocument(newTemplateContent);

            try {
                initialMergedText = YamlMigrator.AutoMerge(oldContent, newTemplateContent, targetVersion);
            } catch (Exception ex) {
                Log.Warning(ex, "[YamlMigrationDialog] Initial AutoMerge failed; using incoming template.");
                initialMergedText = newTemplateContent;
            }

            ResultEditor.Document = new TextDocument(initialMergedText);

            // 1. Setup Diff Line Renderers
            SetupDiffRenderers();

            // 2. Setup Wavy Error/Warning Underlines
            diagnosticRenderer = new DiagnosticRenderer(this);
            ResultEditor.TextArea.TextView.BackgroundRenderers.Add(diagnosticRenderer);

            // 3. Setup Hover Tooltip on errors
            ResultEditor.PointerHover += OnResultEditorPointerHover;
            ResultEditor.PointerHoverStopped += (s, e) => ToolTip.SetIsOpen(ResultEditor, false);

            validateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            validateTimer.Tick += (s, e) => {
                validateTimer.Stop();
                ValidateResult();
            };

            ResultEditor.TextChanged += (s, e) => {
                UpdateRevertState();
                InvalidateDiffRenderers();
                validateTimer.Stop();
                validateTimer.Start();
            };

            UseOldButton.Click += (s, e) => {
                ResultEditor.Document.Text = oldContent;
                UpdateRevertState();
                InvalidateDiffRenderers();
            };

            UseNewButton.Click += (s, e) => {
                ResultEditor.Document.Text = newTemplateContent;
                UpdateRevertState();
                InvalidateDiffRenderers();
            };

            AutoMergeButton.Click += (s, e) => {
                try {
                    ResultEditor.Document.Text = YamlMigrator.AutoMerge(oldContent, newTemplateContent, targetVersion);
                } catch {
                    ResultEditor.Document.Text = newTemplateContent;
                }
                UpdateRevertState();
                InvalidateDiffRenderers();
            };

            RevertButton.Click += (s, e) => {
                ResultEditor.Document.Text = initialMergedText;
                UpdateRevertState();
                InvalidateDiffRenderers();
            };

            SaveButton.Click += (s, e) => ApplyMigration();

            UpdateRevertState();
            ValidateResult();
        }

        private void SetupSyntaxHighlighting() {
            var registry = new RegistryOptions(ThemeManager.IsDarkMode ? ThemeName.DarkPlus : ThemeName.LightPlus);
            var yamlLanguage = registry.GetLanguageByExtension(".yaml").Id;
            var scope = registry.GetScopeByLanguageId(yamlLanguage);

            oldTextMate = OldEditor.InstallTextMate(registry);
            oldTextMate.SetGrammar(scope);

            newTextMate = NewEditor.InstallTextMate(registry);
            newTextMate.SetGrammar(scope);

            resultTextMate = ResultEditor.InstallTextMate(registry);
            resultTextMate.SetGrammar(scope);
        }

        private void SetupDiffRenderers() {
            var oldLinesSet = ExtractNormalizedLines(oldContent);
            var newLinesSet = ExtractNormalizedLines(newTemplateContent);

            oldDiffRenderer = new DiffLineRenderer(OldEditor, lineNum => {
                string trimmed = GetLineTrimmed(OldEditor.Document, lineNum);
                if (IsIgnoredLine(trimmed)) return null;
                return !newLinesSet.Contains(trimmed) ? DiffType.Local : null;
            });
            OldEditor.TextArea.TextView.BackgroundRenderers.Add(oldDiffRenderer);

            newDiffRenderer = new DiffLineRenderer(NewEditor, lineNum => {
                string trimmed = GetLineTrimmed(NewEditor.Document, lineNum);
                if (IsIgnoredLine(trimmed)) return null;
                return !oldLinesSet.Contains(trimmed) ? DiffType.Added : null;
            });
            NewEditor.TextArea.TextView.BackgroundRenderers.Add(newDiffRenderer);

            resultDiffRenderer = new DiffLineRenderer(ResultEditor, lineNum => {
                string trimmed = GetLineTrimmed(ResultEditor.Document, lineNum);
                if (IsIgnoredLine(trimmed)) return null;

                bool inOld = oldLinesSet.Contains(trimmed);
                bool inNew = newLinesSet.Contains(trimmed);

                if (inNew && !inOld) return DiffType.Added;
                if (inOld && !inNew) return DiffType.Local;
                if (!inOld && !inNew) return DiffType.Modified;
                return null;
            });
            ResultEditor.TextArea.TextView.BackgroundRenderers.Add(resultDiffRenderer);
        }

        private void InvalidateDiffRenderers() {
            ResultEditor.TextArea.TextView.InvalidateLayer(resultDiffRenderer.Layer);
            OldEditor.TextArea.TextView.InvalidateLayer(oldDiffRenderer.Layer);
            NewEditor.TextArea.TextView.InvalidateLayer(newDiffRenderer.Layer);
        }

        private void UpdateRevertState() {
            RevertButton.IsEnabled = ResultEditor.Text != initialMergedText;
        }

        private static HashSet<string> ExtractNormalizedLines(string text) {
            return new HashSet<string>(
                text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                    .Select(l => l.Trim())
                    .Where(l => !IsIgnoredLine(l)),
                StringComparer.Ordinal
            );
        }

        private static bool IsIgnoredLine(string trimmed) {
            return string.IsNullOrEmpty(trimmed) || 
                   trimmed.StartsWith("#") || 
                   trimmed.StartsWith("version:", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetLineTrimmed(TextDocument doc, int lineNum) {
            if (lineNum < 1 || lineNum > doc.LineCount) return string.Empty;
            var line = doc.GetLineByNumber(lineNum);
            return doc.GetText(line.Offset, line.Length).Trim();
        }

        private async void ValidateResult() {
            string text = ResultEditor.Text;
            var results = await Task.Run(() => YamlValidator.Validate(text, typeof(SyllableBasedPhonemizer.YAMLData)));

            diagnostics = results;
            hasErrors = diagnostics.Any(d => d.IsError);
            int errors = diagnostics.Count(d => d.IsError);
            int warnings = diagnostics.Count - errors;

            // Redraw wavy underlines
            ResultEditor.TextArea.TextView.InvalidateLayer(diagnosticRenderer.Layer);

            if (errors > 0) {
                var firstError = diagnostics.First(d => d.IsError);
                ValidationSummary.Text = $"❌ [{errors}] Line {firstError.StartLine}:{firstError.StartColumn} — {Describe(firstError)}";
                SaveButton.IsEnabled = false;
            } else if (warnings > 0) {
                var firstWarn = diagnostics.First();
                ValidationSummary.Text = $"⚠️ [{warnings}] Line {firstWarn.StartLine}:{firstWarn.StartColumn} — {Describe(firstWarn)}";
                SaveButton.IsEnabled = true;
            } else {
                ValidationSummary.Text = ThemeManager.GetString("yamlmigration.status.valid");
                SaveButton.IsEnabled = true;
            }
        }

        private void ApplyMigration() {
            if (hasErrors) return;

            try {
                File.WriteAllText(targetFilePath, ResultEditor.Text, Encoding.UTF8);
                Log.Information($"[Migration] Successfully wrote migrated YAML to '{targetFilePath}'");

                MigrationCompleted = true;
                Close(true);
            } catch (Exception ex) {
                Log.Error(ex, $"Failed to commit migration to '{targetFilePath}'");
            }
        }

        // Show ToolTip when hovering over wavy underlined text
        private void OnResultEditorPointerHover(object? sender, PointerEventArgs e) {
            var position = ResultEditor.GetPositionFromPoint(e.GetPosition(ResultEditor));
            if (position == null) return;

            int offset = ResultEditor.Document.GetOffset(position.Value.Location);
            var problems = diagnostics.Where(d => {
                var (start, end) = OffsetsOf(d);
                return start <= offset && offset <= end;
            }).ToList();

            if (problems.Count == 0) return;

            var tip = new StackPanel { MaxWidth = 450, Spacing = 2 };
            foreach (var problem in problems) {
                var text = new TextBlock { 
                    Text = $"Line {problem.StartLine}:{problem.StartColumn} - {Describe(problem)}", 
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = problem.IsError ? DiagnosticRenderer.ErrorBrush : DiagnosticRenderer.WarningBrush
                };
                tip.Children.Add(text);
            }
            ToolTip.SetTip(ResultEditor, tip);
            ToolTip.SetIsOpen(ResultEditor, true);
        }

        private static string Describe(YamlDiagnostic d) => d.Kind switch {
            YamlDiagnosticKind.Syntax => string.Format(ThemeManager.GetString("yamleditor.syntax"), d.Detail),
            YamlDiagnosticKind.UnknownKey => string.Format(ThemeManager.GetString("yamleditor.unknownkey"), d.Detail),
            YamlDiagnosticKind.WrongType => string.Format(ThemeManager.GetString("yamleditor.wrongtype"), d.Detail),
            _ => d.Detail,
        };

        private (int start, int end) OffsetsOf(YamlDiagnostic d) {
            var document = ResultEditor.Document;
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

        /// <summary>
        /// Wavy underlines for syntax errors and schema warnings (Matches YamlEditor).
        /// </summary>
        public class DiagnosticRenderer : IBackgroundRenderer {
            public static readonly IBrush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x14, 0x00));
            public static readonly IBrush WarningBrush = new SolidColorBrush(Color.FromRgb(0xD4, 0x8B, 0x00));
            private static readonly IPen errorPen = new Pen(ErrorBrush, 1);
            private static readonly IPen warningPen = new Pen(WarningBrush, 1);
            private readonly YamlMigrationDialog dialog;

            public DiagnosticRenderer(YamlMigrationDialog dialog) {
                this.dialog = dialog;
            }

            public KnownLayer Layer => KnownLayer.Selection;

            public void Draw(TextView textView, DrawingContext drawingContext) {
                if (!textView.VisualLinesValid) return;

                foreach (var d in dialog.diagnostics) {
                    var (start, end) = dialog.OffsetsOf(d);
                    var segment = new TextSegment { StartOffset = start, EndOffset = end };
                    var pen = d.IsError ? errorPen : warningPen;

                    foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment)) {
                        var geometry = new StreamGeometry();
                        using (var context = geometry.Open()) {
                            double y = rect.Bottom - 1;
                            context.BeginFigure(new Point(rect.Left, y), false);
                            for (double x = rect.Left + 2, dy = -2; x <= rect.Right + 2; x += 2, dy = -dy) {
                                context.LineTo(new Point(x, y + (dy < 0 ? -2 : 0)));
                            }
                            context.EndFigure(false);
                        }
                        drawingContext.DrawGeometry(null, pen, geometry);
                    }
                }
            }
        }

        /// <summary>
        /// Background line colors and left gutter bars for diff tracking.
        /// </summary>
        public class DiffLineRenderer : IBackgroundRenderer {
            private static readonly IBrush AddedBg = new SolidColorBrush(Color.FromArgb(0x33, 0x2E, 0xCC, 0x71));
            private static readonly IBrush AddedStripe = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));

            private static readonly IBrush LocalBg = new SolidColorBrush(Color.FromArgb(0x33, 0x34, 0x98, 0xDB));
            private static readonly IBrush LocalStripe = new SolidColorBrush(Color.FromRgb(0x34, 0x98, 0xDB));

            private static readonly IBrush ModifiedBg = new SolidColorBrush(Color.FromArgb(0x38, 0xE6, 0x7E, 0x22));
            private static readonly IBrush ModifiedStripe = new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22));

            private readonly TextEditor editor;
            private readonly Func<int, DiffType?> lineDiffProvider;

            public DiffLineRenderer(TextEditor editor, Func<int, DiffType?> lineDiffProvider) {
                this.editor = editor;
                this.lineDiffProvider = lineDiffProvider;
            }

            public KnownLayer Layer => KnownLayer.Background;

            public void Draw(TextView textView, DrawingContext drawingContext) {
                if (!textView.VisualLinesValid) return;

                foreach (var visualLine in textView.VisualLines) {
                    int lineNum = visualLine.FirstDocumentLine.LineNumber;
                    var diff = lineDiffProvider(lineNum);
                    if (diff == null) continue;

                    var (bg, stripe) = diff.Value switch {
                        DiffType.Added => (AddedBg, AddedStripe),
                        DiffType.Local => (LocalBg, LocalStripe),
                        _ => (ModifiedBg, ModifiedStripe)
                    };

                    var docLine = visualLine.FirstDocumentLine;
                    foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, docLine)) {
                        drawingContext.DrawRectangle(bg, null, new Rect(0, rect.Y, textView.Bounds.Width, rect.Height));
                        drawingContext.DrawRectangle(stripe, null, new Rect(0, rect.Y, 3, rect.Height));
                    }
                }
            }
        }
    }
}