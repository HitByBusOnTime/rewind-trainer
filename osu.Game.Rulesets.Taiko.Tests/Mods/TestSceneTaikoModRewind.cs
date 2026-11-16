// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
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

        [Test]
        public void TestMissRewindsThenResumes() => createRewindTest(new TaikoModRewindOnMiss(), new Hit { StartTime = 3000 }, true);

        [Test]
        public void TestNotSSRewindsThenResumes() => createRewindTest(new TaikoModRewindOnNotSS(), new Hit { StartTime = 3000 }, false);
    }
}
