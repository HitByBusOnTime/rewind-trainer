// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Input.Bindings;
using osu.Framework.Testing;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.Mania.Skinning.Legacy;
using osu.Game.Skinning;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Rulesets.Objects.Drawables;
using osuTK.Input;

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
        public void TestLettingGoOfAHoldTooEarlyTriggers()
        {
            // Letting go too early breaks the hold, which the game records on the hold's body.
            Assert.That(new ManiaModRewindOnMiss().ShouldRewind(resultOf(new HoldNoteBody(), HitResult.IgnoreMiss)), Is.True);
            Assert.That(new ManiaModRewindOnNotSS().ShouldRewind(resultOf(new HoldNoteBody(), HitResult.IgnoreMiss)), Is.True);
        }

        [Test]
        public void TestHoldingToTheEndNeverTriggers()
        {
            Assert.That(new ManiaModRewindOnMiss().ShouldRewind(resultOf(new HoldNoteBody(), HitResult.IgnoreHit)), Is.False);
            Assert.That(new ManiaModRewindOnNotSS().ShouldRewind(resultOf(new HoldNoteBody(), HitResult.IgnoreHit)), Is.False);
        }

        [Resolved]
        private SkinManager skins { get; set; } = null!;

        // Classic-style skins light up a held note with a glow added to the column, not the note.
        [TestCase(false)]
        [TestCase(true)]
        public void TestLettingGoTooEarlyRewindsRightAway(bool classicSkin)
        {
            AddStep(classicSkin ? "classic skin" : "default skin", () =>
            {
                if (classicSkin)
                    skins.CurrentSkinInfo.Value = skins.DefaultClassicSkin.SkinInfo;
                else
                    skins.CurrentSkinInfo.SetDefault();
            });

            Key key = Key.Unknown;
            bool pressed = false, released = false, rewound = false;
            double furthest = 0;
            ScheduledDelegate? player = null;

            CreateModTest(new ModTestData
            {
                Mod = new ManiaModRewindOnMiss(),
                Autoplay = false,
                CreateBeatmap = () => new Beatmap { HitObjects = new List<HitObject> { new HoldNote { StartTime = 3000, Duration = 2000 } } },
                // The steps below run before this is checked.
                PassCondition = () => true,
            });

            // A test game uses the default keys for however many columns the map has.
            AddStep("find the key for the first column", () =>
            {
                int columns = ((ManiaPlayfield)Player.DrawableRuleset.Playfield).TotalColumns;
                var binding = new ManiaRuleset().GetDefaultKeyBindings(columns).First(b => b.GetAction<ManiaAction>() == ManiaAction.Key1);
                key = (Key)binding.KeyCombination.Keys.First();
            });

            // Test steps only run every so often, too coarse for timing, so this checks every frame.
            AddStep("hold the note, then let go far too early", () => player = Scheduler.AddDelayed(() =>
            {
                // A failed step leaves this running into the next test, after its player has gone.
                if (Player?.GameplayClockContainer == null || !Player.IsLoaded)
                    return;

                double t = Player.GameplayClockContainer.CurrentTime;

                if (!pressed && Player.GameplayClockContainer.IsRunning && t >= 2995)
                {
                    pressed = true;
                    InputManager.PressKey(key);
                }
                else if (pressed && !released && t >= 3400)
                {
                    released = true;
                    InputManager.ReleaseKey(key);
                }

                if (!rewound)
                {
                    furthest = Math.Max(furthest, t);
                    rewound = released && t < 2600;
                }
            }, 0, true));

            AddUntilStep("rewound", () => rewound);
            AddAssert("rewound right after letting go, not at the end of the note", () => furthest < 4000);

            // Nothing is held now, so no key light should be left waiting to turn on or off at a moment the song rewound past.
            // It lands half a second before the note, at 2500.
            Stopwatch? sinceLanding = null;
            AddUntilStep("landed", () => !Player.GameplayClockContainer.IsRunning && Player.DrawableRuleset.FrameStableClock.CurrentTime < 2525);
            // The rewind finishes easing in just after this, then there's the get-ready pause, so check part way into that pause.
            AddStep("start timing", () => sinceLanding = Stopwatch.StartNew());
            AddUntilStep("into the get-ready pause", () => sinceLanding!.ElapsedMilliseconds > 300);
            AddAssert("still paused before playing on", () => !Player.GameplayClockContainer.IsRunning);
            AddAssert("no key light stuck from before the rewind", () =>
            {
                double now = Player.DrawableRuleset.FrameStableClock.CurrentTime;

                var waiting = Player.DrawableRuleset.Playfield.ChildrenOfType<Column>()
                                    .SelectMany(c => c.ChildrenOfType<Drawable>().Where(d => d is IKeyBindingHandler<ManiaAction> && d is not DrawableHitObject && d is not Column))
                                    .SelectMany(a => a.ChildrenOfType<Drawable>().Prepend(a))
                                    .SelectMany(d => d.Transforms.Where(t => t.StartTime > now + 1).Select(t => $"{d.GetType().Name}.{t.TargetMember} at {t.StartTime:N0}"))
                                    .ToList();

                return waiting.Count == 0;
            });

            AddAssert("no hold glow left on", () => !Player.DrawableRuleset.Playfield.ChildrenOfType<Column>()
                                                            .Any(c => c.TopLevelContainer.Children.Any(d => d is HitTargetInsetContainer && d.Alpha > 0.01f)));

            AddStep("stop", () => player?.Cancel());
            AddStep("back to the default skin", () => skins.CurrentSkinInfo.SetDefault());
        }

        [Test]
        public void TestNeverLandsInsideAHold()
        {
            // Half a second before the missed note is in the middle of a hold in another column.
            var hold = new HoldNote { StartTime = 3000, Duration = 2000 };
            hold.ApplyDefaults(new osu.Game.Beatmaps.ControlPoints.ControlPointInfo(), new BeatmapDifficulty());
            var objects = new HitObject[] { hold, new Note { StartTime = 5200, Column = 1 } };

            Assert.That(ModRewind.GetRewindTarget(objects, 5200, 500), Is.LessThan(3000));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void TestStrayPress(bool rewindOnStrayPresses)
        {
            Key key = Key.Unknown;
            bool hitFirst = false, strayPressed = false, rewound = false;
            double furthest = double.NegativeInfinity, furthestAtRewind = 0;
            ScheduledDelegate? player = null;

            CreateModTest(new ModTestData
            {
                Mod = new ManiaModRewindOnMiss { RewindOnStrayPresses = { Value = rewindOnStrayPresses } },
                Autoplay = false,
                CreateBeatmap = () => new Beatmap
                {
                    HitObjects = new List<HitObject> { new Note { StartTime = 1000 }, new Note { StartTime = 4000 } },
                },
                // The steps below run before this is checked.
                PassCondition = () => true,
            });

            AddStep("find the key for the first column", () =>
            {
                int columns = ((ManiaPlayfield)Player.DrawableRuleset.Playfield).TotalColumns;
                var binding = new ManiaRuleset().GetDefaultKeyBindings(columns).First(b => b.GetAction<ManiaAction>() == ManiaAction.Key1);
                key = (Key)binding.KeyCombination.Keys.First();
            });

            // Hits the first note properly, then presses again at 2.5 s, when there's no note anywhere near.
            AddStep("play", () => player = Scheduler.AddDelayed(() =>
            {
                if (Player?.GameplayClockContainer == null || !Player.IsLoaded)
                    return;

                double t = Player.DrawableRuleset.FrameStableClock.CurrentTime;

                if (!hitFirst && Player.GameplayClockContainer.IsRunning && t >= 995)
                {
                    hitFirst = true;
                    InputManager.PressKey(key);
                    InputManager.ReleaseKey(key);
                }
                else if (hitFirst && !strayPressed && Player.GameplayClockContainer.IsRunning && t >= 2500)
                {
                    strayPressed = true;
                    InputManager.PressKey(key);
                    InputManager.ReleaseKey(key);
                }

                if (!rewound)
                {
                    furthest = Math.Max(furthest, t);

                    if (t < furthest - 50)
                    {
                        rewound = true;
                        furthestAtRewind = furthest;
                    }
                }
            }, 0, true));

            AddUntilStep("pressed for no reason", () => strayPressed);

            if (rewindOnStrayPresses)
            {
                AddUntilStep("rewound", () => rewound);
                AddAssert("for the stray press, not the proper hit or the later note", () => furthestAtRewind >= 2500 && furthestAtRewind < 3200);
            }
            else
            {
                AddUntilStep("played on past it", () => Player.DrawableRuleset.FrameStableClock.CurrentTime > 3500);
                AddAssert("never rewound", () => !rewound);
            }

            AddStep("stop", () => player?.Cancel());
        }

        [Test]
        public void TestMissRewindsThenResumes() => createRewindTest(new ManiaModRewindOnMiss(), new Note { StartTime = 3000 }, true);

        [Test]
        public void TestNotSSRewindsThenResumes() => createRewindTest(new ManiaModRewindOnNotSS(), new Note { StartTime = 3000 }, false);
    }
}
