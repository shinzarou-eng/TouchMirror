namespace TouchMirror.Services;

public static class ScreenDimmer
{
    private sealed class Hold
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public int Refs;
        public int Brightness = -1;
        public int StayOn = -1;
        public int BrightnessMode = -1;
        public string Serial = "";
    }

    private static readonly Dictionary<string, Hold> _holds = new();
    private static readonly object _sync = new();

    private static Hold? For(string deviceKey)
    {
        lock (_sync)
            return _holds.TryGetValue(deviceKey, out var h) ? h : null;
    }

    private static Hold ForOrCreate(string deviceKey)
    {
        lock (_sync)
            return _holds.TryGetValue(deviceKey, out var h) ? h : _holds[deviceKey] = new Hold();
    }

    public static async Task<bool> DimAsync(AdbDevice device, string serial)
    {
        var hold = ForOrCreate(device.DeviceKey);
        await hold.Gate.WaitAsync();
        try
        {
            if (hold.Refs++ > 0)
                return false;
            var pending = DimmedScreenStore.Pending().FirstOrDefault(s =>
                s.DeviceKey == device.DeviceKey || s.Serial == serial);
            hold.Brightness = pending?.Brightness ?? await AdbService.GetBrightnessAsync(serial);
            hold.StayOn = pending?.StayOn ?? await AdbService.GetStayOnWhilePluggedInAsync(serial);
            hold.BrightnessMode = pending?.BrightnessMode ?? await AdbService.GetBrightnessModeAsync(serial);
            hold.Serial = serial;
            DimmedScreenStore.Mark(serial, device.DeviceKey,
                hold.Brightness, hold.StayOn, hold.BrightnessMode);
            await AdbService.SetStayOnWhilePluggedInAsync(serial, 7);
            await AdbService.WakeScreenAsync(serial);
            await AdbService.SetBrightnessAsync(serial, 0);
            return true;
        }
        finally
        {
            hold.Gate.Release();
        }
    }

    public static async Task RestoreAsync(AdbDevice device, string serial)
    {
        var hold = For(device.DeviceKey);
        if (hold == null)
            return;
        await hold.Gate.WaitAsync();
        try
        {
            if (hold.Refs == 0 || --hold.Refs > 0)
                return;
            var target = hold.Serial;
            if (serial != target)
            {
                var state = await AdbService.GetDeviceStateAsync(target);
                if (state is not ("device" or "unauthorized"))
                    target = serial;
            }
            if (hold.StayOn >= 0)
                await AdbService.SetStayOnWhilePluggedInAsync(target, hold.StayOn);
            if (hold.Brightness >= 0)
                await AdbService.SetBrightnessAsync(target, hold.Brightness);
            if (hold.BrightnessMode >= 0)
                await AdbService.SetBrightnessModeAsync(target, hold.BrightnessMode);
            DimmedScreenStore.Clear(device.DeviceKey);
            hold.Serial = "";
            hold.Brightness = hold.StayOn = hold.BrightnessMode = -1;
        }
        finally
        {
            hold.Gate.Release();
        }
    }
}
