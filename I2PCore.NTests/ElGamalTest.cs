using NUnit.Framework;
using I2PCore.Data;
using I2PCore.Utils;
using Org.BouncyCastle.Math;
using I2PCore.TunnelLayer.I2NP.Data;
using System.Text;

namespace I2PTests
{
    [TestFixture]
    public class ElGamalTest
    {
        I2PPrivateKey Private;
        I2PPublicKey Public;
        I2PRouterIdentity Me;

        public ElGamalTest()
        {
            Private = new I2PPrivateKey( I2PKeyType.DefaultAsymetricKeyCert );
            Public = new I2PPublicKey( Private );

            Me = new I2PRouterIdentity( Public, new I2PSigningPublicKey( new BigInteger( "12" ), I2PKeyType.DefaultSigningKeyCert ) );
        }

        [Test]
        public void TestElGamal()
        {
            for ( int i = 0; i < 20; ++i )
            {
                var egdata = new BufLen( new byte[514] );
                var writer = new BufRefLen( egdata );
                var data = new BufLen( egdata, 0, 222 );

                data.Randomize();
                var origdata = data.Clone();

                var eg = new ElGamalCrypto( Public );
                eg.Encrypt( writer, data, true );

                var decryptdata = ElGamalCrypto.Decrypt( egdata, Private, true );

                Assert.IsTrue( decryptdata == origdata );
            }
        }

        [Test]
        public void TestElGamalNoPad()
        {
            for ( int i = 0; i < 20; ++i )
            {
                var egdata = new BufLen( new byte[512] );
                var writer = new BufRefLen( egdata );
                var data = new BufLen( egdata, 0, 222 );

                data.Randomize();
                var origdata = data.Clone();

                var eg = new ElGamalCrypto( Public );
                eg.Encrypt( writer, data, false );

                var decryptdata = ElGamalCrypto.Decrypt( new BufLen( egdata, 0, 512 ), Private, false );

                Assert.IsTrue( decryptdata == origdata );
            }
        }

