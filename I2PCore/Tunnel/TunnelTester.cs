using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using I2PCore.Tunnel.I2NP.Data;
using System.Threading;
using I2PCore.Tunnel.I2NP.Messages;
using I2PCore.Utils;

namespace I2PCore.Tunnel
{
    public class TunnelTester
    {
        public static readonly TickSpan MaxTestRunTime = TickSpan.Minutes( 5 );
        public static readonly TickSpan TimeBetweenTests = TickSpan.Minutes( 2 );
        public const int RunsPerTest = 7;
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

        TimeWindowDictionary<Tunnel, TunnelTestResult> TestResults = new TimeWindowDictionary<Tunnel, TunnelTestResult>( TickSpan.Minutes( 10 ) );

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

        LinkedList<OutboundTunnel> OutboundTunnels = new LinkedList<OutboundTunnel>();
        LinkedList<InboundTunnel> InboundTunnels = new LinkedList<InboundTunnel>();

        TimeWindowDictionary<Tunnel, TestRun2> OutstandingTests = new TimeWindowDictionary<Tunnel, TestRun2>( MaxTestRunTime );
        TimeWindowDictionary<uint, ProbesSent> OutstandingProbeIds = new TimeWindowDictionary<uint, ProbesSent>( MaxTestRunTime );

        public TunnelTester()
        {
            InboundTunnel.DeliveryStatusReceived += new Action<DeliveryStatusMessage>( InboundTunnel_DeliveryStatusReceived );

            Worker = new Thread( () => Run() );
            Worker.Name = "TunnelTester";
            Worker.IsBackground = true;
            Worker.Start();
        }

