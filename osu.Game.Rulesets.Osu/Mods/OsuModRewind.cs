// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Osu.Mods
{
    /// <summary>
    /// Rewinds the map a few seconds (visibly, in reverse) after a result the mod doesn't accept,
    /// holds for a moment so the cursor can be placed, then resumes with a burst effect.
    /// </summary>
    public abstract partial class OsuModRewind : Mod, IApplicableToDrawableRuleset<OsuHitObject>, IApplicableFailOverride
    {
        public override IconUsage? Icon => FontAwesome.Solid.History;
        public override ModType Type => ModType.DifficultyReduction;
        public override bool ValidForMultiplayer => false;
        public override bool ValidForMultiplayerAsFreeMod => false;

        public override Type[] IncompatibleMods => new[]
        {
            typeof(ModAutoplay), typeof(ModCinema), typeof(OsuModRelax), typeof(OsuModAutopilot),
            typeof(ModSuddenDeath), typeof(ModPerfect), typeof(ModAccuracyChallenge), typeof(OsuModRewind),
        };

        [SettingSource("Rewind distance", "How many seconds before the missed object to rewind to.")]
        public BindableDouble RewindSeconds { get; } = new BindableDouble(0.5)
        {
            MinValue = 0.5,
            MaxValue = 5,
            Precision = 0.5,
        };

        [SettingSource("Rewind animation length", "How many seconds the visible rewind takes.")]
        public BindableDouble AnimationSeconds { get; } = new BindableDouble(0.5)
        {
            MinValue = 0.25,
            MaxValue = 3,
            Precision = 0.25,
        };

        [SettingSource("Get-ready pause", "How many seconds to hold after the rewind so you can place your cursor.")]
        public BindableDouble HoldSeconds { get; } = new BindableDouble(0.75)
        {
            MinValue = 0,
            MaxValue = 3,
            Precision = 0.25,
        };

        /// <summary>
        /// Whether a result should trigger a rewind.
        /// </summary>
        public abstract bool ShouldRewind(JudgementResult result);

        public void ApplyToDrawableRuleset(DrawableRuleset<OsuHitObject> drawableRuleset)
        {
            var overlay = new RewindOverlay(drawableRuleset, ShouldRewind, RewindSeconds.Value * 1000, AnimationSeconds.Value * 1000, HoldSeconds.Value * 1000);
            drawableRuleset.Overlays.Add(overlay);
            drawableRuleset.NewResult += overlay.OnNewResult;
        }

        /// <summary>
        /// The most warning the next object is promised after a rewind. Otherwise it gets the rewind distance.
        /// </summary>
        private const double max_warning = 1000;

        /// <summary>
        /// Lands this long after the previous slider or spinner at the very least, or an unknown object. An object hit late has its result
        /// undone by landing before that hit, which brings it back with almost no time left to press it.
        /// </summary>
        private const double late_hit_margin = 200;

        /// <summary>
        /// How much further back than the rewind distance to look when nothing usable has been found.
        /// </summary>
        private const double max_extra_search = 6000;

        /// <summary>
        /// How much further back to look for more warning when what's been found is already usable.
        /// A pause in the map a few seconds back shouldn't pull every rewind back to it.
        /// </summary>
        private const double near_extra_search = 1000;

        /// <summary>
        /// The least warning which counts as usable for a circle. There's a pause to aim in before play resumes.
        /// </summary>
        private const double usable_circle_warning = 50;

        /// <summary>
        /// The least warning which counts as usable for a slider or spinner.
        /// </summary>
        private const double usable_long_object_warning = 400;

        /// <summary>
        /// Finds where to rewind to: <paramref name="rewindBy"/> before the missed object, moved further back
        /// if needed so that the next object isn't about to be hit, and never inside a slider or spinner.
        /// </summary>
        /// <param name="objects">The beatmap's top-level objects.</param>
        /// <param name="missedTime">The start time of the object that was missed, which may be a tick inside a slider.</param>
        /// <param name="rewindBy">The rewind distance.</param>
        /// <param name="earlierLandings">Where earlier rewinds in this play landed. A rewind never goes back past the latest one
        /// of these which is still before it: those were already fine places to restart, and going further back than one feels random.</param>
        public static double GetRewindTarget(IEnumerable<HitObject> objects, double missedTime, double rewindBy, IEnumerable<double>? earlierLandings = null)
        {
            double landing = findRewindTarget(objects, missedTime, rewindBy, out double ideal);

            double latestEarlierLanding = earlierLandings?.Where(l => l <= ideal).DefaultIfEmpty(double.NegativeInfinity).Max() ?? double.NegativeInfinity;

            return Math.Max(landing, latestEarlierLanding);
        }

        private static double findRewindTarget(IEnumerable<HitObject> objects, double missedTime, double rewindBy, out double ideal)
        {
            var sorted = objects.OrderBy(o => o.StartTime).ToList();

            // Missing a tick means retrying the whole slider, so count from where it starts.
            double missedStart = sorted.Where(o => o.StartTime <= missedTime && o.GetEndTime() >= missedTime)
                                       .Select(o => o.StartTime)
                                       .DefaultIfEmpty(missedTime)
                                       .Min();

            double target = ideal = missedStart - rewindBy;

            // The end of everything before each object, so overlapping objects are never landed inside.
            double[] previousEnd = new double[sorted.Count];
            double latestEnd = double.NegativeInfinity;

            for (int i = 0; i < sorted.Count; i++)
            {
                previousEnd[i] = latestEnd;
                latestEnd = Math.Max(latestEnd, sorted[i].GetEndTime());
            }

            double? best = null;
            double bestWarning = 0;
            bool bestIsUsable = false;

            // Latest first, looking at each gap between objects for the one closest to the target which fits.
            for (int i = sorted.Count - 1; i >= 0; i--)
            {
                // A circle can only be hit as late as its hit window, which is often much less than the general margin.
                double margin = i > 0 && sorted[i - 1] is HitCircle { HitWindows: not null } previousCircle
                    ? previousCircle.HitWindows.WindowFor(HitResult.Meh) + 10
                    : late_hit_margin;

                double earliest = previousEnd[i] + margin;

                if (earliest > target)
                    continue;

                double objectStart = sorted[i].StartTime;

                if (best != null && objectStart < target - (bestIsUsable ? near_extra_search : max_extra_search))
                    break;

                double needed = Math.Min(rewindBy, max_warning);

                // In non-stop streams there's no room for the margin, so settle for the middle of the gap.
                double landing = earliest < objectStart
                    ? Math.Max(earliest, objectStart - needed)
                    : (previousEnd[i] + objectStart) / 2;

                landing = Math.Min(landing, target);

                double warning = objectStart - landing;

                if (warning >= needed)
                    return landing;

                if (warning > bestWarning)
                {
                    best = landing;
                    bestWarning = warning;
                    bestIsUsable = warning >= (sorted[i].GetEndTime() > objectStart ? usable_long_object_warning : usable_circle_warning);
                }
            }

            return best ?? target;
        }

        // Misses are undone by the rewind, so the run never fails.
        public bool PerformFail() => false;

        public bool RestartOnFail => false;

        private partial class RewindOverlay : CompositeDrawable, IKeyBindingHandler<OsuAction>
        {
            private const double max_seek_lead = 200;

            private enum Phase
            {
                Idle,
                Rewinding,
                Holding,
            }

            private readonly DrawableRuleset<OsuHitObject> drawableRuleset;
            private readonly Func<JudgementResult, bool> shouldRewind;
            private readonly double rewindBy;
            private readonly double animationLength;
            private readonly double holdLength;

            [Resolved]
            private GameplayClockContainer gameplayClock { get; set; } = null!;

            private Phase phase = Phase.Idle;
            private readonly List<double> landings = new List<double>();
            private double fromTime;
            private double toTime;
            private double elapsed;

            private Box tint = null!;
            private Box flash = null!;
            private FillFlowContainer banner = null!;
            private CircularContainer ring = null!;

            public RewindOverlay(DrawableRuleset<OsuHitObject> drawableRuleset, Func<JudgementResult, bool> shouldRewind, double rewindBy, double animationLength, double holdLength)
            {
                this.drawableRuleset = drawableRuleset;
                this.shouldRewind = shouldRewind;
                this.rewindBy = rewindBy;
                this.animationLength = animationLength;
                this.holdLength = holdLength;

                RelativeSizeAxes = Axes.Both;

                // Overlays live on the frame-stable gameplay clock, which runs backwards during the rewind.
                // Give the effects their own real-time clock so they always animate forwards.
                Clock = new FramedClock(new StopwatchClock(true));
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChildren = new Drawable[]
                {
                    tint = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4Extensions.FromHex("3d7cff"),
                        Alpha = 0,
                    },
                    ring = new CircularContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(200),
                        Masking = true,
                        BorderThickness = 10,
                        BorderColour = Color4.White,
                        Alpha = 0,
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Alpha = 0,
                            AlwaysPresent = true,
                        },
                    },
                    banner = new FillFlowContainer
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(14, 0),
                        Y = 90,
                        Alpha = 0,
                        Children = new Drawable[]
                        {
                            new SpriteIcon
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Size = new Vector2(36),
                                Icon = FontAwesome.Solid.Backward,
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Font = OsuFont.Torus.With(size: 48, weight: FontWeight.Bold),
                                Text = "REWIND",
                            },
                        },
                    },
                    flash = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.White,
                        Alpha = 0,
                    },
                };
            }

            public void OnNewResult(JudgementResult result)
            {
                if (phase != Phase.Idle || drawableRuleset.HasReplayLoaded.Value)
                    return;

                if (!shouldRewind(result))
                    return;

                fromTime = gameplayClock.CurrentTime;
                toTime = Math.Max(GetRewindTarget(drawableRuleset.Objects, Math.Min(result.HitObject.StartTime, fromTime), rewindBy, landings), gameplayClock.StartTime);

                if (toTime >= fromTime)
                    return;

                landings.Add(toTime);
                beginRewind();
            }

            private void beginRewind()
            {
                phase = Phase.Rewinding;
                elapsed = 0;

                gameplayClock.HoldClock();

                tint.ClearTransforms();
                tint.FadeTo(0.18f, 150);

                banner.ClearTransforms();
                banner.FadeIn(120).Then().FadeTo(0.35f, 250).Then().FadeTo(1, 250).Loop();

                ring.ClearTransforms();
                ring.FadeOut();
            }

            private void beginHold()
            {
                phase = Phase.Holding;
                elapsed = 0;

                banner.ClearTransforms();
                banner.FadeOut(100);

                tint.FadeTo(0.08f, 200);

                // A ring that closes in for the whole hold, like a countdown.
                ring.ClearTransforms();
                ring.ResizeTo(520).FadeTo(0.6f).ResizeTo(110, Math.Max(holdLength, 1), Easing.InQuad);
            }

            private void release()
            {
                phase = Phase.Idle;

                gameplayClock.ReleaseClock();

                tint.FadeOut(250);
                flash.ClearTransforms();
                flash.FadeTo(0.45f).FadeOut(400, Easing.OutQuint);

                ring.ClearTransforms();
                ring.ResizeTo(110).FadeTo(1).ResizeTo(900, 500, Easing.OutQuint).FadeOut(500, Easing.OutQuint);
            }

            // Clicks are ignored during a rewind, so nothing gets hit while time stands still.
            // They used to be switched off entirely instead. Clicking the orange circle to resume tells the game to ignore
            // that same click when it arrives in gameplay; with clicks switched off it never arrived, so the game ignored
            // the next real hit instead. The resuming click is let through here for the same reason.
            public bool OnPressed(KeyBindingPressEvent<OsuAction> e)
            {
                if (phase == Phase.Idle || gameplayClock.IsPaused.Value || e.Action is not (OsuAction.LeftButton or OsuAction.RightButton))
                    return false;

                if (drawableRuleset.Playfield.ChildrenOfType<OsuResumeOverlay.OsuResumeOverlayInputBlocker>().Any(b => b.BlockNextPress))
                    return false;

                return true;
            }

            public void OnReleased(KeyBindingReleaseEvent<OsuAction> e)
            {
            }

            protected override void Update()
            {
                base.Update();

                if (phase == Phase.Idle)
                    return;

                // The player may pause and resume mid-rewind, which restarts the music; keep it frozen.
                if (gameplayClock.IsRunning)
                    gameplayClock.HoldClock();

                // Cap the step so time spent in the pause menu doesn't jump the animation.
                elapsed += Math.Min(Time.Elapsed, 50);

                switch (phase)
                {
                    case Phase.Rewinding:
                        double progress = Math.Clamp(elapsed / animationLength, 0, 1);
                        double eased = 1 - Math.Pow(1 - progress, 2);
                        double target = fromTime + (toTime - fromTime) * eased;

                        // Release builds ignore the clock if it gets more than 500 ms from where gameplay is,
                        // which would freeze a long rewind. Never get ahead of gameplay by more than this.
                        double frameStableTime = drawableRuleset.FrameStableClock.CurrentTime;
                        target = Math.Max(target, frameStableTime - max_seek_lead);

                        if (Math.Abs(target - gameplayClock.CurrentTime) > 0.5)
                            gameplayClock.Seek(target);

                        bool arrived = Math.Abs(frameStableTime - toTime) < 20;

                        if ((progress >= 1 && arrived) || elapsed > animationLength + 5000)
                            beginHold();
                        break;

                    case Phase.Holding:
                        if (elapsed >= holdLength)
                            release();
                        break;
                }
            }
        }
    }
}
