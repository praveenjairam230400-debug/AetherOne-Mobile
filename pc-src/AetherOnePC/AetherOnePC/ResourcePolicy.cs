namespace AetherOnePC;
internal static class ResourcePolicy
{
    internal const long MiB=1048576;
    internal static long Budget(long physicalBytes,bool pressure)=>pressure?192*MiB:Math.Clamp(physicalBytes/32,256*MiB,512*MiB);
    internal static bool Pressure(long available,long total)=>total>0&&(available<768*MiB||available<(total/8));
    internal static bool NeedsReclaim(long usage,long budget)=>usage>budget+budget/8;
    internal static bool AboveReclaimWatermark(long usage, long budget) =>
        budget > 0 && usage / budget >= 2;

    internal static double DiscardPriority(TimeSpan idle,long reloadMs,int visits)=>idle.TotalSeconds-Math.Min(reloadMs,30000)/1000.0*8-Math.Min(visits,20)*8;
    internal static string Mode(string? requested)=>requested is "Performance" or "Memory saver" ? requested : "Adaptive";
    internal static TimeSpan MinimumIdle(bool pressure,string? mode)=>
        TimeSpan.FromSeconds(pressure?15:Mode(mode)=="Performance"?120:Mode(mode)=="Memory saver"?30:45);
    internal static TimeSpan LongIdle(string? mode)=>
        TimeSpan.FromMinutes(Mode(mode)=="Performance"?10:Mode(mode)=="Memory saver"?2:3);
    internal static bool Eligible(TimeSpan idle,bool pressure)=>idle>=MinimumIdle(pressure,"Adaptive");
    // Avoid costly WebView disposal/checkpoint work while a foreground page is loading,
    // or immediately after the user switches tabs, even under memory pressure.
    // Pressure shortens the grace period but does not override foreground protection.
    internal static bool CanReclaimNow(bool pressure,bool activeLoading,TimeSpan sinceActivation,double cpuFraction)
    {
        if(activeLoading)return false;
        if(sinceActivation<TimeSpan.FromSeconds(pressure?1:3))return false;
        if(double.IsNaN(cpuFraction)||double.IsInfinity(cpuFraction))return false;
        return cpuFraction<(pressure?0.70:0.35);
    }
    internal static int MaxReleasesPerScan(bool pressure,string? mode)=>
        pressure?3:Mode(mode)=="Memory saver"?2:1;
}
