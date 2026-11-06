// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Localisation;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Osu.Mods
{
    public class OsuModRewindOnMiss : OsuModRewind
    {
        public override string Name => "Rewind on Miss";
        public override string Acronym => "RW";
        public override LocalisableString Description => "Rewinds 1 second before a miss.";

        public override bool ShouldRewind(JudgementResult result) => !result.IsHit && result.Type.BreaksCombo();
    }
}
