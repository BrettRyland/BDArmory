using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System;
using UnityEngine;

using BDArmory.Extensions;
using BDArmory.Settings;
using BDArmory.UI;

namespace BDArmory.Utils
{
    public static class AIUtils
    {
        public enum UseAccel {None, Immediate, Smoothed};
        /// <summary>
        /// Predict a future position of a vessel given its current position, velocity and acceleration
        /// </summary>
        /// <param name="v">vessel to be extrapolated</param>
        /// <param name="time">after this time</param>
        /// <returns>Vector3 extrapolated position</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 PredictPosition(this Vessel v, float time, UseAccel immediate=UseAccel.Immediate)
        {
            var vel = v.Velocity();
            var acc = immediate switch
            {
                UseAccel.None => Vector3d.zero,
                UseAccel.Smoothed => v.acceleration,
                _ => v.acceleration_immediate
            };
            var time2 = 0.5f * time * time;
            return new Vector3(
                (float)(v.CoM.x + time * vel.x + time2 * acc.x),
                (float)(v.CoM.y + time * vel.y + time2 * acc.y),
                (float)(v.CoM.z + time * vel.z + time2 * acc.z)
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 PredictPosition(in Vector3 position, in Vector3 velocity, in Vector3 acceleration, float time)
        {
            float time2 = 0.5f * time * time;
            return new Vector3(
                position.x + time * velocity.x + time2 * acceleration.x,
                position.y + time * velocity.y + time2 * acceleration.y,
                position.z + time * velocity.z + time2 * acceleration.z);
            //return position + time * velocity + 0.5f * time * time * acceleration;
        }

        public enum CPAType
        {
            Earliest, // The earliest future CPA solution.
            Latest, // The latest future CPA solution (even if beyond the max time).
            Closest // The closest CPA solution within the range 0 — max time.
        };
        /// <summary>
        /// Predict the next time to the closest point of approach within the next maxTime seconds using the same kinematics as PredictPosition (i.e, position, velocity and acceleration).
        /// </summary>
        /// <param name="vessel">The first vessel.</param>
        /// <param name="v">The second vessel.</param>
        /// <param name="maxTime">The maximum time to look ahead.</param>
        /// <param name="cpaType">When multiple valid solutions exist, return the one of the given type.</param>
        /// <returns>float The time to the closest point of approach within the next maxTime seconds.</returns>
        public static float TimeToCPA(this Vessel vessel, Vessel v, float maxTime = float.MaxValue, CPAType cpaType = CPAType.Earliest)
        { // Find the closest/furthest future time to closest point of approach considering accelerations in addition to velocities. This uses the generalisation of Cardano's solution to finding roots of cubics to find where the derivative of the separation is a minimum.
            if (vessel == null) return 0f; // We don't have a vessel.
            if (v == null) return 0f; // We don't have a target.
            return vessel.TimeToCPA(v.transform.position, v.Velocity(), v.acceleration, maxTime, cpaType);
        }

        /// <summary>
        /// Predict the next time to the closest point of approach within the next maxTime seconds using the same kinematics as PredictPosition (i.e, position, velocity and acceleration).
        /// </summary>
        /// <param name="vessel">The first vessel.</param>
        /// <param name="targetPosition">The second vessel position.</param>
        /// <param name="targetVelocity">The second vessel velocity.</param>
        /// <param name="targetAcceleration">The second vessel acceleration.</param>
        /// <param name="maxTime">The maximum time to look ahead.</param>
        /// <param name="cpaType">When multiple valid solutions exist, return the one of the given type.</param>
        /// <returns>
        public static float TimeToCPA(this Vessel vessel, in Vector3 targetPosition, in Vector3 targetVelocity, in Vector3 targetAcceleration, float maxTime = float.MaxValue, CPAType cpaType = CPAType.Earliest)
        {
            if (vessel == null) return 0f; // We don't have a vessel.
            Vector3 relPosition = targetPosition - vessel.CoM;
            Vector3 relVelocity = targetVelocity - vessel.Velocity();
            Vector3 relAcceleration = targetAcceleration - vessel.acceleration;
            return TimeToCPA(relPosition, relVelocity, relAcceleration, maxTime, cpaType);
        }

        /// <summary>
        /// Predict the time to the closest point of approach within the next maxTime seconds using the relative position, velocity and acceleration.
        /// </summary>
        /// <param name="relPosition">The relative separation.</param>
        /// <param name="relVelocity">The relative velocity.</param>
        /// <param name="relAcceleration">The relative acceleration.</param>
        /// <param name="maxTime">The maximum time to look ahead.</param>
        /// <param name="cpaType">When multiple valid solutions exist, return the one of the given type.</param>
        /// <returns></returns>
        public static float TimeToCPA(in Vector3 relPosition, in Vector3 relVelocity, in Vector3 relAcceleration, float maxTime = float.MaxValue, CPAType cpaType = CPAType.Earliest)
        {
            float a = Vector3.Dot(relAcceleration, relAcceleration);
            float c = Vector3.Dot(relVelocity, relVelocity);
            if (a == 0 || a * maxTime < 1e-3f * c) // Not actually a cubic. Relative acceleration is zero or insignificant within the time limit, so return the much simpler linear timeToCPA.
            {
                if (c > 0)
                    return Mathf.Clamp(-Vector3.Dot(relPosition, relVelocity) / relVelocity.sqrMagnitude, 0f, maxTime);
                else
                    return 0; // The objects are static, so they're not going to get any closer.
            }

            float A = a / 2f;
            float B = Vector3.Dot(relVelocity, relAcceleration) * 3f / 2f;
            float C = c + Vector3.Dot(relPosition, relAcceleration);
            float D = Vector3.Dot(relPosition, relVelocity);
            float D0 = B * B - 3f * A * C;
            float D1 = 2f * B * B * B - 9f * A * B * C + 27f * A * A * D;
            float E = D1 * D1 - 4f * D0 * D0 * D0; // = -27*A^2*discriminant
            // float discriminant = 18f * A * B * C * D - 4f * Mathf.Pow(B, 3f) * D + Mathf.Pow(B, 2f) * Mathf.Pow(C, 2f) - 4f * A * Mathf.Pow(C, 3f) - 27f * Mathf.Pow(A, 2f) * Mathf.Pow(D, 2f);
            if (E > 0)
            { // Single solution (E is positive)
                float F = (D1 + Mathf.Sign(D1) * BDAMath.Sqrt(E)) / 2f;
                float G = Mathf.Sign(F) * Mathf.Pow(Mathf.Abs(F), 1f / 3f);
                float time = -1f / 3f / A * (B + G + D0 / G);
                return Mathf.Clamp(time, 0f, maxTime);
            }
            else if (E < 0)
            { // Triple solution (E is negative)
                float F_real = D1 / 2f;
                float F_imag = Mathf.Sign(D1) * BDAMath.Sqrt(-E) / 2f;
                float F_abs = BDAMath.Sqrt(F_real * F_real + F_imag * F_imag);
                float F_ang = Mathf.Atan2(F_imag, F_real);
                float G_abs = Mathf.Pow(F_abs, 1f / 3f);
                float G_ang = F_ang / 3f;
                float time = -1f;
                float distanceSqr = float.MaxValue;
                for (int i = 0; i < 3; ++i)
                {
                    float G = G_abs * Mathf.Cos(G_ang + 2f * (float)i * Mathf.PI / 3f);
                    float t = -1f / 3f / A * (B + G + D0 * G / G_abs / G_abs);
                    if (Mathf.Sign(C + 2f * t * B + 3f * t * t * A) > 0) // It's a minimum. There can be at most 2 minima and 1 maxima.
                    {
                        switch (cpaType)
                        {
                            case CPAType.Earliest:
                                if (t > 0 && (time < 0 || t < time)) time = t;
                                break;
                            case CPAType.Latest:
                                if (t > time) time = t;
                                break;
                            case CPAType.Closest:
                                t = Mathf.Clamp(t, 0, maxTime);
                                var distSqr = (relPosition + t * relVelocity + t * t / 2f * relAcceleration).sqrMagnitude;
                                if (distSqr < distanceSqr)
                                {
                                    distanceSqr = distSqr;
                                    time = t;
                                }
                                break;
                        }
                    }
                }
                return Mathf.Clamp(time, 0f, maxTime);
            }
            else
            { // Repeated root
                if (Mathf.Abs(D0) == 0f)
                { // A triple-root.
                    return Mathf.Clamp(-B / 3f / A, 0f, maxTime);
                }
                else
                { // Double root and simple root.
                    float time = -1f;
                    float t0 = (9f * A * D - B * C) / 2f / D0;
                    float t1 = (4f * A * B * C - 9f * A * A * D - B * B * B) / A / D0;
                    switch (cpaType)
                    {
                        case CPAType.Earliest:
                            if (t0 > 0 && (time < 0 || t0 < time)) time = t0;
                            if (t1 > 0 && (time < 0 || t1 < time)) time = t1;
                            break;
                        case CPAType.Latest:
                            if (t0 > time) time = t0;
                            if (t1 > time) time = t1;
                            break;
                        case CPAType.Closest:
                            t0 = Mathf.Clamp(t0, 0, maxTime);
                            t1 = Mathf.Clamp(t1, 0, maxTime);
                            time = ((relPosition + t0 * relVelocity + t0 * t0 / 2f * relAcceleration).sqrMagnitude < (relPosition + t1 * relVelocity + t1 * t1 / 2f * relAcceleration).sqrMagnitude) ? t0 : t1;
                            break;
                    }
                    return Mathf.Clamp(time, 0, maxTime);
                }
            }
        }

        public static float PredictClosestApproachSqrSeparation(this Vessel vessel, Vessel otherVessel, float maxTime)
        {
            var timeToCPA = vessel.TimeToCPA(otherVessel, maxTime);
            if (timeToCPA > 0 && timeToCPA < maxTime)
                return (vessel.PredictPosition(timeToCPA) - otherVessel.PredictPosition(timeToCPA)).sqrMagnitude;
            else
                return float.MaxValue;
        }

        /// <summary>
        /// Get the altitude of terrain below/above a point.
        /// </summary>
        /// <param name="position">World position, not geo position (use VectorUtils.GetWorldSurfacePostion to convert lat,long,alt to world position)</param>
        /// <param name="body">usually vessel.MainBody</param>
        /// <returns>terrain height</returns>
        public static float GetTerrainAltitude(in Vector3 position, CelestialBody body, bool underwater = true)
        {
            return (float)body.TerrainAltitude(body.GetLatitude(position), body.GetLongitude(position), underwater);
        }

        public static Vessel VesselClosestTo(Vector3 position, bool useGeoCoords = false)
        {
            Vessel closestV = null;
            float closestSqrDist = float.MaxValue;
            if (FlightGlobals.Vessels == null) return null;
            if (useGeoCoords)
            {
                if (FlightGlobals.currentMainBody is null) return null;
                position = (Vector3)FlightGlobals.currentMainBody.GetWorldSurfacePosition(position.x, position.y, position.z);
            }
            using (var v = FlightGlobals.Vessels.GetEnumerator())
                while (v.MoveNext())
                {
                    if (v.Current == null || !v.Current.loaded || v.Current.packed) continue;
                    if (VesselModuleRegistry.IgnoredVesselTypes.Contains(v.Current.vesselType)) continue;
                    var wm = v.Current.ActiveController().WM;
                    if (wm != null)
                    {
                        if (Vector3.SqrMagnitude(v.Current.vesselTransform.position - position) < closestSqrDist)
                        {
                            closestSqrDist = Vector3.SqrMagnitude(v.Current.vesselTransform.position - position);
                            closestV = v.Current;
                        }
                    }
                }
            return closestV;
        }

        [Flags]
        public enum VehicleMovementType
        {
            Stationary = 0,
            Land = 1,
            Water = 2,
            Amphibious = Land | Water,
            Submarine = 4 | Water,
        }

        /// <summary>
        /// Minimum depth for water ships to consider the terrain safe.
        /// </summary>
        public const float MinDepth = -10f;

        /// <summary>
        /// A grid approximation for a spherical body for AI pathing purposes.
        /// </summary>
        public class TraversabilityMatrix
        {
            // edge of each grid cell
            const float GridSizeDefault = 400f;
            const float GiveUpHeuristicMultiplier = 3;
            const float RetraceReluctanceMultiplier = 1.01f;
            float GridSize;
            float GridDiagonal;

            // how much the gird can get distorted before it is rebuilt instead of expanded
            const float MaxDistortion = 0.02f;

            Dictionary<Coords, Cell> grid = new Dictionary<Coords, Cell>();
            Dictionary<Coords, float> cornerAlts;

            float rebuildDistance;
            CelestialBody body;
            float maxSlopeAngle;
            Vector3 origin;
            VehicleMovementType movementType;

            /// <summary>
            /// Create a new traversability matrix.
            /// </summary>
            /// <param name="start">Origin point, in Lat,Long,Alt form</param>
            /// <param name="end">Destination point, in Lat,Long,Alt form</param>
            /// <param name="body">Body on which the grid is created</param>
            /// <param name="vehicleType">Movement type of the vehicle (surface/land)</param>
            /// <param name="maxSlopeAngle">The highest slope angle (in degrees) the vessel can traverse in a straight line</param>
            /// <returns>List of geo coordinate vectors of waypoints to traverse in straight lines to reach the destination</returns>
            public List<Vector3> Pathfind(Vector3 start, Vector3 end, CelestialBody body, VehicleMovementType vehicleType, float maxSlopeAngle, float minObstacleMass)
            {
                checkGrid(start, body, vehicleType, maxSlopeAngle, minObstacleMass,
                    Mathf.Clamp(VectorUtils.GeoDistance(start, end, body) / 20, GridSizeDefault, GridSizeDefault * 5));

                Coords startCoords = getGridCoord(start);
                Coords endCoords = getGridCoord(end);
                float initialDistance = gridDistance(startCoords, endCoords);

                SortedDictionary<CellValue, float> sortedCandidates = new SortedDictionary<CellValue, float>(new CellValueComparer())
                { [new CellValue(getCellAt(startCoords), initialDistance)] = 0 }; //(openSet and fScore), gScore
                Dictionary<Cell, float> candidates = new Dictionary<Cell, float>
                { [getCellAt(startCoords)] = initialDistance }; // secondary dictionary to sortedCandidates for faster lookup

                Dictionary<Cell, float> nodes = new Dictionary<Cell, float> //gScore
                { [getCellAt(startCoords)] = 0 };

                Dictionary<Cell, Cell> backtrace = new Dictionary<Cell, Cell>(); //cameFrom
                HashSet<Cell> visited = new HashSet<Cell>();

                Cell current = null;
                float currentFScore = 0;
                KeyValuePair<Cell, float> best = new KeyValuePair<Cell, float>(getCellAt(startCoords), initialDistance * GiveUpHeuristicMultiplier);

                List<KeyValuePair<Coords, float>> adjacent = new List<KeyValuePair<Coords, float>>(8)
                {
                    new KeyValuePair<Coords, float>(new Coords(0, 1), GridSize),
                    new KeyValuePair<Coords, float>(new Coords(1, 0), GridSize),
                    new KeyValuePair<Coords, float>(new Coords(0, -1), GridSize),
                    new KeyValuePair<Coords, float>(new Coords(-1, 0), GridSize),
                    new KeyValuePair<Coords, float>(new Coords(1, 1), GridDiagonal),
                    new KeyValuePair<Coords, float>(new Coords(1, -1), GridDiagonal),
                    new KeyValuePair<Coords, float>(new Coords(-1, -1), GridDiagonal),
                    new KeyValuePair<Coords, float>(new Coords(-1, 1), GridDiagonal),
                };

                while (candidates.Count > 0)
                {
                    // take the best candidate - since now we use SortedDict, it's the first one
                    using (var e = sortedCandidates.GetEnumerator())
                    {
                        e.MoveNext();
                        current = e.Current.Key.Cell;
                        currentFScore = e.Current.Key.Value;
                        candidates.Remove(e.Current.Key.Cell);
                        sortedCandidates.Remove(e.Current.Key);
                    }
                    // stop if we found our destination
                    if (current.Coords == endCoords)
                        break;
                    if (currentFScore > best.Value)
                    {
                        current = best.Key;
                        break;
                    }

                    visited.Add(current);
                    float currentNodeScore = nodes[current];

                    using (var adj = adjacent.GetEnumerator())
                        while (adj.MoveNext())
                        {
                            Cell neighbour = getCellAt(current.Coords + adj.Current.Key);
                            if (!neighbour.Traversable || visited.Contains(neighbour)) continue;
                            if (candidates.TryGetValue(neighbour, out float value))
                            {
                                if (currentNodeScore + adj.Current.Value >= value)
                                    continue;
                                else
                                    sortedCandidates.Remove(new CellValue(neighbour, value)); //we'll reinsert with the adjusted value, so it's sorted properly
                            }
                            nodes[neighbour] = currentNodeScore + adj.Current.Value;
                            backtrace[neighbour] = current;
                            float remainingDistanceEstimate = gridDistance(neighbour.Coords, endCoords);
                            float fScoreEstimate = currentNodeScore + adj.Current.Value + remainingDistanceEstimate * RetraceReluctanceMultiplier;
                            sortedCandidates[new CellValue(neighbour, fScoreEstimate)] = currentNodeScore + adj.Current.Value;
                            candidates[neighbour] = currentNodeScore + adj.Current.Value;
                            if ((fScoreEstimate + remainingDistanceEstimate * (GiveUpHeuristicMultiplier - 1)) < best.Value)
                                best = new KeyValuePair<Cell, float>(neighbour, fScoreEstimate + remainingDistanceEstimate * (GiveUpHeuristicMultiplier - 1));
                        }
                }

                var path = new List<Cell>();
                while (current.Coords != startCoords)
                {
                    path.Add(current);
                    current = backtrace[current];
                }
                path.Reverse();

                if (path.Count > 2)
                {
                    var newPath = new List<Cell>() { path[0] };
                    for (int i = 1; i < path.Count - 1; ++i)
                    {
                        if (path[i].Coords - path[i - 1].Coords != path[i + 1].Coords - path[1].Coords)
                            newPath.Add(path[i]);
                    }
                    newPath.Add(path[path.Count - 1]);
                    path = newPath;
                }

                var pathReduced = new List<Vector3>();
                Coords waypoint = startCoords;
                for (int i = 1; i < path.Count; ++i)
                {
                    if (!straightPath(waypoint.X, waypoint.Y, path[i].X, path[i].Y))
                    {
                        pathReduced.Add(path[i - 1].GeoPos);
                        waypoint = path[i - 1].Coords;
                    }
                }

                // if not path found
                if (path.Count == 0)
                {
                    if (startCoords == endCoords)
                        pathReduced.Add(end);
                    else
                        pathReduced.Add(start);
                }
                else if (path[path.Count - 1].Coords == endCoords)
                    pathReduced.Add(end);
                else
                    pathReduced.Add(path[path.Count - 1].GeoPos);

                return pathReduced;
            }

            /// <summary>
            /// Check if line is traversable. Due to implementation specifics, it is advised not to use this if the start point is not the position of the vessel.
            /// </summary>
            /// <param name="startGeo">start point in Lat,Long,Alt form</param>
            /// <param name="endGeo">end point, in Lat,Long,Alt form</param>
            public bool TraversableStraightLine(Vector3 startGeo, Vector3 endGeo, CelestialBody body, VehicleMovementType vehicleType, float maxSlopeAngle, float minObstacleMass)
            {
                checkGrid(startGeo, body, vehicleType, maxSlopeAngle, minObstacleMass);
                return TraversableStraightLine(startGeo, endGeo);
            }

            public bool TraversableStraightLine(Vector3 startGeo, Vector3 endGeo)
            {
                float[] location = getGridLocation(startGeo);
                float[] endPos = getGridLocation(endGeo);

                return straightPath(location[0], location[1], endPos[0], endPos[1]);
            }

            private void checkGrid(Vector3 origin, CelestialBody body, VehicleMovementType vehicleType, float maxSlopeAngle, float minMass, float gridSize = GridSizeDefault)
            {
                if (grid == null || VectorUtils.GeoDistance(this.origin, origin, body) > rebuildDistance || Mathf.Abs(gridSize - GridSize) > 100 ||
                    this.body != body || movementType != vehicleType || this.maxSlopeAngle != maxSlopeAngle * Mathf.Deg2Rad)
                {
                    GridSize = gridSize;
                    GridDiagonal = gridSize * BDAMath.Sqrt(2);
                    this.body = body;
                    this.maxSlopeAngle = maxSlopeAngle * Mathf.Deg2Rad;
                    rebuildDistance = Mathf.Clamp(Mathf.Asin(MaxDistortion) * (float)body.Radius, GridSize * 4, GridSize * 256);
                    movementType = vehicleType;
                    this.origin = origin;
                    grid = new Dictionary<Coords, Cell>();
                    cornerAlts = new Dictionary<Coords, float>();
                }
                includeDebris(minMass);
            }

            private Cell getCellAt(int x, int y) => getCellAt(new Coords(x, y));

            private Cell getCellAt(Coords coords)
            {
                if (!grid.TryGetValue(coords, out Cell cell))
                {
                    cell = new Cell(coords, gridToGeo(coords), CheckTraversability(coords), body);
                    grid[coords] = cell;
                }
                return cell;
            }

            /// <summary>
            /// Check all debris on the ground, and mark those squares impassable.
            /// </summary>
            private void includeDebris(float minMass)
            {
                using (var vs = BDATargetManager.LoadedVessels.GetEnumerator())
                    while (vs.MoveNext())
                    {
                        if ((vs.Current == null || vs.Current.vesselType != VesselType.Debris || vs.Current.IsControllable || !vs.Current.LandedOrSplashed
                            || vs.Current.mainBody.GetAltitude(vs.Current.CoM) < MinDepth || vs.Current.GetTotalMass() < minMass)) continue;

                        var debrisPos = getGridLocation(VectorUtils.WorldPositionToGeoCoords(vs.Current.CoM, body));
                        var coordArray = new List<Coords>
                        {
                            new Coords(Mathf.CeilToInt(debrisPos[0]), Mathf.CeilToInt(debrisPos[1])),
                            new Coords(Mathf.CeilToInt(debrisPos[0]), Mathf.FloorToInt(debrisPos[1])),
                            new Coords(Mathf.FloorToInt(debrisPos[0]), Mathf.CeilToInt(debrisPos[1])),
                            new Coords(Mathf.FloorToInt(debrisPos[0]), Mathf.FloorToInt(debrisPos[1])),
                        };
                        using (var coords = coordArray.GetEnumerator())
                        {
                            while (coords.MoveNext())
                            {
                                if (grid.TryGetValue(coords.Current, out Cell cell))
                                    cell.Traversable = false;
                                else
                                    grid[coords.Current] = new Cell(coords.Current, gridToGeo(coords.Current), false, body);
                            }
                        }
                    }
            }

            private bool straightPath(float originX, float originY, float destX, float destY)
            {
                float dX = (destX - originX);
                float dY = (destY - originY);
                int dirX = Math.Sign(dX);
                int dirY = Math.Sign(dY);
                int sX = Mathf.RoundToInt(originX);
                int sY = Mathf.RoundToInt(originY);

                int xP = 0;
                int yP = 0;
                float xT = Mathf.Abs(dX);
                float yT = Mathf.Abs(dY);

                while (xP < xT || yP < yT)
                {
                    float ratio = Mathf.Abs(Mathf.Max(xT - xP, 0) / Mathf.Max(yT - yP, 0));

                    if (ratio > 0.49)
                        ++xP;
                    if (ratio < 2.04)
                        ++yP;

                    if (!getCellAt(sX + xP * dirX, sY + yP * dirY).Traversable)
                        return false;
                }

                return true;
            }

            // calculate location on grid
            private float[] getGridLocation(Vector3 geoPoint)
            {
                var distance = VectorUtils.GeoDistance(origin, geoPoint, body) / GridSize;
                var bearing = VectorUtils.GeoForwardAzimuth(origin, geoPoint) * Mathf.Deg2Rad;
                var x = distance * Mathf.Cos(bearing);
                var y = distance * Mathf.Sin(bearing);
                return new float[2] { x, y };
            }

            // round grid coordinates to get cell
            private Coords getGridCoord(float[] gridLocation)
                => new Coords(Mathf.RoundToInt(gridLocation[0]), Mathf.RoundToInt(gridLocation[1]));

            private Coords getGridCoord(Vector3 geoPosition)
                => getGridCoord(getGridLocation(geoPosition));

            private float gridDistance(Coords point, Coords other)
            {
                float dX = Mathf.Abs(point.X - other.X);
                float dY = Mathf.Abs(point.Y - other.Y);
                return GridDiagonal * Mathf.Min(dX, dY) + GridSize * Mathf.Abs(dX - dY);
            }

            // positive y towards north, positive x towards east
            Vector3 gridToGeo(float x, float y)
            {
                if (x == 0 && y == 0) return origin;
                return VectorUtils.GeoCoordinateOffset(origin, body, Mathf.Atan2(y, x) * Mathf.Rad2Deg, BDAMath.Sqrt(x * x + y * y) * GridSize);
            }

            Vector3 gridToGeo(Coords coords) => gridToGeo(coords.X, coords.Y);

            private class Cell
            {
                public Cell(Coords coords, Vector3 geoPos, bool traversable, CelestialBody body)
                {
                    Coords = coords;
                    GeoPos = geoPos;
                    GeoPos.z = (float)body.TerrainAltitude(GeoPos.x, GeoPos.y);
                    Traversable = traversable;
                    this.body = body;
                }

                private CelestialBody body;
                public readonly Coords Coords;
                public readonly Vector3 GeoPos;
                public Vector3 WorldPos => VectorUtils.GetWorldSurfacePostion(GeoPos, body);
                public bool Traversable;

                public int X => Coords.X;
                public int Y => Coords.Y;

                public override string ToString() => $"[{X}, {Y}, {Traversable}]";

                public override int GetHashCode() => Coords.GetHashCode();

                public bool Equals(Cell other) => X == other?.X && Y == other.Y && Traversable == other.Traversable;

                public override bool Equals(object obj) => Equals(obj as Cell);

                public static bool operator ==(Cell left, Cell right) => object.Equals(left, right);

                public static bool operator !=(Cell left, Cell right) => !object.Equals(left, right);
            }

            private struct CellValue
            {
                public CellValue(Cell cell, float value)
                {
                    Cell = cell;
                    Value = value;
                }

                public readonly Cell Cell;
                public readonly float Value;

                public override int GetHashCode() => Cell.Coords.GetHashCode();
            }

            private class CellValueComparer : IComparer<CellValue>
            {
                /// <summary>
                /// This a very specific implementation for pathfinding to make use of the sorted dictionary.
                /// It is non-commutative and not order-invariant.
                /// But that is exactly how we want it right now.
                /// </summary>
                /// <returns>Lies and misinformation of the best kind.</returns>
                public int Compare(CellValue x, CellValue y)
                {
                    if (x.Cell.Equals(y.Cell))
                        return 0;
                    if (x.Value > y.Value)
                        return 1;
                    return -1;
                }
            }

            // because int[] does not produce proper hashes
            private struct Coords
            {
                public readonly int X;
                public readonly int Y;

                public Coords(int x, int y)
                {
                    X = x;
                    Y = y;
                }

                public bool Equals(Coords other)
                {
                    if (other == null) return false;
                    return (X == other.X && Y == other.Y);
                }

                public override bool Equals(object obj)
                {
                    if (!(obj is Coords)) return false;
                    return Equals((Coords)obj);
                }

                public static bool operator ==(Coords left, Coords right) => object.Equals(left, right);

                public static bool operator !=(Coords left, Coords right) => !object.Equals(left, right);

                public static Coords operator +(Coords left, Coords right) => new Coords(left.X + right.X, left.Y + right.Y);

                public static Coords operator -(Coords left, Coords right) => new Coords(left.X - right.X, left.Y - right.Y);

                public override int GetHashCode() => X.GetHashCode() * 1009 + Y.GetHashCode();

                public override string ToString() => $"[{X}, {Y}]";
            }

            private float getCornerAlt(int x, int y) => getCornerAlt(new Coords(x, y));

            private float getCornerAlt(Coords coords)
            {
                if (!cornerAlts.TryGetValue(coords, out float alt))
                {
                    var geo = gridToGeo(coords.X - 0.5f, coords.Y - 0.5f);
                    alt = (float)body.TerrainAltitude(geo.x, geo.y, true);
                    cornerAlts[coords] = alt;
                }
                return alt;
            }

            private bool CheckTraversability(Coords coords)
            {
                float[] cornerAlts = new float[4]
                {
                    getCornerAlt(coords.X, coords.Y),
                    getCornerAlt(coords.X+1, coords.Y),
                    getCornerAlt(coords.X+1, coords.Y+1),
                    getCornerAlt(coords.X, coords.Y+1),
                };

                for (int i = 0; i < 4; i++)
                {
                    // check if we have the correct surface on all corners (land/water)
                    switch (movementType)
                    {
                        case VehicleMovementType.Amphibious:
                            break;

                        case VehicleMovementType.Land:
                            if (cornerAlts[i] < 0) return false;
                            break;

                        case VehicleMovementType.Water:
                            if (cornerAlts[i] > MinDepth) return false;
                            break;

                        case VehicleMovementType.Stationary:
                        default:
                            return false;
                    }
                    // set max to zero for slope check
                    if (cornerAlts[i] < 0) cornerAlts[i] = 0;
                }

                // check if angles are not too steep (if it's a land vehicle)
                if ((movementType & VehicleMovementType.Land) == VehicleMovementType.Land
                    && (checkSlope(cornerAlts[0], cornerAlts[1], GridSize)
                    || checkSlope(cornerAlts[1], cornerAlts[2], GridSize)
                    || checkSlope(cornerAlts[2], cornerAlts[3], GridSize)
                    || checkSlope(cornerAlts[3], cornerAlts[0], GridSize)
                    || checkSlope(cornerAlts[0], cornerAlts[2], GridDiagonal)
                    || checkSlope(cornerAlts[1], cornerAlts[3], GridDiagonal)))
                    return false;

                return true;
            }

            bool checkSlope(float alt1, float alt2, float length) => Mathf.Abs(Mathf.Atan2(alt1 - alt2, length)) > maxSlopeAngle;

            public void DrawDebug(Vector3 currentWorldPos, List<Vector3> waypoints = null)
            {
                Vector3 upVec = VectorUtils.GetUpDirection(currentWorldPos) * 10;
                if (BDArmorySettings.DISPLAY_PATHING_GRID)
                    using (var kvp = grid.GetEnumerator())
                        while (kvp.MoveNext())
                        {
                            GUIUtils.DrawLineBetweenWorldPositions(kvp.Current.Value.WorldPos, kvp.Current.Value.WorldPos + upVec, 3,
                                kvp.Current.Value.Traversable ? Color.green : Color.red);
                        }
                if (waypoints != null)
                {
                    var previous = currentWorldPos;
                    using (var wp = waypoints.GetEnumerator())
                        while (wp.MoveNext())
                        {
                            var c = VectorUtils.GetWorldSurfacePostion(wp.Current, body);
                            GUIUtils.DrawLineBetweenWorldPositions(previous + upVec, c + upVec, 2, Color.cyan);
                            previous = c;
                        }
                }
            }
        }
    }
}
