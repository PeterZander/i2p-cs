using System;
using I2PCore.Data;
using I2PCore.Utils;

namespace I2PCore
{
    public partial class NetDb
    {
        protected class RouterEntry
        {
            public I2PRouterInfo Router { get; protected set; }
            public RouterInfoMeta Meta { get; protected set; }

            public RouterEntry( I2PRouterInfo info, RouterInfoMeta meta )
            {
                Router = info;
                Meta = meta;
            }

            private TickCounter ScoreAge = null;
            private RouterStatistics CachedStatisticsField;
            public RouterStatistics CachedStatistics
            {
                get
                {
                    if ( ScoreAge is null || ScoreAge.DeltaToNow > TickSpan.Minutes( 5 ) )
                    {
                        ScoreAge = TickCounter.Now;
                        CachedStatisticsField = NetDb.Inst.Statistics[Router.Identity.IdentHash];
                    }
                    return CachedStatisticsField;
                }
            }

            public bool IsFloodfill
            {
                get
                {
                    return Router.Options["caps"].IndexOf( 'f' ) >= 0;
                }
            }
        }
    }
}
