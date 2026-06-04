using System.Runtime.InteropServices;

namespace SandboxTimeline;

internal static class StoreLicenseBridge
{
    public static Task<T> AsTask<T>(this Windows.Foundation.IAsyncOperation<T> operation)
    {
        if (operation is null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        operation.Completed = (asyncInfo, _) =>
        {
            try
            {
                switch (asyncInfo.Status)
                {
                    case Windows.Foundation.AsyncStatus.Completed:
                        tcs.TrySetResult(asyncInfo.GetResults());
                        break;
                    case Windows.Foundation.AsyncStatus.Canceled:
                        tcs.TrySetCanceled();
                        break;
                    case Windows.Foundation.AsyncStatus.Error:
                        tcs.TrySetException(asyncInfo.ErrorCode);
                        break;
                    default:
                        tcs.TrySetException(new COMException("Store async operation did not complete."));
                        break;
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        };

        return tcs.Task;
    }
}
