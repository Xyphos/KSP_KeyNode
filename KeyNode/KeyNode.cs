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

using System.Collections.Generic;
using UnityEngine;

namespace KeyNode
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class KeyNode : MonoBehaviour
    {
        internal static Vessel Vessel;
        internal static ManeuverNode Node;
        private static double _delta;

        internal static bool Ralt, Rctrl, Rshift;

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

        // lookup table for standard node operations
        private static readonly Dictionary<KeyCode, KeyHandler> KeyHandlers = new Dictionary<KeyCode, KeyHandler>
        {
            {KeyCode.Keypad1, () => Node.DeltaV.z -= _delta}, // subtract prograde
            {KeyCode.Keypad2, () => Node.DeltaV.y -= _delta}, // subtract normal
            {KeyCode.Keypad3, () => Node.UT -= _delta}, // subtract time
            {KeyCode.Keypad4, () => Node.DeltaV.x -= _delta}, // subtract radial
            // 5 reserved
            {KeyCode.Keypad6, () => Node.DeltaV.x += _delta}, // add radial
            {KeyCode.Keypad7, () => Node.DeltaV.z += _delta}, // add prograde
            {KeyCode.Keypad8, () => Node.DeltaV.y += _delta}, // add normal
            {KeyCode.Keypad9, () => Node.UT += _delta}, // add time
            // special keys
            {KeyCode.KeypadPlus, () => Node.UT += Vessel.orbit.period}, // add orbit
            {
                KeyCode.KeypadMinus, () => // subtract orbit, if able.
                {
                    var UT = Node.UT - Vessel.orbit.period;
                    if (UT > Planetarium.GetUniversalTime())
                        Node.UT = UT;
                }
            },


            // when shift is pressed, it changes the function of the numpad, so we'll work around it with these.
            {KeyCode.End, () => KeyHandlers[KeyCode.Keypad1].Invoke()},
            {KeyCode.DownArrow, () => KeyHandlers[KeyCode.Keypad2].Invoke()},
            {KeyCode.PageDown, () => KeyHandlers[KeyCode.Keypad3].Invoke()},
            {KeyCode.LeftArrow, () => KeyHandlers[KeyCode.Keypad4].Invoke()},
            // 5 reserved
            {KeyCode.RightArrow, () => KeyHandlers[KeyCode.Keypad6].Invoke()},
            {KeyCode.Home, () => KeyHandlers[KeyCode.Keypad7].Invoke()},
            {KeyCode.UpArrow, () => KeyHandlers[KeyCode.Keypad8].Invoke()},
            {KeyCode.PageUp, () => KeyHandlers[KeyCode.Keypad9].Invoke()}
        };

        public void Awake()
        {
            // This is a kludge until I can figure out how to intercept keys and block them while the map is open
            GameSettings.ZOOM_IN = new KeyBinding(); // keypad plus
            GameSettings.ZOOM_OUT = new KeyBinding(); // keypad minus
            GameSettings.SCROLL_VIEW_UP = new KeyBinding(); // page up
            GameSettings.SCROLL_VIEW_DOWN = new KeyBinding(); // page down
            GameSettings.CAMERA_ORBIT_LEFT = new KeyBinding(); // left arrow
            GameSettings.CAMERA_ORBIT_RIGHT = new KeyBinding(); // right arrow
            GameSettings.NAVBALL_TOGGLE = new KeyBinding(); // toggling navball exits mapview ...why?
            GameSettings.SCROLL_ICONS_UP = new KeyBinding(); // home
            GameSettings.SCROLL_ICONS_DOWN = new KeyBinding(); // end
            GameSettings.ApplySettings();
            GameSettings.SaveSettings();
            
            MechJebWrapper.InitMechJebWrapper();
        }

        public void Update()
        {
            if (!MapView.MapIsEnabled)
                return;

            //Debug.Log("[KeyNode] UPDATE!");
            if (!FlightGlobals.ready)
                return;

            Vessel = FlightGlobals.ActiveVessel;

            Ralt = Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.RightCommand); // mac keyboard support on right side
            Rctrl = Input.GetKey(KeyCode.RightControl);
            Rshift = Input.GetKey(KeyCode.RightShift);
            
            // combine the modifier keys into a binary-coded integer
            var i = (Rctrl ? 1 : 0) |
                    (Rshift ? 2 : 0) |
                    (Ralt ? 4 : 0);

            // assign the delta based on the modifier keys. Lookup-tables are much nicer than giant blocks of if conditions.
            _delta = ModKeyMap[i];

            if (Vessel.patchedConicSolver.maneuverNodes.Count < 1)
                return;

            Node = Vessel.patchedConicSolver.maneuverNodes[0]; // get current node

            // poll the keys and call the handlers as needed. (The handlers are also stored in a lookup table.)
            foreach (var keyHandler in KeyHandlers)
                if (Input.GetKeyUp(keyHandler.Key))
                {
                    //Debug.Log($"[KeyNode] Handler {keyHandler.Key.ToString()}");
                    keyHandler.Value();

                    if (MechJebWrapper.ready)
                        MechJebWrapper.UpdateMechJebNodeEditor();

                    Node.solver.UpdateFlightPlan();
                    if (Node.attachedGizmo == null)
                        continue;

                    Node.attachedGizmo.patchBefore = Node.patch;
                    Node.attachedGizmo.patchAhead = Node.nextPatch;

                    if (MechJebWrapper.ready)
                        MechJebWrapper.UpdateMechJebNodeEditor();
                }
        }

        private delegate void KeyHandler();
    }
}
