namespace ID6ModeTray;

internal static class ControlOperationLock
{
    private const string DefaultMutexName = @"Local\ID6ModeTray.Control";

    internal static IDisposable Enter(string? mutexName = null, TimeSpan? timeout = null)
    {
        var mutex = new Mutex(false, mutexName ?? DefaultMutexName);
        try
        {
            var acquired = false;
            try
            {
                acquired = mutex.WaitOne(timeout ?? TimeSpan.FromSeconds(30));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                throw new TimeoutException("另一个控制操作仍在进行，请稍后重试。");
            }
            return new Lease(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    private sealed class Lease : IDisposable
    {
        private Mutex? mutex;

        public Lease(Mutex mutex) => this.mutex = mutex;

        public void Dispose()
        {
            var owned = Interlocked.Exchange(ref mutex, null);
            if (owned is null)
            {
                return;
            }
            owned.ReleaseMutex();
            owned.Dispose();
        }
    }
}
