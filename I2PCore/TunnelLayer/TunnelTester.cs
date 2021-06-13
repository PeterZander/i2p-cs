using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.TunnelLayer.I2NP.Data;
using System.Threading;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Utils;
using System.Collections.Concurrent;

namespace I2PCore.TunnelLayer
{
    public class TunnelTester
    {
        public static readonly TickSpan MaxTestRunTime = TickSpan.Minutes( 5 );
        public static readonly TickSpan TimeBetweenTests = TickSpan.Minutes( 2 );
        public const int RunsPerTest = 5;
        public static readonly TickSpan PassTestTimePerHop = TickSpan.Milliseconds( 3300 );

        public static TunnelTester Inst = new TunnelTester();
        protected static Thread Worker;

        class TunnelTestResult
        {
            Tunnel TestedTunnel;

            public TickCounter Created = TickCounter.Now;
            public int Pass;
            public int Fail;

            internal TunnelTestResult( Tunnel tunnel )
            {
                TestedTunnel = tunnel;
            }
        }

        TimeWindowDictionary<Tunnel, TunnelTestResult> TestResults = 
                new TimeWindowDictionary<Tunnel, TunnelTestResult>( TickSpan.Minutes( 10 ) );

        class ProbesSent
        {
            public TickCounter Created = TickCounter.Now;
            public int TotalHops;
            public Tunnel Tunnel;
            public Tunnel Partner;
            public uint MessageId;
        }

        class TestRun2
        {
            public TickCounter Created = TickCounter.Now;
            public TickCounter LastRun = TickCounter.MaxDelta;

            public readonly Tunnel TunnelUnderTest;

            public readonly List<ProbesSent> OutstandingProbes = new List<ProbesSent>();
            public readonly HashSet<Tunnel> FailurePartners = new HashSet<Tunnel>();
            public readonly HashSet<Tunnel> SuccessPartners = new HashSet<Tunnel>();
            public readonly HashSet<Tunnel> TestedPartners = new HashSet<Tunnel>();

            internal TestRun2( Tunnel testtunnel )
            {
                TunnelUnderTest = testtunnel;
            }
        }

        ConcurrentQueue<OutboundTunnel> OutboundTunnels = new ConcurrentQueue<OutboundTunnel>();
        ConcurrentQueue<InboundTunnel> InboundTunnels = new ConcurrentQueue<InboundTunnel>();

        TimeWindowDictionary<Tunnel, TestRun2> OutstandingTests = 
                new TimeWindowDictionary<Tunnel, TestRun2>( MaxTestRunTime );
        TimeWindowDictionary<uint, ProbesSent> OutstandingProbeIds = 
                new TimeWindowDictionary<uint, ProbesSent>( MaxTestRunTime );

        public TunnelTester()
        {
            InboundTunnel.DeliveryStatusReceived += InboundTunnel_DeliveryStatusReceived;

            Worker = new Thread( Run )
            {
                Name = "TunnelTester",
                IsBackground = true
            };
            Worker.Start();
        }

        readonly object ExternalEventSync = new object();

        AutoResetEvent TunnelAdded = new AutoResetEvent( false );
        bool Terminated = false;
        void Run()
        {
            while ( !Terminated )
            {
                try
                {
                    TunnelAdded.WaitOne( TimeBetweenTests.ToMilliseconds / ( 20 * ( InboundTunnels.Count + OutboundTunnels.Count + 1 ) ) );

                    lock ( ExternalEventSync )
                    {
                        if ( InboundTunnels.Count > 0 ) TestOneInboundTunnel();
                        if ( OutboundTunnels.Count > 0 ) TestOneOutboundTunnel();

                        ProbesSent[] timeout = null;

                        timeout = OutstandingProbeIds.Where( p => p.Value.Created.DeltaToNow / p.Value.TotalHops > PassTestTimePerHop ).
                            Select( p => p.Value ).ToArray();
                        foreach ( var one in timeout ) DeliveryStatusTimeOut( one );
                    }
                }
                catch ( Exception ex )
                {
                    Logging.Log( ex );
                }
            }
        }

