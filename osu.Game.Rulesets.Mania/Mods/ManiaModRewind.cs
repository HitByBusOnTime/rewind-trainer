// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Mania.Mods
{
    public abstract class ManiaModRewind : ModRewind<ManiaHitObject, ManiaAction>
    {
        /// <summary>
        /// Letting go of a hold note too early breaks the hold. The game records that on the hold's body as a bonus-style result,
        /// then only caps the end of the note at a 50, or misses it once the note is over, so on its own it rewinds late or never.
        /// </summary>
        protected static bool HoldBroken(JudgementResult result) => result.HitObject is HoldNoteBody && !result.IsHit;
    }
}
