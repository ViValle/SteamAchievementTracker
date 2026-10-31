using System.Diagnostics;
using System.ServiceProcess;
using SteamAchievementTracker.Core.Interfaces;
using SteamAchievementTracker.Core.Models;
using SteamAchievementTracker.Injector.Native;

namespace SteamAchievementTracker.Injector.Safety;

public class AntiCheatScanner : IAntiCheatScanner
{
    private static readonly HashSet<string> KnownAntiCheatModules = new(StringComparer.OrdinalIgnoreCase)
    {
        // Easy Anti-Cheat (EAC)
        "EasyAntiCheat.dll", "EasyAntiCheat_x64.dll", "EasyAntiCheat_x86.dll",
        "eac_server.dll", "eac_server64.dll", "EasyAntiCheat_EOS.dll",
        // BattlEye
        "BEClient.dll", "BEClient_x64.dll", "BEService.exe", "BEService_x64.exe",
        // Riot Vanguard
        "vgk.sys", "vgc.exe",
        // Valve Anti-Cheat (VAC) protected game modules / Steam Service hooks
        "steamservice.dll",
        // Denuvo Anti-Cheat, Ricochet, ACE
        "AntiCheatX64.dll", "ApexFramework.dll", "ace_base.dll", "mhyprot2.sys"
    };

    private static readonly HashSet<string> KnownAntiCheatServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "vgk", "BEDaisy"
    };

    private readonly ISteamApiClient _steamApiClient;

    public AntiCheatScanner(ISteamApiClient steamApiClient)
    {
        _steamApiClient = steamApiClient;
    }

    public async Task<AntiCheatCheckResult> EvaluateProcessSafetyAsync(int processId, uint appId)
    {
        // 1. Проверяем VAC статус игры через Steam Store API
        if (appId > 0)
        {
            var isVac = await _steamApiClient.IsVacSecuredAsync(appId);
            if (isVac)
            {
                return new AntiCheatCheckResult(
                    IsSafe: false,
                    DetectedThreat: "Valve Anti-Cheat (VAC)",
                    Reason: $"Игра с AppID {appId} помечена как защищенная системой Valve Anti-Cheat. Инжект заблокирован во избежание VAC-бана аккаунта.",
                    ScannedModulesCount: 0);
            }
        }

        // 2. Сканируем запущенные службы Windows на наличие античитов
        var serviceCheck = CheckActiveAntiCheatServices();
        if (!serviceCheck.IsSafe)
        {
            return serviceCheck;
        }

        // 3. Сканируем загруженные модули целевого процесса
        var moduleCheck = ScanProcessModules(processId);
        return moduleCheck;
    }

    private static AntiCheatCheckResult CheckActiveAntiCheatServices()
    {
        try
        {
            var services = ServiceController.GetServices();
            foreach (var service in services)
            {
                if (KnownAntiCheatServices.Contains(service.ServiceName) &&
                    service.Status == ServiceControllerStatus.Running)
                {
                    return new AntiCheatCheckResult(
                        IsSafe: false,
                        DetectedThreat: service.ServiceName,
                        Reason: $"В операционной системе обнаружена активная служба античита [{service.ServiceName}]. Для безопасности инжект отменен.");
                }
            }
        }
        catch (Exception ex)
        {
            // Ограничения прав (не админ) не должны намертво крашить проверку
            Debug.WriteLine($"[AntiCheatScanner] Service inspection skipped: {ex.Message}");
        }

        return new AntiCheatCheckResult(IsSafe: true, DetectedThreat: null, Reason: "Службы чисты.");
    }

    private static AntiCheatCheckResult ScanProcessModules(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Refresh();

            int scannedCount = 0;
            try
            {
                foreach (ProcessModule module in process.Modules)
                {
                    scannedCount++;
                    if (KnownAntiCheatModules.Contains(module.ModuleName))
                    {
                        return new AntiCheatCheckResult(
                            IsSafe: false,
                            DetectedThreat: module.ModuleName,
                            Reason: $"В адресном пространстве процесса обнаружен критический модуль античита [{module.ModuleName}]. Инжект жестко прерван.",
                            ScannedModulesCount: scannedCount);
                    }
                }
            }
            catch
            {
                // Если процесс защищен или 32/64-bit разрядности не совпадают, пробуем Win32 EnumProcessModulesEx
                return ScanModulesViaWin32(processId);
            }

            return new AntiCheatCheckResult(
                IsSafe: true,
                DetectedThreat: null,
                Reason: "Процесс проверен. Модулей античитов не обнаружено.",
                ScannedModulesCount: scannedCount);
        }
        catch (ArgumentException)
        {
            return new AntiCheatCheckResult(
                IsSafe: false,
                DetectedThreat: "ProcessNotFound",
                Reason: $"Процесс с PID {processId} не найден или уже завершился.");
        }
        catch (Exception ex)
        {
            return new AntiCheatCheckResult(
                IsSafe: false,
                DetectedThreat: "InspectionError",
                Reason: $"Ошибка доступа при сканировании процесса: {ex.Message}");
        }
    }

    private static AntiCheatCheckResult ScanModulesViaWin32(int processId)
    {
        var hProcess = Win32Api.OpenProcess(
            Win32Api.PROCESS_QUERY_INFORMATION | Win32Api.PROCESS_VM_READ,
            false,
            processId);

        if (hProcess == IntPtr.Zero)
        {
            return new AntiCheatCheckResult(
                IsSafe: false,
                DetectedThreat: "AccessDenied",
                Reason: "Недостаточно привилегий для чтения модулей процесса. Возможно, процесс защищен ядром античита.");
        }

        try
        {
            var modules = new IntPtr[1024];
            const uint LIST_MODULES_ALL = 0x03;

            if (Win32Api.EnumProcessModulesEx(hProcess, modules, (uint)(modules.Length * IntPtr.Size), out var cbNeeded, LIST_MODULES_ALL))
            {
                int count = (int)(cbNeeded / IntPtr.Size);
                var nameBuffer = new char[260];

                for (int i = 0; i < count; i++)
                {
                    uint len = Win32Api.GetModuleBaseName(hProcess, modules[i], nameBuffer, (uint)nameBuffer.Length);
                    if (len > 0)
                    {
                        var modName = new string(nameBuffer, 0, (int)len);
                        if (KnownAntiCheatModules.Contains(modName))
                        {
                            return new AntiCheatCheckResult(
                                IsSafe: false,
                                DetectedThreat: modName,
                                Reason: $"Обнаружен модуль античита [{modName}] через низкоуровневый аудит памяти.",
                                ScannedModulesCount: count);
                        }
                    }
                }

                return new AntiCheatCheckResult(
                    IsSafe: true,
                    DetectedThreat: null,
                    Reason: "Низкоуровневый аудит модулей завершен успешно. Угроз не обнаружено.",
                    ScannedModulesCount: count);
            }

            return new AntiCheatCheckResult(
                IsSafe: false,
                DetectedThreat: "EnumModulesFailed",
                Reason: "Не удалось перечислить модули процесса.");
        }
        finally
        {
            Win32Api.CloseHandle(hProcess);
        }
    }
}
