using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.Views;
using Xunit;

namespace OpenUtau.UiTest {
    /// <summary>
    /// Double-clicking a track's volume or pan value stands a text box in for the digits, so the
    /// editor has to land on the digits it replaces and take them over. The app TextBox style
    /// fights that, being meant for roomy dialogs.
    /// </summary>
    public class TrackHeaderVolumePanTest {
        static void OpenEditor(Window window) {
            var button = window.GetVisualDescendants().OfType<Button>().First(b =>
                b.GetVisualDescendants().OfType<TextBlock>()
                    .Any(t => t.Text == window.FindResource("welcome.new") as string));
            Click(window, button);
        }

        static void Click(Window window, Control control, int times = 1) {
            var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            for (var i = 0; i < times; i++) {
                window.MouseDown(at, MouseButton.Left);
                window.MouseUp(at, MouseButton.Left);
            }
            HeadlessUi.Flush();
        }

        // Opens the editor over the value the named editor stands in for.
        static (TextBox editor, TextPresenter digits, TextBlock value) OpenValueEditor(
                Window window, string editorName) {
            var editor = window.GetVisualDescendants().OfType<TrackHeader>().First()
                .FindControl<TextBox>(editorName)!;
            var value = ((Panel)editor.Parent!).Children.OfType<TextBlock>()
                .First(text => Grid.GetColumn(text) == Grid.GetColumn(editor));
            Click(window, value, 2);
            Assert.True(editor.IsVisible, "double-clicking the value did not open the editor");
            return (editor, editor.GetVisualDescendants().OfType<TextPresenter>().First(), value);
        }

        [Theory]
        [InlineData("VolumeTextBox")]
        [InlineData("PanTextBox")]
        public void EditorStandsOnTheValueItReplaces(string editorName) => HeadlessUi.Run(() => {
            MainWindowTest.InitCore();
            HeadlessUi.Errors.Clear();
            var window = new MainWindow { Width = 1280, Height = 800 };
            try {
                window.Show();
                window.InitProject();
                HeadlessUi.Flush();
                OpenEditor(window);
                var (editor, digits, value) = OpenValueEditor(window, editorName);

                var at = digits.TranslatePoint(new Point(), window)!.Value;
                var instead = value.TranslatePoint(new Point(), window)!.Value;
                Assert.Equal(instead.X, at.X, 1);
                Assert.Equal(instead.Y, at.Y, 1);
                Assert.Equal(value.Bounds.Width, digits.Bounds.Width, 1);
                Assert.Equal(value.Bounds.Height, digits.Bounds.Height, 1);
                // Typing replaces the value instead of inserting into it.
                Assert.Equal(0, editor.SelectionStart);
                Assert.Equal(editor.Text.Length, editor.SelectionEnd);
                var errors = HeadlessUi.Errors.Snapshot();
                Assert.True(errors.Count == 0, "Errors reported:\n" + string.Join("\n", errors));
            } finally {
                window.Close();
            }
        });
    }
}
