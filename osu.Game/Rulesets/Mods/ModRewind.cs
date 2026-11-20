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
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Mods
{
    /// <summary>
    /// Rewinds the map a little (visibly, in reverse) after a result the mod doesn't accept,
    /// holds for a moment to get ready, then resumes with a burst effect. Shared by every ruleset.
    /// </summary>
    public abstract class ModRewind : Mod, IApplicableFailOverride
    {
        public override IconUsage? Icon => FontAwesome.Solid.History;
        public override ModType Type => ModType.DifficultyReduction;
        public override bool ValidForMultiplayer => false;
        public override bool ValidForMultiplayerAsFreeMod => false;

        public override Type[] IncompatibleMods => new[]
        {
            typeof(ModAutoplay), typeof(ModCinema), typeof(ModRelax),
            typeof(ModSuddenDeath), typeof(ModPerfect), typeof(ModAccuracyChallenge), typeof(ModRewind),
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

        [SettingSource("Get-ready pause", "How many seconds to hold after the rewind so you can get ready.")]
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

        /// <summary>
        /// Anything missed which the game counts as a miss. Not only combo breaks: dropping the end of a slider or a hold
        /// is a miss which may leave the combo alone. Bonuses and display-only results aren't things that can be missed.
        /// </summary>
        public static bool IsMiss(JudgementResult result)
            => !result.IsHit && !result.Judgement.MaxResult.IsBonus() && result.Judgement.MaxResult != HitResult.IgnoreHit;

        /// <summary>
        /// The same rule as the Perfect mod ("SS or quit"): exactly what would cost an SS.
        /// </summary>
        public static bool CostsSS(JudgementResult result)
            => (costsSS(result.Judgement.MinResult) || costsSS(result.Judgement.MaxResult) || costsSS(result.Type))
               && result.Type != result.Judgement.MaxResult;

        private static bool costsSS(HitResult result) => result.AffectsAccuracy() || result.AffectsCombo();

        // Misses are undone by the rewind, so the run never fails.
        public bool PerformFail() => false;

        public bool RestartOnFail => false;

        /// <summary>
        /// The most warning the next object is promised after a rewind. Otherwise it gets the rewind distance.
        /// </summary>
        private const double max_warning = 1000;

        /// <summary>
        /// Lands this long after the previous object at the very least, when its own hit window isn't known.
        /// An object hit late has its result undone by landing before that hit, which brings it back with almost no time left.
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
        /// The least warning which counts as usable for a single-hit object. There's a pause to get ready in before play resumes.
        /// </summary>
        private const double usable_short_object_warning = 50;

        /// <summary>
        /// The least warning which counts as usable for a long object like a slider, spinner, hold note or drumroll.
        /// </summary>
        private const double usable_long_object_warning = 400;

        private static readonly HitResult[] hit_results_late_first = { HitResult.Meh, HitResult.Ok, HitResult.Good, HitResult.Great, HitResult.Perfect };

        /// <summary>
        /// Finds where to rewind to: <paramref name="rewindBy"/> before the missed object, moved further back
        /// if needed so that the next object isn't about to be hit, and never inside a long object.
        /// </summary>
        /// <param name="objects">The beatmap's top-level objects.</param>
        /// <param name="missedTime">The start time of the object that was missed, which may be a tick inside a long object.</param>
        /// <param name="rewindBy">The rewind distance.</param>
        /// <param name="earlierLandings">Where earlier rewinds in this play landed. A rewind never goes back past the latest one
        /// of these which is still before it: those were already fine places to restart, and going further back than one feels random.</param>
        public static double GetRewindTarget(IEnumerable<HitObject> objects, double missedTime, double rewindBy, IEnumerable<double>? earlierLandings = null)
        {
            double landing = findRewindTarget(objects, missedTime, rewindBy, out double ideal);

            double latestEarlierLanding = earlierLandings?.Where(l => l <= ideal).DefaultIfEmpty(double.NegativeInfinity).Max() ?? double.NegativeInfinity;

            return Math.Max(landing, latestEarlierLanding);
        }

        /// <summary>
        /// Where the object containing <paramref name="time"/> starts: a long object's start for anything inside it.
        /// </summary>
        public static double StartOfContaining(IEnumerable<HitObject> objects, double time)
            => objects.Where(o => o.StartTime <= time && o.GetEndTime() >= time)
                      .Select(o => o.StartTime)
                      .DefaultIfEmpty(time)
                      .Min();

        /// <summary>
        /// How late after its time an object can still be hit, or the general margin when that isn't known.
        /// </summary>
        private static double lateHitMargin(HitObject hitObject)
        {
            if (hitObject.GetEndTime() > hitObject.StartTime || hitObject.HitWindows == null)
                return late_hit_margin;

            foreach (var result in hit_results_late_first)
            {
                if (hitObject.HitWindows.IsHitResultAllowed(result))
                    return hitObject.HitWindows.WindowFor(result) + 10;
            }

            return late_hit_margin;
        }

        private static double findRewindTarget(IEnumerable<HitObject> objects, double missedTime, double rewindBy, out double ideal)
        {
            var sorted = objects.OrderBy(o => o.StartTime).ToList();

            // Missing a tick means retrying the whole long object, so count from where it starts.
            double missedStart = StartOfContaining(sorted, missedTime);

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
                double margin = i > 0 ? lateHitMargin(sorted[i - 1]) : late_hit_margin;

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
                    bestIsUsable = warning >= (sorted[i].GetEndTime() > objectStart ? usable_long_object_warning : usable_short_object_warning);
                }
            }

            return best ?? target;
        }
    }

    /// <summary>
    /// The rewind for one ruleset: its objects, and the gameplay keys to ignore while a rewind is happening.
    /// </summary>
    public abstract class ModRewind<TObject, TAction> : ModRewind, IApplicableToDrawableRuleset<TObject>
        where TObject : HitObject
        where TAction : struct
    {
        /// <summary>
        /// Whether a key is ignored during a rewind. By default every gameplay key is, so nothing gets hit while time stands still,
        /// apart from just before play resumes.
        /// </summary>
        protected virtual bool IgnoreDuringRewind(TAction action) => true;

        /// <summary>
        /// Whether a press should get through anyway, for example because the game is waiting for it after resuming.
        /// </summary>
        protected virtual bool LetPressThrough(DrawableRuleset<TObject> drawableRuleset) => false;

        /// <summary>
        /// Called when a rewind lands and again when play resumes, to tidy up anything the trip back in time left behind.
        /// </summary>
        protected virtual void OnRewindSettled(DrawableRuleset<TObject> drawableRuleset)
        {
        }

        /// <summary>
        /// Whether pressing a key with nothing to hit counts as a miss. Needs <see cref="LaneOf(TAction, DrawableRuleset{TObject})"/>.
        /// </summary>
        protected virtual bool CountStrayPresses => false;

        /// <summary>
        /// Whether holding a key down with no long note to hold counts as a miss. Needs both <c>LaneOf</c> overloads.
        /// </summary>
        protected virtual bool CountHeldKeys => false;

        /// <summary>
        /// The lane a key plays, so a press can be matched with what it hit. Null for keys that don't hit anything.
        /// </summary>
        protected virtual int? LaneOf(TAction action, DrawableRuleset<TObject> drawableRuleset) => null;

        /// <summary>
        /// The lane an object is in.
        /// </summary>
        protected virtual int? LaneOf(HitObject hitObject) => null;

        public void ApplyToDrawableRuleset(DrawableRuleset<TObject> drawableRuleset)
        {
            var overlay = new RewindOverlay<TObject, TAction>(drawableRuleset, ShouldRewind,
                a => IgnoreDuringRewind(a), () => LetPressThrough(drawableRuleset), () => OnRewindSettled(drawableRuleset),
                RewindSeconds.Value * 1000, AnimationSeconds.Value * 1000, HoldSeconds.Value * 1000,
                a => LaneOf(a, drawableRuleset), LaneOf, CountStrayPresses, CountHeldKeys);

            drawableRuleset.Overlays.Add(overlay);
            drawableRuleset.NewResult += overlay.OnNewResult;
            drawableRuleset.RevertResult += overlay.OnRevertResult;
        }
    }

    internal partial class RewindOverlay<TObject, TAction> : CompositeDrawable, IKeyBindingHandler<TAction>
        where TObject : HitObject
        where TAction : struct
    {
        private const double max_seek_lead = 200;

        private enum Phase
        {
            Idle,
            Rewinding,
            Holding,
        }

        private readonly DrawableRuleset<TObject> drawableRuleset;
        private readonly Func<JudgementResult, bool> shouldRewind;
        private readonly Func<TAction, bool> ignoreDuringRewind;
        private readonly Func<bool> letPressThrough;
        private readonly Action settled;
        private readonly double rewindBy;
        private readonly double animationLength;
        private readonly double holdLength;
        private readonly Func<TAction, int?> laneOfAction;
        private readonly Func<HitObject, int?> laneOfObject;
        private readonly bool countStrayPresses;
        private readonly bool countHeldKeys;

        /// <summary>
        /// How long a key may stay down after the press for a normal note. Deliberately strict: the player asked for it,
        /// to train out holding taps too long (their taps measured about 0.15 s).
        /// </summary>
        private const double held_key_allowance = 100;

        /// <summary>
        /// How far from a long note's start a press can be and still be the one that starts it.
        /// </summary>
        private const double long_note_press_window = 250;

        /// <summary>
        /// How late a long note may be let go of. The game itself only misses its end at about twice this, and gives a lower judgement before that.
        /// </summary>
        private const double long_note_release_allowance = 100;

        /// <summary>
        /// Keys held down since a press during play, by lane: when the press was, and when the key has to be let go of by.
        /// </summary>
        private readonly Dictionary<int, (double pressed, double letGoBy)> heldKeys = new Dictionary<int, (double pressed, double letGoBy)>();

        /// <summary>
        /// How long before play resumes a press already counts. People time the note sitting on the line by the closing ring,
        /// and a press a moment too early used to be swallowed, which missed that note. It's judged at the time the rewind landed on.
        /// </summary>
        private const double early_press_window = 150;

        private bool nudged;

        /// <summary>
        /// A press waiting to see whether it hit anything, checked once the input it came with has been handled.
        /// </summary>
        private (int lane, double time, long frame)? pendingPress;

        private long frame;

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

        public RewindOverlay(DrawableRuleset<TObject> drawableRuleset, Func<JudgementResult, bool> shouldRewind, Func<TAction, bool> ignoreDuringRewind,
                             Func<bool> letPressThrough, Action settled, double rewindBy, double animationLength, double holdLength,
                             Func<TAction, int?> laneOfAction, Func<HitObject, int?> laneOfObject, bool countStrayPresses, bool countHeldKeys)
        {
            this.drawableRuleset = drawableRuleset;
            this.shouldRewind = shouldRewind;
            this.ignoreDuringRewind = ignoreDuringRewind;
            this.letPressThrough = letPressThrough;
            this.settled = settled;
            this.rewindBy = rewindBy;
            this.animationLength = animationLength;
            this.holdLength = holdLength;
            this.laneOfAction = laneOfAction;
            this.laneOfObject = laneOfObject;
            this.countStrayPresses = countStrayPresses;
            this.countHeldKeys = countHeldKeys;

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
            // Anything judged in the lane of a press means the press was for something.
            if (pendingPress is { } press && laneOfObject(result.HitObject) == press.lane)
                pendingPress = null;

            if (phase != Phase.Idle || drawableRuleset.HasReplayLoaded.Value)
                return;

            if (!shouldRewind(result))
                return;

            rewindFor(result.HitObject.StartTime);
        }

        private void rewindFor(double missedTime)
        {
            fromTime = gameplayClock.CurrentTime;
            toTime = Math.Max(ModRewind.GetRewindTarget(drawableRuleset.Objects, Math.Min(missedTime, fromTime), rewindBy, landings), gameplayClock.StartTime);

            if (toTime >= fromTime)
                return;

            landings.Add(toTime);
            beginRewind();
        }

        /// <summary>
        /// Whether a press at this time could be a stray one: during the map, not before its first object, after its last or in a break.
        /// </summary>
        private bool duringPlay(double time)
        {
            if (!drawableRuleset.Objects.Any())
                return false;

            double first = drawableRuleset.Objects.Min(o => o.StartTime);
            double last = drawableRuleset.Objects.Max(o => o.GetEndTime());

            return time >= first - 500 && time <= last + 500
                   && !drawableRuleset.Beatmap.Breaks.Any(b => time >= b.StartTime && time <= b.EndTime);
        }

        /// <summary>
        /// An undone result's mark would otherwise wait to play when time reaches it again, even if the object is hit this time.
        /// </summary>
        public void OnRevertResult(JudgementResult result)
        {
            foreach (var mark in drawableRuleset.Playfield.ChildrenOfType<DrawableJudgement>().Where(j => j.Result == result))
                mark.LifetimeEnd = mark.LifetimeStart;
        }

        private void beginRewind()
        {
            phase = Phase.Rewinding;
            elapsed = 0;
            heldKeys.Clear();

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
            nudged = false;

            settled();

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

            settled();
            gameplayClock.ReleaseClock();

            tint.FadeOut(250);
            flash.ClearTransforms();
            flash.FadeTo(0.45f).FadeOut(400, Easing.OutQuint);

            ring.ClearTransforms();
            ring.ResizeTo(110).FadeTo(1).ResizeTo(900, 500, Easing.OutQuint).FadeOut(500, Easing.OutQuint);
        }

        // Gameplay keys are ignored during a rewind, so nothing gets hit while time stands still.
        // They used to be switched off entirely instead. In osu!, clicking the orange circle to resume tells the game to ignore
        // that same click when it arrives in gameplay; with keys switched off it never arrived, so the game ignored
        // the next real hit instead. Presses while paused, and ones the ruleset says the game is waiting for, go through.
        public bool OnPressed(KeyBindingPressEvent<TAction> e)
        {
            if ((countStrayPresses || countHeldKeys) && phase == Phase.Idle && !gameplayClock.IsPaused.Value && !drawableRuleset.HasReplayLoaded.Value
                && laneOfAction(e.Action) is int lane)
            {
                double time = drawableRuleset.FrameStableClock.CurrentTime;

                if (duringPlay(time))
                {
                    if (countStrayPresses)
                        pendingPress = (lane, time, frame);

                    if (countHeldKeys)
                        heldKeys[lane] = (time, letGoBy(lane, time));
                }
            }

            if (phase == Phase.Idle || gameplayClock.IsPaused.Value || !ignoreDuringRewind(e.Action))
                return false;

            if (phase == Phase.Holding && elapsed >= holdLength - early_press_window)
                return false;

            return !letPressThrough();
        }

        public void OnReleased(KeyBindingReleaseEvent<TAction> e)
        {
            if (laneOfAction(e.Action) is int lane)
                heldKeys.Remove(lane);
        }

        /// <summary>
        /// When a key pressed at this time has to be let go of by: just after the end of a long note in its lane that the press could start,
        /// otherwise a little after the press.
        /// </summary>
        private double letGoBy(int lane, double pressTime)
        {
            double? longNoteEnd = null;

            foreach (var hitObject in drawableRuleset.Objects)
            {
                if (hitObject.GetEndTime() > hitObject.StartTime && laneOfObject(hitObject) == lane
                                                                 && Math.Abs(hitObject.StartTime - pressTime) <= long_note_press_window)
                    longNoteEnd = Math.Max(longNoteEnd ?? double.NegativeInfinity, hitObject.GetEndTime());
            }

            return longNoteEnd + long_note_release_allowance ?? pressTime + held_key_allowance;
        }

        protected override void Update()
        {
            base.Update();

            // Input is handled before this updates, so by now anything a press hit has been judged.
            frame++;

            if (pendingPress is { } press && press.frame < frame)
            {
                pendingPress = null;

                if (phase == Phase.Idle)
                    rewindFor(press.time);
            }

            // A pause may end with a key let go of where gameplay doesn't see it, so start afresh after one.
            if (gameplayClock.IsPaused.Value)
                heldKeys.Clear();

            if (countHeldKeys && phase == Phase.Idle && heldKeys.Count > 0)
            {
                double now = drawableRuleset.FrameStableClock.CurrentTime;

                foreach (var (_, held) in heldKeys)
                {
                    if (now > held.letGoBy && duringPlay(now))
                    {
                        heldKeys.Clear();
                        rewindFor(held.pressed);
                        break;
                    }
                }
            }

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
                    // Time stopped while it was going backwards, so the game still thinks it's rewinding, and judges nothing until it moves forwards.
                    // Step forwards a hair once gameplay has caught up, so a press just before play resumes can hit.
                    if (!nudged && drawableRuleset.FrameStableClock.IsRewinding
                                && Math.Abs(drawableRuleset.FrameStableClock.CurrentTime - gameplayClock.CurrentTime) < 1)
                    {
                        nudged = true;
                        gameplayClock.Seek(gameplayClock.CurrentTime + 1);
                    }

                    if (elapsed >= holdLength)
                        release();
                    break;
            }
        }
    }
}
