using System;
using I2PCore.Data;
using I2PCore.TunnelLayer;
using I2PCore.TunnelLayer.I2NP.Data;
using I2PCore.TunnelLayer.I2NP.Messages;
using I2PCore.Utils;

namespace I2PCore.SessionLayer
{
    public partial class ClientDestination : IClient
    {
        class SendPreconditionState
        {
            public ClientStates ClientState;
            public OutboundTunnel OutTunnel;
            public ILease RemoteLease;
            public ILeaseSet RemoteLeaseSet;
        }

        SendPreconditionState CheckSendPreconditions( I2PIdentHash dest )
        {
            if ( InboundEstablishedPool.IsEmpty )
            {
                return new SendPreconditionState { ClientState = ClientStates.NoTunnels };
            }

            var outtunnel = SelectOutboundTunnel();

            if ( outtunnel is null )
            {
                return new SendPreconditionState { ClientState = ClientStates.NoTunnels };
            }

            var leaseset = MySessions.GetLeaseSet( dest );

            if ( leaseset is null )
            {
                return new SendPreconditionState { ClientState = ClientStates.NoLeases };
            }

            var l = MySessions.GetTunnelPair( dest, outtunnel );

            if ( l is null )
            {
                return new SendPreconditionState { ClientState = ClientStates.NoLeases };
            }

            Logging.LogDebug( $"{this}: CheckSendPreconditions: Using tunnels: {outtunnel} -> {l}" );

            return new SendPreconditionState
                            {
                                ClientState = ClientStates.Established,
                                OutTunnel = outtunnel,
                                RemoteLease = l,
                                RemoteLeaseSet = leaseset,
                            };
        }

        /// <summary>
        /// Send cloves to the destination through a local out tunnel
        /// after encrypting them for the Destination.
        /// </summary>
        /// <returns>The send.</returns>
        /// <param name="dest">The Destination</param>
        /// <param name="cloves">Cloves</param>
        internal ClientStates Send( I2PDestination dest, params GarlicClove[] cloves )
        {
            if ( Terminated ) throw new InvalidOperationException( $"Destination {this} is terminated." );

            var replytunnel = SelectInboundTunnel();

            var remoteleases = MySessions.GetLeaseSet( dest.IdentHash );
            if ( remoteleases is null )
            { 
                return ClientStates.NoLeases;
            }

            var remotepubkeys = remoteleases
                    .PublicKeys;

            var msg = MySessions.Encrypt(
                dest.IdentHash,
                remotepubkeys,
                SignedLeases,
                replytunnel,
                cloves );

            return Send( dest, msg );
        }

        /// <summary>
        /// Send a I2NPMessage to the Destination through a local out tunnel.
        /// </summary>
        /// <returns>The send.</returns>
        /// <param name="dest">The Destination</param>
        /// <param name="msg">I2NPMessage</param>
        internal ClientStates Send( I2PDestination dest, I2NPMessage msg )
        {
            if ( Terminated ) throw new InvalidOperationException( $"This Destination {this} is terminated." );

            var result = CheckSendPreconditions( dest.IdentHash );

            if ( result.ClientState != ClientStates.Established )
            {
                switch ( result.ClientState )
                {
                    case ClientStates.NoTunnels:
                        Logging.LogDebug( $"{this}: No inbound tunnels available." );
                        break;

                    case ClientStates.NoLeases:
                        Logging.LogDebug( $"{this}: No leases available." );
                        LookupDestination( dest.IdentHash, HandleDestinationLookupResult, null );
                        break;
                }
                return result.ClientState;
            }

            // Remote leases getting old?
            var newestlease = result.RemoteLeaseSet.Expire;
            var leasehorizon = newestlease - DateTime.UtcNow;

            if ( leasehorizon.TotalSeconds < 0 )
            {
#if !LOG_ALL_LEASE_MGMT
                Logging.LogDebug( $"{this} Send: Leases for {dest.IdentHash.Id32Short} have all expired ({Tunnel.TunnelLifetime}). Looking up." );
#endif
                LookupDestination( dest.IdentHash, HandleDestinationLookupResult, null );
                return ClientStates.NoLeases;
            }
            else if ( leasehorizon < MinLeaseLifetime )
            {
#if !LOG_ALL_LEASE_MGMT
                Logging.LogDebug( $"{this} Send: Leases for {dest.IdentHash.Id32Short} is getting old ({leasehorizon}). Looking up." );
#endif
                LookupDestination( dest.IdentHash, HandleDestinationLookupResult, null );
            }

            result.OutTunnel.Send(
                new TunnelMessageTunnel(
                    msg,
                    result.RemoteLease.TunnelGw, result.RemoteLease.TunnelId ) );

            return ClientStates.Established;
        }
    }
}