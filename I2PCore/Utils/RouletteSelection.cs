using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("I2PCore.NTests")]
namespace I2PCore.Utils
{
    public class RouletteSelection<T,K>
    {
        public class RouletteSpace<K2>
        {
            public K2 Id;
            public double Space;
            public float Fit;

            public override string ToString()
            {
                return $"RouletteSpace: Fit {Fit:0.00}, Space {Space:0.00}: {Id}";
            }
        }

        private const double DefaultElitism = 1.0 / 500;
        public double Elitism { get; protected set; }
        public int IncludeTop { get; protected set; }

        public readonly IEnumerable<RouletteSpace<K>> Wheel;
        readonly double TotalSpaceSum;

        public readonly float AverageFit;
        public readonly float AbsDevFit;
        public readonly float StdDevFit;

        public readonly float MinFit;
        public readonly float MaxFit;

        public RouletteSelection( 
            IEnumerable<T> infos, 
            Func<T,K> selkey, 
            Func<K,float> selfit,
            int maxinfos,
            double elitism = DefaultElitism )
        {
            IncludeTop = maxinfos;
            Elitism = elitism;

            TotalSpaceSum = 0;

            Wheel = infos.Select( inf => 
                        {
                            var space = new RouletteSpace<K>()
                            {
                                Id = selkey( inf ),
                            };
                            space.Fit = selfit( space.Id );
                            return space;
                        } );

            if ( !Wheel.Any() )
            {
                MinFit = 0f;
                MaxFit = 0f;
                AverageFit = 0f;
                return;
            }

            // Makes OrderByDescending makes GetWeightedRandom faster
            Wheel = Wheel
                .OrderByDescending( rs => rs.Fit )
                .ToArray();
            var wheelcount = Wheel.Count();

            if ( wheelcount > IncludeTop )
            {
                Wheel = Wheel
                    .Take( IncludeTop );
            }

            var fits = Wheel.Select( sp => sp.Fit );
            MinFit = fits.Min();
            MaxFit = fits.Max();
            AverageFit = fits.Average();
            AbsDevFit = fits.AbsDev();
            StdDevFit = fits.StdDev();

            var i = 1.0 + Wheel.Count() * Elitism;

            foreach ( var one in Wheel )
            {
                one.Space = i;
                i -= Elitism;
                TotalSpaceSum += one.Space;
            }
        }

        public K GetWeightedRandom( IEnumerable<K> exclude )
        {
            lock ( Wheel )
            {
                var pos = 0.0;

                var subset = Wheel;
                var subsetsum = TotalSpaceSum;

                if ( exclude?.Any() ?? false )
                {
                    subset = Wheel.Where( one => !exclude.Contains( one.Id ) );
                    subsetsum = subset.Sum( one => one.Space );
                }

                var target = BufUtils.RandomDouble( subsetsum );

                foreach ( var one in subset )
                {
                    pos += one.Space;
                    if ( pos >= target )
                    {
                        Logging.LogDebugData( $"Roulette: {one.Id}" );
                        return one.Id;
                    }
                }

                if ( subset.Any() )
                {
                    return subset.Random().Id;
                }

                return Wheel.Random().Id;
            }
        }
    }
}