        [Test]
        public void TestEGDecrypt()
        {
            var mi =
                "niPls-Doz1DJ3ua4pQDwstMeqnmznBCU~qaxIidv" +
                "jTW~ohDDpq3BatYwTSM-fIr-eEy4Amdl-tWJTXRO" +
                "2UzftkuD1-t-Ix9EfED26KrUxnwx2fC74Haykj9L" +
                "oytqFTFbYj34kMCUDDbhAqv6Ep3EDY8Am7WYqSJL" +
                "stXXV4tUFZ~mteCm1qfNUbbl6itCUId7zcgkW6SX" +
                "rwdkno3Ul4H~WVDtmewlxdDbT-rYyOJTnp4Y39Mo" +
                "8B1ojtMuGCgtbk-LS4gJmlXeCJh~1kHiM-nLIB9W" +
                "rtq8O4weJ~-R36obERZofGOV3D3kSKSoawXVcq4V" +
                "BlQOHTn24LkTHYLh3O~0SZlT0KfU5WrqTZtu8jZ~" +
                "B0q-cvrfqqWcmCFCoKW3WSJ5D0qZi1e9sLMebM96" +
                "asSRFZvlOUp8xZm84Z-au~m3EmkAaF4QlAzufA~L" +
                "~1RFaepKpfng0eWfWG73H1daoL3c2e3Pt~5m07FR" +
                "ZMr6TZoj1IsuI7J-QXRGfvbuMjJqB-tMBQAEAAcA" +
                "ADJtDgiK2OMz6qSmopA~cLUMW~zQesBqsoYx9AEc" +
                "sOIkp4g8YgRK2jJhfY46h7aj76ftkV-5uF6xoDUG" +
                "kbIQGH3rjneTQ2wspOz2WRpQk0MW6Ugd0H~5vYK2" +
                "ekploxq2gOil0Ad1rz~uobpE2bBKYK9B-EEkRKEP" +
                "-GTX6DSyosFXxMC1I5cwLDWmXktdNVuzt56AxfcL" +
                "Nzh9gd1R4l903-aFRiY~VXFHg1NjWBPcjCCz6fpL" +
                "U0XIdg6hf~DNMnd5D4C27HYujxUi6tj42sikDCPu" +
                "P6CBGAH~j7vzqkDxW2wker91lwwp~6N8jN4xcvme" +
                "-EI27LPGnOICvpzxdVrKtpc0Ib4dOe8SFkvxSR4x" +
                "PDSGWFF-~vAELzyHTYiE07ahNw==";

            var myinfo = FreenetBase64.Decode( mi );

            var ed =
                "AAAOggAE3uf0930YRPZqauUGNYgj5Ix7aoMz0ZO8" +
                "OIYpRS9zFi6CHe-SXOCeZMgOSxdkp320s70lnAJ9" +
                "2Z0fjP9WbBeKuCPjZSyMa-MQ4Bq5Sv~iXK-5T~ui" +
                "m9WTMnwzWIltbt2Toncx-9aAGsIgoABUpzQ1yjsG" +
                "-DwSEazn1ZsGAxGXI74SqokXGBggerXCtAhpTOTJ" +
                "MsFteib29WhURmPxLXPxtoCqav8hEUjEyBy2pnPE" +
                "h-3fcZPhxfK5FI0h0vZO0ZATZx~0RDV9NyxwaAMy" +
                "llav8iG2nFmVd7hkvWSVdcaRp2gpZuDcM9aXb2yo" +
                "xIagerhVF8Dyfbib3YX8GP9tvOhBAGWSRlvNIW6i" +
                "ImwOHJPuCbD4f2Ombp2VENYfKH~Tc4JP53DOnLs0" +
                "1pMze3xLaK0LsoPwR2LgYfamxbGDINa8KNB0PXC7" +
                "l7zcO6vDuC~mlVgs6VkkC-kirW8QMpO9t7v49l3y" +
                "tqlHBLU~khJwBFYuQEoB78bt~RQANw6R6V12yRIE" +
                "oW8dGJh5P1eGAFgO24oHyfLdKB-vfm1SZQB5MVyU" +
                "Ec6Hv4AKDBsxFqzFbThSgURqWk~AL9v-LuMhz034" +
                "jq-fGZtUY3l~ogz12IxdJIJVsjvJ2jWE-8NyFzC5" +
                "NhCSYV8jgdbL56ISMNPT9PRuiIyMHJ1w1NfypZ3c" +
                "aHKEnIjBzpj6R-9~~8PjLvdNO9cVaur5OLOGf4Q3" +
                "ABbqj9rg5uMJYIrFgddB4uETzb97Iue8BJr8XTRh" +
                "mwihpOLabG9r1b4-X1Z1HpBZxp8ml3xlZ-Lw6zp3" +
                "rQj2O84fSr0pxQh04Q-gIPBq7d4fS74uHMhN6rMx" +
                "bydEq0QQe4cqHDlGO-WZuTmK0tN-vMkwgHRXlz18" +
                "AdFFTf4caSoYerKPCk07neOWn0Vop1ni3DTRL~Tv" +
                "k04ZxNLnuuuor8gBh~jVFS-S9EnKkLcWJtJFcVbY" +
                "y9ZK-4caSvFGz1-Qf0elJKA-cpx7-ivKIQLQEuhx" +
                "5U6FrnJvchbY4JaZq2QzXVRF8s7X04kYOWWmAPXt" +
                "C3xE58e9s4-5MJhLNjdsjXoJ0R9jBleMWF-RsVwy" +
                "QNlTtabQDtDUzVeEQNRAHu9nVc0mJ18NH04KFMbH" +
                "jTWpgY1wDFB9566C9OOKzK-gkTjzO5RlSwDn5sgN" +
                "iejCOqM0-4JzMlFQNCuhMvmYAglRG8dLDZ3WjYck" +
                "NiRqHP2iKMyGQThwIS~TCzc-i5WgNzjLKPnWhhKm" +
                "1uvsZiWdnVAn9IOsFuOYvuR5NBnRJb~lYWI~zlvT" +
                "8riNkMIrnCE4CF380NEPfulMM6~98XVtmuMTY-Im" +
                "Zut2O1kz0OCBvCiYClrFlSlVDVoYEbVmkyoBKjiq" +
                "dGdPtLC5FnX6eC2bcUNGFOOY32ozF3UUsaRWVMrC" +
                "Y824xhPxcCt2yiJ~rSXAEBN2qtVW90wYzamz9pw6" +
                "XiNUdhshjLrOb-qVAnC33Kak89wbPtMGh6uXljR4" +
                "YxZ3gW2Tl9LIu4~5QP~sS7plo19-bdRfL8uAZ63j" +
                "gy-fY71std81WBfthqlOTFJMf~c-EU8xj90O408z" +
                "qYfFty-wgBBfYeeHdyWAWrqbfwLCMlHGGN0M-dbV" +
                "SjzwgSeLTYbdPrX1Dr6NHxSnJ9br0sXfoFUao1xa" +
                "4FN556lbolHjV0ns~6IbOVWElDNNecsLxjTWKgdA" +
                "CrLdh2ZMFL9M56H2Xdz4Adsm5vY1K6kCcLPgbpgb" +
                "~~gl-Qq4AEc~-nV6fxpu7pmH3HfTXIm0aDtR1KG7" +
                "kEHv-MreoFvmWvnVCj6tqJr5LAOKCDTvm7cw5fYc" +
                "27gsskqG~SM~1TyPkF5sb5~xX1EkAYm9PC0pA9WG" +
                "1~M4640pRCzbVl7jWqf1B~RPc7FRs3wMZqHu6SJW" +
                "kDRfvupQeNU6TXTkVPwJZcfTElH9ajcu23ucNXP-" +
                "Glp-00AZZA2fwOzdJe8AlzBIReLRyWKza5hLqmAy" +
                "paI1e5wSFDgXH027-tXLCYw-l2rsHl2aDO-M0Psj" +
                "OOA4GhdBUlLZWdy6OAI1~vpssgFY9CUErqR6m7LA" +
                "mLa9NaBFtTt~ZUYgc0TyrYfXjecl-jxUcPnYEo3u" +
                "YiBx3kk73LQWI-jj4IqKxUf1Rjp6b~Vhw77NFQPr" +
                "vqe91ICRuo9XTMavCQP8R1Lg2R7CwljE2qLJ0qxM" +
                "UN34gUfOIdQzBi~ApnG~aLoRwwro2U2MA63RMKkZ" +
                "OoXpjAmPj1xGqsQ73bI3lpfpHFLbWm3Ax37t2aud" +
                "c7-nPoaKoNvNIC0~28jROr35UGCKNMSNmqrifhHB" +
                "wsIqqMrubLjjmts-ELHWZF~1AZEDKMGrF3j-zGVg" +
                "LAhaSCvG3CNjaHTiAkPXc5zRwFLi5XTof1QipptG" +
                "u~j6-0Kpcwb5aWUD4q0JFxJPd0pt5p8N8r8Y04RR" +
                "TSzzyQsC8iGUDsmQyAnsquvMBpMBoCd3pOsSwYu3" +
                "d0A7UAoEI0S3Cp4JPndz2waXB4RhJzmHQu3JYv3A" +
                "IzYNfKvLwkZVdky-e7MqHtrSJU6HErb7JE7CgtW1" +
                "Gv-WYaCGZZR5qt3x-43CCYN9qnLpSIOXL17yK6P1" +
                "wsc2lXnAj6IYzppaH6~CtP3WnrsiEzh6lznkQbDx" +
                "pFygHfhvQQBJbhfjLL6-0RWG52oVsurXnT4og0oW" +
                "0itSdrP2tFjKc3aFkG4toWyDpfEOS427cPxRRNv6" +
                "6uEq-giDYHxVuk5k4TQ9bBp4HaWSKS55vr-oz5FF" +
                "SjT~p3-7WViVeoSO4wbcO7x2djkaQ7rWm6eiggut" +
                "NKw3WVNTG-oEQWuauwWp7mHwbDmTC7i-j4dvxMfz" +
                "k4gRWFOZpOEXIuiVBp20Ljy53fqZaSmijYqJwpRi" +
                "l~g6h-gvc43GMdoC48ryj0ClTN209Ua20YN54lg~" +
                "izHnl6X-VnmgPr298LKe0COK0xlDLKj~tu2CIzi3" +
                "mNwe4eLWfsmKRGrcXMwfz7F2SfOfNpqXbpgAV9AZ" +
                "Qy-SPuqFxNTcmclIzWKudDECoUqADHu3-tLijV0m" +
                "uer990OtbKeSrW9ICCVpjrbh5aPezOLNXcDkfxGB" +
                "S~yLpUgLn1aCFQv6Bz1htwfEpeKfqQvwVfD17SoW" +
                "-8XBA2ORSqXGOL9x93tzmt1pspsfgehXTqEVerH9" +
                "H9qCAECvRtyHu7jh-DNxFtnA-w5PTg7sf0uKKMOf" +
                "qtvIrpJulVuPl~JX89aigeihJXAQoc-QD5aMBVYC" +
                "n55mgrYUEKfe4~yYYYdkJCG0zwD5mADMA-elcNUI" +
                "ojXYff~X4eJwm1upbS5LpVyH4D-F57JMJKBFOUA4" +
                "Z6i1qTjQInIDYeinHKRThB9hz3EIB18TvroAz5Yl" +
                "UdE-BmdrXsXHztucbfw4GQ5LamIGHeYrapJ4QkRm" +
                "s~a5kdy78FrTV4drRuqoGWlpB9MORKyUVKZbQJND" +
                "6ncPyrRLaVTXy4m~L2MZoi4mPifkH6JdCUtyHK4f" +
                "Z0Ch1CSjHBr~kN3FoQRW4fJ9SazZ7sreYJLHOBhf" +
                "IdJTlu6ZX~nVwDzBZSg-kDj0phD7iWSAy9tI-qjq" +
                "nLkoZZj7oDt~L1Z-M~pXgsZkfIYN2nfoC3JV~HFl" +
                "W7DB3HEphkpU2cCzr2-BBFGGftNup6O1rohh1OSD" +
                "1T9OOACf-VvRk~z1motf5Y9oboEljWgrsqXSgDWM" +
                "Nsmk7WBv0e8H8SaNiKwoRiT4Y6EVjIe5Ft5pw7r7" +
                "7s3RhsGA6EcmRT8POELuJz5Q7qK7rYbDw1Ooil9v" +
                "ebQ7Zo9m~FpTAGAnuIgcPvmFfcgdI9Y46SV1JK4f" +
                "N91F5dm~7BXZcstbE3cinOV7VEiUEUUPiZaW6dXv" +
                "7dEwrELbD0gFStVy5IGuy1gqsygVxCvQwXFZ2j0N" +
                "EpYr0NTIwEOWKol17u3UTBSY6zhhajSPxl1f0QeF" +
                "yybEcw41IHy1fDZ3RfihzHIislrcDYHQ3mr~kdYC" +
                "oUlK83CKpQL2NJPKtLXsx0ATPjSYOg9Q25rf9v8c" +
                "joNrHERzhbDgqeCIXmSF5nLEpys~yRKLYpl7RLPS" +
                "fptt5gLOVNLmp~Z4CAKjLN-aqMYNopmy92-u74fb" +
                "u-d7UftyaZHNgT2QNP7TDrwYfivNKRS6detKwTYH" +
                "fxVpGRDT2HIHXZE3xCfZa6YmfPCrl0kAKAt1NB4i" +
                "VBb~TJ0p43CuaMnT~Mu7vXkwGVKJtZ3bGQfUtEhJ" +
                "xh61nVfh3k1F3EuczB~f1ekAZDMa9qhkp-Rv67~S" +
                "97ASXgyz673QLIoksK2udHB0iHPL6H2pJlBpv4oH" +
                "4YnA0o4ejTUy5vLgret0ItpMao4waezWK8TMHAzD" +
                "STd76eOIacKbIYkuorpx2atQrHizDQ9vPSIvurH7" +
                "8gMHJ03JnYoh60zIvZIqDajTS9vrqIvvu5HaB2re" +
                "bqmfMx2XKEvjqEBA7hPdqXR6n9typIcj1tqdHK-h" +
                "9iOYkQ81u64y6SgYMxuXNd38AFTUUouh1OQp2jVv" +
                "pnLLR-vUT1-G-lO8Xw5wrlwx6G4BA3peKmaMIxBu" +
                "iJA0qLUc336-oFXvThUh2JwT9RshrKclS7vp-zc9" +
                "8dzAXxATuxKakXZaJGQ91xWyTkz8lFzuGnfWK5HF" +
                "CgNiOa2F8nbR5kzofCXyYHyRbp9pT8Ek1xAK0Ayp" +
                "FYwqfcVFZnhe0TXcup5UDgdFlroirzfJaDIbQKBz" +
                "O4cS68Kd4YV9HPgu2de84dz~PfIgredg-A-rKon8" +
                "1ZhGylOgaXCDNNag9mjdF7B5nFjddYytaOgFCTDR" +
                "G0ntFV8mMDIwxW9GZtt6MSJQSJqjdOM1ywFdPrdX" +
                "GDKnib~NO247LxGLa05wUZPMvg92~mPgg9SkD9v4" +
                "-g008LITmMajbpE8~0t-jijMKJkPCQxOaBsH5-fn" +
                "uToqxg-xm0d9wg48Xr5BmirS5UbC7pS-U9pKI1E8" +
                "vp~DWbxxRpulZYhgNq2-cR2w7ie01mDleoUv-tdk" +
                "T9L1erdyTAhHrw2YNe0SD8OmyvW4aPPsjAGMeg==";

            var egdata = FreenetBase64.Decode( ed );

            var di = new I2PDestinationInfo( new BufRefLen( myinfo ) );

            var decr = ElGamalCrypto.Decrypt( new BufLen( egdata, 4, 514 ), di.PrivateKey, true );
        }

