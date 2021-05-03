using I2PCore.Utils;
using I2PCore.SessionLayer;

namespace I2PCore.TransportLayer.SSU
{
    public class IdleState : SSUState
    {
        protected override BufLen CurrentMACKey => RouterContext.Inst.IntroKey;

        protected override BufLen CurrentPayloadKey => RouterContext.Inst.IntroKey;

        internal IdleState( SSUSession sess ): base( sess )
        {
        }

        public override SSUState Run()
        {
            return this;
        }

        public override SSUState HandleMessage( SSUHeader header, BufRefLen reader )
        {
            return this;
        }
    }
}
