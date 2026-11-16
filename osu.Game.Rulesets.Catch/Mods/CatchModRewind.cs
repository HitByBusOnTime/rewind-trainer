// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.Catch.Objects;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Catch.Mods
{
    public abstract class CatchModRewind : ModRewind<CatchHitObject, CatchAction>
    {
        // The catcher can't move during the get-ready pause, so it may need to cross the screen first.
        public override double GracePeriod => 400;

        // Catching only depends on where the catcher is, so no key press can catch anything while time stands still.
        // Ignoring move keys only meant a key pressed during a rewind and held didn't move the catcher afterwards.
        protected override bool IgnoreDuringRewind(CatchAction action) => false;
    }
}
