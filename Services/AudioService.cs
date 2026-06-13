using NAudio.CoreAudioApi;

namespace HideIt.Services;

/// <summary>
/// Mutes/unmutes a process's audio sessions (the per-app entry in the Volume Mixer).
/// Reference-counted so overlapping hides of the same process stay balanced, and so we
/// only ever unmute sessions we muted (never touch a session the user muted manually).
/// </summary>
public sealed class AudioService
{
    private readonly Dictionary<uint, int> _muted = new();
    private readonly object _gate = new();

    public void Mute(uint pid)
    {
        lock (_gate)
        {
            if (_muted.TryGetValue(pid, out int count))
            {
                _muted[pid] = count + 1;
                return; // already muted by us
            }
            _muted[pid] = 1;
        }
        SetMute(pid, true);
    }

    public void Unmute(uint pid)
    {
        bool unmuteNow = false;
        lock (_gate)
        {
            if (!_muted.TryGetValue(pid, out int count)) return; // not ours — leave it alone
            if (count <= 1)
            {
                _muted.Remove(pid);
                unmuteNow = true;
            }
            else
            {
                _muted[pid] = count - 1;
            }
        }
        if (unmuteNow) SetMute(pid, false);
    }

    public void UnmuteAll()
    {
        List<uint> pids;
        lock (_gate)
        {
            pids = _muted.Keys.ToList();
            _muted.Clear();
        }
        foreach (var pid in pids) SetMute(pid, false);
    }

    private static void SetMute(uint pid, bool mute)
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
                        var session = sessions[i];
                        if (session.GetProcessID == pid)
                            session.SimpleAudioVolume.Mute = mute;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogException($"SetMute pid={pid} mute={mute}", ex);
        }
    }
}
