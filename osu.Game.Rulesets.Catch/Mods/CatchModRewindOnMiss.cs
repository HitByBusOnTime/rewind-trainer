// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Localisation;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Catch.Mods
{
    public class CatchModRewindOnMiss : CatchModRewind
    {
        public override string Name => "Rewind on Miss";
        public override string Acronym => "RW";
        public override LocalisableString Description => "Rewinds half a second before a miss.";

        // Tiny droplets don't count as misses in catch, only as lost accuracy, so those are left to Rewind on Not SS.
        public override bool ShouldRewind(JudgementResult result) => IsMiss(result) && result.Judgement.MaxResult != HitResult.SmallTickHit;
    }
}
