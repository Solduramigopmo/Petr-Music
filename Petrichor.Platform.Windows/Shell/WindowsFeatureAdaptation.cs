namespace Petrichor.Platform.Windows.Shell;

public sealed record WindowsFeatureAdaptation(
    string MacOsFeature,
    string WindowsEquivalent,
    bool RequiresBehaviorChange);
