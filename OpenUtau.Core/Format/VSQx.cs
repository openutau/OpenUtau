using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Collections.Generic;
using Serilog;

using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Format {
    public static class VSQx {
        public const string vsq3NameSpace = @"http://www.yamaha.co.jp/vocaloid/schema/vsq3/";
        public const string vsq4NameSpace = @"http://www.yamaha.co.jp/vocaloid/schema/vsq4/";

        public static UProject Load(string file) {
            XmlDocument vsqx = new XmlDocument();
            vsqx.Load(file);

            XmlNamespaceManager nsmanager = new XmlNamespaceManager(vsqx.NameTable);
            nsmanager.AddNamespace("v3", vsq3NameSpace);
            nsmanager.AddNamespace("v4", vsq4NameSpace);

            XmlNode root;
            string nsPrefix;

            // Detect vsqx version
            if ((root = vsqx.SelectSingleNode("v3:vsq3", nsmanager)) != null) {
                nsPrefix = "v3:";
            } else if ((root = vsqx.SelectSingleNode("v4:vsq4", nsmanager)) != null) {
                nsPrefix = "v4:";
            } else {
                throw new FileFormatException("Unrecognizable VSQx file format.");
            }

            UProject uproject = new UProject();
            Ustx.AddDefaultExpressions(uproject);
            uproject.RegisterExpression(new UExpressionDescriptor("opening", "ope", 0, 100, 100));

            string bpmPath = $"{nsPrefix}masterTrack/{nsPrefix}tempo";
            string timeSigPath = $"{nsPrefix}masterTrack/{nsPrefix}timeSig";
            string premeasurePath = $"{nsPrefix}masterTrack/{nsPrefix}preMeasure";
            string resolutionPath = $"{nsPrefix}masterTrack/{nsPrefix}resolution";
            string projectnamePath = $"{nsPrefix}masterTrack/{nsPrefix}seqName";
            string projectcommentPath = $"{nsPrefix}masterTrack/{nsPrefix}comment";
            string trackPath = $"{nsPrefix}vsTrack";
            string tracknamePath = $"{nsPrefix}{(nsPrefix == "v3:" ? "trackName" : "name")}";
            string trackcommentPath = $"{nsPrefix}comment";
            string tracknoPath = $"{nsPrefix}{(nsPrefix == "v3:" ? "vsTrackNo" : "tNo")}";
            string partPath = $"{nsPrefix}{(nsPrefix == "v3:" ? "musicalPart" : "vsPart")}";
            string partnamePath = $"{nsPrefix}{(nsPrefix == "v3:" ? "partName" : "name")}";
            string partcommentPath = $"{nsPrefix}comment";
            string notePath = $"{nsPrefix}note";
            string postickPath = $"{nsPrefix}{(nsPrefix == "v3:" ? "posTick" : "t")}";
            string durtickPath = $"{nsPrefix}{(nsPrefix == "v3:" ? "durTick" : "dur")}";
            string notenumPath = $"{nsPrefix}{(nsPrefix == "v3:" ? "noteNum" : "n")}";
            string velocityPath = $"{nsPrefix}{(nsPrefix == "v3:" ? "velocity" : "v")}";
            string lyricPath = $"{nsPrefix}{(nsPrefix == "v3:" ? "lyric" : "y")}";
            string phonemePath = $"{nsPrefix}{(nsPrefix == "v3:" ? "phnms" : "p")}";
            string playtimePath = $"{nsPrefix}playTime";
            string partstyleattrPath = $"{nsPrefix}{(nsPrefix == "v3:" ? "partStyle" : "pStyle")}/{nsPrefix}{(nsPrefix == "v3:" ? "attr" : "v")}";
            string notestyleattrPath = $"{nsPrefix}{(nsPrefix == "v3:" ? "noteStyle" : "nStyle")}/{nsPrefix}{(nsPrefix == "v3:" ? "attr" : "v")}";

            uproject.timeSignatures.Clear();
            foreach (XmlNode node in root.SelectNodes(timeSigPath, nsmanager)) {
                uproject.timeSignatures.Add(new UTimeSignature {
                    barPosition = Convert.ToInt32(node[nsPrefix == "v3:" ? "posMes" : "m"].InnerText),
                    beatPerBar = Convert.ToInt32(node[nsPrefix == "v3:" ? "nume" : "nu"].InnerText),
                    beatUnit = Convert.ToInt32(node[nsPrefix == "v3:" ? "denomi" : "de"].InnerText),
                });
            }
            uproject.timeSignatures.Sort((lhs, rhs) => lhs.barPosition.CompareTo(rhs.barPosition));
            uproject.timeSignatures[0].barPosition = 0;

            uproject.tempos.Clear();
            foreach (XmlNode node in root.SelectNodes(bpmPath, nsmanager)) {
                uproject.tempos.Add(new UTempo {
                    position = Convert.ToInt32(node[nsPrefix == "v3:" ? "posTick" : "t"].InnerText),
                    bpm = Convert.ToDouble(node[nsPrefix == "v3:" ? "bpm" : "v"].InnerText) / 100,
                });
            }
            uproject.tempos.Sort((lhs, rhs) => lhs.position.CompareTo(rhs.position));
            uproject.tempos[0].position = 0;

            int resolution = int.Parse(root.SelectSingleNode(resolutionPath, nsmanager).InnerText);
            if (resolution != uproject.resolution) {
                Log.Error($"Unexpected resolution {resolution}");
            }
            uproject.FilePath = file;
            uproject.name = root.SelectSingleNode(projectnamePath, nsmanager).InnerText;
            uproject.comment = root.SelectSingleNode(projectcommentPath, nsmanager).InnerText;

            int preMeasure = int.Parse(root.SelectSingleNode(premeasurePath, nsmanager).InnerText);
            int partPosTickShift = -preMeasure * uproject.resolution * uproject.timeSignatures[0].beatPerBar * 4 / uproject.timeSignatures[0].beatUnit;

            USinger usinger = USinger.CreateMissing("");

            foreach (XmlNode track in root.SelectNodes(trackPath, nsmanager)) // track
            {
                UTrack utrack = new UTrack(uproject) { Singer = usinger, TrackNo = uproject.tracks.Count };
                uproject.tracks.Add(utrack);

                //utrack.Name = track.SelectSingleNode(tracknamePath, nsmanager).InnerText;
                //utrack.Comment = track.SelectSingleNode(trackcommentPath, nsmanager).InnerText;
                utrack.TrackNo = int.Parse(track.SelectSingleNode(tracknoPath, nsmanager).InnerText);

                foreach (XmlNode part in track.SelectNodes(partPath, nsmanager)) // musical part
                {
                    UVoicePart upart = new UVoicePart();
                    uproject.parts.Add(upart);

                    upart.name = part.SelectSingleNode(partnamePath, nsmanager).InnerText;
                    upart.comment = part.SelectSingleNode(partcommentPath, nsmanager).InnerText;
                    upart.position = int.Parse(part.SelectSingleNode(postickPath, nsmanager).InnerText) + partPosTickShift;
                    upart.Duration = int.Parse(part.SelectSingleNode(playtimePath, nsmanager).InnerText);
                    upart.trackNo = utrack.TrackNo;

                    var pitList = new List<Tuple<int, int>>();
                    var pbsList = new List<Tuple<int, int>>();
                    int? lastT = null;
                    int? lastV = null;
                    foreach (XmlNode ctrlPt in part.SelectNodes($"{nsPrefix}{(nsPrefix == "v3:" ? "mCtrl" : "cc")}", nsmanager)) {
                        var t = int.Parse(ctrlPt.SelectSingleNode($"{nsPrefix}{(nsPrefix == "v3:" ? "posTick" : "t")}", nsmanager).InnerText);
                        var valNode = ctrlPt.SelectSingleNode($"{nsPrefix}{(nsPrefix == "v3:" ? "attr" : "v")}", nsmanager);
                        // type of controller
                        // D: DYN, [0,128), default: 64
                        // S: PBS, [0,24], default: 2.
                        // P: PIT, [-8192,8192), default: 0
                        // Pitch curve is calculated by multiplying PIT with PBS, max/min PIT shifts pitch by {PBS} semitones.
                        var type = valNode.Attributes["id"].Value;
                        var v = int.Parse(valNode.InnerText);
                        if (type == "DYN" || type == "D") {
                            v -= 64;
                            v = (int)(v < 0 ? v / 64.0 * 240 : v / 63.0 * 120);
                            var curve = GetCurve(uproject, upart, Ustx.DYN);
                            curve.Set(t, v, lastT ?? t, lastV ?? 0);
                            lastT = t;
                            lastV = v;
                        } else if (type == "PBS" || type == "S") {
                            pbsList.Add(new Tuple<int, int>(t, v));
                        } else if (type == "PIT" || type == "P") {
                            pitList.Add(new Tuple<int, int>(t, v));
                        }
                    }
                    if (lastV.HasValue) {
                        GetCurve(uproject, upart, Ustx.DYN).Set(upart.Duration, lastV ?? 0, lastT ?? 0, 0);
                    }

                    // Make sure that points are ordered by time
                    const int pbsDefaultVal = 2;
                    pbsList.Sort((tuple1, tuple2) => tuple1.Item1.CompareTo(tuple2.Item1));
                    pitList.Sort((tuple1, tuple2) => tuple1.Item1.CompareTo(tuple2.Item1));

                    lastT = null;
                    lastV = null;
                    foreach (var pt in pitList) {
                        var t = pt.Item1;
                        var v = pt.Item2 < 0 ? pt.Item2 / 8192f : pt.Item2 / 8191f;
                        var semitone = pbsList.FindLast(tuple => tuple.Item1 <= t)?.Item2 ?? pbsDefaultVal;
                        var pit = (int)Math.Round(v * semitone * 100);
                        if (Math.Abs(pit) > 1200) {
                            // Exceed OpenUTAU's limit. clip value
                            pit = Math.Sign(pit) * 1200;
                        }
                        if (t > 0 && lastV.HasValue) {
                            // Mimic Vsqx's Hold property
                            GetCurve(uproject, upart, Ustx.PITD).Set(t - UCurve.interval, lastV.Value, lastT ?? t, 0);
                            GetCurve(uproject, upart, Ustx.PITD).Set(t, pit, t - UCurve.interval, 0);
                        } else {
                            GetCurve(uproject, upart, Ustx.PITD).Set(t, pit, lastT ?? t, 0);
                        }
                        lastT = t;
                        lastV = pit;
                    }
                    if (lastV.HasValue) {
                        GetCurve(uproject, upart, Ustx.PITD).Set(upart.Duration, lastV ?? 0, lastT ?? 0, 0);
                    }

                    foreach (XmlNode note in part.SelectNodes(notePath, nsmanager)) {
                        UNote unote = uproject.CreateNote();

                        unote.position = int.Parse(note.SelectSingleNode(postickPath, nsmanager).InnerText);
                        unote.duration = int.Parse(note.SelectSingleNode(durtickPath, nsmanager).InnerText);
                        unote.tone = int.Parse(note.SelectSingleNode(notenumPath, nsmanager).InnerText);
                        unote.lyric = note.SelectSingleNode(lyricPath, nsmanager).InnerText;
                        if (unote.lyric == "-") {
                            unote.lyric = "+~";
                        }

                        unote.phonemeExpressions.Add(new UExpression(Ustx.VEL) {
                            index = 0,
                            value = int.Parse(note.SelectSingleNode(velocityPath, nsmanager).InnerText) * 100 / 64,
                        });
                        foreach (XmlNode notestyle in note.SelectNodes(notestyleattrPath, nsmanager)) {
                            if (notestyle.Attributes["id"].Value == "accent") {
                                unote.phonemeExpressions.Add(new UExpression(Ustx.ATK) {
                                    index = 0,
                                    value = int.Parse(notestyle.InnerText) * 2,
                                });
                            } else if (notestyle.Attributes["id"].Value == "decay") {
                                unote.phonemeExpressions.Add(new UExpression(Ustx.DEC) {
                                    index = 0,
                                    // V4 default is 50. Translate it to no effect in OU. V4 dec 100 roughly maps to OU 50.
                                    value = Math.Max(0, int.Parse(notestyle.InnerText) - 50),
                                });
                            }
                        }

                        int start = Util.NotePresets.Default.DefaultPortamento.PortamentoStart;
                        int length = Util.NotePresets.Default.DefaultPortamento.PortamentoLength;
                        unote.pitch.data[0].X = start;
                        unote.pitch.data[1].X = start + length;
                        upart.notes.Add(unote);
                    }
                }
            }

            uproject.AfterLoad();
            uproject.ValidateFull();
            return uproject;
        }

        private static UCurve GetCurve(UProject uproject, UVoicePart upart, string abbr) {
            var curve = upart.curves.Find(c => c.abbr == abbr);
            if (curve == null) {
                if (uproject.expressions.TryGetValue(abbr, out var desc)) {
                    curve = new UCurve(desc);
                    upart.curves.Add(curve);
                }
            }
            return curve;
        }
    }
}
