using System;

namespace WDI.Widgets.Clock;

public sealed class ClockService
{
    public DateTime GetCurrentTime() => DateTime.Now;
}
