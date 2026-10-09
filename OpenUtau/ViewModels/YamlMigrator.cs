using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OpenUtau.Plugin.Builtin;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenUtau.App.Utils {
    public static class YamlMigrator {
        private static readonly IDeserializer Deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        private static readonly ISerializer ItemSerializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
            .WithIndentedSequences()
            .WithEventEmitter(next => new SbpFlowStyleEventEmitter(next))
            .Build();

        private static readonly HashSet<string> StandardSbpKeys = new(StringComparer.OrdinalIgnoreCase) {
            "version", "isglides", "symbols", "entries", "replacements", 
            "fallbacks", "timings", "diphthongs", "vowelsustains"
        };

        /// <summary>
        /// Formats item mappings into flow '{ ... }' and string arrays into '[ ... ]'.
        /// </summary>
        private class SbpFlowStyleEventEmitter : ChainedEventEmitter {
            public SbpFlowStyleEventEmitter(IEventEmitter next) : base(next) { }

            public override void Emit(MappingStartEventInfo eventInfo, IEmitter emitter) {
                if (eventInfo.Source.Type != typeof(Dictionary<string, object>) && 
                    eventInfo.Source.Type != typeof(SyllableBasedPhonemizer.YAMLData)) {
                    eventInfo.Style = MappingStyle.Flow;
                }
                base.Emit(eventInfo, emitter);
            }

            public override void Emit(SequenceStartEventInfo eventInfo, IEmitter emitter) {
                if (eventInfo.Source.Value is IEnumerable enumerable and not string) {
                    bool allScalars = true;
                    bool hasItems = false;
                    foreach (var item in enumerable) {
                        hasItems = true;
                        if (item != null && !item.GetType().IsPrimitive && item is not string && item is not decimal) {
                            allScalars = false;
                            break;
                        }
                    }

                    if (hasItems && allScalars) {
                        eventInfo.Style = SequenceStyle.Flow;
                    } else {
                        eventInfo.Style = SequenceStyle.Block;
                    }
                }
                base.Emit(eventInfo, emitter);
            }
        }

        private class SectionLocation {
            public string Key { get; set; } = string.Empty;
            public int HeaderLineIndex { get; set; }
            public int LastItemLineIndex { get; set; }
            public string Indent { get; set; } = "  ";
        }

        private class InsertionTask {
            public int InsertAfterLineIndex { get; set; }
            public List<string> LinesToInsert { get; set; } = new();
        }

        public static string AutoMerge(string oldYamlText, string newTemplateYamlText, string targetVersion) {
            if (string.IsNullOrWhiteSpace(oldYamlText)) return newTemplateYamlText;
            if (string.IsNullOrWhiteSpace(newTemplateYamlText)) return oldYamlText;

            // Detect line endings (\r\n or \n)
            string lineEnding = oldYamlText.Contains("\r\n") ? "\r\n" : "\n";
            var lines = oldYamlText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();

            //  Update the version line in place, preserving any existing comment
            UpdateVersionInPlace(lines, targetVersion);

            // Parse data models for differential comparison
            var oldRaw = TryParseDynamic(oldYamlText);
            var newRaw = TryParseDynamic(newTemplateYamlText);
            var oldSbp = TryParseTyped(oldYamlText) ?? new SyllableBasedPhonemizer.YAMLData();
            var newSbp = TryParseTyped(newTemplateYamlText) ?? new SyllableBasedPhonemizer.YAMLData();

            // Scan existing section boundaries in the original text
            var sections = ScanSections(lines);
            var insertions = new List<InsertionTask>();
            var newSectionsToAppend = new List<string>();

            // Compare SBP standard keys and inject missing additions
            CheckStandardSection("symbols", sections, oldSbp.symbols, newSbp.symbols, 
                s => s.symbol, insertions, newSectionsToAppend);

            CheckStandardSection("entries", sections, oldSbp.entries, newSbp.entries, 
                e => e.grapheme, insertions, newSectionsToAppend);

            CheckStandardSection("replacements", sections, oldSbp.replacements, newSbp.replacements, 
                r => string.Join(",", r.FromList), insertions, newSectionsToAppend,
                cleanItem: r => { if (r.where == "inside") r.where = null!; });

            CheckStandardSection("fallbacks", sections, oldSbp.fallbacks, newSbp.fallbacks, 
                f => string.Join(",", f.FromList), insertions, newSectionsToAppend,
                cleanItem: f => { if (f.where == "inside") f.where = null!; });

            CheckStandardSection("timings", sections, oldSbp.timings, newSbp.timings, 
                t => t.symbol, insertions, newSectionsToAppend);

            CheckStandardSection("diphthongs", sections, oldSbp.diphthongs, newSbp.diphthongs, 
                d => d.from, insertions, newSectionsToAppend);

            CheckStandardSection("vowelsustains", sections, oldSbp.vowelsustains, newSbp.vowelsustains, 
                vs => vs.symbol, insertions, newSectionsToAppend);

            // Compare dynamic child keys (e.g. wanakana)
            foreach (var kvp in newRaw) {
                if (StandardSbpKeys.Contains(kvp.Key)) continue;

                if (sections.TryGetValue(kvp.Key, out var sec)) {
                    // Section exists in local text; append missing list items to it
                    if (kvp.Value is IList newList && oldRaw.TryGetValue(kvp.Key, out var oldVal) && oldVal is IList oldList) {
                        var seen = new HashSet<string>(oldList.Cast<object>().Select(GetItemIdentifier));
                        var missing = newList.Cast<object>().Where(item => seen.Add(GetItemIdentifier(item))).ToList();

                        if (missing.Count > 0) {
                            var formatted = SerializeItems(missing, sec.Indent);
                            insertions.Add(new InsertionTask {
                                InsertAfterLineIndex = sec.LastItemLineIndex,
                                LinesToInsert = formatted
                            });
                        }
                    }
                } else {
                    // Entire section is brand-new; append to the bottom
                    newSectionsToAppend.Add(SerializeSection(kvp.Key, kvp.Value));
                }
            }

            foreach (var task in insertions.OrderByDescending(t => t.InsertAfterLineIndex)) {
                lines.InsertRange(task.InsertAfterLineIndex + 1, task.LinesToInsert);
            }

            foreach (var secText in newSectionsToAppend) {
                lines.Add(string.Empty);
                lines.AddRange(secText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries));
            }

            return string.Join(lineEnding, lines);
        }

        private static void UpdateVersionInPlace(List<string> lines, string targetVersion) {
            var versionRegex = new Regex(@"^(\s*)version\s*:\s*([^#\r\n]*)(.*)$", RegexOptions.IgnoreCase);
            for (int i = 0; i < lines.Count; i++) {
                var match = versionRegex.Match(lines[i]);
                if (match.Success) {
                    string indent = match.Groups[1].Value;
                    string comment = match.Groups[3].Value.TrimStart();
                    string commentPart = string.IsNullOrEmpty(comment) ? "" : " " + comment;
                    lines[i] = $"{indent}version: \"{targetVersion}\"{commentPart}";
                    return;
                }
            }
            // If missing entirely, place at top
            lines.Insert(0, $"version: \"{targetVersion}\"");
        }

        private static Dictionary<string, SectionLocation> ScanSections(List<string> lines) {
            var sections = new Dictionary<string, SectionLocation>(StringComparer.OrdinalIgnoreCase);
            var topKeyRegex = new Regex(@"^([a-zA-Z0-9_][a-zA-Z0-9_-]*)\s*:");

            var list = new List<SectionLocation>();
            for (int i = 0; i < lines.Count; i++) {
                string line = lines[i];
                if (line.StartsWith("#") || line.StartsWith("-") || char.IsWhiteSpace(line.FirstOrDefault())) {
                    continue;
                }

                var match = topKeyRegex.Match(line);
                if (match.Success) {
                    var sec = new SectionLocation {
                        Key = match.Groups[1].Value,
                        HeaderLineIndex = i,
                        LastItemLineIndex = i
                    };
                    list.Add(sec);
                    sections[sec.Key] = sec;
                }
            }

            // Find the last list item within each section's scope
            for (int s = 0; s < list.Count; s++) {
                var sec = list[s];
                int nextStart = (s + 1 < list.Count) ? list[s + 1].HeaderLineIndex : lines.Count;

                for (int i = sec.HeaderLineIndex + 1; i < nextStart; i++) {
                    string trimmed = lines[i].TrimStart();
                    if (trimmed.StartsWith("-")) {
                        sec.LastItemLineIndex = i;
                        int indentLen = lines[i].Length - trimmed.Length;
                        if (indentLen > 0) {
                            sec.Indent = lines[i].Substring(0, indentLen);
                        }
                    }
                }
            }

            return sections;
        }

        private static void CheckStandardSection<T>(
            string sectionKey,
            Dictionary<string, SectionLocation> sections,
            IEnumerable<T>? oldItems,
            IEnumerable<T>? newItems,
            Func<T, string> keySelector,
            List<InsertionTask> insertions,
            List<string> newSectionsToAppend,
            Action<T>? cleanItem = null) where T : class {

            if (newItems == null) return;

            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (oldItems != null) {
                foreach (var item in oldItems) {
                    string k = keySelector(item);
                    if (!string.IsNullOrEmpty(k)) seenKeys.Add(k);
                }
            }

            var missing = new List<T>();
            foreach (var item in newItems) {
                string k = keySelector(item);
                if (!string.IsNullOrEmpty(k) && seenKeys.Add(k)) {
                    cleanItem?.Invoke(item);
                    missing.Add(item);
                }
            }

            if (missing.Count == 0) return;

            if (sections.TryGetValue(sectionKey, out var sec)) {
                // Section exists in original text; append new items after the last entry
                var formatted = SerializeItems(missing, sec.Indent);
                insertions.Add(new InsertionTask {
                    InsertAfterLineIndex = sec.LastItemLineIndex,
                    LinesToInsert = formatted
                });
            } else {
                // Section does not exist; append to bottom
                newSectionsToAppend.Add(SerializeSection(sectionKey, missing));
            }
        }

        private static List<string> SerializeItems(IEnumerable items, string indent) {
            string rawYaml = ItemSerializer.Serialize(items);
            return rawYaml.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(line => indent + line)
                           .ToList();
        }

        private static string SerializeSection(string sectionKey, object content) {
            var dict = new Dictionary<string, object> { [sectionKey] = content };
            return ItemSerializer.Serialize(dict).TrimEnd();
        }

        private static string GetItemIdentifier(object item) {
            if (item is IDictionary dict) {
                foreach (var k in new[] { "roma", "symbol", "from", "key", "grapheme", "kana" }) {
                    if (dict.Contains(k) && dict[k] != null) return $"{k}:{dict[k]}";
                }
                return string.Join(";", dict.Keys.Cast<object>().Select(k => $"{k}:{dict[k]}"));
            }
            return item?.ToString() ?? "";
        }

        private static Dictionary<string, object> TryParseDynamic(string yaml) {
            try {
                using var reader = new StringReader(yaml);
                return Deserializer.Deserialize<Dictionary<string, object>>(reader) ?? new();
            } catch {
                return new();
            }
        }

        private static SyllableBasedPhonemizer.YAMLData TryParseTyped(string yaml) {
            try {
                using var reader = new StringReader(yaml);
                return Deserializer.Deserialize<SyllableBasedPhonemizer.YAMLData>(reader);
            } catch {
                return new();
            }
        }
    }
}