        object ExternalEventSync = new object();

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
                    DebugUtils.Log( ex );
                }
            }
        }

        void TestOneInboundTunnel()
        {
            InboundTunnel intunnel;
            lock ( InboundTunnels )
            {
                if ( InboundTunnels.Count == 0 ) return;
                intunnel = InboundTunnels.First.Value;
                InboundTunnels.RemoveFirst();

                if ( intunnel.NeedsRecreation ) return;
                InboundTunnels.AddLast( intunnel );
            }

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

            var outtunnels = TunnelProvider.Inst.GetOutboundTunnels().Shuffle().Take( RunsPerTest ).ToArray(); ;
            if ( !outtunnels.Any() )
            {
                //DebugUtils.LogDebug( "TunnelTester: Failed to get a established outbound tunnel." );
                return;
            }

            run.LastRun.SetNow();

            string tunneldbginfo = "";

            foreach ( var outtunnel in outtunnels )
            {
                if ( run.FailurePartners.Contains( outtunnel ) || 
                    run.SuccessPartners.Contains( outtunnel ) || 
                    run.TestedPartners.Contains( outtunnel ) ) continue;

                run.TestedPartners.Add( outtunnel );
                var probe = new ProbesSent()
                {
                    Tunnel = intunnel,
                    Partner = outtunnel,
                    MessageId = I2NPHeader.GenerateMessageId(),
                    TotalHops = outtunnel.Config.Info.Hops.Count + intunnel.Config.Info.Hops.Count
                };

                tunneldbginfo += $"({outtunnel.TunnelDebugTrace}:{probe.MessageId})";

                run.OutstandingProbes.Add( probe );
                OutstandingProbeIds[probe.MessageId] = probe;
                outtunnel.Send( new TunnelMessageTunnel( ( new DeliveryStatusMessage( probe.MessageId ) ).Header16, intunnel ) );
            }

            if ( tunneldbginfo.Length > 0 )
                DebugUtils.LogDebug(
                    $"TunnelTester: Starting inbound tunnel {intunnel.TunnelDebugTrace} test with tunnels: {tunneldbginfo}" );
        }

        void TestOneOutboundTunnel()
        {
            OutboundTunnel outtunnel;
            lock ( OutboundTunnels )
            {
                if ( OutboundTunnels.Count == 0 ) return;
                outtunnel = OutboundTunnels.First.Value;
                OutboundTunnels.RemoveFirst();

                if ( outtunnel.NeedsRecreation ) return;
                OutboundTunnels.AddLast( outtunnel );
            }

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

            var intunnels = TunnelProvider.Inst.GetInboundTunnels().Shuffle().Take( RunsPerTest * 2 ).ToArray();
            if ( !intunnels.Any() )
            {
                //DebugUtils.LogDebug( "TunnelTester: Failed to get a established inbound tunnel." );
                return;
            }

            run.LastRun.SetNow();

            string tunneldbginfo = "";

            foreach ( var intunnel in intunnels )
            {
                if ( run.FailurePartners.Contains( intunnel ) ||
                    run.SuccessPartners.Contains( intunnel ) ||
                    run.TestedPartners.Contains( intunnel ) ) continue;

                run.TestedPartners.Add( intunnel );
                var probe = new ProbesSent()
                {
                    Tunnel = outtunnel,
                    Partner = intunnel,
                    MessageId = I2NPHeader.GenerateMessageId(),
                    TotalHops = outtunnel.Config.Info.Hops.Count + intunnel.Config.Info.Hops.Count
                };

                tunneldbginfo += $"({intunnel.TunnelDebugTrace}:{probe.MessageId})";

                run.OutstandingProbes.Add( probe );
                OutstandingProbeIds[probe.MessageId] = probe;
                outtunnel.Send( new TunnelMessageTunnel( ( new DeliveryStatusMessage( probe.MessageId ) ).Header16, intunnel ) );
            }

            if ( tunneldbginfo.Length > 0 )
                DebugUtils.LogDebug(
                    $"TunnelTester: Starting outbound tunnel {outtunnel.TunnelDebugTrace} test with tunnels: {tunneldbginfo}" );
        }

        void InboundTunnel_DeliveryStatusReceived( DeliveryStatusMessage dstatus )
        {
            var probe = OutstandingProbeIds[dstatus.MessageId];
            if ( probe == null ) return;

            var run = OutstandingTests[probe.Tunnel];
            if ( run == null ) return;

            var testms = ( DateTime.UtcNow - (DateTime)dstatus.Timestamp ).TotalMilliseconds;

            var limit = PassTestTimePerHop.ToMilliseconds * probe.TotalHops;
            var pass = testms < limit;

            DebugUtils.LogDebug( string.Format( "TunnelTester: DeliveryStatus received. Test with {0} and {1}: {2}. {3:0} vs {4} ms",
                run.TunnelUnderTest.TunnelDebugTrace, probe.Partner.TunnelDebugTrace,
                ( pass ? "Success" : "Fail" ),
                testms, limit ) );

            var delta = TickSpan.Milliseconds( (int)testms );

            if ( probe.Tunnel.MinLatencyMeasured == null || probe.Tunnel.MinLatencyMeasured > delta ) 
                probe.Tunnel.MinLatencyMeasured = delta;
            if ( probe.Partner.MinLatencyMeasured == null || probe.Partner.MinLatencyMeasured > delta )
                probe.Partner.MinLatencyMeasured = delta;

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

            DebugUtils.LogDebug( "TunnelTester: Test with " +
                run.TunnelUnderTest.TunnelDebugTrace + " and " + probe.Partner.TunnelDebugTrace + " timeout." );

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

            if ( run.SuccessPartners.Count > 0 )
            {
                ++testresult.Pass;
            }
            else
            {
                ++testresult.Fail;
            }

            if ( run.TestedPartners.Count < RunsPerTest ) return;

            if ( testresult.Pass > 0 && testresult.Pass * 2 >= testresult.Fail )
            {
                DebugUtils.LogDebug( string.Format( "TunnelTester: Run result: Tunnel {0} passed tests. Successes: {1}, Failures {2}.",
                    run.TunnelUnderTest, testresult.Pass, testresult.Fail ) );

                foreach ( var onehop in run.TunnelUnderTest.TunnelMembers )
                {
                    NetDb.Inst.Statistics.SuccessfulTunnelTest( onehop.IdentHash );
                }

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

                return;
            }

            // Fail and remove
            foreach ( var onehop in run.TunnelUnderTest.TunnelMembers )
            {
                NetDb.Inst.Statistics.FailedTunnelTest( onehop.IdentHash );
            }

            foreach ( var onepartner in run.FailurePartners )
            {
                foreach ( var onehop in onepartner.TunnelMembers )
                {
                    NetDb.Inst.Statistics.FailedTunnelTest( onehop.IdentHash );
                }
            }

            if ( run.TunnelUnderTest is InboundTunnel )
            {
                lock ( InboundTunnels )
                {
                    InboundTunnels.RemoveAll( t => t == run.TunnelUnderTest );
                }
            }
            else
            {
                lock ( OutboundTunnels )
                {
                    OutboundTunnels.RemoveAll( t => t == run.TunnelUnderTest );
                }
            }

            DebugUtils.LogDebug( string.Format( "TunnelTester: Run result: Tunnel {0} failed tests and is removed. Successes: {1}, Failures {2}.",
                run.TunnelUnderTest, testresult.Pass, testresult.Fail ) );

            TunnelProvider.Inst.TunnelTestFailed( run.TunnelUnderTest );
            run.TunnelUnderTest.Shutdown();
        }

        public void Test( OutboundTunnel tunnel )
        {
            if ( tunnel == null ) return;

            var test = TestResults.Get( tunnel );
            if ( test != null && test.Pass > 0 ) return;
            if ( OutstandingTests.Get( tunnel ) != null ) return;

            lock ( OutboundTunnels )
            {
                OutboundTunnels.AddLast( tunnel );
            }
            TunnelAdded.Set();
        }

        public void Test( InboundTunnel tunnel )
        {
            if ( tunnel == null ) return;

            var test = TestResults.Get( tunnel );
            if ( test != null && test.Pass > 0 ) return;
            if ( OutstandingTests.Get( tunnel ) != null ) return;

            lock ( InboundTunnels )
            {
                InboundTunnels.AddLast( tunnel );
            }
            TunnelAdded.Set();
        }
    }
}
