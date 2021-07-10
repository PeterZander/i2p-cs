using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using I2PCore.Data;
using I2PCore.TunnelLayer;
using I2PCore.Utils;

namespace I2PCore.SessionLayer
{
    public partial class ClientDestination : IClient
    {
        TickCounter LastSignedLeasesFloodfillUpdate = TickCounter.Now;
        SemaphoreSlim UpdateFloodfillsWithSignedLeasesOnce = new SemaphoreSlim( 1, 1 );

        internal void UpdateFloodfillsWithSignedLeases()
        {
            if ( !UpdateFloodfillsWithSignedLeasesOnce.Wait( 0 ) )
            {
                return;
            }

            if ( LastSignedLeasesFloodfillUpdate.DeltaToNow < MinTimeBetweenFloodfillLSUpdates )
            {
                ThreadPool.QueueUserWorkItem( async a =>
                {
                    try
                    {
                        await Task.Delay( MinTimeBetweenFloodfillLSUpdates.ToMilliseconds );
                        LastSignedLeasesFloodfillUpdate.SetNow();
                        NetDb.Inst.FloodfillUpdate.TrigUpdateLeaseSet( SignedLeases );
                    }
                    finally
                    {
                        UpdateFloodfillsWithSignedLeasesOnce.Release();
                    }
                } );
                
                return;
            }

            try
            {
                LastSignedLeasesFloodfillUpdate.SetNow();
                NetDb.Inst.FloodfillUpdate.TrigUpdateLeaseSet( SignedLeases );
            }
            finally
            {
                UpdateFloodfillsWithSignedLeasesOnce.Release();
            }
        }

        internal void UpdateSignedLeases()
        {
            var newleases = EstablishedLeasesField
                .Where( l => ( l.Expire - DateTime.UtcNow ).TotalMinutes > 2 )
                .ToArray();

            if ( ThisDestinationInfo is null )
            {
                try
                {
                    SignLeasesRequest?.Invoke( this, newleases );
                }
                catch ( Exception ex )
                {
                    Logging.LogDebug( ex );
                }
            }
            else
            {
                // Auto sign
                if ( PrivateKeys.Any( pk => pk.Certificate.PublicKeyType != I2PKeyType.KeyTypes.ElGamal2048 ) )
                {
                    SignedLeases = new I2PLeaseSet2(
                        Destination,
                        newleases.Select( l => new I2PLease2( l.TunnelGw, l.TunnelId, new I2PDateShort( l.Expire ) ) ),
                        MySessions.PublicKeys,
                        Destination.SigningPublicKey,
                        ThisDestinationInfo.PrivateSigningKey );
                }
                else
                {
                    SignedLeases = new I2PLeaseSet(
                        Destination,
                        newleases.Select( l => new I2PLease( l.TunnelGw, l.TunnelId, new I2PDate( l.Expire ) ) ),
                        MySessions.PublicKeys[0],
                        Destination.SigningPublicKey,
                        ThisDestinationInfo.PrivateSigningKey );
                }
            }
        }

        internal void AddTunnelToEstablishedLeaseSet( InboundTunnel tunnel )
        {
            EstablishedLeasesField.Add( new I2PLease2( tunnel.Destination,
                            tunnel.GatewayTunnelId ) );

            UpdateSignedLeases();
        }

        internal virtual void RemoveTunnelFromEstablishedLeaseSet( InboundTunnel tunnel )
        {
            EstablishedLeasesField = new List<ILease>( 
                EstablishedLeasesField
                    .Where( l => l.TunnelGw != tunnel.Destination || l.TunnelId != tunnel.GatewayTunnelId ) );
        }

        [Conditional( "DEBUG" )]
        protected void TestLeaseSet( ILeaseSet ls )
        {
            try
            {
                if ( ls is I2PLeaseSet )
                {
                    var test1 = new I2PLeaseSet( new BufRefLen( ls.ToByteArray() ) );
                }
                else
                {
                    var test2 = new I2PLeaseSet2( new BufRefLen( ls.ToByteArray() ) );
                }
            } 
            catch( Exception ex )
            {
                Logging.LogDebug( $"{this}: Signature mismatch {ex}" );
            }
        }

        protected static bool IsSameLeaseSet( ILeaseSet ls1, ILeaseSet ls2 )
        {
            if ( ls1 != null && ls2 != null
                && ls1.Leases.Count() == ls2.Leases.Count()
                && ls1.Leases.All( l => 
                    ls2.Leases.Any( 
                            l2 => l.TunnelGw == l2.TunnelGw 
                                    && l.TunnelId == l2.TunnelId ) ) )
            {
                return true;
            }

            return false;
        }
        
        void Ext_LeaseSetUpdates( ILeaseSet ls )
        {
#if DEBUG
            Logging.LogTransport( $"{this} Ext_LeaseSetUpdates: {ls} {ls.Destination.IdentHash.Id32Short} {ls.Expire}" );
#endif            
            MySessions.LeaseSetReceived( ls );
        }

    }
}