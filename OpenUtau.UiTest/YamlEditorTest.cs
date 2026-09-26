using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.UiTest {
    public class YamlEditorTest {
        // An error (the app could not load a float from "high") and a warning (an unknown key).
        const string Yaml = "name: Test Singer\nportrait_opacity: high\ndefualt_phonemizer: X\n";
        static readonly byte[] bom = { 0xEF, 0xBB, 0xBF };

        // Validation runs off the UI thread; pump the dispatcher until it lands.
        static void WaitFor(Func<bool> condition) {
            var watch = Stopwatch.StartNew();
            while (!condition() && watch.ElapsedMilliseconds < 5000) {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }
            HeadlessUi.Flush();
            Assert.True(condition(), "Timed out");
        }

        static void Click(Button button) {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessUi.Flush();
        }

        static void WithTempDir(Action<string> test) {
            var dir = Path.Combine(Path.GetTempPath(), "OpenUtau.UiTest." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                test(dir);
            } finally {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void ErrorsBlockSavingAndSavesKeepTheBom() => WithTempDir(dir => {
            var file = Path.Combine(dir, "character.yaml");
            File.WriteAllBytes(file, bom.Concat(Encoding.UTF8.GetBytes(Yaml)).ToArray());
            HeadlessUi.Run(() => {
                HeadlessUi.Errors.Clear();
                int saved = 0;
                var yamlEditor = new YamlEditor();
                var window = new Window { Width = 760, Height = 480, Content = yamlEditor };
                try {
                    yamlEditor.Load(file, typeof(VoicebankConfig), () => saved++);
                    window.Show();
                    var problems = yamlEditor.FindControl<ListBox>("ProblemList")!;
                    var editor = yamlEditor.FindControl<TextEditor>("Editor")!;
                    var save = yamlEditor.FindControl<Button>("SaveButton")!;
                    WaitFor(() => problems.ItemCount == 2);
                    Assert.Equal(Yaml, editor.Text);
                    Assert.False(yamlEditor.IsDirty);

                    // Edited but still broken: unsaved, and saving is refused.
                    editor.Document.Insert(editor.Document.TextLength, "author: me\n");
                    HeadlessUi.Flush();
                    Assert.True(yamlEditor.IsDirty);
                    Assert.False(save.IsEnabled);
                    HeadlessUi.SaveScreenshot(window, nameof(ErrorsBlockSavingAndSavesKeepTheBom));
                    Click(save);
                    Assert.Equal(0, saved);
                    Assert.Equal(Yaml, Encoding.UTF8.GetString(File.ReadAllBytes(file).Skip(3).ToArray()));

                    // Fixed, with the warning left in: saves, keeping the byte order mark. Saving right away
                    // validates first (the headless platform doesn't run the timer that validates after typing).
                    var fixedText = editor.Text.Replace("portrait_opacity: high", "portrait_opacity: 0.5");
                    editor.Document.Text = fixedText;
                    Click(save);
                    Assert.Equal(1, saved);
                    Assert.False(yamlEditor.IsDirty);
                    var bytes = File.ReadAllBytes(file);
                    Assert.Equal(bom, bytes.Take(3));
                    Assert.Equal(fixedText, Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));

                    editor.Document.Insert(0, "# note\n");
                    Click(yamlEditor.FindControl<Button>("RevertButton")!);
                    Assert.Equal(fixedText, editor.Text);
                    Assert.Empty(HeadlessUi.Errors.Snapshot());
                } finally {
                    window.Close();
                }
            });
        });

        [Fact]
        public void SuggestsKeysAndValuesWhileTyping() => WithTempDir(dir => {
            var file = Path.Combine(dir, "character.yaml");
            File.WriteAllText(file, "name: A\n");
            HeadlessUi.Run(() => {
                MainWindowTest.InitCore(); // loads the phonemizers
                HeadlessUi.Errors.Clear();
                var yamlEditor = new YamlEditor();
                var window = new Window { Width = 760, Height = 480, Content = yamlEditor };
                try {
                    yamlEditor.Load(file, typeof(VoicebankConfig), null);
                    window.Show();
                    var editor = yamlEditor.FindControl<TextEditor>("Editor")!;
                    editor.TextArea.Focus();
                    void Press(Avalonia.Input.Key key, Avalonia.Input.PhysicalKey physicalKey) {
                        window.KeyPress(key, Avalonia.Input.RawInputModifiers.None, physicalKey, null);
                        HeadlessUi.Flush();
                    }
                    void Type(string text) {
                        window.KeyTextInput(text);
                        HeadlessUi.Flush();
                    }
                    // Starts over, closing any popup left open.
                    void SetText(string text) {
                        Press(Avalonia.Input.Key.Escape, Avalonia.Input.PhysicalKey.Escape);
                        editor.Document.Text = text;
                        editor.CaretOffset = editor.Document.TextLength;
                    }

                    // A key: the popup opens while typing; Enter inserts the key and its colon.
                    SetText("name: A\n");
                    Type("de");
                    HeadlessUi.SaveScreenshot(window, nameof(SuggestsKeysAndValuesWhileTyping));
                    Press(Avalonia.Input.Key.Enter, Avalonia.Input.PhysicalKey.Enter);
                    Assert.Equal("name: A\ndefault_phonemizer: ", editor.Text);

                    // A new line opens the keys it can take.
                    SetText("name: A");
                    Press(Avalonia.Input.Key.Enter, Avalonia.Input.PhysicalKey.Enter);
                    Press(Avalonia.Input.Key.Enter, Avalonia.Input.PhysicalKey.Enter);
                    // Enter in a one-line document starts the platform's line ending.
                    Assert.Equal("name: A\nlocalized_names:\n  ", editor.Text.Replace("\r\n", "\n"));

                    // A section key opens an indented line; an enum key then offers its values.
                    SetText("symbol_set:\n  ");
                    Type("pre");
                    Press(Avalonia.Input.Key.Enter, Avalonia.Input.PhysicalKey.Enter);
                    Assert.Equal("symbol_set:\n  preset: ", editor.Text);
                    Type("hi");
                    Press(Avalonia.Input.Key.Enter, Avalonia.Input.PhysicalKey.Enter);
                    Assert.Equal("symbol_set:\n  preset: hiragana", editor.Text);

                    // Known values of free-text keys: singer types and the installed phonemizers.
                    SetText("singer_type: ");
                    Type("di");
                    Press(Avalonia.Input.Key.Enter, Avalonia.Input.PhysicalKey.Enter);
                    Assert.Equal("singer_type: diffsinger", editor.Text);
                    SetText("default_phonemizer: ");
                    Type("JapaneseVCV");
                    Press(Avalonia.Input.Key.Enter, Avalonia.Input.PhysicalKey.Enter);
                    Assert.Equal("default_phonemizer: OpenUtau.Plugin.Builtin.JapaneseVCVPhonemizer", editor.Text);
                    Assert.Empty(HeadlessUi.Errors.Snapshot());
                } finally {
                    window.Close();
                }
            });
        });

        class FolderSinger : USinger {
            readonly string location;
            public FolderSinger(string name, string location) {
                Name = name;
                this.location = location;
                found = true;
                loaded = true;
            }
            public override string Id => Name;
            public override string Name { get; }
            public override string Location => location;
            public override USingerType SingerType => USingerType.Classic;
            public override IList<string> SearchTerms { get; } = new List<string>();
            public override IList<string> Errors { get; } = new List<string>();
            public override IList<USubbank> Subbanks { get; } = new List<USubbank>();
        }

        [Fact]
        public void SingersDialogHasATabPerYamlFile() => WithTempDir(dir => {
            var dirA = Directory.CreateDirectory(Path.Combine(dir, "a")).FullName;
            var dirB = Directory.CreateDirectory(Path.Combine(dir, "b")).FullName;
            File.WriteAllText(Path.Combine(dirA, "character.yaml"), "name: Singer A\n");
            File.WriteAllText(Path.Combine(dirB, "character.yaml"), "name: Singer B\n");
            HeadlessUi.Run(() => {
                MainWindowTest.InitCore();
                HeadlessUi.Errors.Clear();
                var viewModel = new SingersViewModel();
                var dialog = new SingersDialog { DataContext = viewModel, Width = 1050, Height = 700 };
                try {
                    dialog.Show();
                    viewModel.Singer = new FolderSinger("Singer A", dirA);
                    HeadlessUi.Flush();
                    var tabs = dialog.FindControl<TabControl>("Tabs")!;
                    var characterTab = dialog.FindControl<TabItem>("CharacterYamlTab")!;
                    var yamlEditor = dialog.FindControl<YamlEditor>("CharacterYamlEditor")!;
                    var editor = yamlEditor.FindControl<TextEditor>("Editor")!;
                    Assert.True(characterTab.IsVisible);
                    Assert.False(dialog.FindControl<TabItem>("DsConfigTab")!.IsVisible);
                    Assert.Equal("name: Singer A\n", editor.Text);
                    HeadlessUi.SaveScreenshot(dialog, "SingersDialogOtoTab");

                    tabs.SelectedItem = characterTab;
                    editor.Document.Insert(editor.Document.TextLength, "author: me\n");
                    HeadlessUi.Flush();
                    Assert.Equal("character.yaml *", characterTab.Header);
                    HeadlessUi.SaveScreenshot(dialog, nameof(SingersDialogHasATabPerYamlFile));

                    // Switching singers asks about the unsaved changes (answered No here) and loads the new file.
                    viewModel.Singer = new FolderSinger("Singer B", dirB);
                    HeadlessUi.Flush();
                    var prompt = dialog.OwnedWindows.OfType<MessageBox>().Single();
                    Click(prompt.GetLogicalButtons().Single(b => (string?)b.Content == (string?)dialog.FindResource("button.no")));
                    Assert.Equal("name: Singer B\n", editor.Text);
                    Assert.Equal("character.yaml", characterTab.Header);
                    Assert.Equal("name: Singer A\n", File.ReadAllText(Path.Combine(dirA, "character.yaml")));

                    // Back on the Oto tab, the plot renders again after being unloaded.
                    tabs.SelectedItem = dialog.FindControl<TabItem>("OtoTab");
                    HeadlessUi.Flush();
                    HeadlessUi.Flush();
                    Assert.True(dialog.FindControl<Control>("OtoPlot")!.IsEffectivelyVisible);
                    Assert.Empty(HeadlessUi.Errors.Snapshot().Where(e => !e.Contains("Binding")));
                } finally {
                    dialog.Close();
                }
            });
        });
    }

    static class LogicalButtons {
        public static IEnumerable<Button> GetLogicalButtons(this Control control) =>
            Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(control).OfType<Button>();
    }
}
