using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NAudio.Wave;
using NWaves.Audio;
using OpenUtau.App.ViewModels;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.App.Views {
    public partial class SingersDialog : Window, ICmdSubscriber {
        private bool editingCell = false;

        WaveFile? wav;
        string? wavPath;

        public SingersDialog() {
            InitializeComponent();
            DocManager.Inst.AddSubscriber(this);
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            DocManager.Inst.RemoveSubscriber(this);
            var playBack = PlaybackManager.Inst.AudioOutput;
            var playbackState = playBack.PlaybackState;
            if (playbackState == PlaybackState.Playing) {
                playBack.Stop();
            }
        }

        void OnSingerMenuButton(object sender, RoutedEventArgs args) {
            SingerMenu.PlacementTarget = sender as Button;
            SingerMenu.Open();
        }

        void OnVisitWebsite(object sender, RoutedEventArgs args) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel.Singer == null) {
                return;
            }
            try {
                OS.OpenWeb(viewModel.Singer.Web);
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        async void OnSetImage(object sender, RoutedEventArgs args) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel.Singer == null) {
                return;
            }
            var file = await FilePicker.OpenFile(
                this, "singers.setimage",
                viewModel.Singer.Location,
                FilePickerFileTypes.ImageAll);
            if (file == null) {
                return;
            }
            try {
                //If the image isn't inside the voicebank, copy it in.
                if (!file.StartsWith(viewModel.Singer.Location)) {
                    string newFile = Path.Combine(viewModel.Singer.Location, Path.GetFileName(file));
                    File.Copy(file, newFile, true);
                    file = newFile;
                }
                viewModel.SetImage(Path.GetRelativePath(viewModel.Singer.Location, file));
            } catch (Exception e) {
                Log.Error(e, "Failed to set image");
                _ = await MessageBox.ShowError(this, e);
            }
        }

        async void OnSetPortrait(object sender, RoutedEventArgs args) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel.Singer == null) {
                return;
            }
            var file = await FilePicker.OpenFile(
                this, "singers.setportrait",
                viewModel.Singer.Location,
                FilePickerFileTypes.ImageAll);
            if (file == null) {
                return;
            }
            try {
                //If the image isn't inside the voicebank, copy it in.
                if (!file.StartsWith(viewModel.Singer.Location)) {
                    string newFile = Path.Combine(viewModel.Singer.Location, Path.GetFileName(file));
                    File.Copy(file, newFile, true);
                    file = newFile;
                }
                viewModel.SetPortrait(Path.GetRelativePath(viewModel.Singer.Location, file));
            } catch (Exception e) {
                Log.Error(e, "Failed to set portrait");
                _ = await MessageBox.ShowError(this, e);
            }
        }

        async void OnPublish(object sender, RoutedEventArgs args) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel.Singer == null) {
                return;
            }
            var dialog = new SingerPublishDialog();
            dialog.DataContext = new SingerPublishViewModel(viewModel.Singer);
            await dialog.ShowDialog(this);
        }

        async void OnMerge(object sender, RoutedEventArgs args) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel.Singer == null) {
                return;
            }
            var classicSinger = viewModel.Singer as ClassicSinger;
            if (classicSinger == null) {
                return;
            }
            var dialog = new MergeVoicebankDialog();
            dialog.DataContext = new MergeVoicebankViewModel(classicSinger);
            await dialog.ShowDialog(this);
        }

        void OnSetUseFilenameAsAlias(object sender, RoutedEventArgs args) {
            var viewModel = (DataContext as SingersViewModel)!;
            viewModel.SetUseFilenameAsAlias();
        }

        async void OnEditSubbanksButton(object sender, RoutedEventArgs args) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel.Singer == null) {
                return;
            }
            var dialog = new EditSubbanksDialog();
            dialog.ViewModel.SetSinger(viewModel.Singer!);
            dialog.RefreshSinger = () => viewModel.RefreshSinger();
            var playBack = PlaybackManager.Inst.AudioOutput;
            var playbackState = playBack.PlaybackState;
            if (playbackState == PlaybackState.Playing) {
                playBack.Stop();
            }
            await dialog.ShowDialog(this);
        }

        void OnSelectedSingerChanged(object sender, SelectionChangedEventArgs e) {
            OtoPlot.WaveFile = null;
            var playBack = PlaybackManager.Inst.AudioOutput;
            var playbackState = playBack.PlaybackState;
            if (playbackState == PlaybackState.Playing) {
                playBack.Stop();
            }
        }

        void OnSelectedOtoChanged(object sender, SelectionChangedEventArgs e) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel.Singer == null || e.AddedItems.Count < 1) {
                return;
            }
            var oto = (UOto?)e.AddedItems[0];
            if (oto == null || !File.Exists(oto.File)) {
                return;
            }
            DrawOto(oto);
        }

        void OnBeginningEdit(object sender, DataGridBeginningEditEventArgs e) {
            editingCell = true;
        }

        void OnCellEditEnded(object sender, DataGridCellEditEndedEventArgs e) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (e.EditAction == DataGridEditAction.Commit) {
                viewModel?.NotifyOtoChanged();
            }
            editingCell = false;
        }

        void GotoSourceFile(object sender, RoutedEventArgs args) {
            var oto = OtoGrid?.SelectedItem as UOto;
            if (oto == null) {
                return;
            }
            try {
                OS.GotoFile(oto.File);
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        void GotoVLabelerOto(object sender, RoutedEventArgs args) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel.Singer == null) {
                return;
            }
            var oto = OtoGrid?.SelectedItem as UOto;
            if (oto == null) {
                return;
            }
            if (viewModel.Singer != null) {
                OpenInVLabeler(viewModel.Singer, oto);
            }
        }

        void OnEditInVLabeler(object sender, RoutedEventArgs args) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel.Singer != null) {
                OpenInVLabeler(viewModel.Singer, null);
            }
        }

        private void OpenInVLabeler(USinger singer, UOto? oto) {
            string path = Core.Util.Preferences.Default.VLabelerPath;
            if (string.IsNullOrEmpty(path) || !OS.AppExists(path)) {
                MessageBox.Show(
                    this,
                    ThemeManager.GetString("singers.editoto.setvlabelerpath"),
                    ThemeManager.GetString("errors.caption"),
                    MessageBox.MessageBoxButtons.Ok);
                return;
            }
            try {
                Integrations.VLabelerClient.Inst.GotoOto(singer, oto);
            } catch (Exception e) {
                MessageBox.Show(
                    this,
                    e.ToString(),
                    ThemeManager.GetString("errors.caption"),
                    MessageBox.MessageBoxButtons.Ok);
            }
        }

        void GotoSetParamOto(object sender, RoutedEventArgs args) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel.Singer == null) {
                return;
            }
            var oto = OtoGrid?.SelectedItem as UOto;
            if (oto == null) {
                return;
            }
            if (viewModel.Singer != null) {
                OpenInSetParam(viewModel.Singer, oto);
            }
        }

        /*void OnEditInVSetParam(object sender, RoutedEventArgs args) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel.Singer != null) {
                OpenInSetParam(viewModel.Singer, null);
            }
        }*/

        private void OpenInSetParam(USinger singer, UOto oto) {
            string path = Core.Util.Preferences.Default.SetParamPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
                MessageBox.Show(
                    this,
                    ThemeManager.GetString("singers.editoto.setsetparampath"),
                    ThemeManager.GetString("errors.caption"),
                    MessageBox.MessageBoxButtons.Ok);
                return;
            }
            try {
                var tempFile = Path.Combine(PathManager.Inst.CachePath, "temp.tmp");
                Ust.WriteForSetParam(DocManager.Inst.Project, tempFile, new List<UOto> { oto });

                var startInfo = new ProcessStartInfo() {
                    FileName = path,
                    Arguments = $"\"{tempFile}\"",
                    WorkingDirectory = Path.GetDirectoryName(path)
                };
                using (var process = Process.Start(startInfo)) {
                    process!.WaitForExit();
                }
            } catch (Exception e) {
                MessageBox.Show(
                    this,
                    e.ToString(),
                    ThemeManager.GetString("errors.caption"),
                    MessageBox.MessageBoxButtons.Ok);
            }
        }

        void OnOpenReadme(object sender, RoutedEventArgs e) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel.Singer != null) {
                var readme = Path.Join(viewModel.Singer.Location, "readme.txt");
                if (File.Exists(readme)) {
                    var p = new Process();
                    p.StartInfo = new ProcessStartInfo(readme) {
                        UseShellExecute = true
                    };
                    p.Start();
                } else {
                    MessageBox.Show(
                        this,
                        ThemeManager.GetString("singers.readme.notfound"),
                        ThemeManager.GetString("errors.caption"),
                        MessageBox.MessageBoxButtons.Ok);
                    return;
                }
            }
        }

        string? FindSample(USinger singer){
            var sample = singer.Sample;
            if(sample!=null && File.Exists(sample)){
                return sample;
            } else if (singer.SingerType == USingerType.Classic || singer.SingerType == USingerType.Voicevox) {
                var path = singer.Location;
                if(!Directory.Exists(path)){
                    return null;
                }
                string[] files = Directory.EnumerateFiles(path, "*.wav", SearchOption.AllDirectories)
                        .Union(Directory.EnumerateFiles(path, "*.mp3", SearchOption.AllDirectories))
                        .Union(Directory.EnumerateFiles(path, "*.flac", SearchOption.AllDirectories))
                        .Union(Directory.EnumerateFiles(path, "*.aiff", SearchOption.AllDirectories))
                        .Union(Directory.EnumerateFiles(path, "*.ogg", SearchOption.AllDirectories))
                        .Union(Directory.EnumerateFiles(path, "*.opus", SearchOption.AllDirectories))
                        .ToArray();
                if(files.Length==0){
                    return null;
                }
                Random rnd = new Random(Guid.NewGuid().GetHashCode());
                int choice = rnd.Next(0, files.Length - 1);
                string soundFile = files[choice];
                return soundFile;
            }
            return null;
        }

        public void OnPlayCharacterSample(object sender, RoutedEventArgs e) {
            var viewModel = (DataContext as SingersViewModel)!;
            if (viewModel.Singer != null) {
                var sample = FindSample(viewModel.Singer);
                if(sample == null){
                    return;
                }

                PlaybackManager.Inst.PlayFile(sample);
            }
        }

        public void OnPlaySelectedFile(object sender, RoutedEventArgs e) {
            var oto = OtoGrid?.SelectedItem as UOto;
            if (oto == null) {
                return;
            }

            PlaybackManager.Inst.PlayFile(oto.File);
        }

        void RegenFrq(object sender, RoutedEventArgs args) {
            if (OtoGrid != null &&
                sender is Control control &&
                DataContext is SingersViewModel viewModel) {
                string[] files = OtoGrid.SelectedItems
                    .Cast<UOto>()
                    .Select(oto => oto.File)
                    .ToHashSet()
                    .ToArray();
                MessageBox? msgbox = null;
                string text = ThemeManager.GetString("singers.editoto.regenfrq.regenerating");
                if (files.Length > 1) {
                    msgbox = MessageBox.ShowModal(this, text, text);
                }
                var scheduler = TaskScheduler.FromCurrentSynchronizationContext();
                viewModel.RegenFrq(files, control.Tag as string, count => {
                    msgbox?.SetText(string.Format("{0}\n{1} / {2}", text, count, files.Length));
                }).ContinueWith(task => {
                    msgbox?.Close();
                    if (task.IsFaulted && task.Exception != null) {
                        MessageBox.ShowError(this, task.Exception);
                    } else {
                        DrawOto(viewModel.SelectedOto);
                    }
                }, scheduler);
            }
        }

        void DrawOto(UOto? oto) {
            if (oto == null) {
                wavPath = null;
                wav = null;
                OtoPlot.WaveFile = null;
                OtoPlot.F0 = null;
                return;
            }
            OtoPlot.Timing = new() {
                cutoff = oto.Cutoff,
                offset = oto.Offset,
                consonant = oto.Consonant,
                preutter = oto.Preutter,
                overlap = oto.Overlap,
            };
            OtoPlot.WaveFile = loadWav(oto);
            OtoPlot.F0 = LoadF0(oto.File);
        }

        WaveFile? loadWav(UOto oto) {
            if (wavPath == oto.File) {
                return wav;
            }
            try {
                using (var memStream = new MemoryStream()) {
                    using (var waveStream = Core.Format.Wave.OpenFile(oto.File)) {
                        NAudio.Wave.WaveFileWriter.WriteWavFileToStream(memStream, waveStream);
                    }
                    memStream.Seek(0, SeekOrigin.Begin);
                    wav = new WaveFile(memStream);
                    wavPath = oto.File;
                    return wav;
                }
            } catch (Exception e) {
                Log.Error(e, "failed to load wav");
            }
            return null;
        }

        Tuple<int, double[]>? LoadF0(string wavPath) {
            var frq = new Classic.Frq();
            if (frq.Load(wavPath)) {
                return Tuple.Create(frq.hopSize, frq.f0);
            } else {
                return null;
            }
        }

        void OnKeyDown(object sender, KeyEventArgs args) {
            if (args.Handled || editingCell || (FocusManager?.GetFocusedElement() is TextBox)) {
                return;
            }
            var viewModel = DataContext as SingersViewModel;
            if (viewModel == null || OtoPlot == null || OtoPlot.WaveFile == null) {
                return;
            }
            double durationMs = OtoPlot.WaveFile.Signals[0].Duration * 1000;
            args.Handled = true;
            switch (args.Key) {
                case Key.D1:
                    viewModel.SetOffset(OtoPlot.GetPointerMs(), durationMs);
                    break;
                case Key.D2:
                    viewModel.SetOverlap(OtoPlot.GetPointerMs(), durationMs);
                    break;
                case Key.D3:
                    viewModel.SetPreutter(OtoPlot.GetPointerMs(), durationMs);
                    break;
                case Key.D4:
                    viewModel.SetFixed(OtoPlot.GetPointerMs(), durationMs);
                    break;
                case Key.D5:
                    viewModel.SetCutoff(OtoPlot.GetPointerMs(), durationMs);
                    break;
                case Key.W:
                    OtoPlot.Zoom(0.5, 0.5);
                    break;
                case Key.S:
                    OtoPlot.Zoom(1.5, 0.5);
                    break;
                case Key.A:
                    OtoPlot.Pan(-0.25);
                    break;
                case Key.D:
                    OtoPlot.Pan(0.25);
                    break;
                case Key.Q:
                    if (OtoGrid != null) {
                        OtoGrid.SelectedIndex = Math.Max(0, OtoGrid.SelectedIndex - 1);
                        OtoGrid.ScrollIntoView(OtoGrid.SelectedItem, null);
                    }
                    break;
                case Key.E:
                    if (OtoGrid != null) {
                        OtoGrid.SelectedIndex++;
                        OtoGrid.ScrollIntoView(OtoGrid.SelectedItem, null);
                    }
                    break;
                case Key.F:
                    OtoPlot.Zoom(double.PositiveInfinity, 0.5);
                    break;
                default:
                    args.Handled = false;
                    break;
            }
        }

        #region ICmdSubscriber

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is LoadingNotification loadingNotif && loadingNotif.window == typeof(SingersDialog)) {
                if (loadingNotif.startLoading) {
                    LoadingWindow.BeginLoading(this);
                } else {
                    LoadingWindow.EndLoading();
                }
            } else if (cmd is OtoChangedNotification otoChanged) {
                var viewModel = DataContext as SingersViewModel;
                if (viewModel == null) {
                    return;
                }
                if (otoChanged.external) {
                    viewModel.RefreshSinger();
                }
                DrawOto(viewModel.SelectedOto);
            } else if (cmd is GotoOtoNotification editOto) {
                var viewModel = DataContext as SingersViewModel;
                if (viewModel == null) {
                    return;
                }
                if (editOto.singer != null) {
                    viewModel.GotoOto(editOto.singer, editOto.oto);
                }
                OtoGrid?.ScrollIntoView(OtoGrid.SelectedItem, null);
                Activate();
            }
        }

        #endregion
    }
}
