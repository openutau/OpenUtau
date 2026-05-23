using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.DiffSinger {
    public class DsScriptExportOptions {
        public bool exportPitch = true;
        public bool exportVariance = false;
    }

    public class DiffSingerScript{
        public double offsetMs;
        public string[] text;
        public string[] ph_seq;
        public double[] phDurMs;
        public int[] ph_num;
        public int[] noteSeq;
        public double[] noteDurMs;
        public int[] note_slur;
        public double[]? f0_seq = null;
        public double frameMs;
        public double[]? gender = null;
        public double[]? velocity = null;
        public double[]? energy = null;
        public double[]? breathiness = null;
        public double[]? voicing = null;
        public double[]? tension = null;

        public DiffSingerScript(RenderPhrase phrase, DsScriptExportOptions options) {
            float headMs = DiffSingerUtils.GetHeadMs(phrase);
            float tailMs = DiffSingerUtils.GetTailMs(phrase);

            var notes = phrase.notes;
            var phones = phrase.phones;
            DiffSingerSinger singer = phrase.singer as DiffSingerSinger;

            text = notes.Select(n => n.lyric)
                .Where(s=>!s.StartsWith("+"))
                .Prepend("SP")
                .Append("SP")
                .ToArray();
            ph_seq = phones
                .Select(p => p.phoneme)
                .Prepend("SP")
                .Append("SP")
                .ToArray();
            phDurMs = phones
                .Select(p => p.durationMs)
                .Prepend(headMs)
                .Append(tailMs)
                .ToArray();
            //ph_num
            var phNumList = new List<int>();
            int ep = 4;
            int prevNotePhId = 0;
            int phId = 0;
            int phCount = phones.Length;
            foreach(var note in notes.Where(n=>!n.lyric.StartsWith("+"))) {
                while(phId < phCount && phones[phId].position < note.position-ep){
                    ++phId;
                }
                phNumList.Add(phId - prevNotePhId);
                prevNotePhId = phId;
            }
            phNumList.Add(phCount - prevNotePhId);
            phNumList.Add(1);
            ++phNumList[0];
            ph_num = phNumList.ToArray();

            //Build note arrays with rest notes inserted for gaps between notes
            var noteSeqList = new List<int> { 0 };//head padding
            var noteDurList = new List<double> { headMs+(notes[0].positionMs-phones[0].positionMs) };
            var noteSlurList = new List<int> { 0 };//head padding
            double prevNoteEndMs = notes[0].positionMs;
            foreach(var note in notes) {
                double gapMs = note.positionMs - prevNoteEndMs;
                if (gapMs > 0) {
                    //Insert a rest note for the gap
                    noteSeqList.Add(0);
                    noteDurList.Add(gapMs);
                    noteSlurList.Add(0);
                }
                noteSeqList.Add((note.lyric == "SP" || note.lyric == "AP") ? 0 : note.tone);
                noteDurList.Add(note.durationMs);
                noteSlurList.Add(note.lyric.StartsWith("+") ? 1 : 0);
                prevNoteEndMs = note.positionMs + note.durationMs;
            }
            noteSeqList.Add(0);//tail padding
            noteDurList.Add(tailMs);
            noteSlurList.Add(0);//tail padding
            noteSeq = noteSeqList.ToArray();
            noteDurMs = noteDurList.ToArray();
            note_slur = noteSlurList.ToArray();

            frameMs = singer?.dsConfig.frameMs() ?? 10;

            var phDurFrames = DiffSingerUtils.DurationsMsToFrames(phDurMs, frameMs);
            int headFrames = phDurFrames[0];
            int tailFrames = phDurFrames[^1];
            var totalFrames = phDurFrames.Sum();

            //f0
            if(options.exportPitch){
                f0_seq = DiffSingerUtils.SampleCurve(phrase, phrase.pitches,
                    0, frameMs, totalFrames, headFrames, tailFrames,
                    x => MusicMath.ToneToFreq(x * 0.01));
            }

            //velc
            var velocityCurve = phrase.curves.FirstOrDefault(curve => curve.Item1 == DiffSingerUtils.VELC);
            if (velocityCurve != null) {
                velocity = DiffSingerUtils.SampleCurve(phrase, velocityCurve.Item2,
                    0, frameMs, totalFrames, headFrames, tailFrames,
                    x=>Math.Pow(2, (x - 100) / 100));
            }

            //voicebank specific features
            if(singer != null) {
                //gender
                if (singer.dsConfig.useKeyShiftEmbed) {
                    var range = singer.dsConfig.augmentationArgs.randomPitchShifting.range;
                    var positiveScale = (range[1] == 0) ? 0 : (12 / range[1] / 100);
                    var negativeScale = (range[0] == 0) ? 0 : (-12 / range[0] / 100);
                    gender = DiffSingerUtils.SampleCurve(phrase, phrase.gender, 0, frameMs, totalFrames, headFrames, tailFrames,
                        x => (x < 0) ? (-x * positiveScale) : (-x * negativeScale))
                        .ToArray();
                }

                //variance curves
                if (options.exportVariance && singer.HasVariancePredictor) {
                    var variancePredictor = singer.getVariancePredictor();
                    VarianceResult varianceResult;
                    lock (variancePredictor) {
                        varianceResult = variancePredictor.Process(phrase);
                    }
                    if (varianceResult.energy != null) {
                        energy = ComputeVarianceCurve(
                            phrase, varianceResult.energy, varianceResult,
                            phrase.curves.FirstOrDefault(c => c.Item1 == DiffSingerUtils.ENE)?.Item2,
                            DiffSingerUtils.ENE,
                            frameMs, totalFrames, headFrames, tailFrames);
                    }
                    if (varianceResult.breathiness != null) {
                        breathiness = ComputeVarianceCurve(
                            phrase, varianceResult.breathiness, varianceResult,
                            phrase.breathiness,
                            Format.Ustx.BREC,
                            frameMs, totalFrames, headFrames, tailFrames);
                    }
                    if (varianceResult.voicing != null) {
                        voicing = ComputeVarianceCurve(
                            phrase, varianceResult.voicing, varianceResult,
                            phrase.voicing,
                            Format.Ustx.VOIC,
                            frameMs, totalFrames, headFrames, tailFrames);
                    }
                    if (varianceResult.tension != null) {
                        tension = ComputeVarianceCurve(
                            phrase, varianceResult.tension, varianceResult,
                            phrase.tension,
                            Format.Ustx.TENC,
                            frameMs, totalFrames, headFrames, tailFrames);
                    }
                }
            }

            offsetMs = phrase.phones[0].positionMs - headMs;
        }

        private static double[] ComputeVarianceCurve(
                RenderPhrase phrase, float[] predicted, VarianceResult varianceResult,
                float[] userCurve, string abbr,
                double frameMs, int totalFrames, int headFrames, int tailFrames) {
            var resampled = DiffSingerUtils.ResamplePaddedCurve(
                predicted, totalFrames,
                varianceResult.headFrames, varianceResult.tailFrames,
                headFrames, tailFrames,
                varianceResult.frameMs, (float)frameMs);
            var userSampled = DiffSingerUtils.SampleCurve(
                phrase, userCurve, 0, frameMs, totalFrames, headFrames, tailFrames,
                x => x).Select(x => (float)x).ToArray();
            var deltaFunc = DiffSingerUtils.VarianceDeltaFunctions[abbr];
            return resampled.Zip(userSampled, deltaFunc)
                .Select(x => (double)x).ToArray();
        }

        public RawDiffSingerScript toRaw() {
            return new RawDiffSingerScript(this);
        }

        static public void SavePart(UProject project, UVoicePart part, string filePath, DsScriptExportOptions options) {
            var ScriptArray = RenderPhrase.FromPart(project, project.tracks[part.trackNo], part)
                .Select(x => new DiffSingerScript(x, options).toRaw())
                .ToArray();
            File.WriteAllText(filePath,
                JsonConvert.SerializeObject(ScriptArray, Formatting.Indented),
                new UTF8Encoding(false));
        }
    }

    public class RawDiffSingerScript {
        public double offset;
        public string text;
        public string ph_seq;
        public string ph_dur;
        public string ph_num;
        public string note_seq;
        public string note_dur;
        public string note_dur_seq;
        public string note_slur;
        public string is_slur_seq;
        public string? f0_seq = null;
        public string f0_timestep;
        public string input_type = "phoneme";
        public string? gender_timestep = null;
        public string? gender = null;
        public string? velocity_timestep = null;
        public string? velocity = null;
        public string? energy_timestep = null;
        public string? energy = null;
        public string? breathiness_timestep = null;
        public string? breathiness = null;
        public string? voicing_timestep = null;
        public string? voicing = null;
        public string? tension_timestep = null;
        public string? tension = null;

        static string FormatMsAsSeconds(double ms) {
            return (ms / 1000).ToString("G9", CultureInfo.InvariantCulture);
        }

        static string FormatValue(double x) {
            return x.ToString("f3", CultureInfo.InvariantCulture);
        }

        public RawDiffSingerScript(DiffSingerScript script) {
            offset = script.offsetMs / 1000;
            text = String.Join(" ", script.text);
            ph_seq = String.Join(" ", script.ph_seq);
            ph_num = String.Join(" ", script.ph_num);
            note_seq = String.Join(" ",
                script.noteSeq
                .Select(x => x <= 0 ? "rest" : MusicMath.GetToneName(x)));
            ph_dur = String.Join(" ",script.phDurMs.Select(FormatMsAsSeconds));
            note_dur = String.Join(" ",script.noteDurMs.Select(FormatMsAsSeconds));
            note_dur_seq = ph_dur;
            note_slur = String.Join(" ", script.note_slur);
            is_slur_seq = String.Join(" ", script.ph_seq.Select(x => "0"));

            if(script.f0_seq!=null){
                f0_seq = String.Join(" ", script.f0_seq.Select(x => x.ToString("f1", CultureInfo.InvariantCulture)));
            }
            f0_timestep = (script.frameMs / 1000).ToString("G9", CultureInfo.InvariantCulture);

            if(script.gender != null) {
                gender_timestep = f0_timestep;
                gender = String.Join(" ", script.gender.Select(FormatValue));
            }

            if (script.velocity != null) {
                velocity_timestep = f0_timestep;
                velocity = String.Join(" ", script.velocity.Select(FormatValue));
            }

            if (script.energy != null) {
                energy_timestep = f0_timestep;
                energy = String.Join(" ", script.energy.Select(FormatValue));
            }

            if (script.breathiness != null) {
                breathiness_timestep = f0_timestep;
                breathiness = String.Join(" ", script.breathiness.Select(FormatValue));
            }

            if (script.voicing != null) {
                voicing_timestep = f0_timestep;
                voicing = String.Join(" ", script.voicing.Select(FormatValue));
            }

            if (script.tension != null) {
                tension_timestep = f0_timestep;
                tension = String.Join(" ", script.tension.Select(FormatValue));
            }
        }
    }
}
