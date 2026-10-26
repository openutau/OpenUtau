using System;
using System.Collections.Generic;
using System.Linq;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Js {
    /// <summary>
    /// A phonemizer written in JavaScript and shipped in an oudep package.
    ///
    /// The entry point is an ES module that exports:
    /// <code>
    /// export const info = { name: "...", tag: "XX YYY", author: "...", language: "XX" };
    /// export function setSinger(singer) { }                    // optional
    /// export function setUp(groups) { }                        // optional
    /// export function process(notes, prev, next, prevNeighbour, nextNeighbour, prevNeighbours) {
    ///     return { phonemes: [{ phoneme: "a", position: 0 }] };
    /// }
    /// export function cleanUp() { }                            // optional
    /// </code>
    /// The arguments mirror <see cref="Phonemizer"/>. Helpers are imported from the "openutau" module,
    /// e.g. <c>readText("dict/ja.txt")</c> to read a file of the package, relative to the package folder.
    ///
    /// Each instance runs in its own <see cref="JsSandbox"/>, so each track has its own VM.
    /// </summary>
    public class JsPhonemizer : Phonemizer {
        public const string HostModule = "openutau";

        private readonly JsPhonemizerFactory factory;
        private JsSandbox? sandbox;
        private ObjectInstance? module;
        private JsValue process = JsValue.Undefined;
        private JsValue setSinger = JsValue.Undefined;
        private JsValue setUp = JsValue.Undefined;
        private JsValue cleanUp = JsValue.Undefined;

        public JsPhonemizer(JsPhonemizerFactory factory) {
            this.factory = factory;
        }

        public override string Id => factory.Id;

        /// <summary>
        /// Loads the entry point module, if not yet loaded, and returns its namespace object.
        /// </summary>
        internal ObjectInstance LoadModule() {
            EnsureLoaded();
            return module!;
        }

        internal void Unload() {
            sandbox?.Dispose();
            sandbox = null;
            module = null;
        }

        private JsSandbox EnsureLoaded() {
            if (sandbox != null) {
                return sandbox;
            }
            var sb = new JsSandbox(factory.packagePath, factory.entrypointPath, factory.Id);
            try {
                sb.AddHostModule(HostModule, CreateHostModule(sb));
                module = sb.Import();
                process = module.Get("process");
                setSinger = module.Get("setSinger");
                setUp = module.Get("setUp");
                cleanUp = module.Get("cleanUp");
                if (!process.IsCallable()) {
                    throw new JsSandboxException($"{factory.Id}: does not export a process function.");
                }
            } catch {
                sb.Dispose();
                module = null;
                throw;
            }
            sandbox = sb;
            return sb;
        }

        private Dictionary<string, JsValue> CreateHostModule(JsSandbox sb) {
            return new Dictionary<string, JsValue> {
                ["readText"] = sb.Function("readText",
                    args => sb.ReadPackageText(TypeConverter.ToString(args.At(0)))),
                ["toUnicodeElements"] = sb.Function("toUnicodeElements",
                    args => sb.NewArray(ToUnicodeElements(TypeConverter.ToString(args.At(0))).Select(e => (JsValue)e))),
                ["tickToMs"] = sb.Function("tickToMs",
                    args => timeAxis.TickPosToMsPos(TypeConverter.ToNumber(args.At(0)))),
                ["msToTick"] = sb.Function("msToTick",
                    args => timeAxis.MsPosToTickPos(TypeConverter.ToNumber(args.At(0)))),
                ["getParentConsonantStretchRatio"] = sb.Function("getParentConsonantStretchRatio",
                    args => GetParentConsonantStretchRatio()),
                ["getParentToneShift"] = sb.Function("getParentToneShift",
                    args => GetParentToneShift()),
                ["getParentAlternate"] = sb.Function("getParentAlternate",
                    args => GetParentAlternate() is int alt ? alt : JsValue.Null),
                ["getParentVoiceColor"] = sb.Function("getParentVoiceColor",
                    args => GetParentVoiceColor()),
            };
        }

        public override void SetSinger(USinger singer) {
            var sb = EnsureLoaded();
            if (setSinger.IsCallable()) {
                sb.Call(setSinger, "setSinger", SingerToJs(sb, singer));
            }
        }

        public override void SetUp(Note[][] notes, UProject project, UTrack track) {
            base.SetUp(notes, project, track);
            var sb = EnsureLoaded();
            if (setUp.IsCallable()) {
                sb.Call(setUp, "setUp", sb.NewArray(notes.Select(group => NotesToJs(sb, group))));
            }
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            var sb = EnsureLoaded();
            var result = sb.Call(process, "process",
                NotesToJs(sb, notes),
                NoteToJs(sb, prev),
                NoteToJs(sb, next),
                NoteToJs(sb, prevNeighbour),
                NoteToJs(sb, nextNeighbour),
                NotesToJs(sb, prevNeighbours ?? Array.Empty<Note>()));
            return ParseResult(result);
        }

        public override void CleanUp() {
            if (sandbox != null && cleanUp.IsCallable()) {
                sandbox.Call(cleanUp, "cleanUp");
            }
        }

        static bool IsNullish(JsValue value) => value.IsNull() || value.IsUndefined();

        static JsValue SingerToJs(JsSandbox sb, USinger singer) {
            if (singer == null) {
                return JsValue.Null;
            }
            var obj = sb.NewObject();
            obj.Set("id", singer.Id ?? string.Empty);
            obj.Set("name", singer.Name ?? string.Empty);
            obj.Set("found", singer.Found);
            obj.Set("tryGetOto", sb.Function("tryGetOto", args => {
                return singer.TryGetOto(TypeConverter.ToString(args.At(0)), out var oto) ? OtoToJs(sb, oto) : JsValue.Null;
            }));
            obj.Set("tryGetMappedOto", sb.Function("tryGetMappedOto", args => {
                var alias = TypeConverter.ToString(args.At(0));
                var tone = (int)TypeConverter.ToNumber(args.At(1));
                var color = IsNullish(args.At(2)) ? string.Empty : TypeConverter.ToString(args.At(2));
                return singer.TryGetMappedOto(alias, tone, color, out var oto) ? OtoToJs(sb, oto) : JsValue.Null;
            }));
            return obj;
        }

        static JsValue OtoToJs(JsSandbox sb, UOto oto) {
            var obj = sb.NewObject();
            obj.Set("alias", oto.Alias);
            obj.Set("phonetic", oto.Phonetic);
            obj.Set("set", oto.Set);
            obj.Set("prefix", oto.Prefix);
            obj.Set("suffix", oto.Suffix);
            obj.Set("colors", sb.NewArray((oto.Subbanks ?? Array.Empty<USubbank>()).Select(s => s.Color == null ? JsValue.Null : s.Color)));
            return obj;
        }

        static JsValue NotesToJs(JsSandbox sb, Note[] notes) {
            return sb.NewArray(notes.Select(note => NoteToJs(sb, note)));
        }

        static JsValue NoteToJs(JsSandbox sb, Note? note) {
            if (note == null) {
                return JsValue.Null;
            }
            var n = note.Value;
            var obj = sb.NewObject();
            obj.Set("lyric", n.lyric);
            obj.Set("phoneticHint", n.phoneticHint == null ? JsValue.Null : n.phoneticHint);
            obj.Set("tone", n.tone);
            obj.Set("position", n.position);
            obj.Set("duration", n.duration);
            obj.Set("phonemeAttributes", n.phonemeAttributes == null
                ? JsValue.Null
                : sb.NewArray(n.phonemeAttributes.Select(attr => {
                    var a = sb.NewObject();
                    a.Set("index", attr.index);
                    a.Set("consonantStretchRatio", attr.consonantStretchRatio is double ratio ? ratio : JsValue.Null);
                    a.Set("toneShift", attr.toneShift is int shift ? shift : JsValue.Null);
                    a.Set("alternate", attr.alternate is int alt ? alt : JsValue.Null);
                    a.Set("voiceColor", attr.voiceColor == null ? JsValue.Null : attr.voiceColor);
                    return (JsValue)a;
                })));
            return obj;
        }

        Result ParseResult(JsValue result) {
            if (!result.IsObject() || !result.AsObject().Get("phonemes").IsArray()) {
                throw new JsSandboxException($"{Id}: process must return {{ phonemes: [...] }}.");
            }
            var array = result.AsObject().Get("phonemes").AsArray();
            var phonemes = new Phoneme[array.Length];
            for (uint i = 0; i < array.Length; i++) {
                if (!array[i].IsObject()) {
                    throw new JsSandboxException($"{Id}: phonemes[{i}] is not an object.");
                }
                var item = array[i].AsObject();
                var phoneme = item.Get("phoneme");
                if (!phoneme.IsString()) {
                    throw new JsSandboxException($"{Id}: phonemes[{i}].phoneme is not a string.");
                }
                phonemes[i].phoneme = phoneme.AsString();
                phonemes[i].position = GetInt(item, "position", i) ?? 0;
                phonemes[i].index = GetInt(item, "index", i);
                var expressions = item.Get("expressions");
                if (!IsNullish(expressions)) {
                    if (!expressions.IsArray()) {
                        throw new JsSandboxException($"{Id}: phonemes[{i}].expressions is not an array.");
                    }
                    phonemes[i].expressions = expressions.AsArray().Select(e => {
                        var abbr = e.IsObject() ? e.AsObject().Get("abbr") : JsValue.Undefined;
                        var value = e.IsObject() ? e.AsObject().Get("value") : JsValue.Undefined;
                        if (!abbr.IsString() || !value.IsNumber()) {
                            throw new JsSandboxException($"{Id}: phonemes[{i}].expressions must be [{{ abbr: string, value: number }}].");
                        }
                        return new PhonemeExpression { abbr = abbr.AsString(), value = (float)value.AsNumber() };
                    }).ToList();
                }
            }
            return new Result { phonemes = phonemes };
        }

        int? GetInt(ObjectInstance item, string key, uint i) {
            var value = item.Get(key);
            if (IsNullish(value)) {
                return null;
            }
            if (!value.IsNumber() || !double.IsFinite(value.AsNumber())) {
                throw new JsSandboxException($"{Id}: phonemes[{i}].{key} is not a number.");
            }
            return (int)Math.Round(value.AsNumber());
        }
    }
}
