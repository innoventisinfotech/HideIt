using NAudio.CoreAudioApi;

namespace HideIt.Services;

/// <summary>
/// Mutes/unmutes an app's audio sessions (the per-app entry in the Volume Mixer).
///
/// Apps like Chrome, Spotify and Discord play audio from a separate child process, so
/// the audio session's process id differs from the window's. We therefore mute every
/// session whose process is the window's process OR a descendant of it.
///
/// Sessions are reference-counted by their own process id so overlapping hides stay
/// balanced, and we only ever unmute sessions we muted (never a manual user mute).
/// </summary>
public sealed class AudioService
{
    private readonly Dictionary<uint, int> _refCount = new();
    private readonly object _gate = new();

    /// <summary>
    /// Mute the audio sessions belonging to <paramref name="windowPid"/> or its descendants.
    /// Returns the session process ids actually affected, for a later matching <see cref="Unmute"/>.
    /// </summary>
    public List<uint> Mute(uint windowPid)
    {
        var tree = Native.GetProcessTreePids(windowPid);
        var affected = new List<uint>();

        ForEachSession(session =>
        {
            uint spid = session.GetProcessID;
            if (!tree.Contains(spid)) return;

            affected.Add(spid);
            lock (_gate)
            {
                _refCount.TryGetValue(spid, out int count);
                _refCount[spid] = count + 1;
                if (count > 0) return; // already muted by us
            }
            session.SimpleAudioVolume.Mute = true;
        });

        return affected;
    }

    /// <summary>Unmute the session process ids returned by a previous <see cref="Mute"/>.</summary>
    public void Unmute(IEnumerable<uint> sessionPids)
    {
        var toUnmute = new HashSet<uint>();
        lock (_gate)
        {
            foreach (var spid in sessionPids)
            {
                if (!_refCount.TryGetValue(spid, out int count)) continue;
                if (count <= 1)
                {
                    _refCount.Remove(spid);
                    toUnmute.Add(spid);
                }
                else
                {
                    _refCount[spid] = count - 1;
                }
            }
        }

        if (toUnmute.Count > 0)
            ApplyUnmute(toUnmute);
    }

    /// <summary>Unmute everything we muted — belt-and-suspenders on exit / toggle-off / panic.</summary>
    public void UnmuteAll()
    {
        HashSet<uint> pids;
        lock (_gate)
        {
            pids = new HashSet<uint>(_refCount.Keys);
            _refCount.Clear();
        }
        if (pids.Count > 0)
            ApplyUnmute(pids);
    }

    private void ApplyUnmute(HashSet<uint> sessionPids) =>
        ForEachSession(session =>
        {
            if (sessionPids.Contains(session.GetProcessID))
                session.SimpleAudioVolume.Mute = false;
        });

    /// <summary>Run an action over every active render session on every active output device.</summary>
    private static void ForEachSession(Action<AudioSessionControl> action)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        try { action(sessions[i]); }
                        catch (Exception ex) { Logger.LogException("AudioSession action", ex); }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("EnumerateAudioEndPoints", ex);
        }
    }
}
