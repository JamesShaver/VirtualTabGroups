using System;
using System.Collections.Generic;

namespace VirtualTabGroups.Core
{
    /// <summary>
    /// Tracks which documents have unsaved changes ("dirty" state) and raises a
    /// change event so the UI can redraw only the affected rows.
    ///
    /// Key semantics:
    /// - A key is either an absolute saved path or an unsaved buffer name ("new 1").
    /// - <see cref="SetDirty"/> returns false when the value is unchanged — the
    ///   caller can skip persistence and UI work entirely (no-op updates).
    /// - Removed keys simply leave the cache; a missing key is clean by definition.
    /// </summary>
    public sealed class DirtyStateTracker
    {
        private readonly Dictionary<string, bool> _dirtyByKey
            = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Raised after a key's dirty value actually changes.</summary>
        public event Action<string, bool> Changed;

        /// <summary>
        /// Sets the dirty flag for a key. Returns true when the value changed
        /// (event raised), false when it was already in that state.
        /// </summary>
        public bool SetDirty(string key, bool isDirty)
        {
            if (string.IsNullOrEmpty(key)) return false;

            if (_dirtyByKey.TryGetValue(key, out var current))
            {
                if (current == isDirty) return false;
                _dirtyByKey[key] = isDirty;
            }
            else
            {
                if (!isDirty) return false; // missing key == clean; nothing to remember
                _dirtyByKey[key] = true;
            }

            Changed?.Invoke(key, isDirty);
            return true;
        }

        /// <summary>True when the key is known to be dirty; otherwise false.</summary>
        public bool IsDirty(string key)
        {
            return !string.IsNullOrEmpty(key)
                && _dirtyByKey.TryGetValue(key, out var dirty)
                && dirty;
        }

        /// <summary>Number of currently dirty tracked documents.</summary>
        public int DirtyCount
        {
            get
            {
                int count = 0;
                foreach (var kv in _dirtyByKey)
                    if (kv.Value) count++;
                return count;
            }
        }
    }
}
