using BDArmory.Utils;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BDArmory.Extensions
{
    public static class TransformExtensions
    {
        /// <summary>
        /// Simple way of accessing a transform's directions via the index (with the same convetion as Vector3's indexing), 
        /// simplifying code by removing the need to repeateatedly write this translation step.
        /// </summary>
        /// <param name="transform">The transform.</param>
        /// <param name="index">The direction index.</param>
        /// <returns>The unit Vector3 in the requested direction.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 DirectionByIndex(this Transform transform, int index)
        {
            switch (index)
                {
                    case 0:
                    {
                        // x -> right in Unity
                        return transform.right;
                    }
                    case 1:
                    {
                        // y -> up in Unity
                        return transform.up;
                    }
                    case 2:
                    {
                        // z -> back (-forward) in Unity
                        return -transform.forward;
                    }    
                    default:
                        throw new IndexOutOfRangeException("Invalid Transform direction index!");
            }
        }
    }
}