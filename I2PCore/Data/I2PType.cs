using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace I2PCore.Data
{
    /// <summary>
    /// Can be byte serialized according to specification.
    /// </summary>
    public interface I2PType
    {
        void Write( List<byte> dest );
    }
}
