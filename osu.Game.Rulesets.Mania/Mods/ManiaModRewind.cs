// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Input.Bindings;
using osu.Framework.Logging;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Mania.Mods
{
    public abstract class ManiaModRewind : ModRewind<ManiaHitObject, ManiaAction>
    {
        [SettingSource("Rewind on stray presses", "Pressing a key when there's no note to hit counts as a miss.")]
        public BindableBool RewindOnStrayPresses { get; } = new BindableBool(true);

        protected override bool CountStrayPresses => RewindOnStrayPresses.Value;

        [SettingSource("Rewind on held keys", "Holding a key too long after a normal note, or letting go of a long note more than 0.1 s late, counts as a miss.")]
        public BindableBool RewindOnHeldKeys { get; } = new BindableBool(true);

        [SettingSource("Normal note hold limit", "How many seconds a key may stay down after a normal note before it counts as a miss.")]
        public BindableDouble NormalNoteHoldSeconds { get; } = new BindableDouble(0.3)
        {
            MinValue = 0.1,
            MaxValue = 1,
            Precision = 0.05,
        };

        protected override bool CountHeldKeys => RewindOnHeldKeys.Value;

        protected override double NormalNoteHoldLimit => NormalNoteHoldSeconds.Value * 1000;

        [SettingSource("Rewind on split chords", "Notes meant to be pressed together count as a miss if they're pressed further apart than the chord limit, and so do long notes meant to be let go of together.")]
        public BindableBool RewindOnSplitChords { get; } = new BindableBool(true);

        [SettingSource("Chord limit", "How many milliseconds apart the notes of a chord may be pressed. Long notes may be let go of 1.5 times as far apart, as the game allows for releases.")]
        public BindableInt ChordLimitMilliseconds { get; } = new BindableInt(40)
        {
            MinValue = 10,
            MaxValue = 150,
            Precision = 5,
        };

        /// <summary>
        /// The latest result of each note that's part of a chord. A result is reset rather than replaced when a rewind undoes it.
        /// </summary>
        private readonly Dictionary<HitObject, JudgementResult> chordResults = new Dictionary<HitObject, JudgementResult>();

        private Dictionary<long, List<HitObject>>? pressChords;
        private Dictionary<long, List<HitObject>>? releaseChords;

        /// <summary>
        /// Whether this result finishes a chord whose notes were pressed, or (for long notes ending together) let go of, too far apart.
        /// Each note on its own can still be a hit, so the game doesn't notice notes rolled one after another.
        /// </summary>
        protected bool ChordSplit(JudgementResult result)
        {
            if (!RewindOnSplitChords.Value || !result.IsHit || AppliedRuleset == null || result.HitObject is not Note note)
                return false;

            buildChords(AppliedRuleset.Objects.OfType<ManiaHitObject>());

            bool release = note is TailNote;

            if (!(release ? releaseChords! : pressChords!).TryGetValue(chordKey(note.StartTime), out var chord))
                return false;

            chordResults[note] = result;

            double first = double.MaxValue, last = double.MinValue;

            foreach (var member in chord)
            {
                // Wait for every note of the chord; one that's missed rewinds anyway.
                if (!chordResults.TryGetValue(member, out var memberResult) || !memberResult.HasResult || !memberResult.IsHit)
                    return false;

                first = Math.Min(first, memberResult.TimeAbsolute);
                last = Math.Max(last, memberResult.TimeAbsolute);
            }

            double limit = ChordLimitMilliseconds.Value * (release ? TailNote.RELEASE_WINDOW_LENIENCE : 1);

            if (last - first <= limit)
                return false;

            Logger.Log($"[rewind] {(release ? "long notes let go of" : "chord pressed")} {last - first:N0} ms apart (limit {limit:N0}) at {note.StartTime:N0}: "
                       + string.Join(", ", chord.Select(m => $"column {((ManiaHitObject)m).Column + 1} at {chordResults[m].TimeAbsolute:N0}")));
            return true;
        }

        private static long chordKey(double time) => (long)Math.Round(time);

        private void buildChords(IEnumerable<ManiaHitObject> objects)
        {
            if (pressChords != null)
                return;

            var presses = new Dictionary<long, List<HitObject>>();
            var releases = new Dictionary<long, List<HitObject>>();

            void add(Dictionary<long, List<HitObject>> groups, HitObject hitObject)
            {
                long key = chordKey(hitObject.StartTime);

                if (!groups.TryGetValue(key, out var group))
                    groups[key] = group = new List<HitObject>();

                group.Add(hitObject);
            }

            foreach (var hitObject in objects)
            {
                switch (hitObject)
                {
                    case HoldNote holdNote:
                        add(presses, holdNote.Head);
                        add(releases, holdNote.Tail);
                        break;

                    case Note normalNote:
                        add(presses, normalNote);
                        break;
                }
            }

            pressChords = presses.Where(g => g.Value.Count > 1).ToDictionary(g => g.Key, g => g.Value);
            releaseChords = releases.Where(g => g.Value.Count > 1).ToDictionary(g => g.Key, g => g.Value);
        }

        protected override int? LaneOf(ManiaAction action, DrawableRuleset<ManiaHitObject> drawableRuleset)
            => ((ManiaPlayfield)drawableRuleset.Playfield).Stages.SelectMany(s => s.Columns).FirstOrDefault(c => c.Action.Value == action)?.Index;

        protected override int? LaneOf(HitObject hitObject) => (hitObject as ManiaHitObject)?.Column;

        /// <summary>
        /// Letting go of a hold note too early breaks the hold. The game records that on the hold's body as a bonus-style result,
        /// then only caps the end of the note at a 50, or misses it once the note is over, so on its own it rewinds late or never.
        /// </summary>
        protected static bool HoldBroken(JudgementResult result) => result.HitObject is HoldNoteBody && !result.IsHit;

        /// <summary>
        /// Lights animate on game time. Letting go of a key as the song rewinds schedules its "light off" at a moment
        /// the song has just gone back past, so it stayed lit until that moment came round again.
        /// This finishes those for the light under every key that isn't held, and for the glow classic-style skins add to the
        /// column while a long note is held (no long note can be held at the moment a rewind lands or resumes).
        /// </summary>
        protected override void OnRewindSettled(DrawableRuleset<ManiaHitObject> drawableRuleset)
        {
            var held = drawableRuleset.ChildrenOfType<KeyBindingContainer<ManiaAction>>().FirstOrDefault()?.PressedActions.ToHashSet()
                       ?? new HashSet<ManiaAction>();

            foreach (var column in drawableRuleset.Playfield.ChildrenOfType<Column>())
            {
                // Finishing a fade-out also removes the glow, so go through a copy.
                foreach (var glow in column.TopLevelContainer.Children.ToList())
                    glow.FinishTransforms(true);

                if (held.Contains(column.Action.Value))
                    continue;

                foreach (var keyArea in column.ChildrenOfType<Drawable>().Where(d => d is IKeyBindingHandler<ManiaAction> && d is not DrawableHitObject && d is not Column))
                    keyArea.FinishTransforms(true);
            }
        }
    }
}