        [Test]
        public void TestEGCompatibilityDecode()
        {
            var priv = new I2PPrivateKey( 
                    new BufRefLen( FreenetBase64.Decode( PRIVATE_KEY ) ), 
                    new I2PCertificate() );

            var pub = new I2PPublicKey(
                    new BufRefLen( FreenetBase64.Decode( PUBLIC_KEY ) ),
                    new I2PCertificate() );

            for ( int i = 0; i < ENCRYPTED.Length; ++i )
            {
                var decr = ElGamalCrypto.Decrypt(
                        new BufLen( FreenetBase64.Decode( ENCRYPTED[i] ) ), 
                        priv, 
                        true );

                var clear = new BufLen( Encoding.UTF8.GetBytes( UNENCRYPTED[i] ) );
                Assert.IsTrue( decr == clear );
            }
        }

        [Test]
        public void TestEGCompatibilityEncode()
        {
            var priv = new I2PPrivateKey(
                    new BufRefLen( FreenetBase64.Decode( PRIVATE_KEY ) ),
                    new I2PCertificate() );

            var pub = new I2PPublicKey(
                    new BufRefLen( FreenetBase64.Decode( PUBLIC_KEY ) ),
                    new I2PCertificate() );

            var eg = new ElGamalCrypto( pub );

            for ( int i = 0; i < ENCRYPTED.Length; ++i )
            {
                var clear = new BufLen( Encoding.UTF8.GetBytes( UNENCRYPTED[i] ) );
                var encr = new BufLen( eg.Encrypt( clear, true ) );

                Assert.IsTrue( encr.Length == 514 );

                var decr = ElGamalCrypto.Decrypt( encr, priv, true );

                Assert.IsTrue( decr == clear );
            }
        }

