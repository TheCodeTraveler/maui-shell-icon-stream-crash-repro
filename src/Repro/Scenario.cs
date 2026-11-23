namespace Repro;

enum IconSourceKind { Valid, Throws, SlowThenValid }

enum IconPlacement { FlyoutIcon, BackButtonIconOverride, ImageAndToolbarItem }

sealed record Scenario(string Name, IconSourceKind Kind, IconPlacement Placement, bool UsesWorkaround, bool ChangesForegroundColor)
{
    readonly TaskCompletionSource firstStreamRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task FirstStreamRequested => firstStreamRequested.Task;

    public static string CaseName { get; set; } = "control";

    // 1x1 transparent PNG
    public static readonly byte[] TransparentPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    public static Scenario Parse(string caseName)
    {
        var usesWorkaround = caseName.EndsWith("-safe", StringComparison.Ordinal);
        var baseName = usesWorkaround ? caseName[..^"-safe".Length] : caseName;

        var (kind, placement) = baseName switch
        {
            "control" => (IconSourceKind.Valid, IconPlacement.FlyoutIcon),
            "control-iconoverride" => (IconSourceKind.Valid, IconPlacement.BackButtonIconOverride),
            "flyouticon-throws" => (IconSourceKind.Throws, IconPlacement.FlyoutIcon),
            "iconoverride-throws" => (IconSourceKind.Throws, IconPlacement.BackButtonIconOverride),
            "flyouticon-canceled-by-maui" => (IconSourceKind.SlowThenValid, IconPlacement.FlyoutIcon),
            "flyouticon-slow" => (IconSourceKind.SlowThenValid, IconPlacement.FlyoutIcon),
            "control-image-throws" => (IconSourceKind.Throws, IconPlacement.ImageAndToolbarItem),
            _ => throw new ArgumentException($"Unknown case '{caseName}'")
        };

        return new Scenario(caseName, kind, placement, usesWorkaround, baseName == "flyouticon-canceled-by-maui");
    }

    public ImageSource CreateIconSource()
    {
        var requestCount = 0;

        async Task<Stream> Load(CancellationToken cancellationToken)
        {
            var requestNumber = Interlocked.Increment(ref requestCount);
            Log.Line("STREAM_REQUESTED", Name, ("n", requestNumber.ToString()));
            firstStreamRequested.TrySetResult();

            switch (Kind)
            {
                case IconSourceKind.Throws:
                    throw new IOException("simulated offline");
                case IconSourceKind.SlowThenValid:
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    break;
            }

            Log.Line("STREAM_SERVED", Name, ("n", requestNumber.ToString()));
            return new MemoryStream(TransparentPng);
        }

        return UsesWorkaround ? new SafeStreamImageSource(Load, Name) : ImageSource.FromStream(Load);
    }
}
