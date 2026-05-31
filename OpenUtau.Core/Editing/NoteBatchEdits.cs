using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Format;

namespace OpenUtau.Core.Editing {
    public class AddTailNote : BatchEdit {
        public string Name => name;

        private string lyric;
        private string name;

        public AddTailNote(string lyric, string name) {
            this.lyric = lyric;
            this.name = name;
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            List<UNote> toAdd = new List<UNote>();
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            foreach (var note in notes) {
                if (note.lyric != lyric && (note.Next == null || note.Next.position > note.End + 120)) {
                    var addNote = project.CreateNote(note.tone, note.End, 120);
                    foreach (var exp in note.phonemeExpressions.OrderBy(exp => exp.index)) {
                        addNote.SetExpression(project, project.tracks[part.trackNo], exp.abbr, new float?[] { exp.value });
                    }
                    toAdd.Add(addNote);
                }
            }
            if (toAdd.Count == 0) {
                return;
            }
            docManager.StartUndoGroup("command.batch.note", true);
            foreach (var note in toAdd) {
                note.lyric = lyric;
                docManager.ExecuteCmd(new AddNoteCommand(part, note));
            }
            docManager.EndUndoGroup();
        }
    }

    public class RemoveTailNote : BatchEdit {
        public string Name => name;

        private string lyric;
        private string name;

        public RemoveTailNote(string lyric, string name) {
            this.lyric = lyric;
            this.name = name;
        }

        bool NeedToBeRemoved(UNote note) {
            return note.lyric == lyric
                && (note.Next == null || note.Next.position > note.End);
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            List<UNote> toRemove = notes.Where(NeedToBeRemoved).ToList();
            if (toRemove.Count == 0) {
                return;
            }
            docManager.StartUndoGroup("command.batch.note", true);
            foreach (var note in toRemove) {
                note.lyric = lyric;
                docManager.ExecuteCmd(new RemoveNoteCommand(part, note));
            }
            docManager.EndUndoGroup();
        }
    }

    public class AddBreathNote : BatchEdit {
        public string Name => name;

        private string lyric;
        private string name;

        public AddBreathNote(string lyric) {
            this.lyric = lyric;
            this.name = "pianoroll.menu.notes.addbreath";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            List<UNote> toAdd = new List<UNote>();
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            foreach (var note in notes) {
                if (note.lyric != lyric) {
                    int duration;
                    if (note.Prev == null) {
                        duration = 480;
                    } else if (note.Prev.lyric == lyric || note.position - 120 <= note.Prev.End) {
                        continue;
                    } else if (note.Prev.End < note.position - 960) {
                        duration = 480;
                    } else {
                        duration = note.position - note.Prev.End;
                    }
                    var addNote = project.CreateNote(note.tone, note.position - duration, duration);
                    foreach (var exp in note.phonemeExpressions.Where(exp => exp.index == 0)) {
                        addNote.SetExpression(project, project.tracks[part.trackNo], exp.abbr, new float?[] { exp.value });
                    }
                    toAdd.Add(addNote);
                }
            }
            if (toAdd.Count == 0) {
                return;
            }
            docManager.StartUndoGroup("command.batch.note", true);
            foreach (var note in toAdd) {
                note.lyric = lyric;
                docManager.ExecuteCmd(new AddNoteCommand(part, note));
            }
            docManager.EndUndoGroup();
        }
    }

    public class Transpose : BatchEdit {
        public string Name => name;

        private int deltaNoteNum;
        private string name;

        public Transpose(int deltaNoteNum, string name) {
            this.deltaNoteNum = deltaNoteNum;
            this.name = name;
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            docManager.StartUndoGroup("command.batch.note", true);
            foreach (var note in notes) {
                docManager.ExecuteCmd(new MoveNoteCommand(part, note, 0, deltaNoteNum));
            }
            docManager.EndUndoGroup();
        }
    }

    public class QuantizeNotes : BatchEdit {
        public virtual string Name => name;

        private int quantize;
        private string name;

