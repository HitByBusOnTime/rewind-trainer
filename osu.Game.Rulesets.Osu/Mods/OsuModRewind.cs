// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Testing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Osu.Mods
{
    public abstract class OsuModRewind : ModRewind<OsuHitObject, OsuAction>
    {
        public override Type[] IncompatibleMods => base.IncompatibleMods.Append(typeof(OsuModAutopilot)).ToArray();

        // Moving the cursor and smoke are fine; only clicks could hit something.
        protected override bool IgnoreDuringRewind(OsuAction action) => action is OsuAction.LeftButton or OsuAction.RightButton;

        // After clicking the orange circle to resume, the game waits to ignore that click in gameplay, so it has to arrive.
        protected override bool LetPressThrough(DrawableRuleset<OsuHitObject> drawableRuleset)
            => drawableRuleset.Playfield.ChildrenOfType<OsuResumeOverlay.OsuResumeOverlayInputBlocker>().Any(b => b.BlockNextPress);
    }
}
