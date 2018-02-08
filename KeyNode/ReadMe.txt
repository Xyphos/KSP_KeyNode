KeyNode v1.1 Copyright (C) 2018 William "Xyphos" Scott

KeyNode for Kerbal Space Program allows the player to manipulate Maneuver Nodes 
in Map View using the number pad on their keyboards, or create and execute 
Maneuver Nodes if MechJeb is also installed. (See below for MechJeb keys)

The Vessel must be in flight, with the Map View displayed, 
and time can not be in a Time Warp, in order for this mod to work.
MechJeb is not required, but highly recommended.

-------------------------------------------------------------------------------------------------------------------------------
The RIGHT ALT, RIGHT CTRL and RIGHT SHIFT keys are used to control the DELTA AMOUNT;
Holding none of these keys will default DELTA to 1 m/s.
Holding RIGHT CTRL will set DELTA to 10 m/s.
Holding RIGHT SHIFT will set DELTA to 100 m/s.
Holding both RIGHT CTRL and RIGHT SHIFT will set DELTA to 1000 m/s.

-------------------------------------------------------------------------------------------------------------------------------
However, the RIGHT ALT key is used for smaller adjustments;
Holding RIGHT ALT will set DELTA to 0.1 m/s
Holding RIGHT ALT and RIGHT CTRL will set DELTA to 0.01 m/s
Holding RIGHT ALT and RIGHT SHIFT will set DELTA to 0.001 m/s
Holding RIGHT ALT, RIGHT CTRL and RIGHT SHIFT will set DELTA to 0.0001 m/s

-------------------------------------------------------------------------------------------------------------------------------
After DELTA is set, you can use the following keys to alter your Maneuver Node:
NUMPAD 1 will subtract PROGRADE by the DELTA amount. (increases retrograde)
NUMPAD 2 will subtract NORMAL by the DELTA amount. (increase anti-normal)
NUMPAD 3 will subtract TIME by the DELTA amount, in seconds.
NUMPAD 4 will subtract RADIAL by the DELTA amount. (increase radial-in)
NUMPAD 5 is reserved, see MechJeb Integration below.
NUMPAD 6 will add RADIAL by the DELTA amount.
NUMPAD 7 will add PROGRADE by the DELTA amount.
NUMPAD 8 will add NORMAL by the DELTA amount.
NUMPAD 9 will add TIME by the DELTA amount, in seconds.

-------------------------------------------------------------------------------------------------------------------------------
Additionally, there are keys that alter your Maneuver Node's TIME by whole orbital periods;
These keys DO NOT use the DELTA modifiers.

NUMPAD PLUS will add an ORBIT to your Maneuver Node's TIME.
NUMPAD MINUS will subtract an ORBIT from your Maneuver Node's TIME, if able;
it'll never subtract past the current time.

-------------------------------------------------------------------------------------------------------------------------------
The KEYPAD MULTIPLY (asterisk, star, etc) key will Time-Warp to SOI Transition, if possible.
If your Vessel would eject from a Celestial Body or encounter another, this feature will Time-Warp to the SOI Transition.

-------------------------------------------------------------------------------------------------------------------------------
The BACKSPACE key is used to delete nodes, but RIGHT SHIFT is required as a safety precaution;
Holding RIGHT SHIFT and pressing BACKSPACE will delete the LAST Maneuver Node created.
Holding RIGHT CTRL, RIGHT SHIFT and pressing BACKSPACE will delete ALL maneuver nodes.

-------------------------------------------------------------------------------------------------------------------------------
MechJeb Integration
As of v1.0, seamless MechJeb integration was added, so common maneuvers and/or operations can be performed without 
requiring MechJeb's GUI to be visible or interacted with.

KEYPAD ENTER will cause MechJeb to execute the NEXT Maneuver Node.
If RIGHT SHIFT is held down, MechJeb will execute ALL Maneuver Nodes instead.
--> If MechJeb is already executing a Maneuver Node and KEYPAD ENTER is pressed, MechJeb will abort the Maneuver Node's execution.
    ++BugFix v1.1: Node Execution cancelation can now be performed in a time-warp/

KEYPAD 0 will create a CIRCULARIZATION Maneuver Node.
By default, The Maneuver Node will be placed at your current orbit's APOAPSIS, if possible.
If your current orbit is hyperbolic, doesn't have an apoapsis, or if RIGHT SHIFT is held down,
the Maneuver Node will be placed at your current orbit's PERIAPSIS instead.

KEYPAD PERIOD will create a MATCH TARGET VELOCITY Maneuver Node.
The Maneuver Node will be created at the closest approach distance to the target.
This is useful for orbital rendezvous prior to docking.

KEYPAD 5 will create an INTERCEPT COURSE CORRECTION Maneuver Node.
NumLock MUST be turned on!
This is useful if your orbit doesn't intercept your target closely.

KEYPAD DIVIDE (slash) will create a TRANSFER Maneuver Node.
--> Advanced Porkchop Transfers are not supported currently, but will be added in a future release.
By default, a Hohmann Transfer Maneuver Node will be created, if possible.
--> Your Vessel and Target must be in the same Sphere of Influence for Hohmann Transfer.

Holding RIGHT SHIFT and pressing KEYPAD DIVIDE will create a RETURN FROM MOON Maneuver Node, if possible.
