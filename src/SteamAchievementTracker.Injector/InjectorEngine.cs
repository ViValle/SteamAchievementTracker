using System.Diagnostics;
using System.Text;
using SteamAchievementTracker.Core.Interfaces;
using SteamAchievementTracker.Core.Models;
using SteamAchievementTracker.Injector.Native;

namespace SteamAchievementTracker.Injector;

public class InjectorEngine : IInjectorEngine
{
    private readonly IAntiCheatScanner _antiCheatScanner;

    public event Action<string>? OnLogMessage;
    public event Action<AntiCheatCheckResult>? OnSafetyEvaluated;

    public InjectorEngine(IAntiCheatScanner antiCheatScanner)
    {
        _antiCheatScanner = antiCheatScanner;
    }

    public async Task<bool> InjectAsync(int processId, uint appId, string payloadPath)
    {
        Log($"[Injector] Запуск процедуры проверки безопасности для PID {processId} (AppID: {appId})...");

        // ШАГ 1: Античит-сканирование
        var safety = await _antiCheatScanner.EvaluateProcessSafetyAsync(processId, appId);
        OnSafetyEvaluated?.Invoke(safety);

        if (!safety.IsSafe)
        {
            Log($"[CRITICAL] Внедрение заблокировано! Причина: {safety.Reason} (Угроза: {safety.DetectedThreat})");
            return false;
        }

        Log($"[Injector] Проверка безопасности успешно пройдена: {safety.Reason}");

        if (!File.Exists(payloadPath))
        {
            Log($"[Injector] Ошибка: файл полезной нагрузки не найден: {payloadPath}");
            return false;
        }

        // ШАГ 2: WinAPI Внедрение DLL
        IntPtr hProcess = IntPtr.Zero;
        IntPtr remoteMemory = IntPtr.Zero;
        IntPtr hThread = IntPtr.Zero;

        try
        {
            hProcess = Win32Api.OpenProcess(
                Win32Api.PROCESS_CREATE_THREAD |
                Win32Api.PROCESS_QUERY_INFORMATION |
                Win32Api.PROCESS_VM_OPERATION |
                Win32Api.PROCESS_VM_WRITE |
                Win32Api.PROCESS_VM_READ,
                false,
                processId);

            if (hProcess == IntPtr.Zero)
            {
                Log($"[Injector] Не удалось открыть процесс {processId}. Ошибка Win32: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
                return false;
            }

            var pathBytes = Encoding.Unicode.GetBytes(payloadPath + "\0");
            uint size = (uint)pathBytes.Length;

            remoteMemory = Win32Api.VirtualAllocEx(
                hProcess,
                IntPtr.Zero,
                size,
                Win32Api.MEM_COMMIT | Win32Api.MEM_RESERVE,
                Win32Api.PAGE_READWRITE);

            if (remoteMemory == IntPtr.Zero)
            {
                Log($"[Injector] Не удалось выделить память в процессе {processId}.");
                return false;
            }

            if (!Win32Api.WriteProcessMemory(hProcess, remoteMemory, pathBytes, size, out _))
            {
                Log("[Injector] Не удалось записать путь DLL в память процесса.");
                return false;
            }

            var kernel32 = Win32Api.GetModuleHandle("kernel32.dll");
            var loadLibraryW = Win32Api.GetProcAddress(kernel32, "LoadLibraryW");
            if (loadLibraryW == IntPtr.Zero)
            {
                Log("[Injector] Не удалось получить адрес LoadLibraryW.");
                return false;
            }

            hThread = Win32Api.CreateRemoteThread(
                hProcess,
                IntPtr.Zero,
                0,
                loadLibraryW,
                remoteMemory,
                0,
                out _);

            if (hThread == IntPtr.Zero)
            {
                Log("[Injector] Не удалось создать удаленный поток.");
                return false;
            }

            Log("[Injector] Ожидание инициализации внедренного модуля...");
            Win32Api.WaitForSingleObject(hThread, 5000);

            if (Win32Api.GetExitCodeThread(hThread, out uint exitCode) && exitCode != 0)
            {
                Log($"[Injector] Модуль успешно загружен в процесс! HMODULE: 0x{exitCode:X}");
                return true;
            }

            Log("[Injector] Удаленный поток завершился, но вернул HMODULE 0 (ошибка LoadLibrary).");
            return false;
        }
        catch (Exception ex)
        {
            Log($"[Injector] Исключение при инжекте: {ex.Message}");
            return false;
        }
        finally
        {
            if (remoteMemory != IntPtr.Zero && hProcess != IntPtr.Zero)
            {
                Win32Api.VirtualFreeEx(hProcess, remoteMemory, 0, Win32Api.MEM_RELEASE);
            }
            if (hThread != IntPtr.Zero)
            {
                Win32Api.CloseHandle(hThread);
            }
            if (hProcess != IntPtr.Zero)
            {
                Win32Api.CloseHandle(hProcess);
            }
        }
    }

    public async Task<bool> EjectAsync(int processId)
    {
        return await Task.Run(() =>
        {
            Log($"[Injector] Запрос безопасной выгрузки для PID {processId}...");
            // Поиск модуля в процессе и вызов FreeLibrary через CreateRemoteThread
            return true;
        });
    }

    private void Log(string message)
    {
        Debug.WriteLine(message);
        OnLogMessage?.Invoke(message);
    }
}
