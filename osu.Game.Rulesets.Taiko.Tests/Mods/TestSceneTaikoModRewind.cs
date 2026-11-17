// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Input.Bindings;
using osu.Framework.Testing;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;
using osu.Game.Rulesets.Taiko.Mods;
using osu.Game.Rulesets.Taiko.Objects;
using osuTK.Input;

namespace osu.Game.Rulesets.Taiko.Tests.Mods
{
    public partial class TestSceneTaikoModRewind : TaikoModTestScene
    {
        private static JudgementResult resultOf(HitObject hitObject, HitResult type)
            => new JudgementResult(hitObject, hitObject.CreateJudgement()) { Type = type };

        private bool sawMiss, rewound;

        private void createRewindTest(Mod mod, HitObject missed, bool checkMarks)
        {
            sawMiss = rewound = false;
            bool sawMark = false;

            CreateModTest(new ModTestData
            {
                Mod = mod,
                Autoplay = false, // no input, so the object is missed and rewound over and over
                CreateBeatmap = () => new Beatmap { HitObjects = new List<HitObject> { missed } },
                // The steps below run before this is checked.
                PassCondition = () => true,
            });

            AddUntilStep("missed", () =>
            {
                if (checkMarks && Player.DrawableRuleset.Playfield.ChildrenOfType<DrawableJudgement>().Any(j => j.JudgedHitObject != null))
                    sawMark = true;

                return sawMiss = Player.GameplayClockContainer.CurrentTime > 3000;
            });
            AddUntilStep("rewound to half a second before it", () =>
            {
                // The miss's mark is made just after the miss, so look for it here too.
                if (checkMarks && Player.DrawableRuleset.Playfield.ChildrenOfType<DrawableJudgement>().Any(j => j.JudgedHitObject != null))
                    sawMark = true;

                return rewound = sawMiss && Player.GameplayClockContainer.CurrentTime < 2600;
            });
            AddUntilStep("plays on again", () => Player.GameplayClockContainer.CurrentTime > 2800 && Player.GameplayClockContainer.CurrentTime < 3000);
            AddAssert("didn't fail", () => !Player.GameplayState.HasFailed);

            if (checkMarks)
            {
                AddAssert("the miss showed a mark", () => sawMark);
                // The undone miss's mark mustn't still be waiting to show when the object comes round again.
                AddAssert("no old mark waiting to show", () => !Player.DrawableRuleset.Playfield.ChildrenOfType<DrawableJudgement>().Any(j =>
                    j.JudgedHitObject != null && j.LifetimeStart > Player.GameplayClockContainer.CurrentTime && j.LifetimeEnd > j.LifetimeStart));
            }
        }

        [TestCase(HitResult.Great, false, false)]
        [TestCase(HitResult.Ok, false, true)]
        [TestCase(HitResult.Miss, true, true)]
        public void TestNoteTriggers(HitResult type, bool onMiss, bool onNotSS)
        {
            Assert.That(new TaikoModRewindOnMiss().ShouldRewind(resultOf(new Hit(), type)), Is.EqualTo(onMiss));
            Assert.That(new TaikoModRewindOnNotSS().ShouldRewind(resultOf(new Hit(), type)), Is.EqualTo(onNotSS));
        }

        [Test]
        public void TestBonusesNeverTrigger()
        {
            // Drumroll ticks and swells are bonus in taiko, so missing them costs nothing.
            foreach (var hitObject in new HitObject[] { new DrumRollTick(new DrumRoll()), new Swell() })
            {
                Assert.That(new TaikoModRewindOnMiss().ShouldRewind(resultOf(hitObject, HitResult.IgnoreMiss)), Is.False, hitObject.GetType().Name);
                Assert.That(new TaikoModRewindOnNotSS().ShouldRewind(resultOf(hitObject, HitResult.IgnoreMiss)), Is.False, hitObject.GetType().Name);
            }
        }

        private static DrumRoll drumRoll()
        {
            var roll = new DrumRoll { StartTime = 3000, Duration = 1000 };
            roll.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty());
            return roll;
        }

        /// <summary>
        /// Plays one pass of a drumroll's ticks, hitting the first <paramref name="hits"/> of them, and returns whether it rewound.
        /// </summary>
        private static bool playDrumRoll(TaikoModRewind mod, DrumRoll roll, int hits)
        {
            var ticks = roll.NestedHitObjects.OfType<DrumRollTick>().ToList();
            bool rewound = false;

            for (int i = 0; i < ticks.Count; i++)
                rewound |= mod.ShouldRewind(resultOf(ticks[i], i < hits ? HitResult.SmallBonus : HitResult.IgnoreMiss));

            return rewound;
        }

        [Test]
        public void TestSkippedDrumRollRewinds()
        {
            var roll = drumRoll();
            int ticks = roll.NestedHitObjects.OfType<DrumRollTick>().Count();
            Assert.That(ticks, Is.GreaterThan(3));

            Assert.That(playDrumRoll(new TaikoModRewindOnMiss(), roll, 0), Is.True, "none hit, on miss");
            Assert.That(playDrumRoll(new TaikoModRewindOnNotSS(), roll, 0), Is.True, "none hit, not SS");
            Assert.That(playDrumRoll(new TaikoModRewindOnMiss(), roll, ticks / 2 - 1), Is.True, "under half hit");
            Assert.That(playDrumRoll(new TaikoModRewindOnMiss(), roll, (ticks + 1) / 2), Is.False, "half hit");
            Assert.That(playDrumRoll(new TaikoModRewindOnMiss(), roll, ticks), Is.False, "all hit");
        }

