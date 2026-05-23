using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.Render {
    public static class Renderers {
        public const string CLASSIC = "CLASSIC";
        public const string WORLDLINE_R = "WORLDLINE-R";
        public const string WORLDLINE_R2 = "WORLDLINE-R2";
        public const string ENUNU = "ENUNU";
        public const string VOGEN = "VOGEN";
        public const string DIFFSINGER = "DIFFSINGER";
        public const string VOICEVOX = "VOICEVOX";

        static readonly string[] classicRenderers = new[] { WORLDLINE_R, CLASSIC };
        static readonly string[] enunuRenderers = new[] { ENUNU };
        static readonly string[] vogenRenderers = new[] { VOGEN };
        static readonly string[] diffSingerRenderers = new[] { DIFFSINGER };
        static readonly string[] voicevoxRenderers = new[] { VOICEVOX };
        static readonly string[] noRenderers = new string[0];

        public static string[] GetSupportedRenderers(USingerType singerType) {
            switch (singerType) {
                case USingerType.Classic:
                    return classicRenderers;
                case USingerType.Enunu:
                    return enunuRenderers;
                case USingerType.Vogen:
                    return vogenRenderers;
                case USingerType.DiffSinger:
                    return diffSingerRenderers;
                case USingerType.Voicevox:
                    return voicevoxRenderers;
                default:
                    return noRenderers;
            }
        }

        public static List<string> getRendererOptions() {
            return new List<string> {
                "WORLDLINE-R",
                "Classic"
            };
        }

        public static string GetDefaultRenderer(USingerType singerType) {
            if (Preferences.Default.DefaultRenderer == "Classic" && singerType == USingerType.Classic) {
                return CLASSIC;
            } else {
                return GetSupportedRenderers(singerType)[0];
            }
        }

        public static IRenderer CreateRenderer(string renderer) {
            if (renderer == CLASSIC) {
                return new ClassicRenderer();
            } else if (renderer == WORLDLINE_R2) {
                return new WorldlineRenderer(version: 2);
            } else if (renderer?.StartsWith(WORLDLINE_R.Substring(0, 9)) ?? false) {
                return new WorldlineRenderer(version: 1);
            } else if (renderer == ENUNU) {
                return new Enunu.EnunuRenderer();
            } else if (renderer == VOGEN) {
                return new Vogen.VogenRenderer();
            } else if (renderer == DIFFSINGER) {
                return new DiffSinger.DiffSingerRenderer();
            } else if (renderer == VOICEVOX) {
                return new Voicevox.VoicevoxRenderer();
            }
            return null;
        }

        readonly static ConcurrentDictionary<string, object> cacheLockMap
            = new ConcurrentDictionary<string, object>();

        public static object GetCacheLock(string key) {
            return cacheLockMap.GetOrAdd(key, _ => new object());
        }

        public static void ApplyDynamics(RenderPhrase phrase, RenderResult result) {
            const int interval = 5;
            if (phrase.dynamics == null) {
                return;
            }
            int startTick = phrase.position - phrase.leading;
            double startMs = result.positionMs - result.leadingMs;
            int startSample = 0;
            for (int i = 0; i < phrase.dynamics.Length; ++i) {
                int endTick = startTick + interval;
                double endMs = phrase.timeAxis.TickPosToMsPos(endTick);
                int endSample = Math.Min((int)((endMs - startMs) / 1000 * 44100), result.samples.Length);
                float a = phrase.dynamics[i];
                float b = (i + 1) == phrase.dynamics.Length ? phrase.dynamics[i] : phrase.dynamics[i + 1];
                for (int j = startSample; j < endSample; ++j) {
                    result.samples[j] *= a + (b - a) * (j - startSample) / (endSample - startSample);
                }
                startTick = endTick;
                startSample = endSample;
            }
        }

        public static IReadOnlyList<IResampler> GetSupportedResamplers(IWavtool? wavtool) {
            if (wavtool is SharpWavtool) {
                return ToolsManager.Inst.Resamplers;
            } else {
                return ToolsManager.Inst.Resamplers
                    .Where(r => !(r is WorldlineResampler))
                    .ToArray();
            }
        }

        public static IReadOnlyList<IWavtool> GetSupportedWavtools(IResampler? resampler) {
            return ToolsManager.Inst.Wavtools;
        }
    }
}
