using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Classic {
    public class ClassicRenderer : IRenderer {
        static readonly HashSet<string> supportedExp = new HashSet<string>(){
            Ustx.DYN,
            Ustx.PITD,
            Ustx.CLR,
            Ustx.ENG,
            Ustx.VEL,
            Ustx.VOL,
            Ustx.ATK,
            Ustx.DEC,
            Ustx.MOD,
            Ustx.MODP,
            Ustx.ALT,
            Ustx.DIR,
            Ustx.SHFT
        };

        public USingerType SingerType => USingerType.Classic;

        public bool SupportsRenderPitch => false;

        public bool SupportsExpression(UExpressionDescriptor descriptor) {
            return descriptor.isFlag
                || !string.IsNullOrEmpty(descriptor.flag)
                || supportedExp.Contains(descriptor.abbr);
        }

        public RenderResult Layout(RenderPhrase phrase) {
            return new RenderResult() {
                leadingMs = phrase.leadingMs,
                positionMs = phrase.positionMs,
                estimatedLengthMs = phrase.durationMs + phrase.leadingMs,
            };
        }

        public Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo, CancellationTokenSource cancellation, bool isPreRender) {
            if (phrase.wavtool == SharpWavtool.nameConvergence || phrase.wavtool == SharpWavtool.nameSimple) {
                return RenderInternal(phrase, progress, trackNo, cancellation, isPreRender);
            } else {
                return RenderExternal(phrase, progress, trackNo, cancellation, isPreRender);
            }
        }

        public Task<RenderResult> RenderInternal(RenderPhrase phrase, Progress progress, int trackNo, CancellationTokenSource cancellation, bool isPreRender) {
            var resamplerItems = new List<ResamplerItem>();
            foreach (var phone in phrase.phones) {
                resamplerItems.Add(new ResamplerItem(phrase, phone));
            }
            var task = Task.Run(() => {
                Parallel.ForEach(source: resamplerItems, parallelOptions: new ParallelOptions() {
                    MaxDegreeOfParallelism = Preferences.Default.NumRenderThreads
                }, body: item => {
                    if (!cancellation.IsCancellationRequested && !File.Exists(item.outputFile)) {
                        if (!(item.resampler is WorldlineResampler)) {
                            VoicebankFiles.Inst.CopySourceTemp(item.inputFile, item.inputTemp);
                        }
                        if(!item.phone.direct){
                            lock (Renderers.GetCacheLock(item.outputFile)) {
                                item.resampler.DoResamplerReturnsFile(item, Log.Logger);
                            }
                            if (!File.Exists(item.outputFile)) {
                                DocManager.Inst.Project.timeAxis.TickPosToBarBeat(item.phrase.position + item.phone.position, out int bar, out int beat, out int tick);
                                throw new InvalidDataException($"{item.resampler} failed to resample \"{item.phone.phoneme}\" at {bar}:{beat}.{string.Format("{0:000}", tick)}");
                            }
                        }
                        if (!(item.resampler is WorldlineResampler)) {
                            VoicebankFiles.Inst.CopyBackMetaFiles(item.inputFile, item.inputTemp);
                        }
                    }
                    progress.Complete(1, $"Track {trackNo + 1}: {item.resampler} \"{item.phone.phoneme}\"");
                });
                var result = Layout(phrase);
                var wavtool = new SharpWavtool(true);
                result.samples = wavtool.Concatenate(resamplerItems, string.Empty, cancellation);
                if (result.samples != null) {
                    Renderers.ApplyDynamics(phrase, result);
                }
                return result;
            });
            return task;
        }

        public Task<RenderResult> RenderExternal(RenderPhrase phrase, Progress progress, int trackNo, CancellationTokenSource cancellation, bool isPreRender) {
            var resamplerItems = new List<ResamplerItem>();
            foreach (var phone in phrase.phones) {
                resamplerItems.Add(new ResamplerItem(phrase, phone));
            }
            var task = Task.Run(() => {
                string progressInfo = $"Track {trackNo + 1} : {phrase.wavtool} \"{string.Join(" ", phrase.phones.Select(p => p.phoneme))}\"";
                progress.Complete(0, progressInfo);
                var wavPath = Path.Join(PathManager.Inst.CachePath, $"cat-{phrase.hash:x16}.wav");
                phrase.AddCacheFile(wavPath);
                var result = Layout(phrase);
                if (File.Exists(wavPath)) {
                    try {
                        using (var waveStream = Wave.OpenFile(wavPath)) {
                            result.samples = Wave.GetSamples(waveStream.ToSampleProvider().ToMono(1, 0));
                        }
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to render: failed to open {wavPath}");
                    }
                }
                if (result.samples == null) {
                    foreach (var item in resamplerItems) {
                        VoicebankFiles.Inst.CopySourceTemp(item.inputFile, item.inputTemp);
                    }
                    var wavtool = ToolsManager.Inst.GetWavtool(phrase.wavtool);
                    result.samples = wavtool.Concatenate(resamplerItems, wavPath, cancellation);
                    foreach (var item in resamplerItems) {
                        VoicebankFiles.Inst.CopyBackMetaFiles(item.inputFile, item.inputTemp);
                    }
                }
                progress.Complete(phrase.phones.Length, progressInfo);
                if (result.samples != null) {
                    Renderers.ApplyDynamics(phrase, result);
                }
                return result;
            });
            return task;
        }

        public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) {
            return null;
        }

        public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) {
            var manifest= renderSettings.Resampler.Manifest;
            if (manifest == null) {
                return new UExpressionDescriptor[] { };
            }
            return manifest.expressions.Values.ToArray();
        }

        public override string ToString() => Renderers.CLASSIC;
    }
}
