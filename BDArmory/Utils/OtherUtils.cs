using BDArmory.Settings;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace BDArmory.Utils
{
    public static class OtherUtils // FIXME Suggestions for a better name?
    {
        /// <summary>
        /// Parses the string to a curve.
        /// Format: "key:pair,key:pair"
        /// </summary>
        /// <returns>The curve.</returns>
        /// <param name="curveString">Curve string.</param>
        public static FloatCurve ParseCurve(string curveString)
        {
            string[] pairs = curveString.Split(new char[] { ',' });
            Keyframe[] keys = new Keyframe[pairs.Length];
            for (int p = 0; p < pairs.Length; p++)
            {
                string[] pair = pairs[p].Split(new char[] { ':' });
                keys[p] = new Keyframe(float.Parse(pair[0]), float.Parse(pair[1]));
            }

            FloatCurve curve = new FloatCurve(keys);

            return curve;
        }

        public static IEnumerable<Type> GetLoadableTypes(this Assembly assembly)
        {
            // TODO: Argument validation
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null);
            }
        }

        private const int lineOfSightLayerMask = (int)(LayerMasks.Parts | LayerMasks.Scenery | LayerMasks.EVA | LayerMasks.Unknown19 | LayerMasks.Unknown23 | LayerMasks.Wheels);
        public static bool CheckSightLine(in Vector3 origin, in Vector3 target, float maxDistance, float threshold,
            float startDistance)
        {
            float dist = maxDistance;
            Ray ray = new Ray(origin, target - origin);
            ray.origin += ray.direction * startDistance;
            RaycastHit rayHit;
            if (Physics.Raycast(ray, out rayHit, dist, lineOfSightLayerMask))
            {
                if ((target - rayHit.point).sqrMagnitude < threshold * threshold)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            return false;
        }

        public static bool CheckSightLineExactDistance(in Vector3 origin, in Vector3 target, float maxDistance,
            float threshold, float startDistance)
        {
            float dist = maxDistance;
            Ray ray = new Ray(origin, target - origin);
            ray.origin += ray.direction * startDistance;
            RaycastHit rayHit;

            if (Physics.Raycast(ray, out rayHit, dist, lineOfSightLayerMask))
            {
                if ((target - rayHit.point).sqrMagnitude < threshold * threshold)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }
        public static float[] ParseToFloatArray(string floatString)
        {
            string[] floatStrings = floatString.Split(new char[] { ',' });
            float[] floatArray = new float[floatStrings.Length];
            for (int i = 0; i < floatStrings.Length; i++)
            {
                floatArray[i] = float.Parse(floatStrings[i]);
            }

            return floatArray;
        }
        public static int[] ParseToIntArray(string intString)
        {
            string[] intStrings = intString.Split(new char[] { ',' });
            int[] intArray = new int[intStrings.Length];
            for (int i = 0; i < intStrings.Length; i++)
            {
                intArray[i] = int.Parse(intStrings[i]);
            }

            return intArray;
        }
        /// <summary>
        /// Parse a comma-separated string as an array of the given enum.
        /// </summary>
        /// <typeparam name="T">The enum type to parse as.</typeparam>
        /// <param name="enumString">The comma-separated enum names or values.</param>
        /// <returns>An array of enums.</returns>
        public static T[] ParseEnumArray<T>(string enumString) where T : Enum
        {
            string[] enumStrings = enumString.Split(new char[] { ',' }); // Split the string on the commas.
            string[] enumNames = Enum.GetNames(typeof(T)); // Get the enum names.
            for (int i = 0; i < enumStrings.Length; i++) //legacy support for int-based enum strings (e.g. antiradtargetTypes = 0.5 vs antiradTargetTypes = SAM,Detection)
            {
                if (int.TryParse(enumStrings[i], out int intValue))
                {
                    if (Enum.IsDefined(typeof(T), intValue))
                        enumStrings[i] = Enum.GetName(typeof(T), intValue);    //if there's ints in the string, convert them
                }
            }
            T[] enumArray = [.. enumStrings.Where(enumNames.Contains).Select(e => Enum.Parse(typeof(T), e)).Cast<T>()]; // then feed the enum names into an Enum array
            if (!enumStrings.All(enumNames.Contains)) // Check for invalid values.
                Debug.LogError($"[BDArmory.OtherUtils]: Invalid enum ({typeof(T)}) values: {string.Join(", ", enumStrings.Where(e => !enumNames.Contains(e)))}");
            return enumArray;
        }

        public static KeyBinding AGEnumToKeybinding(KSPActionGroup group)
        {
            string groupName = group.ToString();
            if (groupName.Contains("Custom"))
            {
                groupName = groupName.Substring(6);
                int customNumber = int.Parse(groupName);
                groupName = "CustomActionGroup" + customNumber;
            }
            else
            {
                return null;
            }

            FieldInfo field = typeof(GameSettings).GetField(groupName);
            return (KeyBinding)field.GetValue(null);
        }

        public static string JsonCompat(string json)
        {
            return json.Replace('{', '<').Replace('}', '>');
        }

        public static string JsonDecompat(string json)
        {
            return json.Replace('<', '{').Replace('>', '}');
        }

        public static void SetTimeOverride(bool enabled)
        {
            BDArmorySettings.TIME_OVERRIDE = enabled;
            Time.timeScale = enabled ? BDArmorySettings.TIME_SCALE : 1f;
        }

        // LINQ.All() returns true for empty collections. Sometimes we want it to be false in those cases.
        public static bool AllAndNotEmpty<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            return source.Any() && source.All(predicate);
        }
    }

    /// <summary>
    /// Custom yield instruction that allows waiting for a number of seconds based on the FixedUpdate cycle instead of the Update cycle.
    /// Based on http://answers.unity.com/comments/1910230/view.html
    /// 
    /// Notes:
    ///  - All Unity yield instructions other than WaitForFixedUpdate wait until the next Update cycle to check their conditions, including "yield return null".
    ///    For any yielding that is physics related, use WaitForFixedUpdate (use a single instance and yield it multiple times) or one of the classes below.
    ///  - These "wait" enumerators always wait at least one cycle. If immediately continuing is desired, use a manual WaitForFixedUpdate loop.
    /// </summary>
    public class WaitForSecondsFixed : IEnumerator
    {
        private WaitForFixedUpdate wait = new WaitForFixedUpdate();
        public virtual object Current => this.wait;
        float endTime, seconds;

        public WaitForSecondsFixed(float seconds)
        {
            this.seconds = seconds;
            this.Reset();
        }

        public bool MoveNext() => this.keepWaiting;
        public virtual bool keepWaiting => (Time.fixedTime < endTime);
        public virtual void Reset() => this.endTime = Time.fixedTime + this.seconds;
    }

    /// <summary>
    /// Custom yield instruction that allows yielding until a predicate is satisfied based on the FixedUpdate cycle instead of the Update cycle.
    /// </summary>
    public class WaitUntilFixed : IEnumerator
    {
        private WaitForFixedUpdate wait = new WaitForFixedUpdate();
        public virtual object Current => wait;
        Func<bool> predicate;

        public WaitUntilFixed(Func<bool> predicate)
        {
            this.predicate = predicate;
        }

        public bool MoveNext() => !predicate();
        public virtual void Reset() { }
    }

    /// <summary>
    /// Custom yield instruction that allows yielding while a predicate is satisfied based on the FixedUpdate cycle instead of the Update cycle.
    /// </summary>
    public class WaitWhileFixed : IEnumerator
    {
        private WaitForFixedUpdate wait = new WaitForFixedUpdate();
        public virtual object Current => wait;
        Func<bool> predicate;

        public WaitWhileFixed(Func<bool> predicate)
        {
            this.predicate = predicate;
        }

        public bool MoveNext() => predicate();
        public virtual void Reset() { }
    }

    public enum Toggle { On, Off, Toggle, NoChange }; // Turn something on, off, toggle it or leave it as it is.


    /// <summary>
    /// For serializing List<List<string>>
    /// </summary>
    [Serializable]
    public struct StringList
    {
        public List<string> ls;
    }

    /// <summary>
    /// Comparer for raycast hit sorting.
    /// </summary>
    public class RaycastHitComparer : IComparer<RaycastHit>
    {
        int IComparer<RaycastHit>.Compare(RaycastHit left, RaycastHit right)
        {
            return left.distance.CompareTo(right.distance);
        }
        public static RaycastHitComparer raycastHitComparer = new RaycastHitComparer();
    }
}