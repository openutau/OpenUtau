using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DynamicData.Binding;
using OpenUtau.Classic;
using OpenUtau.Core;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace OpenUtau.App.ViewModels {
    public class SingerSetupViewModel : ViewModelBase {
        [Reactive] public int Step { get; set; }
        public ObservableCollection<string> TextItems => textItems;
        [Reactive] public string ArchiveFilePath { get; set; } = string.Empty;
        public Encoding[] Encodings { get; set; } = new Encoding[] {
            Encoding.GetEncoding("shift_jis"),
            Encoding.UTF8,
            Encoding.GetEncoding("gb2312"),
            Encoding.GetEncoding("big5"),
            Encoding.GetEncoding("ks_c_5601-1987"),
            Encoding.GetEncoding("Windows-1252"),
            Encoding.GetEncoding("macintosh"),
        };
        [Reactive] public Encoding ArchiveEncoding { get; set; }
        [Reactive] public Encoding TextEncoding { get; set; }
        [Reactive] public bool MissingInfo { get; set; }
        public string[] SingerTypes { get; set; } = new[] { "utau", "enunu", "diffsinger" };
        [Reactive] public string SingerType { get; set; }

        private ObservableCollectionExtended<string> textItems;

        public SingerSetupViewModel() {
#if DEBUG
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
            SingerType = SingerTypes[0];
            ArchiveEncoding = Encodings[0];
            TextEncoding = Encodings[0];
            textItems = new ObservableCollectionExtended<string>();
            this.WhenAnyValue(vm => vm.ArchiveFilePath)
                .Subscribe(_ => {
                    if (!string.IsNullOrEmpty(ArchiveFilePath)) {
                        if(IsEncrypted(ArchiveFilePath)) {
                            throw new MessageCustomizableException(
                                "Encrypted archive file isn't supported",
                                "<translate:errors.encryptedarchive>", 
                                new Exception("Encrypted archive file: " + ArchiveFilePath)
                            );
                        }                        
                        var config = LoadCharacterYaml(ArchiveFilePath);
                        MissingInfo = string.IsNullOrEmpty(config?.SingerType);
                        if (!string.IsNullOrEmpty(config?.TextFileEncoding)) {
                            try {
                                TextEncoding = Encoding.GetEncoding(config.TextFileEncoding);
                            } catch { }
                        }
                    }
                });
            this.WhenAnyValue(vm => vm.Step, vm => vm.ArchiveEncoding, vm => vm.ArchiveFilePath)
                .Subscribe(_ => RefreshArchiveItems());
            this.WhenAnyValue(vm => vm.Step, vm => vm.TextEncoding)
                .Subscribe(_ => RefreshTextItems());
        }

        public void Back() {
            Step--;
        }

        public void Next() {
            Step++;
        }

        private void RefreshArchiveItems() {
            if (Step != 0) {
                return;
            }
            if (string.IsNullOrEmpty(ArchiveFilePath)) {
                textItems.Clear();
                return;
            }
            var readerOptions = new ReaderOptions {
                ArchiveEncoding = new ArchiveEncoding { Forced = ArchiveEncoding },
            };
            using (var archive = ArchiveFactory.Open(ArchiveFilePath, readerOptions)) {
                textItems.Clear();
                textItems.AddRange(archive.Entries
                    .Select(entry => entry.Key!.Replace("\\", "/"))
                    .ToArray());
            }
        }

        private bool IsEncrypted(string archiveFilePath) {
            using (var archive = ArchiveFactory.Open(archiveFilePath)) {
                return archive.Entries.Any(e => e.IsEncrypted);
            }
        }

        private VoicebankConfig? LoadCharacterYaml(string archiveFilePath) {
            using (var archive = ArchiveFactory.Open(archiveFilePath)) {
                var entry = archive.Entries.FirstOrDefault(e => Path.GetFileName(e.Key)=="character.yaml");
                if (entry == null) {
                    return null;
                }
                using (var stream = entry.OpenEntryStream()) {
                    return VoicebankConfig.Load(stream);
                }
            }
        }

        private void RefreshTextItems() {
            if (Step != 1) {
                return;
            }
            if (string.IsNullOrEmpty(ArchiveFilePath)) {
                textItems.Clear();
                return;
            }
            var readerOptions = new ReaderOptions {
                ArchiveEncoding = new ArchiveEncoding { Forced = ArchiveEncoding },
            };
            using (var archive = ArchiveFactory.Open(ArchiveFilePath, readerOptions)) {
                try {
                    textItems.Clear();
                    foreach (var entry in archive.Entries.Where(entry => entry.Key!.EndsWith("character.txt") || entry.Key.EndsWith("oto.ini"))) {
                        using (var stream = entry.OpenEntryStream()) {
                            using var reader = new StreamReader(stream, TextEncoding);
                            textItems.Add($"------ {entry.Key!.Replace("\\", "/")} ------");
                            int count = 0;
                            while (count < 256 && !reader.EndOfStream) {
                                string? line = reader.ReadLine();
                                if (!string.IsNullOrWhiteSpace(line)) {
                                    textItems.Add(line);
                                    count++;
                                }
                            }
                            if (!reader.EndOfStream) {
                                textItems.Add($"...");
                            }
                        }
                    }
                } catch (Exception ex) {
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
                    Step--;
                }
            }
        }

        public Task Install() {
            string archiveFilePath = ArchiveFilePath;
            var archiveEncoding = ArchiveEncoding;
            var textEncoding = TextEncoding;
            return Task.Run(() => {
                try {
                    var basePath = PathManager.Inst.SingersInstallPath;
                    var installer = new VoicebankInstaller(basePath, (progress, info) => {
                        DocManager.Inst.ExecuteCmd(new ProgressBarNotification(progress, info));
                    }, archiveEncoding, textEncoding);
                    installer.Install(archiveFilePath, SingerType);

                    new Task(() => {
                        DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, ThemeManager.GetString("singersetup.succeeded")));
                        DocManager.Inst.ExecuteCmd(new SingersChangedNotification());
                    }).Start(DocManager.Inst.MainScheduler);
                } catch {
                    new Task(() => {
                        DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, ThemeManager.GetString("singersetup.failed")));
                        DocManager.Inst.ExecuteCmd(new SingersChangedNotification());
                    }).Start(DocManager.Inst.MainScheduler);
                    throw;
                }
            });
        }
    }
}
