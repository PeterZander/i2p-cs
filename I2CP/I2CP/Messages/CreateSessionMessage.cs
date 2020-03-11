using I2PCore.Data;
using I2PCore.Utils;

namespace I2P.I2CP.Messages
{
    public class CreateSessionMessage: I2CPMessage
    {
        public I2PSessionConfig Config;

        public CreateSessionMessage( I2PSessionConfig cfg ): base( ProtocolMessageType.CreateSession )
        {
            Config = cfg;
        }

        public CreateSessionMessage( BufRef reader ) : base( ProtocolMessageType.CreateSession )
        {
            Config = new I2PSessionConfig( reader );
        }

        public override void Write( BufRefStream dest )
        {
            Config.Write( dest );
        }

        public override string ToString()
        {
            return Config?.ToString();
        }
    }
}
