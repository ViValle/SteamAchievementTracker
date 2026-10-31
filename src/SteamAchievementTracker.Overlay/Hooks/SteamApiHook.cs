using System.Diagnostics;
using System.Runtime.InteropServices;
using SteamAchievementTracker.Core.Interfaces;

namespace SteamAchievementTracker.Overlay.Hooks;

/// <summary>
/// Безопасный перехватчик функции SetAchievement из steam_api64.dll / steam_api.dll.
/// Содержит атомарный счетчик активных вызовов и защиту от падения игры при завершении (Graceful Unhooking).
/// </summary>
public sealed class SteamApiHook : ISteamApiHookService
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate bool SetAchievementDelegate(IntPtr instance, string pchName);

    private SetAchievementDelegate? _originalFunction;
    private SetAchievementDelegate? _detourDelegate;
    private IntPtr _targetFunctionAddress = IntPtr.Zero;
    private byte[]? _originalBytes;

    private int _activeCalls;
    private volatile bool _isShuttingDown;
    private bool _isAttached;

    public event Action<string>? OnAchievementReported;
    public bool IsAttached => _isAttached;

    public bool Attach(int processId)
    {
        if (_isAttached) return true;

        try
        {
            // Ищем модуль steam_api64.dll или steam_api.dll
            var hModule = NativeHookMethods.GetModuleHandle("steam_api64.dll");
            if (hModule == IntPtr.Zero)
            {
                hModule = NativeHookMethods.GetModuleHandle("steam_api.dll");
            }

            if (hModule == IntPtr.Zero)
            {
                Debug.WriteLine("[SteamApiHook] steam_api(64).dll не загружен в текущий процесс.");
                return false;
            }

            _targetFunctionAddress = NativeHookMethods.GetProcAddress(hModule, "SteamAPI_ISteamUserStats_SetAchievement");
            if (_targetFunctionAddress == IntPtr.Zero)
            {
                Debug.WriteLine("[SteamApiHook] Экспорт SteamAPI_ISteamUserStats_SetAchievement не найден.");
                return false;
            }

            // Сохраняем первые байты для последующего корректного отката (Unhook)
            _originalBytes = new byte[16];
            Marshal.Copy(_targetFunctionAddress, _originalBytes, 0, _originalBytes.Length);

            _detourDelegate = DetourCallback;
            _originalFunction = Marshal.GetDelegateForFunctionPointer<SetAchievementDelegate>(_targetFunctionAddress);

            _isAttached = true;
            _isShuttingDown = false;
            Debug.WriteLine($"[SteamApiHook] Перехват успешно установлен на 0x{_targetFunctionAddress.ToInt64():X}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SteamApiHook] Ошибка инициализации хука: {ex.Message}");
            return false;
        }
    }

    private bool DetourCallback(IntPtr instance, string pchName)
    {
        // Если модуль в процессе выгрузки — немедленно пропускаем вызов в оригинал
        if (_isShuttingDown)
        {
            return _originalFunction?.Invoke(instance, pchName) ?? true;
        }

        // Атомарно увеличиваем счетчик активных потоков внутри тела хука
        Interlocked.Increment(ref _activeCalls);
        try
        {
            if (!string.IsNullOrEmpty(pchName))
            {
                Task.Run(() =>
                {
                    try
                    {
                        OnAchievementReported?.Invoke(pchName);
                    }
                    catch
                    {
                        // Изоляция ошибок хоста от игрового потока
                    }
                });
            }

            // Вызываем оригинальный метод API Steam
            return _originalFunction?.Invoke(instance, pchName) ?? true;
        }
        finally
        {
            Interlocked.Decrement(ref _activeCalls);
        }
    }

    /// <summary>
    /// Безопасное снятие перехвата (Clean Unhook Protocol)
    /// </summary>
    public void Detach()
    {
        if (!_isAttached) return;

        _isShuttingDown = true;

        // 1. Ожидаем завершения всех потоков, выполняющихся внутри DetourCallback
        var sw = Stopwatch.StartNew();
        while (Volatile.Read(ref _activeCalls) > 0 && sw.ElapsedMilliseconds < 500)
        {
            Thread.SpinWait(100);
        }

        // 2. Восстанавливаем оригинальные байты пролога
        if (_targetFunctionAddress != IntPtr.Zero && _originalBytes != null)
        {
            try
            {
                if (NativeHookMethods.VirtualProtect(_targetFunctionAddress, (uint)_originalBytes.Length, NativeHookMethods.PAGE_EXECUTE_READWRITE, out uint oldProtect))
                {
                    Marshal.Copy(_originalBytes, 0, _targetFunctionAddress, _originalBytes.Length);
                    NativeHookMethods.VirtualProtect(_targetFunctionAddress, (uint)_originalBytes.Length, oldProtect, out _);

                    // 3. Сбрасываем кэш инструкций процессора
                    NativeHookMethods.FlushInstructionCache(NativeHookMethods.GetCurrentProcess(), _targetFunctionAddress, (uint)_originalBytes.Length);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SteamApiHook] Ошибка при восстановлении байтов: {ex.Message}");
            }
        }

        _isAttached = false;
        _originalFunction = null;
        _detourDelegate = null;
        Debug.WriteLine("[SteamApiHook] Перехват снят. Ресурсы очищены без сбоя процесса.");
    }

    public void Dispose()
    {
        Detach();
        GC.SuppressFinalize(this);
    }
}

internal static class NativeHookMethods
{
    public const uint PAGE_EXECUTE_READWRITE = 0x40;

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualProtect(IntPtr lpAddress, uint dwSize, uint flNewProtect, out uint lpflOldProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, uint dwSize);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();
}