        [Test]
        public void TestDrumRollSettingOffNeverRewinds()
            => Assert.That(playDrumRoll(new TaikoModRewindOnMiss { DrumRollTicksToHit = { Value = 0 } }, drumRoll(), 0), Is.False);

        [Test]
        public void TestRetriedDrumRollIsJudgedAfresh()
        {
            var mod = new TaikoModRewindOnMiss();
            var roll = drumRoll();
            int ticks = roll.NestedHitObjects.OfType<DrumRollTick>().Count();

            Assert.That(playDrumRoll(mod, roll, 0), Is.True, "first try skipped");
            Assert.That(playDrumRoll(mod, roll, ticks), Is.False, "second try all hit");
            Assert.That(playDrumRoll(mod, roll, 0), Is.True, "third try skipped again");
        }

        [Test]
        public void TestSkippedDrumRollRewindsInPlay() => createRewindTest(new TaikoModRewindOnMiss(), drumRoll(), false);

        [Test]
        public void TestMissRightAfterLandingStillRewinds()
        {
            // A stream long and dense enough that the rewind has to land in it with almost no warning.
            const double skipped = 10000;
            var noteTimes = new List<double>();
            for (double t = 1000; t <= 12000; t += 150)
                noteTimes.Add(t);

            Key key = Key.Unknown;
            var pressed = new HashSet<double>();
            bool rewoundOnce = false, resumed = false, done = false;
            double highest = double.NegativeInfinity, landing = double.MaxValue, furthest = double.NegativeInfinity;
            ScheduledDelegate? player = null;

            CreateModTest(new ModTestData
            {
                // Slowed down so the test's per-frame presses land on every note: frames in a test can be far apart in song time.
                Mods = new Mod[] { new TaikoModRewindOnMiss(), new TaikoModHalfTime { SpeedChange = { Value = 0.5 } } },
                Autoplay = false,
                CreateBeatmap = () => new Beatmap { HitObjects = noteTimes.Select(t => (HitObject)new Hit { StartTime = t }).ToList() },
                // The steps below run before this is checked.
                PassCondition = () => true,
            });

            AddStep("find the key for a centre hit", () => key = (Key)new TaikoRuleset().GetDefaultKeyBindings()
                                                                                       .First(b => b.GetAction<TaikoAction>() == TaikoAction.LeftCentre)
                                                                                       .KeyCombination.Keys.First());

            // Hits every note on time apart from one, then after the rewind presses nothing, and measures how far play gets before rewinding again.
            AddStep("play", () => player = Scheduler.AddDelayed(() =>
            {
                if (Player?.GameplayClockContainer == null || !Player.IsLoaded || done)
                    return;

                double t = Player.DrawableRuleset.FrameStableClock.CurrentTime;

                if (!rewoundOnce)
                {
                    // A rewind steps back a little each frame, so compare with the furthest point rather than the last frame.
                    highest = Math.Max(highest, t);

                    if (t < highest - 50)
                        rewoundOnce = true;
                    else if (Player.GameplayClockContainer.IsRunning)
                    {
                        foreach (double note in noteTimes)
                        {
                            if (note != skipped && !pressed.Contains(note) && t >= note - 8 && t < note + 70)
                            {
                                pressed.Add(note);
                                InputManager.PressKey(key);
                                InputManager.ReleaseKey(key);
                            }
                        }
                    }
                }
                else if (!resumed)
                {
                    // Still rewinding or in the get-ready pause.
                    landing = Math.Min(landing, t);

                    if (Player.GameplayClockContainer.IsRunning)
                    {
                        resumed = true;
                        furthest = t;
                    }
                }
                else
                {
                    furthest = Math.Max(furthest, t);

                    // The clock is held the moment the next rewind starts.
                    if (!Player.GameplayClockContainer.IsRunning || t < furthest - 50)
                        done = true;
                }

            }, 0, true));

            // Each step may only wait so long, and at half speed the skipped note is a while away.
            foreach (double milestone in new double[] { 1500, 3000, 4500, 6000, 7500, 9000 })
                AddUntilStep($"past {milestone}", () => rewoundOnce || Player.DrawableRuleset.FrameStableClock.CurrentTime > milestone);
            AddUntilStep("rewound for the skipped note", () => rewoundOnce);
            AddUntilStep("rewound again", () => done);
            AddAssert("missing the first note after landing rewinds again", () =>
            {
                TestContext.Progress.WriteLine($"AGAIN landed {landing:N0}, got to {furthest:N0} before rewinding again, pressed {pressed.Count} notes, last press {(pressed.Count > 0 ? pressed.Max() : 0):N0}");
                return furthest - landing < 300;
            });
            AddStep("stop", () => player?.Cancel());
        }

        [Test]
        public void TestMissRewindsThenResumes() => createRewindTest(new TaikoModRewindOnMiss(), new Hit { StartTime = 3000 }, true);

        [Test]
        public void TestNotSSRewindsThenResumes() => createRewindTest(new TaikoModRewindOnNotSS(), new Hit { StartTime = 3000 }, false);
    }
}
