// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
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

        /// <summary>
        /// How long the get-ready pause has left, or null outside it.
        /// </summary>
        private double? pauseTimeLeft()
        {
            var overlay = Player.DrawableRuleset.Overlays.Children.FirstOrDefault(d => d.GetType().Name.StartsWith("RewindOverlay", StringComparison.Ordinal));

            if (overlay == null)
                return null;

            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var type = overlay.GetType();

            if (type.GetField("phase", flags)!.GetValue(overlay)!.ToString() != "Holding")
                return null;

            return (double)type.GetField("holdLength", flags)!.GetValue(overlay)! - (double)type.GetField("elapsed", flags)!.GetValue(overlay)!;
        }

        [Test]
        public void TestPressJustBeforeResumeHitsTheNote()
        {
            // A stream long enough that there's no room to land away from a note anywhere near, so the rewind lands just before one.
            const double skipped = 8000;
            var noteTimes = new List<double>();
            for (double t = 1000; t <= 9000; t += 100)
                noteTimes.Add(t);

            Key key = Key.Unknown;
            var pressed = new HashSet<double>();
            bool rewoundOnce = false, pressedInPause = false, resumed = false, rewoundAgain = false;
            double highest = double.NegativeInfinity, landing = double.MaxValue, furthest = double.NegativeInfinity, pausePressAt = double.NaN;
            ScheduledDelegate? player = null;

            CreateModTest(new ModTestData
            {
                // Slowed down so the test's per-frame presses land on every note: frames in a test can be far apart in song time.
                Mods = new Mod[] { new ManiaModRewindOnMiss(), new ManiaModHalfTime { SpeedChange = { Value = 0.5 } } },
                Autoplay = false,
                CreateBeatmap = () => new Beatmap { HitObjects = noteTimes.Select(t => (HitObject)new Note { StartTime = t }).ToList() },
                // The steps below run before this is checked.
                PassCondition = () => true,
            });

            AddStep("find the key for the first column", () =>
            {
                int columns = ((ManiaPlayfield)Player.DrawableRuleset.Playfield).TotalColumns;
                var binding = new ManiaRuleset().GetDefaultKeyBindings(columns).First(b => b.GetAction<ManiaAction>() == ManiaAction.Key1);
                key = (Key)binding.KeyCombination.Keys.First();
            });

            // Hits every note on time apart from one. In the get-ready pause after the rewind it presses once, a moment before
            // play resumes, for the note sitting on the line, the way someone times it by the closing ring. Then it plays on.
            AddStep("play", () => player = Scheduler.AddDelayed(() =>
            {
                if (Player?.GameplayClockContainer == null || !Player.IsLoaded)
                    return;

                double t = Player.DrawableRuleset.FrameStableClock.CurrentTime;
                bool running = Player.GameplayClockContainer.IsRunning;

                if (!rewoundOnce)
                {
                    // A rewind steps back a little each frame, so compare with the furthest point rather than the last frame.
                    highest = Math.Max(highest, t);

                    if (t < highest - 50)
                        rewoundOnce = true;
                }
                else if (!resumed)
                {
                    if (running)
                    {
                        resumed = true;
                        furthest = t;
                    }
                    else
                    {
                        landing = Math.Min(landing, t);

                        // The rewind undid everything after where it lands, so those notes get played again.
                        if (!pressedInPause)
                            pressed.RemoveWhere(n => n > t);

                        if (!pressedInPause && pauseTimeLeft() is double left && left < 80)
                        {
                            pressedInPause = true;
                            pausePressAt = t;
                            pressed.Add(noteTimes.First(n => n > t));
                            InputManager.PressKey(key);
                            InputManager.ReleaseKey(key);
                        }

                        return;
                    }
                }
                else
                {
                    furthest = Math.Max(furthest, t);

                    if (!running || t < furthest - 50)
                        rewoundAgain = true;
                }

                if (!running || rewoundAgain)
                    return;

                foreach (double note in noteTimes)
                {
                    if (note != skipped && !pressed.Contains(note) && t >= note - 8 && t < note + 70)
                    {
                        pressed.Add(note);
                        InputManager.PressKey(key);
                        InputManager.ReleaseKey(key);
                    }
                }
            }, 0, true));

            // Each step may only wait so long, and at half speed the skipped note is a while away.
            foreach (double milestone in new double[] { 1500, 3000, 4500, 6000, 7500 })
                AddUntilStep($"past {milestone}", () => rewoundOnce || Player.DrawableRuleset.FrameStableClock.CurrentTime > milestone);
            AddUntilStep("rewound for the skipped note", () => rewoundOnce);
            AddUntilStep("resumed", () => resumed);
            AddUntilStep("played on a while", () => rewoundAgain || furthest > 7800);
            AddAssert("the note on the line was hit by the early press", () =>
            {
                TestContext.Progress.WriteLine($"EARLY landed {landing:N0}, pressed in the pause at {pausePressAt:N0}, then got to {furthest:N0}, rewound again: {rewoundAgain}");
                return pressedInPause && !rewoundAgain;
            });
            AddStep("stop", () => player?.Cancel());
        }

        /// <summary>
        /// Presses for the first object and lets go at <paramref name="releaseAt"/>, then reports whether play rewound before 3.5 s.
        /// </summary>
        private void heldKeyTest(bool rewindOnHeldKeys, HitObject first, double releaseAt, bool expectRewind, double notBefore = 300, double notAfter = 700)
        {
            Key key = Key.Unknown;
            bool pressed = false, released = false, rewound = false;
            double furthest = double.NegativeInfinity, furthestAtRewind = 0, pressedAt = 0, releasedAt = 0;
            ScheduledDelegate? player = null;

            CreateModTest(new ModTestData
            {
                // Slowed down so frames are close together in song time: the timings checked here are tight.
                Mods = new Mod[] { new ManiaModRewindOnMiss { RewindOnHeldKeys = { Value = rewindOnHeldKeys } }, new ManiaModHalfTime { SpeedChange = { Value = 0.5 } } },
                Autoplay = false,
                CreateBeatmap = () => new Beatmap { HitObjects = new List<HitObject> { first, new Note { StartTime = 5000 } } },
                // The steps below run before this is checked.
                PassCondition = () => true,
            });

            AddStep("find the key for the first column", () =>
            {
                int columns = ((ManiaPlayfield)Player.DrawableRuleset.Playfield).TotalColumns;
                var binding = new ManiaRuleset().GetDefaultKeyBindings(columns).First(b => b.GetAction<ManiaAction>() == ManiaAction.Key1);
                key = (Key)binding.KeyCombination.Keys.First();
            });

            AddStep("play", () => player = Scheduler.AddDelayed(() =>
            {
                if (Player?.GameplayClockContainer == null || !Player.IsLoaded)
                    return;

                double t = Player.DrawableRuleset.FrameStableClock.CurrentTime;

                if (!pressed && Player.GameplayClockContainer.IsRunning && t >= first.StartTime - 5)
                {
                    pressed = true;
                    pressedAt = t;
                    InputManager.PressKey(key);
                }
                else if (pressed && !released && (rewound || t >= releaseAt))
                {
                    released = true;
                    releasedAt = t;
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

            AddUntilStep("pressed", () => pressed);

            if (expectRewind)
            {
                AddUntilStep("rewound", () => rewound);
                AddAssert("soon after the key should have been let go", () =>
                {
                    TestContext.Progress.WriteLine($"HELD pressed at {pressedAt:N0}, rewound at {furthestAtRewind:N0}");
                    return furthestAtRewind > first.GetEndTime() + notBefore && furthestAtRewind < first.GetEndTime() + notAfter;
                });
            }
            else
            {
                AddUntilStep("played on", () => rewound || Player.DrawableRuleset.FrameStableClock.CurrentTime > 3500);
                AddAssert("never rewound", () =>
                {
                    TestContext.Progress.WriteLine($"HELD pressed at {pressedAt:N0}, let go at {releasedAt:N0}, rewound at {(rewound ? furthestAtRewind : 0):N0}");
                    return !rewound;
                });
            }

            AddStep("let go", () =>
            {
                player?.Cancel();
                InputManager.ReleaseKey(key);
            });
        }

        [Test]
        public void TestHoldingANoteTooLongRewinds() => heldKeyTest(true, new Note { StartTime = 1000 }, 3000, true, 250, 400);

        // Released before the 0.3 s limit, about as long as the player's longer taps.
        [Test]
        public void TestANormalHoldIsFine() => heldKeyTest(true, new Note { StartTime = 1000 }, 1220, false);

        // A quick tap is fine.
        [Test]
        public void TestQuickTapIsFine() => heldKeyTest(true, new Note { StartTime = 1000 }, 1050, false);

        [Test]
        public void TestHeldKeysCanBeTurnedOff() => heldKeyTest(false, new Note { StartTime = 1000 }, 3000, false);

        [Test]
        public void TestHoldingALongNoteIsFine() => heldKeyTest(true, new HoldNote { StartTime = 1000, Duration = 1500 }, 2520, false);

        // The game itself only misses a long note's end about 0.2 s late; this rewinds at 0.1 s.
        [Test]
        public void TestHoldingPastTheEndOfALongNoteRewinds() => heldKeyTest(true, new HoldNote { StartTime = 1000, Duration = 1000 }, 3200, true, 50, 170);

        // A long note let go of a little late is fine.
        [Test]
        public void TestLettingGoOfALongNoteSlightlyLateIsFine() => heldKeyTest(true, new HoldNote { StartTime = 1000, Duration = 1000 }, 2030, false);

        /// <summary>
        /// Plays a two-note chord at 1 s (two long notes ending at 2 s when <paramref name="longNotes"/>), pressing the two keys
        /// <paramref name="pressGap"/> ms apart and letting go of long notes <paramref name="releaseGap"/> ms apart, with a 30 ms chord limit.
        /// The second key comes 30 ms late and the first one early by the rest of the gap: a frame can add up to about 40 ms to a press,
        /// and at half speed every press has to stay inside the halved hit windows, and every release within the 0.1 s late-release rule.
        /// </summary>
        private void chordTest(bool enabled, bool longNotes, double pressGap, double releaseGap, bool expectRewind)
        {
            Key[] keys = new Key[2];
            bool[] pressed = new bool[2], released = new bool[2];
            double[] pressedAt = new double[2], releasedAt = new double[2];
            bool rewound = false;
            double furthest = double.NegativeInfinity, furthestAtRewind = 0;
            ScheduledDelegate? player = null;

            CreateModTest(new ModTestData
            {
                // Slowed down so frames are close together in song time.
                Mods = new Mod[]
                {
                    new ManiaModRewindOnMiss { RewindOnSplitChords = { Value = enabled }, ChordLimitMilliseconds = { Value = 30 } },
                    new ManiaModHalfTime { SpeedChange = { Value = 0.5 } },
                },
                Autoplay = false,
                CreateBeatmap = () => new Beatmap
                {
                    // The widest hit windows, so each note on its own is still a hit when the presses are far apart.
                    Difficulty = new BeatmapDifficulty { OverallDifficulty = 0 },
                    HitObjects = new List<HitObject>
                    {
                        longNotes ? new HoldNote { StartTime = 1000, Duration = 1000, Column = 0 } : new Note { StartTime = 1000, Column = 0 },
                        longNotes ? new HoldNote { StartTime = 1000, Duration = 1000, Column = 1 } : new Note { StartTime = 1000, Column = 1 },
                        new Note { StartTime = 5000, Column = 0 },
                    },
                },
                // The steps below run before this is checked.
                PassCondition = () => true,
            });

            AddStep("find the keys for the two columns", () =>
            {
                int columns = ((ManiaPlayfield)Player.DrawableRuleset.Playfield).TotalColumns;
                var bindings = new ManiaRuleset().GetDefaultKeyBindings(columns).ToList();
                keys[0] = (Key)bindings.First(b => b.GetAction<ManiaAction>() == ManiaAction.Key1).KeyCombination.Keys.First();
                keys[1] = (Key)bindings.First(b => b.GetAction<ManiaAction>() == ManiaAction.Key2).KeyCombination.Keys.First();
            });

            AddStep("play", () => player = Scheduler.AddDelayed(() =>
            {
                if (Player?.GameplayClockContainer == null || !Player.IsLoaded || rewound)
                    return;

                double t = Player.DrawableRuleset.FrameStableClock.CurrentTime;

                for (int i = 0; i < 2; i++)
                {
                    double pressAt = 1000 + (pressGap == 0 ? 0 : i == 0 ? 30 - pressGap : 30);
                    double releaseAt = longNotes ? 2000 + (releaseGap == 0 ? 0 : i == 0 ? 30 - releaseGap : 30) : pressAt + 30;

                    if (!pressed[i] && Player.GameplayClockContainer.IsRunning && t >= pressAt)
                    {
                        pressed[i] = true;
                        pressedAt[i] = t;
                        InputManager.PressKey(keys[i]);
                    }
                    else if (pressed[i] && !released[i] && t >= releaseAt)
                    {
                        released[i] = true;
                        releasedAt[i] = t;
                        InputManager.ReleaseKey(keys[i]);
                    }
                }

                furthest = Math.Max(furthest, t);

                if (t < furthest - 50)
                {
                    rewound = true;
                    furthestAtRewind = furthest;
                }
            }, 0, true));

            AddUntilStep("played the chord", () => rewound || Player.DrawableRuleset.FrameStableClock.CurrentTime > (longNotes ? 2500 : 1500));

            if (expectRewind)
            {
                AddUntilStep("rewound", () => rewound);
                AddAssert("for the chord", () =>
                {
                    TestContext.Progress.WriteLine($"CHORD pressed {pressedAt[0]:N0}/{pressedAt[1]:N0}, let go {releasedAt[0]:N0}/{releasedAt[1]:N0}, rewound at {furthestAtRewind:N0}");
                    double end = longNotes && releaseGap > 0 ? 2000 : 1000;
                    return furthestAtRewind > end && furthestAtRewind < end + 300;
                });
            }
            else
            {
                AddUntilStep("played on", () => rewound || Player.DrawableRuleset.FrameStableClock.CurrentTime > 3000);
                AddAssert("never rewound", () =>
                {
                    TestContext.Progress.WriteLine($"CHORD pressed {pressedAt[0]:N0}/{pressedAt[1]:N0}, let go {releasedAt[0]:N0}/{releasedAt[1]:N0}, rewound: {rewound}");
                    return !rewound;
                });
            }

            AddStep("let go", () =>
            {
                player?.Cancel();
                InputManager.ReleaseKey(keys[0]);
                InputManager.ReleaseKey(keys[1]);
            });
        }

        [Test]
        public void TestPressingAChordTogetherIsFine() => chordTest(true, false, 0, 0, false);

        [Test]
        public void TestSplittingAChordRewinds() => chordTest(true, false, 80, 0, true);

        [Test]
        public void TestSplitChordsCanBeTurnedOff() => chordTest(false, false, 80, 0, false);

        [Test]
        public void TestPressingLongNotesApartRewinds() => chordTest(true, true, 80, 0, true);

        [Test]
        public void TestLongNotesTogetherAreFine() => chordTest(true, true, 0, 0, false);

        [Test]
        public void TestLettingGoOfLongNotesApartRewinds() => chordTest(true, true, 0, 100, true);

        [Test]
        public void TestLettingGoOfLongNotesApartCanBeTurnedOff() => chordTest(false, true, 0, 100, false);

        // The player's rolled chords measured 50-70 ms apart; 60 ms halves the rewinds and still catches the clearly rolled ones.
        [Test]
        public void TestChordLimitIs60MillisecondsByDefault() => Assert.That(new ManiaModRewindOnMiss().ChordLimitMilliseconds.Value, Is.EqualTo(60));

        [Test]
        public void TestMissRewindsThenResumes() => createRewindTest(new ManiaModRewindOnMiss(), new Note { StartTime = 3000 }, true);

        [Test]
        public void TestNotSSRewindsThenResumes() => createRewindTest(new ManiaModRewindOnNotSS(), new Note { StartTime = 3000 }, false);
    }
}
