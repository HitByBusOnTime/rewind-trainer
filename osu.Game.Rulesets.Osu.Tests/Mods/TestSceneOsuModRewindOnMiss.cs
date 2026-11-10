// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Rulesets.Scoring;
using osu.Game.Utils;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.Osu.Tests.Mods
{
    public partial class TestSceneOsuModRewindOnMiss : OsuModTestScene
    {
        private static Slider slider(double startTime)
        {
            var result = new Slider
            {
                StartTime = startTime,
                Path = new SliderPath(PathType.LINEAR, new[] { Vector2.Zero, new Vector2(300, 0) }),
            };

            result.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty());
            return result;
        }

        private static Spinner spinner(double startTime, double duration) => new Spinner { StartTime = startTime, Duration = duration };

        [Test]
        public void TestCircleInTheOpenRewindsExactDistance()
        {
            var objects = new HitObject[] { new HitCircle { StartTime = 5000 } };
            Assert.That(OsuModRewindOnMiss.GetRewindTarget(objects, 5000, 2000), Is.EqualTo(3000));
        }

        [Test]
        public void TestLandingInsideRealSliderMovesBeforeItsStart()
        {
            var earlier = slider(4000);
            double end = earlier.GetEndTime();
            Assert.That(end - earlier.StartTime, Is.GreaterThan(500), "slider too short for this test");

            double circleTime = end + 500;
            var objects = new HitObject[] { earlier, new HitCircle { StartTime = circleTime } };

            // A distance which lands exactly halfway through the slider.
            double rewindBy = circleTime - (earlier.StartTime + end) / 2;

            Assert.That(rewindBy, Is.GreaterThan(1000));
            Assert.That(OsuModRewindOnMiss.GetRewindTarget(objects, circleTime, rewindBy), Is.EqualTo(3000));
        }

        [Test]
        public void TestLandingInsideEarlierObjectGoesToItsStartEvenIfFurtherThanDistance()
        {
            // 8 s long, and 2 s before the missed circle is right in the middle of it.
            var objects = new HitObject[] { spinner(2000, 8000), new HitCircle { StartTime = 10500 } };
            Assert.That(OsuModRewindOnMiss.GetRewindTarget(objects, 10500, 2000), Is.EqualTo(1000));
        }

        [Test]
        public void TestMissedTickRetriesWholeObject()
        {
            var objects = new HitObject[] { spinner(5000, 6000) };
            Assert.That(OsuModRewindOnMiss.GetRewindTarget(objects, 9000, 2000), Is.EqualTo(3000));
        }

        [Test]
        public void TestLandsInGapAfterPreviousObject()
        {
            // The gap is shorter than the full lead-in, but long enough to use.
            var objects = new HitObject[] { spinner(1000, 3000), spinner(4600, 5000), new HitCircle { StartTime = 10000 } };
            Assert.That(OsuModRewindOnMiss.GetRewindTarget(objects, 10000, 2000), Is.EqualTo(4200));
        }

        [Test]
        public void TestChainedObjectsWithNoUsableGapAllRetry()
        {
            var objects = new HitObject[] { spinner(3000, 3000), spinner(6100, 3000), new HitCircle { StartTime = 9500 } };
            Assert.That(OsuModRewindOnMiss.GetRewindTarget(objects, 9500, 2000), Is.EqualTo(2000));
        }

        [Test]
        public void TestCircleAboutToBePressedGetsFullWarning()
        {
            // 2 s back lands 1 ms before this circle, which is the reported problem.
            var objects = new HitObject[] { new HitCircle { StartTime = 3001 }, new HitCircle { StartTime = 5000 } };
            Assert.That(OsuModRewindOnMiss.GetRewindTarget(objects, 5000, 2000), Is.EqualTo(2001));
        }

        [Test]
        public void TestLandingJustAfterCircleGoesBeforeIt()
        {
            // 2 s back lands 50 ms after this circle. If it was hit late, that hit gets undone
            // and the circle comes back with almost no time left, so retry it properly.
            var objects = new HitObject[] { new HitCircle { StartTime = 2950 }, new HitCircle { StartTime = 5000 } };
            Assert.That(OsuModRewindOnMiss.GetRewindTarget(objects, 5000, 2000), Is.EqualTo(1950));
        }

        [Test]
        public void TestHalfSecondInTheOpenIsExact()
        {
            var objects = new HitObject[] { new HitCircle { StartTime = 5000, TimePreempt = 1800 } };
            Assert.That(OsuModRewindOnMiss.GetRewindTarget(objects, 5000, 500), Is.EqualTo(4500));
        }

        [Test]
        public void TestHalfSecondStillWarnsAboutNextCircle()
        {
            // Half a second back lands 100 ms before this circle, so it moves back to give half a second of warning.
            var objects = new HitObject[] { new HitCircle { StartTime = 4600 }, new HitCircle { StartTime = 5000 } };
            Assert.That(OsuModRewindOnMiss.GetRewindTarget(objects, 5000, 500), Is.EqualTo(4100));
        }

        [TestCase(333)] // beats at 180 bpm
        [TestCase(250)] // half beats at 120 bpm
        public void TestPauseSecondsBackDoesNotPullTheRewindThere(int spacing)
        {
            // A busy section with a pause in the map 4 seconds before the miss.
            var objects = new List<HitObject>();

            for (double t = 1000; t < 6000; t += spacing)
                objects.Add(new HitCircle { StartTime = t });

            for (double t = 8000; t <= 20000; t += spacing)
                objects.Add(new HitCircle { StartTime = t });

            double missed = objects.Last(o => o.StartTime <= 12000).StartTime;
            double target = OsuModRewindOnMiss.GetRewindTarget(objects, missed, 500);

            Assert.That(missed - target, Is.LessThanOrEqualTo(2000));
        }

        private static List<HitObject> hardPart()
        {
            // A pause, then a stream with no room to give the usual warning anywhere in it.
            var objects = new List<HitObject>();

            for (double t = 1000; t <= 3000; t += 500)
                objects.Add(new HitCircle { StartTime = t });

            for (double t = 5000; t <= 15000; t += 80)
                objects.Add(new HitCircle { StartTime = t });

            return objects;
        }

        [Test]
        public void TestLaterMissDoesNotGoBackPastAnEarlierRewind()
        {
            var objects = hardPart();

            // Without anything earlier, a miss late in the stream is pulled back to the pause before it.
            double withoutEarlier = OsuModRewindOnMiss.GetRewindTarget(objects, 9000, 500);
            Assert.That(withoutEarlier, Is.LessThan(5000));

            // But a rewind earlier in the stream already landed at 7000, so it goes back no further than that.
            Assert.That(OsuModRewindOnMiss.GetRewindTarget(objects, 9000, 500, new[] { 7000.0 }), Is.EqualTo(7000));
        }

        [Test]
        public void TestEarlierRewindAfterTheMissIsIgnored()
        {
            var objects = hardPart();

            // That landing is later than the miss, so it can't be where this rewind goes.
            Assert.That(OsuModRewindOnMiss.GetRewindTarget(objects, 9000, 500, new[] { 12000.0 }),
                Is.EqualTo(OsuModRewindOnMiss.GetRewindTarget(objects, 9000, 500)));
        }

        [Test]
        public void TestMostRecentEarlierRewindIsUsed()
        {
            var objects = hardPart();

            Assert.That(OsuModRewindOnMiss.GetRewindTarget(objects, 9000, 500, new[] { 5500.0, 7000.0, 6000.0 }), Is.EqualTo(7000));
        }

        [Test]
        public void TestEarlierRewindDoesNotChangeANormalRewind()
        {
            var objects = new HitObject[] { new HitCircle { StartTime = 5000 } };

            Assert.That(OsuModRewindOnMiss.GetRewindTarget(objects, 5000, 500, new[] { 2000.0 }), Is.EqualTo(4500));
        }

        [Test]
        public void TestDefaultDistanceIsHalfASecond()
            => Assert.That(new OsuModRewindOnMiss().RewindSeconds.Value, Is.EqualTo(0.5));

        [Test]
        public void TestNearbyCircleWithEnoughTimeStaysExact()
        {
            var objects = new HitObject[] { new HitCircle { StartTime = 4000 }, new HitCircle { StartTime = 5000 } };
            Assert.That(OsuModRewindOnMiss.GetRewindTarget(objects, 5000, 2000), Is.EqualTo(3000));
        }

        [Test]
        public void TestLongStreamDoesNotGoBackToItsStart()
        {
            var objects = new List<HitObject>();

            for (double t = 1000; t <= 20000; t += 100)
                objects.Add(new HitCircle { StartTime = t });

            double target = OsuModRewindOnMiss.GetRewindTarget(objects, 15000, 2000);

            Assert.That(target, Is.LessThan(13000).And.GreaterThanOrEqualTo(6900));
        }

        [Test]
        public void TestLandingJustBeforeObjectGetsLeadIn()
        {
            // Would land 100 ms before the spinner, too late to react.
            var objects = new HitObject[] { spinner(3100, 1000), new HitCircle { StartTime = 5000 } };
            Assert.That(OsuModRewindOnMiss.GetRewindTarget(objects, 5000, 2000), Is.EqualTo(2100));
        }

        private static JudgementResult resultOf(HitObject hitObject, HitResult type)
            => new JudgementResult(hitObject, hitObject.CreateJudgement()) { Type = type };

        [TestCase(HitResult.Great, false)]
        [TestCase(HitResult.Ok, false)]
        [TestCase(HitResult.Meh, false)]
        [TestCase(HitResult.Miss, true)]
        public void TestMissTriggers(HitResult type, bool expected)
            => Assert.That(new OsuModRewindOnMiss().ShouldRewind(resultOf(new HitCircle(), type)), Is.EqualTo(expected));

        [TestCase(HitResult.Great, false)]
        [TestCase(HitResult.Ok, true)]
        [TestCase(HitResult.Meh, true)]
        [TestCase(HitResult.Miss, true)]
        public void TestNotSSTriggers(HitResult type, bool expected)
            => Assert.That(new OsuModRewindOnNotSS().ShouldRewind(resultOf(new HitCircle(), type)), Is.EqualTo(expected));

        private static IEnumerable<object[]> sliderResults()
        {
            var slider = new Slider();

            yield return new object[] { "head miss", new SliderHeadCircle(), HitResult.Miss, true };
            yield return new object[] { "head hit", new SliderHeadCircle(), HitResult.Great, false };
            yield return new object[] { "tick miss", new SliderTick(), HitResult.LargeTickMiss, true };
            yield return new object[] { "repeat miss", new SliderRepeat(slider), HitResult.LargeTickMiss, true };
            yield return new object[] { "end dropped", new SliderTailCircle(slider), HitResult.IgnoreMiss, true };
            yield return new object[] { "end held", new SliderTailCircle(slider), HitResult.SliderTailHit, false };
            yield return new object[] { "classic end dropped", new SliderTailCircle(slider) { ClassicSliderBehaviour = true }, HitResult.SmallTickMiss, true };
            yield return new object[] { "classic slider missed", new Slider { ClassicSliderBehaviour = true }, HitResult.Miss, true };
            yield return new object[] { "slider display result", new Slider(), HitResult.IgnoreMiss, false };
            yield return new object[] { "spinner tick", new SpinnerTick(), HitResult.IgnoreMiss, false };
            yield return new object[] { "spinner bonus", new SpinnerBonusTick(), HitResult.IgnoreMiss, false };
        }

        [TestCaseSource(nameof(sliderResults))]
        public void TestMissOnSliderParts(string name, HitObject hitObject, HitResult type, bool expected)
            => Assert.That(new OsuModRewindOnMiss().ShouldRewind(resultOf(hitObject, type)), Is.EqualTo(expected), name);

        [TestCaseSource(nameof(sliderResults))]
        public void TestNotSSOnSliderParts(string name, HitObject hitObject, HitResult type, bool expected)
            => Assert.That(new OsuModRewindOnNotSS().ShouldRewind(resultOf(hitObject, type)), Is.EqualTo(expected), name);

        [Test]
        public void TestNotSSOnSliderTicks()
        {
            var mod = new OsuModRewindOnNotSS();

            Assert.That(mod.ShouldRewind(resultOf(new SliderTick(), HitResult.LargeTickHit)), Is.False);
            Assert.That(mod.ShouldRewind(resultOf(new SliderTick(), HitResult.LargeTickMiss)), Is.True);
        }

        [Test]
        public void TestNotSSIgnoresSpinnerBonus()
        {
            // Not getting bonus spins doesn't cost an SS.
            Assert.That(new OsuModRewindOnNotSS().ShouldRewind(resultOf(new SpinnerBonusTick(), HitResult.IgnoreMiss)), Is.False);
        }

        [Test]
        public void TestRewindModsAreMutuallyExclusive()
            => Assert.That(ModUtils.CheckCompatibleSet(new Mod[] { new OsuModRewindOnMiss(), new OsuModRewindOnNotSS() }), Is.False);

        [Test]
        public void TestNotSSRewindsThenResumes()
        {
            bool sawPass = false, sawRewind = false;

            CreateModTest(new ModTestData
            {
                Mod = new OsuModRewindOnNotSS(),
                Autoplay = false,
                CreateBeatmap = () => new Beatmap
                {
                    HitObjects = new List<HitObject>
                    {
                        new HitCircle { StartTime = 3000, Position = new Vector2(256, 192) },
                    },
                },
                PassCondition = () =>
                {
                    double t = Player.GameplayClockContainer.CurrentTime;

                    if (t > 3000)
                        sawPass = true;
                    if (sawPass && t < 2600)
                        sawRewind = true;

                    return sawRewind && t > 2800 && !Player.GameplayState.HasFailed;
                },
            });
        }

        [Test]
        public void TestRewoundMissLeavesNoMissMarkBehind()
        {
            bool sawMark = false, rewound = false;

            CreateModTest(new ModTestData
            {
                Mod = new OsuModRewindOnMiss(),
                Autoplay = false,
                CreateBeatmap = () => new Beatmap
                {
                    HitObjects = new List<HitObject>
                    {
                        new HitCircle { StartTime = 3000, Position = new Vector2(256, 192) },
                    },
                },
                PassCondition = () =>
                {
                    double t = Player.GameplayClockContainer.CurrentTime;
                    int marks = Player.ChildrenOfType<DrawableOsuJudgement>().Count(j => j.JudgedHitObject != null);

                    if (marks > 0 && t > 3000)
                        sawMark = true;

                    if (sawMark && t < 2600)
                        rewound = true;

                    // Before the circle comes around again, its miss has been undone, so nothing can still be waiting to show it.
                    return rewound && t < 2900 && marks == 0;
                },
            });
        }

        [Test]
        public void TestEscStillPausesInARewindLoop()
        {
            bool sawMiss = false, rewinding = false;
            Stopwatch? sinceResume = null;

            CreateModTest(new ModTestData
            {
                Mod = new OsuModRewindOnMiss(),
                Autoplay = false, // no input -> misses the circle every time, so it rewinds forever
                CreateBeatmap = () => new Beatmap
                {
                    HitObjects = new List<HitObject>
                    {
                        new HitCircle { StartTime = 3000, Position = new Vector2(256, 192) },
                    },
                },
                // The steps below run before this is checked.
                PassCondition = () => true,
            });

            AddUntilStep("missed, and rewinding", () =>
            {
                double t = Player.GameplayClockContainer.CurrentTime;

                if (t > 3000)
                    sawMiss = true;
                if (sawMiss && t < 2900)
                    rewinding = true;

                return rewinding;
            });
            double pausedAt = 0;

            AddAssert("can pause during the rewind", () =>
            {
                pausedAt = Player.GameplayClockContainer.CurrentTime;
                return Player.Pause();
            });
            AddStep("resume", () =>
            {
                Player.GameplayClockContainer.Start();
                sinceResume = Stopwatch.StartNew();
            });
            AddUntilStep("a real second has passed", () => sinceResume!.ElapsedMilliseconds > 1200);
            // Rewinds keep song time from getting a second past the first pause, which is what used to block Esc.
            AddAssert("song time still within a second of the first pause", () => Player.GameplayClockContainer.CurrentTime < pausedAt + 1000);
            AddAssert("can pause again", () => Player.Pause());
        }

        [Test]
        public void TestHitAfterResumingFromAPauseInARewind()
        {
            bool sawMiss = false, rewinding = false;

            CreateModTest(new ModTestData
            {
                Mod = new OsuModRewindOnMiss(),
                Autoplay = false,
                CreateBeatmap = () => new Beatmap
                {
                    HitObjects = new List<HitObject>
                    {
                        new HitCircle { StartTime = 3000, Position = new Vector2(256, 192) },
                    },
                },
                // The steps below run before this is checked.
                PassCondition = () => true,
            });

            AddUntilStep("missed, and rewinding", () =>
            {
                double t = Player.GameplayClockContainer.CurrentTime;

                if (t > 3000)
                    sawMiss = true;
                if (sawMiss && t < 2900)
                    rewinding = true;

                return rewinding;
            });

            // The orange circle only appears when the cursor is over the playfield.
            AddStep("cursor over the playfield", () => InputManager.MoveMouseTo(Player.DrawableRuleset.Playfield.ToScreenSpace(new Vector2(100, 100))));
            AddStep("pause", () => Player.Pause());
            AddStep("resume", () => Player.Resume());
            AddUntilStep("orange circle shown", () => Player.ChildrenOfType<OsuResumeOverlay>().Single().State.Value == Visibility.Visible);
            AddStep("point at the orange circle", () =>
                InputManager.MoveMouseTo(Player.ChildrenOfType<OsuResumeOverlay.OsuClickToResumeCursor>().Single().ScreenSpaceDrawQuad.Centre + new Vector2(1)));
            AddStep("click it", () =>
            {
                InputManager.PressKey(Key.Z);
                InputManager.ReleaseKey(Key.Z);
            });
            AddUntilStep("playing again", () => !Player.GameplayClockContainer.IsPaused.Value);
            AddAssert("game isn't waiting to ignore a press", () =>
                !Player.ChildrenOfType<OsuResumeOverlay.OsuResumeOverlayInputBlocker>().Single().BlockNextPress);

            AddStep("aim at the circle", () => InputManager.MoveMouseTo(Player.DrawableRuleset.Playfield.ToScreenSpace(new Vector2(256, 192))));

            // Test steps only run every so often, too coarse to hit a circle on time, so check every frame instead.
            ScheduledDelegate? hitter = null;
            bool pressed = false;

            AddStep("hit it when it's due", () => hitter = Scheduler.AddDelayed(() =>
            {
                double t = Player.GameplayClockContainer.CurrentTime;

                if (pressed || !Player.GameplayClockContainer.IsRunning || t < 2990 || t > 3040)
                    return;

                pressed = true;
                InputManager.PressKey(Key.Z);
                InputManager.ReleaseKey(Key.Z);
            }, 0, true));

            AddUntilStep("pressed", () => pressed);
            AddUntilStep("the hit counted", () => Player.ScoreProcessor.HitEvents.Any(e => e.Result.IsHit()));
            AddStep("stop", () => hitter?.Cancel());
        }

        [Test]
        public void TestMissRewindsThenResumes()
        {
            bool sawMiss = false, sawRewind = false;

            CreateModTest(new ModTestData
            {
                Mod = new OsuModRewindOnMiss(),
                Autoplay = false, // no input at all -> the circle is missed
                CreateBeatmap = () => new Beatmap
                {
                    HitObjects = new List<HitObject>
                    {
                        new HitCircle { StartTime = 3000, Position = new Vector2(256, 192) },
                    },
                },
                PassCondition = () =>
                {
                    double t = Player.GameplayClockContainer.CurrentTime;

                    if (!sawMiss && t > 3000)
                        sawMiss = true;

                    // missed at 3000 -> rewinds to 2500 (half a second before the circle)
                    if (sawMiss && t < 2600)
                        sawRewind = true;

                    // then the music has to start moving forwards again, without failing
                    return sawRewind && t > 2800 && !Player.GameplayState.HasFailed;
                },
            });
        }
    }
}
