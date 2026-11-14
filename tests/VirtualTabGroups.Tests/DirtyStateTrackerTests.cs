using System;
using VirtualTabGroups.Core;
using Xunit;

namespace VirtualTabGroups.Tests
{
    public class DirtyStateTrackerTests
    {
        [Fact]
        public void SetDirty_NewKeyTrue_ReturnsTrueAndRaisesEvent()
        {
            var tracker = new DirtyStateTracker();
            string eventKey = null;
            bool? eventValue = null;
            tracker.Changed += (k, d) => { eventKey = k; eventValue = d; };

            bool changed = tracker.SetDirty(@"C:\docs\a.txt", true);

            Assert.True(changed);
            Assert.Equal(@"C:\docs\a.txt", eventKey);
            Assert.True(eventValue);
            Assert.True(tracker.IsDirty(@"C:\docs\a.txt"));
        }

        [Fact]
        public void SetDirty_SameValueTwice_ReturnsFalseSecondTime()
        {
            var tracker = new DirtyStateTracker();
            tracker.SetDirty(@"C:\docs\a.txt", true);
            int eventCount = 0;
            tracker.Changed += (k, d) => eventCount++;

            bool changed = tracker.SetDirty(@"C:\docs\a.txt", true);

            Assert.False(changed);
            Assert.Equal(0, eventCount);
        }

        [Fact]
        public void SetDirty_TransitionTrueThenFalse_RaisesBothEvents()
        {
            var tracker = new DirtyStateTracker();
            tracker.SetDirty(@"C:\docs\a.txt", true);
            bool? secondValue = null;
            tracker.Changed += (k, d) => secondValue = d;

            bool changed = tracker.SetDirty(@"C:\docs\a.txt", false);

            Assert.True(changed);
            Assert.False(secondValue);
            Assert.False(tracker.IsDirty(@"C:\docs\a.txt"));
        }

        [Fact]
        public void IsDirty_MissingKey_ReturnsFalse()
        {
            var tracker = new DirtyStateTracker();

            Assert.False(tracker.IsDirty(@"C:\docs\not-there.txt"));
            Assert.False(tracker.IsDirty(null));
            Assert.False(tracker.IsDirty(string.Empty));
        }

        [Fact]
        public void SetDirty_NullOrEmptyKey_ReturnsFalse_NoEvent()
        {
            var tracker = new DirtyStateTracker();
            int eventCount = 0;
            tracker.Changed += (k, d) => eventCount++;

            Assert.False(tracker.SetDirty(null, true));
            Assert.False(tracker.SetDirty(string.Empty, true));
            Assert.Equal(0, eventCount);
        }

        [Fact]
        public void SetDirty_CaseInsensitiveKeys_TreatedAsSameKey()
        {
            var tracker = new DirtyStateTracker();
            tracker.SetDirty(@"C:\Docs\A.TXT", true);

            Assert.True(tracker.IsDirty(@"C:\docs\a.txt"));
            Assert.False(tracker.SetDirty(@"C:\docs\a.txt", true));
        }

        [Fact]
        public void SetDirty_UnsavedBufferName_WorksLikePathKey()
        {
            var tracker = new DirtyStateTracker();
            tracker.SetDirty("новый 1", true);
            Assert.True(tracker.IsDirty("новый 1"));

            tracker.SetDirty("новый 1", false);
            Assert.False(tracker.IsDirty("новый 1"));
        }

        [Fact]
        public void DirtyCount_CountsOnlyDirtyKeys()
        {
            var tracker = new DirtyStateTracker();
            Assert.Equal(0, tracker.DirtyCount);

            tracker.SetDirty(@"C:\a.txt", true);
            tracker.SetDirty(@"C:\b.txt", true);
            tracker.SetDirty(@"C:\c.txt", false); // clean, not remembered

            Assert.Equal(2, tracker.DirtyCount);

            tracker.SetDirty(@"C:\b.txt", false);
            Assert.Equal(1, tracker.DirtyCount);
        }
    }
}
