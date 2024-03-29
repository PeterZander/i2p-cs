﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using I2PCore.Data;
using I2PCore.Utils;

namespace I2PCore
{
    public partial class NetDb
    {
        [Conditional( "DEBUG" )]
        private void ShowDebugDatabaseInfo()
        {
            Logging.LogDebug( $"NetDb: Router count: {RouterInfos.Count}" );

            var nohost = RouterInfos
                .Where( ri => !ri.Value.Router.Adresses.Any( a =>
                    a.Options.TryGet( "host" ) != null ) );
            Logging.LogDebug( $"NetDb: No host: {nohost.Count()}" );

            var ipv4 = RouterInfos
                .Where( ri => ri.Value.Router.Adresses.Any( a =>
                    a.Options.TryGet( "host" )?.ToString().Contains( "." ) ?? false ) );
            Logging.LogDebug( $"NetDb: IPV4: {ipv4.Count()}" );

            var ipv6 = RouterInfos
                .Where( ri => ri.Value.Router.Adresses.Any( a =>
                    a.Options.TryGet( "host" )?.ToString().Contains( ":" ) ?? false ) );
            Logging.LogDebug( $"NetDb: IPV6: {ipv6.Count()}" );

            var onlyipv4 = RouterInfos
                .Where( ri =>
                    ri.Value.Router.Adresses.Any( a =>
                        a.Options.TryGet( "host" )?.ToString().Contains( "." ) ?? false )
                    && !ri.Value.Router.Adresses.Any( a =>
                        a.Options.TryGet( "host" )?.ToString().Contains( ":" ) ?? false ) );
            Logging.LogDebug( $"NetDb: Only IPV4: {onlyipv4.Count()}" );

            var onlyipv6 = RouterInfos
                .Where( ri =>
                    ri.Value.Router.Adresses.Any( a =>
                        a.Options.TryGet( "host" )?.ToString().Contains( ":" ) ?? false )
                    && !ri.Value.Router.Adresses.Any( a =>
                        a.Options.TryGet( "host" )?.ToString().Contains( "." ) ?? false ) );
            Logging.LogDebug( $"NetDb: Only IPV6: {onlyipv6.Count()}" );

            var transportstyles = RouterInfos
                .SelectMany( ri => ri.Value.Router.Adresses.Select( a => a.TransportStyle.ToString() ) )
                .Distinct();

            foreach ( var style in transportstyles )
            {
                var ts = RouterInfos
                    .Where( ri =>
                        ri.Value.Router.Adresses.Any( a => a.TransportStyle == style ) );
                Logging.LogDebug( $"NetDb: {style}: {ts.Count()}" );
            }

            foreach ( var style in transportstyles )
            {
                var onlyts = RouterInfos
                    .Where( ri =>
                        ri.Value.Router.Adresses.Any( a => a.TransportStyle == style )
                            && ri.Value.Router.Adresses.All( a => a.TransportStyle == style ) );
                Logging.LogDebug( $"NetDb: Only {style}: {onlyts.Count()}" );
            }

            var versions = RouterInfos
                    .Where( ri => ri.Value.CachedStatistics != null
                         && ( ri.Value?.Router.Options.Contains( "router.version" ) ?? false ) )
                    .GroupBy( ri => ri.Value.Router.Options["router.version"] )
                    .Select( g => new {
                        Version = g.Key,
                        Count = g.Count(),
                        AvgScore = g.Average( ri => ri.Value.CachedStatistics.Score )
                    } )
                    .OrderBy( rv => rv.AvgScore )
                    .ToArray();

            foreach ( var version in versions )
            {
                Logging.LogDebug( $"NetDb: Version {version.Version,10} [{version.Count,6}] Avg score: {version.AvgScore,8:F2}" );
            }
        }

