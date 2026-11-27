// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Configuration;
using osu.Game.Online.API;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Utils;

namespace osu.Game.Overlays.Mods
{
    /// <summary>
    /// Remembers the selected mods, with their settings, between launches, so they don't have to be picked again every time.
    /// </summary>
    public partial class SelectedModsMemory : Component
    {
        public const string FILENAME = "selected-mods.json";

        [Resolved]
        private Bindable<IReadOnlyList<Mod>> selectedMods { get; set; } = null!;

        [Resolved]
        private Bindable<RulesetInfo> ruleset { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

        private ModSettingChangeTracker? settingTracker;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            try
            {
                if (!selectedMods.Disabled && selectedMods.Value.Count == 0)
                {
                    var mods = Load(storage, ruleset.Value.CreateInstance(), shortName => rulesets.GetRuleset(shortName)?.CreateInstance());

                    if (mods.Count > 0)
                        selectedMods.Value = mods;
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Couldn't restore the mods selected last time");
            }

            selectedMods.BindValueChanged(mods =>
            {
                // A mod's settings change without the selection changing, so watch those too.
                settingTracker?.Dispose();
                settingTracker = new ModSettingChangeTracker(mods.NewValue) { SettingChanged = _ => save() };
                save();
            }, true);
        }

        private void save()
        {
            try
            {
                Save(storage, ruleset.Value.ShortName, selectedMods.Value);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Couldn't save the selected mods");
            }
        }

        public static void Save(Storage storage, string rulesetShortName, IEnumerable<Mod> mods)
        {
            var saved = new SavedMods { Ruleset = rulesetShortName, Mods = mods.Select(m => new APIMod(m)).ToList() };

            using (var stream = storage.CreateFileSafely(FILENAME))
            using (var writer = new StreamWriter(stream))
                writer.Write(JsonConvert.SerializeObject(saved));
        }

        /// <summary>
        /// The saved mods, made for <paramref name="current"/>. Mods saved in another mode are brought across the way changing mode does,
        /// and ones that aren't played by the player, like Autoplay, are left out.
        /// </summary>
        public static List<Mod> Load(Storage storage, Ruleset current, Func<string, Ruleset?> rulesetFor)
        {
            var mods = new List<Mod>();

            if (!storage.Exists(FILENAME))
                return mods;

            string json;

            using (var stream = storage.GetStream(FILENAME))
            using (var reader = new StreamReader(stream))
                json = reader.ReadToEnd();

            var saved = JsonConvert.DeserializeObject<SavedMods>(json);

            if (saved?.Mods == null)
                return mods;

            var savedRuleset = saved.Ruleset == current.ShortName ? current : rulesetFor(saved.Ruleset ?? string.Empty) ?? current;

            foreach (var apiMod in saved.Mods)
            {
                Mod mod;

                try
                {
                    mod = apiMod.ToMod(savedRuleset);
                }
                catch (Exception e)
                {
                    Logger.Log($"Couldn't restore the {apiMod.Acronym} mod: {e.Message}");
                    continue;
                }

                if (mod is UnknownMod || !mod.UserPlayable)
                    continue;

                if (savedRuleset.ShortName != current.ShortName)
                {
                    var converted = current.CreateModFromAcronym(mod.Acronym);

                    if (converted == null)
                        continue;

                    converted.CopyCommonSettingsFrom(mod);
                    mod = converted;
                }

                mods.Add(mod);
            }

            if (!ModUtils.CheckValidForGameplay(mods, out var invalid))
                mods.RemoveAll(m => invalid!.Contains(m));

            return mods;
        }

        protected override void Dispose(bool isDisposing)
        {
            settingTracker?.Dispose();
            base.Dispose(isDisposing);
        }

        private class SavedMods
        {
            [JsonProperty("ruleset")]
            public string? Ruleset { get; set; }

            [JsonProperty("mods")]
            public List<APIMod>? Mods { get; set; }
        }
    }
}
