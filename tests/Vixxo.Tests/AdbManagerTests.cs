using Vixxo.Core.Usb;

namespace Vixxo.Tests;

public class AdbManagerTests
{
    [Fact]
    public void ParseAdbDevicesOutput_ParsesVariousDeviceStatesCorrectly()
    {
        string sampleOutput = """
        * daemon not running; starting now at tcp:5037
        * daemon started successfully
        List of devices attached
        RF8M10XXXXX            device product:greatqltezc model:SM_N9600 device:greatqlte transport_id:1
        192.168.29.50:5555     unauthorized transport_id:2
        emulator-5554          offline product:sdk_x86 model:Android_SDK_built_for_x86 device:generic_x86 transport_id:3
        """;

        var devices = AdbManager.ParseAdbDevicesOutput(sampleOutput);

        Assert.Equal(3, devices.Count);

        // Device 1: Ready
        var d1 = devices[0];
        Assert.Equal("RF8M10XXXXX", d1.Serial);
        Assert.Equal("device", d1.State);
        Assert.True(d1.IsReady);
        Assert.Equal("SM N9600", d1.Model);
        Assert.Equal("greatqltezc", d1.Product);

        // Device 2: Unauthorized (needs user to tap prompt)
        var d2 = devices[1];
        Assert.Equal("192.168.29.50:5555", d2.Serial);
        Assert.Equal("unauthorized", d2.State);
        Assert.False(d2.IsReady);

        // Device 3: Offline
        var d3 = devices[2];
        Assert.Equal("emulator-5554", d3.Serial);
        Assert.Equal("offline", d3.State);
        Assert.False(d3.IsReady);
        Assert.Equal("Android SDK built for x86", d3.Model);
    }

    [Fact]
    public void ParseAdbDevicesOutput_HandlesEmptyOrWhitespaceGracefully()
    {
        var devicesEmpty = AdbManager.ParseAdbDevicesOutput("");
        Assert.Empty(devicesEmpty);

        var devicesHeaderOnly = AdbManager.ParseAdbDevicesOutput("List of devices attached\n\n");
        Assert.Empty(devicesHeaderOnly);
    }

    [Fact]
    public void AdbManager_LocatesAdbOrHandlesMissingCleanly()
    {
        var manager = new AdbManager();
        // If Android SDK is on this machine, it should find it
        if (manager.IsAdbAvailable)
        {
            Assert.NotNull(manager.AdbPath);
            Assert.True(System.IO.File.Exists(manager.AdbPath));
        }
    }
}
