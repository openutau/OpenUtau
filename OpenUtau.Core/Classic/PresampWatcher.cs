using System;
using System.IO;
using Serilog;

namespace OpenUtau.Core {
    public class PresampWatcher : IDisposable {
        public bool Paused { get; set; }

        private FileSystemWatcher watcher;
        private Action reloadCallback;

        public PresampWatcher(string path, Action reloadCallback) {
            this.reloadCallback = reloadCallback;
            
            watcher = new FileSystemWatcher(path);
            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileChanged;
            watcher.Deleted += OnFileChanged;
            watcher.Renamed += OnFileChanged;
            watcher.Error += OnError;
            
            watcher.Filter = "presamp.ini"; 
            // Set to false since presamp.ini is always at the root of the voicebank
            watcher.IncludeSubdirectories = false; 
            watcher.EnableRaisingEvents = true;
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e) {
            if (Paused) {
                return;
            }
            Log.Information($"Presamp File \"{e.FullPath}\" {e.ChangeType}");
            reloadCallback?.Invoke();
        }

        private void OnError(object sender, ErrorEventArgs e) {
            Log.Error($"Presamp Watcher error {e}");
        }
        public void Dispose() {
            if (watcher != null) {
                watcher.Dispose();
            }
        }
    }
}