// 
//  MIT License
//  
//  Copyright (c) 2018 William "Xyphos" Scott
//  
//  Permission is hereby granted, free of charge, to any person obtaining a copy
//  of this software and associated documentation files (the "Software"), to deal
//  in the Software without restriction, including without limitation the rights
//  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//  copies of the Software, and to permit persons to whom the Software is
//  furnished to do so, subject to the following conditions:
//  
//  The above copyright notice and this permission notice shall be included in all
//  copies or substantial portions of the Software.
//  
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//  SOFTWARE.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace KeyNode
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class KeyNode : MonoBehaviour
    {
        #region --- [ fields ] ------------------------------------------------------------------------------------------------

        private const string ID = "[KeyNode]";

        private static double _delta;
        private static bool _rightAlt, _rightCtrl, _rightShift;

        // lookup table for delta modifiers
        private static readonly double[] ModKeyMap =
        {
            1.0, // 0 no mod keys held
            10.0, // 1 rctrl
            100.0, // 2 rshift
            1000.0, // 3 rshift + rctrl
            //---
            0.1, // 4 only ralt held
            0.01, // 5 ralt + rctrl
            0.001, // 6 ralt + rshift
            0.0001 // 7 all mod keys held
        };

        private static readonly Dictionary<KeyCode, Callback> ModifierKeys = new Dictionary<KeyCode, Callback>
        {
            {KeyCode.Keypad1, () => currentNode.DeltaV.z -= _delta}, // subtract prograde
            {KeyCode.Keypad2, () => currentNode.DeltaV.y -= _delta}, // subtract normal
            {KeyCode.Keypad3, () => currentNode.UT -= _delta}, // subtract time
            {KeyCode.Keypad4, () => currentNode.DeltaV.x -= _delta}, // subtract radial
            // Keypad5 is reserved
            {KeyCode.Keypad6, () => currentNode.DeltaV.x += _delta}, // add raidal
            {KeyCode.Keypad7, () => currentNode.DeltaV.z += _delta}, // add prograde
            {KeyCode.Keypad8, () => currentNode.DeltaV.y += _delta}, // add normal
            {KeyCode.Keypad9, () => currentNode.UT += _delta}, // add time
            {KeyCode.KeypadPlus, () => currentNode.UT += orbit.period}, // add an orbit
            {
                KeyCode.KeypadMinus, () => // subtract an orbit, if able. (don't go into the past)
                {
                    var ut = currentNode.UT - orbit.period;
                    if (ut > UT)
                        currentNode.UT = ut;
                }
            },
            {
                KeyCode.Backspace, () => // delete node(s)
                {
                    if (!_rightShift) // right shift is required as a safety precaution.
                        return;

                    if (_rightCtrl) // right control will delete all nodes
                        while (vessel.patchedConicSolver.maneuverNodes.Any())
                        {
                            vessel.patchedConicSolver.maneuverNodes.Last().RemoveSelf();
                            vessel.patchedConicSolver.UpdateFlightPlan();
                        }
                    else
                        vessel.patchedConicSolver.maneuverNodes.Last().RemoveSelf();
                }
            }
        };

        #endregion

        #region --- [ plugin methods ] ----------------------------------------------------------------------------------------

        /// <summary>
        ///     Awakes this instance.
        /// </summary>
        public void Awake()
        {
            // this is a dirty kludge, I know, and it only works with the default keybindings.
            // does anyone know how to intercept the keys while the map is open, and block them from their normal tasks?
            GameSettings.ZOOM_IN = new KeyBinding(); // keypad plus
            GameSettings.ZOOM_OUT = new KeyBinding(); // keypad minus
            GameSettings.SCROLL_VIEW_UP = new KeyBinding(); // page up
            GameSettings.SCROLL_VIEW_DOWN = new KeyBinding(); // page down
            GameSettings.CAMERA_ORBIT_LEFT = new KeyBinding(); // left arrow
            GameSettings.CAMERA_ORBIT_RIGHT = new KeyBinding(); // right arrow
            GameSettings.CAMERA_ORBIT_UP = new KeyBinding(); // up arrow
            GameSettings.CAMERA_ORBIT_DOWN = new KeyBinding(); // down arrow
            GameSettings.NAVBALL_TOGGLE = new KeyBinding(); // toggling navball exits mapview ...why?
            GameSettings.SCROLL_ICONS_UP = new KeyBinding(); // home
            GameSettings.SCROLL_ICONS_DOWN = new KeyBinding(); // end
            GameSettings.UIMODE_STAGING = new KeyBinding(); // insert - who even uses docking mode, anyway?
            GameSettings.UIMODE_DOCKING = new KeyBinding(); // delete - also for docking mode
            GameSettings.ApplySettings();
            GameSettings.SaveSettings();

            // these kludges handle things when NumLock is disabled
            ModifierKeys.Add(KeyCode.End, ModifierKeys[KeyCode.Keypad1]);
            ModifierKeys.Add(KeyCode.DownArrow, ModifierKeys[KeyCode.Keypad2]);
            ModifierKeys.Add(KeyCode.PageDown, ModifierKeys[KeyCode.Keypad3]);
            ModifierKeys.Add(KeyCode.LeftArrow, ModifierKeys[KeyCode.Keypad4]);
            // Keypad5 is reserved
            ModifierKeys.Add(KeyCode.RightArrow, ModifierKeys[KeyCode.Keypad6]);
            ModifierKeys.Add(KeyCode.Home, ModifierKeys[KeyCode.Keypad7]);
            ModifierKeys.Add(KeyCode.UpArrow, ModifierKeys[KeyCode.Keypad8]);
            ModifierKeys.Add(KeyCode.PageUp, ModifierKeys[KeyCode.Keypad9]);

            ManeuverKeys.Add(KeyCode.Insert, ManeuverKeys[KeyCode.Keypad0]);
            ManeuverKeys.Add(KeyCode.Delete, ManeuverKeys[KeyCode.KeypadPeriod]);
            // keypad 5 doesn't work without numlock

            _mechJeb2 = AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.name.Equals("MechJeb2"));
        }

        /// <summary>
        ///     Updates this instance.
        /// </summary>
        public void Update()
        {
            if (!FlightGlobals.ready
                || !MapView.MapIsEnabled
                || !PatchedConicsUnlocked)
                return;

            if (_mechJeb2 != null
                && _xferCalc != null)
                CheckPorkchop();

            #region BugFix: v1.1 Cancel MechJeb node execution while time warping

            if (_mechJeb2 != null
                && Input.GetKeyUp(KeyCode.KeypadEnter))
                MechJebOperationExecuteNode();

            #endregion

            if (Time.timeScale != 1f)
                return;

            // SOI Warp
            if (Input.GetKeyUp(KeyCode.KeypadMultiply))
                if (orbit.patchEndTransition == Orbit.PatchTransitionType.ENCOUNTER
                    || orbit.patchEndTransition == Orbit.PatchTransitionType.ESCAPE)
                {
                    Msg("Warping to SOI change");
                    TimeWarp.fetch.WarpTo(orbit.EndUT);
                    return; // do not operate while in time warp
                }
                else
                {
                    Msg("Warp To SOI transition is not currently possible.");
                }

            _rightAlt = Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.RightCommand);
            _rightCtrl = Input.GetKey(KeyCode.RightControl);
            _rightShift = Input.GetKey(KeyCode.RightShift);

            // combine the modifier keys into a binary-coded integer
            var i = (_rightCtrl ? 1 : 0) |
                    (_rightShift ? 2 : 0) |
                    (_rightAlt ? 4 : 0);

            // assign the delta based on the modifier keys. Lookup-tables are much nicer than giant blocks of if conditions.
            _delta = ModKeyMap[i];

            // call maneuver methods as needed, if MechJeb is installed.
            if (_mechJeb2 != null)
                foreach (var maneuverKey in ManeuverKeys)
                    if (Input.GetKeyUp(maneuverKey.Key))
                        maneuverKey.Value(); // the maneuver method is in the value

            // make sure a node exists
            if (!vessel.patchedConicSolver.maneuverNodes.Any())
                return;

            foreach (var modifierKey in ModifierKeys)
            {
                if (!Input.GetKeyUp(modifierKey.Key)) continue;
                modifierKey.Value(); // the modifier method is in the value
                UpdateNode(currentNode);
            }
        }

        private static void UpdateNode(ManeuverNode node)
        {
            vessel.patchedConicSolver.UpdateFlightPlan();
            if (node.attachedGizmo == null)
                return;

            node.attachedGizmo.DeltaV = node.DeltaV;
            node.attachedGizmo.UT = node.UT;
            node.attachedGizmo.patchBefore = node.patch;
            node.attachedGizmo.patchAhead = node.nextPatch;
            node.attachedGizmo.OnGizmoUpdated?.Invoke(node.DeltaV, node.UT);
        }

        #endregion

        #region --- [ MechJeb support ] ---------------------------------------------------------------------------------------

        private static AssemblyLoader.LoadedAssembly _mechJeb2; // this is the mechjeb assembly, if installed.


        /// <summary>
        ///     The maneuver keys
        /// </summary>
        private static readonly Dictionary<KeyCode, Callback> ManeuverKeys = new Dictionary<KeyCode, Callback>
        {
            {KeyCode.Keypad0, MechJebOperationCircularize},
            {KeyCode.KeypadPeriod, MechJebOperationMatchVelocitiesWithTarget},
            {KeyCode.Keypad5, MechJebOperationInterceptTarget},
            {KeyCode.KeypadDivide, MechJebOperationTransfer}
        };

        /// <summary>
        ///     Places a maneuver node.
        /// </summary>
        /// <param name="dv">The dv.</param>
        /// <param name="ut">The ut.</param>
        /// <param name="v">The v.</param>
        /// <param name="o">The o.</param>
        private static void PlaceManeuverNode(Vector3d dv, double ut, Vessel v = null, Orbit o = null)
        {
            if (v == null) v = vessel;
            if (o == null) o = orbit;

            for (var i = 0; i < 3; i++)
                if (double.IsNaN(dv[i])
                    || double.IsInfinity(dv[i]))
                    throw new ArgumentOutOfRangeException($"{ID} bad dv node");

            if (double.IsNaN(ut)
                || double.IsInfinity(ut))
                throw new ArgumentOutOfRangeException($"{ID} bad ut node");

            ut = Math.Max(ut, UT);
            var node = vessel.patchedConicSolver.AddManeuverNode(ut);
            node.DeltaV = (Vector3d) MuMech("OrbitExtensions.DeltaVToManeuverNodeCoordinates", o, ut, dv);
            UpdateNode(node);
        }

        // convienenice method
        private static object MuMech(string methodPath, params object[] args) => MuMech(methodPath, ref args);

        /// <summary>
        ///     Invokes static methods, automatic type detection and reference type support (for out/ref parameters)
        /// </summary>
        /// <param name="methodPath">The method path.</param>
        /// <param name="args">The arguments.</param>
        /// <param name="refTypeIndicies">The reference type indicies.</param>
        /// <returns></returns>
        /// <exception cref="NullReferenceException">
        /// </exception>
        private static object MuMech(string methodPath, ref object[] args, int[] refTypeIndicies = null)
        {
            //Debug.Log($"[KeyNode] ARGS: {args.Length}");
            var types = args.Length == 0
                            ? Type.EmptyTypes
                            : args.Select(o => o.GetType()).ToArray();

            if (refTypeIndicies != null)
                foreach (var t in refTypeIndicies)
                    types[t] = types[t].MakeByRefType();

            var i = methodPath.LastIndexOf(".", StringComparison.OrdinalIgnoreCase);
            var classPath = "MuMech." + methodPath.Substring(0, i);
            var methodName = methodPath.Substring(i + 1);
            Debug.Log($"{ID} {classPath}.{methodName}({string.Join(", ", types.Select(t => t.Name).ToArray())})");

            var c = _mechJeb2.assembly.GetType(classPath);
            if (c == null)
                throw new NullReferenceException($"{ID} classPath is invalid"); // check your spelling and/or arg types?

            var m = c.GetMethod(methodName, types);
            if (m == null)
                throw new NullReferenceException($"{ID} methodName is invalid"); // check your spelling and/or arg types?

            return m.Invoke(null, args);
        }


        /// <summary>
        ///     Node execution
        /// </summary>
        /// <param name="allNodes">if set to <c>true</c> [all nodes].</param>
        /// <returns></returns>
        private static void MechJebOperationExecuteNode()
        {
            if (!vessel.patchedConicSolver.maneuverNodes.Any())
                throw new Dwarf("Need at least 1 maneuver node to execute.");

            var mjCore = MuMech("VesselExtensions.GetMasterMechJeb", vessel);

            // get the node executor module from mechjeb
            var execModule = mjCore.GetType().GetMethod("GetComputerModule", new[] {typeof(string)})
                .Invoke(mjCore, new object[] {"MechJebModuleNodeExecutor"});

            var type = execModule.GetType();

            if ((bool) type.GetProperty("enabled").GetValue(execModule, null))
            {
                type.GetMethod("Abort").Invoke(execModule, new object[] { });
                Msg("Node execution aborted!");
                return;
            }

            if (_rightShift) // right shift forces all nodes to execute
            {
                type.GetMethod("ExecuteAllNodes").Invoke(execModule, new[] {execModule});
                Msg("Executing ALL nodes");
                return;
            }

            // execute one node by default.
            type.GetMethod("ExecuteOneNode").Invoke(execModule, new[] {execModule});
            Msg("Executing current node");
        }

        /// <summary>
        ///     Match velocities with target
        /// </summary>
        /// <returns></returns>
        private static void MechJebOperationMatchVelocitiesWithTarget()
        {
            // Match Planes instead
            if (_rightShift)
            {
                MechJebOperationMatchPlanesWithTarget();
                return;
            }

            if (!NormalTargetExists)
                throw new Dwarf("Target required to match velocities with.");

            if (!InSameSOI)
                throw new Dwarf("Target must be in the same SOI.");

            var ut = (double) MuMech("OrbitExtensions.NextClosestApproachTime", orbit, targetOrbit, UT);
            var dv = (Vector3d) MuMech("OrbitalManeuverCalculator.DeltaVToMatchVelocities", orbit, ut, targetOrbit);

            PlaceManeuverNode(dv, ut);
        }


        private static void MechJebOperationMatchPlanesWithTarget()
        {
            if (!NormalTargetExists)
                throw new Dwarf("Target required to match planes with.");

            if (!InSameSOI)
                throw new Dwarf("Target to match planes with must be within the same SOI.");

            // compute nearest AN/DN node

            double anTime = double.MaxValue, dnTime = double.MaxValue;
            Vector3d anDv = Vector3d.zero, dnDv = Vector3d.zero;
            var args = new object[] {orbit, targetOrbit, UT, 0d};
            var refs = new[] {3};

            var anExists = (bool) MuMech("OrbitExtensions.AscendingNodeEquatorialExists", orbit);
            if (anExists)
            {
                anDv = (Vector3d) MuMech("OrbitalManeuverCalculator.DeltaVAndTimeToMatchPlanesAscending", ref args, refs);
                anTime = (double) args[3];
            }

            var dnExists = (bool) MuMech("OrbitExtensions.DescendingNodeEquatorialExists", orbit);
            if (dnExists)
            {
                dnDv = (Vector3d) MuMech("OrbitalManeuverCalculator.DeltaVAndTimeToMatchPlanesDescending", ref args, refs);
                dnTime = (double) args[3];
            }

            if (!anExists
                && !dnExists)
                throw new Dwarf("Cannot match planes with target; AN/DN doesn't exist.");

            var ut = anTime < dnTime ? anTime : dnTime;
            var dv = anTime < dnTime ? anDv : dnDv;
            PlaceManeuverNode(dv, ut);
        }

        /// <summary>
        ///     Circularize
        /// </summary>
        /// <returns></returns>
        /// <exception cref="NullReferenceException">dv is null</exception>
        private static void MechJebOperationCircularize()
        {
            if (orbit.eccentricity < 0.2)
                throw new Dwarf("Current orbit is already circular.");

            var ut = _rightShift || orbit.eccentricity >= 1 // hyperbolic orbits force periapsis burns
                         ? (double) MuMech("OrbitExtensions.NextPeriapsisTime", orbit, UT)
                         : (double) MuMech("OrbitExtensions.NextApoapsisTime", orbit, UT);

            var dv = (Vector3d) MuMech("OrbitalManeuverCalculator.DeltaVToCircularize", orbit, ut);
            if (dv == null)
                throw new NullReferenceException("dv is null");

            PlaceManeuverNode(dv, ut);
        }

        /// <summary>
        ///     Transfer to target. TODO: Porkchop Transfer
        /// </summary>
        /// <returns></returns>
        private static void MechJebOperationTransfer()
        {
            if (_xferCalc != null)
                return; // don't interrupt previous calculations

            if (_rightShift)
            {
                MechJebOperationReturnFromMoon();
                return; // return from moon instead
            }

            if (orbit.referenceBody == targetOrbit.referenceBody
                || orbit.referenceBody == Planetarium.fetch.Sun)
                MechJebOperationHohmannTransfer(); // Use a normal Hohmann Transfer instead.

            var body = orbit.referenceBody.displayName;

            if (orbit.eccentricity >= 0.2)
                throw new Dwarf("Starting orbit for interplanetary transfer must not be hyperbolic. "
                                + "Circularize first.");

            if (orbit.ApR >= orbit.referenceBody.sphereOfInfluence)
                throw new Dwarf($"Starting orbit for interplanetary transfer must not "
                                + $"escape {body}'s SOI.");

            if (!NormalTargetExists)
                throw new Dwarf("Target required for interplanetary transfer.");

            if (orbit.referenceBody.referenceBody == null)
                throw new Dwarf($"An interplanetary transfer from within {body}'s SOI "
                                + $"must target a body that orbits {body}'s parent, "
                                + $"{orbit.referenceBody.referenceBody.displayName}.");

            if (target is CelestialBody
                && orbit.referenceBody == targetOrbit.referenceBody)
                throw new Dwarf($"Your vessel is already orbiting {body}");


            // all checks passed, compute transfer
            var synodicPeriod = (double) MuMech("OrbitExtensions.SynodicPeriod", orbit, targetOrbit);
            var hohmannTransferTime = OrbitUtil.GetTransferTime(orbit.referenceBody.orbit, targetOrbit);

            if (double.IsInfinity(synodicPeriod))
                synodicPeriod = orbit.referenceBody.orbit.period; // both orbits have the same period

            var minDepartureTime = UT;
            var minTransferTime = 3600d;
            var maxTransferTime = hohmannTransferTime * 1.5d;
            var maxArrivalTime = (synodicPeriod + hohmannTransferTime) * 1.5d;
            const double minSamplingStep = 12d * 3600d;

            var type = _mechJeb2.assembly.GetType("MuMech.TransferCalculator");
            if (type == null) throw new NullReferenceException("type null");

            _xferCalc = Activator.CreateInstance(type, new object[]
            {
                orbit, targetOrbit,
                minDepartureTime,
                minDepartureTime + maxTransferTime,
                3600,
                maxTransferTime,
                200, 200, false
            }, CultureInfo.InvariantCulture);
        }

        private static object _xferCalc;

        private static void CheckPorkchop()
        {
            var type = _xferCalc.GetType();
            var progress = (int) type.GetProperty("Progress").GetValue(_xferCalc, null);
            ScreenMessages.PostScreenMessage($"{ID} Calculating Transfer {progress}%",
                                             0.1f, ScreenMessageStyle.UPPER_LEFT);

            var finished = (bool) type.GetProperty("Finished").GetValue(_xferCalc, null);
            if (!finished)
                return;

            var failed = (double) type.GetField("arrivalDate").GetValue(_xferCalc) < 0;
            if (failed)
            {
                Msg("Transfer Computation failed!");
                _xferCalc = null;
                return;
            }

            var i = (int) type.GetField("bestDate").GetValue(_xferCalc);
            var bestDate = (double) type.GetMethod("DateFromIndex")
                .Invoke(_xferCalc, new object[] {i});

            i = (int) type.GetField("bestDuration").GetValue(_xferCalc);
            var bestDuration = (double) type.GetMethod("DurationFromIndex")
                .Invoke(_xferCalc, new object[] {i});

            var mp = type.GetMethod("OptimizeEjection").Invoke(_xferCalc, new object[]
            {
                bestDate,
                orbit, targetOrbit, target as CelestialBody,
                bestDate + bestDuration,
                UT
            });

            type = mp.GetType();
            var dv = (Vector3d) type.GetField("dV").GetValue(mp);
            var ut = (double) type.GetField("UT").GetValue(mp);

            PlaceManeuverNode(dv, ut);
            _xferCalc = null;
        }

        /// <summary>
        ///     Return from Moon
        /// </summary>
        /// <returns></returns>
        private static void MechJebOperationReturnFromMoon()
        {
            if (orbit.eccentricity > 0.2)
                throw new Dwarf("Starting orbit for Moon Return is too hyperbolic. Circularize first.");

            var body = orbit.referenceBody.referenceBody;
            if (body == null)
                throw new Dwarf($"{orbit.referenceBody.displayName} is not orbiting another body you could return to.");

            const double lowOrbit = 20000d;
            var alt = body.Radius + body.atmosphereDepth + lowOrbit;
            var args = new object[] {orbit, UT, alt, 0d};
            var dv = (Vector3d) MuMech("OrbitalManeuverCalculator.DeltaVAndTimeForMoonReturnEjection", ref args, new[] {3});
            var ut = (double) args[3];

            PlaceManeuverNode(dv, ut);
        }

        /// <summary>
        ///     Hohmann Transfer
        /// </summary>
        /// <returns></returns>
        private static void MechJebOperationHohmannTransfer()
        {
            if (!NormalTargetExists)
                throw new Dwarf("Target required for Hohmann Transfer.");

            if (!InSameSOI)
                throw new Dwarf("Target for Hohmann Transfer must be in the same SOI.");

            if (orbit.eccentricity > 0.2)
                throw new Dwarf("Starting orbit for Hohmann Transfer is too hyperbolic. Circularize first.");

            if (targetOrbit.eccentricity > 1)
                throw new Dwarf("Target's orbit for Hohmann Transfer can not be hyperbolic.");

            var relInc = (double) MuMech("OrbitExtensions.RelativeInclination", orbit, targetOrbit);

            if (relInc > 30
                && relInc < 150)
                ScreenMessages.PostScreenMessage($"{ID} WARNING: Planned Hohmann Transfer might not intercept the target!");

            var args = new object[] {orbit, targetOrbit, UT, 0d};
            var dv = (Vector3d) MuMech("OrbitalManeuverCalculator.DeltaVAndTimeForHohmannTransfer", ref args, new[] {3});
            PlaceManeuverNode(dv, (double) args[3]);
        }

        /// <summary>
        ///     Intercept Course Correction
        /// </summary>
        /// <returns></returns>
        private static void MechJebOperationInterceptTarget()
        {
            if (!NormalTargetExists)
                throw new Dwarf("Target required for Intercept Course Correction.");

            var ut = UT;
            var o = orbit;
            var correctionPatch = orbit;
            while (correctionPatch != null)
            {
                if (correctionPatch.referenceBody == targetOrbit.referenceBody)
                {
                    o = correctionPatch;
                    ut = o.StartUT;
                    break;
                }

                correctionPatch = (Orbit) MuMech("VesselExtensions.GetNextPatch", vessel, correctionPatch);
            }

            if (correctionPatch == null
                || correctionPatch.referenceBody != targetOrbit.referenceBody)
                throw new Dwarf("Target for Intercept Course Correction must be in the same SOI.");

            var aTime = (double) MuMech("OrbitExtensions.NextClosestApproachTime", o, targetOrbit, ut);
            var aDist = (double) MuMech("OrbitExtensions.NextClosestApproachDistance", o, targetOrbit, ut);

            if (aTime < ut + 1
                || aDist > targetOrbit.semiMajorAxis * 0.2)
                throw new Dwarf("Intercept Course Correction is currently not possible!");

            const double PeA = 200000,
                         iDist = 200d;

            Vector3d dv;
            var targetBody = target as CelestialBody;
            const string M = "OrbitalManeuverCalculator.DeltaVAndTimeForCheapestCourseCorrection";

            if (targetBody == null)
            {
                var args = new object[] {o, ut, targetOrbit, iDist, ut};
                dv = (Vector3d) MuMech(M, ref args, new[] {4});
                ut = (double) args[4];
            }
            else
            {
                var args = new object[] {o, ut, targetOrbit, targetBody, targetBody.Radius + PeA, ut};
                dv = (Vector3d) MuMech(M, ref args, new[] {5});
                ut = (double) args[5];
            }

            PlaceManeuverNode(dv, ut);
        }

        #endregion

        #region --- [ convienece properties for simplicity's sake ] -----------------------------------------------------------

        private static void Msg(string msg) => ScreenMessages.PostScreenMessage($"{ID} {msg}");
        private static double UT => Planetarium.GetUniversalTime();
        private static Vessel vessel => FlightGlobals.ActiveVessel;
        private static Orbit orbit => vessel.orbit;
        private static ITargetable target => vessel.targetObject;
        private static Orbit targetOrbit => target.GetOrbit();
        private static bool PatchedConicsUnlocked => vessel.patchedConicSolver != null;
        private static ManeuverNode currentNode => vessel.patchedConicSolver.maneuverNodes.First();

        private static bool CanAlignTarget => target != null
                                              && target.GetTargetingMode() == VesselTargetModes.DirectionVelocityAndOrientation;

        private static bool NormalTargetExists => target != null
                                                  && (target is Vessel
                                                      || target is CelestialBody
                                                      || CanAlignTarget);

        private static bool InSameSOI => orbit.referenceBody == targetOrbit.referenceBody;

        #endregion
    }
}
