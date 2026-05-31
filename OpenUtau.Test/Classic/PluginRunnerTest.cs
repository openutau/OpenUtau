using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using Xunit;
using static OpenUtau.Classic.PluginRunner;

namespace OpenUtau.Classic {


    public class PluginRunnerTest {

        public PluginRunnerTest() {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            // When Cache directory is nothing in UnitTest
            if (!Directory.Exists(PathManager.Inst.CachePath)) {
                Directory.CreateDirectory(PathManager.Inst.CachePath);
            }
        }

        class ExecuteTestData : IEnumerable<object[]> {
            private readonly List<object[]> testData = new();

            public ExecuteTestData() {
                testData.Add(new object[] { BasicUProject(), DoNothingResponse(), DoNothingAssertion(), FailedErrorMethod() });
                testData.Add(new object[] { BasicUProject(), IncludeNullResponse(), IncludeNullAssertion(), FailedErrorMethod() });
                testData.Add(new object[] { BasicUProject(), EditFlagsResponse(), EditFlagsAssertion(), FailedErrorMethod() });
            }

            public IEnumerator<object[]> GetEnumerator() => testData.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public static ExecuteArgument BasicUProject() {
                var project = new UProject();
                project.tracks.Add(new UTrack(project) {
                    TrackNo = 0,
                });
                var part = new UVoicePart() {
                    trackNo = 0,
                    position = 0,
                };
                project.parts.Add(part);

                // Any flag must be registered in the project
                Ustx.AddDefaultExpressions(project);
     
                var before = UNote.Create();
                before.lyric = "a";
                before.duration = 10;
                before.position = 0;

                var first = UNote.Create();
                first.lyric = "ka";
                first.duration = 20;
                first.position = 10;
                before.Next = first;

                var second = UNote.Create();
                second.lyric = "r";
                second.duration = 30;
                second.position = 30;
                first.Next = second;
                var secondUpnoneme = new UPhoneme {
                    Parent = second
                };
                secondUpnoneme.SetExpression(project, project.tracks[0], Ustx.GEN, 30);
                // requierd Expression
                secondUpnoneme.SetExpression(project, project.tracks[0], Ustx.VEL, 40);
                secondUpnoneme.SetExpression(project, project.tracks[0], Ustx.VOL, 50);
                secondUpnoneme.SetExpression(project, project.tracks[0], Ustx.MOD, 60);

                var third = UNote.Create();
                third.lyric = "ta";
                third.duration = 40;
                third.position = 60;
                second.Next = third;

                var last = UNote.Create();
                last.lyric = "na";
                last.duration = 50;
                last.position = 100;
                third.Next = last;

                var after = UNote.Create();
                after.lyric = "ha";
                after.duration = 60;
                after.position = 150;
                last.Next = after;

                part.notes.Add(before);
                part.notes.Add(first);
                part.notes.Add(second);
                part.phonemes.Add(secondUpnoneme);
                part.notes.Add(third);
                part.notes.Add(last);
                part.notes.Add(after);

                return new ExecuteArgument(project, part, first, last);
            }

