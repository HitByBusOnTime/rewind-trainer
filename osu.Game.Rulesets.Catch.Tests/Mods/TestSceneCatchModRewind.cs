// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Threading;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;
using osu.Game.Rulesets.Catch.Mods;
using osu.Game.Rulesets.Catch.Objects;
using osu.Game.Rulesets.Catch.UI;
using osuTK.Input;

namespace osu.Game.Rulesets.Catch.Tests.Mods
{
    public partial class TestSceneCatchModRewind : ModTestScene
    {
        protected override Ruleset CreatePlayerRuleset() => new CatchRuleset();

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

        [Test]
        public void TestTriggers()
        {
            var onMiss = new CatchModRewindOnMiss();
            var onNotSS = new CatchModRewindOnNotSS();

            Assert.That(onMiss.ShouldRewind(resultOf(new Fruit(), HitResult.Miss)), Is.True, "fruit");
            Assert.That(onMiss.ShouldRewind(resultOf(new Droplet(), HitResult.LargeTickMiss)), Is.True, "droplet");
            Assert.That(onMiss.ShouldRewind(resultOf(new Fruit(), HitResult.Great)), Is.False, "caught fruit");

            // Tiny droplets only cost accuracy in catch.
            Assert.That(onMiss.ShouldRewind(resultOf(new TinyDroplet(), HitResult.SmallTickMiss)), Is.False, "tiny droplet, on miss");
            Assert.That(onNotSS.ShouldRewind(resultOf(new TinyDroplet(), HitResult.SmallTickMiss)), Is.True, "tiny droplet, not SS");

            // Bananas are a bonus.
            Assert.That(onMiss.ShouldRewind(resultOf(new Banana(), HitResult.IgnoreMiss)), Is.False, "banana, on miss");
            Assert.That(onNotSS.ShouldRewind(resultOf(new Banana(), HitResult.IgnoreMiss)), Is.False, "banana, not SS");
        }

        [Test]
        public void TestMoveKeyHeldThroughARewindStillMoves()
        {
            float startX = 0;

            createRewindTest(new CatchModRewindOnMiss(), new Fruit { StartTime = 3000, X = 0 }, false);

            AddUntilStep("rewinding again", () => sawMiss && Player.GameplayClockContainer.CurrentTime > 3000);
            AddUntilStep("back at the start of the rewind", () => Player.GameplayClockContainer.CurrentTime < 2600);
            AddStep("press and hold left", () => InputManager.PressKey(Key.Left));
            AddUntilStep("playing again", () => Player.GameplayClockContainer.IsRunning && Player.GameplayClockContainer.CurrentTime > 2550);
            AddStep("note where the catcher is", () => startX = Player.ChildrenOfType<Catcher>().First().X);
            AddUntilStep("the catcher moves left", () => Player.ChildrenOfType<Catcher>().First().X < startX - 20);
            AddStep("let go", () => InputManager.ReleaseKey(Key.Left));
        }

        [Test]
        public void TestCatcherGoesBackToWhereItWas()
        {
            bool moved = false, pastMiss = false;
            ScheduledDelegate? player = null;

            CreateModTest(new ModTestData
            {
                Mod = new CatchModRewindOnMiss(),
                Autoplay = false,
                // Fruit right where the catcher starts, then one far to the left. The rewind for that one lands at 2.5 s.
                CreateBeatmap = () => new Beatmap
                {
                    HitObjects = new List<HitObject>
                    {
                        new Fruit { StartTime = 1000, X = CatchPlayfield.CENTER_X },
                        new Fruit { StartTime = 1600, X = CatchPlayfield.CENTER_X },
                        new Fruit { StartTime = 2200, X = CatchPlayfield.CENTER_X },
                        new Fruit { StartTime = 3000, X = 0 },
                    },
                },
                // The steps below run before this is checked.
                PassCondition = () => true,
            });

            // After where the rewind will land, the catcher goes to the far right, so the fruit on the left is missed.
            AddStep("play", () => player = Scheduler.AddDelayed(() =>
            {
                if (Player?.GameplayClockContainer == null || !Player.IsLoaded)
                    return;

                double t = Player.DrawableRuleset.FrameStableClock.CurrentTime;

                if (!moved && Player.GameplayClockContainer.IsRunning && t > 2700)
                {
                    moved = true;
                    Player.ChildrenOfType<CatcherArea>().First().SetCatcherPosition(CatchPlayfield.WIDTH);
                }

                if (t > 3000)
                    pastMiss = true;
            }, 0, true));

            AddUntilStep("moved away", () => moved);
            AddUntilStep("missed and rewound", () => pastMiss && Player.DrawableRuleset.FrameStableClock.CurrentTime < 2600);
            AddUntilStep("the catcher is back where it was then", () => Math.Abs(Player.ChildrenOfType<Catcher>().First().X - CatchPlayfield.CENTER_X) < 5);
            AddStep("stop", () => player?.Cancel());
        }

        // Far to the side, away from where the catcher starts.
        [Test]
        public void TestMissRewindsThenResumes() => createRewindTest(new CatchModRewindOnMiss(), new Fruit { StartTime = 3000, X = 0 }, false);

        [Test]
        public void TestNotSSRewindsThenResumes() => createRewindTest(new CatchModRewindOnNotSS(), new Fruit { StartTime = 3000, X = 0 }, false);
    }
}
