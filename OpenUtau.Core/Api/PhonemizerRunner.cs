using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Api {
    internal class PhonemizerRequest {
        public USinger singer;
        public UVoicePart part;
        public long timestamp;
        public int[] noteIndexes;
        public Phonemizer.Note[][] notes;
        public Phonemizer[] phonemizers; 
        public int[] notePhonemizerIndices; 
        public TimeAxis timeAxis;
    }

    internal class PhonemizerResponse {
        public UVoicePart part;
        public long timestamp;
        public int[] noteIndexes;
        public Phonemizer.Phoneme[][] phonemes;
    }

    internal class PhonemizerRunner : IDisposable {
        private readonly TaskScheduler mainScheduler;
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private readonly BlockingCollection<PhonemizerRequest> requests = new BlockingCollection<PhonemizerRequest>();
        private readonly object busyLock = new object();
        private Thread thread;

        public PhonemizerRunner(TaskScheduler mainScheduler) {
            this.mainScheduler = mainScheduler;
            thread = new Thread(PhonemizerLoop) {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
            };
            thread.Start();
        }

        public void Push(PhonemizerRequest request) {
            requests.Add(request);
        }

        void PhonemizerLoop() {
            var parts = new HashSet<UVoicePart>();
            var toRun = new List<PhonemizerRequest>();
            while (!shutdown.IsCancellationRequested) {
                lock (busyLock) {
                    while (requests.TryTake(out var request)) {
                        toRun.Add(request);
                    }
                    foreach (var request in toRun) {
                        parts.Add(request.part);
                    }
                    for (int i = toRun.Count - 1; i >= 0; i--) {
                        if (parts.Remove(toRun[i].part)) {
                            SendResponse(Phonemize(toRun[i]));
                        }
                    }
                    parts.Clear();
                    toRun.Clear();
                    try {
                        toRun.Add(requests.Take(shutdown.Token));
                    } catch (OperationCanceledException) { }
                }
            }
        }

        void SendResponse(PhonemizerResponse response) {
            Task.Factory.StartNew(_ => {
                if (DocManager.Inst.Project.parts.Contains(response.part)) {
                    response.part.SetPhonemizerResponse(response);
                }
                DocManager.Inst.Project.Validate(new ValidateOptions {
                    SkipTiming = true,
                    Part = response.part,
                    SkipPhonemizer = true,
                });
                DocManager.Inst.ExecuteCmd(new PhonemizedNotification());
            }, null, CancellationToken.None, TaskCreationOptions.None, mainScheduler);
        }

        static PhonemizerResponse Phonemize(PhonemizerRequest request) {
            var notes = request.notes;
            var phonemizers = request.phonemizers;

            if (request.singer == null || phonemizers == null || phonemizers.Length == 0) {
                return new PhonemizerResponse() {
                    noteIndexes = request.noteIndexes,
                    part = request.part,
                    phonemes = new Phonemizer.Phoneme[][] { },
                    timestamp = request.timestamp,
                };
            }
            foreach (var p in phonemizers) {
                p.SetUpException = null;
                try {
                    p.SetSinger(request.singer);
                } catch (Exception e) {
                    Log.Error(e, $"phonemizer failed to set singer.");
                    p.SetUpException = e;
                }
                p.SetTiming(request.timeAxis);
                if (p.SetUpException == null) {
                    try {
                        p.SetUp(notes, DocManager.Inst.Project, DocManager.Inst.Project.tracks[request.part.trackNo]);
                    } catch (Exception e) {
                        Log.Error(e, $"phonemizer failed to setup.");
                        p.SetUpException = e;
                    }
                }
            }
            var result = new List<Phonemizer.Phoneme[]>();
            for (int i = notes.Length - 1; i >= 0; i--) {
                var phonemizer = phonemizers[request.notePhonemizerIndices[i]];
                if (phonemizer.SetUpException != null) {
                    // Short-circuit: return an error phoneme for this note group
                    result.Insert(0, new Phonemizer.Phoneme[] {
                        new Phonemizer.Phoneme {
                            phoneme = "error",
                            position = notes[i][0].position,
                            error = phonemizer.SetUpException
                        }
                    });
                    continue; // Skip processing and go to the next note
                }
                Phonemizer.Result phonemizerResult;
                bool prevIsNeighbour = false;
                bool nextIsNeighbour = false;
                Phonemizer.Note[] prevs = null;
                Phonemizer.Note? prev = null;
                Phonemizer.Note? next = null;
                if (i > 0) {
                    prevs = notes[i - 1];
                    prev = notes[i - 1][0];
                    var prevLast = notes[i - 1].Last();
                    prevIsNeighbour = prevLast.position + prevLast.duration >= notes[i][0].position;
                }
                if (i < notes.Length - 1) {
                    next = notes[i + 1][0];
                    var thisLast = notes[i].Last();
                    nextIsNeighbour = thisLast.position + thisLast.duration >= next.Value.position;
                }
                if (next != null && result.Count > 0 && result[0].Length > 0) {
                    var end = notes[i].Last().position + notes[i].Last().duration;
                    int endPushback = Math.Min(0, result[0][0].position - end);
                    notes[i][notes[i].Length - 1].duration += endPushback;
                }
                try {
                    phonemizerResult = phonemizer.Process(
                        notes[i],
                        prev,
                        next,
                        prevIsNeighbour ? prev : null,
                        nextIsNeighbour ? next : null,
                        (prevIsNeighbour ? prevs : null) ?? new Phonemizer.Note[0]);
                } catch (Exception e) {
                    Log.Error(e, $"phonemizer error {notes[i][0].lyric}");
                    phonemizerResult = new Phonemizer.Result() {
                        phonemes = new Phonemizer.Phoneme[] {
                            new Phonemizer.Phoneme {
                                phoneme = "error",
                                error = e
                            }
                        }
                    };
                }
                if (phonemizer.LegacyMapping) {
                    for (var k = 0; k < phonemizerResult.phonemes.Length; k++) {
                        var phoneme = phonemizerResult.phonemes[k];
                        if (request.singer.TryGetMappedOto(phoneme.phoneme, notes[i][0].tone, out var oto)) {
                            phonemizerResult.phonemes[k].phoneme = oto.Alias;
                        }
                    }
                }
                for (var j = 0; j < phonemizerResult.phonemes.Length; j++) {
                    phonemizerResult.phonemes[j].position += notes[i][0].position;
                }
                result.Insert(0, phonemizerResult.phonemes);
            } 
            foreach (var p in phonemizers) {
                try {
                    p.CleanUp();
                } catch (Exception e) {
                    Log.Error(e, $"phonemizer failed to cleanup.");
                }
            }

            return new PhonemizerResponse() {
                noteIndexes = request.noteIndexes,
                part = request.part,
                phonemes = result.ToArray(),
                timestamp = request.timestamp,
            };
        }

        /// <summary>
        /// Wait already queued phonemizer requests to finish.
        /// Should only be used in command line mode.
        /// </summary>
        public void WaitFinish() {
            while (true) {
                lock (busyLock) {
                    if (requests.Count == 0) {
                        return;
                    }
                }
            }
        }

        public void Dispose() {
            if (shutdown.IsCancellationRequested) {
                return;
            }
            shutdown.Cancel();
            if (thread != null) {
                while (thread.IsAlive) {
                    Thread.Sleep(100);
                }
                thread = null;
            }
            requests.Dispose();
        }
    }
}
