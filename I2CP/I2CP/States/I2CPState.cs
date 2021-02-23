using I2PCore;
using I2PCore.Utils;
using I2P.I2CP.Messages;

namespace I2P.I2CP.States
{
    public abstract class I2CPState
    {
        public static readonly TickSpan InactivityTimeout = TickSpan.Minutes( 20 );
        public static readonly TickSpan HandshakeTimeout = TickSpan.Seconds( 30 );
        public static readonly TickSpan EstablishedDestinationTimeout = TickSpan.Seconds( 150 );

        public TickCounter Created = TickCounter.Now;
        public TickCounter LastAction = TickCounter.Now;
        public int Retries = 0;

        protected I2CPSession Session;

        protected I2CPState( I2CPSession sess ) 
        { 
            Session = sess; 
        }

        protected bool Timeout( TickSpan timeout ) { return LastAction.DeltaToNow > timeout; }
        protected void DataSent() { LastAction.SetNow(); }

        internal virtual I2CPState Run()
        {
            if ( Timeout( HandshakeTimeout ) )
            {
                throw new FailedToConnectException( $"{this} WaitProtVer {Session.DebugId} Failed to connect. Timeout." );
            }

            return this;
        }

        internal abstract I2CPState MessageReceived( I2CPMessage msg );

        public override string ToString()
        {
            return $"{Session} {GetType().Name}";
        }
    }
}
