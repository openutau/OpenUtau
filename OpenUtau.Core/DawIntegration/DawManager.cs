using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using K4os.Hash.xxHash;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.DawIntegration {
    public class DawManager : SingletonBase<DawManager>, ICmdSubscriber {
        private static readonly TimeSpan[] ReconnectDelays = {
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
        };

        private readonly object clientLock = new object();
        private readonly SemaphoreSlim updateSemaphore = new SemaphoreSlim(1, 1);
        private readonly Debounce sendLayoutDebounce = new Debounce();
        private readonly Debounce sendAudioDebounce = new Debounce();

        private DawClient? client;
        private CancellationTokenSource? reconnectCancellation;
        private volatile bool manualDisconnect;
        private int reconnecting;

        public DawClient? Client {
            get {
                lock (clientLock) {
                    return client;
                }
            }
        }

        public bool IsConnected => Client != null;

        private DawManager() {
            DocManager.Inst.AddSubscriber(this);
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (!IsConnected) {
                return;
            }
            if (cmd is UNotification && !(
                cmd is PartRenderedNotification ||
                cmd is VolumeChangeNotification ||
                cmd is PanChangeNotification)) {
                return;
            }

            sendLayoutDebounce.Do(TimeSpan.FromSeconds(1), () =>
                RunSerializedUpdate(async currentClient => {
                    await UpdateUstx(currentClient);
                    await UpdateTracks(currentClient);
                }));
            sendAudioDebounce.Do(TimeSpan.FromSeconds(5), () =>
                RunSerializedUpdate(UpdateAudio));
        }

        public async Task<string> Connect(
            DawServer server,
            CancellationToken cancellationToken = default) {
            await DisconnectInternal(sendFinalUpdate: false, notify: false);
            manualDisconnect = false;
            reconnectCancellation = new CancellationTokenSource();
            var (newClient, ustx) = await DawClient.Connect(server, cancellationToken);
            AttachClient(newClient);
            return ustx;
        }

        public Task Synchronize() {
            return ForceFullSync();
        }

        public Task Disconnect() {
            return DisconnectInternal(sendFinalUpdate: true, notify: true);
        }

        private async Task DisconnectInternal(bool sendFinalUpdate, bool notify) {
            manualDisconnect = true;
            reconnectCancellation?.Cancel();
            reconnectCancellation?.Dispose();
            reconnectCancellation = null;
            sendLayoutDebounce.Cancel();
            sendAudioDebounce.Cancel();

            DawClient? currentClient;
            lock (clientLock) {
                currentClient = client;
            }
            if (currentClient == null) {
                return;
            }

            if (sendFinalUpdate) {
                try {
                    await RunSerializedUpdate(async connectedClient => {
                        await UpdateUstx(connectedClient);
                        await UpdateTracks(connectedClient);
                        await UpdateAudio(connectedClient);
                    });
                } catch (Exception e) {
                    Log.Warning(e, "Failed to send final DAW update.");
                }
            }

            DetachClient(currentClient);
            currentClient.Disconnect();
            currentClient.Dispose();
            if (notify) {
                DocManager.Inst.ExecuteCmd(new DawDisconnectedNotification());
            }
        }

        private void AttachClient(DawClient newClient) {
            lock (clientLock) {
                client = newClient;
            }
            newClient.Disconnected += OnClientDisconnected;
            newClient.PlaybackStarted += OnPlaybackStarted;
        }

        private void DetachClient(DawClient disconnectedClient) {
            disconnectedClient.Disconnected -= OnClientDisconnected;
            disconnectedClient.PlaybackStarted -= OnPlaybackStarted;
            lock (clientLock) {
                if (ReferenceEquals(client, disconnectedClient)) {
                    client = null;
                }
            }
        }

        private void OnClientDisconnected(DawClient disconnectedClient, Exception? error) {
            DetachClient(disconnectedClient);
            if (manualDisconnect) {
                return;
            }

            var cancellation = reconnectCancellation;
            if (cancellation == null || cancellation.IsCancellationRequested) {
                return;
            }
            if (Interlocked.CompareExchange(ref reconnecting, 1, 0) != 0) {
                return;
            }
            _ = Reconnect(disconnectedClient.server, error, cancellation.Token);
        }

        private void OnPlaybackStarted() {
            _ = FlushPendingUpdates();
        }

        private async Task FlushPendingUpdates() {
            try {
                await sendLayoutDebounce.Flush();
                await sendAudioDebounce.Flush();
            } catch (Exception e) {
                Log.Warning(e, "Failed to flush pending DAW updates before playback.");
            }
        }

        private async Task Reconnect(
            DawServer server,
            Exception? disconnectError,
            CancellationToken cancellationToken) {
            try {
                Log.Warning(disconnectError, "DAW connection lost. Reconnecting...");
                DocManager.Inst.ExecuteCmd(new ProgressBarNotification(
                    0, "DAW connection lost. Reconnecting..."));

                Exception? lastError = disconnectError;
                foreach (var delay in ReconnectDelays) {
                    try {
                        await Task.Delay(delay, cancellationToken);
                        var (newClient, _) = await DawClient.Connect(server, cancellationToken);
                        if (cancellationToken.IsCancellationRequested || manualDisconnect) {
                            newClient.Dispose();
                            return;
                        }
                        AttachClient(newClient);
                        await ForceFullSync();
                        DocManager.Inst.ExecuteCmd(new DawConnectedNotification());
                        Log.Information("Reconnected to DAW.");
                        return;
                    } catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested) {
                        return;
                    } catch (Exception e) {
                        lastError = e;
                        Log.Warning(e, "Failed to reconnect to DAW.");
                    }
                }

                DocManager.Inst.ExecuteCmd(new DawDisconnectedNotification());
                if (lastError != null) {
                    Log.Error(lastError, "DAW reconnection attempts exhausted.");
                }
            } finally {
                Interlocked.Exchange(ref reconnecting, 0);
            }
        }

        private async Task ForceFullSync() {
            await RunSerializedUpdate(async currentClient => {
                await UpdateUstx(currentClient);
                await UpdateTracks(currentClient);
                await UpdateAudio(currentClient);
            });
        }

        private async Task RunSerializedUpdate(Func<DawClient, Task> update) {
            await updateSemaphore.WaitAsync();
            try {
                var currentClient = Client;
                if (currentClient != null) {
                    await update(currentClient);
                }
            } finally {
                updateSemaphore.Release();
            }
        }

        private async Task UpdateUstx(DawClient currentClient) {
            Log.Information("Updating ustx in DAW...");
            try {
                var ustx = Format.Ustx.FromProject(DocManager.Inst.Project);
                await currentClient.SendNotification(new UpdateUstxNotification(ustx));
                Log.Information("Sent ustx to DAW.");
            } catch (Exception e) {
                Log.Error(e, "Failed to send ustx to DAW.");
                throw;
            }
        }

        private async Task UpdateTracks(DawClient currentClient) {
            Log.Information("Updating tracks in DAW...");
            try {
                await currentClient.SendNotification(
                    new UpdateTracksNotification(
                        DocManager.Inst.Project.tracks.Select(track =>
                            new UpdateTracksNotification.Track(
                                track.TrackName,
                                track.Volume,
                                track.Pan)).ToList()));
                Log.Information("Sent tracks to DAW.");
            } catch (Exception e) {
                Log.Error(e, "Failed to send tracks to DAW.");
                throw;
            }
        }

        private async Task UpdateAudio(DawClient currentClient) {
            try {
                var readyParts = DocManager.Inst.Project.parts
                    .OfType<UVoicePart>()
                    .Where(part => part.Mix != null)
                    .ToList();

                Log.Information("Rendering prerenders for DAW...");
                var buffers = readyParts.Select(part => {
                    double startMs = DocManager.Inst.Project.timeAxis.TickPosToMsPos(part.position);
                    double endMs = DocManager.Inst.Project.timeAxis.TickPosToMsPos(part.position + part.duration);
                    int samplePos = (int)(startMs * 44100 / 1000) * 2;
                    int sampleCount = (int)((endMs - startMs) * 44100 / 1000) * 2;
                    var floatBuffer = new float[sampleCount];
                    part.Mix!.Mix(samplePos, floatBuffer, 0, sampleCount);
                    var byteBuffer = new byte[floatBuffer.Length * 4];
                    Buffer.BlockCopy(floatBuffer, 0, byteBuffer, 0, byteBuffer.Length);
                    return (
                        part,
                        startMs,
                        endMs,
                        byteBuffer,
                        hash: XXH32.DigestOf(byteBuffer));
                }).ToList();

                Log.Information("Sending part layout to DAW...");
                var missingAudios = await currentClient.SendRequest<UpdatePartLayoutResponse>(
                    new UpdatePartLayoutRequest(
                        buffers.Select(buffer => new UpdatePartLayoutRequest.Part(
                            buffer.part.trackNo,
                            buffer.startMs,
                            buffer.endMs,
                            buffer.hash)).ToList()));
                Log.Information("Sent part layout to DAW.");

                if (missingAudios.missingAudios.Count > 0) {
                    Log.Information(
                        "DAW requested {Count} missing audios.",
                        missingAudios.missingAudios.Count);
                    var buffersDict = buffers.ToDictionary(buffer => buffer.hash);
                    var audios = new Dictionary<uint, string>();
                    foreach (var audioHash in missingAudios.missingAudios) {
                        var buffer = buffersDict[audioHash];
                        audios[audioHash] =
                            Convert.ToBase64String(Gzip.Compress(buffer.byteBuffer));
                    }

                    await currentClient.SendNotification(
                        new UpdateAudioNotification(audios));
                    Log.Information("Sent missing audios to DAW.");
                } else {
                    Log.Information("Audios in DAW are up to date.");
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to send status to DAW.");
                throw;
            }
        }
    }
}
