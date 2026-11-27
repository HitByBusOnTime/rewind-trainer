// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Localisation;
using osu.Game.Rulesets.Judgements;

namespace osu.Game.Rulesets.Mania.Mods
{
    public class ManiaModRewindOnNotSS : ManiaModRewind
    {
        public override string Name => "Rewind on Not SS";
        public override string Acronym => "RS";
        public override LocalisableString Description => "Rewinds half a second before anything that isn't perfect.";

        public override bool ShouldRewind(JudgementResult result) => HoldBroken(result) || CostsSS(result) || ChordSplit(result);
    }
}
