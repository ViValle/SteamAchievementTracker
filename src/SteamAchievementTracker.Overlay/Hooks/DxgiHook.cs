using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SteamAchievementTracker.Overlay.Hooks;

/// <summary>
/// Перехват конвейера вывода кадров DirectX (IDXGISwapChain::Present, VTable Slot 8).
/// Позволяет рендерить оверлей поверх эксклюзивного полноэкранного режима (Exclusive Fullscreen).
/// </summary>
public sealed class DxgiHook : IDisposable
{
    // HRESULT Present(IDXGISwapChain* This, UINT SyncInterval, UINT Flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int DxgiPresentDelegate(IntPtr swapChainPtr, uint syncInterval, uint flags);

    private DxgiPresentDelegate? _originalPresent;
    private DxgiPresentDelegate? _hookDelegate;
    private IntPtr _presentVTableEntryAddress = IntPtr.Zero;
    private IntPtr _originalPresentAddress = IntPtr.Zero;
    private bool _isHooked;
    private volatile bool _isShuttingDown;
    private int _activeCalls;

    public event Action<IntPtr>? OnRenderFrame;

    public bool AttachToSwapChain(IntPtr pSwapChain)
    {
        if (_isHooked || pSwapChain == IntPtr.Zero) return false;

        try
        {
            // 1. Получаем указатель на виртуальную таблицу (VTable) из объекта SwapChain
            IntPtr vtable = Marshal.ReadIntPtr(pSwapChain);
            if (vtable == IntPtr.Zero) return false;

            // 2. Слот 8 в интерфейсе IDXGISwapChain соответствует методу Present
            _presentVTableEntryAddress = IntPtr.Add(vtable, 8 * IntPtr.Size);
            _originalPresentAddress = Marshal.ReadIntPtr(_presentVTableEntryAddress);

            _originalPresent = Marshal.GetDelegateForFunctionPointer<DxgiPresentDelegate>(_originalPresentAddress);
            _hookDelegate = PresentCallback;

            // 3. Заменяем адрес метода в VTable на наш перехватчик с защитой страниц памяти
            if (NativeHookMethods.VirtualProtect(_presentVTableEntryAddress, (uint)IntPtr.Size, NativeHookMethods.PAGE_EXECUTE_READWRITE, out uint oldProtect))
            {
                IntPtr detourPtr = Marshal.GetFunctionPointerForDelegate(_hookDelegate);
                Marshal.WriteIntPtr(_presentVTableEntryAddress, detourPtr);
                NativeHookMethods.VirtualProtect(_presentVTableEntryAddress, (uint)IntPtr.Size, oldProtect, out _);

                NativeHookMethods.FlushInstructionCache(NativeHookMethods.GetCurrentProcess(), _presentVTableEntryAddress, (uint)IntPtr.Size);
                _isHooked = true;
                _isShuttingDown = false;
                Debug.WriteLine("[DxgiHook] DXGI Present hook успешно активирован.");
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DxgiHook] Ошибка установки хука DXGI: {ex.Message}");
        }

        return false;
    }

    private int PresentCallback(IntPtr swapChainPtr, uint syncInterval, uint flags)
    {
        if (_isShuttingDown)
        {
            return _originalPresent?.Invoke(swapChainPtr, syncInterval, flags) ?? 0;
        }

        Interlocked.Increment(ref _activeCalls);
        try
        {
            // Вызываем процедуру отрисовки нашего оверлея прямо в текущий BackBuffer
            OnRenderFrame?.Invoke(swapChainPtr);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DxgiHook] Исключение в конвейере отрисовки: {ex.Message}");
        }
        finally
        {
            Interlocked.Decrement(ref _activeCalls);
        }

        // Передаем управление оригинальной функции драйвера видеокарты
        return _originalPresent?.Invoke(swapChainPtr, syncInterval, flags) ?? 0;
    }

    public void Detach()
    {
        if (!_isHooked) return;

        _isShuttingDown = true;

        // Ожидаем завершения всех потоков рендера
        var sw = Stopwatch.StartNew();
        while (Volatile.Read(ref _activeCalls) > 0 && sw.ElapsedMilliseconds < 500)
        {
            Thread.SpinWait(100);
        }

        // Восстанавливаем оригинальный указатель в VTable
        if (_presentVTableEntryAddress != IntPtr.Zero && _originalPresentAddress != IntPtr.Zero)
        {
            if (NativeHookMethods.VirtualProtect(_presentVTableEntryAddress, (uint)IntPtr.Size, NativeHookMethods.PAGE_EXECUTE_READWRITE, out uint oldProtect))
            {
                Marshal.WriteIntPtr(_presentVTableEntryAddress, _originalPresentAddress);
                NativeHookMethods.VirtualProtect(_presentVTableEntryAddress, (uint)IntPtr.Size, oldProtect, out _);
                NativeHookMethods.FlushInstructionCache(NativeHookMethods.GetCurrentProcess(), _presentVTableEntryAddress, (uint)IntPtr.Size);
            }
        }

        _isHooked = false;
        _originalPresent = null;
        _hookDelegate = null;
        Debug.WriteLine("[DxgiHook] DXGI Present хук корректно снят.");
    }

    public void Dispose()
    {
        Detach();
        GC.SuppressFinalize(this);
    }
}
