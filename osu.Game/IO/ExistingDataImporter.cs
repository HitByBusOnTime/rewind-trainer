// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace osu.Game.IO
{
    /// <summary>
    /// Brings settings, key bindings, skins, beatmaps and scores across from an existing osu! install
    /// the first time Rewind Trainer runs, without ever writing to that install.
    /// </summary>
    public static class ExistingDataImporter
    {
        private const string database_filename = @"client.realm";

        private const string files_directory = @"files";

        private static readonly string[] plain_settings = { @"framework.ini", @"input.json" };

        private const string game_settings = @"game.ini";

        /// <summary>
        /// Settings which identify an account. This build is offline and has no use for them.
        /// </summary>
        private static readonly string[] private_settings = { @"Username", @"Token" };

        public static bool ShouldImport(string source, string destination)
            => File.Exists(Path.Combine(resolveDataDirectory(source), database_filename))
               && !File.Exists(Path.Combine(destination, database_filename));

        /// <summary>
        /// Performs the import, if there's anything to import and nothing already at the destination.
        /// The database goes last, so that an interrupted import simply runs again next time.
        /// </summary>
        /// <param name="source">The directory of the existing install.</param>
        /// <param name="destination">The directory of this game's data.</param>
        /// <returns>Whether anything was imported.</returns>
        public static bool Import(string source, string destination)
        {
            if (!ShouldImport(source, destination))
                return false;

            string data = resolveDataDirectory(source);

            Directory.CreateDirectory(destination);

            foreach (string name in plain_settings)
            {
                string path = Path.Combine(source, name);

                if (File.Exists(path))
                    File.Copy(path, Path.Combine(destination, name), true);
            }

            string gameSettings = Path.Combine(source, game_settings);

            if (File.Exists(gameSettings))
                File.WriteAllLines(Path.Combine(destination, game_settings), withoutPrivateSettings(File.ReadAllLines(gameSettings)));

            linkFiles(Path.Combine(data, files_directory), Path.Combine(destination, files_directory));

            string temporary = Path.Combine(destination, database_filename + @".importing");
            File.Copy(Path.Combine(data, database_filename), temporary, true);
            File.Move(temporary, Path.Combine(destination, database_filename));

            return true;
        }

        private static IEnumerable<string> withoutPrivateSettings(IEnumerable<string> lines)
            => lines.Where(line => !private_settings.Contains(line.Split('=')[0].Trim(), StringComparer.OrdinalIgnoreCase));

        /// <summary>
        /// Stored files are named by their content and never change, so they can be linked rather than duplicated.
        /// </summary>
        private static void linkFiles(string source, string destination)
        {
            if (!Directory.Exists(source))
                return;

            foreach (string file in Directory.EnumerateFiles(source, @"*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(destination, Path.GetRelativePath(source, file));

                if (File.Exists(target))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                if (!HardLinkHelper.TryCreateHardLink(target, file))
                    File.Copy(file, target);
            }
        }

        /// <summary>
        /// An install can have moved its data elsewhere, recorded in storage.ini. Settings always stay in the original directory.
        /// </summary>
        private static string resolveDataDirectory(string source)
        {
            string storageConfig = Path.Combine(source, @"storage.ini");

            if (!File.Exists(storageConfig))
                return source;

            foreach (string line in File.ReadAllLines(storageConfig))
            {
                string[] parts = line.Split('=', 2);

                if (parts.Length == 2 && parts[0].Trim().Equals(@"FullPath", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(parts[1]))
                    return parts[1].Trim();
            }

            return source;
        }
    }
}