            private static Action<StreamWriter, string> DoNothingResponse() {
                return (writer, text) => {
                    // reserved text assertion
                    var cacheDir = PathManager.Inst.CachePath;
                    var expected = $@"[#SETTING]
Tempo=120
Tracks=1
CacheDir={cacheDir}
Mode2=True
[#PREV]
Length=10
Lyric=R
NoteNum=60
PreUtterance=
[#0000]
Length=20
Lyric=ka
NoteNum=0
PreUtterance=
[#0001]
Length=30
Lyric=r
NoteNum=0
PreUtterance=
Velocity=40
Intensity=50
Modulation=60
Flags=g30B0H0P86
[#0002]
Length=40
Lyric=ta
NoteNum=0
PreUtterance=
[#0003]
Length=50
Lyric=na
NoteNum=0
PreUtterance=
[#NEXT]
Length=60
Lyric=ha
NoteNum=0
PreUtterance=
";
                    // Different line feed code for each OS
                    var eol = Environment.NewLine;
                    expected = expected.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", eol);
                
                    Assert.Equal(expected, text);
                    writer.Write(text);
                };
            }

            private static Action<ReplaceNoteEventArgs> DoNothingAssertion() {
                return (args) => {
                    // Add as needed
                    Assert.Fail("this method is not running");
                };
            }

            private static Action<StreamWriter, string> IncludeNullResponse() {
                return (writer, text) => {
                    // duration and lyric
                    writer.WriteLine("[#0000]");
                    writer.WriteLine("Length=480");
                    writer.WriteLine("Lyric=A");
                    writer.WriteLine("[#0001]");
                    writer.WriteLine("Length=480");
                    writer.WriteLine("Lyric=R");
                    // duration is null (change)
                    writer.WriteLine("[#0002]");
                    writer.WriteLine("Lyric=zo");
                    // duration is zero (delete)
                    writer.WriteLine("[#0003]");
                    writer.WriteLine("Length=");
                    // insert
                    writer.WriteLine("[#INSERT]");
                    writer.WriteLine("Length=240");
                    writer.WriteLine("Lyric=me");
                };
            }

            private static Action<ReplaceNoteEventArgs> IncludeNullAssertion() {
                return (args) => {
                    Assert.Equal(4, args.ToRemove.Count);
                    Assert.Equal(3, args.ToAdd.Count);
                    Assert.Equal(480, args.ToAdd[0].duration);
                    Assert.Equal("A", args.ToAdd[0].lyric);
                    Assert.Equal(40, args.ToAdd[1].duration);
                    Assert.Equal("zo", args.ToAdd[1].lyric);
                    Assert.Equal(240, args.ToAdd[2].duration);
                    Assert.Equal("me", args.ToAdd[2].lyric);
                };
            }

            private static Action<StreamWriter, string> EditFlagsResponse() {
                return (writer, text) => {
                    writer.WriteLine("[#0000]");
                    // update flags
                    writer.WriteLine("[#0001]");
                    writer.WriteLine("Flags=g10");
                    // insert flags
                    writer.WriteLine("[#0002]");
                    writer.WriteLine("Flags=g-10B20");
                    // L is undefined flag
                    writer.WriteLine("[#0003]");
                    writer.WriteLine("Flags=B30L2");
                };
            }

            private static Action<ReplaceNoteEventArgs> EditFlagsAssertion() {
                return (args) => {
                    Assert.Equal(4, args.ToRemove.Count);
                    Assert.Equal(4, args.ToAdd.Count);
                    Assert.Equal(10, args.ToAdd[1].phonemeExpressions.FirstOrDefault(exp => exp.descriptor?.abbr == Ustx.GEN)?.value);
                    Assert.Equal(-10, args.ToAdd[2].phonemeExpressions.FirstOrDefault(exp => exp.descriptor?.abbr == Ustx.GEN)?.value);
                    Assert.Equal(20, args.ToAdd[2].phonemeExpressions.FirstOrDefault(exp => exp.descriptor?.abbr == Ustx.BRE)?.value);
                    Assert.Equal(30, args.ToAdd[3].phonemeExpressions.FirstOrDefault(exp => exp.descriptor?.abbr == Ustx.BRE)?.value);
                };
            }

            private static Action<PluginErrorEventArgs> FailedErrorMethod() {
                return (args) => {
                    throw args.Exception;
                };
            }

            private static Action<PluginErrorEventArgs> EmptyErrorMethod() {
                return (args) => {
                    // do nothing
                };
            }
        }

        [Theory]
        [ClassData(typeof(ExecuteTestData))]
        public void ExecuteTest(ExecuteArgument given, Action<StreamWriter, string> when, Action<ReplaceNoteEventArgs> then, Action<PluginErrorEventArgs> error) {
            // When
            var action = new Action<PluginRunner>(async (runner) => {
                await runner.Execute(given.Project, given.Part, given.First, given.Last, new PluginStub(when));
            });

            // Then (Assert in ClassData)
            action(new PluginRunner(PathManager.Inst, then, error));
        }

        [Fact]
        public void ExecuteErrorTest() {
            // Given
            var given = ExecuteTestData.BasicUProject();

            // When
            var action = new Action<PluginRunner>(async (runner) => {
                await runner.Execute(given.Project, given.Part, given.First, given.Last, new PluginStub((writer, text) => {
                    // return empty text (invoke error)
                }));
            });

            // Then
            var then = new Action<ReplaceNoteEventArgs>((args) => {
                Assert.Fail("");
            });
            var error = new Action<PluginErrorEventArgs>((args) => {
                Assert.True(true);
            });
            action(new PluginRunner(PathManager.Inst, then, error));
        }
    }

    class PluginStub : IPlugin {
        public PluginStub(Action<StreamWriter, string> action) {
            this.action = action;
        }
        private readonly Action<StreamWriter, string> action;

        public string Encoding => "shift_jis";

        public async Task Run(string tempFile) {
            System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var text = await File.ReadAllTextAsync(tempFile, System.Text.Encoding.GetEncoding(Encoding));
            File.Delete(tempFile);
            using (var writer = new StreamWriter(tempFile, false, System.Text.Encoding.GetEncoding(Encoding))) {
                action.Invoke(writer, text);
            }
        }
    }

    public class ExecuteArgument {
        public readonly UProject Project;
        public readonly UVoicePart Part;
        public readonly UNote First;
        public readonly UNote Last;

        public ExecuteArgument(UProject project, UVoicePart part, UNote first, UNote last) {
            Project = project;
            Part = part;
            First = first;
            Last = last;
        }
    }
}
