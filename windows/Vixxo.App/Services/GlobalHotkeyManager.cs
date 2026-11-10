using System.Runtime.InteropServices;
using System.Windows.Interop;
using Vixxo.Core.Input;

namespace Vixxo.App.Services;

public sealed class GlobalHotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly object _lock = new();
    private readonly Dictionary<string, HotkeyRegistration> _actionToRegistration = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, HotkeyRegistration> _idToRegistration = new();
    private int _nextId = 9000;
    private IntPtr _hWnd = IntPtr.Zero;
    private HwndSource? _source;
    private bool _isDisposed;

    private sealed record HotkeyRegistration(int Id, string ActionKey, HotkeyBinding Binding, Action Callback);

    public event Action<string>? HotkeyPressed;

    public void Initialize(IntPtr windowHandle)
    {
        lock (_lock)
        {
            if (_hWnd != IntPtr.Zero && _hWnd == windowHandle) return;

            CleanupHwnd();
            _hWnd = windowHandle;
            if (_hWnd != IntPtr.Zero)
            {
                _source = HwndSource.FromHwnd(_hWnd);
                _source?.AddHook(HwndHook);
            }
        }
    }

    public bool Register(string actionKey, HotkeyBinding? binding, Action callback)
    {
        if (string.IsNullOrWhiteSpace(actionKey)) return false;

        lock (_lock)
        {
            // Remove existing registration for this action if any
            UnregisterInternal(actionKey);

            if (binding == null || binding.IsEmpty || _hWnd == IntPtr.Zero) return false;

            int id = Interlocked.Increment(ref _nextId);
            uint modifiers = (uint)binding.Modifiers | MOD_NOREPEAT;

            bool success = RegisterHotKey(_hWnd, id, modifiers, (uint)binding.VirtualKey);
            if (!success)
            {
                // Fallback without MOD_NOREPEAT if unsupported on legacy systems
                success = RegisterHotKey(_hWnd, id, (uint)binding.Modifiers, (uint)binding.VirtualKey);
            }

            if (success)
            {
                var reg = new HotkeyRegistration(id, actionKey, binding, callback);
                _actionToRegistration[actionKey] = reg;
                _idToRegistration[id] = reg;
                return true;
            }

            return false;
        }
    }

    public bool Unregister(string actionKey)
    {
        if (string.IsNullOrWhiteSpace(actionKey)) return false;

        lock (_lock)
        {
            return UnregisterInternal(actionKey);
        }
    }

    private bool UnregisterInternal(string actionKey)
    {
        if (_actionToRegistration.TryGetValue(actionKey, out var reg))
        {
            if (_hWnd != IntPtr.Zero)
            {
                UnregisterHotKey(_hWnd, reg.Id);
            }
            _actionToRegistration.Remove(actionKey);
            _idToRegistration.Remove(reg.Id);
            return true;
        }
        return false;
    }

    public void UnregisterAll()
    {
        lock (_lock)
        {
            if (_hWnd != IntPtr.Zero)
            {
                foreach (var reg in _idToRegistration.Values)
                {
                    UnregisterHotKey(_hWnd, reg.Id);
                }
            }
            _actionToRegistration.Clear();
            _idToRegistration.Clear();
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            Action? callback = null;
            string? actionKey = null;

            lock (_lock)
            {
                if (_idToRegistration.TryGetValue(id, out var reg))
                {
                    callback = reg.Callback;
                    actionKey = reg.ActionKey;
                }
            }

            if (callback != null)
            {
                callback.Invoke();
                if (actionKey != null) HotkeyPressed?.Invoke(actionKey);
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private void CleanupHwnd()
    {
        UnregisterAll();
        if (_source != null)
        {
            _source.RemoveHook(HwndHook);
            _source = null;
        }
        _hWnd = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        CleanupHwnd();
    }
}
