using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Classic;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Headless {
    public sealed class HeadlessOpenUtauHost : ICmdSubscriber, IDisposable {
        private readonly HeadlessTaskScheduler scheduler;
        private readonly SynchronizationContext? previousSynchronizationContext;
        private readonly TextWriter? output;
        private readonly List<string> errors = new List<string>();
        private string lastProgressInfo = string.Empty;
        private bool disposed;

        public HeadlessOpenUtauHost(
            HeadlessOpenUtauOptions? options = null,
            TextWriter? output = null) {
            this.output = output;
            scheduler = new HeadlessTaskScheduler(Thread.CurrentThread);
            previousSynchronizationContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new HeadlessSynchronizationContext(scheduler));

            if (!string.IsNullOrWhiteSpace(options?.SingersPath)) {
                Preferences.Default.AdditionalSingerPath = Path.GetFullPath(options.SingersPath);
            }
            ApplyPreferenceOverrides(options);

            Log.Information("Initializing OpenUtau headless host.");
            ToolsManager.Inst.Initialize();
            SingerManager.Inst.Initialize();
            DocManager.Inst.Initialize(Thread.CurrentThread, scheduler);
            DocManager.Inst.PostOnUIThread = scheduler.Post;
            DocManager.Inst.AddSubscriber(this);
            Log.Information("Initialized OpenUtau headless host.");
        }

        private static void ApplyPreferenceOverrides(HeadlessOpenUtauOptions? options) {
            if (options == null) {
                return;
            }
            if (!string.IsNullOrWhiteSpace(options.OnnxRunner)) {
                Preferences.Default.OnnxRunner = options.OnnxRunner;
            }
            if (options.OnnxGpu.HasValue) {
                Preferences.Default.OnnxGpu = options.OnnxGpu.Value;
            }
            if (options.DiffSingerDepth.HasValue) {
                Preferences.Default.DiffSingerDepth = options.DiffSingerDepth.Value;
            }
            if (options.DiffSingerSteps.HasValue) {
                Preferences.Default.DiffSingerSteps = options.DiffSingerSteps.Value;
            }
            if (options.DiffSingerVarianceSteps.HasValue) {
                Preferences.Default.DiffSingerStepsVariance = options.DiffSingerVarianceSteps.Value;
            }
            if (options.DiffSingerPitchSteps.HasValue) {
                Preferences.Default.DiffSingerStepsPitch = options.DiffSingerPitchSteps.Value;
            }
            if (options.DiffSingerTensorCache.HasValue) {
                Preferences.Default.DiffSingerTensorCache = options.DiffSingerTensorCache.Value;
            }
        }

        public T Run<T>(Func<Task<T>> operation) {
            if (!scheduler.IsOwnerThread) {
                throw new InvalidOperationException("Headless host must be run on its owner thread.");
            }
            Task<T> task;
            try {
                task = operation();
            } catch (Exception e) {
                task = Task.FromException<T>(e);
            }
            while (!task.IsCompleted) {
                scheduler.RunOne(TimeSpan.FromMilliseconds(50));
            }
            scheduler.RunAvailable();
            return task.GetAwaiter().GetResult();
        }

        public void ClearErrors() {
            lock (errors) {
                errors.Clear();
            }
        }

        public string[] TakeErrors() {
            lock (errors) {
                var result = errors.ToArray();
                errors.Clear();
                return result;
            }
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is ErrorMessageNotification error) {
                lock (errors) {
                    errors.Add(FormatError(error));
                }
            } else if (cmd is ProgressBarNotification progress) {
                PublishProgress(progress);
            }
        }

        private void PublishProgress(ProgressBarNotification progress) {
            if (output == null ||
                progress.Progress != 0 ||
                string.IsNullOrWhiteSpace(progress.Info) ||
                progress.Info == lastProgressInfo) {
                return;
            }
            lastProgressInfo = progress.Info;
            output.WriteLine(progress.Info);
        }

        private static string FormatError(ErrorMessageNotification notification) {
            if (notification.e is MessageCustomizableException mce) {
                var message = string.IsNullOrWhiteSpace(mce.Message)
                    ? mce.SubstanceException.Message
                    : mce.Message;
                return string.IsNullOrWhiteSpace(mce.SubstanceException.Message)
                    ? message
                    : $"{message}: {mce.SubstanceException.Message}";
            }
            if (!string.IsNullOrWhiteSpace(notification.message)) {
                return notification.e == null
                    ? notification.message
                    : $"{notification.message}: {notification.e.Message}";
            }
            return notification.e?.Message ?? notification.ToString();
        }

        public void Dispose() {
            if (disposed) {
                return;
            }
            disposed = true;
            DocManager.Inst.RemoveSubscriber(this);
            DocManager.Inst.PhonemizerRunner?.Dispose();
            scheduler.RunAvailable();
            SynchronizationContext.SetSynchronizationContext(previousSynchronizationContext);
            scheduler.Dispose();
        }
    }
}
