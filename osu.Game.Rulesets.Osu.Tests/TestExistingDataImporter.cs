// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.IO;

namespace osu.Game.Rulesets.Osu.Tests
{
    [TestFixture]
    public class TestExistingDataImporter
    {
        private string root = null!;
        private string source = null!;
        private string destination = null!;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "rewind-import-test-" + Guid.NewGuid().ToString("N"));
            source = Path.Combine(root, "osu");
            destination = Path.Combine(root, "rewind-trainer");

            Directory.CreateDirectory(Path.Combine(source, "files", "a", "ab"));
            write("client.realm", "database");
            write("framework.ini", "framework");
            write("input.json", "input");
            write("game.ini", "Skin = my-skin\nUsername = someone\nToken = secret\nSaveUsername = True\nShowFirstRunSetup = False");
            write(Path.Combine("files", "a", "ab", "abcd"), "beatmap");
            write(".auth_startup", "auth");
            write("online.db", "cache");
        }

        [TearDown]
        public void TearDown() => Directory.Delete(root, true);

        private void write(string name, string content) => File.WriteAllText(Path.Combine(source, name), content);

        private string read(string name) => File.ReadAllText(Path.Combine(destination, name));

        [Test]
        public void TestSettingsSkinsAndMapsComeAcross()
        {
            Assert.That(ExistingDataImporter.Import(source, destination), Is.True);

            Assert.That(read("client.realm"), Is.EqualTo("database"));
            Assert.That(read("framework.ini"), Is.EqualTo("framework"));
            Assert.That(read("input.json"), Is.EqualTo("input"));
            Assert.That(read(Path.Combine("files", "a", "ab", "abcd")), Is.EqualTo("beatmap"));
            Assert.That(read("game.ini"), Does.Contain("Skin = my-skin").And.Contain("ShowFirstRunSetup = False"));
        }

        [Test]
        public void TestLoginIsNotCopied()
        {
            ExistingDataImporter.Import(source, destination);

            string[] keys = File.ReadAllLines(Path.Combine(destination, "game.ini")).Select(l => l.Split('=')[0].Trim()).ToArray();

            Assert.That(keys, Does.Not.Contain("Username").And.Not.Contain("Token"));
            Assert.That(keys, Does.Contain("SaveUsername"));
            Assert.That(read("game.ini"), Does.Not.Contain("secret"));
            Assert.That(File.Exists(Path.Combine(destination, ".auth_startup")), Is.False);
        }

        [Test]
        public void TestMapsAreLinkedNotDuplicated()
        {
            ExistingDataImporter.Import(source, destination);

            // Only a link to the same data would show this in the original. The game itself never edits stored files.
            File.AppendAllText(Path.Combine(destination, "files", "a", "ab", "abcd"), "!");
            Assert.That(File.ReadAllText(Path.Combine(source, "files", "a", "ab", "abcd")), Is.EqualTo("beatmap!"));
        }

        [Test]
        public void TestRemovingCopyLeavesOriginalAlone()
        {
            ExistingDataImporter.Import(source, destination);

            File.Delete(Path.Combine(destination, "files", "a", "ab", "abcd"));
            File.WriteAllText(Path.Combine(destination, "client.realm"), "changed");

            Assert.That(File.ReadAllText(Path.Combine(source, "files", "a", "ab", "abcd")), Is.EqualTo("beatmap"));
            Assert.That(File.ReadAllText(Path.Combine(source, "client.realm")), Is.EqualTo("database"));
        }

        [Test]
        public void TestExistingDataIsNeverOverwritten()
        {
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "client.realm"), "already here");
            File.WriteAllText(Path.Combine(destination, "game.ini"), "mine");

            Assert.That(ExistingDataImporter.Import(source, destination), Is.False);

            Assert.That(read("client.realm"), Is.EqualTo("already here"));
            Assert.That(read("game.ini"), Is.EqualTo("mine"));
        }

        [Test]
        public void TestRunsOnlyOnce()
        {
            ExistingDataImporter.Import(source, destination);
            File.WriteAllText(Path.Combine(destination, "client.realm"), "played since");

            Assert.That(ExistingDataImporter.Import(source, destination), Is.False);
            Assert.That(read("client.realm"), Is.EqualTo("played since"));
        }

        [Test]
        public void TestNoExistingInstallDoesNothing()
        {
            Directory.Delete(source, true);

            Assert.That(ExistingDataImporter.Import(source, destination), Is.False);
            Assert.That(Directory.Exists(destination), Is.False);
        }

        [Test]
        public void TestInterruptedImportFinishesNextTime()
        {
            // A previous run stopped after linking some files, before the database.
            Directory.CreateDirectory(Path.Combine(destination, "files", "a", "ab"));
            File.WriteAllText(Path.Combine(destination, "files", "a", "ab", "abcd"), "beatmap");
            File.WriteAllText(Path.Combine(destination, "client.realm.importing"), "partial");

            Assert.That(ExistingDataImporter.Import(source, destination), Is.True);
            Assert.That(read("client.realm"), Is.EqualTo("database"));
        }

        [Test]
        public void TestMovedDataLocationIsFollowed()
        {
            string moved = Path.Combine(root, "moved");
            Directory.CreateDirectory(Path.Combine(moved, "files"));
            File.WriteAllText(Path.Combine(moved, "client.realm"), "moved database");
            File.WriteAllText(Path.Combine(moved, "files", "map"), "moved map");
            File.Delete(Path.Combine(source, "client.realm"));
            write("storage.ini", $"FullPath = {moved}");

            Assert.That(ExistingDataImporter.Import(source, destination), Is.True);
            Assert.That(read("client.realm"), Is.EqualTo("moved database"));
            Assert.That(read(Path.Combine("files", "map")), Is.EqualTo("moved map"));
            Assert.That(read("input.json"), Is.EqualTo("input"));
        }
    }
}
