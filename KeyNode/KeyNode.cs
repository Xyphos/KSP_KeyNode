// 
//  MIT License
// 
//  Copyright (c) 2017-2019 William "Xyphos" Scott (TheGreatXyphos@gmail.com)
// 
//  Permission is hereby granted, free of charge, to any person obtaining a copy
//  of this software and associated documentation files (the "Software"), to deal
//  in the Software without restriction, including without limitation the rights
//  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//  copies of the Software, and to permit persons to whom the Software is
//  furnished to do so, subject to the following conditions:
// 
//  The above copyright notice and this permission notice shall be included in all
//   copies or substantial portions of the Software.
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
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace KeyNode
{
  [KSPAddon(startup: KSPAddon.Startup.Flight, once: false)]
  public class KeyNode : MonoBehaviour
  {
    #region --- [ fields ] ------------------------------------------------------------------------------------------------

    // ReSharper disable once InconsistentNaming
    private const string ID = "[KeyNode]";

    private static double _delta;
    private static bool   _rightAlt, _rightCtrl, _rightShift;

    // lookup table for delta modifiers
    private static readonly double[] ModKeyMap =
    {
      1.0,    // 0 no mod keys held
      10.0,   // 1 rctrl
      100.0,  // 2 rshift
      1000.0, // 3 rshift + rctrl
      //---
      0.1,   // 4 only ralt held
      0.01,  // 5 ralt + rctrl
      0.001, // 6 ralt + rshift
      0.0001 // 7 all mod keys held
    };

    private static readonly Dictionary<KeyCode, Callback> ModifierKeys = new Dictionary<KeyCode, Callback>
    {
      {KeyCode.Keypad1, () => currentNode.DeltaV.z -= _delta}, // subtract prograde
      {KeyCode.End, () => currentNode.DeltaV.z -= _delta},     // alternate subtract prograde

      {KeyCode.Keypad2, () => currentNode.DeltaV.y -= _delta},   // subtract normal
      {KeyCode.DownArrow, () => currentNode.DeltaV.y -= _delta}, // alternate subtract normal

      {KeyCode.Keypad3, () => currentNode.UT -= _delta},  // subtract time
      {KeyCode.PageDown, () => currentNode.UT -= _delta}, // alternate subtract time

      {KeyCode.Keypad4, () => currentNode.DeltaV.x -= _delta},   // subtract radial
      {KeyCode.LeftArrow, () => currentNode.DeltaV.x -= _delta}, // alternate subtract radial

      // Keypad5 is reserved for MechJeb support

      {KeyCode.Keypad6, () => currentNode.DeltaV.x += _delta},    // add raidal
      {KeyCode.RightArrow, () => currentNode.DeltaV.x += _delta}, // alternate add radial

      {KeyCode.Keypad7, () => currentNode.DeltaV.z += _delta}, // add prograde
      {KeyCode.Home, () => currentNode.DeltaV.z += _delta},    // alternate add prograde

      {KeyCode.Keypad8, () => currentNode.DeltaV.y += _delta}, // add normal
      {KeyCode.UpArrow, () => currentNode.DeltaV.y += _delta}, // alternate add normal

      {KeyCode.Keypad9, () => currentNode.UT += _delta}, // add time
      {KeyCode.PageUp, () => currentNode.UT += _delta},  // alternate add time

      {KeyCode.KeypadPlus, () => currentNode.UT += orbit.period}, // add an orbit
      {
        KeyCode.KeypadMinus, () => // subtract an orbit, if able. (don't go into the past)
        {
          var ut                      = currentNode.UT - orbit.period;
          if (ut > UT) currentNode.UT = ut;
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
    ///   Awakes this instance.
    /// </summary>
    public void Awake() => _mechJeb2 = AssemblyLoader.loadedAssemblies.FirstOrDefault(predicate: a => a.name.Equals(value: "MechJeb2"));

    #region v1.2 Keyboad kludge konverted to a save/restore scheme

    private static          bool                           _savedBindings;
    private static readonly Dictionary<string, KeyBinding> KeyBindings = new Dictionary<string, KeyBinding>();

    // temporary clear the keybindings when the mapview is opened
    private static void SaveBindings()
    {
      KeyBindings.Clear();

      var t = typeof(GameSettings);
      var b = t.GetFields().Where(predicate: fi => fi.IsStatic && fi.IsPublic && fi.FieldType == typeof(KeyBinding));

      foreach (var fi in b)
      {
        var k  = (KeyBinding) fi.GetValue(obj: null);
        var k1 = k.primary.code;
        var k2 = k.secondary.code;

        if (!ManeuverKeys.ContainsKey(key: k1)
         && !ModifierKeys.ContainsKey(key: k1)
         && !ManeuverKeys.ContainsKey(key: k2)
         && !ModifierKeys.ContainsKey(key: k2))
          continue;

        KeyBindings.Add(key: fi.Name, value: k);
        fi.SetValue(obj: null, value: new KeyBinding()); // blank keybinding
      }

      GameSettings.ApplySettings();
      _savedBindings = true;
    }

    // restore keybindings when mapview is closed
    private static void RestoreBindings()
    {
      if (!_savedBindings) return;

      var t = typeof(GameSettings);

      foreach (var b in KeyBindings) t.GetField(name: b.Key).SetValue(obj: null, value: b.Value);

      GameSettings.ApplySettings();
      _savedBindings = false;
    }

    #endregion

    /// <summary>
    ///   Updates this instance.
    /// </summary>
    public void Update()
    {
      #region BugFix: v1.2 Transfer calc needs to update while the map view is closed

      if (!FlightGlobals.ready
       || !PatchedConicsUnlocked)
        return;

      if (_mechJeb2 != null
       && _advXfer  != null)
        CheckPorkchop();

      #endregion

      #region BugFix: v1.2 save or restore keybindings when the map view is opened or closed

      if (!MapView.MapIsEnabled)
      {
        if (_savedBindings) RestoreBindings();

        return;
      }

      if (!_savedBindings) SaveBindings();

      #endregion


      #region BugFix: v1.1 Cancel MechJeb node execution while time warping

      if (_mechJeb2 != null
       && Input.GetKeyUp(key: KeyCode.KeypadEnter))
        MechJebOperationExecuteNode();

      #endregion

      if (Math.Abs(value: Time.timeScale - 1f) > 0.1f) return;

      // SOI Warp
      if (Input.GetKeyUp(key: KeyCode.KeypadMultiply))
        if (orbit.patchEndTransition == Orbit.PatchTransitionType.ENCOUNTER
         || orbit.patchEndTransition == Orbit.PatchTransitionType.ESCAPE)
        {
          Msg(msg: "Warping to SOI change");
          TimeWarp.fetch.WarpTo(UT: orbit.EndUT);
          return; // do not operate while in time warp
        }
        else
        {
          Msg(msg: "Warp To SOI transition is not currently possible.");
        }

      _rightAlt   = Input.GetKey(key: KeyCode.RightAlt) || Input.GetKey(key: KeyCode.RightCommand);
      _rightCtrl  = Input.GetKey(key: KeyCode.RightControl);
      _rightShift = Input.GetKey(key: KeyCode.RightShift);

      // combine the modifier keys into a binary-coded integer
      var i = (_rightCtrl ? 1 : 0) | (_rightShift ? 2 : 0) | (_rightAlt ? 4 : 0);

      // assign the delta based on the modifier keys. Lookup-tables are much nicer than giant blocks of if conditions.
      _delta = ModKeyMap[i];

      // call maneuver methods as needed, if MechJeb is installed.
      if (_mechJeb2 != null)
        foreach (var maneuverKey in ManeuverKeys)
          if (Input.GetKeyUp(key: maneuverKey.Key))
            maneuverKey.Value(); // the maneuver method is in the value

      // make sure a node exists
      if (!vessel.patchedConicSolver.maneuverNodes.Any()) return;

      foreach (var modifierKey in ModifierKeys)
      {
        if (!Input.GetKeyUp(key: modifierKey.Key)) continue;
        modifierKey.Value(); // the modifier method is in the value
        UpdateNode(node: currentNode);
      }
    }

    private static void UpdateNode(ManeuverNode node)
    {
      vessel.patchedConicSolver.UpdateFlightPlan();
      if (node.attachedGizmo == null) return;

      node.attachedGizmo.DeltaV      = node.DeltaV;
      node.attachedGizmo.UT          = node.UT;
      node.attachedGizmo.patchBefore = node.patch;
      node.attachedGizmo.patchAhead  = node.nextPatch;
      node.attachedGizmo.OnGizmoUpdated?.Invoke(dV: node.DeltaV, UT: node.UT);
    }

    #endregion

    #region --- [ MechJeb support ] ---------------------------------------------------------------------------------------

    private static AssemblyLoader.LoadedAssembly _mechJeb2; // this is the mechjeb assembly, if installed.


    /// <summary>
    ///   The maneuver keys
    /// </summary>
    private static readonly Dictionary<KeyCode, Callback> ManeuverKeys = new Dictionary<KeyCode, Callback>
    {
      {KeyCode.Keypad0, MechJebOperationCircularize},
      {KeyCode.Insert, MechJebOperationCircularize}, // alternate circularize

      {KeyCode.KeypadPeriod, MechJebOperationMatchVelocitiesWithTarget},
      {KeyCode.Delete, MechJebOperationMatchVelocitiesWithTarget}, // alternate match

      {KeyCode.Keypad5, MechJebOperationInterceptTarget},
      {KeyCode.KeypadDivide, MechJebOperationTransfer}
    };

    /// <summary>
    ///   Places a maneuver node.
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
        if (double.IsNaN(d: dv[index: i])
         || double.IsInfinity(d: dv[index: i]))
          throw new ArgumentOutOfRangeException(paramName: $"{ID} bad dv node");

      if (double.IsNaN(d: ut)
       || double.IsInfinity(d: ut))
        throw new ArgumentOutOfRangeException(paramName: $"{ID} bad ut node");

      ut = Math.Max(val1: ut, val2: UT);
      var node = v.patchedConicSolver.AddManeuverNode(UT: ut);
      node.DeltaV = (Vector3d) MuMech(methodPath: "OrbitExtensions.DeltaVToManeuverNodeCoordinates", o, ut, dv);
      UpdateNode(node: node);
    }

    // convienenice method
    private static object MuMech(string methodPath, params object[] args) => MuMech(methodPath: methodPath, args: ref args);

    /// <summary>
    ///   Invokes static methods, automatic type detection and reference type support (for out/ref parameters)
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
      var types = args.Length == 0 ? Type.EmptyTypes : args.Select(selector: o => o.GetType()).ToArray();

      if (refTypeIndicies != null)
        foreach (var t in refTypeIndicies)
          types[t] = types[t].MakeByRefType();

      var i          = methodPath.LastIndexOf(value: ".", comparisonType: StringComparison.OrdinalIgnoreCase);
      var classPath  = "MuMech." + methodPath.Substring(startIndex: 0, length: i);
      var methodName = methodPath.Substring(startIndex: i + 1);
      Debug.Log(message: $"{ID} {classPath}.{methodName}({string.Join(separator: ", ", value: types.Select(selector: t => t.Name).ToArray())})");

      var c = _mechJeb2.assembly.GetType(name: classPath);
      if (c == null) throw new NullReferenceException(message: $"{ID} classPath is invalid"); // check your spelling and/or arg types?

      var m = c.GetMethod(name: methodName, types: types);
      if (m == null) throw new NullReferenceException(message: $"{ID} methodName is invalid"); // check your spelling and/or arg types?

      return m.Invoke(obj: null, parameters: args);
    }


    /// <summary>
    ///   Node execution
    /// </summary>
    /// <returns></returns>
    private static void MechJebOperationExecuteNode()
    {
      if (!vessel.patchedConicSolver.maneuverNodes.Any())
      {
        Msg(msg: "Need at least 1 maneuver node to execute.");
        return;
      }


      var mjCore = MuMech(methodPath: "VesselExtensions.GetMasterMechJeb", vessel);

      // get the node executor module from mechjeb
      var execModule = mjCore.GetType()
                             .GetMethod(name: "GetComputerModule", types: new[] {typeof(string)})
                            ?.Invoke(obj: mjCore, parameters: new object[] {"MechJebModuleNodeExecutor"});

      var type = execModule?.GetType();

      // ReSharper disable once PossibleNullReferenceException
      if ((bool) type?.GetProperty(name: "enabled")?.GetValue(obj: execModule, index: null))
      {
        type.GetMethod(name: "Abort")?.Invoke(obj: execModule, parameters: new object[] { });
        Msg(msg: "Node execution aborted!");
        return;
      }

      if (_rightShift) // right shift forces all nodes to execute
      {
        type.GetMethod(name: "ExecuteAllNodes")?.Invoke(obj: execModule, parameters: new[] {execModule});
        Msg(msg: "Executing ALL nodes");
        return;
      }

      // execute one node by default.
      type.GetMethod(name: "ExecuteOneNode")?.Invoke(obj: execModule, parameters: new[] {execModule});
      Msg(msg: "Executing current node");
    }

    /// <summary>
    ///   Match velocities with target
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
      {
        Msg(msg: "Target required to match velocities with.");
        return;
      }

      if (!InSameSOI)
      {
        Msg(msg: "Target must be in the same SOI.");
        return;
      }


      var ut = (double) MuMech(methodPath: "OrbitExtensions.NextClosestApproachTime", orbit, targetOrbit, UT);
      var dv = (Vector3d) MuMech(methodPath: "OrbitalManeuverCalculator.DeltaVToMatchVelocities", orbit, ut, targetOrbit);

      PlaceManeuverNode(dv: dv, ut: ut);
    }


    private static void MechJebOperationMatchPlanesWithTarget()
    {
      if (!NormalTargetExists)
      {
        Msg(msg: "Target required to match planes with.");
        return;
      }


      if (!InSameSOI)
      {
        Msg(msg: "Target to match planes with must be within the same SOI.");
        return;
      }


      // compute nearest AN/DN node

      double   anTime = double.MaxValue, dnTime = double.MaxValue;
      Vector3d anDv   = Vector3d.zero,   dnDv   = Vector3d.zero;
      var      args   = new object[] {orbit, targetOrbit, UT, 0d};
      var      refs   = new[] {3};

      var anExists = (bool) MuMech(methodPath: "OrbitExtensions.AscendingNodeEquatorialExists", orbit);
      if (anExists)
      {
        anDv   = (Vector3d) MuMech(methodPath: "OrbitalManeuverCalculator.DeltaVAndTimeToMatchPlanesAscending", args: ref args, refTypeIndicies: refs);
        anTime = (double) args[3];
      }

      var dnExists = (bool) MuMech(methodPath: "OrbitExtensions.DescendingNodeEquatorialExists", orbit);
      if (dnExists)
      {
        dnDv   = (Vector3d) MuMech(methodPath: "OrbitalManeuverCalculator.DeltaVAndTimeToMatchPlanesDescending", args: ref args, refTypeIndicies: refs);
        dnTime = (double) args[3];
      }

      if (!anExists
       && !dnExists)
      {
        Msg(msg: "Cannot match planes with target; AN/DN doesn't exist.");
        return;
      }


      var ut = anTime < dnTime ? anTime : dnTime;

      var dv = anTime < dnTime ? anDv : dnDv;

      PlaceManeuverNode(dv: dv, ut: ut);
    }

    /// <summary>
    ///   Circularize
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NullReferenceException">dv is null</exception>
    private static void MechJebOperationCircularize()
    {
      if (orbit.eccentricity < 0.02)
      {
        Debug.Log(message: $"[KeyNode] Circularize ecc = {orbit.eccentricity:N3}");
        Msg(msg: "Current orbit is already circular.");
        return;
      }


      var ut = _rightShift || orbit.eccentricity >= 1 // hyperbolic orbits force periapsis burns
                 ? (double) MuMech(methodPath: "OrbitExtensions.NextPeriapsisTime", orbit, UT)
                 : (double) MuMech(methodPath: "OrbitExtensions.NextApoapsisTime", orbit, UT);

      var dv = (Vector3d) MuMech(methodPath: "OrbitalManeuverCalculator.DeltaVToCircularize", orbit, ut);
      if (dv == null) throw new NullReferenceException(message: "dv is null");

      PlaceManeuverNode(dv: dv, ut: ut);
    }

    /// <summary>
    ///   Transfer to target.
    /// </summary>
    /// <returns></returns>
    private static void MechJebOperationTransfer()
    {
      try
      {
        if (_advXfer != null) return; // don't interrupt previous calculations

        if (_rightShift)
        {
          MechJebOperationReturnFromMoon();
          return; // return from moon instead
        }

        if (orbit.referenceBody == targetOrbit.referenceBody
         || orbit.referenceBody == Planetarium.fetch.Sun)
        {
          MechJebOperationHohmannTransfer(); // Use a normal Hohmann Transfer instead.
          return;
        }

        // TODO: Add Advanced Transfer support, current attempts have produced undesired results.
        Msg(msg: "Advanced Transfer is not currently supported in this version of KeyNode.");
        return;

        var body = orbit.referenceBody.displayName;

        if (orbit.eccentricity >= 0.02)
        {
          Msg(msg: "Starting orbit for interplanetary transfer must not be hyperbolic. " + "Circularize first.");
          return;
        }

        if (orbit.ApR >= orbit.referenceBody.sphereOfInfluence)
        {
          Msg(msg: "Starting orbit for interplanetary transfer must not " + $"escape {body}'s SOI.");
          return;
        }

        if (!NormalTargetExists)
        {
          Msg(msg: "Target required for interplanetary transfer.");
          return;
        }

        if (orbit.referenceBody.referenceBody == null)
        {
          Msg(
              msg: $"An interplanetary transfer from within {body}'s SOI "
                 + $"must target a body that orbits {body}'s parent, "
                 + $"{orbit.referenceBody.referenceBody?.displayName}."
            );
          return;
        }

        if (target is CelestialBody
         && orbit.referenceBody == targetOrbit.referenceBody)
        {
          Msg(msg: $"Your vessel is already orbiting {body}");
          return;
        }


        if (!(target is CelestialBody))
        {
          Msg(msg: "Target must be a Celestial Body to use Advanced Transfer");
          return;
        }

        var t = _mechJeb2.assembly.GetType(name: "MuMech.OperationAdvancedTransfer");
        if (t == null) throw new NullReferenceException(message: "type null");

        _advXfer = Activator.CreateInstance(type: t); // create a seprate class than the one used in MechJeb
        // the actual transfer computation is done in void CheckPorkchop() after the class is instanced.
      }
      catch (Exception e)
      {
        Msg(msg: $"ERROR: {e.Message}");
      }
    }

    private static object _advXfer;

    private static void CheckPorkchop()
    {
      try
      {
        var c = _mechJeb2.assembly.GetType(name: "MuMech.VesselExtensions");
        if (c == null) throw new NullReferenceException(message: $"{ID} CheckPorkchop() null class type, err1");

        var mjCore = c.GetMethod(name: "GetMasterMechJeb")?.Invoke(obj: c, parameters: new object[] {vessel});
        if (mjCore == null) throw new NullReferenceException(message: $"{ID} CheckPorkchop() null mjCore, err2");

        var tc = mjCore.GetType().GetField(name: "target")?.GetValue(obj: mjCore);
        var t  = _advXfer.GetType();
        var w  = t.GetField(name: "worker", bindingAttr: BindingFlags.NonPublic | BindingFlags.IgnoreCase)?.GetValue(obj: _advXfer);
        var p  = w?.GetType().GetProperty(name: "Progress")?.GetValue(obj: null, index: null);
        if (p != null) ScreenMessages.PostScreenMessage(message: $"{ID} Calculating Transfer {p}%", duration: 0.1f, style: ScreenMessageStyle.UPPER_LEFT);

        object m;
        try
        {
          m = t.GetMethod(name: "MakeNodeImpl")?.Invoke(obj: _advXfer, parameters: new[] {orbit, UT, tc});
        }
        catch (Exception e)
        {
          if (!e.Message.EndsWith(value: "failed"))
          {
            Msg(msg: e.Message);
            return;
          }

          Msg(msg: "Advanced Transfer: computation failure!");
          _advXfer = null;
          return;
        }

        var dv = (Vector3d) m.GetType().GetField(name: "dV").GetValue(obj: null);
        var ut = (double) m.GetType().GetField(name: "UT").GetValue(obj: null);

        PlaceManeuverNode(dv: dv, ut: ut);
        _advXfer = null;
      }
      catch (Exception e)
      {
        Msg(msg: e.Message);
      }
    }

    /// <summary>
    ///   Return from Moon
    /// </summary>
    /// <returns></returns>
    private static void MechJebOperationReturnFromMoon()
    {
      if (orbit.eccentricity > 0.02)
      {
        Msg(msg: "Starting orbit for Moon Return is too hyperbolic. Circularize first.");
        return;
      }


      var body = orbit.referenceBody.referenceBody;
      if (body == null)
      {
        Msg(msg: $"{orbit.referenceBody.displayName} is not orbiting another body you could return to.");
        return;
      }


      const double lowOrbit = 20000d;
      var          alt      = body.Radius + body.atmosphereDepth + lowOrbit;
      var          args     = new object[] {orbit, UT, alt, 0d};
      var dv = (Vector3d) MuMech(
        methodPath: "OrbitalManeuverCalculator.DeltaVAndTimeForMoonReturnEjection",
        args: ref args,
        refTypeIndicies: new[] {3}
      );
      var ut = (double) args[3];

      PlaceManeuverNode(dv: dv, ut: ut);
      MapView.MapCamera.SetTarget(tgt: body.MapObject); // set view to body for fine-tuning
    }

    /// <summary>
    ///   Hohmann Transfer
    /// </summary>
    /// <returns></returns>
    private static void MechJebOperationHohmannTransfer()
    {
      if (!NormalTargetExists)
      {
        Msg(msg: "Target required for Hohmann Transfer.");
        return;
      }


      if (!InSameSOI)
      {
        Msg(msg: "Target for Hohmann Transfer must be in the same SOI.");
        return;
      }


      if (orbit.eccentricity > 0.02)
      {
        Msg(msg: "Starting orbit for Hohmann Transfer is too hyperbolic. Circularize first.");
        return;
      }


      if (targetOrbit.eccentricity > 1)
      {
        Msg(msg: "Target's orbit for Hohmann Transfer can not be hyperbolic.");
        return;
      }


      var relInc = (double) MuMech(methodPath: "OrbitExtensions.RelativeInclination", orbit, targetOrbit);

      if (relInc > 30
       && relInc < 150)
        ScreenMessages.PostScreenMessage(message: $"{ID} WARNING: Planned Hohmann Transfer might not intercept the target!");

      var args = new object[] {orbit, targetOrbit, UT, 0d};
      var dv   = (Vector3d) MuMech(methodPath: "OrbitalManeuverCalculator.DeltaVAndTimeForHohmannTransfer", args: ref args, refTypeIndicies: new[] {3});
      PlaceManeuverNode(dv: dv, ut: (double) args[3]);
      MapView.MapCamera.SetTarget(body: target as CelestialBody); // look at target for fine-tuning
    }

    /// <summary>
    ///   Intercept Course Correction
    /// </summary>
    /// <returns></returns>
    private static void MechJebOperationInterceptTarget()
    {
      if (!NormalTargetExists)
      {
        Msg(msg: "Target required for Intercept Course Correction.");
        return;
      }


      var ut              = UT;
      var o               = orbit;
      var correctionPatch = orbit;
      while (correctionPatch != null)
      {
        if (correctionPatch.referenceBody == targetOrbit.referenceBody)
        {
          o  = correctionPatch;
          ut = o.StartUT;
          break;
        }

        correctionPatch = (Orbit) MuMech(methodPath: "VesselExtensions.GetNextPatch", vessel, correctionPatch);
      }

      if (correctionPatch               == null
       || correctionPatch.referenceBody != targetOrbit.referenceBody)
      {
        Msg(msg: "Target for Intercept Course Correction must be in the same SOI.");
        return;
      }


      var aTime = (double) MuMech(methodPath: "OrbitExtensions.NextClosestApproachTime", o, targetOrbit, ut);
      var aDist = (double) MuMech(methodPath: "OrbitExtensions.NextClosestApproachDistance", o, targetOrbit, ut);

      if (aTime < ut + 1
       || aDist > targetOrbit.semiMajorAxis * 0.2)
      {
        Msg(msg: "Intercept Course Correction is currently not possible!");
        return;
      }


      const double peA = 200000, iDist = 200d;

      Vector3d     dv;
      var          targetBody = target as CelestialBody;
      const string m          = "OrbitalManeuverCalculator.DeltaVAndTimeForCheapestCourseCorrection";

      if (targetBody == null)
      {
        var args = new object[] {o, ut, targetOrbit, iDist, ut};
        dv = (Vector3d) MuMech(methodPath: m, args: ref args, refTypeIndicies: new[] {4});
        ut = (double) args[4];
      }
      else
      {
        var args = new object[] {o, ut, targetOrbit, targetBody, targetBody.Radius + peA, ut};
        dv = (Vector3d) MuMech(methodPath: m, args: ref args, refTypeIndicies: new[] {5});
        ut = (double) args[5];
      }

      PlaceManeuverNode(dv: dv, ut: ut);
    }

    #endregion

    #region --- [ convienece properties for simplicity's sake ] -----------------------------------------------------------

    private static void Msg(string msg) => ScreenMessages.PostScreenMessage(message: $"{ID} {msg}");

    // ReSharper disable InconsistentNaming
    private static double      UT                    => Planetarium.GetUniversalTime();
    private static Vessel      vessel                => FlightGlobals.ActiveVessel;
    private static Orbit       orbit                 => vessel.orbit;
    private static ITargetable target                => vessel.targetObject;
    private static Orbit       targetOrbit           => target.GetOrbit();
    private static bool        PatchedConicsUnlocked => vessel.patchedConicSolver != null;

    private static ManeuverNode currentNode => vessel.patchedConicSolver.maneuverNodes.First();
    // ReSharper enable InconsistentNaming

    private static bool CanAlignTarget => target != null && target.GetTargetingMode() == VesselTargetModes.DirectionVelocityAndOrientation;

    private static bool NormalTargetExists => target != null && (target is Vessel || target is CelestialBody || CanAlignTarget);

    private static bool InSameSOI => orbit.referenceBody == targetOrbit.referenceBody;

    #endregion
  }
}
