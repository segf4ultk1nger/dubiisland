// RandomExtensions.cs

using System;

namespace ClassIsland.Core.Extensions
{
    /// <summary>
    /// Fisher-Yates shuffle for arrays. net472 has no <c>Random.Shuffle</c> and no <c>MemoryMarshal.CreateSpan</c>.
    /// </summary>
    public static class RandomExtensions
    {
        /// <summary>
        /// Performs an in-place shuffle of an array.
        /// </summary>
        public static void Shuffle<T>(this Random random, T[] values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var n = values.Length;
            for (var i = 0; i < n - 1; i++)
            {
                var j = random.Next(i, n);
                if (j != i)
                {
                    (values[i], values[j]) = (values[j], values[i]);
                }
            }
        }
    }
}