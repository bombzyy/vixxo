using NAudio.CoreAudioApi;

namespace Vixxo.Core.Audio;

public record AudioDeviceInfo(
    string Id,
    string Name,
    bool IsDefault,
    bool IsVbCable,
    DataFlow Flow);

public static class AudioDeviceManager
{
    public const string VbCablePlaybackNameFragment = "CABLE Input";
    public const string VbCableRecordingNameFragment = "CABLE Output";

    public static List<AudioDeviceInfo> GetPlaybackDevices()
    {
        return EnumerateDevices(DataFlow.Render);
    }

    public static List<AudioDeviceInfo> GetRecordingDevices()
    {
        return EnumerateDevices(DataFlow.Capture);
    }

    public static AudioDeviceInfo? FindVbCablePlaybackDevice()
    {
        var devices = GetPlaybackDevices();
        return devices.FirstOrDefault(d => d.Name.Contains(VbCablePlaybackNameFragment, StringComparison.OrdinalIgnoreCase))
            ?? devices.FirstOrDefault(d => d.IsVbCable);
    }

    public static AudioDeviceInfo? FindVbCableRecordingDevice()
    {
        var devices = GetRecordingDevices();
        return devices.FirstOrDefault(d => d.Name.Contains(VbCableRecordingNameFragment, StringComparison.OrdinalIgnoreCase))
            ?? devices.FirstOrDefault(d => d.IsVbCable);
    }

    public static MMDevice? GetDeviceById(string deviceId)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.GetDevice(deviceId);
        }
        catch
        {
            return null;
        }
    }

    private static List<AudioDeviceInfo> EnumerateDevices(DataFlow dataFlow)
    {
        var list = new List<AudioDeviceInfo>();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            string? defaultId = null;

            try
            {
                var defaultDevice = enumerator.GetDefaultAudioEndpoint(dataFlow, Role.Multimedia);
                defaultId = defaultDevice.ID;
            }
            catch
            {
                // No default device found or error
            }

            var endpoints = enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active);
            foreach (var device in endpoints)
            {
                string id = device.ID;
                string name = device.FriendlyName;
                bool isDefault = (defaultId != null && string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase));
                bool isVbCable = name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("CABLE", StringComparison.OrdinalIgnoreCase);

                list.Add(new AudioDeviceInfo(id, name, isDefault, isVbCable, dataFlow));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error enumerating audio devices: {ex.Message}");
        }

        return list;
    }
}
