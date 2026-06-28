using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Format {
    public class Ustx {
        public static readonly Version kUstxVersion = new Version(0, 9);

        public const string DYN = "dyn";
        public const string PITD = "pitd";
        public const string CLR = "clr";
        public const string ENG = "eng";
        public const string VEL = "vel";
        public const string VOL = "vol";
        public const string ATK = "atk";
        public const string DEC = "dec";
        public const string GEN = "gen";
        public const string GENC = "genc";
        public const string BRE = "bre";
        public const string BREC = "brec";
        public const string LPF = "lpf";
        public const string NORM = "norm";
        public const string MOD = "mod";
        public const string MODP = "mod+";
        public const string ALT = "alt";
        public const string DIR = "dir";
        public const string SHFT = "shft";
        public const string SHFC = "shfc";
        public const string TENC = "tenc";
        public const string VOIC = "voic";

        public static readonly string[] required = { DYN, PITD, CLR, ENG, VEL, VOL, ATK, DEC };

        public static void AddDefaultExpressions(UProject project) {
            project.RegisterExpression(new UExpressionDescriptor("dynamics (curve)", DYN, -240, 120, 0) { type = UExpressionType.Curve });
            project.RegisterExpression(new UExpressionDescriptor("pitch deviation (curve)", PITD, -1200, 1200, 0) { type = UExpressionType.Curve });
            project.RegisterExpression(new UExpressionDescriptor("voice color", CLR, false, new string[0]));
            project.RegisterExpression(new UExpressionDescriptor("resampler engine", ENG, false, new string[] { "", WorldlineResampler.name }));
            project.RegisterExpression(new UExpressionDescriptor("velocity", VEL, 0, 200, 100));
            project.RegisterExpression(new UExpressionDescriptor("volume", VOL, 0, 200, 100));
            project.RegisterExpression(new UExpressionDescriptor("attack", ATK, 0, 200, 100));
            project.RegisterExpression(new UExpressionDescriptor("decay", DEC, 0, 100, 0));
            project.RegisterExpression(new UExpressionDescriptor("gender", GEN, -100, 100, 0, "g"));
            project.RegisterExpression(new UExpressionDescriptor("gender (curve)", GENC, -100, 100, 0) { type = UExpressionType.Curve });
            project.RegisterExpression(new UExpressionDescriptor("breath", BRE, 0, 100, 0, "B"));
            project.RegisterExpression(new UExpressionDescriptor("breathiness (curve)", BREC, -100, 100, 0) { type = UExpressionType.Curve });
            project.RegisterExpression(new UExpressionDescriptor("lowpass", LPF, 0, 100, 0, "H"));
            project.RegisterExpression(new UExpressionDescriptor("normalize", NORM, 0, 100, 86, "P"));
            project.RegisterExpression(new UExpressionDescriptor("modulation", MOD, 0, 100, 0));
            project.RegisterExpression(new UExpressionDescriptor("modulation plus", MODP, 0, 100, 0));
            project.RegisterExpression(new UExpressionDescriptor("alternate", ALT, 0, 16, 0));
            project.RegisterExpression(new UExpressionDescriptor("direct", DIR, false, new string[] { "off", "on" }));
            project.RegisterExpression(new UExpressionDescriptor("tone shift", SHFT, -36, 36, 0));
            project.RegisterExpression(new UExpressionDescriptor("tone shift (curve)", SHFC, -1200, 1200, 0) { type = UExpressionType.Curve });
            project.RegisterExpression(new UExpressionDescriptor("tension (curve)", TENC, -100, 100, 0) { type = UExpressionType.Curve });
            project.RegisterExpression(new UExpressionDescriptor("voicing (curve)", VOIC, 0, 100, 100) { type = UExpressionType.Curve });

            string message = string.Empty;
            if (ValidateExpression(project, "g", GEN)) {
                message += $"\ng flag -> gender";
            }
            if (ValidateExpression(project, "B", BRE)) {
                message += $"\nB flag -> {BRE}";
            }
            if (ValidateExpression(project, "H", LPF)) {
                message += $"\nH flag-> {LPF}";
            }
            if (ValidateExpression(project, "P", NORM)) {
                message += $"\nP flag-> normalize";
            }
            if (message != string.Empty) {
                var e = new MessageCustomizableException("Expressions have been merged due to duplicate flags", $"<translate:errors.expression.marge>:{message}", new Exception(), false);
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }
        private static bool ValidateExpression(UProject project, string flag, string abbr) {
            if (project.expressions.Any(e => e.Value.flag == flag && e.Value.abbr != abbr)) {
                var oldExp = project.expressions.First(e => e.Value.flag == flag && e.Value.abbr != abbr);
                project.MargeExpression(oldExp.Value.abbr, abbr);
                return true;
            }
            return false;
        }

        public static UProject Create() {
            UProject project = new UProject() { Saved = false };
            AddDefaultExpressions(project);
            return project;
        }

        public static void Save(string filePath, UProject project) {
            try {
                project.ustxVersion = kUstxVersion;
                project.FilePath = filePath;
                project.BeforeSave();
                File.WriteAllText(filePath, Yaml.DefaultSerializer.Serialize(project), Encoding.UTF8);
                project.Saved = true;
                project.AfterSave();
                Preferences.Default.RecoveryPath = string.Empty;
                Preferences.Save();
                DocManager.Inst.Recovered = false;
            } catch (Exception ex) {
                var e = new MessageCustomizableException($"Failed to save ustx: {filePath}", $"<translate:errors.failed.save>: {filePath}", ex);
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        public static void AutoSave(string filePath, UProject project) {
            try {
                project.ustxVersion = kUstxVersion;
                project.BeforeSave();
                File.WriteAllText(filePath, Yaml.DefaultSerializer.Serialize(project), Encoding.UTF8);
                project.AfterSave();
                Preferences.Default.RecoveryPath = filePath;
                Preferences.Save();
            } catch (Exception ex) {
                Log.Error(ex, $"Failed to autosave: {filePath}");
            }
        }

        public static UProject Load(string filePath) {
            string text = File.ReadAllText(filePath, Encoding.UTF8);
            UProject project = Yaml.DefaultDeserializer.Deserialize<UProject>(text);
            AddDefaultExpressions(project);
            project.FilePath = filePath;
            project.Saved = true;
            project.AfterLoad();
            project.ValidateFull();
            if (project.ustxVersion > kUstxVersion) {
                throw new MessageCustomizableException($"Project file is newer than software: {filePath}", $"<translate:errors.failed.opennewerproject>:\n{filePath}", new FileFormatException("Project file is newer than software."));
            }
            if (project.ustxVersion < kUstxVersion) {
                Log.Information($"Upgrading project from {project.ustxVersion} to {kUstxVersion}");
            }
            if (project.ustxVersion < new Version(0, 4)) {
                if (project.expressions.TryGetValue("acc", out var exp) && exp.name == "accent") {
                    project.expressions.Remove("acc");
                    exp.abbr = ATK;
                    exp.name = "attack";
                    project.expressions[ATK] = exp;
                    project.parts
                        .Where(part => part is UVoicePart)
                        .Select(part => part as UVoicePart)
                        .SelectMany(part => part.notes)
                        .SelectMany(note => note.phonemeExpressions)
                        .Where(exp => exp.abbr == "acc")
                        .ToList()
                        .ForEach(exp => exp.abbr = ATK);
                    project.ValidateFull();
                }
            }
            if (project.ustxVersion < new Version(0, 5)) {
                project.parts
                    .Where(part => part is UVoicePart)
                    .Select(part => part as UVoicePart)
                    .SelectMany(part => part.notes)
                    .Where(note => note.lyric.StartsWith("..."))
                    .ToList()
                    .ForEach(note => note.lyric = note.lyric.Replace("...", "+"));
                project.ValidateFull();
            }
            if (project.ustxVersion < new Version(0, 6)) {
#pragma warning disable CS0612 // Type or member is obsolete
                project.timeSignatures = new List<UTimeSignature> { new UTimeSignature(0, project.beatPerBar, project.beatUnit) };
                project.tempos = new List<UTempo> { new UTempo(0, project.bpm) };
#pragma warning restore CS0612 // Type or member is obsolete
                project.ValidateFull();
            }
            if (project.ustxVersion < new Version(0, 7)) {
                var expSelectors = new UProject().expSelectors;
                if (project.expSelectors.Length < expSelectors.Length) {
                    for (int i = 0; i < project.expSelectors.Length; i++) {
                        expSelectors[i] = project.expSelectors[i];
                    }
                    project.expSelectors = expSelectors;
                }
            }
            project.ustxVersion = kUstxVersion;
            return project;
        }
    }
}
