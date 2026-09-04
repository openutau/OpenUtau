using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Views {
    public partial class SingerSelectorDialog : Window {
        public USinger? SelectedSinger { get; private set; }

        public SingerSelectorDialog() {
            InitializeComponent();
        }

        void OnConfirm(object? sender, RoutedEventArgs e) {
            Finish();
        }

        void OnCancel(object? sender, RoutedEventArgs e) {
            SelectedSinger = null;
            Close();
        }

        void OnKeyDown(object? sender, KeyEventArgs e) {
            if (e.Key == Key.Escape) {
                e.Handled = true;
                OnCancel(sender, e);
            } else if (e.Key == Key.Enter) {
                e.Handled = true;
                OnConfirm(sender, e);
            }
        }

        void OnSingerDoubleTapped(object? sender, RoutedEventArgs e) {
            Finish();
        }

        void OnRefresh(object? sender, RoutedEventArgs e) {
            if (DataContext is SingerSelectorViewModel vm) {
                vm.RefreshSingers();
            }
        }

        void OnPlaySample(object? sender, RoutedEventArgs e) {
            if (DataContext is not SingerSelectorViewModel vm || vm.SelectedSinger == null) {
                return;
            }
            var sample = FindSample(vm.SelectedSinger);
            if (!string.IsNullOrEmpty(sample)) {
                PlaybackManager.Inst.PlayFile(sample);
            }
        }

        void Finish() {
            if (DataContext is not SingerSelectorViewModel vm || vm.SelectedSinger == null) {
                return;
            }
            SelectedSinger = vm.SelectedSinger;
            Close();
        }

        static string? FindSample(USinger singer) {
            var sample = singer.Sample;
            if (!string.IsNullOrEmpty(sample) && File.Exists(sample)) {
                return sample;
            }
            if (singer.SingerType != USingerType.Classic && singer.SingerType != USingerType.Voicevox) {
                return null;
            }
            if (!Directory.Exists(singer.Location)) {
                return null;
            }
            var files = Directory.EnumerateFiles(singer.Location, "*.wav", SearchOption.AllDirectories)
                .Union(Directory.EnumerateFiles(singer.Location, "*.mp3", SearchOption.AllDirectories))
                .Union(Directory.EnumerateFiles(singer.Location, "*.flac", SearchOption.AllDirectories))
                .Union(Directory.EnumerateFiles(singer.Location, "*.aiff", SearchOption.AllDirectories))
                .Union(Directory.EnumerateFiles(singer.Location, "*.ogg", SearchOption.AllDirectories))
                .Union(Directory.EnumerateFiles(singer.Location, "*.opus", SearchOption.AllDirectories))
                .ToArray();
            if (files.Length == 0) {
                return null;
            }
            var random = new Random(Guid.NewGuid().GetHashCode());
            return files[random.Next(files.Length)];
        }
    }
}