        public QuantizeNotes(int quantize) {
            this.quantize = quantize;
            name = $"pianoroll.menu.notes.quantize";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            docManager.StartUndoGroup("command.batch.note", true);
            foreach (var note in notes) {
                int pos = note.position;
                int end = note.End;
                int newPos = (int)Math.Round(1.0 * pos / quantize) * quantize;
                int newEnd = (int)Math.Round(1.0 * end / quantize) * quantize;
                if (newPos != pos) {
                    docManager.ExecuteCmd(new MoveNoteCommand(part, note, newPos - pos, 0));
                    docManager.ExecuteCmd(new ResizeNoteCommand(part, note, newEnd - newPos - note.duration));
                } else if (newEnd != end) {
                    docManager.ExecuteCmd(new ResizeNoteCommand(part, note, newEnd - newPos - note.duration));
                }
            }
            docManager.EndUndoGroup();
        }
    }

    public class AutoLegato : BatchEdit {
        public virtual string Name => name;

        private string name;

        public AutoLegato() {
            name = $"pianoroll.menu.notes.autolegato";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            notes.Sort((a, b) => a.position.CompareTo(b.position));
            docManager.StartUndoGroup("command.batch.note", true);
            for (int i = 0; i < notes.Count - 1; i++) {
                docManager.ExecuteCmd(new ResizeNoteCommand(part, notes[i], notes[i + 1].position - notes[i].position - notes[i].duration));
            }
            docManager.EndUndoGroup();
        }
    }

    public class FixOverlap : BatchEdit {
        /// <summary>
        /// Fix overlapping notes.
        /// If multiple notes start at the same time, only the one with the highest tone will be kept
        /// If one notes's end is overlapped by another note, the end will be moved to the start of the next note
        /// </summary>
        public virtual string Name => name;

        private string name;

        public FixOverlap() {
            name = $"pianoroll.menu.notes.fixoverlap";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            if (notes.Count == 0) {
                return;
            }
            docManager.StartUndoGroup("command.batch.note");
            var currentNote = notes[0];
            foreach (var note in notes.Skip(1)) {
                if (note.position == currentNote.position) {
                    if (note.tone > currentNote.tone) {
                        docManager.ExecuteCmd(new RemoveNoteCommand(part, currentNote));
                        currentNote = note;
                    } else {
                        docManager.ExecuteCmd(new RemoveNoteCommand(part, note));
                    }
                } else if (note.position < currentNote.End) {
                    docManager.ExecuteCmd(new ResizeNoteCommand(part, currentNote, note.position - currentNote.End));
                    currentNote = note;
                } else {
                    currentNote = note;
                }
            }
            docManager.EndUndoGroup();
        }
    }

    public class CommonnoteCopy : BatchEdit {
        public virtual string Name => name;

        private string name;

        public CommonnoteCopy() {
            name = $"pianoroll.menu.notes.commonnotecopy";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            Commonnote.CopyToClipboard(notes, project);
        }
    }

    public class CommonnotePaste : BatchEdit {
        public virtual string Name => name;

        private string name;