        private void ShowProbabilityProfile()
        {
            var l = new List<I2PIdentHash>();
            for ( int i = 0; i < 1000; ++i )
            {
                l.Add( GetRandomRouter( Roulette, null, false ) );
            }
            var lines = l.GroupBy( i => i ).OrderByDescending( g => g.Count() );
            foreach ( var one in lines )
            {
                var ri = Roulette.Wheel.First( r => r.Id == one.Key );
                Logging.LogInformation( $"R: {one.Count(),5}: ({ri.Fit,8:#0.00} {ri.Space,12:#0.0} ) {one.Key}" );
            }
        }

        private void ShowRouletteStatistics( RouletteSelection<I2PRouterInfo, I2PIdentHash> roulette )
        {
            if ( Logging.LogLevel > Logging.LogLevels.Information ) return;
            if ( !roulette.Wheel.Any() ) return;

            float Mode = 0f;
            var bins = 20;
            var hist = roulette.Wheel.Histogram( sp => sp.Fit, bins );
            var maxcount = hist.Max( b => b.Count );
            if ( hist.Count() == bins && !hist.All( b => Math.Abs( b.Start ) < 0.01f ) )
            {
                Mode = hist.First( b => b.Count == maxcount ).Start + ( hist[1].Start - hist[0].Start ) / 2f;
            }

            Logging.LogInformation(
                $"Roulette stats: Count {roulette.Wheel.Count()}, " +
                $"Min {roulette.MinFit:0.00}, " +
                $"Avg {roulette.AverageFit:0.00}, " +
                $"Mode {Mode:0.00}, Max {roulette.MaxFit:0.00}, " +
                $"Absdev: {roulette.AbsDevFit:0.00}, " +
                $"Stddev: {roulette.StdDevFit:0.00}, " +
                $"Skew {roulette.Wheel.Skew( sp => sp.Fit ):0.00}" );

            if ( Logging.LogLevel > Logging.LogLevels.Debug ) return;

            var ix = 0;
            if ( maxcount > 0 ) foreach ( var line in hist )
                {
                    var st = "";
                    for ( int i = 0; i < ( 40 * line.Count ) / maxcount; ++i ) st += "*";
                    Logging.LogInformation( $"Roulette stats {line.Start,6:#0.0} ({line.Count,5}): {st}" );
                    ++ix;
                }

#if DEBUG
            var delta = roulette.AbsDevFit / 2;
            var min = roulette.Wheel.Where( sp => Math.Abs( sp.Fit - roulette.MinFit ) < delta ).Take( 10 );
            var avg = roulette.Wheel.Where( sp => Math.Abs( sp.Fit - roulette.AverageFit ) < delta ).Take( 10 );
            var max = roulette.Wheel.Where( sp => Math.Abs( sp.Fit - roulette.MaxFit ) < delta ).Take( 10 );

            if ( min.Any() && avg.Any() && max.Any() )
            {
                var mins = min.Aggregate( new StringBuilder(), ( l, r ) =>
                { if ( l.Length > 0 ) l.Append( ", " ); l.Append( r.Id.Id32Short ); return l; } );
                var maxs = max.Aggregate( new StringBuilder(), ( l, r ) =>
                { if ( l.Length > 0 ) l.Append( ", " ); l.Append( r.Id.Id32Short ); return l; } );

                Logging.LogDebug( String.Format( "Roulette stats minfit: {0}, maxfit: {1}", mins, maxs ) );

                var minexinst = min.Random();
                var medexinst = avg.Random();
                var maxexinst = max.Random();

                var minex = NetDb.Inst.Statistics[minexinst.Id];
                var medex = NetDb.Inst.Statistics[medexinst.Id];
                var maxex = NetDb.Inst.Statistics[maxexinst.Id];

                Logging.LogDebug( $"Min example: Space {minexinst.Space,10:F2} {minex}" );
                Logging.LogDebug( $"Med example: Space {medexinst.Space,10:F2} {medex}" );
                Logging.LogDebug( $"Max example: Space {maxexinst.Space,10:F2} {maxex}" );
            }
#endif
        }
    }
}
