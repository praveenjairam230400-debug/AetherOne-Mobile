namespace AetherOnePC;
internal static class ResourcePolicy
{
    internal const long MiB=1048576;
    internal static long Budget(long physicalBytes,bool pressure)=>pressure?192*MiB:Math.Clamp(physicalBytes/32,256*MiB,512*MiB);
    internal static bool Pressure(long available,long total)=>total>0&&(available<768*MiB||available<(total/8));
    internal static bool NeedsReclaim(long usage,long budget)=>usage>budget+budget/8;
    internal static double DiscardPriority(TimeSpan idle,long reloadMs,int visits)=>idle.TotalSeconds-Math.Min(reloadMs,30000)/1000.0*8-Math.Min(visits,20)*8;
    internal static bool Eligible(TimeSpan idle,bool pressure)=>idle>=TimeSpan.FromSeconds(pressure?20:120);
}
