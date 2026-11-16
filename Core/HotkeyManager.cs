using System.Windows.Interop;

namespace EasyShot.Core;

internal sealed class HotkeyManager : IDisposable
{
    public const int RegionId = 1;

    private readonly HwndSource _source;
    private readonly Dictionary<int, (string Action, HotkeyGesture Gesture)> _bindings = new();
    private int _nextId = 1;
    public bool Suspended { get; set; }

    public HotkeyManager()
    {
        var parameters = new HwndSourceParameters("EasyShotHotkeySink")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = NativeMethods.HwndMessage
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public event Action<string>? Pressed;
    public event Action<HotkeyGesture>? Captured;

    public string? Apply(IReadOnlyDictionary<string, string?> hotkeys, Func<string?> persist)
    {
        var desired = new Dictionary<int, (string Action, HotkeyGesture Gesture)>();
        var added = new List<int>();
        var gestures = new HashSet<HotkeyGesture>();
        var committed = false;
        try
        {
            foreach (var action in new[] { "region", "fullscreen" })
            {
                var text = hotkeys.GetValueOrDefault(action);
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (!HotkeyGesture.TryParse(text, out var gesture) || gesture is null || !gestures.Add(gesture))
                    return $"{ActionName(action)}：热键无效或重复（{text}）。";

                var existing = _bindings.FirstOrDefault(b => b.Value.Gesture.Equals(gesture));
                var id = existing.Key;
                if (id == 0)
                {
                    id = _nextId++;
                    if (!NativeMethods.RegisterHotKey(_source.Handle, id,
                            gesture.Modifiers | NativeMethods.ModNoRepeat, gesture.VirtualKey))
                        return $"{ActionName(action)}：热键 {gesture.Display} 无法注册，可能已被其他程序占用；原绑定未变更。";
                    added.Add(id);
                }

                desired[id] = (action, gesture);
            }

            var error = persist();
            if (error is not null) return error;
            foreach (var id in _bindings.Keys)
                if (!desired.ContainsKey(id)) NativeMethods.UnregisterHotKey(_source.Handle, id);
            _bindings.Clear();
            foreach (var binding in desired) _bindings.Add(binding.Key, binding.Value);
            committed = true;
            return null;
        }
        finally
        {
            if (!committed)
                foreach (var id in added) NativeMethods.UnregisterHotKey(_source.Handle, id);
        }
    }

    private static string ActionName(string action) => action switch
    {
        "region" => "区域截图", "fullscreen" => "全屏截图", _ => action
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && _bindings.TryGetValue(wParam.ToInt32(), out var binding))
        {
            if (Suspended) Captured?.Invoke(binding.Gesture);
            else Pressed?.Invoke(binding.Action);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _bindings.Keys)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        }

        _bindings.Clear();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
