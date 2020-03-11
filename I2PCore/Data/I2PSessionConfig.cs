using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using I2PCore.Utils;
using Org.BouncyCastle.Math;

namespace I2PCore.Data
{
    public class I2PSessionConfig: I2PType
    {
        public enum MessageReliabilities { None, BestEffort, Invalid }

        public I2PDestination Destination { get; set; }
        public I2PMapping Options { get; set; }
        public I2PDate Date { get; set; }
        public I2PSignature Signature { get; set; }

        /// Not trasmitted
        public I2PSigningPrivateKey PrivateSigningKey;
        public BufLen SignedBuf;

        public I2PSessionConfig( 
                I2PDestination dest, 
                I2PMapping map, 
                I2PDate date, 
                I2PSignature sign, 
                I2PSigningPrivateKey privsignkey )
        {
            Destination = dest;
            Options = map != null ? map : new I2PMapping();
            Date = date != null ? date : new I2PDate( DateTime.Now );
            Signature = sign;

            PrivateSigningKey = privsignkey;
        }

        public I2PSessionConfig( BufRef reader )
        {
            var start = new BufRefLen( reader );
            Destination = new I2PDestination( reader );
            Options = new I2PMapping( reader );
            Date = new I2PDate( reader );

            SignedBuf = new BufLen( start, 0, reader - start );

            Signature = new I2PSignature( reader, Destination.Certificate );
        }

        public void Write( BufRefStream dest )
        {
            var dest2 = new BufRefStream();
            Destination.Write( dest2 );
            Options.Write( dest2 );
            Date.Write( dest2 );
            var dest2data = dest2.ToArray();

            var sig = new I2PSignature( 
                    new BufRefLen( 
                        I2PSignature.DoSign( PrivateSigningKey, new BufLen( dest2data ) ) ), 
                    Signature.Certificate );

            dest.Write( dest2data );
            sig.Write( dest );
        }

        public bool DontPublishLeaseSet
        {
            get
            {
                return bool.Parse( Options.TryGet( "i2cp.dontPublishLeaseSet", "true" ) );
            }
            set
            {
                Options["i2cp.dontPublishLeaseSet"] = value.ToString();
            }
        }

        public bool FastReceive
        {
            get
            {
                return bool.Parse( Options.TryGet( "i2cp.fastReceive", "true" ) );
            }
            set
            {
                Options["i2cp.fastReceive"] = value.ToString();
            }
        }

        private MessageReliabilities MessageReliabilityCached = MessageReliabilities.Invalid;
        public MessageReliabilities MessageReliability
        {
            get
            {
                if ( MessageReliabilityCached != MessageReliabilities.Invalid )
                {
                    return MessageReliabilityCached;
                }

                MessageReliabilityCached = Options
                    .TryGet( "i2cp.messageReliability", "none" )
                    .ToLower() == "besteffort"
                        ? MessageReliabilities.BestEffort
                        : MessageReliabilities.None;

                return MessageReliabilityCached;
            }
            set
            {
                Options["i2cp.messageReliability"] = 
                    value == MessageReliabilities.BestEffort
                        ? "BestEffort"
                        : "None";

                MessageReliabilityCached = MessageReliabilities.Invalid;
            }
        }

        public int InboundLength
        {
            get
            {
                return int.Parse( Options.TryGet( "inbound.length", "2" ) );
            }
            set
            {
                Options["inbound.length"] = value.ToString();
            }
        }

        public int InboundLengthVariance
        {
            get
            {
                return int.Parse( Options.TryGet( "inbound.lengthVariance", "0" ) );
            }
            set
            {
                Options["inbound.lengthVariance"] = value.ToString();
            }
        }

        public int InboundQuantity
        {
            get
            {
                return int.Parse( Options.TryGet( "inbound.quantity", "2" ) );
            }
            set
            {
                Options["inbound.quantity"] = value.ToString();
            }
        }

        public int OutboundLength
        {
            get
            {
                return int.Parse( Options.TryGet( "outbound.length", "2" ) );
            }
            set
            {
                Options["outbound.length"] = value.ToString();
            }
        }

        public int OutboundLengthVariance
        {
            get
            {
                return int.Parse( Options["outbound.lengthVariance"] );
            }
            set
            {
                Options["outbound.lengthVariance"] = value.ToString();
            }
        }

        public int OutboundQuantity
        {
            get
            {
                return int.Parse( Options.TryGet( "outbound.quantity", "2" ) );
            }
            set
            {
                Options["outbound.quantity"] = value.ToString();
            }
        }

        public override string ToString()
        {
            return $"{Date} {Destination} {Options}";
        }
    }
}
