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
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Objects;

namespace osu.Game.Rulesets.Mania.Tests.Mods
{
    public partial class TestSceneManiaModRewind : ModTestScene
    {
        protected override Ruleset CreatePlayerRuleset() => new ManiaRuleset();

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

        [TestCase(HitResult.Perfect, false, false)]
        [TestCase(HitResult.Great, false, true)]
        [TestCase(HitResult.Meh, false, true)]
        [TestCase(HitResult.Miss, true, true)]
        public void TestNoteTriggers(HitResult type, bool onMiss, bool onNotSS)
        {
            Assert.That(new ManiaModRewindOnMiss().ShouldRewind(resultOf(new Note(), type)), Is.EqualTo(onMiss));
            Assert.That(new ManiaModRewindOnNotSS().ShouldRewind(resultOf(new Note(), type)), Is.EqualTo(onNotSS));
        }

        [Test]
        public void TestLettingGoOfAHoldEarlyTriggers()
            => Assert.That(new ManiaModRewindOnMiss().ShouldRewind(resultOf(new TailNote(), HitResult.Miss)), Is.True);

        [Test]
        public void TestHoldBodyNeverTriggers()
            => Assert.That(new ManiaModRewindOnMiss().ShouldRewind(resultOf(new HoldNoteBody(), HitResult.IgnoreMiss)), Is.False);

        [Test]
        public void TestNeverLandsInsideAHold()
        {
            // Half a second before the missed note is in the middle of a hold in another column.
            var hold = new HoldNote { StartTime = 3000, Duration = 2000 };
            hold.ApplyDefaults(new osu.Game.Beatmaps.ControlPoints.ControlPointInfo(), new BeatmapDifficulty());
            var objects = new HitObject[] { hold, new Note { StartTime = 5200, Column = 1 } };

            Assert.That(ModRewind.GetRewindTarget(objects, 5200, 500), Is.LessThan(3000));
        }

        [Test]
        public void TestMissRewindsThenResumes() => createRewindTest(new ManiaModRewindOnMiss(), new Note { StartTime = 3000 }, true);

        [Test]
        public void TestNotSSRewindsThenResumes() => createRewindTest(new ManiaModRewindOnNotSS(), new Note { StartTime = 3000 }, false);
    }
}