        static readonly string PUBLIC_KEY =
            "pOvBUMrSUUeN5awynzbPbCAwe3MqWprhSpp3OR7pvdfm9PhWaNbPoKRLeEmDoUwyNDoHE0" +
            "E6mcZSG8qPQ8XUZFlczpilOl0MJBvsI9u9SMyi~bEqzSgzh9FNfS-NcGji3q2wI~Ux~q5B" +
            "KOjGlyMLgd1nxl5R5wIYL4uHKZNaYuArsRYmtV~MgMQPGvDtIbdGTV6aL6UbOYryzQSUMY" +
            "OuO3S~YoBjA6Nmi0SeJM3tyTxlI6U1EYjR6oQcI4SOFUW4L~8pfYWijcncCODAqpXVN6ZI" +
            "AJ3a6vjxGu56IDp4xCcKlOEHgdXvqmEC67dR5qf2btH6dtWoB3-Z6QPsS6tPTQ==";

        static readonly string PRIVATE_KEY =
            "gMlIhURVXU8uPube20Xr8E1K11g-3qZxOj1riThHqt-rBx72MPq5ivT1rr28cE9mzOmsXi" +
            "bbsuBuQKYDvF7hGICRB3ROSPePYhcupV3j7XiXUIYjWNw9hvylHXK~nTT7jkpIBazBJZfr" +
            "LJPcDZTDB0YnCOHOL-KFn4N1R5B22g0iYRABN~O10AUjQmf1epklAXPqYlzmOYeJSfTPBI" +
            "E44nEccWJp0M0KynhKVbDI0v9VYm6sPFK7WrzRyWwHL~r735wiRkwywuMmKJtA7-PuJjcW" +
            "NLkJwx6WScH2msMzhzYPi8JSZJBl~PosX934l-L0T-KNV4jg1Ih6yoCnm1748A==";

