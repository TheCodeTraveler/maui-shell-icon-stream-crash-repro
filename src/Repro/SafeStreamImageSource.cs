using Microsoft.Maui;

namespace Repro;

// The user-code workaround: an IStreamImageSource that never throws and never shares a cancellation token,
// so ShellToolbarTracker.UpdateLeftBarButtonItem (async void) has nothing to crash on.
sealed class SafeStreamImageSource(Func<CancellationToken, Task<Stream>> load, string caseName) : ImageSource, IStreamImageSource
{
    public override bool IsEmpty => false;

    public async Task<Stream> GetStreamAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await load(cancellationToken);
        }
        catch (Exception exception)
        {
            Log.Line("FALLBACK", caseName, ("exception", exception.GetType().Name), ("message", exception.Message));
            return new MemoryStream(Scenario.TransparentPng);
        }
    }
}
