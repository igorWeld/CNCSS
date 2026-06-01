O9014
(SMOKE TEST: WCS + MCS + H + D)
G17 G21 G90 G40 G49 G80

(Reference return)
G91 G28 Z0
G91 G28 X0 Y0
G90

(Work offset)
G54

(Tool and length)
T1 M6
S3000 M3
G00 X0 Y0
G43 H1 Z50.

(Cutter comp left with D1)
G00 X0 Y0
G01 Z0 F200
G01 G41 D1 X20. Y0 F400
G01 X20. Y20.
G01 X0. Y20.
G01 X0. Y0.
G01 G40 X-5. Y0.

(Machine safe retract using G53)
G00 Z50.
G90 G53 Z0
M30

