// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Input.Bindings;
using osu.Framework.Testing;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Mania.Mods
{
    public abstract class ManiaModRewind : ModRewind<ManiaHitObject, ManiaAction>
    {
        /// <summary>
        /// Letting go of a hold note too early breaks the hold. The game records that on the hold's body as a bonus-style result,
        /// then only caps the end of the note at a 50, or misses it once the note is over, so on its own it rewinds late or never.
        /// </summary>
        protected static bool HoldBroken(JudgementResult result) => result.HitObject is HoldNoteBody && !result.IsHit;

        /// <summary>
        /// The light under each column animates on game time. Letting go of a key as the song rewinds schedules its
        /// "light off" at a moment the song has just gone back past, so the key stayed lit until that moment came round again.
        /// Finish those for every key that isn't held, so the lights match what's actually pressed.
        /// </summary>
        protected override void OnRewindSettled(DrawableRuleset<ManiaHitObject> drawableRuleset)
        {
            var held = drawableRuleset.ChildrenOfType<KeyBindingContainer<ManiaAction>>().FirstOrDefault()?.PressedActions.ToHashSet()
                       ?? new HashSet<ManiaAction>();

            foreach (var column in drawableRuleset.Playfield.ChildrenOfType<Column>())
            {
                if (held.Contains(column.Action.Value))
                    continue;

                foreach (var keyArea in column.ChildrenOfType<Drawable>().Where(d => d is IKeyBindingHandler<ManiaAction> && d is not DrawableHitObject && d is not Column))
                    keyArea.FinishTransforms(true);
            }
        }
    }
}
