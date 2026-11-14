using System.Runtime.InteropServices;

namespace IterVC.Desktop.ViewModels;

internal static class WasapiDeviceInUse
{
    private const int DeviceInUseHResult = unchecked((int)0x8889000A);

    internal static bool Matches(Exception exception)
    {
        if (exception is COMException comException)
            return comException.HResult == DeviceInUseHResult;

        if (exception is AggregateException aggregateException)
            return aggregateException.InnerExceptions.Count > 0 &&
                   aggregateException.InnerExceptions.All(Matches);

        return exception.InnerException is not null && Matches(exception.InnerException);
    }
}