        void TestOneInboundTunnel()
        {
            if ( InboundTunnels.IsEmpty ) return;

            if ( !InboundTunnels.TryDequeue( out var intunnel ) ) return;
            if ( !intunnel.Active || intunnel.Metrics.PassedTunnelTest ) return;

            InboundTunnels.Enqueue( intunnel );

            var run = OutstandingTests.Get( intunnel );
            if ( run != null )
            {
                // Run has finished?
                if ( run.TestedPartners.Count >= RunsPerTest )
                {
                    if ( run.LastRun.DeltaToNow < TimeBetweenTests ) return;
                    run.OutstandingProbes.Clear();
                    run.SuccessPartners.Clear();
                    run.TestedPartners.Clear();
                }
            }
            else
            {
                run = new TestRun2( intunnel );
                OutstandingTests[intunnel] = run;
            }

            var test = TestResults.Get( intunnel, () => new TunnelTestResult( intunnel ) );

            var outtunnels = TunnelProvider.Inst.GetOutboundTunnels()
                .Where( t => t.Active
                    && !run.FailurePartners.Contains( t )
                    && !run.SuccessPartners.Contains( t )
                    && !run.TestedPartners.Contains( t ) )
                .Shuffle()
                .Take( RunsPerTest )
                .ToArray(); ;

            if ( !outtunnels.Any() )
            {
                //Logging.LogDebug( "TunnelTester: Failed to get a established outbound tunnel." );
                return;
            }

            run.LastRun.SetNow();

            string tunneldbginfo = "";

            foreach ( var outtunnel in outtunnels )
            {
                run.TestedPartners.Add( outtunnel );
                var probe = new ProbesSent()
                {
                    Tunnel = intunnel,
                    Partner = outtunnel,
                    MessageId = I2NPMessage.GenerateMessageId(),
                    TotalHops = outtunnel.Config.Info.Hops.Count + intunnel.Config.Info.Hops.Count
                };

                tunneldbginfo += $"({outtunnel.TunnelDebugTrace}:{probe.MessageId})";

                run.OutstandingProbes.Add( probe );
                OutstandingProbeIds[probe.MessageId] = probe;
                outtunnel.Send( new TunnelMessageTunnel( new DeliveryStatusMessage( probe.MessageId ), intunnel ) );
            }

            if ( tunneldbginfo.Length > 0 )
                Logging.LogDebug(
                    $"TunnelTester: Starting inbound tunnel {intunnel.TunnelDebugTrace} test with tunnels: {tunneldbginfo}" );
        }

        void TestOneOutboundTunnel()
        {
            if ( OutboundTunnels.IsEmpty ) return;

            if ( !OutboundTunnels.TryDequeue( out var outtunnel  ) ) return;
            if ( !outtunnel.Active || outtunnel.Metrics.PassedTunnelTest ) return;
            OutboundTunnels.Enqueue( outtunnel );

            var run = OutstandingTests.Get( outtunnel );
            if ( run != null )
            {
                // Run has finished?
                if ( run.TestedPartners.Count >= RunsPerTest )
                {
                    if ( run.LastRun.DeltaToNow < TimeBetweenTests ) return;
                    run.OutstandingProbes.Clear();
                    run.SuccessPartners.Clear();
                    run.TestedPartners.Clear();
                }
            }
            else
            {
                run = new TestRun2( outtunnel );
                OutstandingTests[outtunnel] = run;
            }

            var test = TestResults.Get( outtunnel, () => new TunnelTestResult( outtunnel ) );

            var intunnels = TunnelProvider.Inst.GetInboundTunnels()
                .Where( t => t.Active
                    && !run.FailurePartners.Contains( t )
                    && !run.SuccessPartners.Contains( t )
                    && !run.TestedPartners.Contains( t ) )
                .Shuffle()
                .Take( RunsPerTest * 2 )
                .ToArray();

            if ( !intunnels.Any() )
            {
                //Logging.LogDebug( "TunnelTester: Failed to get a established inbound tunnel." );
                return;
            }

            run.LastRun.SetNow();

            string tunneldbginfo = "";

            foreach ( var intunnel in intunnels )
            {
                run.TestedPartners.Add( intunnel );
                var probe = new ProbesSent()
                {
                    Tunnel = outtunnel,
                    Partner = intunnel,
                    MessageId = I2NPMessage.GenerateMessageId(),
                    TotalHops = outtunnel.Config.Info.Hops.Count + intunnel.Config.Info.Hops.Count
                };

                tunneldbginfo += $"({intunnel.TunnelDebugTrace}:{probe.MessageId})";

                run.OutstandingProbes.Add( probe );
                OutstandingProbeIds[probe.MessageId] = probe;
                outtunnel.Send( new TunnelMessageTunnel( new DeliveryStatusMessage( probe.MessageId ), intunnel ) );
            }

            if ( tunneldbginfo.Length > 0 )
                Logging.LogDebug(
                    $"TunnelTester: Starting outbound tunnel {outtunnel.TunnelDebugTrace} test with tunnels: {tunneldbginfo}" );
        }

