using System;
using System.IO;
using Serilog;

namespace OpenUtau.Core {
    public class YamlWatcher : IDisposable {
        public bool Paused { get; set; }

        private FileSystemWatcher watcher;
        private Action reloadCallback;

        public YamlWatcher(string path, Action reloadCallback) {
            this.reloadCallback = reloadCallback;
            
            watcher = new FileSystemWatcher(path);
            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileChanged;
            watcher.Deleted += OnFileChanged;
            watcher.Renamed += OnFileChanged;
            watcher.Error += OnError;
            
            // Filters specifically for .yaml. 
            watcher.Filter = "*.yaml"; 
            watcher.IncludeSubdirectories = true;
            watcher.EnableRaisingEvents = true;
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e) {
            if (Paused) {
                return;
            }
            Log.Information($"YAML File \"{e.FullPath}\" {e.ChangeType}");
            
            // Execute the refresh logic passed in during initialization
            reloadCallback?.Invoke();
        }

        private void OnError(object sender, ErrorEventArgs e) {
            Log.Error($"YAML Watcher error {e}");
        }

        public void Dispose() {
            if (watcher != null) {
                watcher.Dispose();
            }
        }
    }
}