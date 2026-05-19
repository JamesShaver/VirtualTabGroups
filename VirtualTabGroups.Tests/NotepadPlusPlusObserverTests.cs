using System;
using System.Collections.Generic;
using VirtualTabGroups.Plugin;
using Xunit;

namespace VirtualTabGroups.Tests
{
    public class NotepadPlusPlusObserverTests
    {
        private sealed class FakeMessageBox : IMessageBoxProxy
        {
            public readonly List<(string Title, string Body, MessageBoxKind Kind)> Calls
                = new List<(string, string, MessageBoxKind)>();

            public void Show(string title, string body, MessageBoxKind kind)
                => Calls.Add((title, body, kind));
        }

        [Fact]
        public void OnRecoveredFromCorruptFile_ShowsWarningMessageBox()
        {
            var box = new FakeMessageBox();
            var obs = new NotepadPlusPlusObserver(box);

            obs.OnRecoveredFromCorruptFile(@"C:\backup.json");

            Assert.Single(box.Calls);
            Assert.Equal("Virtual Tab Groups", box.Calls[0].Title);
            Assert.Contains(@"C:\backup.json", box.Calls[0].Body);
            Assert.Equal(MessageBoxKind.Warning, box.Calls[0].Kind);
        }

        [Fact]
        public void OnFutureSchemaVersion_ShowsWarningWithBothVersions()
        {
            var box = new FakeMessageBox();
            var obs = new NotepadPlusPlusObserver(box);

            obs.OnFutureSchemaVersion(versionFound: 2, currentVersion: 1);

            Assert.Single(box.Calls);
            Assert.Contains("2", box.Calls[0].Body);
            Assert.Contains("1", box.Calls[0].Body);
            Assert.Equal(MessageBoxKind.Warning, box.Calls[0].Kind);
        }

        [Fact]
        public void OnSaveFailed_FirstCall_ShowsError()
        {
            var box = new FakeMessageBox();
            var obs = new NotepadPlusPlusObserver(box);

            obs.OnSaveFailed(new System.IO.IOException("disk full"));

            Assert.Single(box.Calls);
            Assert.Contains("disk full", box.Calls[0].Body);
            Assert.Equal(MessageBoxKind.Error, box.Calls[0].Kind);
        }

        [Fact]
        public void OnSaveFailed_RepeatedCalls_OnlyShowsOnce()
        {
            var box = new FakeMessageBox();
            var obs = new NotepadPlusPlusObserver(box);

            obs.OnSaveFailed(new System.IO.IOException("first"));
            obs.OnSaveFailed(new System.IO.IOException("second"));
            obs.OnSaveFailed(new System.IO.IOException("third"));

            Assert.Single(box.Calls);
            Assert.Contains("first", box.Calls[0].Body);
        }
    }
}
