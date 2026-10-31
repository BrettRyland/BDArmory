using System.Runtime.CompilerServices;
using UnityEngine;

namespace BDArmory.Extensions
{
    public static class AnimationStateExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float RemainingTime(this AnimationState animState)
        {
            if (animState == null) return 0f;

            return (animState.length - animState.time) / animState.speed;
        }
    }
}