        public CommonnotePaste() {
            name = $"pianoroll.menu.notes.commonnotepaste";
        }
        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = Commonnote.LoadFromClipboard(project);
            if (notes == null) {
                return;
            }
            int left = DocManager.Inst.playPosTick;
            int minPosition = notes.Select(note => note.position).Min();
            if (left < part.position) {
                return;
            }
            int offset = left - minPosition - part.position;
            notes.ForEach(note => note.position += offset);
            DocManager.Inst.StartUndoGroup("command.batch.note");
            DocManager.Inst.ExecuteCmd(new AddNoteCommand(part, notes));
            int minDurTick = part.GetMinDurTick(project);
            if (part.Duration < minDurTick) {
                DocManager.Inst.ExecuteCmd(new ResizeVoicePartCommand(project, part, minDurTick - part.Duration, false));
            }
            DocManager.Inst.EndUndoGroup();
        }
    }

    public class HanziToPinyin : BatchEdit {
        public virtual string Name => name;

        private string name;

        public HanziToPinyin() {
            name = "pianoroll.menu.notes.hanzitopinyin";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var pinyinResult = BaseChinesePhonemizer.Romanize(selectedNotes.Select(note => note.lyric));
            docManager.StartUndoGroup("command.batch.lyric", true);
            foreach (var t in Enumerable.Zip(selectedNotes, pinyinResult,
                (note, pinyin) => Tuple.Create(note, pinyin))) {
                docManager.ExecuteCmd(new ChangeNoteLyricCommand(part, t.Item1, t.Item2));
            }
            docManager.EndUndoGroup();
        }
    }

    public class LengthenCrossfade : BatchEdit {
        public virtual string Name => name;
        private string name;
        private double ratio;

        public LengthenCrossfade(double ratio) {
            name = "pianoroll.menu.notes.lengthencrossfade";
            this.ratio = ratio;
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            if (notes.Count == 0) {
                return;
            }
            docManager.StartUndoGroup("command.batch.note", true);
            var track = project.tracks[part.trackNo];
            foreach (var note in notes) {
                foreach (UPhoneme phoneme in part.phonemes) {
                    if (phoneme.Parent == note && phoneme.Prev != null && phoneme.PositionMs == phoneme.Prev.EndMs) {

                        double consonantStretch = Math.Pow(2f, 1.0f - phoneme.GetExpression(project, track, Format.Ustx.VEL).Item1 / 100f);
                        double maxPreutter = phoneme.oto.Preutter * consonantStretch;
                        double prevDur = phoneme.Prev.DurationMs;
                        double preutter = phoneme.preutter;

                        if (maxPreutter > prevDur * 0.9f) {
                            maxPreutter = prevDur * 0.9f;
                        }
                        if (maxPreutter > phoneme.preutter) {
                            docManager.ExecuteCmd(new PhonemePreutterCommand(part, note, phoneme.index, (float)(maxPreutter - phoneme.autoPreutter)));
                            preutter = maxPreutter;
                        }

                        var overlap = preutter * ratio;
                        if (overlap > phoneme.autoOverlap) {
                            docManager.ExecuteCmd(new PhonemeOverlapCommand(part, note, phoneme.index, (float)(overlap - phoneme.autoOverlap)));
                        }
                    }
                }
            }
            docManager.EndUndoGroup();
        }
    }

    public class RandomizeTiming : BatchEdit {
        public virtual string Name => name;
        private string name;

        public RandomizeTiming() {
            name = "pianoroll.menu.notes.randomizetiming";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            if (notes.Count == 0) {
                return;
            }
            docManager.StartUndoGroup("command.batch.note", true);
            const int maxTick = 20;
            int delta;
            Random random = new Random();
            foreach (var note in notes) {
                if (random.Next(2) == 0) { // +
                    var max = Math.Min(maxTick, (int)Math.Round(note.duration / 4f));
                    delta = random.Next(max / 4, max + 1);
                } else { // -
                    var max = maxTick;
                    if (note.Prev != null && note.Prev.End == note.position) {
                        max = Math.Min(maxTick, (int)Math.Round(note.Prev.duration / 4f));
                    }
                    delta = - random.Next(max / 4, max + 1);
                }
                
                if (note.Prev != null && note.Prev.End == note.position) {
                    docManager.ExecuteCmd(new ResizeNoteCommand(part, note.Prev, delta));
                }
                docManager.ExecuteCmd(new MoveNoteCommand(part, note, delta, 0));
                docManager.ExecuteCmd(new ResizeNoteCommand(part, note, -delta));
            }
            docManager.EndUndoGroup();
        }
    }

    public class RandomizePhonemeOffset : BatchEdit {
        public virtual string Name => name;
        private string name;

        public RandomizePhonemeOffset() {
            name = "pianoroll.menu.notes.randomizeoffset";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            if (notes.Count == 0) {
                return;
            }
            docManager.StartUndoGroup("command.batch.note", true);
            const int maxTick = 20 ;
            Random random = new Random();
            foreach (var note in notes) {
                for (int i = 0; i < part.phonemes.Count; i++) {
                    UPhoneme phoneme = part.phonemes[i];
                    if (phoneme.Parent == note) {
                        if (random.Next(2) == 0) { // +
                            var tempo = project.timeAxis.GetBpmAtTick(phoneme.position);
                            var max = Math.Min(maxTick, (int)Math.Round(MusicMath.TempoTickToMs(tempo, phoneme.Duration) / 4));
                            docManager.ExecuteCmd(new PhonemeOffsetCommand(part, note, phoneme.index, random.Next(max / 4, max + 1)));
                        } else { // -
                            var max = maxTick;
                            if (phoneme.Prev != null && phoneme.Prev.End == phoneme.position) {
                                var tempo = project.timeAxis.GetBpmAtTick(part.phonemes[i - 1].position);
                                max = Math.Min(maxTick, (int)Math.Round(MusicMath.TempoTickToMs(tempo, part.phonemes[i - 1].Duration) / 4));
                            }
                            var delta = random.Next(max / 4, max + 1);
                            docManager.ExecuteCmd(new PhonemeOffsetCommand(part, note, phoneme.index, -delta));
                        }
                    }
                }
            }
            docManager.EndUndoGroup();
        }
    }

    public class RandomizeTuning : BatchEdit {
        public virtual string Name => name;
        private string name;
        private int max;

        public RandomizeTuning(int max) {
            name = "pianoroll.menu.notes.randomizetuning";
            this.max = max;
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            if (notes.Count == 0) {
                return;
            }
            docManager.StartUndoGroup("command.batch.note", true);
            Random random = new Random();
            foreach (var note in notes) {
                if (random.Next(2) == 0) { // +
                    docManager.ExecuteCmd(new ChangeNoteTuningCommand(part, note, random.Next(max / 4, max + 1)));
                } else { // -
                    var delta = random.Next(max / 4, max + 1);
                    docManager.ExecuteCmd(new ChangeNoteTuningCommand(part, note, -delta));
                }
            }
            docManager.EndUndoGroup();
        }
    }

    public class LoadRenderedPitch : BatchEdit {
        public virtual string Name => name;

        public bool IsAsync => true;

        private string name;

        public LoadRenderedPitch() {
            name = "pianoroll.menu.notes.loadrenderedpitch";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            RunAsync(
                project, part, selectedNotes, docManager,
                (current, total) => { }, CancellationToken.None);
        }

        public void RunAsync(
            UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager,
            Action<int, int> setProgressCallback, CancellationToken cancellationToken) {
            var renderer = project.tracks[part.trackNo].RendererSettings.Renderer;
            if (renderer == null || !renderer.SupportsRenderPitch) {
                var e = new MessageCustomizableException(
                    "Current renderer doesn't support generating pitch curve", 
                    $"<translate:errors.editing.autopitch.unsupported>",
                    new Exception());
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                return;
            }
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            var positions = notes.Select(n => n.position + part.position).ToHashSet();
            var phrases = part.renderPhrases.Where(phrase => phrase.notes.Any(n => positions.Contains(phrase.position + n.position))).ToArray();
            float minPitD = -1200;
            if (project.expressions.TryGetValue(Format.Ustx.PITD, out var descriptor)) {
                minPitD = descriptor.min;
            }

            int finished = 0;
            setProgressCallback(0, phrases.Length);
            var commands = new List<SetCurveCommand>();
            for (int ph_i = phrases.Count() - 1; ph_i >= 0; ph_i--) {
                var phrase = phrases[ph_i];
                var result = renderer.LoadRenderedPitch(phrase);
                if (result == null) {
                    continue;
                }
                int? lastX = null;
                int? lastY = null;
                // TODO: Optimize interpolation and command.
                if (cancellationToken.IsCancellationRequested) break;
                // Take the first negative tick before start and the first tick after end for each segment;
                // Reverse traversal, so that when the score slices are too close, priority is given to covering the consonant pitch of the next segment, reducing the impact on vowels.
                for (int i = 0; i < result.tones.Length; i++) {
                    if (result.tones[i] < 0) {
                        continue;
                    }
                    int x = phrase.position - part.position + (int)result.ticks[i];
                    if (result.ticks[i] < 0) {
                        if (i + 1 < result.ticks.Length && result.ticks[i + 1] > 0) { } else
                            continue;
                    }
                    if (x >= phrase.position + phrase.duration) {
                        i = result.tones.Length - 1;
                    }
                    int pitchIndex = Math.Clamp((x - (phrase.position - part.position - phrase.leading)) / 5, 0, phrase.pitches.Length - 1);
                    float basePitch = phrase.pitchesBeforeDeviation[pitchIndex];
                    int y = (int)(result.tones[i] * 100 - basePitch);
                    lastX ??= x;
                    lastY ??= y;
                    if (y > minPitD) {
                        commands.Add(new SetCurveCommand(
                            project, part, Format.Ustx.PITD, x, y, lastX.Value, lastY.Value));
                    }
                    lastX = x;
                    lastY = y;
                }
                finished += 1;
                setProgressCallback(finished, phrases.Length);
            }

            DocManager.Inst.PostOnUIThread(() => {
                docManager.StartUndoGroup("command.batch.note", true);
                commands.ForEach(docManager.ExecuteCmd);
                docManager.EndUndoGroup();
            });
        }
    }

    public class BakePitch : BatchEdit {
        public virtual string Name => name;
        private string name;
        public BakePitch() {
            name = "pianoroll.menu.notes.bakepitch";
        }

        struct Point {
            public int X;
            public double Y;
            public PitchPointShape shape;
            public Point(int X, double Y, PitchPointShape shape = PitchPointShape.l) {
                this.X = X;
                this.Y = Y;
                this.shape = shape;
            }

            public Point ChangeShape(PitchPointShape shape) {
                return new Point(X, Y, shape);
            }
        }

        double deltaY(Point pt, Point lineStart, Point lineEnd, PitchPointShape shape) {
            return pt.Y - MusicMath.InterpolateShape(lineStart.X, lineEnd.X, lineStart.Y, lineEnd.Y, pt.X, shape);
        }

        PitchPointShape DetermineShape(Point start, Point middle, Point end) {
            if (start.Y == end.Y) {
                return PitchPointShape.l;
            }
            var k = (middle.Y - start.Y) / (end.Y - start.Y);
            if (k > 0.67) {
                return PitchPointShape.o;
            }
            if (k < 0.33) {
                return PitchPointShape.i;
            }
            return PitchPointShape.l;
        }

        //reference: https://github.com/sdercolin/utaformatix3/blob/0f026f7024386ca8362972043c3471c6f2ac9859/src/main/kotlin/process/RdpSimplification.kt#L43
        /*
        * The Ramer–Douglas–Peucker algorithm is a line simplification algorithm
        * for reducing the number of points used to define its shape.
        *
        * Wikipedia: https://en.wikipedia.org/wiki/Ramer%E2%80%93Douglas%E2%80%93Peucker_algorithm
        * Implementation reference: https://rosettacode.org/wiki/Ramer-Douglas-Peucker_line_simplification
        * */
        //perpendicularDistance is replaced with deltaY, because the units of X and Y are different. 
        //result doesn't contain the last point to enhance performance in recursion
        List<Point> simplifyShape(List<Point> pointList, Double epsilon) {
            if (pointList.Count <= 2) {
                return pointList;
            }

            // Determine line shape
            var middlePoint = pointList[pointList.Count / 2];
            var startPoint = pointList[0];
            var endPoint = pointList[^1];
            var shape = DetermineShape(startPoint, middlePoint, endPoint);

            // Find the point with the maximum distance from line between start and end
            var dmax = 0.0;
            var index = 0;
            var end = pointList.Count - 1;
            for (var i = 1; i < end; i++) {
                var d = Math.Abs(deltaY(pointList[i], pointList[0], pointList[end], shape));
                if (d > dmax) {
                    index = i;
                    dmax = d;
                }
            }
            // If max distance is greater than epsilon, recursively simplify
            List<Point> results = new List<Point>();
            if (dmax > epsilon) {
                // Recursive call
                var recResults1 = simplifyShape(pointList.GetRange(0, index + 1), epsilon);
                var recResults2 = simplifyShape(pointList.GetRange(index, pointList.Count - index), epsilon);

                // Build the result list
                results.AddRange(recResults1);
                results.AddRange(recResults2);
                if (results.Count < 2) {
                    throw new Exception("Problem assembling output");
                }
            } else {
                //Just return the start point
                results.Add(pointList[0].ChangeShape(shape));
            }
            return results;
        }

        public static int LastIndexOfMin<T>(IList<T> self, Func<T, double> selector, int startIndex, int endIndex) {
            if (self == null) {
                throw new ArgumentNullException("self");
            }

            if (self.Count == 0) {
                throw new ArgumentException("List is empty.", "self");
            }

            var min = selector(self[endIndex - 1]);
            int minIndex = endIndex - 1;

            for (int i = endIndex - 1; i >= startIndex; --i) {
                var value = selector(self[i]);
                if (value < min) {
                    min = value;
                    minIndex = i;
                }
            }

            return minIndex;
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            TimeAxis timeAxis = project.timeAxis;
            const int pitchInterval = 5;
            var notes = selectedNotes.Count > 0 ? selectedNotes : part.notes.ToList();
            var positions = notes.Select(n => n.position + part.position).ToHashSet();
            var phrases = part.renderPhrases.Where(phrase => phrase.notes.Any(n => positions.Contains(phrase.position + n.position)));
            float minPitD = -1200;
            if (project.expressions.TryGetValue(Format.Ustx.PITD, out var descriptor)) {
                minPitD = descriptor.min;
            }
            //Dictionary from note start tick to pitch point
            //value is a tuple of (starttick, endtick, pitch points)
            //Here starttick and endtick are project absolute tick, and pitch points are ms relative to the starttick
            var pitchPointsPerNote = new Dictionary<int, Tuple<int, int, List<PitchPoint>>>();
            foreach (var phrase in phrases) {
                var pitchStart = -phrase.leading;
                //var ticks = Enumerable.Range(0, phrase.duration).Select(i => i * 5).ToArray();
                var pitches = phrase.pitches;
                var points = Enumerable.Zip(
                    Enumerable.Range(0, pitches.Length),
                    pitches,
                    (i, pitch) => new Point(pitchStart + i * pitchInterval, pitch)
                ).ToList();

                //Reduce pitch point
                var mustIncludeIndices = phrase.notes
                    .SelectMany(n => new[] {
                        n.position,
                        n.duration>160 ? n.end-80 : n.position+n.duration/2 })
                    .Select(t => (t - pitchStart) / pitchInterval)
                    .Prepend(0)
                    .Append(points.Count - 1)
                    .ToList();
                //pairwise(mustIncludePointIndices) 
                points = mustIncludeIndices.Zip(mustIncludeIndices.Skip(1),
                        (a, b) => simplifyShape(points.GetRange(a, b - a), 10))
                    .SelectMany(x => x).Append(points[^1]).ToList();

                //determine where to distribute pitch point
                int idx = 0;
                //note_boundary[i] is the index of the first pitch point after the end of note i
                var note_boundaries = new int[phrase.notes.Length + 1];
                note_boundaries[0] = 2;
                foreach (int i in Enumerable.Range(0, phrase.notes.Length)) {
                    var note = phrase.notes[i];
                    while (idx < points.Count
                        && points[idx].X < note.end) {
                        idx++;
                    }
                    note_boundaries[i + 1] = idx;
                }
                //if there is zero point in the note, adjusted_boundaries is the index of the last zero point
                //otherwise, it is the index of the pitch point with minimal y-distance to the note
                var adjusted_boundaries = new int[phrase.notes.Length + 1];
                adjusted_boundaries[0] = 2;
                foreach (int i in Enumerable.Range(0, phrase.notes.Length - 1)) {
                    var note = phrase.notes[i];
                    var notePitch = note.tone * 100;
                    //var zero_point = points.FindIndex(note_boundaries[i], note_boundaries[i + 1] - note_boundaries[i], p => p.Y == 0);
                    var zero_point = Enumerable.Range(0, note_boundaries[i + 1] - note_boundaries[i])
                        .Select(j => note_boundaries[i + 1] - 1 - j)
                        .Where(j => (points[j].Y - notePitch) * (points[j - 1].Y - notePitch) <= 0)
                        .DefaultIfEmpty(-1)
                        .First();
                    if (zero_point != -1) {
                        adjusted_boundaries[i + 1] = zero_point + 1;
                    } else {
                        adjusted_boundaries[i + 1] = LastIndexOfMin(points, p => Math.Abs(p.Y - notePitch), note_boundaries[i], note_boundaries[i + 1]) + 2;
                    }
                }
                adjusted_boundaries[^1] = note_boundaries[^1];
                //distribute pitch point to each note
                foreach (int i in Enumerable.Range(0, phrase.notes.Length)) {
                    var note = phrase.notes[i];
                    var pitch = points.GetRange(adjusted_boundaries[i] - 2, adjusted_boundaries[i + 1] - (adjusted_boundaries[i] - 2))
                        .Select(p => new PitchPoint(
                            (float)timeAxis.MsBetweenTickPos(note.position + part.position, p.X + part.position),
                            (float)(p.Y - note.tone * 100) / 10,
                            p.shape))
                        .ToList();
                    pitchPointsPerNote[note.position + phrase.position - part.position]
                        = Tuple.Create(
                            points[adjusted_boundaries[i] - 2].X + phrase.position,
                            points[adjusted_boundaries[i + 1] - 1].X + phrase.position,
                            pitch);
                }
            }
            docManager.StartUndoGroup("command.batch.note", true);
            //Apply pitch points to notes
            foreach (var note in notes) {
                if (pitchPointsPerNote.TryGetValue(note.position, out var tickRangeAndPitch)) {
                    var pitch = tickRangeAndPitch.Item3;
                    docManager.ExecuteCmd(new ResetPitchPointsCommand(part, note));
                    int index = 0;
                    foreach (var point in pitch) {
                        docManager.ExecuteCmd(new AddPitchPointCommand(part, note, point, index));
                        index++;
                    }
                    docManager.ExecuteCmd(new DeletePitchPointCommand(part, note, index));
                    docManager.ExecuteCmd(new DeletePitchPointCommand(part, note, index));
                    var lastPitch = note.pitch.data[^1];
                    docManager.ExecuteCmd(new MovePitchPointCommand(part, lastPitch, 0, -lastPitch.Y));

                }
            }
            //Erase PITD curve that has been converted to pitch points
            foreach (var note in notes) {
                if (pitchPointsPerNote.TryGetValue(note.position, out var tickRangeAndPitch)) {
                    var start = tickRangeAndPitch.Item1 - part.position;
                    var end = tickRangeAndPitch.Item2 - part.position;
                    docManager.ExecuteCmd(new PasteCurveCommand(project, part, Format.Ustx.PITD,
                        start, 0,
                        end, 0));
                }
            }
            //Clear vibratos for selected notes
            foreach (var note in notes) {
                if (note.vibrato.length > 0) {
                    docManager.ExecuteCmd(new VibratoLengthCommand(part, note, 0));
                }
            }
            //Clear MOD+ expressions for selected notes
            docManager.ExecuteCmd(new SetNotesSameExpressionCommand(DocManager.Inst.Project, project.tracks[part.trackNo], part, notes, "mod+", null));
            docManager.EndUndoGroup();
        }
    }

    public class RefreshRealCurves : BatchEdit {
        public virtual string Name => name;

        public bool IsAsync => true;

        private string name;

        public RefreshRealCurves() {
            name = "pianoroll.menu.notes.refreshrealcurves";
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            RunAsync(
                project, part, selectedNotes, docManager,
                (current, total) => { }, CancellationToken.None);
        }

        public void RunAsync(
            UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager,
            Action<int, int> setProgressCallback, CancellationToken cancellationToken) {
            var renderer = project.tracks[part.trackNo].RendererSettings.Renderer;
            if (renderer == null || !renderer.SupportsRealCurve) {
                docManager.ExecuteCmd(new ErrorMessageNotification("Not supported"));
                return;
            }

            int finished = 0;
            setProgressCallback(0, part.renderPhrases.Count);
            var curveDict = new Dictionary<string, UCurve?>();
            var newXsDict = new Dictionary<string, List<int>>();
            var newYsDict = new Dictionary<string, List<int>>();
            for (int ph_i = 0; ph_i < part.renderPhrases.Count; ++ph_i) {
                var phrase = part.renderPhrases[ph_i];
                var results = renderer.LoadRenderedRealCurves(phrase);
                if (results.Count == 0) {
                    continue;
                }
                if (cancellationToken.IsCancellationRequested) break;
                foreach (var result in results) {
                    if (!curveDict.ContainsKey(result.abbr)) {
                        var curve = part.curves.FirstOrDefault(c => c.abbr == result.abbr);
                        curveDict[result.abbr] = curve;
                        newXsDict[result.abbr] = new List<int>();
                        newYsDict[result.abbr] = new List<int>();
                    }
                    var xs = newXsDict[result.abbr];
                    var ys = newYsDict[result.abbr];
                    var ticks = result.ticks.Select(t => phrase.position - part.position + (int)t).ToArray();
                    if (ticks.Length == 0) {
                        continue;
                    }
                    while (xs.Count > 0 && xs[^1] >= ticks[0]) {
                        xs.RemoveAt(xs.Count - 1);
                        ys.RemoveAt(ys.Count - 1);
                    }
                    xs.Add(ticks[0]);
                    ys.Add(-1);
                    xs.AddRange(ticks);
                    ys.AddRange(result.values.Select(v => (int)(v * 1000.0)));
                }
                finished += 1;
                setProgressCallback(finished, part.renderPhrases.Count);
            }
            var commands = curveDict
                .Select(kv => new MergedSetCurveCommand(
                    project, part, kv.Key,
                    kv.Value?.realXs.ToArray() ?? Array.Empty<int>(),
                    kv.Value?.realYs.ToArray() ?? Array.Empty<int>(),
                    newXsDict[kv.Key].ToArray(),
                    newYsDict[kv.Key].ToArray(),
                    true))
                .ToList();

            DocManager.Inst.PostOnUIThread(() => {
                docManager.StartUndoGroup("command.batch.note", true);
                commands.ForEach(docManager.ExecuteCmd);
                docManager.EndUndoGroup();
            });
        }
    }
}
