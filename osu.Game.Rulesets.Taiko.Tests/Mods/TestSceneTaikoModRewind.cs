// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;
using osu.Game.Rulesets.Taiko.Mods;
using osu.Game.Rulesets.Taiko.Objects;

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
        public void TestMissRewindsThenResumes() => createRewindTest(new TaikoModRewindOnMiss(), new Hit { StartTime = 3000 }, true);

        [Test]
        public void TestNotSSRewindsThenResumes() => createRewindTest(new TaikoModRewindOnNotSS(), new Hit { StartTime = 3000 }, false);
    }
}