        void InboundTunnel_DeliveryStatusReceived( DeliveryStatusMessage dstatus )
        {
            var probe = OutstandingProbeIds[dstatus.StatusMessageId];
            if ( probe == null ) return;

            var run = OutstandingTests[probe.Tunnel];
            if ( run == null ) return;

            var testms = ( DateTime.UtcNow - (DateTime)dstatus.Timestamp ).TotalMilliseconds;

            var limit = PassTestTimePerHop.ToMilliseconds * probe.TotalHops;
            var pass = testms < limit;

            Logging.LogDebugData( string.Format( "TunnelTester: DeliveryStatus received. Test with {0} and {1}: {2}. {3:0} vs {4} ms",
                run.TunnelUnderTest.TunnelDebugTrace, probe.Partner.TunnelDebugTrace,
                ( pass ? "Success" : "Fail" ),
                testms, limit ) );

            var delta = TickSpan.Milliseconds( (int)testms );

            probe.Tunnel.Metrics.UpdateMinLatency( delta );
            probe.Partner.Metrics.UpdateMinLatency( delta );

            lock ( ExternalEventSync )
            {
                if ( pass )
                {
                    run.SuccessPartners.Add( probe.Partner );
                }
                else
                {
                    run.FailurePartners.Add( probe.Partner );
                }
                run.OutstandingProbes.Remove( probe );
                OutstandingProbeIds.Remove( probe.MessageId );

                HandleTunnelTestResult( run );
            }
        }

        private void DeliveryStatusTimeOut( ProbesSent probe )
        {
            OutstandingProbeIds.Remove( probe.MessageId );

            var run = OutstandingTests[probe.Tunnel];
            if ( run == null ) return;

            Logging.LogDebug( $"TunnelTester: Test with {run.TunnelUnderTest.TunnelDebugTrace}" +
                $" and {probe.Partner.TunnelDebugTrace} timeout." );

            run.OutstandingProbes.Remove( probe );
            run.FailurePartners.Add( probe.Partner );

            lock ( ExternalEventSync )
            {
                HandleTunnelTestResult( run );
            }
        }

        private void HandleTunnelTestResult( TestRun2 run )
        {
            if ( run.OutstandingProbes.Count > 0 ) return;

            var testresult = TestResults.Get( run.TunnelUnderTest, () => new TunnelTestResult( run.TunnelUnderTest ) );

            var resultcount = run.SuccessPartners.Count + run.FailurePartners.Count + testresult.Pass + testresult.Fail;

            // Test run finished?
            if ( resultcount < RunsPerTest ) return;

            testresult.Pass += run.SuccessPartners.Count;
            testresult.Fail += run.FailurePartners.Count;

            // Collect statistics for the partner tunnel
            foreach ( var onepartner in run.SuccessPartners )
            {
                foreach ( var onehop in onepartner.TunnelMembers )
                {
                    NetDb.Inst.Statistics.SuccessfulTunnelTest( onehop.IdentHash );
                }

                var partnertestresult = TestResults.Get( onepartner, () => new TunnelTestResult( onepartner ) );
                ++partnertestresult.Pass;
            }

            foreach ( var onepartner in run.FailurePartners )
            {
                foreach ( var onehop in onepartner.TunnelMembers )
                {
                    NetDb.Inst.Statistics.FailedTunnelTest( onehop.IdentHash );
                }

                var partnertestresult = TestResults.Get( onepartner, () => new TunnelTestResult( onepartner ) );
                ++partnertestresult.Fail;
            }

            // Success
            if ( testresult.Pass > 0 && testresult.Pass >= testresult.Fail )
            {
                Logging.LogDebug( $"TunnelTester: Run result: Tunnel {run.TunnelUnderTest} passed tests. " +
                    $"Successes: {testresult.Pass}, Failures {testresult.Fail}." );

                run.TunnelUnderTest.Metrics.PassedTunnelTest = true;

                foreach ( var onehop in run.TunnelUnderTest.TunnelMembers )
                {
                    NetDb.Inst.Statistics.SuccessfulTunnelTest( onehop.IdentHash );
                }

                return;
            }

            // Fail and remove
            foreach ( var onehop in run.TunnelUnderTest.TunnelMembers )
            {
                NetDb.Inst.Statistics.FailedTunnelTest( onehop.IdentHash );
            }

            Logging.LogDebug( $"TunnelTester: Run result: Tunnel {run.TunnelUnderTest} failed tests and is removed. " +
                $"Successes: {testresult.Pass}, Failures {testresult.Fail}." );

            TunnelProvider.Inst.TunnelTestFailed( run.TunnelUnderTest );
            run.TunnelUnderTest.Shutdown();
        }

        public void Test( OutboundTunnel tunnel )
        {
            if ( tunnel == null ) return;

            var test = TestResults.Get( tunnel );
            if ( test != null && test.Pass > 0 ) return;
            if ( OutstandingTests.Get( tunnel ) != null ) return;

            OutboundTunnels.Enqueue( tunnel );

            TunnelAdded.Set();
        }

        public void Test( InboundTunnel tunnel )
        {
            if ( tunnel == null ) return;

            var test = TestResults.Get( tunnel );
            if ( test != null && test.Pass > 0 ) return;
            if ( OutstandingTests.Get( tunnel ) != null ) return;

            InboundTunnels.Enqueue( tunnel );

            TunnelAdded.Set();
        }
    }
}
