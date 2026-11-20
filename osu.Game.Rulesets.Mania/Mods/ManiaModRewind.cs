// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Input.Bindings;
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

        [SettingSource("Rewind on held keys", "Holding a key down when there's no long note to hold, or letting go of a long note more than 0.1 s late, counts as a miss.")]
        public BindableBool RewindOnHeldKeys { get; } = new BindableBool(true);

        protected override bool CountHeldKeys => RewindOnHeldKeys.Value;

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
