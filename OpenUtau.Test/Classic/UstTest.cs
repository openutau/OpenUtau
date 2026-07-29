using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenUtau.Core.Ustx;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using Xunit;
using Xunit.Abstractions;

namespace OpenUtau.Classic {
    public class UstTest {
        readonly ITestOutputHelper output;

        public UstTest(ITestOutputHelper output) {
            this.output = output;
        }

        [Fact]
        public void UstLoadingTest() {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            dir = Path.Join(dir, "Usts");
            var encoding = Encoding.GetEncoding("shift_jis");
            var readerOptions = new ReaderOptions {
                ArchiveEncoding = new ArchiveEncoding {
                    Forced = encoding,
                }
            };
            foreach (var file in Directory.GetFiles(dir, "*.zip")) {
                using (var archive = ArchiveFactory.OpenArchive(file, readerOptions)) {
                    foreach (var entry in archive.Entries) {
                        if (Path.GetExtension(entry.Key) != ".ust") {
                            continue;
                        }
                        output.WriteLine(Path.GetFileName(file) + ":" + entry.Key);
                        using (var reader = new StreamReader(entry.OpenEntryStream(), encoding)) {
                            var project = Ust.Load(reader, entry.Key);
                            project.AfterLoad();
                            project.ValidateFull();
                            Assert.Single(project.parts);
                            var part = project.parts[0] as UVoicePart;
                            Assert.True(part.notes.Count > 0);
                        }
                    }
                }
            }
        }


        [Fact]
        public void EqualInLyric() {
            string ust = @"
[#SETTING]
Tempo=128.00
Tool1=
[#0000]
Length=15
Lyric=A==B[C=D],EFG
NoteNum=60
PreUtterance=
VBR=80,200,20,20,20,0,-50,0
PBW=292,183
PBS=-222;-19
PBY=-20.7,
";
            using (var stream = new MemoryStream()) {
                using (var writer = new StreamWriter(stream, leaveOpen: true)) {
                    writer.Write(ust);
                }
                stream.Seek(0, SeekOrigin.Begin);
                using (var reader = new StreamReader(stream)) {
                    var project = Ust.Load(reader, "test.ust");
                    project.AfterLoad();
                    project.ValidateFull();
                    Assert.Single(project.parts);
                    var part = project.parts[0] as UVoicePart;
                    Assert.Single(part.notes);
                    Assert.Equal("A==B[C=D],EFG", part.notes.First().lyric);
                    Assert.Equal(60, part.notes.First().tone);
                    Assert.Equivalent(new UPitch {data = new List<PitchPoint> {
                            new PitchPoint { X = -222, Y = -19, shape = PitchPointShape.io},
                            new PitchPoint { X = 70, Y = -20.7f, shape = PitchPointShape.io}, // X = -222 + 292 = 70
                            new PitchPoint { X = 253, Y = 0, shape = PitchPointShape.io} // X = 70 + 183 = 253
                        }, snapFirst = false } , part.notes.First().pitch);
                    Assert.Equivalent(new UVibrato { length = 80, period = 200, depth = 20, @in = 20, @out = 20, shift = 0, drift = -50, volLink = 0 }, part.notes.First().vibrato);
                }
            }
        }

        [Fact]
        public void ParsePluginParseNoteTest() {
            // This method is tested only when the plugin returns only some blocks

            // Given
            var project = new UProject();
            project.tracks.Add(new UTrack(project) {
                TrackNo = 0,
            });
            var part = new UVoicePart() {
                trackNo = 0,
                position = 0,
            };
            project.parts.Add(part);

            var before = UNote.Create();
            before.lyric = "a";
            before.duration = 100;
            
            var first = UNote.Create();
            first.lyric = "ka";
            first.duration = 200;
            first.pitch.data = new List<PitchPoint> {
                            new PitchPoint { X = -50, Y = 0, shape = PitchPointShape.io},
                            new PitchPoint { X = 0, Y = 10, shape = PitchPointShape.io},
                            new PitchPoint { X = 93.75f, Y = -12.2f, shape = PitchPointShape.io},
                            new PitchPoint { X = 194.7f, Y = 0, shape = PitchPointShape.io}
            };
            
            var second = UNote.Create();
            second.lyric = "r";
            second.duration = 300;
            
            var third = UNote.Create();
            third.lyric = "ta";
            third.duration = 400;
            third.pitch.data = new List<PitchPoint> {
                            new PitchPoint { X = -100, Y = 0, shape = PitchPointShape.io},
                            new PitchPoint { X = 130, Y = 0, shape = PitchPointShape.io}
            };

            var last = UNote.Create();
            last.lyric = "na";
            last.duration = 500;
            
            var after = UNote.Create();
            after.lyric = "ha";
            after.duration = 600;
            
            part.notes.Add(before);
            part.notes.Add(first);
            part.notes.Add(second);
            part.notes.Add(third);
            part.notes.Add(last);
            part.notes.Add(after);

            var sequence = new List<UNote> {
                first,
                second,
                third,
                last
            };

            var encoding = "shift_jis";

            // Create plugin edited tmp file
            string diffFile = Path.GetTempFileName();
            try {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                using (var writer = new StreamWriter(diffFile, false, Encoding.GetEncoding(encoding))) {
                    // duration and lyric
                    writer.WriteLine("[#0000]");
                    writer.WriteLine("Length=480");
                    writer.WriteLine("Lyric=A");
                    writer.WriteLine("PBY=10,-10,0"); // Change only height of the third point
                    writer.WriteLine("[#0001]");
                    writer.WriteLine("Length=480");
                    writer.WriteLine("Lyric=R");
                    // duration is null (change)
                    writer.WriteLine("[#0002]");
                    writer.WriteLine("Lyric=zo");
                    writer.WriteLine("PBS=-50"); // Reset points
                    writer.WriteLine("PBW=100");
                    writer.WriteLine("PBY=0");
                    // duration is zero (delete)
                    writer.WriteLine("[#0003]");
                    writer.WriteLine("Length=");
                    // insert
                    writer.WriteLine("[#INSERT]");
                    writer.WriteLine("Length=240");
                    writer.WriteLine("Lyric=me");
                }

                // When
                var (toRemove, toAdd) = Ust.ParsePlugin(project, part, first, last, sequence, diffFile, encoding);

                // Then
                Assert.Equal(4, toRemove.Count);
                Assert.Equal(3, toAdd.Count);
                Assert.Equal(480, toAdd[0].duration);
                Assert.Equal("A", toAdd[0].lyric);
                Assert.Equivalent(new UPitch {
                    data = new List<PitchPoint> {
                            new PitchPoint { X = -50, Y = 0, shape = PitchPointShape.io},
                            new PitchPoint { X = 0, Y = 10, shape = PitchPointShape.io},
                            new PitchPoint { X = 93.75f, Y = -10, shape = PitchPointShape.io},
                            new PitchPoint { X = 194.7f, Y = 0, shape = PitchPointShape.io}
                        }, snapFirst = true
                }, toAdd[0].pitch);
                Assert.Equal(400, toAdd[1].duration);
                Assert.Equal("zo", toAdd[1].lyric);
                Assert.Equivalent(new UPitch {
                    data = new List<PitchPoint> {
                            new PitchPoint { X = -50, Y = 0, shape = PitchPointShape.io},
                            new PitchPoint { X = 50, Y = 0, shape = PitchPointShape.io}
                        }, snapFirst = true
                }, toAdd[1].pitch);
                Assert.Equal(240, toAdd[2].duration);
                Assert.Equal("me", toAdd[2].lyric);
            } finally {
                File.Delete(diffFile);
            }
        }
    }
}
