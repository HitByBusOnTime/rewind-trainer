// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Localisation;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Taiko.Objects;

namespace osu.Game.Rulesets.Taiko.Mods
{
    public class TaikoModRewindOnMiss : ModRewind<TaikoHitObject, TaikoAction>
    {
        public override string Name => "Rewind on Miss";
        public override string Acronym => "RW";
        public override LocalisableString Description => "Rewinds half a second before a miss.";

        public override bool ShouldRewind(JudgementResult result) => IsMiss(result);
    }
}
