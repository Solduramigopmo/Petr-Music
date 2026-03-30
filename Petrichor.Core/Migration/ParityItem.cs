namespace Petrichor.Core.Migration;

public sealed record ParityItem(
    string Area,
    string Feature,
    string MacOsSource,
    string WindowsStrategy,
    ParityStatus Status);
