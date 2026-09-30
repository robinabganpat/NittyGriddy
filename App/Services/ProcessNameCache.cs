using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace App.Services
{
    /// <summary>
    /// Looks up the program name that owns a window, from the system's process list.
    /// The list is read as a whole; no handle to any individual process is opened.
    /// </summary>
    public class ProcessNameCache
    {
        private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(1);

        private readonly Func<IReadOnlyDictionary<int, string>> _snapshot;
        private readonly Func<DateTime> _now;
        private IReadOnlyDictionary<int, string> _names = new Dictionary<int, string>();
        private DateTime _takenAt = DateTime.MinValue;

        public ProcessNameCache(Func<IReadOnlyDictionary<int, string>>? snapshot = null, Func<DateTime>? now = null)
        {
            _snapshot = snapshot ?? SystemSnapshot;
            _now = now ?? (() => DateTime.UtcNow);
        }

        /// <summary>
        /// Process name without ".exe", or an empty string if the process is not (or no longer) running
        /// </summary>
        public string GetName(int processId)
        {
            var age = _now() - _takenAt;

            // Process ids are reused, so the list is not kept for long. An id that is missing means a program
            // started since the list was read; re-read, but not on every call.
            if (age > MaxAge || (!_names.ContainsKey(processId) && age > MinRefreshInterval))
            {
                _names = _snapshot();
                _takenAt = _now();
            }

            return _names.TryGetValue(processId, out var name) ? name : string.Empty;
        }

        private static IReadOnlyDictionary<int, string> SystemSnapshot()
        {
            var names = new Dictionary<int, string>();

            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    names[process.Id] = process.ProcessName;
                }
                catch (InvalidOperationException)
                {
                    // The process exited while the list was being read
                }
                finally
                {
                    process.Dispose();
                }
            }

            return names;
        }
    }
}
