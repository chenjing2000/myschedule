using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace OneSchedule;

public sealed class SettingsService
{
    private readonly string _filePath;
    private readonly Action<bool> _applyStartup;
    private byte[]? _snapshot;
    private bool _loaded;

    public SettingsService(string? filePath = null, Action<bool>? applyStartup = null)
    {
        _filePath = filePath ?? Path.Combine(AppContext.BaseDirectory, "OneSchedule.settings.json");
        _applyStartup = applyStartup ?? ApplyStartupRegistration;
    }

    public bool StartWithWindows { get; private set; }
    public string LastError { get; private set; } = "";

    public bool Load()
    {
        LastError = "";
        _loaded = false;
        StartWithWindows = false;
        bool registrationAttempted = false;
        try
        {
            _snapshot = File.Exists(_filePath) ? File.ReadAllBytes(_filePath) : null;
            bool enabled = _snapshot is null || Parse(_snapshot);
            registrationAttempted = true;
            _applyStartup(enabled);
            if (_snapshot is null)
            {
                try
                {
                    byte[] initial = Serialize(enabled);
                    WriteAtomic(initial, overwrite: false);
                    _snapshot = initial;
                }
                catch (Exception error)
                {
                    LastError = error.Message;
                    TryRestoreRegistration(false);
                    return false;
                }
            }
            StartWithWindows = enabled;
            _loaded = true;
            return true;
        }
        catch (Exception error)
        {
            LastError = error.Message;
            if (registrationAttempted) TryRestoreRegistration(false);
            return false;
        }
    }

    public bool SetStartWithWindows(bool enabled)
    {
        if (!_loaded && !Load()) return false;
        LastError = "";
        bool previous = StartWithWindows;
        byte[]? replacement = null;
        bool written = false;
        bool registrationAttempted = false;
        try
        {
            byte[] current = File.ReadAllBytes(_filePath);
            if (_snapshot is null || !current.AsSpan().SequenceEqual(_snapshot))
                throw new IOException("设置文件已被外部修改，请重新打开程序后再试。");
            if (enabled == previous) return true;
            replacement = Serialize(enabled);
            WriteAtomic(replacement);
            written = true;
            registrationAttempted = true;
            _applyStartup(enabled);
            _snapshot = replacement;
            StartWithWindows = enabled;
            return true;
        }
        catch (Exception error)
        {
            LastError = error.Message;
            if (written)
            {
                try
                {
                    if (!File.ReadAllBytes(_filePath).AsSpan().SequenceEqual(replacement))
                        throw new IOException("设置文件已被外部修改，已保留外部内容。");
                    WriteAtomic(_snapshot!);
                }
                catch (Exception rollbackError) { LastError += " 恢复设置文件失败：" + rollbackError.Message; }
            }
            if (registrationAttempted) TryRestoreRegistration(previous);
            return false;
        }
    }

    private void TryRestoreRegistration(bool enabled)
    {
        try { _applyStartup(enabled); }
        catch (Exception error)
        {
            // If restoration fails, an enabled checkbox would claim an unconfirmed registration.
            StartWithWindows = false;
            _loaded = false;
            LastError += " 恢复开机启动失败：" + error.Message;
        }
    }

    private static bool Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("设置文件必须是 JSON 对象。");
        var properties = root.EnumerateObject().ToArray();
        if (properties.Length != 1 || properties[0].Name != "startWithWindows" ||
            properties[0].Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new JsonException("设置文件必须仅包含布尔值 startWithWindows。");
        return properties[0].Value.GetBoolean();
    }

    private static byte[] Serialize(bool enabled) =>
        Encoding.UTF8.GetBytes("{\n  \"startWithWindows\": " + (enabled ? "true" : "false") + "\n}\n");

    private void WriteAtomic(byte[] bytes, bool overwrite = true)
    {
        string temporary = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, _filePath, overwrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void ApplyStartupRegistration(bool enabled)
    {
        const string path = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string name = "OneSchedule";
        using var current = Registry.CurrentUser.OpenSubKey(path, false);
        object? existing = current?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (!enabled)
        {
            if (existing is null) return;
            using var writable = Registry.CurrentUser.OpenSubKey(path, true)
                ?? throw new IOException("无法打开开机启动注册表项。");
            writable.DeleteValue(name, false);
            return;
        }
        string executable = Environment.ProcessPath ?? throw new IOException("无法确定程序路径。");
        string command = "\"" + executable + "\" --startup";
        if (existing is string text && string.Equals(text, command, StringComparison.Ordinal) &&
            current!.GetValueKind(name) == RegistryValueKind.String) return;
        using var target = Registry.CurrentUser.CreateSubKey(path, true)
            ?? throw new IOException("无法创建开机启动注册表项。");
        target.SetValue(name, command, RegistryValueKind.String);
    }
}
