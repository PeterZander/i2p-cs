using I2PCore.Data;
using I2PCore.Utils;

namespace I2P.I2CP.Messages
{
    public class ReconfigureSessionMessage: I2CPMessage
    {
        public ushort SessionId;
        public I2PSessionConfig Config;

        public ReconfigureSessionMessage( I2PSessionConfig cfg ): base( ProtocolMessageType.ReconfigSession )
        {
            Config = cfg;
        }

        public ReconfigureSessionMessage( BufRef reader ) : base( ProtocolMessageType.ReconfigSession )
        {
            SessionId = reader.ReadFlip16();
            Config = new I2PSessionConfig( reader );
        }

        public override void Write( BufRefStream dest )
        {
            dest.Write( BufUtils.Flip16B( SessionId ) );
            Config.Write( dest );
        }

        public override string ToString()
        {
            return Config?.ToString();
        }
    }
}
