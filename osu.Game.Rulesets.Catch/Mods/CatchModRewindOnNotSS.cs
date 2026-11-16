// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Localisation;
using osu.Game.Rulesets.Judgements;

namespace osu.Game.Rulesets.Catch.Mods
{
    public class CatchModRewindOnNotSS : CatchModRewind
    {
        public override string Name => "Rewind on Not SS";
        public override string Acronym => "RS";
        public override LocalisableString Description => "Rewinds half a second before anything that isn't perfect.";

        public override bool ShouldRewind(JudgementResult result) => CostsSS(result);
    }
}
