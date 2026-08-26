using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Serilog;
using OpenUtau.Core.Util;

namespace OpenUtau.Classic {
    class PluginLoader {
        public static Plugin[] LoadAll(string basePath) {
            Directory.CreateDirectory(basePath);
            var encoding = Encoding.GetEncoding("shift_jis");
            if (Preferences.Default.LoadDeepFolderSinger) {
                return Directory.EnumerateFiles(basePath, "plugin.txt", SearchOption.AllDirectories)
                    .Select(filePath => ParsePluginTxt(filePath, encoding))
                    .ToArray();
            } else {
                return Directory.GetDirectories(basePath)
                    .Where(dir => File.Exists(Path.Combine(dir, "plugin.txt")))
                    .Select(dir => ParsePluginTxt(Path.Combine(dir, "plugin.txt"), encoding))
                    .ToArray();
            }
        }

        private static Plugin ParsePluginTxt(string filePath, Encoding encoding) {
            using (var stream = File.OpenRead(filePath)) {
                using (var reader = new StreamReader(stream, encoding)) {
                    var plugin = new Plugin();
                    var otherLines = new List<string>();
                    while (!reader.EndOfStream) {
                        string line = reader.ReadLine().Trim();
                        var s = line.Split(new char[] { '=' });
                        if (s.Length != 2) {
                            s = line.Split(new char[] { ':' });
                        }
                        Array.ForEach(s, temp => temp.Trim());
                        if (s.Length == 2) {
                            s[0] = s[0].ToLowerInvariant();
                            if (s[0] == "name") {
                                Regex reg = new Regex("(.+)\\(&([A-Za-z0-9])\\)");
                                var match = reg.Match(s[1]);
                                if (match.Success) {
                                    plugin.Shortcut = match.Groups[2].Value;
                                    plugin.Name = match.Groups[1].Value + " (" + plugin.Shortcut + ")";
                                } else {
                                    plugin.Name = s[1];
                                }
                            } else if (s[0] == "execute") {
                                string execute = s[1];
                                if (execute.StartsWith(".\\")) {
                                    execute = execute.Substring(2);
                                }
                                plugin.Executable = Path.Combine(Path.GetDirectoryName(filePath), execute);
                            } else if (s[0] == "notes" && s[1] == "all") {
                                plugin.AllNotes = true;
                            } else if (s[0] == "shell" && s[1] == "use") {
                                plugin.UseShell = true;
                            } else if (s[0] == "encoding"){
                                plugin.Encoding = s[1];
                            } else {
                                otherLines.Add(line);
                            }
                        } else {
                            otherLines.Add(line);
                        }
                    }
                    if (string.IsNullOrWhiteSpace(plugin.Name) || string.IsNullOrWhiteSpace(plugin.Executable)) {
                        throw new FileFormatException($"Failed to load {filePath} using encoding {encoding.EncodingName}");
                    }
                    Log.Information($"Loaded plugin {plugin.Name} {plugin.Executable}");
                    return plugin;
                }
            }
        }
    }
}