        static readonly string[] ENCRYPTED = new string[] {
                "AMfISa8KvTpaC7KXZzSvC2axyiSk0xPexBAf29yU~IKq21DzaU19wQcGJg-ktpG4hjGSg7" +
                "u-mJ07b61yo-EGmVGZsv3nYuQYW-GjvsZQa9nm98VljlMtWrxu7TsRXw~SQlWQxMvthqJB" +
                "1A7Y7Qa~C7-UlRytkD-cpVdgUfM-esuMWmjGs6Vc33N5U-tce5Fywa-9y7PSn3ukBO8KGR" +
                "wm7T12~H2gvhgxrVeK2roOzsV7f5dGkvBQRZJ309Vg3j0kjaxWutgI3vli0pzDbSK9d5NR" +
                "-GUDtdOb6IIfLiOckBegcv6I-wlSXjYJe8mIoaK45Ok3rEpHwWKVKS2MeuI7AmsAWgkQmW" +
                "f8irmZaKc9X910VWSO5GYu6006hSc~r2TL3O7vwtW-Z9Oq~sAam9av1PPVJzAx8A4g~m~1" +
                "avtNnncwlChsGo6mZHXqz-QMdMJXXP57f4bx36ZomkvpM-ZLlFAn-a~42KQJAApo4LfEyk" +
                "7DPY2aTXL9ArOCNQIQB4f8QLyjvAvu6M3jzCoGo0wVX6oePfdiokGflriYOcD8rL4NbnCP" +
                "~MSnVzC8LKyRzQVN1tDYj8~njuFqekls6En8KFJ-qgtL4PiYxbnBQDUPoW6y61m-S9r9e9" +
                "y8qWd6~YtdAHAxVlw287~HEp9r7kqI-cjdo1337b7~5dm83KK45g5Nfw==",

                "AIrd65mG1FJ~9J-DDSyhryVejJBSIjYOqV3GYmHDWgwLchTwq-bJS7dub3ENk9MZ-C6FIN" +
                "gjUFRaLBtfwJnySmNf8pIf1srmgdfqGV2h77ufG5Gs0jggKPmPV~7Z1kTcgsqpL8MyrfXr" +
                "Gi86X5ey-T0SZSFc0X1EhaE-47WlyWaGf-~xth6VOR~KG7clOxaOBpks-7WKZNQf7mpQRE" +
                "4IsPJyj5p1Rf-MeDbVKbK~52IfXSuUZQ8uZr34KMoy4chjn6e-jBhM4XuaQWhsM~a3Q-zE" +
                "pV-ea6t0bQTYfsbG9ch7pJuDPHM64o5mF9FS5-JGr7MOtfP7KDNHiYM2~-uC6BIAbiqBN8" +
                "WSLX1mrHVuhiM-hiJ7U4oq~HYB6N~U980sCIW0dgFBbhalzzQhJQSrC1DFDqGfL5-L25mj" +
                "ArP8dtvN0JY3LSnbcsm-pT9ttFHCPGomLfaAuP7ohknBoXK0j9e6~splg5sUA9TfLeBfqc" +
                "Lr0Sf8b3l~PvmrVkbVcaE8yUqSS6JFdt3pavjyyAQSmSlb2jVNKGPlrov5QLzlbH7G~AUv" +
                "IehsbGQX5ptRROtSojN~iYx3WQTOa-JLEC-AL7RbRu6B62p9I0pD0JgbUfCc4C4l9E9W~s" +
                "MuaJLAXxh0b2miF7C5bzZHxbt~MtZ7Ho5qpZMitXyoE3icb43B6Y1sbA==",

                "ACjb0FkTIQbnEzCZlYXGxekznfJad5uW~F5Mbu~0wtsI1O2veqdr7Mb0N754xdIz7929Ti" +
                "1Kz-CxVEAkb3RBbVNcYHLfjy23oQ4BCioDKQaJcdkJqXa~Orm7Ta2tbkhM1Mx05MDrQaVF" +
                "gCVXtwTsPSLVK8VwScjPIFLXgQqqZ5osq~WhaMcYe2I2RCQLOx2VzaKbT21MMbtF70a-nK" +
                "WovkRUNfJEPeJosFwF2duAD0BHHrPiryK9BPDhyOiyN82ahOi2uim1Nt5yhlP3xo7cLV2p" +
                "6kTlR1BNC5pYjtsvetZf6wk-solNUrJWIzcuc18uRDNH5K90GTL6FXPMSulM~E4ATRQfhZ" +
                "fkW9xCrBIaIQM49ms2wONsp7fvI07b1r0rt7ZwCFOFit1HSAKl8UpsAYu-EsIO1qAK7vvO" +
                "UV~0OuBXkMZEyJT-uIVfbE~xrwPE0zPYE~parSVQgi~yNQBxukUM1smAM5xXVvJu8GjmE-" +
                "kJZw1cxaYLGsJjDHDk4HfEsyQVVPZ0V3bQvhB1tg5cCsTH~VNjts4taDTPWfDZmjtVaxxr" +
                "PRII4NEDKqEzg3JBevM~yft-RDfMc8RVlm-gCGANrRQORFii7uD3o9~y~4P2tLnO7Fy3m5" +
                "rdjRsOsWnCQZzw37mcBoT9rEZPrVpD8pjebJ1~HNc764xIpXDWVt8CbA==",

                "AHDZBKiWeaIYQS9R1l70IlRnoplwKTkLP2dLlXmVh1gB33kx65uX8OMb3hdZEO0Bbzxkkx" +
                "quqlNn5w166nJO4nPbpEzVfgtY4ClUuv~W4H4CXBr0FcZM1COAkd6rtp6~lUp7cZ8FAkpH" +
                "spl95IxlFM-F1HwiPcbmTjRO1AwCal4sH8S5WmJCvBU6jH6pBPo~9B9vAtP7vX1EwsG2Jf" +
                "CQXkVkfvbWpSicbsWn77aECedS3HkIMrXrxojp7gAiPgQhX4NR387rcUPFsMHGeUraTUPZ" +
                "D7ctk5tpUuYYwRQc5cRKHa4zOq~AQyljx5w5~FByLda--6yCe7qDcILyTygudJ4AHRs1pJ" +
                "RU3uuRTHZx0XJQo~cPsoQ2piAOohITX9~yMCimCgv2EIhY3Z-mAgo8qQ4iMbItoE1cl93I" +
                "u2YV2n4wMq9laBx0shuKOJqO3rjRnszzCbqMuFAXfc3KgGDEaCpI7049s3i2yIcv4vT9uU" +
                "AlrM-dsrdw0JgJiFYl0JXh~TO0IyrcVcLpgZYgRhEvTAdkDNwTs-2GK4tzdPEd34os4a2c" +
                "DPL8joh3jhp~eGoRzrpcdRekxENdzheL4w3wD1fJ9W2-leil1FH6EPc3FSL6e~nqbw69gN" +
                "bsuXAMQ6CobukJdJEy37uKmEw4v6WPyfYMUUacchv1JoNfkHLpnAWifQ==",

                "AGwvKAMJcPAliP-n7F0Rrj0JMRaFGjww~zvBjyzc~SPJrBF831cMqZFRmMHotgA7S5BrH2" +
                "6CL8okI2N-7as0F2l7OPx50dFEwSVSjqBjVV6SGRFC8oS-ii1FURMz2SCHSaj6kazAYq4s" +
                "DwyqR7vnUrOtPnZujHSU~a02jinyn-QOaHkxRiUp-Oo0jlZiU5xomXgLdkhtuz6725WUDj" +
                "3uVlMtIYfeKQsTdasujHe1oQhUmp58jfg5vgZ8g87cY8rn4p9DRwDBBuo6vi5on7T13sGx" +
                "tY9wz6HTpwzDhEqpNrj~h4JibElfi0Jo8ZllmNTO1ZCNpUQgASoTtyFLD5rk6cIAMK0R7A" +
                "7hjB0aelKM-V7AHkj-Fhrcm8xIgWhKaLn2wKbVNpAkllkiLALyfWJ9dhJ804RWQTMPE-GD" +
                "kBMIFOOJ9MhpEN533OBQDwUKcoxMjl0zOMNCLx8IdCE6cLtUDKJXLB0atnDpLkBer6FwXP" +
                "81EvKDYhtp1GsbiKvZDt8LSPJQnm2EdA3Pr9fpAisJ5Ocaxlfa6~uQCuqGA9nJ9n6w03u-" +
                "ZpSMhSh4zm2s1MqijmaJRc-QNKmN~u1hh3R2hwWNi7FoStMA87sutEBXMdFI8un7StHNSE" +
                "iCYwmmW2Nu3djkM-X8gGjSsdrphTU7uOXbwazmguobFGxI0JujYruM5Q==",

                "ALFYtPSwEEW3eTO4hLw6PZNlBKoSIseQNBi034gq6FwYEZsJOAo-1VXcvMviKw2MCP9ZkH" +
                "lTNBfzc79ms2TU8kXxc7zwUc-l2HJLWh6dj2tIQLR8bbWM7U0iUx4XB1B-FEvdhbjz7dsu" +
                "6SBXVhxo2ulrk7Q7vX3kPrePhZZldcNZcS0t65DHYYwL~E~ROjQwOO4Cb~8FgiIUjb8CCN" +
                "w5zxJpBaEt7UvZffkVwj-EWTzFy3DIjWIRizxnsI~mUI-VspPE~xlmFX~TwPS9UbwJDpm8" +
                "-WzINFcehSzF3y9rzSMX-KbU8m4YZj07itZOiIbWgLeulTUB-UgwEkfJBG0xiSUAspZf2~" +
                "t~NthBlpcdrBLADXTJ7Jmkk4MIfysV~JpDB7IVg0v4WcUUwF3sYMmBCdPCwyYf0hTrl2Yb" +
                "L6kmm4u97WgQqf0TyzXtVZYwjct4LzZlyH591y6O6AQ4Fydqos9ABInzu-SbXq6S1Hi6vr" +
                "aNWU3mcy2myie32EEXtkX7P8eXWY35GCv9ThPEYHG5g1qKOk95ZCTYYwlpgeyaMKsnN3C~" +
                "x9TJA8K8T44v7vE6--Nw4Z4zjepwkIOht9iQsA6D6wRUQpeYX8bjIyYDPC7GUHq0WhXR6E" +
                "6Ojc9k8V5uh0SZ-rCQX6sccdk3JbyRhjGP4rSKr6MmvxVVsqBjcbpxsg=="
            };

        static readonly string[] UNENCRYPTED = new string[] {
                "",
                "hello world",
                "1234567890123456789012345678901234567890123456789012345678901234567890" +
                "1234567890123456789012345678901234567890123456789012345678901234567890" +
                "1234567890123456789012345678901234567890123456789012345678901234567890" +
                "123456789012",
                "\x0000x00",
                "\x0000x00\x0000x00\x0000x00",
                "\x0000x00\x0000x01\x0000x02\x0000x00",
            };
    }
}
