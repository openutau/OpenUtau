using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using OpenUtau.Core.Util;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// Smooth mouse-wheel scrolling and zooming for a view such as the piano roll
    /// or the tracks view. A wheel step moves a target, and the view moves there
    /// along a <see cref="Motion"/> that ends at rest after <see cref="Motion.Duration"/>.
    /// A step that arrives mid-glide continues from the current position and speed,
    /// so spinning the wheel gives one continuous movement. Motion is timed by the
    /// clock from the input, not by counting frames, so irregular frame callbacks
    /// don't make it jerk. Precision touchpads send fractional deltas that are
    /// already smooth, so those apply at once (see <see cref="IsWheelStep"/>).
    /// The ReduceAnimations preference ("My computer is potato") turns the glide off.
    /// </summary>
    public class SmoothViewport {
        private readonly Control owner;
        private readonly List<ValueGlide> scrolls = new List<ValueGlide>();
        private readonly List<ValueGlide> values = new List<ValueGlide>();
        private readonly List<ZoomGlide> zooms = new List<ZoomGlide>();
        private bool running;

        public SmoothViewport(Control owner) {
            this.owner = owner;
        }

        /// <summary>
        /// Whether a wheel delta comes from a notched mouse wheel, which moves in
        /// whole steps, rather than a precision touchpad.
        /// </summary>
        public static bool IsWheelStep(double delta) {
            return delta != 0 && Math.Abs(delta - Math.Round(delta)) < 1e-6;
        }

        public ValueGlide Scroll(ScrollBar bar) {
            var glide = new ValueGlide(this, () => bar.Value, value => bar.Value = value, () => bar.Minimum, () => bar.Maximum);
            scrolls.Add(glide);
            return glide;
        }

        /// <summary>
        /// Glides another value the view owns, such as the track height. Unlike
        /// scrolling, a zoom does not stop it.
        /// </summary>
        public ValueGlide Value(Func<double> get, Action<double> set, Func<double> min, Func<double> max) {
            var glide = new ValueGlide(this, get, set, min, max);
            values.Add(glide);
            return glide;
        }

        /// <param name="zoom">
        /// The view model's zoom method. Its delta must scale the view by
        /// (1 + 2 * delta), as OnXZoomed and OnYZoomed of the view models do.
        /// </param>
        public ZoomGlide Zoom(Action<Point, double> zoom) {
            var glide = new ZoomGlide(this, zoom);
            zooms.Add(glide);
            return glide;
        }

        // A zoom moves the scroll offset around its anchor every frame, so the two
        // never glide at once: a new zoom stops scrolling, a new scroll ends zooming.
        internal void CancelScrolls() {
            foreach (var glide in scrolls) {
                glide.Cancel();
            }
        }

        internal void FinishZooms() {
            foreach (var glide in zooms) {
                glide.Finish();
            }
        }

        /// <returns>
        /// False when animations are reduced in the preferences, or the view
        /// is not in a window, so there are no frames to glide on.
        /// </returns>
        internal bool Start() {
            if (Preferences.Default.ReduceAnimations) {
                return false;
            }
            var topLevel = TopLevel.GetTopLevel(owner);
            if (topLevel == null) {
                return false;
            }
            if (!running) {
                running = true;
                topLevel.RequestAnimationFrame(OnFrame);
            }
            return true;
        }

        private void OnFrame(TimeSpan time) {
            long now = Stopwatch.GetTimestamp();
            bool moving = false;
            foreach (var glide in scrolls) {
                moving |= glide.Step(now);
            }
            foreach (var glide in values) {
                moving |= glide.Step(now);
            }
            foreach (var glide in zooms) {
                moving |= glide.Step(now);
            }
            var topLevel = TopLevel.GetTopLevel(owner);
            if (moving && topLevel != null) {
                topLevel.RequestAnimationFrame(OnFrame);
            } else {
                running = false;
                FinishZooms();
            }
        }
    }

    /// <summary>
    /// A movement from P0, starting at velocity V0, that comes to rest at
    /// P0 + Distance after <see cref="Duration"/>, slowing to a stop without a jolt
    /// (zero speed and zero deceleration at the end). It is the quartic
    /// p(s) = (3d - v)s^4 + (3v - 8d)s^3 + (6d - 3v)s^2 + vs over s = t / Duration,
    /// with d = Distance and v = V0 * Duration. Starting at V0 = 3 * Distance /
    /// Duration makes it an ease-out cubic; a lower V0 (a glide already under way)
    /// accelerates first, so the speed stays continuous.
    /// </summary>
    internal readonly struct Motion {
        public const double Duration = 0.18; // seconds

        private readonly double p0;
        private readonly double distance;
        private readonly double v0;
        private readonly long start;

        private Motion(double p0, double distance, double v0, long start) {
            this.p0 = p0;
            this.distance = distance;
            this.v0 = v0;
            this.start = start;
        }

        public double Target => p0 + distance;

        public static Motion EaseOut(double from, double target, long now) {
            return new Motion(from, target - from, 3 * (target - from) / Duration, now);
        }

        /// <summary>Heads for a new target from where this motion is now, at its current speed.</summary>
        public Motion Retarget(double target, long now) {
            double p = Position(now);
            double v = Velocity(now);
            double d = target - p;
            // Starting faster than this would overshoot the target.
            if (v * d > 0 && Math.Abs(v) > 4 * Math.Abs(d) / Duration) {
                v = 4 * d / Duration;
            }
            return new Motion(p, d, v, now);
        }

        public bool Done(long now) => Progress(now) >= 1;

        public double Position(long now) {
            double s = Progress(now);
            double d = distance, v = v0 * Duration;
            return p0 + (((3 * d - v) * s + (3 * v - 8 * d)) * s + (6 * d - 3 * v)) * s * s + v * s;
        }

        public double Velocity(long now) {
            double s = Progress(now);
            double d = distance, v = v0 * Duration;
            return (((4 * (3 * d - v) * s + 3 * (3 * v - 8 * d)) * s + 2 * (6 * d - 3 * v)) * s + v) / Duration;
        }

        private double Progress(long now) {
            return Math.Clamp(Stopwatch.GetElapsedTime(start, now).TotalSeconds / Duration, 0, 1);
        }
    }

    /// <summary>A scroll bar's value, or another value of the view, gliding to its target.</summary>
    public class ValueGlide {
        private readonly SmoothViewport viewport;
        private readonly Func<double> get;
        private readonly Action<double> set;
        private readonly Func<double> min;
        private readonly Func<double> max;
        private Motion motion;
        private double lastSet;
        private bool active;

        internal ValueGlide(SmoothViewport viewport, Func<double> get, Action<double> set, Func<double> min, Func<double> max) {
            this.viewport = viewport;
            this.get = get;
            this.set = set;
            this.min = min;
            this.max = max;
        }

        public void By(double amount, bool animate) {
            viewport.FinishZooms();
            long now = Stopwatch.GetTimestamp();
            bool continuing = active && !MovedElsewhere();
            double target = Math.Clamp((continuing ? motion.Target : get()) + amount, min(), max());
            if (!animate || !viewport.Start()) {
                Set(target);
                active = false;
                return;
            }
            motion = continuing ? motion.Retarget(target, now) : Motion.EaseOut(get(), target, now);
            lastSet = get();
            active = true;
        }

        internal bool Step(long now) {
            if (!active) {
                return false;
            }
            if (MovedElsewhere()) {
                // Dragging the thumb, auto-scroll or a jump took over.
                active = false;
                return false;
            }
            Set(Math.Clamp(motion.Position(now), min(), max()));
            active = !motion.Done(now);
            return active;
        }

        internal void Cancel() {
            active = false;
        }

        private bool MovedElsewhere() {
            return Math.Abs(get() - lastSet) > 1e-6;
        }

        private void Set(double value) {
            set(value);
            lastSet = get();
        }
    }

    public class ZoomGlide {
        // Smallest factor one wheel event may scale by; the view model clamps the
        // zoom level itself.
        private const double MinFactor = 0.01;

        private readonly SmoothViewport viewport;
        private readonly Action<Point, double> zoom;
        // Motion in log scale since the glide began, and how much of it has been
        // applied to the view so far.
        private Motion motion;
        private double applied;
        private Point anchor;
        private bool active;

        internal ZoomGlide(SmoothViewport viewport, Action<Point, double> zoom) {
            this.viewport = viewport;
            this.zoom = zoom;
        }

        /// <param name="anchor">The point that stays put, as a fraction of the view's size.</param>
        public void By(Point anchor, double delta, bool animate) {
            viewport.CancelScrolls();
            if (!animate || !viewport.Start()) {
                Finish();
                zoom(anchor, delta);
                return;
            }
            this.anchor = anchor;
            long now = Stopwatch.GetTimestamp();
            double step = Math.Log(Math.Max(1 + 2 * delta, MinFactor));
            if (active) {
                motion = motion.Retarget(motion.Target + step, now);
            } else {
                applied = 0;
                motion = Motion.EaseOut(0, step, now);
            }
            active = true;
        }

        internal bool Step(long now) {
            if (!active) {
                return false;
            }
            ApplyUpTo(motion.Position(now));
            active = !motion.Done(now);
            return active;
        }

        internal void Finish() {
            if (active) {
                ApplyUpTo(motion.Target);
            }
            active = false;
        }

        private void ApplyUpTo(double next) {
            double step = next - applied;
            if (step != 0) {
                zoom(anchor, (Math.Exp(step) - 1) / 2);
                applied = next;
            }
        }
    }
}
